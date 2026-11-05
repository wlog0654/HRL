using System;
using System.Reflection;
using System.Collections.Generic;
using System.Threading;
using System.IO;
using HsReconnectLite;
class GameActionTests {
    sealed class Fake : IGameRuntime {
        public bool Running, Exists = true, RefuseExit;
        public int Closed, Launched, StartAt = 1;
        public int LauncherCloses, WindowChecks, ReadyAfter = 1;
        public bool CloseAccepted = true;
        public Action OnWindowCheck;
        public bool LauncherExitedDuringClose;
        public Action OnDelay;
        public List<int> Waits = new List<int>();
        public bool IsRunning() { return Running; }
        public bool LauncherExists(string path) { return Exists; }
        public void CloseTree() { Closed++; if (!RefuseExit) Running = false; }
        public void Launch(string path) { Launched++; if (Launched >= StartAt) Running = true; }
        public void Delay(int milliseconds, CancellationToken token) { Waits.Add(milliseconds); if (OnDelay != null) OnDelay(); token.ThrowIfCancellationRequested(); }
        public bool IsGameWindowReady() { if (OnWindowCheck != null) OnWindowCheck(); return ++WindowChecks >= ReadyAfter; }
        public bool RequestLauncherClose(string path) { LauncherCloses++; if (LauncherExitedDuringClose) throw new ArgumentException("Launcher already exited"); return CloseAccepted; }
    }
    static int failed;
    static void Check(string name, bool ok) { Console.WriteLine((ok ? "PASS " : "FAIL ") + name); if (!ok) failed++; }
    static bool Fails(Action action) { try { action(); return false; } catch (InvalidOperationException) { return true; } }
    static int Main() {
        bool invalidPathRejected = false;
        try { invalidPathRejected = !new WindowsGameRuntime().LauncherExists("invalid\0Battle.net.exe"); } catch (ArgumentException) { }
        Check("corrupted launcher path is safely rejected", invalidPathRejected);
        var f = new Fake { Running = true }; new GameActions(f).Execute(false, null, CancellationToken.None);
        Check("close only ends game tree and never launches", f.Closed == 1 && f.Launched == 0 && f.LauncherCloses == 0);
        f = new Fake(); new GameActions(f).Execute(false, null, CancellationToken.None);
        Check("missing game is a harmless no-op", f.Closed == 0 && f.Launched == 0);
        f = new Fake { Running = true, Exists = false };
        Check("missing launcher rejected before closing game", Fails(delegate { new GameActions(f).Execute(true, "missing", CancellationToken.None); }) && f.Closed == 0);
        f = new Fake { Running = true }; new GameActions(f).Execute(true, "launcher", CancellationToken.None);
        Check("restart closes then waits three seconds and launches", f.Closed == 1 && f.Launched == 1 && f.Waits[0] == 3000);
        f = new Fake(); new GameActions(f).Execute(true, "launcher", CancellationToken.None);
        Check("start when game absent skips three-second restart delay", f.Closed == 0 && !f.Waits.Contains(3000));
        Check("successful start requests launcher window close once", f.LauncherCloses == 1);
        f = new Fake { ReadyAfter = 3 }; new GameActions(f).Execute(true, "launcher", CancellationToken.None);
        Check("launcher stays until game window is ready", f.WindowChecks == 3 && f.LauncherCloses == 1);
        f = new Fake { ReadyAfter = 100 }; string pending = new GameActions(f).Execute(true, "launcher", CancellationToken.None);
        Check("window readiness timeout preserves launcher", f.WindowChecks == 20 && f.LauncherCloses == 0);
        f = new Fake { CloseAccepted = false }; string incomplete = new GameActions(f).Execute(true, "launcher", CancellationToken.None);
        Check("launcher close failure does not fail game startup", incomplete.Contains("战网") && f.Running && f.LauncherCloses == 1);
        f = new Fake { LauncherExitedDuringClose = true }; bool cleanupRecovered = false;
        try { cleanupRecovered = new GameActions(f).Execute(true, "launcher", CancellationToken.None).Contains("炉石"); } catch (ArgumentException) { }
        Check("launcher exit race does not fail successful restart", cleanupRecovered && f.Running);
        f = new Fake { Running = true, RefuseExit = true };
        Check("exit timeout stops without launching", Fails(delegate { new GameActions(f).Execute(true, "launcher", CancellationToken.None); }) && f.Launched == 0 && f.Waits.Count == 10);
        f = new Fake { StartAt = 3 }; new GameActions(f).Execute(true, "launcher", CancellationToken.None);
        Check("launch retries succeed without repeating close", f.Launched == 3 && f.Closed == 0);
        f = new Fake { StartAt = 100 };
        Check("failed startup bounded to six requests", Fails(delegate { new GameActions(f).Execute(true, "launcher", CancellationToken.None); }) && f.Launched == 6 && f.Waits.Count == 24);
        Check("failed startup preserves launcher window", f.LauncherCloses == 0);
        using (var stop = new CancellationTokenSource()) {
            f = new Fake { Running = true, OnDelay = delegate { stop.Cancel(); } };
            bool cancelled = false;
            try { new GameActions(f).Execute(true, "launcher", stop.Token); } catch (OperationCanceledException) { cancelled = true; }
            Check("exit cancels pending restart before launch", cancelled && f.Launched == 0);
        }
        using (var stop = new CancellationTokenSource()) {
            f = new Fake { OnWindowCheck = delegate { stop.Cancel(); } };
            bool cancelled = false;
            try { new GameActions(f).Execute(true, "launcher", stop.Token); } catch (OperationCanceledException) { cancelled = true; }
            Check("cancellation after window appears prevents launcher close", cancelled && f.LauncherCloses == 0);
        }
        f = new Fake { ReadyAfter = 100 }; f.OnWindowCheck = delegate { f.Running = false; };
        Check("game exiting during readiness wait preserves launcher", Fails(delegate { new GameActions(f).Execute(true, "launcher", CancellationToken.None); }) && f.LauncherCloses == 0);
        Check("launcher window policy accepts configured Chinese main window", LauncherWindowPolicy.Matches("C:\\Games\\Battle.net.exe", "C:\\Games\\Battle.net.exe", "战网", true, false));
        Check("different launcher installation rejected", !LauncherWindowPolicy.Matches("D:\\Battle.net.exe", "C:\\Games\\Battle.net.exe", "Battle.net", true, false));
        Check("other window titles rejected", !LauncherWindowPolicy.Matches("C:\\Games\\Battle.net.exe", "C:\\Games\\Battle.net.exe", "Hearthstone", true, false));
        Check("hidden and owned windows rejected", !LauncherWindowPolicy.Matches("C:\\Games\\Battle.net.exe", "C:\\Games\\Battle.net.exe", "Battle.net", false, false)
            && !LauncherWindowPolicy.Matches("C:\\Games\\Battle.net.exe", "C:\\Games\\Battle.net.exe", "Battle.net", true, true));
        string directory = Path.Combine(Path.GetTempPath(), "HrlGameActions-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "battle-net-path.txt");
        try {
            Check("launcher path missing is recoverable", LauncherPreferences.Load(path) == null);
            Check("launcher path persists with spaces", LauncherPreferences.Save(path, "C:\\Game Tools\\Battle.net.exe") && LauncherPreferences.Load(path) == "C:\\Game Tools\\Battle.net.exe");
            Check("launcher path can be atomically replaced", LauncherPreferences.Save(path, "D:\\Battle.net.exe") && LauncherPreferences.Load(path) == "D:\\Battle.net.exe");
        } finally { if (File.Exists(path)) File.Delete(path); if (Directory.Exists(directory)) Directory.Delete(directory); }
        Console.WriteLine("Failures: " + failed); return failed == 0 ? 0 : 1;
    }
}

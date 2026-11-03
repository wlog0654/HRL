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
        public Action OnDelay;
        public List<int> Waits = new List<int>();
        public bool IsRunning() { return Running; }
        public bool LauncherExists(string path) { return Exists; }
        public void CloseTree() { Closed++; if (!RefuseExit) Running = false; }
        public void Launch(string path) { Launched++; if (Launched >= StartAt) Running = true; }
        public void Delay(int milliseconds, CancellationToken token) { Waits.Add(milliseconds); if (OnDelay != null) OnDelay(); token.ThrowIfCancellationRequested(); }
    }
    static int failed;
    static void Check(string name, bool ok) { Console.WriteLine((ok ? "PASS " : "FAIL ") + name); if (!ok) failed++; }
    static bool Fails(Action action) { try { action(); return false; } catch (InvalidOperationException) { return true; } }
    static int Main() {
        bool invalidPathRejected = false;
        try { invalidPathRejected = !new WindowsGameRuntime().LauncherExists("invalid\0Battle.net.exe"); } catch (ArgumentException) { }
        Check("corrupted launcher path is safely rejected", invalidPathRejected);
        var f = new Fake { Running = true }; new GameActions(f).Execute(false, null, CancellationToken.None);
        Check("close only ends game tree and never launches", f.Closed == 1 && f.Launched == 0);
        f = new Fake(); new GameActions(f).Execute(false, null, CancellationToken.None);
        Check("missing game is a harmless no-op", f.Closed == 0 && f.Launched == 0);
        f = new Fake { Running = true, Exists = false };
        Check("missing launcher rejected before closing game", Fails(delegate { new GameActions(f).Execute(true, "missing", CancellationToken.None); }) && f.Closed == 0);
        f = new Fake { Running = true }; new GameActions(f).Execute(true, "launcher", CancellationToken.None);
        Check("restart closes then waits three seconds and launches", f.Closed == 1 && f.Launched == 1 && f.Waits[0] == 3000);
        f = new Fake(); new GameActions(f).Execute(true, "launcher", CancellationToken.None);
        Check("start when game absent skips three-second restart delay", f.Closed == 0 && !f.Waits.Contains(3000));
        f = new Fake { Running = true, RefuseExit = true };
        Check("exit timeout stops without launching", Fails(delegate { new GameActions(f).Execute(true, "launcher", CancellationToken.None); }) && f.Launched == 0 && f.Waits.Count == 10);
        f = new Fake { StartAt = 3 }; new GameActions(f).Execute(true, "launcher", CancellationToken.None);
        Check("launch retries succeed without repeating close", f.Launched == 3 && f.Closed == 0);
        f = new Fake { StartAt = 100 };
        Check("failed startup bounded to six requests", Fails(delegate { new GameActions(f).Execute(true, "launcher", CancellationToken.None); }) && f.Launched == 6 && f.Waits.Count == 24);
        using (var stop = new CancellationTokenSource()) {
            f = new Fake { Running = true, OnDelay = delegate { stop.Cancel(); } };
            bool cancelled = false;
            try { new GameActions(f).Execute(true, "launcher", stop.Token); } catch (OperationCanceledException) { cancelled = true; }
            Check("exit cancels pending restart before launch", cancelled && f.Launched == 0);
        }
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

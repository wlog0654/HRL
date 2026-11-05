using System;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Diagnostics;
using System.Runtime.InteropServices;
using HsReconnectLite;
class OverwatchTests {
    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    sealed class FakeProcesses : IGameProcessControl {
        public Dictionary<int, string[]> Items = new Dictionary<int, string[]>();
        public List<int> Killed = new List<int>();
        public bool ExitOnMatch;
        public int[] Find(string name) { return Items.Where(p => p.Value[0] == name).Select(p => p.Key).ToArray(); }
        public bool Matches(int pid, string expected) { if (ExitOnMatch) { Items.Remove(pid); return false; } return Items.ContainsKey(pid) && String.Equals(Items[pid][1], expected, StringComparison.OrdinalIgnoreCase); }
        public void Terminate(int pid, string expected) { if (!Matches(pid, expected)) throw new InvalidOperationException(); Killed.Add(pid); Items.Remove(pid); }
        public void Add(int pid, string name, string path) { Items[pid] = new[] { name, path }; }
    }
    static int failures;
    static void Check(string name, bool ok) { Console.WriteLine((ok ? "PASS " : "FAIL ") + name); if (!ok) failures++; }
    static int Main(string[] args) {
        if (args.Length > 0 && args[0] == "--child") { Console.ReadLine(); return 0; }
        var p = new FakeProcesses(); string root = "C:\\Games\\Overwatch\\_retail_\\";
        p.Add(1, "Overwatch", root + "Overwatch.exe");
        p.Add(2, "OWNeacClient", root + "OWNeacClient.exe");
        p.Add(3, "NeacLoader", root + "NeacLoader.exe");
        var runtime = new OverwatchRuntime(root + "Overwatch.exe", p);
        bool refused = false;
        try { runtime.CleanupAfterExit(CancellationToken.None); } catch (InvalidOperationException) { refused = true; }
        Check("NEAC is never cleaned while OW is running", refused && p.Killed.Count == 0);
        runtime.CloseTree();
        Check("close first targets only the verified OW executable", p.Killed.SequenceEqual(new[] { 1 }) && p.Items.ContainsKey(2));
        runtime.CleanupAfterExit(CancellationToken.None);
        Check("after exit clean OWNeacClient then NeacLoader", p.Killed.SequenceEqual(new[] { 1, 2, 3 }));
        runtime.CleanupAfterExit(CancellationToken.None);
        Check("cleanup is harmless when companions are already gone", p.Killed.Count == 3);
        p.Add(4, "OWNeacClient", "D:\\OtherGame\\OWNeacClient.exe");
        runtime.CleanupAfterExit(CancellationToken.None);
        Check("same-name process in another installation is preserved", p.Items.ContainsKey(4));
        p.Add(5, "Overwatch", "D:\\OtherGame\\Overwatch.exe");
        refused = false;
        try { runtime.CloseTree(); } catch (InvalidOperationException) { refused = true; }
        Check("foreign OW installation cannot be closed", refused && p.Items.ContainsKey(5));
        var disappearing = new FakeProcesses { ExitOnMatch = true }; disappearing.Add(9, "Overwatch", root + "Overwatch.exe");
        bool exitedNaturally = false;
        try { new OverwatchRuntime(root + "Overwatch.exe", disappearing).CloseTree(); exitedNaturally = true; } catch (InvalidOperationException) { }
        Check("OW exiting during path check is not reported as foreign installation", exitedNaturally && disappearing.Killed.Count == 0);
        p.Items.Remove(5); p.Add(6, "OWNeacClient", root + "OWNeacClient.exe");
        using (var cancellation = new CancellationTokenSource()) {
            cancellation.Cancel(); bool cancelled = false;
            try { runtime.CleanupAfterExit(cancellation.Token); } catch (OperationCanceledException) { cancelled = true; }
            Check("cancellation preserves pending cleanup target", cancelled && p.Items.ContainsKey(6));
        }
        using (var child = Process.Start(new ProcessStartInfo { FileName = Assembly.GetExecutingAssembly().Location, Arguments = "--child", UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true })) {
            IntPtr handle = OpenProcess(0x1001, false, (uint)child.Id);
            try {
                bool refusedWrongPath = false;
                try { new GameProcessControl().Terminate(child.Id, "C:\\NotTheTest\\Other.exe"); } catch (InvalidOperationException) { refusedWrongPath = true; }
                Check("real process with changed identity is not terminated", refusedWrongPath && !child.HasExited);
                child.StandardInput.WriteLine("exit"); child.StandardInput.Flush();
                if (!child.WaitForExit(5000)) throw new Exception("Own test child did not exit");
                var terminateHandle = typeof(GameProcessControl).GetMethod("TerminateHandle", BindingFlags.Static | BindingFlags.NonPublic);
                bool handledExit = false;
                if (handle != IntPtr.Zero && terminateHandle != null) {
                    try { terminateHandle.Invoke(null, new object[] { handle, Assembly.GetExecutingAssembly().Location }); handledExit = true; } catch (TargetInvocationException) { }
                }
                Check("already exited process on the same handle is harmless", handledExit);
            } finally { if (handle != IntPtr.Zero) CloseHandle(handle); }
        }
        Console.WriteLine("Failures: " + failures); return failures == 0 ? 0 : 1;
    }
}

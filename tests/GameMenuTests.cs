using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using HsReconnectLite;

class GameMenuTests {
    sealed class Fake : IGameRuntime, IGameExitCleanup {
        public bool Running = true;
        public int Closed, Launched;
        public int Cleanups;
        public bool NeverStarts, WaitForCancel;
        public ManualResetEvent Release = new ManualResetEvent(false);
        public bool IsRunning() { return Running; }
        public bool LauncherExists(string path) { return true; }
        public void CloseTree() { Interlocked.Increment(ref Closed); Release.WaitOne(); Running = false; }
        public void Launch(string path) { Launched++; if (!NeverStarts) Running = true; }
        public void Delay(int milliseconds, CancellationToken cancellation) { if (WaitForCancel) cancellation.WaitHandle.WaitOne(); cancellation.ThrowIfCancellationRequested(); }
        public bool IsGameWindowReady() { return true; }
        public bool RequestLauncherClose(string path) { return true; }
        public void CleanupAfterExit(CancellationToken token) { Cleanups++; }
    }
    static int failed;
    static void Check(string name, bool ok) { Console.WriteLine((ok ? "PASS " : "FAIL ") + name); if (!ok) failed++; }
    static object Field(object owner, string name) { return owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(owner); }
    static void Pump(int ms) { var clock = Stopwatch.StartNew(); while (clock.ElapsedMilliseconds < ms) { Application.DoEvents(); Thread.Sleep(5); } }
    [STAThread]
    static int Main() {
        Application.EnableVisualStyles(); Control.CheckForIllegalCrossThreadCalls = true;
        var runtime = new Fake(); int disconnects = 0;
        var ow = new Fake(); ow.Release.Set();
        using (var context = new TrayContext(true, false, delegate { disconnects++; }, runtime, ow)) {
            ((System.Windows.Forms.Timer)Field(context, "timer")).Stop();
            var menu = (ContextMenuStrip)Field(context, "menu");
            var close = menu.Items.Cast<ToolStripItem>().Single(i => i.Text == "关闭炉石");
            var restart = menu.Items.Cast<ToolStripItem>().Single(i => i.Text == "重启炉石");
            var startOw = menu.Items.Cast<ToolStripItem>().Single(i => i.Text == "关闭ow");
            var restartOw = menu.Items.Cast<ToolStripItem>().Single(i => i.Text == "重启ow");
            close.PerformClick(); Pump(150);
            Check("close menu invokes only game action", runtime.Closed == 1 && disconnects == 0 && runtime.Launched == 0);
            Check("all destructive actions disabled while pending", !close.Enabled && !restart.Enabled && !menu.Items[0].Enabled && !startOw.Enabled && !restartOw.Enabled);
            typeof(TrayContext).GetMethod("Reconnect", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(context, null);
            Check("hotkey cannot disconnect during close", disconnects == 0);
            runtime.Release.Set(); Pump(200);
            Check("menus restored after background completion", close.Enabled && restart.Enabled && menu.Items[0].Enabled);
            Pump(1900); restart.PerformClick(); Pump(200);
            Check("restart menu uses launcher action without disconnect", runtime.Launched == 1 && disconnects == 0);
            Pump(1900); startOw.PerformClick(); Pump(200);
            Check("OW close menu closes game and cleans NEAC without launching", ow.Closed == 1 && ow.Cleanups == 1 && ow.Launched == 0);
            Pump(1900); restartOw.PerformClick(); Pump(200);
            Check("OW restart menu launches after close and NEAC cleanup", ow.Closed == 1 && ow.Cleanups == 2 && ow.Launched == 1 && runtime.Closed == 1 && disconnects == 0);
            Pump(1900); ow.Running = false; ow.NeverStarts = true; ow.WaitForCancel = true;
            restartOw.PerformClick(); Pump(150);
            var cancel = menu.Items.Cast<ToolStripItem>().Single(i => i.Text == "取消等待");
            Check("cancel action stays available during stalled startup", cancel.Available && cancel.Enabled && !restartOw.Enabled);
            cancel.PerformClick(); Pump(250);
            Check("cancelling wait restores game actions and hides cancel", restartOw.Enabled && close.Enabled && startOw.Enabled && !cancel.Available);
            context.ExitThread();
        }
        runtime.Release.Dispose();
        ow.Release.Dispose();
        Console.WriteLine("Failures: " + failed); return failed == 0 ? 0 : 1;
    }
}

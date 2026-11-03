using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using HsReconnectLite;

class GameMenuTests {
    sealed class Fake : IGameRuntime {
        public bool Running = true;
        public int Closed, Launched;
        public ManualResetEvent Release = new ManualResetEvent(false);
        public bool IsRunning() { return Running; }
        public bool LauncherExists(string path) { return true; }
        public void CloseTree() { Interlocked.Increment(ref Closed); Release.WaitOne(); Running = false; }
        public void Launch(string path) { Launched++; Running = true; }
        public void Delay(int milliseconds, CancellationToken cancellation) { cancellation.ThrowIfCancellationRequested(); }
    }
    static int failed;
    static void Check(string name, bool ok) { Console.WriteLine((ok ? "PASS " : "FAIL ") + name); if (!ok) failed++; }
    static object Field(object owner, string name) { return owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(owner); }
    static void Pump(int ms) { var clock = Stopwatch.StartNew(); while (clock.ElapsedMilliseconds < ms) { Application.DoEvents(); Thread.Sleep(5); } }
    [STAThread]
    static int Main() {
        Application.EnableVisualStyles(); Control.CheckForIllegalCrossThreadCalls = true;
        var runtime = new Fake(); int disconnects = 0;
        using (var context = new TrayContext(true, false, delegate { disconnects++; }, runtime)) {
            ((System.Windows.Forms.Timer)Field(context, "timer")).Stop();
            var menu = (ContextMenuStrip)Field(context, "menu");
            var close = menu.Items.Cast<ToolStripItem>().Single(i => i.Text == "只关闭炉石（强制）");
            var restart = menu.Items.Cast<ToolStripItem>().Single(i => i.Text == "重启炉石");
            close.PerformClick(); Pump(150);
            Check("close menu invokes only game action", runtime.Closed == 1 && disconnects == 0 && runtime.Launched == 0);
            Check("all destructive actions disabled while pending", !close.Enabled && !restart.Enabled && !menu.Items[0].Enabled);
            typeof(TrayContext).GetMethod("Reconnect", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(context, null);
            Check("hotkey cannot disconnect during close", disconnects == 0);
            runtime.Release.Set(); Pump(200);
            Check("menus restored after background completion", close.Enabled && restart.Enabled && menu.Items[0].Enabled);
            Pump(1900); restart.PerformClick(); Pump(200);
            Check("restart menu uses launcher action without disconnect", runtime.Launched == 1 && disconnects == 0);
            context.ExitThread();
        }
        runtime.Release.Dispose();
        Console.WriteLine("Failures: " + failed); return failed == 0 ? 0 : 1;
    }
}

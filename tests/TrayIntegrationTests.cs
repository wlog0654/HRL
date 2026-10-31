using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using HsReconnectLite;

class TrayIntegrationTests {
    static int failed;
    static void Check(string name, bool ok) { Console.WriteLine((ok ? "PASS " : "FAIL ") + name); if (!ok) failed++; }
    static object Field(object owner, string name) { return owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(owner); }
    static void Pump(int millis) { var watch = Stopwatch.StartNew(); while (watch.ElapsedMilliseconds < millis) { Application.DoEvents(); Thread.Sleep(5); } }
    static void Trigger(TrayContext context) { typeof(TrayContext).GetMethod("Reconnect", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(context, null); }
    [STAThread]
    static int Main() {
        Application.EnableVisualStyles(); Control.CheckForIllegalCrossThreadCalls = true;
        Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs args) {
            failed++; Console.WriteLine("FAIL unhandled UI exception: " + args.Exception.GetType().Name + " " + args.Exception.Message);
        };
        int count = 0; bool fail = false; int mainThread = Thread.CurrentThread.ManagedThreadId, workerThread = mainThread;
        int crossThreadFailures = 0;
        AppDomain.CurrentDomain.FirstChanceException += delegate(object sender, System.Runtime.ExceptionServices.FirstChanceExceptionEventArgs args) {
            if (Thread.CurrentThread.ManagedThreadId != mainThread && args.Exception.StackTrace != null && args.Exception.StackTrace.Contains("System.Windows.Forms.Control.get_Handle"))
                Interlocked.Increment(ref crossThreadFailures);
        };
        using (var context = new TrayContext(true, false, delegate {
            workerThread = Thread.CurrentThread.ManagedThreadId; Interlocked.Increment(ref count); Thread.Sleep(70);
            if (fail) throw new InvalidOperationException("Simulated disconnect error; no real network action.");
        })) {
            ((System.Windows.Forms.Timer)Field(context, "timer")).Stop();
            var overlay = (FloatingButton)Field(context, "overlay");
            var binding = HotkeyBinding.FromKeys(Keys.Control | Keys.Alt | Keys.Shift | Keys.F24);
            var changeError = (string)typeof(TrayContext).GetMethod("ApplyHotkey", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(context, new object[] { binding });
            var menu = (ContextMenuStrip)Field(context, "menu");
            var tray = (NotifyIcon)Field(context, "tray");
            Check("hotkey update refreshes menu and tray text", changeError == null && menu.Items[0].Text.Contains(binding.ToString()) && tray.Text.Contains(binding.ToString()));
            Trigger(context); Trigger(context); Pump(200);
            Check("rapid repeated requests invoke operation once", count == 1);
            Check("network work runs off the UI thread", workerThread != mainThread);
            Check("success feedback returns to UI thread", (string)Field(overlay, "label") == "已拔");
            Trigger(context); Pump(50); Check("shared two-second cooldown rejects another source", count == 1);
            Pump(1900); fail = true; Trigger(context); Pump(200);
            Check("failed operation is reported without crashing UI", count == 2 && (string)Field(overlay, "label") == "失败");
            Check("error path never accesses window from worker thread", crossThreadFailures == 0);
            var toggle = (ToolStripMenuItem)Field(context, "overlayToggle");
            toggle.PerformClick(); Check("tray menu hides overlay", !overlay.Visible && !toggle.Checked);
            toggle.PerformClick(); Check("tray menu restores overlay", overlay.Visible && toggle.Checked);
            context.ExitThread(); Check("exit disposes floating window", overlay.IsDisposed);
        }
        using (var finished = new ManualResetEvent(false)) {
            var closing = new TrayContext(true, false, delegate { finished.WaitOne(); });
            ((System.Windows.Forms.Timer)Field(closing, "timer")).Stop();
            Trigger(closing); closing.ExitThread(); finished.Set(); Pump(150); closing.Dispose();
            Check("exit while background operation runs is safe", crossThreadFailures == 0);
        }
        Console.WriteLine("Failures: " + failed); return failed == 0 ? 0 : 1;
    }
}

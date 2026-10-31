using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using HsReconnectLite;

class HotkeyCaptureTests {
    [DllImport("user32.dll")] static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, IntPtr processId);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] static extern bool AttachThreadInput(uint first, uint second, bool attach);
    static int failed;
    static void Check(string name, bool ok) { Console.WriteLine((ok ? "PASS " : "FAIL ") + name); if (!ok) failed++; }
    static void Pump(int milliseconds) {
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < milliseconds) { Application.DoEvents(); Thread.Sleep(5); }
    }
    static void Foreground(Form form) {
        // A closing form can post a later activation change. Let that settle and retry
        // rather than injecting input while another application owns the foreground.
        Pump(150);
        var watch = Stopwatch.StartNew();
        do {
            uint foregroundThread = GetWindowThreadProcessId(GetForegroundWindow(), IntPtr.Zero), currentThread = GetCurrentThreadId();
            bool attached = foregroundThread != 0 && foregroundThread != currentThread && AttachThreadInput(currentThread, foregroundThread, true);
            try { form.Activate(); SetForegroundWindow(form.Handle); }
            finally { if (attached) AttachThreadInput(currentThread, foregroundThread, false); }
            Pump(150);
            if (GetForegroundWindow() == form.Handle) break;
        } while (watch.ElapsedMilliseconds < 3000);
        bool focused = GetForegroundWindow() == form.Handle;
        Check("test window owns foreground", focused);
        if (!focused) throw new InvalidOperationException("Cannot acquire test window foreground; refusing to send keyboard input.");
    }
    static void PressBinding() {
        // Send through the Windows input pipeline, including RegisterHotKey and keyboard hooks.
        // Avoid common shortcuts so the test cannot affect unrelated applications.
        byte[] keys = { 0x11, 0x12, 0x10, 0x86 }; // Ctrl+Alt+Shift+F23
        try { foreach (byte key in keys) keybd_event(key, 0, 0, UIntPtr.Zero); }
        finally { for (int i = keys.Length - 1; i >= 0; i--) keybd_event(keys[i], 0, 2, UIntPtr.Zero); }
        Pump(250);
    }
    [STAThread]
    static int Main() {
        DesktopUi.InitializeDpi(); Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        int captured = 0, actions = 0; string error;
        var binding = HotkeyBinding.FromKeys(Keys.Control | Keys.Alt | Keys.Shift | Keys.F23);
        using (var owner = new HotkeyRegistration(delegate { actions++; }))
        using (var host = new Form { Text = "Hotkey capture test", Width = 300, Height = 160 })
        using (var dialog = new HotkeyDialog(HotkeyBinding.Default, delegate(HotkeyBinding candidate) {
            Check("real input captures the complete occupied binding", candidate.SameAs(binding));
            captured++; return "Test conflict: original owner must keep this binding.";
        })) {
            bool registered = owner.TryChange(binding, null, out error);
            Check("test owner registers occupied shortcut", registered);
            if (!registered) { Console.WriteLine(error); return 1; }
            host.Show(); Foreground(host); dialog.Show(); Foreground(dialog);
            PressBinding();
            Check("occupied shortcut reaches capture dialog exactly once", captured == 1);
            Check("capture suppresses the original hotkey action", actions == 0);

            Foreground(host); PressBinding();
            Check("deactivated dialog leaves original hotkey working", actions == 1 && captured == 1);

            Foreground(dialog); PressBinding();
            Check("reactivated dialog resumes exclusive capture", captured == 2 && actions == 1);

            dialog.Close(); Foreground(host); PressBinding();
            Check("closing capture releases interception and preserves original registration", actions == 2 && captured == 2);
            host.Close();
        }
        Console.WriteLine("Failures: " + failed); return failed == 0 ? 0 : 1;
    }
}

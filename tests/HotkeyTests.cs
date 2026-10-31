using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using HsReconnectLite;

class HotkeyTests {
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wparam, IntPtr lparam);
    static int failed;
    static void Check(string name, bool ok) { Console.WriteLine((ok ? "PASS " : "FAIL ") + name); if (!ok) failed++; }
    [STAThread]
    static int Main() {
        DesktopUi.InitializeDpi(); Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        Type type = Assembly.GetExecutingAssembly().GetType("HsReconnectLite.HotkeyBinding");
        Check("custom hotkey feature exists", type != null);
        if (type == null) return 1;
        return Run();
    }
    static int Run() {
        Check("letters require modifiers", !HotkeyBinding.FromKeys(Keys.R).IsValid);
        Check("pure modifier and escape rejected", !HotkeyBinding.FromKeys(Keys.Control | Keys.ControlKey).IsValid && !HotkeyBinding.FromKeys(Keys.Control | Keys.Escape).IsValid);
        Check("function keys allowed without modifier", HotkeyBinding.FromKeys(Keys.F9).IsValid);
        Check("debugger reserved F12 rejected", !HotkeyBinding.FromKeys(Keys.F12).IsValid);
        Check("combination captured and formatted", HotkeyBinding.FromKeys(Keys.Control | Keys.Alt | Keys.D7).ToString() == "Ctrl+Alt+7");
        Check("unknown modifiers rejected", !new HotkeyBinding(Keys.R, 0x8000).IsValid);
        Check("modified key code rejected", !new HotkeyBinding(Keys.Control | Keys.R, 2).IsValid);
        var first = HotkeyBinding.FromKeys(Keys.Control | Keys.Alt | Keys.Shift | Keys.F22);
        var second = HotkeyBinding.FromKeys(Keys.Control | Keys.Alt | Keys.Shift | Keys.F23);
        int actions = 0; string error;
        using (var owner = new HotkeyRegistration(delegate { actions++; }))
        using (var other = new HotkeyRegistration(delegate { })) {
            Check("initial binding registered", owner.TryChange(first, null, out error));
            Check("real Windows conflict preserves other owner", !other.TryChange(first, null, out error) && other.Current == null);
            Check("saving failure preserves current binding", !owner.TryChange(second, delegate { return false; }, out error) && owner.Current.SameAs(first));
            Check("failed candidate released", other.TryChange(second, null, out error));
            Check("candidate conflict preserves old key", !owner.TryChange(second, null, out error) && owner.Current.SameAs(first));
            SendMessage(owner.Handle, 0x312, new IntPtr(1), new IntPtr(((long)first.Key << 16) | first.Modifiers));
            Check("registered binding dispatches callback", actions == 1);
            owner.Suppressed = true;
            SendMessage(owner.Handle, 0x312, new IntPtr(1), new IntPtr(((long)first.Key << 16) | first.Modifiers));
            Check("capture suppression blocks action", actions == 1);
            owner.Suppressed = false;
            other.Dispose();
            Check("switch succeeds after conflict released", owner.TryChange(second, delegate { return true; }, out error));
            using (var third = new HotkeyRegistration(delegate { })) Check("successful change releases previous binding", third.TryChange(first, null, out error));
            SendMessage(owner.Handle, 0x312, new IntPtr(1), new IntPtr(((long)first.Key << 16) | first.Modifiers));
            Check("stale hotkey message ignored", actions == 1);
            SendMessage(owner.Handle, 0x312, new IntPtr(2), new IntPtr(((long)second.Key << 16) | second.Modifiers));
            Check("new binding dispatches callback", actions == 2);
        }
        using (var released = new HotkeyRegistration(delegate { })) Check("dispose releases active binding", released.TryChange(second, null, out error));
        string directory = Path.Combine(Path.GetTempPath(), "HsHotkey-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "settings.ini");
        try {
            File.WriteAllText(path, "enabled=0\nx=-100\ny=40\n");
            var prefs = OverlayPreferences.Load(path);
            Check("legacy settings migrate without losing position", prefs.Hotkey.SameAs(HotkeyBinding.Default) && !prefs.Enabled && prefs.Position.Value.X == -100);
            prefs.Hotkey = second;
            Check("custom binding survives restart", prefs.Save(path) && OverlayPreferences.Load(path).Hotkey.SameAs(second));
            File.WriteAllText(path, "hotkey=999999\nmodifiers=2\n");
            Check("invalid stored binding falls back to default", OverlayPreferences.Load(path).Hotkey.SameAs(HotkeyBinding.Default));
            File.WriteAllText(path, "hotkey=82\nmodifiers=4294967295\n");
            Check("invalid stored modifiers fall back to default", OverlayPreferences.Load(path).Hotkey.SameAs(HotkeyBinding.Default));
        } finally { File.Delete(path); Directory.Delete(directory); }
        int captured = 0;
        using (var dialog = new HotkeyDialog(HotkeyBinding.Default, delegate(HotkeyBinding binding) { if (binding.SameAs(second)) captured++; return null; })) {
            bool preview = Array.IndexOf(Environment.GetCommandLineArgs(), "--preview") >= 0;
            if (preview) { dialog.Show(); Application.DoEvents(); }
            typeof(HotkeyDialog).GetMethod("Record", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(dialog, new object[] { Keys.Control | Keys.Alt | Keys.Shift | Keys.F23 });
            Check("dialog records captured combination without text input", captured == 1);
            if (preview) {
                Application.DoEvents();
                using (var bitmap = new System.Drawing.Bitmap(dialog.Width, dialog.Height)) {
                    dialog.DrawToBitmap(bitmap, new System.Drawing.Rectangle(System.Drawing.Point.Empty, bitmap.Size));
                    bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "hotkey-preview.png"));
                }
                dialog.Close();
            }
        }
        Console.WriteLine("Failures: " + failed);
        return failed == 0 ? 0 : 1;
    }
}

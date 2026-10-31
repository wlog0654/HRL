using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using HsReconnectLite;

class DesktopUiTests {
    [DllImport("user32.dll")] static extern int GetMenuItemCount(IntPtr menu);
    [DllImport("user32.dll")] static extern uint GetMenuState(IntPtr menu, uint item, uint flags);
    [DllImport("user32.dll")] static extern uint GetGuiResources(IntPtr process, uint type);
    static int failed;
    static void Check(string name, bool ok) { Console.WriteLine((ok ? "PASS " : "FAIL ") + name); if (!ok) failed++; }
    [STAThread]
    static int Main() {
        DesktopUi.InitializeDpi();
        Check("100 percent uses logical size", DesktopUi.Pixels(76, 96) == 76);
        Check("150 percent redraws at native pixel size", DesktopUi.Pixels(76, 144) == 114);
        Check("200 percent redraws at native pixel size", DesktopUi.Pixels(40, 192) == 80);
        foreach (int size in new[] { 16, 20, 24, 32, 48, 64, 128, 256 }) {
            using (var icon = Branding.LoadIcon(size)) Check("embedded icon frame " + size, icon.Width == size && icon.Height == size);
        }
        using (var model = new ContextMenuStrip()) {
            int count = 0;
            model.Items.Add("Action", null, delegate { count++; });
            var toggle = new ToolStripMenuItem("Visible") { Checked = true };
            toggle.Click += delegate { count++; }; model.Items.Add(toggle);
            model.Items.Add(new ToolStripMenuItem("Disabled") { Enabled = false });
            model.Items.Add(new ToolStripSeparator());
            model.Items.Add(new ToolStripMenuItem("Hidden") { Visible = false });
            using (var native = new NativeMenuSession(model)) {
                Check("native menu has visible entries and separator", native.Handle != IntPtr.Zero && GetMenuItemCount(native.Handle) == 4);
                Check("checked state copied to native menu", native.Handle != IntPtr.Zero && (GetMenuState(native.Handle, 1, 0x400) & 8) != 0);
                Check("disabled state copied to native menu", native.Handle != IntPtr.Zero && (GetMenuState(native.Handle, 2, 0x400) & 3) != 0);
                Check("cancel performs no action", !native.Execute(0) && count == 0);
                Check("unknown command performs no action", !native.Execute(999) && count == 0);
                Check("disabled command cannot execute", !native.Execute(3) && count == 0);
                Check("selected command executes exactly once", native.Execute(2) && count == 1);
            }
            uint before = GetGuiResources(Process.GetCurrentProcess().Handle, 0);
            for (int i = 0; i < 100; i++) using (var native = new NativeMenuSession(model)) { }
            uint after = GetGuiResources(Process.GetCurrentProcess().Handle, 0);
            Check("repeated native menu creation releases USER resources", after <= before + 1);
            uint gdiBefore = GetGuiResources(Process.GetCurrentProcess().Handle, 1);
            for (int i = 0; i < 100; i++) using (var icon = Branding.LoadIcon(24)) using (var bitmap = icon.ToBitmap()) { }
            uint gdiAfter = GetGuiResources(Process.GetCurrentProcess().Handle, 1);
            Check("repeated icon decoding releases GDI resources", gdiAfter <= gdiBefore + 2);
        }
        Console.WriteLine("Failures: " + failed); return failed == 0 ? 0 : 1;
    }
}

using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using HsReconnectLite;

class MenuVisualTests {
    [StructLayout(LayoutKind.Sequential)] struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string className, string title);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll")] static extern bool EndMenu();
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr window, System.Text.StringBuilder text, int size);
    [STAThread]
    static int Main() {
        DesktopUi.InitializeDpi(); Application.EnableVisualStyles();
        bool captured = false; int actions = 0, attempts = 0;
        using (var model = new ContextMenuStrip()) using (var timer = new Timer { Interval = 200 }) {
            model.Items.Add("拔线一次\tCtrl+Shift+R", null, delegate { actions++; });
            model.Items.Add(new ToolStripMenuItem("显示悬浮按钮") { Checked = true });
            model.Items.Add("保存连接诊断", null, delegate { actions++; });
            model.Items.Add(new ToolStripSeparator());
            model.Items.Add("退出", null, delegate { actions++; });
            timer.Tick += delegate {
                IntPtr menu = FindWindow("#32768", null); uint process;
                GetWindowThreadProcessId(menu, out process);
                Rect rect;
                if (process == Process.GetCurrentProcess().Id && GetWindowRect(menu, out rect) && rect.Right > rect.Left) {
                    using (var bitmap = new Bitmap(rect.Right - rect.Left, rect.Bottom - rect.Top)) {
                        using (var graphics = Graphics.FromImage(bitmap)) graphics.CopyFromScreen(new Point(rect.Left, rect.Top), Point.Empty, bitmap.Size);
                        bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "native-menu-preview.png"), ImageFormat.Png);
                    }
                    captured = true; timer.Stop(); EndMenu();
                } else if (++attempts > 10) { timer.Stop(); EndMenu(); }
            };
            IntPtr previousWindow = GetForegroundWindow();
            timer.Start(); NativeContextMenu.Show(model, new Point(300, 250)); timer.Stop();
            IntPtr currentWindow = GetForegroundWindow();
            var currentTitle = new System.Text.StringBuilder(256); GetWindowText(currentWindow, currentTitle, currentTitle.Capacity);
            // Another application may take focus during the modal test. Never force focus back over it.
            bool restored = currentTitle.ToString() != "HsReconnectLite.Menu";
            Console.WriteLine((captured ? "PASS" : "FAIL") + " live native menu shown and captured");
            Console.WriteLine((actions == 0 ? "PASS" : "FAIL") + " native menu cancellation performs no action");
            Console.WriteLine((restored ? "PASS" : "FAIL") + " native menu leaves no invisible foreground owner (previous restored=" + (currentWindow == previousWindow) + ")");
            return captured && actions == 0 && restored ? 0 : 1;
        }
    }
}

using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using HsReconnectLite;

class OverlayWindowTests {
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    static int failed;
    static void Check(string name, bool value) { Console.WriteLine((value ? "PASS " : "FAIL ") + name); if (!value) failed++; }
    static void Mouse(FloatingButton form, string method, MouseButtons button, int x, int y) {
        typeof(FloatingButton).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(form, new object[] { new MouseEventArgs(button, 1, x, y, 0) });
    }
    static void Click(FloatingButton form) { Mouse(form, "OnMouseDown", MouseButtons.Left, 20, 15); Mouse(form, "OnMouseUp", MouseButtons.Left, 20, 15); }
    static void SimulateDpiChange(FloatingButton form, int dpi) {
        IntPtr rectangle = Marshal.AllocHGlobal(16);
        try {
            Marshal.WriteInt32(rectangle, 0, form.Left); Marshal.WriteInt32(rectangle, 4, form.Top);
            Marshal.WriteInt32(rectangle, 8, form.Left + DesktopUi.Pixels(76, dpi));
            Marshal.WriteInt32(rectangle, 12, form.Top + DesktopUi.Pixels(40, dpi));
            var message = Message.Create(form.Handle, 0x02E0, new IntPtr(dpi | (dpi << 16)), rectangle);
            typeof(FloatingButton).GetMethod("WndProc", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(form, new object[] { message });
        } finally { Marshal.FreeHGlobal(rectangle); }
    }
    [STAThread]
    static int Main() {
        SetProcessDPIAware(); Application.EnableVisualStyles();
        int clicks = 0, moved = 0;
        using (var menu = new ContextMenuStrip())
        using (var form = new FloatingButton(delegate { clicks++; }, delegate { moved++; }, menu, new Point(200, 200))) {
            form.Show(); Application.DoEvents();
            Check("overlay does not activate itself", GetForegroundWindow() != form.Handle);
            Check("overlay is small, topmost and translucent", form.TopMost && !form.ShowInTaskbar && form.Width <= 200 && form.Height <= 110 && form.Opacity > .5 && form.Opacity < 1);
            Click(form);
            Check("one complete click invokes harmless callback once", clicks == 1);
            Mouse(form, "OnMouseDown", MouseButtons.Left, 20, 15);
            Mouse(form, "OnMouseMove", MouseButtons.Left, 55, 25);
            Mouse(form, "OnMouseUp", MouseButtons.Left, 20, 15);
            Check("actual form drag moves without invoking callback", clicks == 1 && moved == 1 && form.Location != new Point(200, 200));
            form.SetFeedback("稍候", true, false); Click(form);
            Check("processing button rejects repeat clicks", clicks == 1);
            form.SetFeedback("已拔", false, false); Click(form);
            Check("button accepts clicks again after processing", clicks == 2);
            Mouse(form, "OnMouseDown", MouseButtons.Right, 20, 15);
            Check("right mouse button never invokes callback", clicks == 2);
            form.Hide(); Check("manually hidden without disposal", !form.Visible && !form.IsDisposed);
            form.Show(); Check("hidden overlay can be shown again", form.Visible);
            int originalDpi = DesktopUi.WindowDpi(form.Handle, 96);
            SimulateDpiChange(form, 192);
            using (var snapshot = form.CreateSnapshot()) {
                Check("DPI event resizes overlay at native density", form.ClientSize == new Size(152, 80) && snapshot.HorizontalResolution == 192);
            }
            SimulateDpiChange(form, 96);
            Check("moving back to 100 percent restores size", form.ClientSize == new Size(76, 40));
            SimulateDpiChange(form, originalDpi);
            string imagePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "floating-button-preview.png");
            using (var image = new Bitmap(form.Width * 3, form.Height + 10)) {
                using (var background = Graphics.FromImage(image)) background.Clear(Color.FromArgb(245, 245, 245));
                string[] labels = { "拔线", "稍候", "失败" };
                for (int i = 0; i < 3; i++) {
                    form.SetFeedback(labels[i], i == 1, i == 2);
                    using (var tile = form.CreateSnapshot()) {
                        using (var graphics = Graphics.FromImage(image)) graphics.DrawImageUnscaled(tile, i * form.Width, 5);
                    }
                }
                image.Save(imagePath, ImageFormat.Png);
            }
            Console.WriteLine("Preview: " + imagePath);
            form.SetFeedback("拔线", false, false); Application.DoEvents();
            using (var screen = new Bitmap(form.Width, form.Height)) {
                using (var graphics = Graphics.FromImage(screen)) graphics.CopyFromScreen(form.Location, Point.Empty, form.Size);
                screen.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "floating-button-screen.png"), ImageFormat.Png);
            }
        }
        Console.WriteLine("Failures: " + failed); return failed == 0 ? 0 : 1;
    }
}

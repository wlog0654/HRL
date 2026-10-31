using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Win32;

namespace HsReconnectLite {
    public sealed class FloatingButton : Form {
        readonly Action activate;
        readonly Action moved;
        readonly DragGesture gesture = new DragGesture();
        readonly System.Windows.Forms.Timer feedbackTimer;
        readonly Font labelFont;
        readonly ContextMenuStrip menuModel;
        Bitmap badge;
        int currentDpi = 96;
        public Action<Point> MenuRequested;
        public Action DisplayChangedCallback;
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        struct NativeRect { public int Left, Top, Right, Bottom; }
        string label = "拔线";
        Color accent = Color.FromArgb(151, 224, 203);
        bool busy;
        bool subscribed;
        bool ready;
        double surfaceOpacity = 0.68;
        public new double Opacity {
            get { return surfaceOpacity; }
            set { surfaceOpacity = Math.Max(0.05, Math.Min(1, value)); RefreshSurface(); }
        }

        public FloatingButton(Action clicked, Action positionChanged, ContextMenuStrip menu, Point? savedPosition) {
            activate = clicked; moved = positionChanged;
            menuModel = menu;
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true;
            StartPosition = FormStartPosition.Manual; AutoScaleMode = AutoScaleMode.None;
            BackColor = Color.FromArgb(24, 32, 36); ForeColor = Color.FromArgb(238, 244, 242);
            Opacity = 0.68; Text = "炉石轻连悬浮按钮";
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            using (Graphics graphics = CreateGraphics()) currentDpi = DesktopUi.WindowDpi(Handle, (int)Math.Round(graphics.DpiX));
            ClientSize = new Size(DesktopUi.Pixels(76, currentDpi), DesktopUi.Pixels(40, currentDpi));
            labelFont = new Font("Microsoft YaHei UI", 9, FontStyle.Bold, GraphicsUnit.Point);
            badge = Branding.LoadBadge(DesktopUi.Pixels(18, currentDpi));
            var area = Screen.PrimaryScreen.WorkingArea;
            Location = KeepOnScreen(savedPosition ?? new Point(area.Right - Width - 24, area.Top + area.Height / 2));
            feedbackTimer = new System.Windows.Forms.Timer { Interval = 2200 };
            feedbackTimer.Tick += delegate { feedbackTimer.Stop(); label = "拔线"; accent = Color.FromArgb(151, 224, 203); RefreshSurface(); };
            SystemEvents.DisplaySettingsChanged += DisplayChanged; subscribed = true;
            ready = true;
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams {
            get { var parameters = base.CreateParams; parameters.ExStyle |= 0x08000000 | 0x00000080 | 0x00080000; return parameters; }
        }
        protected override void WndProc(ref Message message) {
            if (message.Msg == 0x0021) { message.Result = new IntPtr(3); return; } // MA_NOACTIVATE
            if (message.Msg == 0x02E0) { // WM_DPICHANGED: redraw at target monitor's real pixel density.
                currentDpi = Math.Max(96, (int)(message.WParam.ToInt64() & 0xffff));
                var rectangle = (NativeRect)System.Runtime.InteropServices.Marshal.PtrToStructure(message.LParam, typeof(NativeRect));
                ClientSize = new Size(DesktopUi.Pixels(76, currentDpi), DesktopUi.Pixels(40, currentDpi));
                var previous = badge; badge = Branding.LoadBadge(DesktopUi.Pixels(18, currentDpi)); if (previous != null) previous.Dispose();
                Location = KeepOnScreen(new Point(rectangle.Left, rectangle.Top));
                RefreshSurface();
                if (ready && moved != null) moved();
                if (DisplayChangedCallback != null) DisplayChangedCallback();
                message.Result = IntPtr.Zero; return;
            }
            base.WndProc(ref message);
        }
        GraphicsPath Outline() {
            var path = new GraphicsPath(); float diameter = Height - 2;
            path.AddArc(1, 1, diameter, diameter, 90, 180);
            path.AddArc(Width - diameter - 1, 1, diameter, diameter, 270, 180);
            path.CloseFigure(); return path;
        }
        public Bitmap CreateSnapshot() {
            var bitmap = new Bitmap(Width, Height, PixelFormat.Format32bppPArgb);
            bitmap.SetResolution(currentDpi, currentDpi);
            using (var graphics = Graphics.FromImage(bitmap)) {
                graphics.Clear(Color.Transparent); graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var path = Outline()) using (var background = new SolidBrush(BackColor)) graphics.FillPath(background, path);
                int left = DesktopUi.Pixels(7, currentDpi);
                if (badge != null) graphics.DrawImageUnscaled(badge, left, (Height - badge.Height) / 2);
                int dot = DesktopUi.Pixels(3, currentDpi);
                using (var brush = new SolidBrush(accent)) graphics.FillEllipse(brush, left + DesktopUi.Pixels(15, currentDpi), Height / 2 + DesktopUi.Pixels(5, currentDpi), dot, dot);
                graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                using (var brush = new SolidBrush(ForeColor)) using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    graphics.DrawString(label, labelFont ?? Font, brush, new RectangleF(Width / 3F, 0, Width * 3 / 5F, Height), format);
            }
            return bitmap;
        }
        void RefreshSurface() {
            if (!ready || !Visible || IsDisposed || !IsHandleCreated) return;
            using (var bitmap = CreateSnapshot()) LayeredSurface.Present(Handle, bitmap, Location, surfaceOpacity);
        }
        protected override void OnShown(EventArgs e) { base.OnShown(e); RefreshSurface(); }
        protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); RefreshSurface(); }
        protected override void OnPaint(PaintEventArgs e) {
            // Also support WM_PRINTCLIENT without using an opaque background or border.
            if (!ready) return;
            using (var bitmap = CreateSnapshot()) e.Graphics.DrawImageUnscaled(bitmap, 0, 0);
        }
        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); Opacity = 0.94; }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); if (!gesture.Active) Opacity = 0.68; }
        protected override void OnMouseDown(MouseEventArgs e) {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left) { gesture.Begin(PointToScreen(e.Location), Location); Capture = true; }
        }
        protected override void OnMouseMove(MouseEventArgs e) {
            base.OnMouseMove(e);
            if (!gesture.Active) return;
            Point desired = gesture.Move(PointToScreen(e.Location), SystemInformation.DragSize);
            if (gesture.Dragging) { Cursor = Cursors.SizeAll; Location = desired; }
        }
        protected override void OnMouseUp(MouseEventArgs e) {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Right) {
                Point point = PointToScreen(e.Location);
                if (MenuRequested != null) MenuRequested(point); else NativeContextMenu.Show(menuModel, point);
                return;
            }
            if (e.Button != MouseButtons.Left || !gesture.Active) return;
            bool dragged = gesture.Dragging;
            bool clicked = gesture.End(PointToScreen(e.Location), SystemInformation.DragSize);
            Capture = false; Cursor = Cursors.Default; Opacity = ClientRectangle.Contains(e.Location) ? 0.94 : 0.68;
            if (dragged || !clicked) { Location = KeepOnScreen(Location); if (moved != null) moved(); }
            else if (!busy && ClientRectangle.Contains(e.Location) && activate != null) activate();
        }
        protected override void OnMouseCaptureChanged(EventArgs e) {
            base.OnMouseCaptureChanged(e);
            if (!Capture && gesture.Active) { gesture.Cancel(); Cursor = Cursors.Default; Location = KeepOnScreen(Location); if (moved != null) moved(); }
        }
        public void SetFeedback(string text, bool processing, bool error) {
            feedbackTimer.Stop(); busy = processing; label = text;
            accent = error ? Color.FromArgb(245, 167, 111) : Color.FromArgb(151, 224, 203);
            if (!processing) feedbackTimer.Start(); RefreshSurface();
        }
        public Point KeepOnScreen(Point desired) { return OverlayPlacement.Clamp(desired, Size, Screen.AllScreens.Select(s => s.WorkingArea).ToArray()); }
        void DisplayChanged(object sender, EventArgs e) {
            if (IsDisposed || !IsHandleCreated) return;
            try { BeginInvoke((Action)delegate {
                if (IsDisposed) return; Point clamped = KeepOnScreen(Location);
                if (clamped != Location) { Location = clamped; if (moved != null) moved(); }
                if (DisplayChangedCallback != null) DisplayChangedCallback();
            }); } catch (InvalidOperationException) { }
        }
        protected override void Dispose(bool disposing) {
            if (disposing) {
                if (subscribed) { SystemEvents.DisplaySettingsChanged -= DisplayChanged; subscribed = false; }
                if (feedbackTimer != null) feedbackTimer.Dispose();
                if (labelFont != null) labelFont.Dispose();
                if (badge != null) { badge.Dispose(); badge = null; }
            }
            base.Dispose(disposing);
        }
    }
}

using System;
using System.Drawing;
using System.Windows.Forms;

namespace HsReconnectLite {
    public sealed class HotkeyDialog : Form {
        readonly Label status;
        readonly Font uiFont;
        readonly Func<HotkeyBinding, string> apply;
        HotkeyCapture capture;
        bool saving;
        public HotkeyDialog(HotkeyBinding current, Func<HotkeyBinding, string> callback) {
            apply = callback;
            Text = "HRL · Hotkeys"; FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false; StartPosition = FormStartPosition.CenterScreen;
            AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(440, 240); KeyPreview = true;
            uiFont = new Font("Microsoft YaHei UI", 9); Font = uiFont;
            Icon = Branding.LoadIcon(32);
            var title = new Label { Text = "直接按下新的快捷键", AutoSize = true, Location = new Point(20, 18) };
            status = new Label { Text = "当前：" + current, Location = new Point(20, 52), Size = new Size(400, 62) };
            var help = new Label { Text = "组合键：Ctrl / Alt / Shift + 字母或数字\n也支持 F1–F24（F12 除外）。\n按下即保存；录入时不会拔线。Esc 关闭。", Location = new Point(20, 117), Size = new Size(400, 75) };
            var reset = new Button { Text = "恢复默认", Location = new Point(20, 203), Size = new Size(90, 28) };
            reset.Click += delegate { Apply(HotkeyBinding.Default); };
            var close = new Button { Text = "关闭", Location = new Point(330, 203), Size = new Size(90, 28), DialogResult = DialogResult.Cancel };
            CancelButton = close;
            Controls.AddRange(new Control[] { title, status, help, reset, close });
        }
        protected override void OnActivated(EventArgs e) {
            base.OnActivated(e);
            if (capture != null) return;
            try {
                capture = new HotkeyCapture(Handle, delegate(Keys keys) {
                    // Return promptly from the native hook; disk access happens later.
                    BeginInvoke((Action)delegate {
                        if (!IsDisposed && ContainsFocus) Record(keys);
                    });
                });
            } catch (System.ComponentModel.Win32Exception) {
                status.ForeColor = Color.Firebrick;
                status.Text = "无法启用按键录入，请关闭此窗口后重试。";
            }
        }
        protected override void OnDeactivate(EventArgs e) {
            if (capture != null) { capture.Dispose(); capture = null; }
            base.OnDeactivate(e);
        }
        protected override bool ProcessCmdKey(ref Message message, Keys keyData) {
            if ((keyData & Keys.KeyCode) == Keys.Escape) { Close(); return true; }
            return base.ProcessCmdKey(ref message, keyData);
        }
        void Record(Keys keyData) {
            var binding = HotkeyBinding.FromKeys(keyData);
            if (binding.IsValid) Apply(binding);
            else {
                status.ForeColor = Color.Firebrick;
                status.Text = (keyData & Keys.KeyCode) == Keys.F12 ? "F12 为系统调试保留键，请选择其他按键。" : "请同时按住 Ctrl、Alt 或 Shift，再按字母或数字。";
            }
        }
        void Apply(HotkeyBinding binding) {
            if (saving) return;
            saving = true;
            try {
                string error = apply(binding);
                status.ForeColor = error == null ? Color.FromArgb(24, 116, 85) : Color.Firebrick;
                status.Text = error ?? ("已保存：" + binding + "\n关闭此窗口后即可使用。");
            } finally { saving = false; }
        }
        protected override void Dispose(bool disposing) {
            if (disposing && capture != null) { capture.Dispose(); capture = null; }
            if (disposing && Icon != null) { Icon.Dispose(); Icon = null; }
            base.Dispose(disposing);
            if (disposing && uiFont != null) uiFont.Dispose();
        }
    }
}

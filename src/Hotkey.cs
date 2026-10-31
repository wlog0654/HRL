using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace HsReconnectLite {
    public sealed class HotkeyBinding {
        public readonly Keys Key;
        public readonly uint Modifiers;
        public static readonly HotkeyBinding Default = new HotkeyBinding(Keys.R, 6);
        public HotkeyBinding(Keys key, uint modifiers) { Key = key; Modifiers = modifiers; }
        public bool IsValid {
            get {
                bool function = Key >= Keys.F1 && Key <= Keys.F24 && Key != Keys.F12;
                bool normal = (Key >= Keys.A && Key <= Keys.Z) || (Key >= Keys.D0 && Key <= Keys.D9) || (Key >= Keys.NumPad0 && Key <= Keys.NumPad9);
                return (Modifiers & ~7u) == 0 && (function || (normal && Modifiers != 0));
            }
        }
        public static HotkeyBinding FromKeys(Keys keys) {
            uint modifiers = 0;
            if ((keys & Keys.Control) != 0) modifiers |= 2;
            if ((keys & Keys.Alt) != 0) modifiers |= 1;
            if ((keys & Keys.Shift) != 0) modifiers |= 4;
            return new HotkeyBinding(keys & Keys.KeyCode, modifiers);
        }
        public bool SameAs(HotkeyBinding other) { return other != null && other.Key == Key && other.Modifiers == Modifiers; }
        public override string ToString() {
            string key = Key >= Keys.D0 && Key <= Keys.D9 ? ((int)Key - (int)Keys.D0).ToString() : Key.ToString();
            return ((Modifiers & 2) != 0 ? "Ctrl+" : "") + ((Modifiers & 1) != 0 ? "Alt+" : "") + ((Modifiers & 4) != 0 ? "Shift+" : "") + key;
        }
    }
    public sealed class HotkeyRegistration : NativeWindow, IDisposable {
        [DllImport("user32.dll", SetLastError = true)] static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr window, int id);
        readonly Action action;
        int activeId;
        bool disposed;
        public bool Suppressed;
        public HotkeyBinding Current { get; private set; }
        public HotkeyRegistration(Action callback) {
            action = callback; CreateHandle(new CreateParams { Parent = new IntPtr(-3) });
        }
        public bool TryChange(HotkeyBinding binding, Func<bool> persist, out string error) {
            error = null;
            if (disposed) { error = "快捷键服务已关闭。"; return false; }
            if (binding == null || !binding.IsValid) { error = "请按 Ctrl / Alt / Shift 加字母或数字，或 F1–F24（F12 除外）。"; return false; }
            if (binding.SameAs(Current)) return true;
            int next = activeId == 1 ? 2 : 1;
            if (!RegisterHotKey(Handle, next, binding.Modifiers | 0x4000, (uint)binding.Key)) {
                error = "快捷键 " + binding + " 无法注册，可能已被其他程序占用。原快捷键保持不变。";
                return false;
            }
            bool saved = false;
            try { saved = persist == null || persist(); }
            catch { saved = false; }
            if (!saved) {
                UnregisterHotKey(Handle, next);
                error = "设置保存失败，原快捷键保持不变。请检查本地设置目录的写入权限。";
                return false;
            }
            if (activeId != 0) UnregisterHotKey(Handle, activeId);
            activeId = next; Current = binding; return true;
        }
        protected override void WndProc(ref Message message) {
            if (message.Msg == 0x0312 && message.WParam.ToInt32() == activeId && !Suppressed && !disposed) {
                // Ignore stale WM_HOTKEY messages from a previous binding after a switch.
                long data = message.LParam.ToInt64();
                if (Current != null && (uint)(data & 0xffff) == Current.Modifiers && (Keys)((data >> 16) & 0xffff) == Current.Key) action();
            }
            base.WndProc(ref message);
        }
        public void Dispose() {
            if (disposed) return;
            disposed = true;
            if (activeId != 0) UnregisterHotKey(Handle, activeId);
            DestroyHandle();
        }
    }
}

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace HsReconnectLite {
    // Installed only while the recording dialog is foreground. Never used at idle.
    public sealed class HotkeyCapture : IDisposable {
        delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);
        [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetWindowsHookEx(int kind, HookProc callback, IntPtr module, uint thread);
        [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        readonly HookProc callback;
        readonly Action<Keys> captured;
        readonly IntPtr owner;
        readonly HashSet<int> held = new HashSet<int>();
        IntPtr handle;
        public HotkeyCapture(IntPtr window, Action<Keys> action) {
            owner = window; captured = action; callback = OnKey;
            handle = SetWindowsHookEx(13, callback, GetModuleHandle(null), 0);
            if (handle == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        IntPtr OnKey(int code, IntPtr message, IntPtr data) {
            if (code < 0 || GetForegroundWindow() != owner) return CallNextHookEx(handle, code, message, data);
            int key = Marshal.ReadInt32(data), kind = message.ToInt32();
            bool supported = (key >= (int)Keys.A && key <= (int)Keys.Z) || (key >= (int)Keys.D0 && key <= (int)Keys.D9)
                || (key >= (int)Keys.NumPad0 && key <= (int)Keys.NumPad9) || (key >= (int)Keys.F1 && key <= (int)Keys.F24);
            if (!supported || GetAsyncKeyState((int)Keys.LWin) < 0 || GetAsyncKeyState((int)Keys.RWin) < 0)
                return CallNextHookEx(handle, code, message, data);
            if (kind == 0x100 || kind == 0x104) {
                if (held.Add(key)) {
                    Keys keys = (Keys)key;
                    if (GetAsyncKeyState((int)Keys.ControlKey) < 0) keys |= Keys.Control;
                    if (GetAsyncKeyState((int)Keys.Menu) < 0) keys |= Keys.Alt;
                    if (GetAsyncKeyState((int)Keys.ShiftKey) < 0) keys |= Keys.Shift;
                    try { captured(keys); } catch (InvalidOperationException) { /* Owner is closing. */ }
                }
                return new IntPtr(1); // Do not trigger another application's registered hotkey.
            }
            if ((kind == 0x101 || kind == 0x105) && held.Remove(key)) return new IntPtr(1);
            return CallNextHookEx(handle, code, message, data);
        }
        public void Dispose() {
            if (handle == IntPtr.Zero) return;
            UnhookWindowsHookEx(handle); handle = IntPtr.Zero; held.Clear();
        }
    }
}

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace HsReconnectLite {
    public sealed class NativeMenuSession : IDisposable {
        [DllImport("user32.dll", SetLastError = true)] static extern IntPtr CreatePopupMenu();
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool AppendMenu(IntPtr menu, uint flags, UIntPtr id, string text);
        [DllImport("user32.dll")] static extern bool DestroyMenu(IntPtr menu);
        readonly Dictionary<uint, ToolStripItem> commands = new Dictionary<uint, ToolStripItem>();
        bool executed;
        public IntPtr Handle { get; private set; }
        public NativeMenuSession(ContextMenuStrip model) {
            Handle = CreatePopupMenu();
            if (Handle == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            try {
                for (int i = 0; i < model.Items.Count; i++) {
                    ToolStripItem item = model.Items[i]; if (!item.Available) continue;
                    uint id = (uint)i + 1;
                    uint flags = item is ToolStripSeparator ? 0x800u : 0;
                    if (!item.Enabled) flags |= 1;
                    var toggle = item as ToolStripMenuItem; if (toggle != null && toggle.Checked) flags |= 8;
                    if (!AppendMenu(Handle, flags, new UIntPtr(id), item.Text)) throw new Win32Exception(Marshal.GetLastWin32Error());
                    if (!(item is ToolStripSeparator)) commands.Add(id, item);
                }
            } catch { Dispose(); throw; }
        }
        public bool Execute(uint command) {
            ToolStripItem item;
            if (executed || Handle == IntPtr.Zero || !commands.TryGetValue(command, out item) || !item.Enabled || !item.Available) return false;
            executed = true; item.PerformClick(); return true;
        }
        public void Dispose() { if (Handle != IntPtr.Zero) { DestroyMenu(Handle); Handle = IntPtr.Zero; } }
    }
    public static class NativeContextMenu {
        [DllImport("user32.dll")] static extern int TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr owner, IntPtr parameters);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr window, int command);
        [DllImport("user32.dll")] static extern bool SetLayeredWindowAttributes(IntPtr window, uint color, byte alpha, uint flags);
        sealed class PopupOwner : NativeWindow, IDisposable {
            public PopupOwner(Point point) {
                CreateHandle(new CreateParams { Caption = "HsReconnectLite.Menu", Style = unchecked((int)0x80000000), ExStyle = 0x80 | 0x80000,
                    X = point.X, Y = point.Y, Width = 1, Height = 1 });
                SetLayeredWindowAttributes(Handle, 0, 0, 2); ShowWindow(Handle, 4);
            }
            public void Dispose() { DestroyHandle(); }
        }
        static bool showing;
        public static void Show(ContextMenuStrip model, Point point) {
            if (showing || model.IsDisposed) return;
            showing = true; IntPtr previousContext = IntPtr.Zero;
            try {
                try { previousContext = DesktopUi.SetThreadDpiAwarenessContext(new IntPtr(-4)); } catch (EntryPointNotFoundException) { }
                using (var session = new NativeMenuSession(model)) {
                    int command;
                    using (var owner = new PopupOwner(point)) {
                        IntPtr previousWindow = GetForegroundWindow();
                        try {
                            SetForegroundWindow(owner.Handle);
                            command = TrackPopupMenuEx(session.Handle, 0x100 | 0x80 | 0x2, point.X, point.Y, owner.Handle, IntPtr.Zero);
                        } finally {
                            PostMessage(owner.Handle, 0, IntPtr.Zero, IntPtr.Zero);
                            if (GetForegroundWindow() == owner.Handle && previousWindow != IntPtr.Zero) SetForegroundWindow(previousWindow);
                        }
                    }
                    session.Execute((uint)command);
                }
            } finally {
                if (previousContext != IntPtr.Zero) DesktopUi.SetThreadDpiAwarenessContext(previousContext);
                showing = false;
            }
        }
    }
}

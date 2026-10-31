using System;
using System.Runtime.InteropServices;

namespace HsReconnectLite {
    public static class DesktopUi {
        [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr context);
        [DllImport("shcore.dll")] static extern int SetProcessDpiAwareness(int awareness);
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
        [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr window);
        [DllImport("user32.dll")] static extern int GetSystemMetricsForDpi(int metric, uint dpi);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string name, string title);
        public static void InitializeDpi() {
            try { if (SetProcessDpiAwarenessContext(new IntPtr(-4))) return; } catch (EntryPointNotFoundException) { }
            try { if (SetProcessDpiAwareness(2) == 0) return; } catch (EntryPointNotFoundException) { } catch (DllNotFoundException) { }
            SetProcessDPIAware();
        }
        public static int WindowDpi(IntPtr window, int fallback) {
            try { uint dpi = GetDpiForWindow(window); if (dpi > 0) return (int)dpi; } catch (EntryPointNotFoundException) { }
            return Math.Max(96, fallback);
        }
        public static int TrayDpi { get { return WindowDpi(FindWindow("Shell_TrayWnd", null), 96); } }
        public static int IconPixels(int dpi) {
            try { return Math.Max(16, Math.Min(64, GetSystemMetricsForDpi(49, (uint)dpi))); } catch (EntryPointNotFoundException) { return Pixels(16, dpi); }
        }
        public static int Pixels(int logical, int dpi) { return Math.Max(1, (int)Math.Round(logical * Math.Max(96, dpi) / 96.0)); }
    }
}

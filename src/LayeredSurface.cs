using System;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;

namespace HsReconnectLite {
    // Per-pixel alpha keeps rounded edges antialiased without a hard window region.
    static class LayeredSurface {
        [StructLayout(LayoutKind.Sequential)] struct NativePoint { public int X, Y; public NativePoint(int x, int y) { X = x; Y = y; } }
        [StructLayout(LayoutKind.Sequential)] struct NativeSize { public int Width, Height; public NativeSize(int w, int h) { Width = w; Height = h; } }
        [StructLayout(LayoutKind.Sequential, Pack = 1)] struct Blend { public byte Operation, Flags, Alpha, Format; }
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr window);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr window, IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr value);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr value);
        [DllImport("user32.dll", SetLastError = true)] static extern bool UpdateLayeredWindow(IntPtr window, IntPtr screen, ref NativePoint destination,
            ref NativeSize size, IntPtr source, ref NativePoint origin, uint key, ref Blend blend, uint flags);
        public static void Present(IntPtr window, Bitmap image, Point location, double opacity) {
            IntPtr screen = GetDC(IntPtr.Zero), memory = IntPtr.Zero, bitmap = IntPtr.Zero, previous = IntPtr.Zero;
            try {
                if (screen == IntPtr.Zero) throw new InvalidOperationException("Cannot obtain screen drawing context.");
                memory = CreateCompatibleDC(screen);
                if (memory == IntPtr.Zero) throw new InvalidOperationException("Cannot create drawing context.");
                bitmap = image.GetHbitmap(Color.FromArgb(0)); previous = SelectObject(memory, bitmap);
                var destination = new NativePoint(location.X, location.Y); var origin = new NativePoint(0, 0);
                var size = new NativeSize(image.Width, image.Height);
                var blend = new Blend { Alpha = (byte)Math.Round(255 * opacity), Format = 1 };
                if (!UpdateLayeredWindow(window, screen, ref destination, ref size, memory, ref origin, 0, ref blend, 2))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
            } finally {
                if (previous != IntPtr.Zero && previous != new IntPtr(-1)) SelectObject(memory, previous);
                if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
                if (memory != IntPtr.Zero) DeleteDC(memory);
                if (screen != IntPtr.Zero) ReleaseDC(IntPtr.Zero, screen);
            }
        }
    }
}

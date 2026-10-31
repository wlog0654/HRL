using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace HsReconnectLite {
    public static class Branding {
        [DllImport("user32.dll")] static extern IntPtr CreateIconFromResourceEx(byte[] data, uint length, bool icon, uint version, int width, int height, uint flags);
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
        public static Icon LoadIcon(int size) {
            try {
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("HsReconnectLite.AppIcon.ico")) {
                    if (stream != null) using (var reader = new BinaryReader(stream)) {
                        reader.ReadUInt16(); reader.ReadUInt16(); int count = reader.ReadUInt16();
                        uint bestOffset = 0, bestLength = 0; int nearest = Int32.MaxValue;
                        for (int i = 0; i < count; i++) {
                            int width = reader.ReadByte(); if (width == 0) width = 256;
                            reader.ReadByte(); reader.ReadByte(); reader.ReadByte(); reader.ReadUInt16(); reader.ReadUInt16();
                            uint length = reader.ReadUInt32(), offset = reader.ReadUInt32();
                            int distance = Math.Abs(width - size);
                            if (distance < nearest) { nearest = distance; bestLength = length; bestOffset = offset; }
                        }
                        if (bestLength > 0 && bestLength <= 1024 * 1024 && bestOffset + (long)bestLength <= stream.Length) {
                            stream.Position = bestOffset; byte[] bytes = reader.ReadBytes((int)bestLength);
                            IntPtr handle = CreateIconFromResourceEx(bytes, (uint)bytes.Length, true, 0x30000, size, size, 0);
                            if (handle != IntPtr.Zero) {
                                try { using (var icon = Icon.FromHandle(handle)) return (Icon)icon.Clone(); }
                                finally { DestroyIcon(handle); }
                            }
                        }
                    }
                }
            } catch (ArgumentException) { } catch (IOException) { } catch (ExternalException) { }
            return (Icon)SystemIcons.Application.Clone();
        }
        public static Bitmap LoadBadge(int size) { using (var icon = LoadIcon(size)) return icon.ToBitmap(); }
    }
}

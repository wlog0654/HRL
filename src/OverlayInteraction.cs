using System;
using System.Drawing;
using System.IO;

namespace HsReconnectLite {
    public sealed class DragGesture {
        public bool Active { get; private set; }
        public bool Dragging { get; private set; }
        Point start, origin;
        public void Begin(Point pointer, Point window) { start = pointer; origin = window; Active = true; Dragging = false; }
        public Point Move(Point pointer, Size threshold) {
            if (!Active) return origin;
            int dx = pointer.X - start.X, dy = pointer.Y - start.Y;
            if (Math.Abs((long)dx) >= Math.Max(1, threshold.Width / 2) || Math.Abs((long)dy) >= Math.Max(1, threshold.Height / 2)) Dragging = true;
            return Dragging ? new Point(origin.X + dx, origin.Y + dy) : origin;
        }
        public bool End(Point pointer, Size threshold) {
            if (!Active) return false;
            Move(pointer, threshold);
            bool click = !Dragging; Active = false; return click;
        }
        public void Cancel() { Active = false; }
    }
    public static class OverlayPlacement {
        public static Point Clamp(Point desired, Size size, Rectangle[] areas) {
            if (areas == null || areas.Length == 0) return desired;
            Rectangle best = areas[0]; double nearest = Double.MaxValue;
            foreach (var area in areas) {
                int x = Math.Max(area.Left, Math.Min(desired.X, Math.Max(area.Left, area.Right - size.Width)));
                int y = Math.Max(area.Top, Math.Min(desired.Y, Math.Max(area.Top, area.Bottom - size.Height)));
                double dx = (double)x - desired.X, dy = (double)y - desired.Y;
                double distance = dx * dx + dy * dy;
                if (distance < nearest) { nearest = distance; best = area; }
            }
            return new Point(Math.Max(best.Left, Math.Min(desired.X, Math.Max(best.Left, best.Right - size.Width))),
                Math.Max(best.Top, Math.Min(desired.Y, Math.Max(best.Top, best.Bottom - size.Height))));
        }
    }
    public sealed class OverlayPreferences {
        public bool Enabled = true;
        public Point? Position;
        public HotkeyBinding Hotkey = HotkeyBinding.Default;
        public static OverlayPreferences Load(string path) {
            var preferences = new OverlayPreferences();
            try {
                if (!File.Exists(path) || new FileInfo(path).Length > 4096) return preferences;
                int x = 0, y = 0, key = 0; uint modifiers = 0; bool hasX = false, hasY = false, hasKey = false, hasModifiers = false;
                foreach (string line in File.ReadAllLines(path)) {
                    string[] pair = line.Split(new[] { '=' }, 2); if (pair.Length != 2) continue;
                    if (pair[0] == "enabled" && (pair[1] == "0" || pair[1] == "1")) preferences.Enabled = pair[1] == "1";
                    if (pair[0] == "x") hasX = Int32.TryParse(pair[1], out x);
                    if (pair[0] == "y") hasY = Int32.TryParse(pair[1], out y);
                    if (pair[0] == "hotkey") hasKey = Int32.TryParse(pair[1], out key);
                    if (pair[0] == "modifiers") hasModifiers = UInt32.TryParse(pair[1], out modifiers);
                }
                if (hasX && hasY) preferences.Position = new Point(x, y);
                var binding = new HotkeyBinding((System.Windows.Forms.Keys)key, modifiers);
                if (hasKey && hasModifiers && binding.IsValid) preferences.Hotkey = binding;
            } catch { }
            return preferences;
        }
        public bool Save(string path) {
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
                string text = "enabled=" + (Enabled ? "1" : "0") + Environment.NewLine;
                if (Position.HasValue) text += "x=" + Position.Value.X + Environment.NewLine + "y=" + Position.Value.Y + Environment.NewLine;
                var binding = Hotkey != null && Hotkey.IsValid ? Hotkey : HotkeyBinding.Default;
                text += "hotkey=" + (int)binding.Key + Environment.NewLine + "modifiers=" + binding.Modifiers + Environment.NewLine;
                File.WriteAllText(temporary, text);
                if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
                return true;
            } catch { return false; }
            finally { try { if (File.Exists(temporary)) File.Delete(temporary); } catch { } }
        }
    }
}

using System;
using System.Drawing;
using System.IO;
using HsReconnectLite;

class OverlayTests {
    static int failed;
    static void Check(string name, bool ok) { Console.WriteLine((ok ? "PASS " : "FAIL ") + name); if (!ok) failed++; }
    static int Main() {
        var gesture = new DragGesture(); var threshold = new Size(8, 8);
        gesture.Begin(new Point(110, 110), new Point(100, 100));
        Check("mouse-down alone never completes a click", gesture.Active);
        Check("small hand jitter is a click", gesture.End(new Point(112, 111), threshold));
        gesture.Begin(new Point(110, 110), new Point(100, 100));
        Check("drag updates screen position", gesture.Move(new Point(140, 125), threshold) == new Point(130, 115));
        Check("drag release never triggers click", !gesture.End(new Point(140, 125), threshold));
        gesture.Begin(new Point(110, 110), new Point(100, 100));
        gesture.Move(new Point(150, 110), threshold); gesture.Move(new Point(110, 110), threshold);
        Check("drag away and back never triggers click", !gesture.End(new Point(110, 110), threshold));
        gesture.Begin(Point.Empty, Point.Empty); gesture.Cancel();
        Check("capture lost cancels click", !gesture.End(Point.Empty, threshold));
        gesture.Begin(Point.Empty, Point.Empty);
        Check("large move without intermediate events is drag", !gesture.End(new Point(40, 0), threshold));
        var primary = new Rectangle(0, 0, 1920, 1040); var left = new Rectangle(-1280, 0, 1280, 984);
        Check("disconnected monitor position recovered", OverlayPlacement.Clamp(new Point(3000, 400), new Size(76, 40), new[] { primary }) == new Point(1844, 400));
        Check("negative monitor coordinates retained", OverlayPlacement.Clamp(new Point(-1000, 50), new Size(76, 40), new[] { primary, left }) == new Point(-1000, 50));
        Check("button stays inside bottom edge", OverlayPlacement.Clamp(new Point(40, 1030), new Size(76, 40), new[] { primary }) == new Point(40, 1000));
        Check("missing monitor list handled", OverlayPlacement.Clamp(new Point(20, 20), new Size(76, 40), new Rectangle[0]) == new Point(20, 20));
        string directory = Path.Combine(Path.GetTempPath(), "HsOverlayTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "overlay.ini");
        try {
            Check("missing preferences default to visible", OverlayPreferences.Load(path).Enabled);
            var preferences = new OverlayPreferences { Enabled = false, Position = new Point(-1000, 70) };
            Check("preferences saved", preferences.Save(path));
            var loaded = OverlayPreferences.Load(path);
            Check("visibility and negative coordinates restored", !loaded.Enabled && loaded.Position == new Point(-1000, 70));
            preferences.Position = new Point(250, 90); preferences.Enabled = true;
            Check("atomic overwrite supported", preferences.Save(path) && OverlayPreferences.Load(path).Position == new Point(250, 90));
            File.WriteAllText(path, "enabled=nonsense\nx=2147483647999\ny=oops");
            var corrupt = OverlayPreferences.Load(path);
            Check("corrupted settings recover to defaults", corrupt.Enabled && !corrupt.Position.HasValue);
        } finally {
            foreach (string file in Directory.GetFiles(directory)) File.Delete(file);
            Directory.Delete(directory);
        }
        Console.WriteLine("Failures: " + failed); return failed == 0 ? 0 : 1;
    }
}

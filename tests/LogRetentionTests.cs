using System;
using System.IO;
using System.Diagnostics;
using System.Threading;
using HsReconnectLite;

class LogRetentionTests {
    static int failures;
    static void Check(string name, bool ok) { Console.WriteLine((ok ? "PASS " : "FAIL ") + name); if (!ok) failures++; }
    static string Session(string root, int day) {
        string path = Path.Combine(root, "Hearthstone_2020_01_" + day.ToString("00") + "_12_00_00");
        Directory.CreateDirectory(path); Directory.SetCreationTimeUtc(path, new DateTime(2020, 1, day, 12, 0, 0, DateTimeKind.Utc));
        return path;
    }
    static string Log(string folder, string name, int bytes) { string path = Path.Combine(folder, name); File.WriteAllBytes(path, new byte[bytes]); return path; }
    static void Junction(string path, string target) {
        using (var process = Process.Start(new ProcessStartInfo("cmd.exe", "/d /c mklink /J \"" + path + "\" \"" + target + "\"") {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        })) {
            if (!process.WaitForExit(5000) || process.ExitCode != 0) throw new Exception("Unable to create isolated test junction.");
        }
    }
    static int Main() {
        string fixture = Path.Combine(Path.GetTempPath(), "HRL-LogTests-" + Guid.NewGuid().ToString("N"));
        string root = Path.Combine(fixture, "Logs"); Directory.CreateDirectory(root);
        try {
            string a = Log(Session(root, 1), "Power.log", 50);
            string b = Log(Session(root, 2), "GameNetLogger.log", 50);
            string current = Log(Session(root, 3), "Power.log", 60);
            var result = LogRetention.Clean(root, DateTime.UtcNow, 150, 110, CancellationToken.None);
            Check("oldest removed first until low watermark", !File.Exists(a) && File.Exists(b) && File.Exists(current) && result.RemainingBytes == 110 && result.DeletedFiles == 1);
            result = LogRetention.Clean(root, DateTime.UtcNow, 150, 80, CancellationToken.None);
            Check("below high watermark does not clean", File.Exists(b) && result.DeletedFiles == 0);
            string unknown = Log(Path.GetDirectoryName(b), "keep.txt", 100);
            string nestedDir = Path.Combine(Path.GetDirectoryName(b), "nested"); Directory.CreateDirectory(nestedDir);
            string nested = Log(nestedDir, "keep.log", 100);
            string foreignDir = Path.Combine(root, "other"); Directory.CreateDirectory(foreignDir);
            string foreign = Log(foreignDir, "keep.log", 100);
            result = LogRetention.Clean(root, DateTime.UtcNow, 100, 10, CancellationToken.None);
            Check("newest session survives even above target", File.Exists(current) && result.RemainingBytes == 60);
            Check("unknown files and directories never removed", File.Exists(unknown) && File.Exists(nested) && File.Exists(foreign));
            string active = Log(Session(root, 4), "GameNetLogger.log", 100);
            string newest = Log(Session(root, 5), "Power.log", 100);
            DateTime activeStart = new DateTime(2020, 1, 4, 0, 0, 0, DateTimeKind.Local).ToUniversalTime();
            result = LogRetention.Clean(root, activeStart, 150, 1, CancellationToken.None);
            Check("all sessions since active process start protected", File.Exists(active) && File.Exists(newest));
            string locked = Log(Session(root, 1), "locked.log", 100);
            using (var handle = new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None)) {
                result = LogRetention.Clean(root, DateTime.UtcNow, 100, 1, CancellationToken.None);
                Check("locked files skipped without failing cleanup", File.Exists(locked) && File.Exists(newest) && result.SkippedFiles >= 1);
            }
            var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            bool stopped = false;
            try { LogRetention.Clean(root, DateTime.UtcNow, 100, 1, cancelled.Token); } catch (OperationCanceledException) { stopped = true; }
            Check("cancellation prevents deletion", stopped && File.Exists(locked));
            bool refused = false;
            try { LogRetention.Clean(fixture, DateTime.UtcNow, 100, 1, CancellationToken.None); } catch (ArgumentException) { refused = true; }
            Check("non Logs root refused", refused && File.Exists(locked));
            refused = false;
            try { LogRetention.Clean(root, DateTime.UtcNow, 100, 100, CancellationToken.None); } catch (ArgumentException) { refused = true; }
            Check("invalid watermarks refused", refused && File.Exists(locked));
            string outside = Path.Combine(fixture, "Outside"); Directory.CreateDirectory(outside);
            string outsideLog = Log(outside, "Power.log", 500);
            string link = Path.Combine(root, "Hearthstone_2019_01_01_00_00_00");
            Junction(link, outside);
            try {
                LogRetention.Clean(root, DateTime.UtcNow, 100, 1, CancellationToken.None);
                Check("linked session never traversed or cleaned", File.Exists(outsideLog));
            } finally { Directory.Delete(link); }
            string linkedParent = Path.Combine(fixture, "Linked");
            Junction(linkedParent, fixture);
            try {
                result = LogRetention.Clean(Path.Combine(linkedParent, "Logs"), DateTime.UtcNow, 100, 1, CancellationToken.None);
                Check("linked ancestor refused before scanning", result.BeforeBytes == 0 && result.DeletedFiles == 0);
            } finally { Directory.Delete(linkedParent); }
        }
        finally { if (Directory.Exists(fixture)) Directory.Delete(fixture, true); }
        Console.WriteLine("Failures: " + failures); return failures == 0 ? 0 : 1;
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;

namespace HsReconnectLite {
    public sealed class LogCleanupResult {
        public long BeforeBytes, RemainingBytes;
        public int DeletedFiles, SkippedFiles;
    }
    public static class LogRetention {
        sealed class Session {
            public DirectoryInfo Directory;
            public DateTime StartedUtc;
        }
        sealed class Candidate {
            public FileInfo File;
            public long Length;
            public DateTime WrittenUtc;
        }
        static bool SafePath(string path) {
            for (var entry = new DirectoryInfo(path); entry != null; entry = entry.Parent)
                if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) return false;
            return true;
        }
        public static LogCleanupResult Clean(string root, DateTime activeStartUtc, long high, long low, CancellationToken stop) {
            if (low < 0 || high <= low) throw new ArgumentException("Invalid log retention watermarks.");
            var directory = new DirectoryInfo(Path.GetFullPath(root));
            if (!String.Equals(directory.Name, "Logs", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Expected Hearthstone Logs directory.");
            stop.ThrowIfCancellationRequested();
            var result = new LogCleanupResult();
            if (!directory.Exists || !SafePath(directory.FullName)) return result;
            var sessions = new List<Session>();
            foreach (var folder in directory.GetDirectories("Hearthstone_*", SearchOption.TopDirectoryOnly)) {
                stop.ThrowIfCancellationRequested();
                DateTime started;
                if ((folder.Attributes & FileAttributes.ReparsePoint) != 0 ||
                    !DateTime.TryParseExact(folder.Name, "'Hearthstone_'yyyy_MM_dd_HH_mm_ss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out started)) continue;
                sessions.Add(new Session { Directory = folder, StartedUtc = started.ToUniversalTime() });
            }
            if (sessions.Count == 0) return result;
            DateTime newest = sessions.Max(s => s.StartedUtc);
            var candidates = new List<Candidate>();
            foreach (var session in sessions.OrderBy(s => s.StartedUtc)) {
                stop.ThrowIfCancellationRequested();
                bool keep = session.StartedUtc == newest || session.StartedUtc >= activeStartUtc.AddSeconds(-10) || session.Directory.CreationTimeUtc >= activeStartUtc.AddSeconds(-10);
                foreach (var file in session.Directory.GetFiles("*", SearchOption.TopDirectoryOnly).OrderBy(f => f.LastWriteTimeUtc)) {
                    stop.ThrowIfCancellationRequested();
                    if (!String.Equals(file.Extension, ".log", StringComparison.OrdinalIgnoreCase) || (file.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                    long length = file.Length;
                    result.BeforeBytes += length;
                    if (!keep) candidates.Add(new Candidate { File = file, Length = length, WrittenUtc = file.LastWriteTimeUtc });
                }
            }
            result.RemainingBytes = result.BeforeBytes;
            if (result.BeforeBytes <= high) return result;
            foreach (var candidate in candidates) {
                stop.ThrowIfCancellationRequested();
                if (result.RemainingBytes <= low) break;
                try {
                    // Recheck identity boundaries and changes since the snapshot; never recurse.
                    if (!SafePath(candidate.File.DirectoryName)) { result.SkippedFiles++; continue; }
                    candidate.File.Refresh();
                    if (!candidate.File.Exists || (candidate.File.Attributes & FileAttributes.ReparsePoint) != 0 ||
                        candidate.File.Length != candidate.Length || candidate.File.LastWriteTimeUtc != candidate.WrittenUtc) { result.SkippedFiles++; continue; }
                    candidate.File.Delete();
                    result.DeletedFiles++;
                    result.RemainingBytes -= candidate.Length;
                } catch (IOException) { result.SkippedFiles++; }
                catch (UnauthorizedAccessException) { result.SkippedFiles++; }
            }
            return result;
        }
    }
}

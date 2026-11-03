using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace HsReconnectLite {
    public interface IGameRuntime {
        bool IsRunning();
        bool LauncherExists(string path);
        void CloseTree();
        void Launch(string path);
        void Delay(int milliseconds, CancellationToken cancellation);
    }
    public sealed class GameActions {
        readonly IGameRuntime runtime;
        public GameActions(IGameRuntime runtime) { this.runtime = runtime; }
        public string Execute(bool restart, string launcher, CancellationToken cancellation) {
            // Validate before closing the game: a stale path must never leave it closed.
            if (restart && !runtime.LauncherExists(launcher)) throw new InvalidOperationException("找不到 Battle.net.exe，请重新选择战网启动器。");
            cancellation.ThrowIfCancellationRequested();
            bool wasRunning = runtime.IsRunning();
            if (wasRunning) runtime.CloseTree();
            int checks = 0;
            while (runtime.IsRunning()) {
                if (checks++ >= 10) throw new InvalidOperationException("炉石未能在 5 秒内退出，已停止操作。");
                runtime.Delay(500, cancellation);
            }
            cancellation.ThrowIfCancellationRequested();
            if (!restart) return wasRunning ? "炉石已关闭。" : "炉石未运行，无需关闭。";
            if (wasRunning) runtime.Delay(3000, cancellation);
            for (int attempt = 0; attempt < 6; attempt++) {
                cancellation.ThrowIfCancellationRequested();
                if (runtime.IsRunning()) return "已检测到炉石进程。";
                runtime.Launch(launcher);
                for (int check = 0; check < 4; check++) {
                    runtime.Delay(500, cancellation);
                    cancellation.ThrowIfCancellationRequested();
                    if (runtime.IsRunning()) return "已检测到炉石进程，游戏是否进入对局请以画面为准。";
                }
            }
            throw new InvalidOperationException("已发送 6 次启动请求，仍未检测到炉石。请检查战网是否已登录、是否正在更新。不会继续重试。");
        }
    }
    public sealed class WindowsGameRuntime : IGameRuntime {
        public bool IsRunning() {
            var processes = Process.GetProcessesByName("Hearthstone");
            try { return processes.Length != 0; }
            finally { foreach (var process in processes) process.Dispose(); }
        }
        public bool LauncherExists(string path) {
            try { return !String.IsNullOrWhiteSpace(path) && String.Equals(Path.GetFileName(path), "Battle.net.exe", StringComparison.OrdinalIgnoreCase) && File.Exists(path); }
            catch (ArgumentException) { return false; }
        }
        public void CloseTree() {
            using (var command = Process.Start(new ProcessStartInfo {
                FileName = Path.Combine(Environment.SystemDirectory, "taskkill.exe"), Arguments = "/F /T /IM Hearthstone.exe",
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden
            })) {
                if (command == null) throw new InvalidOperationException("无法启动炉石关闭命令。");
                if (!command.WaitForExit(5000)) {
                    try { command.Kill(); } catch (InvalidOperationException) { }
                    throw new InvalidOperationException("关闭命令超时，已停止操作。");
                }
                if (command.ExitCode != 0 && IsRunning()) throw new InvalidOperationException("关闭炉石失败，请检查进程权限。");
            }
        }
        public void Launch(string path) {
            using (var launcher = Process.Start(new ProcessStartInfo {
                FileName = path, WorkingDirectory = Path.GetDirectoryName(path), Arguments = "--exec=\"launch WTCG\"", UseShellExecute = false
            })) {
                if (launcher == null) throw new InvalidOperationException("无法向战网发送启动请求。");
            }
        }
        public void Delay(int milliseconds, CancellationToken cancellation) {
            for (int remaining = milliseconds; remaining > 0; remaining -= 100) {
                cancellation.ThrowIfCancellationRequested(); Thread.Sleep(Math.Min(100, remaining));
            }
            cancellation.ThrowIfCancellationRequested();
        }
    }
    public static class LauncherPreferences {
        public static string Load(string path) {
            try {
                if (File.Exists(path) && new FileInfo(path).Length <= 4096) return File.ReadAllText(path).Trim();
            } catch (IOException) { } catch (UnauthorizedAccessException) { }
            return null;
        }
        public static bool Save(string path, string launcher) {
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
                File.WriteAllText(temporary, launcher);
                if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
                return true;
            } catch (IOException) { return false; } catch (UnauthorizedAccessException) { return false; }
            finally { try { if (File.Exists(temporary)) File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        }
    }
}

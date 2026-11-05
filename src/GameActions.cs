using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace HsReconnectLite {
    public interface IGameRuntime {
        bool IsRunning();
        bool LauncherExists(string path);
        void CloseTree();
        void Launch(string path);
        void Delay(int milliseconds, CancellationToken cancellation);
        bool IsGameWindowReady();
        bool RequestLauncherClose(string path);
    }
    public interface IGameExitCleanup { void CleanupAfterExit(CancellationToken cancellation); }
    public interface IGameLaunchProgress { bool HasLaunchStarted(); }
    public sealed class GameActions {
        readonly IGameRuntime runtime;
        readonly string gameName;
        readonly int launchAttempts, startupChecks, windowChecks;
        readonly Action<string> trace;
        public GameActions(IGameRuntime runtime, string name = "炉石", int launchAttempts = 6, int startupChecks = 4, int windowChecks = 20, Action<string> trace = null) {
            this.runtime = runtime; gameName = name; this.launchAttempts = launchAttempts; this.startupChecks = startupChecks; this.windowChecks = windowChecks;
            this.trace = trace;
        }
        void Record(string message) { if (trace != null) trace(gameName + ": " + message); }
        public string Execute(bool restart, string launcher, CancellationToken cancellation, bool startOnly = false) {
            try { return ExecuteCore(restart, launcher, cancellation, startOnly).Replace("炉石", gameName); }
            catch (InvalidOperationException ex) {
                if (gameName == "炉石") throw;
                throw new InvalidOperationException(ex.Message.Replace("炉石", gameName), ex);
            }
        }
        string ExecuteCore(bool restart, string launcher, CancellationToken cancellation, bool startOnly) {
            // Validate before closing the game: a stale path must never leave it closed.
            if (restart && !runtime.LauncherExists(launcher)) throw new InvalidOperationException("找不到 Battle.net.exe，请重新选择战网启动器。");
            cancellation.ThrowIfCancellationRequested();
            bool wasRunning = runtime.IsRunning();
            if (startOnly && wasRunning) return CompleteRestart(launcher, cancellation);
            if (wasRunning) runtime.CloseTree();
            int checks = 0;
            while (runtime.IsRunning()) {
                if (checks++ >= 10) throw new InvalidOperationException("炉石未能在 5 秒内退出，已停止操作。");
                runtime.Delay(500, cancellation);
            }
            cancellation.ThrowIfCancellationRequested();
            var cleanup = runtime as IGameExitCleanup;
            if (cleanup != null) { Record("cleanup begin"); cleanup.CleanupAfterExit(cancellation); Record("cleanup complete"); }
            cancellation.ThrowIfCancellationRequested();
            if (!restart) return wasRunning ? "炉石已关闭。" : "炉石未运行，无需关闭。";
            if (wasRunning) runtime.Delay(3000, cancellation);
            for (int attempt = 0; attempt < launchAttempts; attempt++) {
                cancellation.ThrowIfCancellationRequested();
                if (LaunchReceived()) return CompleteLaunchRequest(launcher, cancellation);
                if (runtime.IsRunning()) return CompleteRestart(launcher, cancellation);
                Record("launch request " + (attempt + 1) + "/" + launchAttempts);
                runtime.Launch(launcher);
                for (int check = 0; check < startupChecks; check++) {
                    runtime.Delay(500, cancellation);
                    cancellation.ThrowIfCancellationRequested();
                    if (LaunchReceived()) return CompleteLaunchRequest(launcher, cancellation);
                    if (runtime.IsRunning()) return CompleteRestart(launcher, cancellation);
                }
            }
            throw new InvalidOperationException("已发送 " + launchAttempts + " 次启动请求，仍未检测到炉石。请检查战网是否已登录、是否正在更新。不会继续重试。");
        }
        bool LaunchReceived() { var progress = runtime as IGameLaunchProgress; return progress != null && progress.HasLaunchStarted(); }
        string CompleteLaunchRequest(string launcher, CancellationToken cancellation) {
            cancellation.ThrowIfCancellationRequested();
            Record("OW/NEAC launch receipt confirmed; release wait without requiring game window");
            try { runtime.RequestLauncherClose(launcher); }
            catch (ArgumentException) { } catch (Win32Exception) { } catch (InvalidOperationException) { }
            return "已检测到 OW 启动流程；完成授权后等待游戏启动。";
        }
        string CompleteRestart(string launcher, CancellationToken cancellation) {
            if (runtime is IGameLaunchProgress) return CompleteLaunchRequest(launcher, cancellation);
            // Wait only during this restart. Do not close the launcher while login or
            // updates might still require attention, or if the game exits again.
            for (int check = 0; check < windowChecks; check++) {
                cancellation.ThrowIfCancellationRequested();
                if (!runtime.IsRunning()) throw new InvalidOperationException("炉石在启动期间退出，已保留战网窗口。");
                bool ready;
                try { ready = runtime.IsGameWindowReady(); }
                catch (Win32Exception) { break; }
                catch (InvalidOperationException) { break; }
                if (ready) {
                    cancellation.ThrowIfCancellationRequested();
                    bool sent = false;
                    try { sent = runtime.RequestLauncherClose(launcher); }
                    catch (ArgumentException) { /* Launcher exited before PID revalidation. */ }
                    catch (Win32Exception) { }
                    catch (InvalidOperationException) { }
                    return sent ? "炉石窗口已出现，已向战网主窗口发送关闭请求。" : "炉石窗口已出现；如战网仍显示，可手动点叉收起。";
                }
                runtime.Delay(500, cancellation);
            }
            return "已检测到炉石进程，但尚未确认游戏窗口，已保留战网窗口。";
        }
    }
    public static class LauncherWindowPolicy {
        public static bool Matches(string executable, string configured, string title, bool visible, bool owned) {
            if (!visible || owned || String.IsNullOrWhiteSpace(executable) || String.IsNullOrWhiteSpace(configured)) return false;
            if (!String.Equals(title, "战网", StringComparison.OrdinalIgnoreCase) && !String.Equals(title, "Battle.net", StringComparison.OrdinalIgnoreCase)
                && !String.Equals(title, "Blizzard Battle.net", StringComparison.OrdinalIgnoreCase)) return false;
            try {
                return String.Equals(Path.GetFileName(executable), "Battle.net.exe", StringComparison.OrdinalIgnoreCase)
                    && String.Equals(Path.GetFullPath(executable), Path.GetFullPath(configured), StringComparison.OrdinalIgnoreCase);
            } catch (ArgumentException) { return false; } catch (IOException) { return false; } catch (NotSupportedException) { return false; }
        }
    }
    public class WindowsGameRuntime : IGameRuntime {
        readonly string processName, productCode;
        public WindowsGameRuntime(string processName = "Hearthstone", string productCode = "WTCG") {
            if (!((processName == "Hearthstone" && productCode == "WTCG") || (processName == "Overwatch" && productCode == "Pro"))) throw new ArgumentException("Unknown game");
            this.processName = processName; this.productCode = productCode;
        }
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr window, uint command);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
        [DllImport("user32.dll", SetLastError = true)] static extern bool PostMessage(IntPtr window, uint message, IntPtr wparam, IntPtr lparam);
        public bool IsGameWindowReady() {
            var games = Process.GetProcessesByName(processName);
            try {
                if (games.Length != 1) return false;
                // Protected OW processes can deny HasExited's broader process access.
                // Window ownership and visibility confirm readiness without opening that handle.
                IntPtr window = games[0].MainWindowHandle;
                if (window == IntPtr.Zero || !IsWindowVisible(window)) return false;
                uint owner; GetWindowThreadProcessId(window, out owner);
                return owner == (uint)games[0].Id;
            }
            finally { foreach (var game in games) game.Dispose(); }
        }
        public bool RequestLauncherClose(string path) {
            IntPtr target = IntPtr.Zero; int targetPid = 0;
            var launchers = Process.GetProcessesByName("Battle.net");
            try {
                foreach (var launcher in launchers) {
                    try {
                        IntPtr window = launcher.MainWindowHandle;
                        if (window == IntPtr.Zero || !LauncherWindowPolicy.Matches(launcher.MainModule.FileName, path, launcher.MainWindowTitle,
                            IsWindowVisible(window), GetWindow(window, 4) != IntPtr.Zero)) continue;
                        if (target != IntPtr.Zero) return false; // Ambiguous: do not close multiple windows.
                        target = window; targetPid = launcher.Id;
                    } catch (Win32Exception) { } catch (InvalidOperationException) { }
                }
                if (target == IntPtr.Zero) return false;
                uint currentPid; GetWindowThreadProcessId(target, out currentPid);
                if (currentPid != (uint)targetPid || !IsWindowVisible(target)) return false;
                using (var current = Process.GetProcessById(targetPid)) {
                    if (current.MainWindowHandle != target) return false;
                    if (!LauncherWindowPolicy.Matches(current.MainModule.FileName, path, current.MainWindowTitle, true, GetWindow(target, 4) != IntPtr.Zero)) return false;
                    uint verifiedPid; GetWindowThreadProcessId(target, out verifiedPid);
                    if (verifiedPid != currentPid) return false;
                    return PostMessage(target, 0x0010, IntPtr.Zero, IntPtr.Zero); // WM_CLOSE, same request as the window's X button.
                }
            } finally { foreach (var launcher in launchers) launcher.Dispose(); }
        }
        public virtual bool IsRunning() {
            var processes = Process.GetProcessesByName(processName);
            try { return processes.Length != 0; }
            finally { foreach (var process in processes) process.Dispose(); }
        }
        public bool LauncherExists(string path) {
            try { return !String.IsNullOrWhiteSpace(path) && String.Equals(Path.GetFileName(path), "Battle.net.exe", StringComparison.OrdinalIgnoreCase) && File.Exists(path); }
            catch (ArgumentException) { return false; }
        }
        public virtual void CloseTree() {
            using (var command = Process.Start(new ProcessStartInfo {
                FileName = Path.Combine(Environment.SystemDirectory, "taskkill.exe"), Arguments = "/F /T /IM " + processName + ".exe",
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
                FileName = path, WorkingDirectory = Path.GetDirectoryName(path), Arguments = "--exec=\"launch " + productCode + "\"", UseShellExecute = false
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

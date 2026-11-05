using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace HsReconnectLite {
    public interface IGameProcessControl {
        int[] Find(string name);
        bool Matches(int pid, string expectedPath);
        void Terminate(int pid, string expectedPath);
    }
    public sealed class GameProcessControl : IGameProcessControl {
        [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder name, ref int length);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool TerminateProcess(IntPtr process, uint code);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetExitCodeProcess(IntPtr process, out uint code);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
        public int[] Find(string name) {
            var processes = Process.GetProcessesByName(name);
            try { var ids = new int[processes.Length]; for (int i = 0; i < processes.Length; i++) ids[i] = processes[i].Id; return ids; }
            finally { foreach (var process in processes) process.Dispose(); }
        }
        static string Image(IntPtr handle) {
            var text = new StringBuilder(32768); int length = text.Capacity;
            if (!QueryFullProcessImageName(handle, 0, text, ref length)) throw new Win32Exception(Marshal.GetLastWin32Error());
            return text.ToString();
        }
        static bool SamePath(string actual, string expected) { return String.Equals(Path.GetFullPath(actual), Path.GetFullPath(expected), StringComparison.OrdinalIgnoreCase); }
        static bool Exited(IntPtr handle) { uint code; return GetExitCodeProcess(handle, out code) && code != 259; }
        public bool Matches(int pid, string expectedPath) {
            IntPtr handle = OpenProcess(0x1000, false, (uint)pid);
            if (handle == IntPtr.Zero) { int error = Marshal.GetLastWin32Error(); if (error == 87) return false; throw new Win32Exception(error, "无法确认游戏相关进程的路径，请以管理员权限运行 HRL。"); }
            try {
                if (Exited(handle)) return false;
                try { return SamePath(Image(handle), expectedPath); }
                catch (Win32Exception) { if (Exited(handle)) return false; throw; }
            }
            finally { CloseHandle(handle); }
        }
        public void Terminate(int pid, string expectedPath) {
            // Query and terminate the same process handle: PID reuse cannot redirect the kill.
            IntPtr handle = OpenProcess(0x1001, false, (uint)pid);
            if (handle == IntPtr.Zero) { int error = Marshal.GetLastWin32Error(); if (error == 87) return; throw new Win32Exception(error, "无法关闭游戏相关进程，请以管理员权限运行 HRL。"); }
            try { TerminateHandle(handle, expectedPath); }
            finally { CloseHandle(handle); }
        }
        static void TerminateHandle(IntPtr handle, string expectedPath) {
            if (Exited(handle)) return;
            try { if (!SamePath(Image(handle), expectedPath)) throw new InvalidOperationException("进程身份已经变化，已停止操作。"); }
            catch (Win32Exception) { if (Exited(handle)) return; throw; }
            if (!TerminateProcess(handle, 1)) {
                int error = Marshal.GetLastWin32Error();
                if (Exited(handle)) return; // A concurrent normal exit can return ERROR_ACCESS_DENIED.
                throw new Win32Exception(error, "游戏相关进程未能关闭。");
            }
        }
    }
    public sealed class OverwatchRuntime : WindowsGameRuntime, IGameExitCleanup, IGameLaunchProgress {
        readonly string gamePath, directory;
        readonly IGameProcessControl processes;
        static readonly string[] companions = { "OWNeacClient", "NeacLoader" };
        public OverwatchRuntime(string gamePath, IGameProcessControl processes = null) : base("Overwatch", "Pro") {
            if (!String.Equals(Path.GetFileName(gamePath), "Overwatch.exe", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("请选择 Overwatch.exe。");
            this.gamePath = Path.GetFullPath(gamePath); directory = Path.GetDirectoryName(this.gamePath);
            this.processes = processes ?? new GameProcessControl();
        }
        public override bool IsRunning() { return processes.Find("Overwatch").Length != 0; }
        public bool HasLaunchStarted() {
            foreach (int pid in processes.Find("Overwatch")) if (processes.Matches(pid, gamePath)) return true;
            foreach (string name in companions) foreach (int pid in processes.Find(name))
                if (processes.Matches(pid, Path.Combine(directory, name + ".exe"))) return true;
            return false;
        }
        public override void CloseTree() {
            foreach (int pid in processes.Find("Overwatch")) {
                if (!processes.Matches(pid, gamePath)) {
                    if (Array.IndexOf(processes.Find("Overwatch"), pid) < 0) continue;
                    throw new InvalidOperationException("发现不同安装目录的 OW，已停止关闭操作。");
                }
                processes.Terminate(pid, gamePath);
            }
        }
        public void CleanupAfterExit(CancellationToken cancellation) {
            cancellation.ThrowIfCancellationRequested();
            if (IsRunning()) throw new InvalidOperationException("OW 尚未退出，不会单独关闭 NEAC。");
            foreach (string name in companions) {
                string expected = Path.Combine(directory, name + ".exe");
                foreach (int pid in processes.Find(name)) {
                    cancellation.ThrowIfCancellationRequested();
                    if (IsRunning()) throw new InvalidOperationException("OW 已重新运行，已停止清理 NEAC。");
                    if (processes.Matches(pid, expected)) processes.Terminate(pid, expected);
                }
            }
            for (int check = 0; check <= 20; check++) {
                cancellation.ThrowIfCancellationRequested();
                if (IsRunning()) throw new InvalidOperationException("OW 已重新运行，已停止等待 NEAC 退出。");
                bool remaining = false;
                foreach (string name in companions) foreach (int pid in processes.Find(name))
                    if (processes.Matches(pid, Path.Combine(directory, name + ".exe"))) remaining = true;
                if (!remaining) return;
                if (check == 20) throw new InvalidOperationException("OW 的 NEAC 进程未能退出，已停止启动，避免战网一直显示正在游戏中。");
                Delay(500, cancellation);
            }
        }
    }
}

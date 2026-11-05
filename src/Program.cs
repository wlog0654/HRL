using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace HsReconnectLite {
    static class Program {
        public static readonly string Root = AppDomain.CurrentDomain.BaseDirectory;
        public static readonly string DataRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HsReconnectLite");
        public static readonly string LogPath = Path.Combine(DataRoot, "reconnect-lite.log");
        static readonly object logGate = new object();
        public static void Log(string message) {
            lock (logGate) {
                try {
                    Directory.CreateDirectory(DataRoot);
                    if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 1024 * 1024) {
                        if (File.Exists(LogPath + ".old")) File.Delete(LogPath + ".old");
                        File.Move(LogPath, LogPath + ".old");
                    }
                    File.AppendAllText(LogPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss ") + message + Environment.NewLine, Encoding.UTF8);
                } catch { }
            }
        }
        static bool IsAdmin() {
            using (var id = WindowsIdentity.GetCurrent()) return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }
        [STAThread]
        static int Main(string[] args) {
            DesktopUi.InitializeDpi();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            bool inspect = args.Contains("--inspect"), idleProbe = args.Contains("--idle-probe");
            bool smoke = args.Contains("--smoke") || idleProbe;
            if (!inspect && !smoke && !IsAdmin()) {
                try { Process.Start(new ProcessStartInfo(Application.ExecutablePath) { UseShellExecute = true, Verb = "runas", WorkingDirectory = Root }); return 0; }
                catch (Win32Exception) { return 1; }
            }
            try {
                if (inspect) { Inspect(); return 0; }
                bool created;
                using (var mutex = new Mutex(true, smoke ? "Local\\HsReconnectLite-Smoke-" + Process.GetCurrentProcess().Id : "Local\\HsReconnectLite-v1", out created)) {
                    if (!created) { MessageBox.Show("HRL已在托盘运行。", "HRL"); return 1; }
                    using (var context = new TrayContext(smoke, idleProbe)) Application.Run(context);
                }
                return 0;
            } catch (Exception ex) {
                Log("ERROR " + ex);
                if (!inspect && !smoke) MessageBox.Show(ex.Message, "HRL", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }
        public static List<Connection> Current(out uint pid, out string logIp, out int logPort) {
            pid = 0; logIp = null; logPort = 0;
            var processes = Process.GetProcessesByName("Hearthstone");
            try {
                if (processes.Length == 0) throw new InvalidOperationException("炉石未运行。请先打开游戏。");
                if (processes.Length != 1) throw new InvalidOperationException("发现多个炉石进程，无法唯一确定目标。");
                var game = processes[0]; pid = (uint)game.Id;
                // Optional hint only. Actual connection enumeration is always fresh.
                try {
                    string logs = Path.Combine(Path.GetDirectoryName(game.MainModule.FileName), "Logs");
                    var newest = new DirectoryInfo(logs).GetDirectories().Where(d => d.CreationTimeUtc >= game.StartTime.ToUniversalTime().AddSeconds(-10))
                        .OrderByDescending(d => d.CreationTimeUtc).FirstOrDefault();
                    if (newest != null) {
                        foreach (string name in new[] { "GameNetLogger.log", "Hearthstone.log" }) {
                            string path = Path.Combine(newest.FullName, name);
                            if (!File.Exists(path)) continue;
                            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) {
                                // Read a bounded tail: long sessions must not load huge logs into memory.
                                stream.Seek(Math.Max(0, stream.Length - 262144), SeekOrigin.Begin);
                                using (var reader = new StreamReader(stream)) {
                                    var hits = Regex.Matches(reader.ReadToEnd(), @"Network\.GotoGameServe\(\).*?address=\s*([0-9.]+):(\d+)");
                                    if (hits.Count > 0) {
                                        var last = hits[hits.Count - 1]; logIp = last.Groups[1].Value;
                                        int.TryParse(last.Groups[2].Value, out logPort); break;
                                    }
                                }
                            }
                        }
                    }
                } catch (Exception ex) { Log("Optional log hint unavailable: " + ex.Message); }
                uint targetPid = pid;
                return NativeTcp.Read().Where(r => r.ProcessId == targetPid).ToList();
            } finally { foreach (var process in processes) process.Dispose(); }
        }
        public static void Inspect() {
            var text = new StringBuilder();
            text.AppendLine("只读连接诊断 / " + DateTime.Now.ToString("s"));
            text.AppendLine("管理员: " + IsAdmin());
            try {
                uint pid; string ip; int port;
                var rows = Current(out pid, out ip, out port);
                text.AppendLine("日志提示: " + (ip ?? "无") + ":" + port);
                foreach (var row in rows) text.AppendLine(row + " state=" + row.State);
                var selected = ConnectionSelector.Select(rows, pid, ip, port);
                text.AppendLine("候选目标: " + (selected == null ? "无唯一可靠目标，拒绝拔线" : selected.ToString()));
            } catch (Exception ex) { text.AppendLine(ex.Message); }
            Directory.CreateDirectory(DataRoot);
            string path = Path.Combine(DataRoot, "diagnostics.txt");
            File.WriteAllText(path, text.ToString(), Encoding.UTF8);
            Log("Read-only diagnostics saved: " + path);
        }
    }
    sealed class TrayContext : ApplicationContext {
        readonly NotifyIcon tray;
        readonly HotkeyRegistration hotkey;
        HotkeyDialog hotkeyDialog;
        readonly ContextMenuStrip menu;
        readonly System.Windows.Forms.Timer timer;
        readonly FloatingButton overlay;
        readonly ToolStripMenuItem overlayToggle;
        readonly OverlayPreferences preferences;
        readonly Action disconnectOperation;
        readonly IGameRuntime gameRuntime;
        readonly GameActions gameActions;
        readonly CancellationTokenSource actionStop = new CancellationTokenSource();
        readonly ToolStripItem closeGame, restartGame, startOverwatch, restartOverwatch;
        readonly IGameRuntime overwatchRuntimeOverride;
        bool selectingLauncher;
        readonly bool smoke;
        readonly string preferencesPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HsReconnectLite", "overlay.ini");
        readonly Stopwatch clock = Stopwatch.StartNew();
        long lastAttempt = -3000;
        bool pending;
        volatile bool exiting;
        Icon trayIcon;
        int trayIconPixels;
        public TrayContext(bool smoke, bool idleProbe, Action operation = null, IGameRuntime runtime = null, IGameRuntime owRuntime = null) {
            this.smoke = smoke;
            disconnectOperation = operation ?? CloseCurrentConnection;
            gameRuntime = runtime ?? new WindowsGameRuntime();
            gameActions = new GameActions(gameRuntime);
            overwatchRuntimeOverride = owRuntime;
            preferences = smoke ? new OverlayPreferences() : OverlayPreferences.Load(preferencesPath);
            menu = new ContextMenuStrip();
            menu.Items.Add("拔线一次", null, delegate { Reconnect(); });
            menu.Items.Add("设置快捷键…", null, delegate { ConfigureHotkey(); });
            overlayToggle = new ToolStripMenuItem("显示悬浮按钮") { Checked = preferences.Enabled };
            overlayToggle.Click += delegate { SetOverlayVisible(!preferences.Enabled); };
            menu.Items.Add(overlayToggle);
            menu.Items.Add(new ToolStripSeparator());
            closeGame = menu.Items.Add("只关闭炉石（强制）", null, delegate { GameCommand(false); });
            restartGame = menu.Items.Add("重启炉石", null, delegate { GameCommand(true); });
            menu.Items.Add(new ToolStripSeparator());
            startOverwatch = menu.Items.Add("启动 OW", null, delegate { OverwatchCommand(false); });
            restartOverwatch = menu.Items.Add("重启 OW", null, delegate { OverwatchCommand(true); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("保存连接诊断", null, delegate {
                try { Program.Inspect(); Notify("诊断已保存：" + Path.Combine(Program.DataRoot, "diagnostics.txt"), false); }
                catch (Exception ex) { Notify(ex.Message, true); }
            });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出", null, delegate { ExitThread(); });
            tray = new NotifyIcon();
            RefreshTrayIcon(); tray.Visible = true;
            tray.MouseUp += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Right) ShowMenu(Control.MousePosition); };
            overlay = new FloatingButton(Reconnect, SavePreferences, menu, preferences.Position);
            overlay.MenuRequested = ShowMenu; overlay.DisplayChangedCallback = RefreshTrayIcon;
            if (preferences.Enabled) overlay.Show();
            hotkey = new HotkeyRegistration(Reconnect);
            string hotkeyError;
            if (!hotkey.TryChange(preferences.Hotkey, null, out hotkeyError)) {
                Program.Log("HOTKEY UNAVAILABLE " + hotkeyError);
                if (!smoke) Notify(hotkeyError + " 可右键设置其他快捷键，或点击悬浮按钮。", true);
            }
            RefreshHotkeyText();
            Program.Log("START floating=" + preferences.Enabled + " hotkey=" + (hotkey.Current != null) + " admin=" + new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator));
            if (smoke) {
                timer = new System.Windows.Forms.Timer { Interval = idleProbe ? 7000 : 1500 };
                timer.Tick += delegate { Program.Log("SMOKE PASS: tray and floating button started; no disconnect invoked."); ExitThread(); };
                timer.Start();
            } else if (hotkey.Current != null) Notify("点击悬浮按钮或按 " + hotkey.Current + " 拔线；右键可设置快捷键。", false);
        }
        void RefreshHotkeyText() {
            string binding = hotkey.Current == null ? "快捷键未启用" : hotkey.Current.ToString();
            menu.Items[0].Text = "拔线一次\t" + binding;
            tray.Text = "HRL · " + binding;
        }
        void ConfigureHotkey() {
            if (exiting) return;
            if (hotkeyDialog != null) { hotkeyDialog.Activate(); return; }
            hotkey.Suppressed = true;
            try {
                using (var dialog = new HotkeyDialog(preferences.Hotkey, ApplyHotkey)) {
                    hotkeyDialog = dialog; dialog.ShowDialog();
                }
            } finally { hotkeyDialog = null; hotkey.Suppressed = false; }
        }
        string ApplyHotkey(HotkeyBinding candidate) {
            string error;
            bool changed = hotkey.TryChange(candidate, delegate {
                var previous = preferences.Hotkey;
                preferences.Hotkey = candidate;
                preferences.Position = overlay.Location;
                if (smoke || preferences.Save(preferencesPath)) return true;
                preferences.Hotkey = previous; return false;
            }, out error);
            if (changed) RefreshHotkeyText();
            return changed ? null : error;
        }
        void RefreshTrayIcon() {
            if (exiting) return;
            int size = DesktopUi.IconPixels(DesktopUi.TrayDpi);
            if (size == trayIconPixels && trayIcon != null) return;
            Icon replacement = Branding.LoadIcon(size), previous = trayIcon;
            tray.Icon = replacement; trayIcon = replacement; trayIconPixels = size;
            if (previous != null) previous.Dispose();
        }
        void ShowMenu(Point point) {
            if (exiting) return;
            try { RefreshTrayIcon(); NativeContextMenu.Show(menu, point); }
            catch (Exception ex) { Program.Log("MENU FAILED " + ex.Message); Notify("菜单暂时无法打开，请重试。", true); }
        }
        void SetOverlayVisible(bool visible) {
            preferences.Enabled = visible; overlayToggle.Checked = visible;
            if (visible) { overlay.Location = overlay.KeepOnScreen(overlay.Location); overlay.Show(); }
            else overlay.Hide();
            SavePreferences();
        }
        void SavePreferences() {
            if (smoke || overlay == null || overlay.IsDisposed) return;
            preferences.Position = overlay.Location;
            if (!preferences.Save(preferencesPath)) Program.Log("Could not save overlay preferences; current session remains usable.");
        }
        void Notify(string message, bool error) {
            tray.BalloonTipTitle = error ? "操作未执行或失败" : "HRL";
            tray.BalloonTipText = message;
            tray.BalloonTipIcon = error ? ToolTipIcon.Warning : ToolTipIcon.Info;
            tray.ShowBalloonTip(3500);
        }
        void Reconnect() {
            StartOperation(delegate { disconnectOperation(); return "已关闭对局连接，等待游戏自行重连。"; }, "已拔");
        }
        void GameCommand(bool restart) {
            if (!CanOperate()) return;
            string launcher = null;
            if (restart && !TryProgramPath("battle-net-path.txt", "Battle.net.exe", gameRuntime.LauncherExists, out launcher)) return;
            if (!CanOperate()) return;
            var token = actionStop.Token;
            StartOperation(delegate { return gameActions.Execute(restart, launcher, token); }, restart ? "已启" : "已关");
        }
        void OverwatchCommand(bool restart) {
            if (!CanOperate()) return;
            IGameRuntime runtime = overwatchRuntimeOverride;
            if (runtime == null) {
                string game;
                if (!TryProgramPath("overwatch-path.txt", "Overwatch.exe", delegate(string path) {
                    try { return File.Exists(path) && String.Equals(Path.GetFileName(path), "Overwatch.exe", StringComparison.OrdinalIgnoreCase); }
                    catch (ArgumentException) { return false; }
                }, out game)) return;
                runtime = new OverwatchRuntime(game);
            }
            string launcher;
            if (!TryProgramPath("battle-net-path.txt", "Battle.net.exe", runtime.LauncherExists, out launcher) || !CanOperate()) return;
            var token = actionStop.Token;
            var actions = new GameActions(runtime, "守望先锋", 1, 120, 60);
            StartOperation(delegate { return actions.Execute(true, launcher, token, !restart); }, "已启");
        }
        bool TryProgramPath(string configName, string fileName, Func<string, bool> validate, out string path) {
            string config = Path.Combine(Program.DataRoot, configName);
            path = LauncherPreferences.Load(config);
            if (!validate(path)) {
                selectingLauncher = true; hotkey.Suppressed = true;
                try {
                    using (var picker = new OpenFileDialog { Title = "选择 " + fileName, Filter = fileName + "|" + fileName, CheckFileExists = true }) {
                        if (picker.ShowDialog() != DialogResult.OK) return false;
                        path = picker.FileName;
                        if (!validate(path)) { Notify("请选择有效的 " + fileName + "。", true); return false; }
                        if (!LauncherPreferences.Save(config, path)) { Notify("无法保存程序路径，未执行游戏操作。", true); return false; }
                    }
                } finally { selectingLauncher = false; hotkey.Suppressed = false; }
            }
            return !exiting;
        }
        bool CanOperate() {
            return !exiting && hotkeyDialog == null && !selectingLauncher && !pending && clock.ElapsedMilliseconds - lastAttempt >= 2000;
        }
        void StartOperation(Func<string> operation, string feedback) {
            if (!CanOperate()) return;
            lastAttempt = clock.ElapsedMilliseconds;
            pending = true; SetOperationMenus(false); overlay.SetFeedback("稍候", true, false);
            Task.Run(delegate {
                Exception failure = null;
                string result = null;
                try { result = operation(); }
                catch (Exception ex) { failure = ex; Program.Log("FAILED " + ex.Message); }
                if (exiting) return;
                try {
                    // Explicit dispatch also works when a native menu or nested message loop
                    // has changed SynchronizationContext.Current.
                    overlay.BeginInvoke((Action)delegate { CompleteOperation(failure, result, feedback); });
                } catch (InvalidOperationException) { /* Window closed while operation finished. */ }
            });
        }
        void SetOperationMenus(bool enabled) { menu.Items[0].Enabled = enabled; closeGame.Enabled = enabled; restartGame.Enabled = enabled; startOverwatch.Enabled = enabled; restartOverwatch.Enabled = enabled; }
        void CompleteOperation(Exception failure, string result, string feedback) {
            if (exiting) return;
            try {
                if (failure == null) { overlay.SetFeedback(feedback, false, false); Notify(result, false); }
                else { overlay.SetFeedback("失败", false, true); Notify(failure.Message, true); }
            } finally { pending = false; SetOperationMenus(true); }
        }
        static void CloseCurrentConnection() {
            uint pid; string ip; int port;
            var rows = Program.Current(out pid, out ip, out port);
            var target = ConnectionSelector.Select(rows, pid, ip, port);
            Program.Log("TRIGGER logHint=" + (ip ?? "none") + ":" + port + " connections=" + String.Join("; ", rows.Select(r => r.ToString())));
            if (target == null) throw new InvalidOperationException("未找到唯一对局连接。请在对局中使用；可右键保存连接诊断。");
            using (var game = Process.GetProcessById((int)pid))
                if (!String.Equals(game.ProcessName, "Hearthstone", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("游戏进程已变化，请重试。");
            NativeTcp.Close(target);
            Program.Log("TCP CLOSED " + target);
        }
        protected override void ExitThreadCore() {
            if (exiting) return;
            exiting = true;
            actionStop.Cancel(); actionStop.Dispose();
            if (timer != null) { timer.Stop(); timer.Dispose(); }
            if (hotkeyDialog != null) hotkeyDialog.Close();
            SavePreferences(); overlay.Dispose();
            if (hotkey != null) hotkey.Dispose();
            tray.Visible = false; tray.Dispose(); menu.Dispose();
            if (trayIcon != null) { trayIcon.Dispose(); trayIcon = null; }
            Program.Log("EXIT"); base.ExitThreadCore();
        }
    }
}

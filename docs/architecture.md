# 实现与验证范围

程序入口负责单实例、按需 UAC 提权和只读诊断；`TrayContext` 将悬浮窗、托盘与快捷键汇入同一个异步操作，并进行防重入与两秒防连按。进程、连接及游戏日志线索每次重新读取。

`ConnectionSelector` 只选择唯一可靠的对局连接，`NativeTcp` 使用 IP Helper API 枚举 IPv4 TCP 表并通过 `SetTcpEntry(DELETE_TCB)` 关闭目标。拔线操作不会注入 DLL、读取游戏内存、修改防火墙或结束游戏进程。IPv6、UDP 与特殊代理拓扑不在当前实现范围内。

`HotkeyBinding` 限定允许的按键类型；`HotkeyRegistration` 通过消息窗口注册全局快捷键，两个 ID 轮换，先登记候选、保存设置，成功后释放旧注册。失败则释放候选、保留旧注册。录入窗口期间屏蔽回调，并检查消息键值以忽略旧消息。

Windows 会先分发已注册的快捷键，普通窗口的按键事件无法完整覆盖冲突录入。因此 `HotkeyCapture` 仅在录入窗口前台激活时安装临时低级键盘钩子，拦截支持的主键，并将后续注册、保存排入 UI 消息队列；失焦或关闭立即卸载。常规运行没有键盘钩子。退出与异步结果交错时先检查退出状态，界面结果使用 `BeginInvoke` 明确返回窗口线程。

配置文件用同目录临时文件加原子替换，兼容缺少快捷键字段的旧配置。运行日志有锁和体积轮换，读取游戏日志只取最多 256 KiB 的尾部。

`FloatingButton` 使用逐像素透明窗口，鼠标点击不激活游戏外窗口，拖动使用系统阈值；显示器变化与 DPI 变化时重新定位和绘制。菜单使用 Windows 原生弹出菜单，资源按使用周期释放。

## 自动验证

| 测试 | 验证范围 |
| --- | --- |
| SelectionTests | 旧日志、目标 PID、登录端口、多候选与不可靠目标拒绝 |
| NativeTests | 自建本地套接字的 PID、地址、端口和状态枚举 |
| HotkeyTests | 按键规范、实际 Windows 注册冲突、失败回退、释放注册、持久化与录入 |
| HotkeyCaptureTests | 真实系统输入、已占用组合键录入、防误触其他程序及关闭/失焦释放捕获 |
| OverlayTests | 拖动阈值、负坐标、显示器位置恢复与配置损坏恢复 |
| OverlayWindowTests | 实际悬浮窗交互、拖动、忙碌反馈与模拟 DPI 变化 |
| TrayIntegrationTests | 共享防重入、后台执行、错误处理与显隐 |
| DesktopUiTests | 图标尺寸、菜单状态及 USER / GDI 资源释放 |
| MenuVisualTests | 实际原生菜单打开、截图和取消 |

CI 明确跳过五组需要交互桌面的测试。测试通过不能代替真实对局兼容性验证；新版本不自动触发真实游戏拔线。不同物理显示器间的拖动、全部 Windows / 区服 / 代理组合仍需用户反馈。

API 依据：[RegisterHotKey](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey)、[WM_HOTKEY](https://learn.microsoft.com/en-us/windows/win32/inputdev/wm-hotkey)。F12 为调试保留键，因此不允许录入。

## 设置与诊断

运行数据保存在 `%LOCALAPPDATA%\HsReconnectLite\`，沿用旧版目录以兼容已有配置。`overlay.ini` 保存快捷键、按钮显示状态和位置；缺少或损坏的字段回退到默认值。保存失败不会破坏原文件，快捷键保存失败则保留原绑定。

`reconnect-lite.log` 为本地运行日志，超过约 1 MiB 时轮换，最多保留一份 `.old`。右键“保存连接诊断”或执行 `HRL.exe --inspect` 可生成 `diagnostics.txt`，该操作只读取连接信息，不拔线。日志和诊断可能包含路径、PID 与服务器地址，提交 Issue 前请先脱敏；程序不会自动上传。

快捷键支持 Ctrl / Alt / Shift 加字母、数字，以及 F1–F24（F12 除外）。录入后即时保存，Esc 关闭录入窗口；恢复默认也会检查冲突。启动时绑定被占用，按钮仍可使用，也可右键重新设置快捷键。

## 构建与打包

使用 Windows PowerShell 5.1 与系统自带的 .NET Framework C# 编译器，无需 Visual Studio、.NET SDK 或 NuGet 包。

```powershell
# 完整构建与测试，需要可交互桌面
.\build.ps1
# CI：跳过五组桌面交互测试
.\build.ps1 -SkipDesktopTests
# 构建、测试并生成 ZIP 与 SHA-256 清单
.\package.ps1
# 从绘图源码重新生成多尺寸图标
.\tools\build-icon.ps1
```

默认测试不会关闭游戏连接。可选的 `build\NativeTests.exe --close-test` 需要管理员权限，只关闭测试进程自建的回环连接，不由默认构建运行。

## 关闭与重启炉石

右键菜单的“只关闭炉石（强制）”与“重启炉石”是独立于拔线的显式操作，会结束 Hearthstone.exe 进程树。后台调用系统 taskkill，最多等待约 5 秒退出；若原来运行着游戏，重启前额外等待 3 秒。通过战网 `--exec="launch WTCG"` 启动，最多发送 6 次请求，每次观察 2 秒，检测到进程后停止重试；进程出现不代表已经进入对局。

战网路径保存在本机 `%LOCALAPPDATA%\HsReconnectLite\battle-net-path.txt`，无效时通过文件选择器重新指定。先验证启动器路径，再关闭游戏。路径不包含在公开仓库或通用发布包中。

这些操作与拔线共用忙碌状态和两秒防连按保护；运行中禁用其他游戏操作，避免关闭、启动与拔线交错。退出 HRL 会取消等待及后续启动请求，已经执行的关闭操作无法撤销。只在操作期间短时查询进程，闲置不增加轮询。

`GameActionTests` 使用模拟运行环境验证关闭、启动、超时、有限重试、取消及路径配置；`GameMenuTests` 用无害回调验证菜单接线、防重入和恢复。默认测试不执行真实的游戏关闭或重启。

## 重启后收起战网窗口

检测到炉石进程后，最多再等待 10 秒确认可见游戏主窗口。确认后，仅向配置路径下、标题为“战网”或 Battle.net 的唯一可见无属主主窗口发送一次 WM_CLOSE 请求。发送前重新核对主窗口句柄与所属进程，身份不确定则保留窗口。其效果遵循战网自己的点叉设置，不强制终止战网进程，也不修改战网设置。

该步骤只在重启操作内执行，没有常驻监控。启动失败、窗口等待超时或取消时不关闭战网；战网已自行退出或关闭请求未能发送，不会将已成功启动的炉石报告为启动失败。发送请求不代表战网已经处理完成。

API 依据：[WM_CLOSE](https://learn.microsoft.com/en-us/windows/win32/winmsg/wm-close)、[PostMessage](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-postmessagew)。模拟测试验证窗口出现前后、超时、取消、窗口身份过滤与进程退出竞态，不自动重启游戏或操作真实战网窗口。

## OW 关闭、重启与 NEAC 残留

OW 提供关闭和重启两项操作。两者都先关闭指定路径的 Overwatch.exe，再清理同目录 OWNeacClient.exe 与 NeacLoader.exe，等待退出。关闭操作到此结束，不需要战网路径；重启操作再使用战网产品代码 Pro 启动游戏。OWNeacClient 在任务管理器中可能显示为 neacprotect。不会停止服务、修改驱动或绕过游戏保护，也不会在已运行的 OW 中单独清理 NEAC。

路径保存在本机 `overwatch-path.txt`。进程读取使用 QueryFullProcessImageName 的有限查询权限，终止前在同一个进程句柄上重新验证路径，避免 PID 复用误关。同名不同目录的进程不清理；读取身份或关闭失败会停止重启并提示。已退出进程视为已完成，清理后最多等待 10 秒。

OW 只发送一次启动请求，最多等待 60 秒检测游戏进程，随后最多等待 30 秒确认游戏窗口。不会每两秒重发启动请求。窗口确认使用可见性和所属 PID，适配拒绝 HasExited 查询的受保护进程。随后复用战网点叉收起逻辑。所有等待仅发生在手动操作期间，与炉石操作共享忙碌保护与取消信号。

测试覆盖启动不打断正在运行的游戏、NEAC 清理顺序、路径过滤、取消和进程退出竞态。1.5.0 开发时，经用户授权在空闲 OW 中实际检查了退出、残留清理、重启和战网收起；未修改游戏文件、账号或系统保护设置。

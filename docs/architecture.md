# 实现与验证范围

程序入口负责单实例、按需 UAC 提权和只读诊断；`TrayContext` 将悬浮窗、托盘与快捷键汇入同一个异步操作，并进行防重入与两秒防连按。进程、连接及游戏日志线索每次重新读取。

`ConnectionSelector` 只选择唯一可靠的对局连接，`NativeTcp` 使用 IP Helper API 枚举 IPv4 TCP 表并通过 `SetTcpEntry(DELETE_TCB)` 关闭目标。不会注入 DLL、读取游戏内存、修改防火墙或结束游戏进程。IPv6、UDP 与特殊代理拓扑不在当前实现范围内。

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

CI 明确跳过四组需要交互桌面的测试。测试通过不能代替真实对局兼容性验证；新版本不自动触发真实游戏拔线。不同物理显示器间的拖动、全部 Windows / 区服 / 代理组合仍需用户反馈。

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
# CI：跳过四组桌面交互测试
.\build.ps1 -SkipDesktopTests
# 构建、测试并生成 ZIP 与 SHA-256 清单
.\package.ps1
# 从绘图源码重新生成多尺寸图标
.\tools\build-icon.ps1
```

默认测试不会关闭游戏连接。可选的 `build\NativeTests.exe --close-test` 需要管理员权限，只关闭测试进程自建的回环连接，不由默认构建运行。

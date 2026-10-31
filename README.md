# 炉石轻连 · Hearthstone Reconnect Lite

<img src="assets/icon.png" width="72" height="72" alt="炉石轻连图标">

一个只做手动拔线的 Windows 小工具。单个 EXE，半透明悬浮按钮、托盘菜单、自定义全局快捷键；不依赖 HDT。

## 使用

1. 从 [Releases](https://github.com/wlog0654/hearthstone-reconnect-lite/releases) 下载 ZIP 并解压。
2. 双击 `HearthstoneReconnectLite.exe`，接受管理员权限提示。
3. 在炉石对局中点击悬浮按钮，或按默认快捷键 **Ctrl+Shift+R**。
4. 按住按钮拖动位置。右键按钮或托盘图标，可以隐藏按钮、设置快捷键或退出。

**设置快捷键：** 右键 → 设置快捷键 → 直接按下组合键，即时保存，无需输入文字。支持 Ctrl / Alt / Shift + 字母、数字，以及 F1–F24（系统保留的 F12 除外）；Esc 关闭设置窗口。录入期间不会拔线。冲突或保存失败会保留原快捷键；恢复默认也会检查冲突。启动时遇到占用，可继续使用按钮或重新设置快捷键。

隐藏悬浮按钮后，快捷键仍然可用。退出程序会释放快捷键。不会开机自启，也没有自动拔线、广告、遥测、自动更新或联网下载组件。

## 运行要求与限制

- Windows 10 / 11 **x64**，.NET Framework 4.8。其他 Windows 版本、ARM64 原生运行未验证。
- 关闭 TCP 连接需要管理员权限；UAC 取消后程序退出。
- 目前识别 **IPv4 TCP**；只操作能唯一确定的炉石对局连接。无法确认时拒绝操作，不批量断开所有连接。
- 使用窗口或无边框窗口更适合显示悬浮按钮。独占全屏的显示行为取决于游戏。
- 不保证兼容所有区服、游戏更新、代理或加速器；不保证游戏一定重连成功。“已拔”仅表示 Windows 接受了关闭连接请求。
- 程序是第三方独立项目，与 Blizzard、HDT 和其他记牌器无隶属关系。请自行了解并遵守游戏规则。

## 轻量与鲁棒性

原生 C# / WinForms + Windows API，没有常驻键盘钩子。快捷键使用 `RegisterHotKey`，支持禁止长按重复触发；按钮、托盘与快捷键共用防重入和两秒防连按保护。切换快捷键时先注册新键并原子保存，再释放旧键。

录入窗口处于前台时临时捕获组合键，避免被其他程序占用的快捷键触发原程序；切换到其他窗口或关闭录入窗口后立即释放。平时依靠系统快捷键消息，不监听每次键盘输入。

闲置时不扫描网络、不轮询游戏进程、不持续播放动画。触发时在后台重新查询当前进程与连接。悬浮按钮使用逐像素透明、按 DPI 重新绘制，拖动不触发拔线，显示器变化后将位置移回可见区域。

内存与 CPU 占用取决于 Windows、DPI 和运行环境；不把短时本机采样当作所有机器的保证。

## 设置与诊断

所有运行数据保存在 `%LOCALAPPDATA%\HsReconnectLite\`，沿用旧版目录以兼容已有配置：

| 文件 | 用途 |
| --- | --- |
| `overlay.ini` | 快捷键、按钮显示状态和位置；损坏字段回退默认值 |
| `reconnect-lite.log` | 本地运行日志；超过约 1 MiB 时轮换，最多保留一份 `.old` |
| `diagnostics.txt` | 右键“保存连接诊断”手动生成的只读连接信息 |

也可以运行 `HearthstoneReconnectLite.exe --inspect`，只生成诊断，不拔线。设置文件写入失败不会破坏原文件；更改快捷键保存失败则不应用该更改。

诊断和日志可能包含本机路径、PID 和服务器地址。提交 Issue 前请检查并删除不希望公开的信息；程序不会自动上传这些文件。

从旧版升级：先在托盘退出旧程序，再运行新 EXE。配置自动沿用，旧版没有快捷键配置时默认 Ctrl+Shift+R。两个版本共用单实例限制。

## 从源码构建

无需安装 Visual Studio、.NET SDK 或 NuGet 包。Windows PowerShell 5.1 和系统自带的 .NET Framework C# 编译器即可：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

输出为 `build\HearthstoneReconnectLite.exe`。默认同时运行全部测试，部分窗口与菜单测试需要可交互的 Windows 桌面。

```powershell
# 无交互桌面的 CI：明确跳过四组桌面交互测试
.\build.ps1 -SkipDesktopTests
# 构建、测试并打包 ZIP 和 SHA-256 清单
.\package.ps1
# 可选：从原创绘图源码重新生成多尺寸图标
.\tools\build-icon.ps1
```

测试不会拔掉真实游戏连接；网络集成测试仅枚举自己创建的回环套接字，界面测试使用无害回调。另有需要管理员权限的可选本地关闭测试 `build\NativeTests.exe --close-test`，只关闭测试进程自己的回环连接，不由默认构建运行。

## 连接选择

每次操作重新枚举 `Hearthstone.exe` 的已建立连接：唯一远端 3724 连接优先；存在多个候选时，必须由当前游戏会话日志唯一匹配。无 3724 候选时，只接受日志端点与实时连接精确匹配，并排除 1119 登录端口。不采用“选择最新连接”的猜测兜底。

技术说明与测试范围见 [架构说明](docs/architecture.md)。欢迎提交问题或精简改进，参见 [贡献说明](CONTRIBUTING.md)。

## 许可证

源码、原创图标及图标生成器使用 [MIT License](LICENSE)。仓库不包含 HDT、Curtion 的反编译文件、游戏资源或第三方程序二进制。

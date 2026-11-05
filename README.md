# HRL

炉石酒馆战棋的手动拔线工具。点一下悬浮按钮，或按一次快捷键，断开当前对局的连接。

单个 EXE，免安装。附带关闭、重启炉石的快捷操作。

[下载最新版](https://github.com/wlog0654/HRL/releases/latest) · [反馈问题](https://github.com/wlog0654/HRL/issues)

## 使用

运行 `HRL.exe`，允许管理员权限。对局中点悬浮按钮，或按 `Ctrl+Shift+R`。右键按钮或托盘图标，可以改快捷键、隐藏按钮、退出。

右键菜单也可以**只关闭炉石（强制）**或**重启炉石**。首次重启时选择战网 `Battle.net.exe`，之后会记住路径。重启检测到炉石窗口后，会自动向战网主窗口发送“点叉”关闭请求，收起方式遵循战网自己的设置。关闭和重启会结束游戏进程，请按需使用。

按钮可以拖动，位置会记住。隐藏之后快捷键仍然有效。

## 行为

- 不轮询，手动触发时才查询连接，两秒内连按只算一次。
- 找不到唯一可靠的对局连接时不会动手，并提示原因。
- 拔线功能只关闭网络连接，不注入 DLL、不读游戏内存、不改防火墙、不结束游戏进程。
- 快捷键冲突或保存失败时，保留原来的设置。

## 限制

Windows 10 / 11 x64，需要 .NET Framework 4.8。只支持 IPv4 TCP。悬浮按钮建议配合窗口或无边框模式。

提示“已拔”只说明连接已关闭，不代表游戏已经重连成功。各区服和加速器环境无法全部验证，遇到问题请提 Issue。

## 构建

在 Windows PowerShell 中运行：

```powershell
.\build.ps1
```

会构建并测试，输出 `build\HRL.exe`。不需要 Visual Studio 或 .NET SDK。

更多内容：[实现与测试](docs/architecture.md) · [设置与诊断](docs/architecture.md#设置与诊断) · [参与贡献](CONTRIBUTING.md) · [更新记录](CHANGELOG.md)

MIT License。独立第三方工具，与 Blizzard 无隶属关系。

# HRL

**Hearthstone Reconnect Lite**

炉石酒馆战棋的轻量手动拔线工具。**点一下悬浮按钮，或按一次快捷键。**

**约 69 KiB · 单文件免安装 · 半透明悬浮按钮 · 快捷键按键录入**

[下载最新版](https://github.com/wlog0654/HRL/releases/latest) · [反馈问题](https://github.com/wlog0654/HRL/issues)

## 简单，轻量，随手可用

- **一个 EXE 就够了**：下载即用，无需运行 HDT。
- **按钮随手放**：半透明、可拖动、可隐藏，自动记住位置。
- **快捷键直接按**：按下想用的组合键即可保存，冲突时保留原设置。
- **闲置不轮询**：手动触发才查询游戏连接，带防连按保护。

## 开始使用

1. 下载并解压，运行 `HRL.exe`，允许管理员权限。
2. 对局中点击悬浮按钮，或按 **Ctrl+Shift+R** 拔线。
3. 右键按钮或托盘图标，可设置快捷键、隐藏按钮或退出。

按住左键拖动按钮；隐藏后快捷键仍然有效。升级前先退出旧版，原有设置自动沿用。

## 运行环境

Windows 10 / 11 x64，.NET Framework 4.8。悬浮按钮建议搭配窗口或无边框模式。

目前支持 IPv4 TCP；无法可靠识别对局连接时拒绝操作。游戏、区服及代理环境的兼容性以实际使用为准，“已拔”不代表游戏已完成重连。

## 开发与开源

Windows PowerShell 中运行 `powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1`，即可构建并测试，输出 `build\HRL.exe`。

[实现与测试](docs/architecture.md) · [设置与诊断](docs/architecture.md#设置与诊断) · [参与贡献](CONTRIBUTING.md) · [更新记录](CHANGELOG.md) · [MIT License](LICENSE)

本项目为独立第三方工具，与 Blizzard 无隶属关系。

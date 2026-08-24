# Piko Desktop Pet

Piko 是一个 Windows 10/11 x64 桌面宠物 MVP。它不是悬浮聊天按钮，而是把真实窗口、屏幕边缘、鼠标和文件活动当作生活环境的小生命。

当前版本：`0.1.0`

## 已实现

- 原创矢量占位形象、透明无边框置顶窗口；
- 点击打招呼、拖拽放下、双击设置、右键菜单；
- `Ctrl+Alt+P` 全局召回，支持鼠标穿透和托盘显示/隐藏；
- 站在窗口顶部并跟随窗口移动，支撑消失后安全下落；
- 窗口边缘攀爬、窗口间跳跃、屏幕外探头；
- 把窗口表面当作散步和休息的家具；
- 鼠标静止时走近并保持安全距离；
- 本地观察下载、桌面、文档目录及 Windows Shell 复制进度；
- 设置与位置持久化、异常退出召回、隐私日志和标题无关诊断快照；
- 输出面向未来 ESP32/Nomi 风格屏幕的简化眼睛状态 JSON；
- 独立 World Lab，用于查看窗口几何和可站立表面。

默认不联网，不读取文件内容，不记录窗口标题。

## 直接使用

从 GitHub Releases 下载 `Piko-0.1.0-win-x64.zip`，解压后双击 `Piko.exe`。发布包自带运行时，不需要安装 .NET。

常用操作：

| 操作 | 结果 |
|---|---|
| 单击 Piko | 打招呼 |
| 拖动 Piko | 抱起并放到窗口或桌面上 |
| 双击 Piko | 打开设置 |
| 右击 Piko / 托盘图标 | 打开控制菜单和行为演示 |
| `Ctrl+Alt+P` | 从任何屏幕位置召回 |

完整说明见 [中文使用说明](docs/USER_GUIDE_ZH.md)。

## 开发与验证

要求 Windows 10/11 和 .NET 8 SDK：

```powershell
.\scripts\verify.ps1
.\scripts\run-piko.ps1
.\scripts\publish.ps1
```

也可以直接使用标准命令：

```powershell
dotnet restore Piko.sln
dotnet build Piko.sln -c Release
dotnet test Piko.sln -c Release
dotnet run --project src/Piko.Desktop/Piko.Desktop.csproj
```

## 项目结构

```text
src/Piko.World/             物理坐标、桌面世界编译器、宠物状态机
src/Piko.World.Windows/     Win32、DWM、显示器与窗口观察
src/Piko.Desktop/           可交付的 WPF 桌宠应用
src/Piko.WorldLab/          桌面几何诊断工具
tests/Piko.World.Tests/     确定性世界和行为测试
docs/                       产品、技术、协议和验收文档
```

## ESP32 预留

Piko 运行时会写入：

```text
%LOCALAPPDATA%\PikoDesktopPet\device-state.json
```

实体端只需消费 `eyeShape`、`lookX`、`lookY`、`brightness` 等 Nomi 风格眼睛字段，不需要复制 PC 端复杂动画。协议见 [ESP32 状态协议](docs/ESP32_STATE_PROTOCOL.md)。

## 许可

MIT License。角色占位形象为本项目原创矢量组合，不包含 QQ 宠物、蔚来 Nomi 或其他第三方角色素材。

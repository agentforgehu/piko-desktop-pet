# Piko Desktop Pet

Piko 是一个 Windows 10/11 x64、本地优先的智能桌面宠物。它不是悬浮聊天按钮，而是把真实窗口、屏幕边缘、鼠标、文件活动和已授权的开发事件当作生活环境。

当前开发版本：`1.0.0-alpha.1`。稳定公开版仍为 `0.1.0`。

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
- 独立用户级 Runtime 后台、受保护命名管道、健康检查与自动恢复；
- 本地情境引擎和打扰策略，可理解离开/返回、编程、构建、测试与系统状态；
- 可选 VS Code Bridge，只发送诊断数量、构建/测试结果和 Git 计数，不发送源码、文件名或终端正文；
- 可选加密本地记忆和可删除记忆管理界面；
- 可选 AI Agent：模型只提出结构化计划，1.0 只执行用户确认、限定目录内的只读工具；
- 独立 World Lab，用于查看窗口几何和可站立表面。

默认不联网，不读取文件内容，不记录窗口标题。开发感知、Git、记忆、云端 AI 和 Agent 读取权限均默认关闭。

## 直接使用

从 [GitHub Release v0.1.0](https://github.com/agentforgehu/piko-desktop-pet/releases/tag/v0.1.0) 下载 `Piko-0.1.0-win-x64.zip`，解压后双击 `Piko.exe`。发布包自带运行时，不需要安装 .NET。

1.0 Alpha 正在生产收口，当前本地构建可使用 `scripts/publish.ps1` 生成单文件当前用户安装器、自包含便携包、更新清单和 VS Code 扩展。Alpha 尚未签名；正式 1.0 的发布脚本默认拒绝生成未签名稳定版。

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
.\scripts\stability.ps1 -Version 1.0.0-alpha.1 -DurationSeconds 1800
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
src/Piko.Desktop/           WPF 桌宠、托盘、设置、Agent 与记忆界面
src/Piko.Runtime/           每用户后台、Sensors、情境、IPC 和受控 Agent 宿主
src/Piko.Runtime.Client/    版本化 IPC 契约、客户端和凭据接口
src/Piko.Context*/          Context Event、隐私、Situation、Intervention 和 Windows Sensors
src/Piko.Agent/             AI Provider、规划、策略、工具与审计
src/Piko.Memory/            SQLite 分层记忆与字段加密
src/Piko.Update/            更新清单、下载边界、哈希与 Authenticode 验证
src/Piko.Setup/             当前用户安装、回滚更新和卸载
src/Piko.WorldLab/          桌面几何诊断工具
integrations/vscode/        最小数据 VS Code Context Bridge
tests/                      世界、Context、Runtime、Agent 与 Memory 自动化测试
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

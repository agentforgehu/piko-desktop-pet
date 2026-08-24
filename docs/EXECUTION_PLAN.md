# Piko Desktop Pet MVP 执行计划

状态：`DONE` 已验证；`ACTIVE` 正在执行；`NEXT` 下一步。

| 工作包 | 退出条件 | 状态 |
|---|---|---|
| WP-00 产品与验证边界 | PRD、World Lab Charter、TDD 完成 | DONE |
| WP-01 桌面世界 | 物理像素窗口、显示器、表面、遮挡和快照 | DONE |
| WP-02 桌宠运行时 | 透明窗口、渲染、拖拽、召回、托盘和设置 | DONE |
| WP-03 空间行为 | 站立/跟随/下落/攀爬/跳跃/探头 | DONE |
| WP-04 环境互动 | 窗口家具、鼠标驻足、复制/下载观察 | DONE |
| WP-05 本地后端 | 设置、日志、崩溃恢复、快照、设备状态 | DONE |
| WP-06 自动与真实桌面验证 | Release build、18 tests、WPF smoke | DONE |
| WP-07 Windows x64 发布 | 自包含 exe、zip、SHA256、独立冒烟 | DONE |
| WP-08 GitHub 交付 | 新仓库、main、v0.1.0、Release asset | DONE |

## 2026-08-24 运行证据

- 本地 .NET SDK 8.0.424；没有修改系统 SDK。
- 全解决方案 Release 构建：0 警告、0 错误。
- 自动化测试：18/18 通过。
- World Lab 真实采集：显示器、窗口、可站立表面及快照回放通过。
- Piko WPF 真实进程：启动、桌面采集、状态投影、干净退出通过。
- 文件活动误报规则已通过真实运行结果收紧为 Shell 真实进度控件。
- 当前设备投影结果：`standing / normal`。
- 自包含包：ZIP 66,251,036 bytes；独立解压运行 exit code 0。
- SHA256：`cdfb9cbf07b01fb90ff52e5fe06c57f0a0e074ed96ca50f65d92fa5a557440c8`。
- 私有仓库：`https://github.com/agentforgehu/piko-desktop-pet`。
- 远端主提交：`a670025e9feab07da40a129076ce812d98a197a3`。
- 正式发布：`https://github.com/agentforgehu/piko-desktop-pet/releases/tag/v0.1.0`；ZIP 与 SHA256 文件均已在页面验证。

## 发布门禁

1. `scripts/verify.ps1` 全绿。
2. `scripts/publish.ps1` 生成自包含 win-x64 包。
3. 不借助系统 .NET 运行发布版 `Piko.exe --smoke-test`。
4. 校验 ZIP SHA256。
5. Git 初始化后只提交源代码和文档，不提交 `bin/obj/releases`。
6. 新建 GitHub 仓库，推送 `main`，创建 `v0.1.0` Release 并上传 ZIP 与校验文件。


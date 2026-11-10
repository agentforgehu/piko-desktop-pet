# Piko Desktop Pet 执行计划

项目目标已调整为**通用智能体认知核心**，以 [项目章程](COGNITION_CORE_CHARTER.md) 和阶段验收为准。桌宠 `1.0.0` 发布候选仍按 [交付与验收](RELEASE_1.0_READINESS.md) 独立维护；它是首个宿主集成，不再定义认知系统目标。

## Cognitive State System V1 进度

桌宠原型现状见 [认知状态系统 V1](COGNITIVE_STATE_SYSTEM_V1.md)。核心项目阶段如下：

| 里程碑 | 内容 | 状态 |
|---|---|---|
| C0 通用核心抽取 | 宿主无关的情绪/稳态、人格偏好与行为建议 API；桌宠适配层接入 | 已实现并提交 PR #6；Windows CI 构建、测试及桌面冒烟全部通过；待 PR 合并 |
| C1 通用记忆核心 | 中性数据契约、来源/置信/敏感度、有效期、版本、删除/导出与冲突复核 | `Piko.Cognition` 记忆 DTO/接口及 `Piko.Memory` SQLite 适配器已实现，加入置信度字段和旧库迁移；等待 CI 验证；自由文本冲突、复合事实导出及第二宿主验收仍待实现 |
| C2 情绪、动机与人格 | 连续状态更新、调节恢复、目标与稳定特质解耦 | 首批稳态机制存在；目标/动机分层和连续互动验证未完成 |
| C3 通用评测与效率 | 多宿主无关场景、独立人工审查、质量/安全/成本消融 | 当前仅有 10 条合成桌宠场景和本地配对 runner；覆盖不足 |
| C4 受控迭代 | 隔离候选、可复现构建、人工签审、灰度及回滚 | 尚未完成；当前 runner 评估模型输出，不执行代码候选 |
| C5 宿主适配 | 桌宠及至少一种非桌宠 Agent 接入同一核心 | Piko 桌宠为首个适配示例；第二宿主未开始 |

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
- 远端源码基线提交：`a670025e9feab07da40a129076ce812d98a197a3`；最终验收文档提交：`930ab1641fdbd7715c1e462b472c15d3d6c9f7b1`。
- 正式发布：`https://github.com/agentforgehu/piko-desktop-pet/releases/tag/v0.1.0`；ZIP 与 SHA256 文件均已在页面验证。

## 发布门禁

1. `scripts/verify.ps1` 全绿。
2. `scripts/publish.ps1` 生成自包含 win-x64 包。
3. 不借助系统 .NET 运行发布版 `Piko.exe --smoke-test`。
4. 校验 ZIP SHA256。
5. Git 初始化后只提交源代码和文档，不提交 `bin/obj/releases`。
6. 新建 GitHub 仓库，推送 `main`，创建 `v0.1.0` Release 并上传 ZIP 与校验文件。

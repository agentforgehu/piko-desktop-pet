# Piko 1.0 后台、后端与 AI Agent 运维说明

## 1. 这里的“后端”是什么

Piko 1.0 是本地优先的 Windows 产品，不依赖必须在线的传统云后端。生产包包含两个进程：

- `Piko.exe`：桌宠表现、托盘、设置、权限开关、Agent 计划确认和记忆管理；
- `Piko.Runtime.exe`：当前登录用户下的后台宿主，负责 Sensors、事件总线、情境、打扰策略、加密记忆、AI Provider 和 Agent 工具执行。

Runtime 不是 LocalSystem 服务，不需要管理员权限，也不监听网卡。Desktop 与 Runtime 只通过 `CurrentUserOnly` Windows 命名管道通信；当前协议 schema 为 v1，含请求关联、消息大小和超时限制。

## 2. 后台感知与决策流

```text
Windows / VS Code 安全摘要
  → ContextEvent schema 校验
  → 权限与字段级隐私过滤
  → 有序 Event Bus
  → Situation Engine
  → Intervention Policy
  → 桌宠语义动作 / ESP32 Nomi 眼睛状态
```

Windows 端只保留空闲/活跃/锁定、前台应用类别、全屏、电源/电池和分桶内存健康。VS Code 扩展只发送诊断数量、构建/测试结果与时长、Git staged/changed/conflict 数量；不发送源码、文件名、窗口标题、诊断正文、终端输出和仓库路径。

Situation 是带置信度和证据的本地状态，例如 `Coding`、`BuildFailed`、`Meeting`、`Away`。Intervention 再结合输入活跃度、勿扰、失败阈值、冷却和每小时预算决定 Piko 是否应该出现，而不是每个事件都打扰。

## 3. AI Agent 如何工作

AI 默认关闭。启用后仍分为两道独立权限：云端 AI 和 Agent 读取。流程如下：

1. 用户在“问 Piko”里主动输入问题；
2. Runtime 只组合已过滤的高层 Situation，不自动附加源码、路径或窗口标题；
3. API Key 从 Windows 凭据管理器读取，缺少 Key 时保证零网络；
4. Provider 调用 Responses API，`store=false`，要求严格 JSON Schema 输出；
5. 模型返回解释和工具计划，此时工具没有执行；
6. Runtime 校验工具名、参数 schema 和风险等级，生成随机、五分钟、一次性 proposal；
7. 用户再次点击执行并选择允许的工作目录；
8. Runtime 只执行 1.0 注册的 `git.status`、`workspace.file.read` 只读工具，并限制目录、超时和输出大小；
9. 结果只显示在本机，不自动二次上传给模型；审计只记录工具名、状态和原因，不记录参数、路径或正文。

模型不能注册工具、扩大目录、延长 proposal、改变用户确认后的参数，也不能直接访问进程、Shell、网络或文件系统。写文件、运行命令、Git push 和外部通信工具在 1.0 中没有注册。

## 4. 记忆与数据控制

记忆分为 Working、Episodic、Semantic、Profile 和 Relationship。SQLite 只保存 AES-256-GCM 字段密文，随机 nonce 和 purpose-bound AAD 防止字段互换；密钥在 Windows 凭据管理器。Working 默认一天、Episodic 默认 30 天，最多 10,000 条。

用户可从托盘查看安全摘要或删除全部记忆。完全卸载时使用 `--purge-data` 才会删除 `%LOCALAPPDATA%\PikoDesktopPet` 和两项 Windows 凭据；普通卸载默认保留数据，便于重新安装。

## 5. 后台控制与诊断

本地目录：`%LOCALAPPDATA%\PikoDesktopPet`。

- `runtime-status.json`：原子心跳、版本、健康、当前 Situation 和权限状态；
- `runtime-settings.json`：非敏感后台开关与 AI endpoint/model；
- `memory.db`：字段加密记忆；
- `agent-audit.jsonl`：最小化执行审计，2 MB 轮转；
- `device-state.json`：未来 ESP32 消费的 Nomi 风格眼睛语义状态。

Desktop 每 30 秒监督 Runtime；后台不可用时桌宠继续以本地表现层运行。托盘“查看后台状态”显示健康、版本、情境和最近心跳。设置变化会原子保存并受控重启 Runtime。

## 6. 生产验收方法

- `scripts/verify.ps1`：构建、93 项测试、TypeScript 严格检查、Desktop/Runtime 真进程 smoke；
- `scripts/publish.ps1`：自包含打包、文件版本一致性、Setup payload smoke、哈希与更新清单；
- `scripts/stability.ps1`：隔离运行 Desktop/Runtime，检查工作集、CPU、句柄、心跳和干净退出；默认 30 分钟；
- 设置中关闭云 AI或清除 API Key后，Provider 测试保证不产生网络请求；
- 写入工具不在生产注册表，未知工具、越界路径、过期/复用 proposal 均 fail closed。

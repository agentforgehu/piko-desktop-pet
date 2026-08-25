# Piko Desktop Pet 1.0 生产架构

## 1. 产品运行单元

Piko 1.0 采用本地优先的进程与组件安全边界，而不是把所有能力放进桌宠窗口。当前发布包含两个进程：桌面表现进程和每用户后台进程；Agent 是由 Runtime 托管的受控组件，不是拥有独立系统权限的第三个进程。

```text
Piko.Desktop
  WPF 角色、托盘、设置、权限提示和用户确认
        │ authenticated local IPC
Piko.Runtime
  每用户常驻后台、Sensors、Event Bus、Situation、Intervention、Memory、ESP32
        │ privacy filter + tool policy
Piko.Agent（托管于 Piko.Runtime）
  可选模型理解、结构化规划、只读工具和审批执行

VS Code Extension ── authenticated local IPC ── Piko.Runtime
```

`Piko.Runtime` 是登录用户会话中的普通进程，不是 LocalSystem Windows Service。Windows 服务与交互桌面隔离，不适合直接观察当前用户窗口、前台程序和桌面会话；Runtime 不要求管理员权限，并随用户登录启动。

## 2. 组件职责

### Piko.Desktop

- 只负责表现和直接交互；
- 展示当前 Situation、感知状态和行为原因；
- 展示模型提供方、关闭/未测试/健康/异常状态，并提供保存后真实连接测试；
- 提供分项权限、暂停感知、记忆查看/删除和 Agent 审批；
- Runtime 暂时不可用时仍能显示安全降级角色并提供恢复入口。

### Piko.Runtime

- 单实例、崩溃恢复、健康检查和生命周期；
- Windows、VS Code、Git、构建、测试和文件活动 Sensors；
- 事件校验、隐私过滤、顺序分发和短期缓冲；
- Situation Engine 和 Intervention Policy；
- SQLite/加密字段记忆存储；
- 本地 IPC 身份校验、速率限制和消息版本协商；
- ESP32 状态投影和连接管理；
- 默认不联网。

### Piko.Agent（Runtime 内的受控组件）

- `IAiProvider` 屏蔽具体模型供应商；
- OpenAI API 使用 Responses 结构化输出，本地模型使用仅限回环地址的 OpenAI-compatible Chat Completions；
- Runtime 记录最近模型健康、错误类别和检查时间，但不记录 API Key、提示词或模型回复正文；
- Context Composer 只消费隐私过滤后的 Situation 和显式选择的证据；
- Tool Registry 记录每个工具的权限等级、参数 schema 和副作用；
- 1.0 默认只注册只读工具；写文件、运行命令、Git 远端或外部通信工具不进入生产注册表；
- 审批令牌、风险分级和工作目录约束已经作为安全基础设施存在，未来增加写工具时仍必须逐次审批；
- 执行具有工作目录边界、超时、输出上限、敏感字段脱敏和审计记录；
- 模型不可绕过策略层直接调用系统 API。

## 3. 数据流

```text
Raw signal
  → Sensor adapter
  → ContextEvent schema validation
  → ContextPrivacyFilter
  → ContextEventBus
  → SituationEngine
  → InterventionPolicy
  → Behavior semantic action
  → Desktop / ESP32 projection
```

AI 只在以下条件之一成立时参与：

1. 用户主动请求解释或帮助；
2. 本地规则无法理解一个已授权的高层事件；
3. 用户明确启用了相应主动能力且未处于勿扰状态。

基础桌宠、感知、情境与规则行为完全离线可用。

## 4. 权限等级

| 等级 | 示例 | 默认 |
|---|---|---|
| 无内容感知 | 在场、应用类别、全屏、文件活动 | 允许本地 |
| 项目摘要 | 项目标识、诊断数量、构建结果、Git 摘要 | 关闭，分项授权 |
| 敏感内容 | 窗口标题、路径、Diagnostic 正文、终端输出 | 关闭，按会话授权 |
| 捕获 | 截图、麦克风 | 关闭，单次显式授权 |
| Agent 只读 | 查看 git status、读取用户选择的文件 | 关闭，任务授权 |
| Agent 写入 | 改文件、运行命令、Git push | 1.0 不提供；未来版本每次审批 |

## 5. 生产安全不变量

1. 原始键盘内容永不采集。
2. 模型输入必须通过隐私过滤器。
3. 窗口标题、文件路径和终端正文默认拒绝。
4. 本地 IPC 不监听外部网卡。
5. 写操作不能仅凭模型输出执行。
6. API Key、设备密钥和签名凭据不进入仓库或普通日志。
7. 用户可停止 Runtime、暂停所有 Sensors、删除记忆并撤销授权。
8. 任何未知 schema、未知工具或过期审批默认拒绝。

## 6. 外部依赖

- AI Provider：可选；未配置时 Agent 处于离线禁用状态；
- 代码签名：Release Candidate 前可使用未签名内部包，正式 1.0 需要可信签名方案；
- 美术和音频：表现层可替换，不阻塞后台研发；
- ESP32：1.0 保持协议和模拟器，硬件验收需要具体板型和屏幕资料。


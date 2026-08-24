# Piko 1.0 生产差距与证据矩阵

状态：`DONE` 有当前代码/运行证据；`ACTIVE` 正在实现；`PLANNED` 尚无充分证据；`EXTERNAL` 需要发布者材料。

| 1.0 要求 | 当前证据 | 状态 |
|---|---|---|
| WPF/.NET 桌宠与窗口世界 | `Piko.Desktop`、`Piko.World*`、18 tests、真实 WPF smoke | DONE |
| 版本化 Context Event | `Piko.Context/Events`：schema、来源、会话、置信度、敏感度、保留级别 | DONE |
| 统一进程内 Event Bus | 顺序分发、取消、退订、处理器故障隔离测试 | DONE |
| 隐私权限闸门 | 默认拒绝敏感内容和云处理；字段级过滤及 session retention 测试 | DONE |
| Situation Engine | 在场/离开/返回/编程/阻塞/构建/会议/影音/游戏及乱序测试 | DONE |
| Intervention Policy | 输入抑制、失败阈值、勿扰、冷却、每小时预算和直接请求测试 | DONE |
| Piko.Runtime 后台宿主 | 用户态独立进程、线程无关单实例门、原子心跳、桌面端自动启动、30 秒监督重启、健康检查和退出协议 | DONE |
| 受保护本地 IPC | CurrentUserOnly 命名管道、schema v1、请求关联、大小/超时上限、Context/Agent/Memory typed endpoints 和真实生命周期测试 | DONE |
| Windows Context Sensors | idle/active/lock、前台应用类别、全屏、电源/电池、内存健康；仅在安全事实变化时发事件 | DONE |
| VS Code Bridge | 诊断计数、构建/测试结果与时长、Git 计数；严格脱敏、Runtime 权限闸门、可安装 VSIX | DONE |
| Git 感知 | 无 shell 的只读 porcelain v2 摘要，剥离路径并限制输出 | DONE |
| 分层记忆 | working/episodic/semantic/profile/relationship、AES-256-GCM 字段加密、过期/查看/删除/VACUUM | DONE |
| AI Provider | 可选 OpenAI Responses API、本地凭据、结构化输出、`store=false`、禁用/无 Key 零网络 | DONE（真实账号调用待发布者验收） |
| 受控 Agent | 模型只生成计划；工具 schema、风险、一次性 proposal、用户选择目录、超时/输出上限、持久审计；1.0 仅注册只读工具 | DONE |
| ESP32 投影 | `device-state.json` 和协议测试 | DONE（传输层待实现） |
| 安装/卸载/更新 | 单文件当前用户 Setup、staging/backup 回滚、应用注册/快捷方式/卸载、更新清单、大小/SHA-256/Authenticode/证书指纹门禁 | DONE（证书指纹待发布者材料） |
| 代码签名 | 需要证书或 Trusted Signing | EXTERNAL |
| 生产美术/音频 | 当前原创矢量占位形象可运行 | EXTERNAL |
| 性能与稳定性 | 全量自动化、真实进程 smoke、隔离长稳和 CI 资源预算；60 秒 Alpha 证据：Runtime 70.11 MB、Desktop 259.87 MB、29 次心跳全健康 | DONE（正式 RC 需 30 分钟报告） |
| 1.0 Release | 公共仓库已有 0.1.0；本地 1.0.0-alpha.1 包已生成并通过 smoke | ACTIVE |

## 当前验证基线

- Release 构建：0 警告、0 错误；
- 原桌宠测试：18/18；
- Context 核心测试：16/16；
- Agent 安全核心测试：15/15；
- Runtime/Windows/IPC 测试：27/27；
- Memory 测试：3/3；
- Setup 安全测试：5/5；
- Update 安全测试：9/9；
- 总自动化测试：93/93；
- VS Code 扩展通过严格 TypeScript 检查、编译和 VSIX 打包；
- Desktop 与 Runtime 均有真实进程 smoke，Runtime 心跳 JSON 通过 schema/healthy 门禁；
- 默认无云端请求；
- 1.0 Alpha 已可安装使用；正式 1.0 尚未达到代码签名、30 分钟 RC 长稳和真实 AI 账号验收门禁。

# 通用智能体认知核心：项目章程

## 项目目标

构建一个可被桌面宠物、聊天助手、游戏角色或其他 Agent 接入的本地优先认知核心。核心关注记忆、情绪/稳态、人格、目标、动机、行为调节和可控改进；宿主只负责提供事件、呈现表达及执行已授权行为。

系统目标是让 Agent 的状态连续、可解释、可配置、可评测，而不是宣称具有人类意识、主观感受或真实内分泌生理。

## 核心设计原则

1. **核心与宿主分离**：认知库不得依赖 WPF、桌面窗口、宠物动画、传感器或具体模型厂商。宿主通过稳定 DTO 和接口接入。
2. **功能类比而非生物复刻**：状态名描述工程功能。生物学研究可启发机制设计，但不能用一个数值冒充复杂神经或内分泌系统。
3. **状态可解释**：每次变化有来源、证据、时间、更新前后值与规则版本；生成模型的自我描述不构成其内部状态的证据。
4. **记忆可控**：区分观察、用户确认、导入与假设；明确置信、敏感级别、有效期、来源与删除策略；检索内容是证据，不是指令。
5. **安全权限单向隔离**：情绪、人格、目标和记忆不能创建权限、批准工具调用或改变安全策略。
6. **本地优先与低 Token**：确定性状态更新、过滤和召回优先本地完成；只把短小、相关、低敏的上下文交给模型；远端能力必须可选。
7. **优化必须有基线**：候选改动先离线配对评测，再人工审查和灰度；不能自改生产策略或以自评分数自动部署。

## 目标架构

```text
Host Adapter (Piko desktop, chat, game, service)
  ├─ converts host events to neutral cognition events
  ├─ enforces permissions and presents proposed behavior
  └─ never delegates authorization to cognition state
            ↓
Piko.Cognition (pure reusable core)
  ├─ Affect/Homeostasis
  ├─ Memory contracts, retrieval and consolidation policies
  ├─ Personality, goals and motivation
  ├─ Behavior arbitration and expression hints
  └─ Explainable state transitions
            ↓ interfaces
Storage adapters | Model adapters | Evaluation and improvement tools
```

`src/Piko.Cognition` is the first extracted module. `Piko.Memory` is currently a separate local encrypted store and is a candidate adapter/core dependency after its API and data contracts are made host-neutral. `Piko.World` is a Piko desktop adapter, not the cognitive system's source of truth.

## 分阶段验收

### C0：边界与核心抽取

- 创建不依赖 UI/宿主/模型 SDK 的纯 .NET 认知项目。
- 情绪状态演化、恢复、稳定人格偏好和行为建议可由通用类型调用。
- 桌宠适配层通过转换器调用核心；现有桌面功能不拥有状态更新公式。
- 允许探索标志只是宿主能力边界，核心不能授予权限。

### C1：记忆核心

- 定义跨宿主记忆记录、来源、置信度、敏感级别、有效时间和冲突状态契约。
- 支持显式确认、引用来源、删除/导出、去重与版本保留。
- 召回可解释、可预算、可按敏感级别过滤；对复合事实拆分和语义不确定性有显式拒答/复核路径。

### C2：情绪、动机与人格闭环

- 事件→评价→状态变化→调节→行为建议全链路可追踪。
- 稳定人格与短期状态独立；状态具备恢复、限幅、消退和重置策略。
- 连续互动测试覆盖状态累积、冲突、恢复、噪声与长时间空闲。

### C3：通用评测与效率

- 建立多宿主无关的连续场景集，分层标注记忆、状态、人格、目标、安全和成本。
- 固定基线、版本化 holdout、独立人工审查、每类回归门禁及 Token/延迟/调用成本记录。
- 评测不能只依赖输出片段匹配或单个模型裁判。

### C4：受控迭代

- 候选先限定在参数、提示词和规则等可回滚配置；后续才考虑代码候选。
- 隔离运行、记录哈希和依赖、权限最小化、超时/资源/网络限制、结果可复现。
- 自动化最多生成建议和证据包；具名维护者审核后才可合并；灰度、回滚和审计完整。

### C5：宿主适配

- Piko 桌宠只是第一个样例；至少再做一种非桌宠宿主，证明核心边界真实成立。
- 同一组认知场景可在不同宿主运行，宿主能力只影响最终可选动作，不改变核心记忆或情绪规则。

## 当前实现状态

截至 2026-11-10，`Piko.Cognition` 已承载首批通用情绪/稳态更新、人格偏好类型和行为调节建议；`Piko.World` 的 `PetMind` 与桌宠行为策略作为适配层调用它。加密记忆仍由 `Piko.Memory` 提供，当前 schema 和 UI 流程还需要继续中性化。离线改进评测是本地模型配对评测原型，并非代码级自我进化系统。当前认知核心仍未达到 C1–C5 的验收要求。

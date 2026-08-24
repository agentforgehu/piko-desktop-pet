# Piko Context Event Protocol 1.0

## 1. 事件信封

每个 Sensor 只发布版本化事件，不直接控制宠物，也不直接调用 AI。

```json
{
  "eventId": "2f024bac-1e1e-4e0f-9de5-6b7c10620102",
  "schemaVersion": 1,
  "type": "development.build.completed",
  "source": "vscode-extension",
  "timestamp": "2026-08-24T10:21:23Z",
  "sessionId": "local-session-id",
  "correlationId": "build-42",
  "confidence": 1.0,
  "capability": "terminalSummary",
  "sensitivity": "medium",
  "retention": "session",
  "data": {
    "success": { "value": "false", "sensitivity": "low" },
    "failureCategory": { "value": "module_resolution", "sensitivity": "medium" }
  }
}
```

## 2. 强制字段与限制

| 字段 | 规则 |
|---|---|
| `eventId` | UUID；用于去重 |
| `schemaVersion` | 当前为 `1`；未知版本默认拒绝 |
| `type` | 稳定的点分语义 ID，最长 128 字符 |
| `source` | Sensor/Bridge 稳定 ID，不使用设备用户名 |
| `timestamp` | UTC；超出乱序容限的事件不改变当前 Situation |
| `sessionId` | 随 Runtime 会话生成，不是账户 ID |
| `correlationId` | 可选；关联一次 build/test/agent task |
| `confidence` | `0..1`；事实事件通常为 `1` |
| `capability` | 对应一个可撤销权限 |
| `sensitivity` | `public/low/medium/high/restricted` |
| `retention` | `none/session/thirtyDays/persistent` |
| `data` | 字段值最长 4096 字符，每个字段单独标敏感等级 |

## 3. V1 事件类型

```text
presence.changed
application.foreground.changed
display.fullscreen.changed
input.intensity.changed
file.activity.changed
media.playback.changed
development.build.started
development.build.completed
development.tests.completed
development.diagnostics.changed
development.git.activity
```

不发布原始按键。窗口标题、项目名、路径、Diagnostic 正文和终端正文不能混入低敏感字段。

## 4. 默认权限

默认允许本地处理和有限保留：

```text
Presence
ForegroundApplicationCategory
FullscreenState
FileActivity
SystemHealth
```

默认拒绝：

```text
WindowTitle
ProjectIdentity
DiagnosticsSummary / Details
TerminalSummary / Output
GitMetadata
ScreenCapture
Microphone
CloudAiProcessing
AgentRead / AgentWrite
```

`AllowSession` 权限不能升级为持久保留。云端目的地还必须额外获得 `CloudAiProcessing` 权限。

## 5. Situation 输出

V1 核心输出：

```text
Unknown
Active
Away
Returned
FocusedWork
Coding
CodingBlocked
Building
Meeting
WatchingMedia
Gaming
```

Situation 必须携带开始时间、更新时间、置信度和可解释 Evidence，不将 AI 推断伪装成系统事实。

## 6. 干预规则

```text
用户直接请求
  > 安全/勿扰条件
  > 构建恢复和重复失败
  > 返回问候
  > 自主行为
```

- 用户高强度输入时不说话；
- 会议、游戏、全屏和安静时段默认不主动说话；
- 第一次或第二次构建失败仅使用无声动作；
- 连续失败达到阈值且用户空闲时才询问是否需要帮助；
- 相同行为带冷却，主动语言带每小时预算；
- 用户主动请求不受主动语言预算限制。

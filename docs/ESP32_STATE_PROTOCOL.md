# Piko ESP32/Nomi 风格状态协议 0.1

## 定位

PC 是行为和关系状态的主控端。ESP32 实体桌宠是一个轻量投影端，只显示类似 Nomi 交互范式的双眼方向与眼型，不复制 PC 端角色动画，也不使用蔚来素材。

当前 PC 端以原子方式写入：

```text
%LOCALAPPDATA%\PikoDesktopPet\device-state.json
```

后续串口、BLE 或 WebSocket 传输层直接转发同一语义对象即可。

## JSON 示例

```json
{
  "sequence": 42,
  "timestamp": "2026-08-24T08:00:00Z",
  "mode": "observingtransfer",
  "eyeShape": "focused",
  "lookX": 28,
  "lookY": 0,
  "brightness": 80,
  "message": "正在观察文件活动"
}
```

## 字段

| 字段 | 类型 | 约束 | ESP32 用途 |
|---|---|---|---|
| `sequence` | int64 | 单次 PC 进程内递增 | 丢弃乱序状态 |
| `timestamp` | ISO-8601 | UTC | 判断状态是否过期 |
| `mode` | string | 稳定语义 ID | 调试或选择过渡动画 |
| `eyeShape` | string | `normal/sleepy/focused/happy/wide` | 选择眼型 |
| `lookX` | int | -100..100 | 水平视线 |
| `lookY` | int | -100..100 | 垂直视线 |
| `brightness` | int | 0..100 | 屏幕亮度建议 |
| `message` | string | 可忽略 | 调试，不要求实体显示文本 |

## 实体端降级规则

- 未识别的 `eyeShape` 按 `normal`。
- 连接断开后保留最后状态最多五秒，然后回到 `normal`。
- ESP32 不根据 `message` 自行推断情绪。
- 所有过渡动画在实体端限制在 300ms 内，PC 新状态可以随时打断。

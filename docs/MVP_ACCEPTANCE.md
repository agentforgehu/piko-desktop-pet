# Piko Desktop Pet MVP 0.1 验收矩阵

| 要求 | 实现证据 | 自动/运行证据 | 状态 |
|---|---|---|---|
| 原创占位形象 | `PetWindow.xaml` 纯矢量角色 | WPF 进程冒烟 | PASS |
| 透明置顶、穿透切换 | WPF 透明窗口 + Win32 扩展样式 | WPF 进程冒烟 | PASS |
| 拖拽与全局召回 | 输入处理 + `Ctrl+Alt+P` 热键 | 拖放/Recall 状态机测试 | PASS |
| 窗口站立和跟随 | owner/local anchor | owner move 和 owner loss 测试 | PASS |
| 窗口边缘攀爬 | `Climbing` 状态 | 攀爬到窗口顶测试 | PASS |
| 窗口间跳跃 | 弹道和着陆检测 | 跨窗口着陆测试 | PASS |
| 屏幕边缘探头 | 屏外坐标 + 专用眼睛层 | peek/recall 测试 | PASS |
| 窗口家具 | walk/rest 状态 | 窗口 rest 测试 | PASS |
| 鼠标附近驻足 | 空闲检测、接近、72px 安全距离 | pointer approach 测试 | PASS |
| 复制/下载观察 | 目录事件 + Shell UIA 进度控件 | observing 状态测试、真实进程自检 | PASS |
| 设置、托盘、开机启动 | WPF 设置 + NotifyIcon + HKCU Run | WPF 进程冒烟 | PASS |
| 崩溃恢复 | clean-exit marker + 安全召回 | 真实进程写入 clean marker | PASS |
| 隐私日志/快照 | 标题无关快照 + 无文件名日志 | 快照 round-trip 测试 | PASS |
| ESP32 状态接口 | 原子 JSON 投影 | 简化眼型映射测试、真实文件输出 | PASS |
| Release 包 | 自包含 win-x64 发布脚本 | 解压后独立冒烟 exit 0、SHA256 通过 | PASS |
| GitHub 仓库与 Release | CI/Release workflow | 私有仓库 `agentforgehu/piko-desktop-pet`、tag `v0.1.0`、ZIP 与 SHA256 asset 已在线验证 | PASS |

当前自动化测试数：18。最终发布前必须重新执行 `scripts/verify.ps1` 和自包含包 `--smoke-test`。


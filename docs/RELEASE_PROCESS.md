# Piko 版本与文档同步流程

`release-version.txt` 是产品版本号的唯一来源。`Directory.Build.props` 从该文件生成所有 .NET 程序的 Product、Assembly 和 File Version；发布脚本、运行脚本与长稳脚本默认读取同一文件。

每个预览版或正式版必须同步：

1. `CHANGELOG.md`：新增本版本真实完成项；
2. `README.md`：当前版本、能力、命令与限制；
3. `docs/USER_GUIDE_ZH.md`：安装包名、操作、设置和已知限制；
4. `docs/V1_PRODUCTION_GAP_MATRIX.md`：当前证据，不复用过期测试数字；
5. 受功能影响的架构、隐私、安全与协议文档；
6. VS Code 扩展的 `package.json` 与 `package-lock.json`；
7. 安装器、ZIP、VSIX、更新清单及各自 SHA-256。

运行：

```powershell
.\scripts\check-version-sync.ps1
.\scripts\verify.ps1
.\scripts\publish.ps1
```

`check-version-sync.ps1` 是硬门禁。任一必需文档或扩展版本不一致时，验证和发布立即失败。历史 CHANGELOG、历史证据和依赖包自身的版本号不应被批量替换。

发布前还必须完成真实 Desktop/Runtime smoke；正式版继续遵守代码签名、哈希、更新来源和长稳要求。


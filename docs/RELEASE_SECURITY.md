# Piko 1.0 安装、签名与更新发布

## 发布物

产品版本由 `release-version.txt` 单点定义。`scripts/check-version-sync.ps1` 会检查 README、CHANGELOG、用户指南、生产差距矩阵、安装器版本源和 VS Code 扩展；不同步时 `verify.ps1` 与 `publish.ps1` 都会失败。

`scripts/publish.ps1` 生成：

- `Piko-<version>-Setup.exe`：当前用户单文件安装器；
- `Piko-<version>-win-x64.zip`：免安装便携包；
- `piko-context-bridge-<version>.vsix`：VS Code Bridge；
- `update-manifest.json`：受边界校验的更新清单；
- 每个发布物的 SHA-256 文件。

Setup 安装到 `%LOCALAPPDATA%\Programs\PikoDesktopPet`。新 payload 先解压到随机 staging，旧 app 移到 backup，新 app 切换失败时恢复旧 app。它注册当前用户“已安装的应用”、App Paths、开始菜单快捷方式和卸载命令，不申请管理员权限。

## Authenticode 门禁

稳定版本默认必须签名。示例：

```powershell
.\scripts\publish.ps1 `
  -Version 1.0.0 `
  -SignToolPath 'C:\Program Files (x86)\Windows Kits\10\bin\<sdk>\x64\signtool.exe' `
  -SigningCertificateThumbprint '<CURRENT_USER_MY_CERT_SHA1>'
```

脚本对 Desktop、Runtime 和 Setup 使用 SHA-256 Authenticode 和 RFC 3161 时间戳，并在打包后调用 `signtool verify /pa`。没有证书时 Alpha 可以生成；稳定 semver 会直接失败。候选版使用 `-Channel preview`，生成的清单不会允许自动安装。

拿到正式证书后还必须把发布者证书指纹写入 `TrustedUpdateSigners.Thumbprints` 并重新构建。更新器只有同时满足以下条件才执行：

1. 清单 schema、版本、GitHub 仓库 URL 和大小合法；
2. 下载最终落在允许的 GitHub Release/CDN HTTPS 主机；
3. 实际字节数和 SHA-256 与清单完全一致；
4. `WinVerifyTrust` 通过 Authenticode 链与吊销检查；
5. 签名叶证书指纹命中编译时白名单。

任一失败都会删除 partial 文件并保持当前版本不变。下载完成后，Setup 等待当前 Piko 进程干净退出，再进行 staging/backup 切换。

## 正式 1.0 仍需发布者提供

- Windows Authenticode 代码签名证书，或 Azure Trusted Signing 的账户、证书配置文件和 CI 授权；
- 最终 Publisher/公司名称、官网、隐私政策 URL 和安全联系地址；
- 一个仅用于真实 AI 验收的 OpenAI-compatible API Key；Key 不提交仓库；
- 确认最终默认模型和允许使用的国家/地区/合规策略；
- 最终角色美术、图标和音频授权材料（当前原创矢量占位可继续使用）；
- 正式 RC 至少 30 分钟本机长稳报告，推荐另做 8 小时夜间 soak。


## GitHub Actions 签名接入

Release 工作流在稳定版打包之前运行 `scripts/check-release-ready.ps1`。当前 `release-approval.json` 明确为未批准；只有已完成的发布者与实机验收才能改为通过。

现有 CI 支持把生产代码签名 PFX 存入仓库 Actions secret `PIKO_SIGNING_PFX_BASE64`，密码存入 `PIKO_SIGNING_PFX_PASSWORD`。不要在聊天、仓库文件或日志中提供证书私钥或密码。CI 在临时 runner 的当前用户证书库导入证书，核对指纹白名单，签名与验签后清理。仅可使用组织允许导出的生产证书；使用硬件密钥或 Azure Trusted Signing 时需接入相应签名提供方，此路径当前尚未实现。

签名不能代替身份、隐私和交互验收。工作流不会自动填写发布批准结果。

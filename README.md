# Copilot Usage for Windows

Windows 原生系统托盘工具，使用 C# / .NET 10 / WinForms / WebView2。
每 5 分钟读取 **GitHub Settings → Copilot → Features → Usage** 中的
**Usage this cycle** 卡片，不使用 Billing 账单接口。

本项目是独立社区工具，与 GitHub、Microsoft 或 ByteDance 无隶属、背书关系。
GitHub 和 Copilot 等名称仅用于说明兼容的服务。

当前为**公开源码预览版本**，可从
[GitHub 仓库](https://github.com/Abo-Fat/copilot-usage-monitor) 获取源码。
尚未发布 Releases 安装包，也未配置自动构建；请按“构建与离线回归”一节在 Windows 上构建。
文档和离线测试使用人工构造的账号及用量数据，不代表任何真实账户的额度或用量。

## 启动与登录

完成本地构建后，打开 `artifacts\win-x64\CopilotUsage.exe`，或运行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Run.ps1
```

构建脚本生成的本地程序包含 .NET 运行时；网页登录需要 Microsoft Edge WebView2 Runtime
（Windows 11 通常已安装）。缺少时程序会提示，安装来源：
<https://developer.microsoft.com/microsoft-edge/webview2/>

1. 点击“登录与设置”→“登录 GitHub / 重新读取”。
2. 在应用的独立 GitHub 窗口中完成登录和双重验证，不要将密码或验证码发送到聊天。
3. 打开 Features 页面，点击窗口底部“读取此账号用量”。
4. 确认账号和卡片数值，点击“保存设置”开始监控。

无需 PAT，不填写手动总额度，也不选择产品/SKU。
“打开 GitHub 用量页”会在默认浏览器打开
<https://github.com/settings/copilot/features>；默认浏览器的登录状态与应用互相独立。
登录窗口仅允许 `https://github.com`，不支持需要跳转外部身份提供方的 SSO。

关闭主窗口仅隐藏到托盘；真正退出使用托盘右键菜单“退出”。
再次启动会激活已有窗口，不会产生第二个后台实例。
需要自启动时，在设置中显式勾选，仅影响当前 Windows 用户。
Windows 可能将图标放在任务栏隐藏图标区域，可手动拖到常显区域。
当前本地构建未签名，Windows 可能提示未知发布者。

## 数据口径

程序直接读取页面显示的已用量、总额度及重置说明。以下为人工示例：

| 页面内容 | 程序含义 |
|---|---|
| `123 / 1,000,000 AI credits` | 本周期已用 123；页面额度 1,000,000 |
| `Resets in 21 days on Sep 30, 2026` | 页面重置日期 2026-09-30，保留页面原文 |
| `456 AI credits used` | 已用 456；页面没有提供总额度，剩余及百分比未知 |

剩余为“页面额度 − 已用”（最低显示 0），百分比由页面数字计算；
二者不是独立的实时余额接口，也不会将账单金额、Token 数或 `grossQuantity`
当作此卡片的用量。

**不再假设每月 1 日 00:00 UTC 重置。** 页面只给日期时就只显示日期，
不自行补充时刻、时区，也不把旧快照的相对天数当作实时倒计时。
识别不了的日期保留原文并显示未知。

主窗口展示卡片原文，便于与 GitHub 对照。采集只匹配 `Usage this cycle`
及其中的 AI credits；不会从其他账单或其他卡片补数。网页使用英文界面，
如果 GitHub 更改页面结构或用量格式，程序会明确报错，不自动切回 Billing。
没有卡片、未登录、账号不符或读取失败都不表示零用量。

“最后成功获取”是本地读取时间，不代表 GitHub 数据已经更新到该时刻。
失败保留最后成功快照并标记过期；本地缓存与超过 10 分钟的数据也会标注。
托盘使用 `icons` 中提供的电池 SVG 轮廓，按**剩余额度**显示 0–4 格：
大于 75% 为 4 格，大于 50% 至 75% 为 3 格，大于 25% 至 50% 为 2 格，
大于 0% 至 25% 为 1 格，耗尽为 0 格。没有总额度或无法确认比例时，
显示空轮廓加 `?`，不把未知用量当作耗尽。
同步时显示蓝色小圆点，过期或错误时显示黄色 `!` 小标记并保留上次电量；
重试期间仍保留错误标记。图标适配 Windows 任务栏的深浅主题、高对比度和 DPI，
不再用整个橙色圆圈替换应用图标。
手动刷新有防抖；网络错误退避重试，登录或格式错误需要重新连接。

## 旧版本迁移

旧版本错误地监控了 Billing 账单。升级后不再读取旧的 PAT、SKU 映射、
手动总额度或 `snapshot.json`，也不会把旧账单缓存转换为网页额度。
首次需重新网页登录。旧设置文件保留到用户主动保存新设置，之后保存为版本 2。

旧 PAT 不再需要，可在 GitHub 撤销；本应用不会自动撤销令牌。
如需清理旧凭据，可在 Windows 凭据管理器中删除
`CopilotUsage.Tray/GitHubClassicPat`。

## 本地数据与隐私

数据位于 `%LOCALAPPDATA%\CopilotUsage`：

| 路径 | 内容 |
|---|---|
| `settings.json` | 已确认账号、配置版本 |
| `features-snapshot.json` | 最后成功读取的账号、用量、额度、日期及卡片原文 |
| `GitHubWebView2\` | 本应用独立 WebView2 会话、Cookie 和浏览器数据 |

不导入其他浏览器的 Cookie，不读取其他应用凭据，不保存或打印密码，
禁用浏览器密码自动保存，不导出完整网页、Cookie 或认证头。
网页本身可能加载 GitHub 的静态资源、验证码等依赖。
程序不发送 AI 推理请求，不修改预算、不购买额外额度。

网页登录具有账号权限；只在可信设备使用，保护好浏览器配置目录。
点击“退出此应用的 GitHub 登录”会清除此独立浏览器配置的浏览数据并断开监控，
不影响其他浏览器；这不等同于在 GitHub 全局撤销所有会话。
缓存和卡片原文仍是个人数据，分享前请检查。

退出登录不删除 `settings.json` 或 `features-snapshot.json`。如需清理全部本地数据，
先在设置中关闭自启动并退出此应用的 GitHub 登录，再从托盘菜单真正退出程序，
最后删除本应用的 `%LOCALAPPDATA%\CopilotUsage` 目录；这会丢失设置和缓存，
不会删除其他浏览器的数据，也不等于撤销 GitHub 上的所有会话。

反馈问题时只提供脱敏后的复现步骤、版本和必要错误信息。不要附上浏览器配置目录、
Cookie、PAT、认证头、真实用量缓存、完整网页或未经脱敏的截图。
普通问题可提交脱敏后的 [Issue](https://github.com/Abo-Fat/copilot-usage-monitor/issues)；
安全漏洞请使用 [私密漏洞报告](https://github.com/Abo-Fat/copilot-usage-monitor/security/advisories/new)，
不要在公开 Issue 中披露。报告要求见 [SECURITY.md](SECURITY.md)。
本地 SDK、构建产物、AI 工具本机配置及运行数据不属于源码分发内容；
`.gitignore` 只是防误提交措施，不能替代上传前检查，也不会清除已提交的历史。

## 构建与离线回归

需要 Windows 和 [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)。
仓库不包含 SDK；安装后确认 `dotnet --version` 能找到 .NET 10 SDK。
`global.json` 允许使用较新的 .NET 10 feature band。
构建脚本优先使用可选的 `.tools\dotnet`，否则使用系统 `dotnet`。

首次获取源码并构建：

```powershell
git clone https://github.com/Abo-Fat/copilot-usage-monitor.git
Set-Location .\copilot-usage-monitor
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Build.ps1
```

已有源码时，只需在仓库根目录运行最后一条构建命令。

脚本构建解决方案、运行数据逻辑与托盘图标回归，并生成 `artifacts\win-x64`。
这里的 `dotnet publish` 只生成本地文件，不会上传 GitHub。
首发源码以 Windows x64 为验证目标。ARM64 可指定 `-Runtime win-arm64`，
但构建选项不代表已经完成 ARM64 实机验证。

电池 PNG 已内嵌，正常构建和运行不需要 Node.js。更换原始 SVG 后，
使用 Node.js 重新生成 16、20、24、32、40、48、64 像素的透明资源：

```powershell
npm --prefix .\tools\icon-assets ci
npm --prefix .\tools\icon-assets run generate
```

生成器使用提供的满电和空电 SVG，删减独立电量格得到中间状态；
SVG 的图形保持不变，并保留来源和修改声明。生成的 PNG 内含许可及修改说明，
重新生成时会自动保留这些元数据。“工作中”SVG 与满电几乎相同，因此同步状态改用更易识别的蓝点。
若以后重新找图标，优先选择透明背景、简单粗线条的 SVG，或含 16/20/24/32/48/64
多尺寸的透明 ICO；避免照片、复杂细节和不透明底色。

```powershell
dotnet run --project .\tests\CopilotUsage.Tests
.\artifacts\win-x64\CopilotUsage.exe --smoke-test
.\artifacts\win-x64\CopilotUsage.exe --icon-smoke-test
```

若只安装了可选的本地 SDK，将上面的 `dotnet` 替换为 `.\.tools\dotnet\dotnet.exe`。

`--smoke-test` 使用人工构造的卡片和独立临时 WebView2 配置，验证页面提取和窗口，
模拟失败后自动退出。不读取真实设置、缓存或浏览器会话，不导航到 GitHub。
`--icon-smoke-test` 单独验证电池格数、明暗/高对比度配色、多尺寸和透明图标，
不启动浏览器、不读取用量或登录会话。

`tools\Read-CopilotBilling.ps1` 仅保留为**旧 Billing 诊断工具**，
不是本程序的数据源，也不能用于核对 Features → Usage 数值。
正常使用本程序不需要运行该工具，也不需要提供 PAT。

## 许可证与第三方资源

项目自有代码和文档使用 [MIT License](LICENSE)，版权署名为 `Abo-Fat`。
MIT 允许商用、修改和再分发，要求保留版权及许可声明，并按许可证提供免责条款。

电池 SVG 和由其生成的 PNG 来自 [IconPark](https://iconpark.oceanengine.com/official)，
保留 **Apache-2.0** 许可，不适用本项目的 MIT 声明。
上游许可全文见 [IconPark-LICENSE.txt](licenses/IconPark-LICENSE.txt)，
来源、修改及依赖许可见 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。
分发这些资源时，应一并保留对应许可和声明。

当前只分发源码和必需的图标资源，不包含下载的依赖、SDK 或应用安装包。
今后分发自包含程序时，需另行核对 .NET、WebView2 等随包组件的许可，
并将相应许可证和声明放入发布包。

## 参考

- [Copilot Features](https://github.com/settings/copilot/features)
- [GitHub 用量监控说明](https://docs.github.com/en/copilot/how-tos/manage-and-track-spending/monitor-ai-usage)
- [Microsoft WebView2](https://learn.microsoft.com/en-us/microsoft-edge/webview2/)

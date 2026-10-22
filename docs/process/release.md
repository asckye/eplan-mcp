# 0.x 预览版发布

依据 [发布清单](development-workflow.md#8-版本与发布)、[ADR-0016](../adr/0016-per-user-installer.md)、[ADR-0020](../adr/0020-no-self-hosted-runner.md) 和 [M5 设计](../design/2026-10-20-m5-operability.md)。0.x 的程序和安装包不签名；附 SHA-256、CycloneDX SBOM、GitHub 来源证明。全程保留草稿，最后由维护者手动发布。

## 发布前

- 将 CHANGELOG 的 Unreleased 整理为目标版本，确认 main 的常规 CI 通过。
- M5 第 10 步必须先合并：`packaging/claude-plugin/` 提供本地市场、`eplan-mcp/` 插件、技能和固定的 `${USERPROFILE}/EplanMcp/app/eplanmcp.exe` 配置；`docs/getting-started/zh-CN.md` 提供中文指南。当前实现引用这些未来输入，缺少时打包失败，不生成残缺安装包。插件版本须与标签一致。
- 维护者准备 PowerShell 7.4+、global.json 指定的 SDK、git、已登录的 gh、本机 EPLAN 2026 引用程序集和授权；关闭示例夹具，供脚本复制。脚本默认使用 2026.0.3 的 Platform 与 Electric P8 Bin，可用参数覆盖这两个目录，但引用程序集仍须属于 2026。
- 另行完成界面中的 Large 测试、Agent 评测、Workbench 冒烟检查（讲述人、高对比度、200% 缩放、中英文和键盘），并记录结果；离线宿主测试不能证明插件注册、审批窗口或 Ctrl+Z 正确。

## 标签、草稿与插件交接

以下命令由维护者执行；示例版本为 `v0.1.0`，在仓库根目录的 PowerShell 中运行。

```powershell
git tag v0.1.0 main
git push origin v0.1.0
gh release create v0.1.0 --repo asckye/eplan-mcp --verify-tag --draft --prerelease --title 'v0.1.0 preview' --notes '预览版，验收记录待补齐，请勿发布。'
eng/scripts/release-addin.ps1 -Tag v0.1.0 -WhatIf
eng/scripts/release-addin.ps1 -Tag v0.1.0
```

`-WhatIf` 只显示计划，不联网、不建 worktree、不构建、不启动 EPLAN、不上传。实际执行会核对本地标签与远端标签（含附注标签的提交），在系统临时目录建立新的 detached worktree，确认干净，按标签版本构建 Release 插件（`EplanSign=false`、锁定还原）。工作区中的未提交修改不会进入产物。

脚本还从该 worktree 构建离线宿主及 Large 测试，将示例复制到该 worktree 的 scratch，在隔离管道上运行 `--filter-trait 'Host=Offline'`。无通过测试、有失败或跳过时不上传。临时进程环境恢复后退出，不修改 PATH、全局环境、插件注册或工程师的项目。

成功后输出 `EplanMcp-addin-v0.1.0.zip`、同名 `.zip.sha256`，并只上传这两个文件到**已有草稿**；禁止覆盖已有插件附件。ZIP 仅含三个产品 DLL、插件 SBOM 和标签/提交/测试数量记录，不含 EPLAN 程序集、授权、项目和测试日志。构建、测试输出及包保留在打印出的临时 worktree，供核查；保存需要的内容后，按脚本打印的 `git worktree remove --force -- ...` 命令清理。

将脚本打印的以下整行原样粘贴到 GitHub 草稿发布说明中，用实际哈希替换占位符；不要放进列表、行内代码或代码围栏：

```text
Add-in SHA-256: <64 位小写十六进制 SHA-256>  EplanMcp-addin-v0.1.0.zip
```

发布说明还须写明“插件由维护者在开发机上从标签 v0.1.0 构建”，附提交号、离线 Large 结果、其他实机和 Agent 评测结果，以及支持矩阵（EPLAN 版本、Electric P8 / Pro Panel、所需 API 与未签名插件授权）。支持 2024–2027 的边界与实际验证过的版本应分开列出，参见 [支持矩阵](../eplan/eplan-versions.md)。

标签 push 会立即触发 workflow；此时草稿或附件尚未就绪，addin 作业会明确失败。这是有意的交接关卡，不轮询开发机，也不使用自托管 runner。补齐上述附件和发布说明后，重跑该标签的整次执行：

```powershell
gh run list --repo asckye/eplan-mcp --workflow release.yml --branch v0.1.0
gh run rerun <上一步的运行 ID> --repo asckye/eplan-mcp
```

## CI 输出与核查

三个作业分别构建/测试 Host 与 Workbench、验证插件、打包；只有打包作业有 `contents`、`id-token`、`attestations` 写权限。插件 ZIP 的实际哈希必须同时符合附件 `.sha256` 和发布说明；标签、提交和测试记录也必须符合。CI 不执行 EPLAN，不自动发布 release。

Host 为 win-x64 自包含单文件；Workbench 为 win-x64 自包含目录，附经固定哈希验证的 Maple Mono CN 字体及许可证。发布使用独立的 `packages.win-x64.lock.json`，常规 CI 继续使用原锁文件，两者都保持 locked mode。CycloneDX 6.0.0（Apache-2.0，仅构建工具）生成三个项目的 SBOM 后合并，并补充随包运行时、字体和 TIA 主题。编译器固定 Inno Setup 6.7.1；托管镜像中的版本变化会失败，须审查并更新版本固定后重试。

附件包括安装包、维护者插件 ZIP/记录、合并 SBOM、安装文件清单及来源/SBOM 证明包。每个实质附件有 `.sha256`，`SHA256SUMS.txt` 汇总它们；校验和文件不递归计算自己的哈希。安装包及其每个文件均纳入来源证明。插件证明只证明 CI 收到的文件，不能证明维护者开发机的源码构建过程。

```powershell
Get-FileHash .\EplanMcp-Setup-0.1.0.exe -Algorithm SHA256
Get-Content .\EplanMcp-Setup-0.1.0.exe.sha256
gh attestation verify .\EplanMcp-Setup-0.1.0.exe --repo asckye/eplan-mcp
```

离线机器核对 SHA-256；联网时可用 `gh attestation verify` 核验 GitHub 证明。维护者须下载最终安装包，在标准用户、无开发工具、断网的干净 Windows 虚拟机上完成 I1–I10，记录 SmartScreen 和智能应用控制状态。特别验证固定目录及 `/DIR` 覆盖无效、无提权/环境修改、变体注册失败的提示、运行中拒绝升级/卸载、EPLAN 运行时升级后重启生效、拒绝降级、自启动默认开启且可关闭、卸载撤销失败时不删程序、成功卸载保留默认和自定义数据根目录。静默安装不勾选变体，工程师随后运行 `install-addin --variant ...`。

卸载时可选移除 `client-config` 记录的客户端配置，默认保留；静默卸载保留。也可在卸载前手动运行 `eplanmcp client-config --remove`；Claude Code 插件通过其插件命令移除。一台电脑由一个 Windows 用户使用 EplanMcp（Q15）。确认所有证据齐全后，维护者在 GitHub 页面手动发布草稿。

## 评审拆分

- 安装器：`eng/installer/EplanMcp.iss`。
- 插件发布脚本：`eng/scripts/release-addin.ps1`。
- 发布 workflow 与文档：`.github/workflows/release.yml`、`eng/installer/Release.pubxml`（仅对 Host 启用单文件打包，固定 SDK 的单文件分析器）、本文、CHANGELOG 的新增一行，以及 `src/` 下 Host、Workbench、Contracts、Packs、Workbench.Protocol 的五个生成的 `packages.win-x64.lock.json`（生成文件不计评审行数）。

实现按 M5 的最新 4.7 使用随包固定 `.mcp.json`，取代 ADR-0016 中较早的“安装时写绝对路径”。卸载在删除前先执行并检查撤销结果，`[UninstallRun]` 保留幂等兜底。其余未改变已接受设计。Inno 编译、真实 GitHub Actions 和实机验收须由维护者完成。

回归检查在常规依赖还原后运行 `tests/release/Release.Tests.ps1`，也由发布作业执行：解析 YAML、脚本和内嵌 PowerShell，检查 action SHA，并用假附件验证正常交接及失败路径；不会调用 EPLAN、git、gh 或网络。

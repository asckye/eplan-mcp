# 0.x 预览版发布

依据 [发布清单](development-workflow.md#8-版本与发布)、[ADR-0016 的 ZIP 修订](../adr/0016-per-user-installer.md)、[ADR-0020](../adr/0020-no-self-hosted-runner.md) 和 [M5 设计](../design/2026-10-20-m5-operability.md)。只交付 ZIP，0.x 程序不签名；附 SHA-256、CycloneDX SBOM、GitHub 来源证明。全程保留草稿，最后由维护者手动发布。

## 发布前

- 将 CHANGELOG 的 Unreleased 整理为目标版本，确认 main 的常规 CI 通过。
- ZIP 改造分两次审阅：Part A 为流水线与文档；Part B 的 Workbench 注册/撤销、任意程序目录 Host 定位及本地市场生成必须合并后才发布。`packaging/claude-plugin/` 提供模板和技能，`client-config` 在数据根目录生成含实际 Host 绝对路径的市场；中英文指南同步到技能副本。插件版本须与标签一致，缺少输入时打包失败。
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

成功后输出 `EplanMcp-addin-v0.1.0.zip`，并只上传这个文件到**已有草稿**；禁止覆盖已有插件附件。ZIP 仅含三个产品 DLL、插件 SBOM 和标签/提交/测试数量记录，不含 EPLAN 程序集、授权、项目和测试日志。构建、测试输出及包保留在打印出的临时 worktree，供核查；保存需要的内容后，按脚本打印的 `git worktree remove --force -- ...` 命令清理。

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

三个作业分别构建/测试 Host 与 Workbench、验证插件、打包；插件作业读取草稿需 `contents: write`，只有打包作业有 `id-token`、`attestations` 写权限。插件 ZIP 的实际哈希必须符合发布说明中唯一且格式完全一致的 `Add-in SHA-256: …  EplanMcp-addin-vX.Y.Z.zip` 行，上传前再次核对；manifest JSON 的标签、提交和测试记录也必须符合。CI 不执行 EPLAN，不自动发布 release。

Host 为 win-x64 自包含单文件；Workbench 为 win-x64 自包含目录，附经固定哈希验证的 Maple Mono CN 字体及许可证。发布使用独立的 `packages.win-x64.lock.json`，常规 CI 继续使用原锁文件，两者都保持 locked mode。CycloneDX 6.0.0（Apache-2.0，仅构建工具）生成三个项目的 SBOM 后合并，并补充随包运行时、字体和 TIA 主题。暂存 `app\` 的全部内容打进唯一顶层目录 `EplanMcp-<version>\`，包括点目录；不套 `app\`，不调用 Inno Setup。

以 `v0.1.0` 为例，发布保留以下 8 个附件：

- `EplanMcp-0.1.0.zip`：工程师下载的完整程序包，顶层目录 `EplanMcp-0.1.0\`；打开其中 `workbench\EplanMcp.Workbench.exe`。
- `SHA256SUMS.txt`：所有其他附件的 SHA-256，包括插件 ZIP、manifest、安装文件清单和两个证明包；不计算自身的哈希。
- `EplanMcp-v0.1.0.cdx.json`：合并的 CycloneDX SBOM。
- `EplanMcp-v0.1.0-provenance.sigstore.json`：来源证明包。
- `EplanMcp-v0.1.0-sbom.sigstore.json`：SBOM 证明包。
- `EplanMcp-v0.1.0-files.sha256`：ZIP 内每个文件的路径和 SHA-256（含版本顶层目录），供解压后逐文件核查。
- `EplanMcp-addin-v0.1.0.zip`：维护者构建的插件包。
- `EplanMcp-addin-v0.1.0.json`：从插件包提取的标签、提交和测试记录（manifest）。

不为每个附件单独生成 `.sha256` 文件。逐文件清单作为附件；来源证明的完整 subject 清单另存于工作区 `.local-subjects.sha256`，不上传。ZIP 及其每个文件均纳入来源证明，SBOM 证明绑定这个完整 ZIP；插件证明不能证明维护者开发机的源码构建过程。插件 ZIP 仅用于 ADR-0020 交接和核查，工程师无需另下。

```powershell
$asset = 'EplanMcp-0.1.0.zip'
$expected = @(Get-Content .\SHA256SUMS.txt | Where-Object { $_ -cmatch "^[0-9a-f]{64}  $([regex]::Escape($asset))$" })
if ($expected.Count -ne 1) { throw 'Missing or duplicate SHA-256 entry.' }
$actual = (Get-FileHash -LiteralPath ".\$asset" -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actual -cne $expected[0].Substring(0, 64)) { throw 'SHA-256 mismatch.' }
gh attestation verify ".\$asset" --repo asckye/eplan-mcp
```

离线核对 SHA-256，联网可核验 GitHub 证明。维护者须下载最终 ZIP，在标准用户、无开发工具、断网的干净 Windows 虚拟机上完成 I1–I10，记录 SmartScreen 和智能应用控制状态。校验后先在 ZIP 属性中解除锁定，再解压到短且固定的任意目录；测试从 `workbench\EplanMcp.Workbench.exe` 勾选变体注册、客户端绝对路径和生成市场、失败提示、撤销后删程序及数据保留。升级必须先关闭 EPLAN、Workbench、客户端，再整体替换同一路径的程序目录；不依赖安装器做进程检查或拒绝降级。

卸载先移除需要清理的客户端配置/Claude Code 市场，在 Workbench 关闭登录自启动、撤销注册，成功后退出并删除程序目录。默认及自定义数据根目录均保留；程序移动后须重新注册、重写客户端配置或生成市场。一台电脑由一个 Windows 用户使用 EplanMcp（Q15）。确认所有证据齐全后，维护者手动发布草稿。

## 评审拆分

- ZIP 打包：`.github/workflows/release.yml`；删除 `eng/installer/EplanMcp.iss` 和 build 的安装脚本检查。
- 插件发布脚本：`eng/scripts/release-addin.ps1`。
- 发布 workflow 与文档：`.github/workflows/release.yml`、`eng/installer/Release.pubxml`（仅对 Host 启用单文件打包，固定 SDK 的单文件分析器）、本文、CHANGELOG 的新增一行，以及 `src/` 下 Host、Workbench、Contracts、Packs、Workbench.Protocol 的五个生成的 `packages.win-x64.lock.json`（生成文件不计评审行数）。

保留 `eng/installer/Release.pubxml`：Host 的单文件发布仍引用它。Part A 不改 UI 或插件生成代码；Part B 完成后再执行真实 GitHub Actions 和实机验收。

回归检查运行 `pwsh -NoProfile -File tests/release/Release.Tests.ps1`，也由发布作业执行：检查 YAML、PowerShell 和 action SHA，以假附件验证交接与失败路径，并实际验证 ZIP 顶层目录、点文件、逐文件哈希、两类证明 subject 和附件清单；不会调用 EPLAN、git、gh 或网络。

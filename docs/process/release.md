# 0.x 预览版发布

依据 [手册开发流程条款](../standards/engineering-handbook.html#build-release)、[ADR-0016 的 ZIP 修订](../adr/0016-per-user-installer.md)、[ADR-0020](../adr/0020-no-self-hosted-runner.md) 和 [M5 设计](../design/2026-10-20-m5-operability.md)。只交付 ZIP，0.x 程序不签名；附 SHA-256、CycloneDX SBOM、GitHub 来源证明。全程保留草稿，最后由维护者手动发布。

## 每周预览版（发布列车）

当前 MCP SDK 固定版本、协议 revision 及客户端兼容注释以 [`Directory.Packages.props`](../../Directory.Packages.props) 为来源；运行时绑定见[架构协议说明](../architecture/architecture.md)。

负责人于 2026-11-01 决定：每周五从 main 发布预览版，首班为 **2026-11-06 的 v0.4.1**，此后按 0.4.x 递增 patch，直到里程碑版 **v0.5.0**；里程碑验收目标保持不变。发布列车不引入破坏性契约变化，此类变化仍归 v0.5.0。`release.yml` 的 `tags: ['v*']` 和 `^v0\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$` 校验均接受 v0.4.1、v0.4.2 等标签；无需改 workflow，也不增加定时发布。

只有受影响 Large 类已通过的功能可默认开启。逐行审阅 [待验台账](large-pending.md)：未关闭项保持默认关闭，或在发布说明中逐项明确标为“未实机验证”，不能将其算作已验证发布范围；`apiTry`、`actionTry` 已默认关闭，继续保持。随包携带待验实现不代表发布验收通过，也不改变发布范围内的 Large、Agent 评测和支持矩阵要求。

每班由维护者完成：

1. 同步 main 和标签（`git fetch origin main --tags`），在干净且 `HEAD == origin/main` 的检出中运行 `pwsh eng/scripts/release-prep.ps1 -Version 0.4.1`（后续换为下一 patch）。脚本只更新三个产品项目的构建版本及 Claude 插件模板版本、将 CHANGELOG Unreleased 移入带当天日期的新版本节，并生成 `.local/release/v0.4.1-notes.md`；留下空的 Unreleased。它只读取本地 Git 引用，不联网、不提交、不打标签、不推送、不创建发布。
2. 审阅从 Unreleased 生成的中文说明和台账未关闭行，核对默认开关、受影响 Large 通过证据及明确排除的待验范围；补齐支持矩阵、Agent 与界面验收记录。台账缺失或格式不明仅提示证据缺口，由维护者核对。
3. 审阅并提交准备改动，在干净工作区执行 `pwsh eng/scripts/local-gate.ps1 -Full -Push`；必须是最终发布提交关卡全绿且已落到 main，不能沿用旧提交结果。准备脚本后的提交与关卡均为人工步骤；关卡若变基，重新核对该提交的验证证据。
4. 在该提交的 Release 构建上执行 `pwsh eng/scripts/smoke.ps1`（可用 `-HostPath` 指定已构建的 Host），完成 `doctor --offline`（未安装 EPLAN 只记警告）、stdio initialize 与 tools/list 往返；离线自检明确标记未检查实时 Bridge 与授权，不需要 EPLAN，任何失败均须处理，不能替代实机 Large。
5. 在通过关卡的准确提交上打标签、推送标签，再用生成说明创建 **draft prerelease**；按下节执行 ADR-0020 插件构建与 **Add-in SHA-256 整行交接**。标签首次工作流可能因交接未完成而失败；补齐草稿、附件和哈希后重跑整次工作流。
6. 核对 CI 最终产物、哈希、实机证据和最终 ZIP 验收，最后由维护者手动发布草稿。脚本不会执行上述发布动作。

若某个周五没有新增且完成验证的内容，**跳过该班**，在 [路线图](../roadmap.md) 记录日期与跳过原因，不消耗 patch 号、不为空列车打标签。

## 发布前

- 将 CHANGELOG 的 Unreleased 整理为目标版本，确认 main 的常规 CI 通过。
- 打标签前核对 `packaging/zip/README.md`：它会复制到 ZIP 根目录 `README.md`，必须与发布说明携带同一份支持矩阵，逐项检查下面的清单。
- 维护者审阅后通过本地关卡直接落到 main；从同一提交准备版本、验证并打标签。核对 Workbench 注册/撤销、任意程序目录 Host 定位与本地市场生成；`packaging/claude-plugin/` 提供模板和技能，`client-config` 在数据根目录生成含实际 Host 绝对路径的市场，中英文指南同步到技能副本。插件版本须与标签一致，缺少输入时打包失败。
- 维护者准备 PowerShell 7.4+、global.json 指定的 SDK、git、已登录的 gh、本机 EPLAN 2026 引用程序集和授权；关闭示例夹具，供脚本复制。脚本默认使用 2026.0.3 的 Platform 与 Electric P8 Bin，可用参数覆盖这两个目录，但引用程序集仍须属于 2026。
- 另行完成界面中的 Large 测试、Agent 评测、Workbench 冒烟检查（讲述人、高对比度、200% 缩放、中英文和键盘），并记录结果；离线宿主测试不能证明插件注册、审批窗口或 Ctrl+Z 正确。

## 标签、草稿与插件交接

以下命令由维护者执行；示例版本为 `v0.4.1`，在仓库根目录的 PowerShell 中运行。

```powershell
git tag v0.4.1 HEAD
git push origin v0.4.1
gh release create v0.4.1 --repo asckye/eplan-mcp --verify-tag --draft --prerelease --title 'v0.4.1 preview' --notes-file .local/release/v0.4.1-notes.md
eng/scripts/release-addin.ps1 -Tag v0.4.1 -WhatIf
eng/scripts/release-addin.ps1 -Tag v0.4.1
```

`-WhatIf` 只显示计划，不联网、不建 worktree、不构建、不启动 EPLAN、不上传。实际执行会核对本地标签与远端标签（含附注标签的提交），在系统临时目录建立新的 detached worktree，确认干净，按标签版本构建 Release 插件（`EplanSign=false`、锁定还原）。工作区中的未提交修改不会进入产物。

脚本还从该 worktree 构建离线宿主及 Large 测试，将示例复制到该 worktree 的 scratch，在隔离管道上运行 `--filter-trait 'Host=Offline'`。无通过测试、有失败或跳过时不上传。临时进程环境恢复后退出，不修改 PATH、全局环境、插件注册或工程师的项目。

成功后输出 `EplanMcp-addin-v0.4.1.zip`，并只上传这个文件到**已有草稿**；禁止覆盖已有插件附件。ZIP 仅含三个产品 DLL、插件 SBOM 和标签/提交/测试数量记录，不含 EPLAN 程序集、授权、项目和测试日志。构建、测试输出及包保留在打印出的临时 worktree，供核查；保存需要的内容后，按脚本打印的 `git worktree remove --force -- ...` 命令清理。

将脚本打印的以下整行原样粘贴到 GitHub 草稿发布说明中，用实际哈希替换占位符；不要放进列表、行内代码或代码围栏：

```text
Add-in SHA-256: <64 位小写十六进制 SHA-256>  EplanMcp-addin-v0.4.1.zip
```

发布说明还须写明“插件由维护者在开发机上从标签 v0.4.1 构建”，附提交号、离线 Large 结果、其他实机和 Agent 评测结果，并按以下清单列出“支持矩阵”（版本边界见 [ADR-0009](../adr/0009-supported-eplan-versions.md) 和[版本兼容性](../eplan/eplan-versions.md)）：

- [ ] 已验证 EPLAN 年份、完整构建号与变体：2026，实测 2026.0.3.25421，Electric P8、Pro Panel。
- [ ] 尚未验证：2024、2025、2027，留待 M7；与已验证版本分开列出。
- [ ] 暂不支持：2022、2023。
- [ ] 授权须包含 API Extension（API 扩展）。
- [ ] 授权须允许加载未签名 API 插件；0.x 插件未签名（[ADR-0007](../adr/0007-licensing-and-distribution.md)）。
- [ ] Windows 10/11 x64；本机安装与连接无需管理员权限，虚拟机网络权限需管理员授权。
- [ ] 客户端：Claude Code 与其他 stdio MCP 客户端；虚拟机连接须支持 Streamable HTTP 和自定义请求头，Claude 桌面版不适用，仅限宿主机与虚拟机私有网络、HTTP 不加密；仅 Claude Code 连接已实测，其他客户端、跨机器连接与 UAC 待验收，见[虚拟机部署](../getting-started/zh-CN.md#10-虚拟机部署)。

标签 push 会立即触发 workflow；此时草稿或附件尚未就绪，addin 作业会明确失败。这是有意的交接关卡，不轮询开发机，也不使用自托管 runner。补齐上述附件和发布说明后，重跑该标签的整次执行：

```powershell
gh run list --repo asckye/eplan-mcp --workflow release.yml --branch v0.4.1
gh run rerun <上一步的运行 ID> --repo asckye/eplan-mcp
```

## CI 输出与核查

三个作业分别构建/测试 Host 与 Workbench、验证插件、打包；插件作业读取草稿需 `contents: write`，只有打包作业有 `id-token`、`attestations` 写权限。插件 ZIP 的实际哈希必须符合发布说明中唯一且格式完全一致的 `Add-in SHA-256: …  EplanMcp-addin-vX.Y.Z.zip` 行，上传前再次核对；manifest JSON 的标签、提交和测试记录也必须符合。CI 不执行 EPLAN，不自动发布 release。

Host 为 win-x64 自包含单文件；Workbench 为 win-x64 自包含目录，附经固定哈希验证的 Maple Mono CN 字体及许可证。发布使用独立的 `packages.win-x64.lock.json`，常规 CI 继续使用原锁文件，两者都保持 locked mode。CycloneDX 6.0.0（Apache-2.0，仅构建工具）生成三个项目的 SBOM 后合并，并补充随包运行时、字体和 TIA 主题。暂存 `app\` 的全部内容打进唯一顶层目录 `EplanMcp-<version>\`，包括点目录；不套 `app\`，不调用 Inno Setup。

以 `v0.4.1` 为例，发布保留以下 8 个附件：

- `EplanMcp-0.4.1.zip`：工程师下载的完整程序包，顶层目录 `EplanMcp-0.4.1\`；打开其中 `workbench\EplanMcp.Workbench.exe`。
- `SHA256SUMS.txt`：所有其他附件的 SHA-256，包括插件 ZIP、manifest、安装文件清单和两个证明包；不计算自身的哈希。
- `EplanMcp-v0.4.1.cdx.json`：合并的 CycloneDX SBOM。
- `EplanMcp-v0.4.1-provenance.sigstore.json`：来源证明包。
- `EplanMcp-v0.4.1-sbom.sigstore.json`：SBOM 证明包。
- `EplanMcp-v0.4.1-files.sha256`：ZIP 内每个文件的路径和 SHA-256（含版本顶层目录），供解压后逐文件核查。
- `EplanMcp-addin-v0.4.1.zip`：维护者构建的插件包。
- `EplanMcp-addin-v0.4.1.json`：从插件包提取的标签、提交和测试记录（manifest）。

不为每个附件单独生成 `.sha256` 文件。逐文件清单作为附件；来源证明的完整 subject 清单另存于工作区 `.local-subjects.sha256`，不上传。ZIP 及其每个文件均纳入来源证明，SBOM 证明绑定这个完整 ZIP；插件证明不能证明维护者开发机的源码构建过程。插件 ZIP 仅用于 ADR-0020 交接和核查，工程师无需另下。

```powershell
$asset = 'EplanMcp-0.4.1.zip'
$expected = @(Get-Content .\SHA256SUMS.txt | Where-Object { $_ -cmatch "^[0-9a-f]{64}  $([regex]::Escape($asset))$" })
if ($expected.Count -ne 1) { throw 'Missing or duplicate SHA-256 entry.' }
$actual = (Get-FileHash -LiteralPath ".\$asset" -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actual -cne $expected[0].Substring(0, 64)) { throw 'SHA-256 mismatch.' }
gh attestation verify ".\$asset" --repo asckye/eplan-mcp
```

离线核对 SHA-256，联网可核验 GitHub 证明。维护者须下载最终 ZIP，在标准用户、无开发工具、断网的干净 Windows 虚拟机上完成 I1–I10，记录 SmartScreen 和智能应用控制状态。校验后先在 ZIP 属性中解除锁定，再解压到短且固定的任意目录；测试从 `workbench\EplanMcp.Workbench.exe` 勾选变体注册、客户端绝对路径和生成市场、失败提示、撤销后删程序及数据保留。升级必须先关闭 EPLAN、Workbench、客户端，再整体替换同一路径的程序目录；不依赖安装器做进程检查或拒绝降级。

卸载先移除需要清理的客户端配置/Claude Code 市场，在 Workbench 关闭登录自启动、撤销注册，成功后退出并删除程序目录。默认及自定义数据根目录均保留；程序移动后须重新注册、重写客户端配置或生成市场。一台电脑由一个 Windows 用户使用 EplanMcp（Q15）。确认所有证据齐全后，维护者手动发布草稿。

## 安装验收清单（M5 发布前逐项通过）
清单来源：本项目实机验证中踩过的坑，以及国内 EPLAN 插件用户反映的常见安装问题（[电气CAD吧插件商店](https://www.cad-bbs.cn/pluginstore/)的安装说明与用户评论）。

| # | 要求 | 用户常遇到的问题 |
|---|---|---|
| I1 | 安装、使用、卸载都**不需要管理员权限**，不需要"以管理员身份运行 EPLAN" | 不以管理员身份运行时，插件提示"未联网"或注册表访问被拒绝 |
| I2 | 只通过 EPLAN 官方的 `EplApiModuleAction` 注册插件，不写注册表、不往 EPLAN 安装目录复制文件 | 安装程序把类库复制到 EPLAN 目录、改注册表 |
| I3 | 安装后**无需手工**在 API 管理中卸载再加载；需要重启 EPLAN 时（例如更新插件，api-notes A8），安装程序明确提示 | 装完要在 API 管理中先卸载再加载，图标才出现 |
| I4 | 升级时直接覆盖安装，不需要先手工卸载旧版本 | 部分版本要求先卸载旧版再安装 |
| I5 | 安装和使用都**不需要联网**（MIT 开源，没有在线授权检查） | 断网安装报错 |
| I6 | **不绑定电脑**，没有授权注册、注销和转移的流程 | 重装系统或换电脑后授权作废，转移要先注销 |
| I7 | 支持矩阵中的每个 EPLAN 版本都实际安装验证过；不支持的版本，`doctor` 给出明确提示 | 新旧版本兼容问题，例如某个小版本图标不显示 |
| I8 | 在虚拟机中同样可以安装和使用 | 虚拟机上授权受限 |
| I9 | 在 Workbench 或经 `uninstall-addin` 完全撤销注册后，再删除程序目录 | 卸载不干净、插件管理混乱 |
| I10 | 由 MSIX 打包的客户端（Claude 桌面版）启动时同样正常工作（ADR-0008） | 本项目实测：`%LOCALAPPDATA%` 被重定向；数据根目录和命名管道不受影响（spike S5） |

## 回归检查

回归检查运行 `pwsh -NoProfile -File tests/release/Release.Tests.ps1`，也由发布作业执行：检查 YAML、PowerShell 和 action SHA，以假附件验证交接与失败路径，并实际验证 ZIP 顶层目录、点文件、逐文件哈希、两类证明 subject 和附件清单；不会调用 EPLAN、git、gh 或网络。

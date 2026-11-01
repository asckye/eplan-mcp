# 开发流程

> 适用范围：本仓库的所有代码、文档、配置变更。
> 依据：Google Engineering Practices、Microsoft Code-With Engineering Playbook、单向门/双向门决策原则，见 [references.md](../references.md)。
> 现实前提（2026-11-01）：负责人交付 brief，Codex 在 worktree 工作，经评审后由维护者通过本地关卡直接推送 main。CI 是推送后的非阻塞检查。外部贡献者使用 PR。

## 1. 五条基本原则

| # | 原则 | 在本项目的含义 |
|---|---|---|
| 1 | **小步变更** | 一个变更集只做一件事，并附带对应测试；重构单独交付。变更集指一份 brief 和一次本地关卡运行。改动目标在 200 行以内，超过 400 行必须拆分。行数只统计生产代码和文档；测试代码、生成文件、契约快照、测试夹具不计入，原样复制、保留许可声明的第三方代码（如 Workbench 的 TIA 主题，ADR-0017）也不计入。 |
| 2 | **主干开发** | `main` 是唯一长期分支；Codex 用 worktree 隔离任务，及时同步主干，按变更集交付。 |
| 3 | **先写下来，再动手** | 按第 3 节分级，重大变更先评审设计文档或 ADR。 |
| 4 | **工具契约就是 API** | 名称、参数、返回结构、描述和配置格式按第 8 节管理。 |
| 5 | **安全默认值不可悄悄放宽** | 放宽审批或扩大可写范围必须有 ADR，L3 由负责人确认。 |

## 2. 工作项生命周期

```text
brief → 分级与设计 → Codex / worktree 实现 → 评审 → 实机证据或待验登记 → 本地关卡 -Push → main → 发布记录
```

1. 负责人给出 [brief](../templates/brief.md)：原因、设计、不变量、验收测试和边界。缺陷附精确输入、复现步骤及相关 EPLAN 版本、变体、授权条件。
2. 按第 3 节分级；L2 先评审规格或设计，L3 先写 Proposed ADR，负责人确认后才能实施。
3. Codex 在 worktree 实现，按 [AGENTS.md](../../AGENTS.md) 执行常设规则，整理证据和未完成项；代理不创建 git 提交。
4. 使用 [评审清单](../templates/review-checklist.md) 自查并评审，处理发现项，理由进入提交正文。
5. 涉及 EPLAN 行为的变更集，只有受影响 Large 类已在真实机器通过，或已列入 [待验台账](large-pending.md) 且提交正文明确说明待验，才能落到 main；详见 [实机验证](live-verification.md)。
6. 维护者审阅并提交后，在干净工作区运行 `pwsh eng/scripts/local-gate.ps1 -Push`。一次通过的关卡对应本次 brief；失败修复后重新运行，不能沿用修复前结果。
7. CI 随推送运行，出现失败立即修复；用户可见变化记入 `CHANGELOG.md` 的 `Unreleased`。

## 3. 变更分级与设计产物

容易回退的决定快速做，难以回退的决定先写清楚。

| 级别 | 典型变更 | 设计产物与评审 |
|---|---|---|
| **L0** 琐碎 | 错别字、注释、纯文档修正 | 简短 brief，自查评审清单。 |
| **L1** 常规 | 现有设计内修 bug、可选参数、重构 | brief 说明问题与验收，对照清单评审。 |
| **L2** 显著 | 一组新工具、已发布契约变化、新依赖或配置 | 先评审 [工具规格](../templates/tool-spec.md) 或 [设计文档](../templates/design-doc.md)，再实现。 |
| **L3** 架构级 | 进程拓扑、IPC、安全/审批、支持的 EPLAN 版本、落盘格式 | [ADR](../adr/) + 设计文档；只有负责人明确确认，ADR 才能从 Proposed 改为 Accepted。 |

会改变“代理能对 EPLAN 项目做什么”的至少 L2；改变系统拆分、通信或安全模型的是 L3。

## 4. brief 与交付检查

brief 按模板写清验收条件、限制及 EPLAN API 证据；未验证行为列为先查证事项，不能凭记忆依赖。L2/L3 的设计先经过第 3 节评审。

可以交付到 main 的条件：

- [ ] 本地关卡通过，评审发现已处理；编码符合 [coding-standards.md](../standards/coding-standards.md)。
- [ ] 工具符合 [mcp-tool-design.md](../standards/mcp-tool-design.md)；契约快照按 [testing.md 第 3 节](../standards/testing.md#3-工具契约快照测试) 更新并审视。
- [ ] Small/Medium 补齐；EPLAN 行为变更满足第 2 节的 Large 通过或待验登记规则。
- [ ] 写操作有审计、dry-run 和失败回滚，不能回滚的原因已记录。
- [ ] 用户可见变化有 CHANGELOG，文档同步，无敏感内容。

实机验证完成和发布验收另见 [live-verification.md](live-verification.md)；登记待验不代表验证完成。

## 5. 分支与提交

worktree 分支名表达任务即可，不要求 Issue 号。只保留 `main` 一条长期分支。

### 提交信息：Conventional Commits 1.0

```text
<type>(<scope>): <摘要，祈使句，≤72 字符>

<正文：为什么改>
```

- **type**：`feat` `fix` `docs` `refactor` `perf` `test` `build` `ci` `chore` `revert`
- **scope**：`host` `bridge` `contracts` `tools` `policy` `audit` `workbench` `installer` `evals` `docs` `ci` `spikes`
- 理由写提交正文；仅 **L2/L3 变更集**必须完整写出 why / verify / risk / rollback（为什么、验证、风险、回退）。L0/L1 不要求四段式正文。
- 任何等级若有 Large 待验，提交正文都必须注明台账条目、受影响类、尚未验证的范围；已有测试结果与未运行项分开记录。
- 破坏性变更用 `!` 和 `BREAKING CHANGE:` 标记，版本及兼容期遵循第 8 节。
- **不允许出现 AI 署名**：提交信息中不加相关 `Co-Authored-By` 尾注；PR 描述、发布说明、CHANGELOG 和文档不加生成标记，也不把代理列为作者。本地 `commit-msg` 钩子和 CI 检查。此规则与 [CLAUDE.md 硬性规则](../../CLAUDE.md#硬性规则违反任何一条都不能合并) 是统一依据，其他文件只引用。

### 推送方式

维护者在评审后提交，运行 `eng/scripts/local-gate.ps1 -Push`：变基到 `origin/main`，检查已提交状态，关卡通过且远端未变化才推送 `HEAD:main`。工作区必须干净，包含未跟踪文件的检查。大小限制按第 1 节的整个变更集计算，不能靠拆成多个提交绕过。外部贡献者保留 [PR 模板](../../.github/pull_request_template.md)，不作为内部评审记录入口。

## 6. 评审

按 [review-checklist.md](../templates/review-checklist.md) 检查，顺序为设计、功能与安全、复杂度、测试、命名与风格。让代码库整体变好即可通过；个人偏好用 `Nit:` 标记，不阻塞交付。

- 每个变更集推送前执行 `/code-review`，L2/L3 用 high 档，发现和处理结果附在评审记录中。
- L3 由负责人明确确认，ADR 记录日期及确认原话；冷却期于 2026-10-20 取消。
- 必测失败路径的范围只维护在 [testing.md 第 6 节](../standards/testing.md#必须测失败路径的地方)。
- 新依赖在设计及提交正文说明用途、许可证、维护状况和替代方案。

<a id="7-ci-关卡"></a>

## 7. 本地关卡与 CI

`eng/scripts/local-gate.ps1` 是 main 落地前的阻塞关卡；GitHub CI 是推送后的非阻塞检查。脚本要求已提交的干净工作区，没有 dry mode。脚本检查相对 `origin/main` 的变更，Small/Medium 程序集选择由 `eng/scripts/affected-tests.ps1` 决定，含项目依赖、外链编译/资源项及重命名两端；改构建配置、脚本或共享测试设施时跑全部。`-Full` 跑全部，CI 在 main 上始终跑全部。自动选程序集不能替代人工选择受影响 Large 类。

| 关卡 | 实际检查 |
|---|---|
| 工作区与推送 | 开始和还原后检查 `git status --porcelain`（含未跟踪文件）为空；仅 `-Push` 拉取、变基，并在关卡通过且远端未变化时推送。 |
| 提交信息 | `eng/scripts/check-commit-message.sh --subject` 逐个检查待推送提交的主题与正文，规则见第 5 节；外部 PR 的标题/描述由 CI 检查。 |
| 还原与构建 | 本地 dotnet 调用设置 `CI=true`，锁定还原；按插件项目求值的 `EplanBinPath` 与 API 引用检测安装，DLL 齐全则还原并 Release 构建 `EplanMcp.slnx`（含 Bridge.Eplan/Offline、分析器及 RS0030），否则打印 `add-in not checked: EPLAN not installed` 并使用 `EplanMcp.Ci.slnf`。CI 始终使用 CI filter；警告即错误。 |
| 格式 | 对上述选中的解决方案运行 `dotnet format --verify-no-changes --no-restore`；有 EPLAN 时包含插件。 |
| Action 目录 | `eng/scripts/check-action-catalog.ps1` 核对 JSON Schema、ID、顺序、文档表格逐行内容与预期数量。 |
| 覆盖文件 | `eng/coverage` 的 `check`；扫描过期只警告，生成结果不一致失败。 |
| Small/Medium | 按影响范围运行 CI filter 中的测试（排除 `Size=Large`），含契约快照；每程序集一个独立命名的 TRX，不重跑补结果，无受影响程序集时跳过。 |
| 测试数量 | 同一次运行中，缺结果、通过数低于下限、未知程序集或下限低于通过数的 80% 均失败；更新到通过数的 90% 向下取整。降低下限须在提交正文说明原因，见 [testing.md 第 7 节](../standards/testing.md#7-测试数量下限)。 |
| 依赖安全 | `dotnet list EplanMcp.Ci.slnf package --vulnerable --include-transitive`，High/Critical 失败。 |

Large 和 Agent 评测由维护者在一台 EPLAN 机器上串行安排，不在本地自动关卡或公共 CI 内运行，不使用自托管 runner（ADR-0020）。EPLAN 行为变更满足第 2 节规则；批次和证据见 [实机验证](live-verification.md)。CI 红灯须立即补修复变更集。

## 8. 版本与发布

公共 API 是工具契约和配置格式。删除/重命名工具或参数、可选改必填、删除/改变返回字段含义、改变安全注解、配置不兼容均属于破坏性变更。

- **0.x**：破坏性变更升 minor（例如 0.4.x → 0.5.0），在 CHANGELOG 增设 **Breaking** 节；重命名或替换时，旧名保留为别名至少一个发布版本，注明新名与移除版本。其他不兼容变化也须提供一个版本的兼容入口或迁移支持，不能悄悄更改含义。
- **1.x 起**：破坏性变更升 major；新增工具、可选参数或返回字段升 minor，修复升 patch。0.x 的兼容新增同样升 minor，修复升 patch。
- 废弃先在描述注明 `[DEPRECATED: 用 X 代替，计划在 vN 移除]`，按上述兼容期保留旧名；描述变化也要审视快照。

### 发布清单

- [ ] `Unreleased` 整理为新版本，含需要的 Breaking 与迁移说明。
- [ ] 发布范围的 Large 待验已清零、实机验收和 Agent 评测通过，结果附发布说明；不能以台账登记代替发布验证。
- [ ] 安装包或压缩包、SHA-256、SBOM（CycloneDX）和 GitHub artifact attestation 齐全。
- [ ] 插件由维护者用 `eng/scripts/release-addin.ps1 -Tag vX.Y.Z` 从标签干净检出构建，在离线宿主通过 Large 后上传草稿发布并给出 SHA-256（ADR-0020）。
- [ ] 发布说明含 EPLAN 版本、变体和授权模块支持矩阵。
- [ ] 打 `vX.Y.Z` 标签，由 CI 构建发布产物；插件是按上一条处理的唯一例外。

## 9. 缺陷与事故

| 级别 | 定义 | 响应 |
|---|---|---|
| **S1** | 写坏用户数据或未经批准写入 | 立即禁用相关工具、修复并写不追究个人责任的复盘。 |
| **S2** | 工具返回错误结果但没有写入 | 下个修订版本修复。 |
| **S3** | 体验、描述、性能问题 | 排入常规迭代。 |

S1 复盘放 `docs/postmortems/`，包含时间线、影响、根因、防线缺口和后续行动；行动项落实到 brief 并跟踪验收。每个实机失败都用精确输入复现为 Small 测试，按 [AGENTS.md](../../AGENTS.md) 执行。

## 10. 与编码代理协作

Codex 先读 [AGENTS.md](../../AGENTS.md)，共享常设规则由它维护；[CLAUDE.md](../../CLAUDE.md) 的五条硬性规则保持有效。所有实现均经过相同评审和关卡；提交规范统一见第 5 节。

## 11. 依赖、协议版本与耗时

### 依赖更新

每月集中复核依赖，安全修复及时处理。NuGet 版本固定在 [`Directory.Packages.props`](../../Directory.Packages.props)，更新时同步锁文件、许可证与兼容验证。GitHub Actions 的更新必须固定到完整提交 SHA，并在注释注明版本；截至 2026-11-01，现有 workflow 仍使用 `@v4` 等版本标签，SHA 固定尚待专门变更落实，本规范不宣称已完成。

### MCP spec 与 SDK

当前 SDK 的固定版本位于 `Directory.Packages.props` 的 `ModelContextProtocol` / `ModelContextProtocol.AspNetCore`（均为 `2.2.0`）；同处注释记录 MCP revision `2026-07-28` 及 `2025-11-25` 客户端兼容。每次 SDK 升级都复核官方 revision、协商/兼容范围、schema、传输和安全差异，同步该处版本与注释、[工具规范](../standards/mcp-tool-design.md)、[安全规范](../standards/security.md) 和契约快照；验证证据写入变更集记录。不要把 SDK 包版本当成协议 revision。

### 每工具主线程时间预算

每个工具在规格中写明 EPLAN 主线程时间目标；普通交互调用以单次占用不超过 **2 秒**为初始目标，批量、导入导出等长操作另写输入规模与预算。Large 测试输出记录工具名、输入规模、EPLAN 版本、主线程执行时长及端到端时长；将排队、审批等待与主线程占用分开。暂时不能取得主线程时长时标“未测”，不能用墙钟时间冒充。超目标需分析并记录改进项；目前是目标，**不是关卡**，不改变已有安全超时或取消要求。

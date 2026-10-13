# 开发流程

> 适用范围：本仓库的所有代码、文档、配置变更。
> 依据：Google Engineering Practices、Microsoft Code-With Engineering Playbook、Amazon 的单向门/双向门决策原则，对照条目见 [references.md](../references.md)。
> 现实前提：项目初期只有一名人类开发者，加上 AI 助手（Claude Code）。流程按这个前提裁剪：凡是大厂靠"第二个人"保证的环节，这里都换成**可自动执行的检查**或**书面记录**，而不是假装有评审人。

---

## 1. 五条基本原则

| # | 原则 | 来源 | 在本项目的含义 |
|---|---|---|---|
| 1 | **小步变更** | Google *Small CLs* | 一个 PR 只做一件事，并附带对应的测试；重构单独提交。改动目标在 200 行以内，超过 400 行必须拆分（生成文件、契约快照、测试夹具不计入）。Google 的参考是：约 100 行算合理，1000 行就太大了。 |
| 2 | **主干开发** | trunk-based development | 只有 `main` 一条长期分支。功能分支存活不超过 2 天，未完成的功能用开关隐藏，不靠长分支。 |
| 3 | **先写下来，再动手** | Google Design Docs、ADR | 重大变更先写设计文档或 ADR（判断标准见第 3 节），决策可以追溯。 |
| 4 | **工具契约就是 API** | Hyrum's Law、Google AIP | MCP 工具的名称、参数、返回结构一经发布，就会有人（和 AI）依赖。改动按 API 变更管理（见第 8 节）。 |
| 5 | **安全默认值不可悄悄放宽** | Microsoft SDL | 任何让写操作更容易执行的改动，比如放宽审批或扩大可写范围，都按"单向门"处理，必须有 ADR。 |

---

## 2. 工作项生命周期

```
Issue ──► 分级(Triage) ──► 就绪(DoR) ──► [设计] ──► 实现 ──► PR ──► CI 关卡 ──► 合并 ──► 实机验证 ──► 发布说明
                                       ▲                                           │
                                       └──────────── 发现设计问题时回退 ◄──────────┘
```

1. **Issue**：所有工作从 Issue 开始，模板见 `.github/ISSUE_TEMPLATE/`。Bug 必须写复现步骤、EPLAN 版本和变体、授权类型。
2. **分级**：打上 `type/*`、`area/*`、`level/*` 标签（level 见第 3 节）。
3. **就绪（Definition of Ready）**：满足第 4 节的检查项后才开始写代码。
4. **设计**：level 2 及以上写设计产物（第 3 节）。
5. **实现 + PR**：见第 5、6 节。
6. **CI 关卡**：见第 7 节，全部通过才能合并。
7. **实机验证**：凡是涉及 EPLAN 交互的变更，合并前或当天必须在真实 EPLAN 上跑一次对应的 Large 测试（[testing.md](../standards/testing.md)），结果写进 PR。
8. **发布说明**：面向用户的变更在 `CHANGELOG.md` 的 `Unreleased` 下记一条。

---

## 3. 变更分级与设计产物

借鉴 Amazon 的单向门/双向门：**容易回退的决定快速做，难以回退的决定慢下来写清楚。**

| 级别 | 典型变更 | 需要的设计产物 | 评审方式 |
|---|---|---|---|
| **L0** 琐碎 | 错别字、注释、纯文档修正 | 无 | PR 自查 |
| **L1** 常规 | 在现有设计内修 bug、给已有工具加可选参数、重构 | Issue 写清楚即可 | PR 清单 + AI 评审 |
| **L2** 显著 | 新增一组工具、修改已发布工具的契约、引入新依赖、新增配置项 | **工具规格**（[templates/tool-spec.md](../templates/tool-spec.md)）或**设计文档**（[templates/design-doc.md](../templates/design-doc.md)） | 设计文档先单独提 PR，合并后再写实现 |
| **L3** 架构级 | 进程拓扑、IPC 协议、支持的 EPLAN 版本、安全/审批模型、数据落盘格式 | **ADR**（[adr/](../adr/)）+ 设计文档 | ADR 状态从 `Proposed` 改为 `Accepted` 需要项目负责人明确同意 |

**判断口诀**：会改变"AI 能对用户的 EPLAN 项目做什么"的，至少 L2；会改变"系统怎么拆、怎么通信、怎么保证安全"的，就是 L3。

---

## 4. 就绪与完成的定义

参考 Microsoft Engineering Playbook 的 Definition of Ready / Definition of Done，按本项目裁剪。

### Definition of Ready（开始编码前）
- [ ] Issue 写明了要解决的问题，而不只是方案。
- [ ] 验收标准可以检验，例如"在 scratch 项目上调用 X，返回 Y"。
- [ ] 级别已确定；L2 及以上的设计产物已合并。
- [ ] 已知涉及的 EPLAN API 已在 [api-notes](../eplan/api-notes.md) 中查证，或者列为本次任务要先查证的事项。

### Definition of Done（可以合并）
- [ ] 代码遵循 [coding-standards.md](../standards/coding-standards.md)，CI 全绿。
- [ ] 新增或修改的工具符合 [mcp-tool-design.md](../standards/mcp-tool-design.md)，工具契约快照已更新并经过审视。
- [ ] 测试按 [testing.md](../standards/testing.md) 补齐：Small 必须有；涉及 EPLAN 的必须有 Large，并已在实机上跑过。
- [ ] 写操作有审计记录、能 dry-run，失败时能回滚，或者明确记录了不能回滚的原因。
- [ ] 用户可见的变化已写进 `CHANGELOG.md`；文档已同步。
- [ ] 没有提交真实客户项目、授权信息或个人数据。

---

## 5. 分支与提交

### 分支命名
`<type>/<issue号>-<简短描述>`，例如 `feat/42-page-create`、`fix/57-pin-designation-encoding`。

### 提交信息：Conventional Commits 1.0
```
<type>(<scope>): <摘要，祈使句，≤72 字符>

<正文：为什么改，而不是改了什么>

Refs: #42
```
- **type**：`feat` `fix` `docs` `refactor` `perf` `test` `build` `ci` `chore` `revert`
- **scope**（与架构组件对应）：`host` `bridge` `contracts` `tools` `policy` `audit` `workbench` `installer` `docs` `ci` `spikes`
- 破坏工具契约的变更必须在 type 后加 `!`，并写 `BREAKING CHANGE:` 脚注。发布工具会根据它决定是否升主版本。
- **不允许出现 AI 署名**：提交信息里不能有 `Co-Authored-By: <AI 名称>` 之类的尾注；PR 描述、发布说明、CHANGELOG 和文档里不能写 "Generated with ..." 等 AI 生成标记，也不能把 AI 列为作者。CI 会检查（见第 7 节）。

### 合并策略
- PR 一律 **squash merge**，`main` 保持线性历史。PR 标题即最终提交信息，所以 PR 标题也要符合 Conventional Commits。
- 禁止直接推送 `main`。即使是一个人开发，也要在仓库设置里开启分支保护，要求 CI 通过才能合并。

---

## 6. Pull Request 与评审

### 提交者
1. 先自查：对照 PR 模板（`.github/pull_request_template.md`）逐项勾选。
2. PR 描述写三件事：**为什么**、**怎么验证的**（附实机测试输出）、**风险和回退办法**。
3. 新增依赖时写明理由：用来做什么、许可证、维护状况、有没有更轻的替代。

### 评审（参考 Google 的评审者指南）
按以下顺序看，前面的不过关就不用看后面的：
1. **设计**：这个变更该不该做，放在这里对不对？
2. **功能与安全**：对 EPLAN 项目会不会有意外写入？失败路径有没有处理？
3. **复杂度**：下一个人（或下一个 AI 会话）能不能读懂？
4. **测试**：测试会不会在代码出错时真的失败？
5. **命名、注释、风格**：大部分交给分析器，人只看分析器管不到的地方。

**通过标准**（Google 原则）：只要这个变更让代码库整体变得更好，就可以通过，不要求完美。挑剔的个人偏好用 `Nit:` 前缀标出，不阻塞合并。

### 一人开发时的替代机制
- **AI 评审**：每个 PR 在合并前跑一次 `/code-review`，L2 及以上用 high 档。处理过的发现在 PR 里逐条回复。
- **冷却期**：L3 的 ADR 写完后至少隔一天再改为 `Accepted`，用时间换"第二双眼睛"。
- **安全敏感区域**（审批策略、可写范围判断、审计、Bridge 的线程调度）的改动，必须附带针对失败场景的测试，不能只测正常路径。

---

## 7. CI 关卡

| 关卡 | 内容 | 运行位置 | 触发 |
|---|---|---|---|
| 构建 | `dotnet build -c Release`，警告即错误 | GitHub 托管 Windows runner | 每个 PR / push |
| 格式 | `dotnet format --verify-no-changes` | 同上 | 每个 PR |
| 提交信息 | PR 标题和每个提交都必须符合 Conventional Commits；凡是包含 AI 署名的一律拒绝，例如 `Co-Authored-By:` 尾注里出现 AI 名称或 noreply 地址，或者 `Generated with` 等字样。同样的规则也检查 PR 描述 | 同上 | 每个 PR |
| Small + Medium 测试 | 单元测试、契约测试、使用 Fake Bridge 的端到端测试 | 同上 | 每个 PR |
| 工具契约快照 | 所有工具的名称、描述、JSON Schema、注解导出为快照，与仓库中的快照比对；有差异时 PR 必须同时更新快照 | 同上 | 每个 PR |
| 依赖安全 | `dotnet list package --vulnerable --include-transitive`，有高危漏洞就失败 | 同上 | 每个 PR + 每周 |
| 文档 | Markdown 链接检查 | 同上 | 每个 PR |
| **Large 实机测试** | 在装有 EPLAN 和授权的机器上，用 scratch 项目跑实机测试 | 自托管 runner（标签 `eplan-2026`） | 每晚 + 发布前 + 手动触发 |
| Agent 评测 | 用真实任务让模型调用工具，统计成功率、调用次数和 token 消耗 | 自托管 runner | 发布前 + 工具描述有改动时 |

> 实机测试不能放在公共 CI 上：需要 EPLAN 授权，而且会占用一个授权席位。自托管 runner 只对本仓库开放，不运行来自 fork 的 PR。

---

## 8. 版本与发布

### 版本号：Semantic Versioning 2.0
对一个 MCP 服务器来说，"公共 API"就是**工具契约**加上**配置文件格式**。以下属于**破坏性变更**，必须升主版本：
- 删除或重命名工具，删除参数，把可选参数改为必填；
- 删除返回结构中的字段，或改变字段含义；
- 改变工具的安全注解，例如把 `readOnlyHint: true` 改为 false；
- 配置文件不兼容。

新增工具、新增可选参数、新增返回字段属于次版本；修复属于修订号。

### 废弃策略
工具要删除时，先在描述开头加 `[DEPRECATED: 用 X 代替，计划在 vN 移除]`，至少保留一个次版本，再在下一个主版本中删除。

### 发布清单
- [ ] `CHANGELOG.md` 把 `Unreleased` 整理为新版本（遵循 Keep a Changelog）。
- [ ] 发布前的 Large 实机测试和 Agent 评测都已通过，结果附在发布说明里。
- [ ] 产物：安装包或压缩包、SHA-256 校验和、SBOM（CycloneDX）、构建来源证明（GitHub artifact attestation）。
- [ ] 发布说明写明支持矩阵：EPLAN 版本、变体、需要的授权模块。
- [ ] 打 `vX.Y.Z` 标签，由 CI 构建产物，**不发布本地构建的二进制**。

---

## 9. 缺陷与事故

| 级别 | 定义 | 响应 |
|---|---|---|
| **S1** | 写坏了用户项目的数据，或在未经批准的情况下执行了写操作 | 立即停止相关工具（通过配置禁用），修复，写一份不追究个人责任的事后复盘 |
| **S2** | 工具返回错误结果，但没有写入 | 下个修订版本修复 |
| **S3** | 体验问题、描述不清、性能问题 | 排入常规迭代 |

S1 复盘放在 `docs/postmortems/`，内容包括：时间线、影响、根因、为什么之前的防线没拦住、后续行动项。行动项必须落到 Issue 上。

---

## 10. 与 AI 助手协作

仓库根目录的 `CLAUDE.md` 是给 AI 助手的规则，和本流程一致。核心约束：
- AI 只能在 **scratch 项目**上执行写操作，永远不能在用户的真实项目上试验。
- AI 提出的 L3 决策只能是 `Proposed` 状态的 ADR，由人确认后才能生效。
- AI 生成的代码同样走 PR、CI 和评审，没有例外。
- 推送到 GitHub 的任何内容都**不带 AI 署名**（见第 5 节）。AI 助手在本仓库中生成提交或 PR 时，不添加任何署名尾注或"由 AI 生成"标记。

---

## 11. 度量

只看少数几个能指导行动的指标，每个里程碑结束时回顾一次：

| 指标 | 来源 | 目标 |
|---|---|---|
| 变更前置时间（从 PR 打开到合并） | DORA | 中位数不超过 1 天 |
| 变更失败率（需要回滚或热修的发布占比） | DORA | 低于 15% |
| 实机测试通过率 | 每晚的 Large 测试 | 高于 95%；不稳定的测试要么修好，要么隔离 |
| Agent 任务成功率 | Agent 评测 | 每次发布不下降 |
| 平均每个任务的工具调用次数和 token 消耗 | Agent 评测 | 呈下降趋势（说明工具设计在变好） |

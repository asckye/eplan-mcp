# ADR-0048：采用本地关卡作为权威合并门

| 项 | 内容 |
|---|---|
| 状态 | Accepted |
| 决策日期 | 2026-10-30 |
| 记录日期 | 2026-11-03 |
| 决策人 | 项目负责人 |
| 门的类型 | 双向门：可另行决定合并门的执行位置 |

## 背景与问题

项目负责人于 2026-10-30 采纳通过本地关卡直接推送 `main`、不开 PR、CI 非阻塞的做法；现行流程见[开发流程第 2 节](https://github.com/asckye/eplan-mcp/blob/7bd1953f548dcd6f43765d467a304044c936495f/docs/process/development-workflow.md#2-工作项生命周期)、[第 5 节](https://github.com/asckye/eplan-mcp/blob/7bd1953f548dcd6f43765d467a304044c936495f/docs/process/development-workflow.md#5-分支与提交)和[第 7 节](https://github.com/asckye/eplan-mcp/blob/7bd1953f548dcd6f43765d467a304044c936495f/docs/process/development-workflow.md#7-本地关卡与-ci)，日期及执行入口见 [`local-gate.ps1` 的说明](../../eng/scripts/local-gate.ps1)。本记录于 2026-11-03 按[手册 BLD-03](../standards/engineering-handbook.html#build-tiers)补记该已采纳的决定。

托管 CI runner 没有 EPLAN，也不使用自托管 runner，见 [ADR-0020](0020-no-self-hosted-runner.md)。本地开发机可检查已安装的 EPLAN API 及插件，CI 使用不含插件的构建范围。

## 备选方案

1. 通过 PR 等待托管 CI 放行：增加等待，仍不能检查本机 EPLAN 插件。
2. 本地关卡放行后直接推送 `main`，CI 在推送后检查：采用现有本机环境和统一脚本。

## 决策

采用方案 2。[`eng/scripts/local-gate.ps1`](../../eng/scripts/local-gate.ps1) 是权威合并门：维护者审阅并提交后，在干净工作区运行 `-Push`，通过才直接推送 `main`，不开 PR。关卡运行构建、Small + Medium、格式和工具契约快照等检查；完整检查项及影响范围选择见[开发流程第 7 节](https://github.com/asckye/eplan-mcp/blob/7bd1953f548dcd6f43765d467a304044c936495f/docs/process/development-workflow.md#7-本地关卡与-ci)。Large 需实机执行，不属于自动关卡。

CI 继续在推送后运行，作为非阻塞检查；失败在 `main` 上向前修复。

## 后果

- 合并前的权威检查使用本机环境；有 EPLAN API 时可包含插件构建。
- 维护者负责执行本地关卡，CI 失败可能在变更已进入 `main` 后发现，须及时补修复变更。
- EPLAN 实机验证仍按[实机验证文档](../process/live-verification.md)记录证据与待验项。

## 2026-11-03 文档引用修订

原正文保留为决策时的历史记录。采用手册后，原规范及本地规则编号不再作为现行规则入口；现行条款和项目事实分别见以下归宿：

- 原引用 `../process/development-workflow.md#2-工作项生命周期` → [手册开发流程条款](../standards/engineering-handbook.html#review-flow)。
- 原引用 `../process/development-workflow.md#5-分支与提交` → [手册开发流程条款](../standards/engineering-handbook.html#review-git)。
- 原引用 `../process/development-workflow.md#7-本地关卡与-ci` → [手册开发流程条款](../standards/engineering-handbook.html#build-tiers)。

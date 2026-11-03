# 本项目采用的开发规范

本仓库遵循[《工程开发手册》v1.1](engineering-handbook.html)，全部采用；本仓库不另设规则。

## 项目声明

| 项目 | 声明 |
|---|---|
| 采用范围 | 领域 A（MCP 服务）、C（EPLAN .NET API）、D（Workbench）；档位 M，审批、写入安全和防止项目数据损坏按 L。 |
| 提交格式（GIT-05） | Conventional Commits；scope 取自 `host bridge contracts tools policy audit workbench installer evals docs ci spikes`。 |
| 语言政策（DOC-03） | 采用手册默认：开发与用户文档中文；代码、注释、提交、发布记录、面向模型的文本英文；与项目负责人交流用中文。 |
| 合并门（BLD-03） | [`eng/scripts/local-gate.ps1`](../../eng/scripts/local-gate.ps1) 是权威合并门，CI 不阻塞；通过本地关卡后直接推送 `main`、不开 PR，见 [ADR-0048](../adr/0048-local-gate-is-the-merge-gate.md)。 |
| 证据台账（P-01、P-03） | EPLAN 行为的出处在 [`docs/eplan/api-notes.md`](../eplan/api-notes.md)；未实机验证项在 [`docs/process/large-pending.md`](../process/large-pending.md)。 |
| 支持的 EPLAN 版本 | 见 [ADR-0009](../adr/0009-supported-eplan-versions.md)。 |

## 原规则 → 手册条款

| 原规则 | 手册条款 |
|---|---|
| 只在 scratch 项目上做写操作 | [AI-10](engineering-handbook.html#ai-machine) |
| 不带 AI 署名 | [GIT-04](engineering-handbook.html#review-git) |
| L3 决策只能提议为 Proposed ADR，由负责人采纳 | [G-05](engineering-handbook.html#purpose) + [18.3](engineering-handbook.html#review-rfc) |
| 不提交敏感内容 | [GIT-07](engineering-handbook.html#review-git) |
| stdio 的 stdout 只输出协议 | [ARC-12](engineering-handbook.html#arch) |

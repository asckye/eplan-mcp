<!-- 本模板仅供外部贡献者使用。仓库内部用 brief → Codex / worktree → 评审 → local-gate.ps1 -Push；见 https://github.com/asckye/engineering-standards/blob/v3.0/skills/general-engineering-standard/references/18-process-review-git.md。 -->

## 原因与范围
<!-- 解决什么问题、精确复现输入、预期行为、变更边界。无需 Issue 号。 -->

## 变更级别与设计
<!-- L0 / L1 / L2 / L3；L2 附工具规格或设计，L3 附 Proposed ADR，由负责人确认。 -->

## 验证与待验
<!-- Small/Medium 命令和结果；涉及 EPLAN 行为：受影响 Large 类已实机通过，或登记 docs/process/large-pending.md，并在提交正文说明待验。证据要求见 docs/process/live-verification.md。 -->

## 风险与回退
<!-- 按手册 T.5 记录影响、未验证事项和回退方式。 -->

## 自查

- [ ] 已对照 [评审清单](../docs/templates/review-checklist.md) 检查并处理发现。
- [ ] 提交信息、正文及文档符合 [规范开发流程条款](https://github.com/asckye/engineering-standards/blob/v3.0/skills/general-engineering-standard/references/18-process-review-git.md#review-git)。
- [ ] 维护者审阅后在干净工作区运行本地关卡，CI 结果作为检查证据；Large 不包含在自动关卡中。

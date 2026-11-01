<!-- 本模板仅供外部贡献者使用。仓库内部用 brief → Codex / worktree → 评审 → local-gate.ps1 -Push；见 docs/process/development-workflow.md。 -->

## 原因与范围
<!-- 解决什么问题、精确复现输入、预期行为、变更边界。无需 Issue 号。 -->

## 变更级别与设计
<!-- L0 / L1 / L2 / L3；L2 附工具规格或设计，L3 附 Proposed ADR，由负责人确认。 -->

## 验证与待验
<!-- Small/Medium 命令和结果；涉及 EPLAN 行为：受影响 Large 类已实机通过，或登记 docs/process/large-pending.md，并在提交正文说明待验。证据要求见 docs/process/live-verification.md。 -->

## 风险与回退
<!-- L2/L3 变更集必须完整记录 why / verify / risk / rollback，并同步提交正文；L0/L1 不要求四段式。理由仍需进入提交正文。 -->

## 自查

- [ ] 已对照 [评审清单](../docs/templates/review-checklist.md) 检查并处理发现。
- [ ] 提交信息、正文及文档符合 [开发流程第 5 节](../docs/process/development-workflow.md#5-分支与提交)。
- [ ] 维护者审阅后在干净工作区运行本地关卡，CI 结果作为检查证据；Large 不包含在自动关卡中。

# 变更集评审清单

- brief / worktree：
- 级别 / 设计 / ADR：
- 评审日期与结论：
- 发现项及处理证据：

## 设计与安全

- [ ] 原因、范围、不变量和验收明确；L2 设计已评审，L3 有负责人 Accepted 记录。
- [ ] 符合 [CLAUDE.md 硬性规则](../../CLAUDE.md#硬性规则违反任何一条都不能合并) 与 [AGENTS.md](../../AGENTS.md) 常设规则。
- [ ] 写入保护、审批、dry-run、审计及回滚成立；失败路径按 [手册第 12 章](../standards/engineering-handbook.html#testing) 检查。
- [ ] 依赖用途、许可证、维护状况与替代方案有依据；EPLAN 行为有 api-notes 证据。

## 测试与契约

- [ ] 每个实机失败有精确输入的 Small 回归，未弱化 main 的 Large 断言。
- [ ] Small/Medium 结果完整；EPLAN 行为变更的受影响 Large 类已实机通过，或已登记 [large-pending.md](../process/large-pending.md) 且提交正文说明待验。
- [ ] 快照按 [Host 契约快照说明](../../tests/EplanMcp.Host.Tests/README.md) 审视；兼容与迁移采用[手册第 17 章](../standards/engineering-handbook.html#compat)。
- [ ] 文档与 CHANGELOG 同步；界面变更对照[手册界面条款](../standards/engineering-handbook.html#ui-checklist)和 [Workbench 实现记录](../design/2026-10-20-m5-operability.md#workbench-implementation)。

## 交付

- [ ] 变更范围按[手册快速交付条款](../standards/engineering-handbook.html#fast)检查。
- [ ] 提交说明与交付证据按[手册评审条款](../standards/engineering-handbook.html#review-code)检查。
- [ ] 提交信息及文档符合 [手册开发流程条款](../standards/engineering-handbook.html#review-git)，无敏感数据。
- [ ] `/code-review` 发现已处理；代理交付未提交成果，由维护者提交后运行 `pwsh eng/scripts/local-gate.ps1 -Push`。CI 是事后检查。

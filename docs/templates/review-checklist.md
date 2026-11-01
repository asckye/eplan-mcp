# 变更集评审清单

- brief / worktree：
- 级别 / 设计 / ADR：
- 评审日期与结论：
- 发现项及处理证据：

## 设计与安全

- [ ] 原因、范围、不变量和验收明确；L2 设计已评审，L3 有负责人 Accepted 记录。
- [ ] 符合 [CLAUDE.md 硬性规则](../../CLAUDE.md#硬性规则违反任何一条都不能合并) 与 [AGENTS.md](../../AGENTS.md) 常设规则。
- [ ] 写入保护、审批、dry-run、审计及回滚成立；失败路径按 [testing.md 第 6 节](../standards/testing.md#必须测失败路径的地方) 检查。
- [ ] 依赖用途、许可证、维护状况与替代方案有依据；EPLAN 行为有 api-notes 证据。

## 测试与契约

- [ ] 每个实机失败有精确输入的 Small 回归，未弱化 main 的 Large 断言。
- [ ] Small/Medium 结果完整；EPLAN 行为变更的受影响 Large 类已实机通过，或已登记 [large-pending.md](../process/large-pending.md) 且提交正文说明待验。
- [ ] 快照按 [testing.md 第 3 节](../standards/testing.md#3-工具契约快照测试) 审视；0.x 破坏性变更含 minor bump、Breaking、旧名一个版本别名及迁移支持。
- [ ] 文档与 CHANGELOG 同步；界面变更对照 [workbench-ui.md](../standards/workbench-ui.md) 的规则索引。

## 交付

- [ ] 一个变更集对应一份 brief 和一次本地关卡；按 [流程第 1 节](../process/development-workflow.md#1-五条基本原则) 计行及拆分。
- [ ] 提交正文说明理由；仅 L2/L3 必填 why / verify / risk / rollback；待验例外任何级别都写明。
- [ ] 提交信息及文档符合 [流程第 5 节](../process/development-workflow.md#5-分支与提交)，无敏感数据。
- [ ] `/code-review` 发现已处理；代理交付未提交成果，由维护者提交后运行 `pwsh eng/scripts/local-gate.ps1 -Push`。CI 是事后检查。

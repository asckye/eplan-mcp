# 工具规格：`eplan_rule_check`
| 项 | 内容 |
|---|---|
| 状态 / 分组 | 已实现；workflow / full |
| 级别 / 注解 | Read；true / false / true / false |
检查显式项目中所选柜的本机已提升正式规则；候选接受不授予规则资格。复用规则解释器与计划编译器，不新增 Bridge 操作。
输入：必填 `project`、`scope`（沿 context 的 pages/structures/kinds/locators）；可选 `cabinet`（多柜必填）、`rules`（精确锁内规则 ID）、`fix_plan=false`、`limit=50`（1–200）、`cursor`、`max_tokens=5000`（1–20000）。

输出：project/cabinet、四值 rules（规则版本、对象 locator、evidence、basis）、coverage、sources、total/nextCursor/truncated/budget、planId。完整求值后分页，按双份 JSON 预算减少行数；单行放不下则要求增加预算。游标绑定快照、绑定、来源与审核摘要，失效须重新查询。
`fix_plan=true` 仅在稳定 stdio 会话中编译计划，不审批或执行。unknown、推断依据或不完整 fix 输入拒绝；同页修复保守视为冲突。计划固化失败前态和必须复查的规则，仍走原计划安全管线。

当前 fix 输入只接纳无单位的 literal/config/property 与 object（对象完整业务名）；复杂表达式、带单位输入及未证明影响范围拒绝，不猜值。关系覆盖沿现有快照适配保守返回 unknown。

错误：INVALID_ARGUMENT（参数/游标，按 violations 修改）、RESOURCE_NOT_FOUND（规则/工作流不在锁内）、PACK_INVALID（内容/正式来源变化）、CAPABILITY_MISSING（unknown 或输入覆盖缺口）、CONFIG_CONFLICT（修复影响重叠）、PLAN_STALE（编译前态变化）。Read 错误不带 execution。
Small 验证预算、schema、注解与仅 full；Medium 验证四值/分页、正式来源、冲突、unknown 不发布及仅生成计划。Agent 场景要求只计划；本任务不运行 EPLAN。

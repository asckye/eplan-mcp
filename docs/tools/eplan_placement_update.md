# 工具规格：`eplan_placement_update`
| 项 | 内容 |
|---|---|
| 分组 / Profile / 级别 | `placement` / `full` / Write；四注解 false/false/true/false |
| 状态 | T5a 仅契约与只读规划器；未注册工具或新 Bridge operation，执行为 `CAPABILITY_MISSING`，入口留 T5b |
## 输入与范围
显式 `project,page,moves[1..50]`；每项给 `eplan_page_get include_drawings` 的完整 locator（含 digest），以及绝对 `x_mm,y_mm` 或 `delta_mm:[dx,dy]`，两者互斥。请求≤256 KiB，严格成员/重复键/有限数；坐标 0–10,000 mm，增量允许负值。默认 `dry_run=false,snap_to_grid=true,response_format=concise`；dry-run 必须显式 true。
增量先换算绝对目标，再按页栅格吸附（中点远离零）；结果固化绝对位置，重复使用增量不具有幂等性，后续执行必须消费固化计划。无组 Function、SymbolReference、PotentialDefinition、Text、Line、Rectangle 及父符号的全部属性文字构成闭包。属性文字不可独立指定；重复 locator、父子同时指定、组/宏组/未知类型拒绝。A57 属性文字共享父对象号，以父归属和各自 digest 区分，不能按 oid 去重。
## 预览与错误
返回 `execution=not_started,dryRun=true,noOp`、栅格/`Page.Size`、闭包逐对象 locator/parent/before/requested/after/snapped/bounds，以及 `graphicalChanges` 的引脚/前后线段、`connectionChanges` 的受影响逻辑端点对和完整页 fingerprint。零增量/同绝对位置保持原位（即使原位不在栅格），或吸附回原位为 no-op，不创建事务。
复用 `ConnectionCreatePlanner.Scene` 的完整页/引脚/自动线/逻辑端点证据；组归属进入绘图 digest。自动线按相向共线引脚预测，必须与读取基线相符，否则缺能力；逻辑端点列表标明需在 T5b 重生成并逐端点核验，不宣称已生成连接。页界不以图框尺寸兜底，属性文字完整外框也须在页内；移动后的对象碰撞拒绝。
`INVALID_ARGUMENT` 带 JSON Pointer；旧 locator 或完整页 fingerprint 变化为 `PLAN_STALE`；组归属/范围/边界/连线无法证明或尝试执行为 `CAPABILITY_MISSING`。concise/detailed 为 5k/20k 预算，完整清单装不下就拒绝，不截断。所有失败均 `not_started`。
## 实现与验证
规划无 EPLAN 写入、审批或新持久记录；内部读模型供 T5b 接既有流水线，尚无公开调用入口。Small 覆盖父子同号闭包、组拒绝、有限数/碰撞/边界和自动线预测；Medium 固定契约快照，保存读取证据后变更再核对 stale/no-op。执行、实机文字跟随/逻辑重生成/一步撤销及范围外不变留 T5b。

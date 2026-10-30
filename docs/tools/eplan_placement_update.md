# 工具规格：`eplan_placement_update`
| 项 | 内容 |
|---|---|
| 分组 / Profile / 级别 | `placement` / `full` / Write；四注解 false/false/true/false |
| 状态 | T5b 已注册 full 移动入口；复用既有 connection.create.plan/execute 的封闭 moves 分支，不增加 Bridge operation；Large 已编写，未运行 |
## 输入与范围

| 参数 | 类型 | 必填 | 默认值 | 说明 |
|---|---|---|---|---|
| `show_page` | boolean | 否 | false | 绘制前打开或聚焦目标页，便于观看；仅影响界面，不修改数据，dry-run 也开页 |

显式 `project,page,moves[1..50]`；每项给 `eplan_page_get include_drawings` 的完整 locator（含 digest），以及绝对 `x_mm,y_mm` 或 `delta_mm:[dx,dy]`，两者互斥。请求≤256 KiB，严格成员/重复键/有限数；坐标 0–10,000 mm，增量允许负值。默认 `dry_run=false,snap_to_grid=true,response_format=concise`。
增量先换算绝对目标，再按页栅格吸附（中点远离零）；结果固化绝对位置，重复使用增量不具有幂等性，后续执行必须消费固化计划。无组 Function、SymbolReference、PotentialDefinition、Text、Line、Rectangle 及父符号的全部属性文字构成闭包。属性文字不可独立指定；重复 locator、父子同时指定、组/宏组/未知类型拒绝。A57 属性文字共享父对象号，以父归属和各自 digest 区分，不能按 oid 去重。
## 预览与错误
`show_page=true` 时返回 `pageShown`：开页成功为 true，失败为 false 并在 `hint` 说明原因；开页失败仍照常写入。省略或 false 时原结果不变。
返回 `execution=not_started,dryRun=true,noOp`、栅格/`Page.Size`、闭包逐对象 locator/parent/before/requested/after/snapped/bounds，以及 `graphicalChanges` 的引脚/前后线段、`connectionChanges` 的受影响逻辑端点对和完整页 fingerprint。零增量/同绝对位置保持原位（即使原位不在栅格），或吸附回原位为 no-op，不创建事务。
复用 `ConnectionCreatePlanner.Scene` 的完整页/引脚/自动线/逻辑端点证据；组归属进入绘图 digest。自动线按相向共线引脚预测，必须与读取基线相符，否则缺能力；逻辑端点列表标明受影响连接；移动保留逻辑端点，之后用 `eplan_connection_generate` 独立生成，其有效修改另占一个撤销步骤。页界不以图框尺寸兜底，属性文字完整外框也须在页内；移动后的对象碰撞拒绝。
`INVALID_ARGUMENT` 带 JSON Pointer；旧 locator 或完整页 fingerprint 变化为 `PLAN_STALE`；组归属/范围/边界/连线无法证明为 `CAPABILITY_MISSING`。concise/detailed 为 5k/20k 预算，完整清单装不下就拒绝，不截断。预检失败为 `not_started`；执行失败已证明恢复为 `rolled_back`，否则 `OUTCOME_UNKNOWN/unknown`。
## 实现与验证
沿既有 WritePipeline 执行策略、审批、审计及 outcome 恢复。Bridge 同次主线程重核完整页指纹，增量只在此次规划时解为绝对目标；no-op 不创建事务。单一 SafetyPoint → UndoStep → QuietModeStep 内只设父对象/自由图元位置，直线同时平移两端；属性文字保持相对偏移（A53/A54/A57）。逐项核对位置、外框、引脚及范围外对象后提交，再核对完整页自动线与逻辑端点，最后 CloseUndo。提交后不符用仍未关闭的 UndoStep.DoUndo（A7/S2）撤回并复核原页指纹；恢复失败报 unknown，不自动重发。
Small 覆盖同号属性闭包、逐项读回、范围外变化、提交后连线与撤回失败；Medium 覆盖假实现的中途失败、stale、no-op 及 Host 流水线。Large 在新 LargeWrite 页移动线圈/标识和自由文本，生成后核对连接并验证一步撤销及范围外不变；本次遵照要求不运行 EPLAN，实机组合行为和画面验收待维护者执行。

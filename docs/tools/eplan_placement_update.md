# 工具规格：`eplan_placement_update`

`layout_guard` 可选 `refuse`（默认）或 `warn`：复现已有图纸时用 `warn`，将写前布局违规写入 `warning`（沿用 `LayoutGuard.Summary` 上限）；自由绘图保持默认。超出页面的 R1 仍拒绝，写后检查和 ADR-0046 不变。

ADR-0049（0.5.0）：省略 `dry_run` 或传 `true` 只预览，不请求审批、不修改项目或文件，返回 `execution: not_started`；执行须再次调用并显式传 `dry_run: false`，`hint` 会说明该步骤。Small/Medium 覆盖省略、true、false 三条路径；审批、读回和撤销规则不变。

| 项 | 内容 |
|---|---|
| 分组 / Profile / 级别 | `placement` / `full` / Write；四注解 false/false/true/false |
| 状态 | Implemented (v0.4.1) |
## 输入与范围

| 参数 | 类型 | 必填 | 默认值 | 说明 |
|---|---|---|---|---|
| `show_page` | boolean | 否 | false | 绘制前打开或聚焦目标页，便于观看；仅影响界面，不修改数据，dry-run 也开页 |

显式 `project,page,moves[1..50]`；每项给 `eplan_page_get include_drawings` 的完整 locator（含 digest），以及绝对 `x_mm,y_mm` 或 `delta_mm:[dx,dy]`，两者互斥。请求≤256 KiB，严格成员/重复键/有限数；坐标 0–10,000 mm，增量允许负值。默认 `dry_run=true,snap_to_grid=true,response_format=concise`。
增量先换算绝对目标，再按页栅格吸附（中点远离零）；结果固化绝对位置，重复使用增量不具有幂等性，后续执行必须消费固化计划。无组 Function、SymbolReference、PotentialDefinition、InterruptionPoint、Cable、ConnectionDefinitionPoint、Text、Line、Rectangle 及父符号的全部属性文字构成闭包。属性文字不可独立指定；重复 locator、父子同时指定、组/宏组/未知类型拒绝。A57 属性文字共享父对象号，以父归属和各自 digest 区分，不能按 oid 去重。Cable 定义线与 ConnectionDefinitionPoint 不参与自动连接，移动时允许穿过导线；其他对象的导线冲突检查、引脚与页界检查保留。电缆成员归属由 EPLAN 在生成连接后处理，再由 `eplan_page_review` 检查（A64/A75）。

D4 增加与 `moves` 互斥的 `property_moves[1..50]`：每项 `{parent,property_id,property_index,x_mm,y_mm,angle_deg}`。`angle_deg` 取 0、90、180、270，或 -16002（取图层的旋转）。parent 为最新绘图父定位器，ID 和零基序号取子项 properties 的同名字段；不能用共享父号的子定位器代替。角度为绝对 0/90/180/270 度，坐标为页面绝对毫米；仅改变选定文字，脱离停靠，父线和其他文字不变。绘图 properties 增加 angle_deg、docked；继承图层或非直角的原始旋转暂拒绝。规划绕文字插入点旋转外框并平移，页界不明或越界拒绝。输出 moves 增加 propertyId/propertyIndex/beforeAngle/angle；沿相同审批、完整页指纹、事务、提交前后读回和一步撤销流程，no-op 无撤销步。Small/Medium 覆盖同父同属性多项、角度/位置/旁项错误及 stale；Large 新页电缆竖排和 PNG 仅编写，未运行。
默认符号文字属于只读库（A57，D4 失败日志），执行时在同一事务内先复制为父符号的本地属性放置，再按属性/序号重新取得选定文字。绘图文字以父对象号及属性/序号、内容几何摘要定位，复制本身不改变其绘图身份；完整页读回、旁项不变和回滚核验照常执行。此修复已补离线回归，实机组合仍待复验。
## 预览与错误
A75/A73：移动闭包含 Cable 时，按移动前或移动后 reader 的 name 归属该电缆的 ConnectionDefinitionPoint、移动后电缆上新出现且名称只有结构前缀的定义点，以及这些定义点自己的属性文字，都排除在闭包身份、放置数量和跟随比较之外。Cable 定义线的位置、外框及自身文字仍须按计划位移，其余放置、另一电缆的定义点、引脚和逻辑连接继续逐项核验。实机（2026-11-02）：移动后 EPLAN 删除旧定义点，在新交叉点建未命名的定义点；生成连接后电缆成员仍为空，芯线号与颜色被清除，即移动会丢失芯线归属。结果 warning：`Moving a cable drops its core assignment (A73): EPLAN recreates its definition points unnamed and eplan_connection_generate does not make the crossed connections cable cores again. Check the cores with eplan_page_review; undo (Ctrl+Z) restores them.` 单独移动 ConnectionDefinitionPoint 仍保持原严格校验。缺失或不可读的归属证据只报告未验证，不判违规。
A72：属性放置读回附带 `properties.displayed_property_id`，用于区分只读计算值与可写文字。同名中断点组内确有位置改变时，只允许其 20201 块属性的 text/display 与摘要重算；点数、名称、父归属、位置、外框及其余属性仍精确核验。失败恢复仍须核对原始完整指纹。
`show_page=true` 时返回 `pageShown`：开页成功为 true，失败为 false 并在 `hint` 说明原因；开页失败仍照常写入。省略或 false 时原结果不变。
返回 `execution=not_started,dryRun=true,noOp`、栅格/`Page.Size`、闭包逐对象 locator/parent/before/requested/after/snapped/bounds，以及 `graphicalChanges` 的引脚/前后线段、`connectionChanges` 的受影响逻辑端点对和完整页 fingerprint。零增量/同绝对位置保持原位（即使原位不在栅格），或吸附回原位为 no-op，不创建事务。
复用 `ConnectionCreatePlanner.Scene` 的完整页/引脚/自动线/逻辑端点证据；组归属进入绘图 digest。自动线按相向共线引脚预测，必须与读取基线相符，否则缺能力；逻辑端点列表标明受影响连接；移动保留逻辑端点，之后用 `eplan_connection_generate` 独立生成，其有效修改另占一个撤销步骤。页界不以图框尺寸兜底，属性文字完整外框也须在页内；移动后的对象碰撞拒绝。
`INVALID_ARGUMENT` 带 JSON Pointer；旧 locator 或完整页 fingerprint 变化为 `PLAN_STALE`；组归属/范围/边界/连线无法证明为 `CAPABILITY_MISSING`。concise/detailed 为 5k/20k 预算，完整清单装不下就拒绝，不截断。预检失败为 `not_started`；执行失败已证明恢复为 `rolled_back`，否则 `OUTCOME_UNKNOWN/unknown`。
## 实现与验证
`EplanPlacementMover.Prepare` 在事务创建之前检查移动根对象、所属项目和所在页的只读/写保护状态（api-notes A76），明确受保护时返回 `EPLAN_OPERATION_FAILED/not_started`，不移动任何对象。

沿既有 WritePipeline 执行策略、审批、审计及 outcome 恢复。Bridge 同次主线程重核完整页指纹，增量只在此次规划时解为绝对目标；no-op 不创建事务。单一 SafetyPoint → UndoStep → QuietModeStep 内只设父对象/自由图元位置，直线同时平移两端；属性文字保持相对偏移（A53/A54/A57）。逐项核对位置、外框、引脚及范围外对象后提交，再核对完整页自动线与逻辑端点，最后 CloseUndo。提交后不符用仍未关闭的 UndoStep.DoUndo（A7/S2）撤回并复核原页指纹；恢复失败报 unknown，不自动重发。
Small 覆盖同号属性闭包、逐项读回、范围外变化、提交后连线与撤回失败；Medium 覆盖假实现的中途失败、stale、no-op 及 Host 流水线。Large 在新 LargeWrite 页移动线圈/标识和自由文本，生成后核对连接并验证一步撤销及范围外不变；本次遵照要求不运行 EPLAN，实机组合行为和画面验收待维护者执行。
共享布局守卫：R1–R5、R8 拒绝并给出规则与可用时的空位建议；R6/R7/R9 默认警告，layout.strict 时拒绝。图框可用区无法读取时继续使用页尺寸减 layout.marginMm 回退并在结果中说明。预测未知不拒绝：写前校验插入点、已知外框及路由；写后在原事务内测量符号与属性文字外框，再校验 R1–R9：按 [ADR-0046](../adr/0046-readback-deviation-warnings.md)，只有新对象超出页面（S2）才整次回滚；其余违规照常提交，warning 中逐条给出 `Readback deviation: …`（规则、对象、实测外框和修复工具；偏差不搜索空位，回滚时才给最近空位），外框无法测量记为未验证。warning/hint 报告写前与写后检查；拒绝的 metadata.layoutViolations/layoutChecks 提供几何和阶段。连接提交后才生效的几何沿用原撤销补偿。参数与限制见 [布局守卫设计](../design/2026-11-01-layout-guard.md)。

`property_moves` 另可设 `alignment`（原生枚举名）、`height_mm`（0.5–100 或默认 -16002）、`visible`、整数 `layer`、`pen:{color,width_mm,style,pattern_length,line_end}`。x_mm/y_mm 仍为页面绝对坐标，内部属性位置加父位置转换，外框由显式 GetBoundingBox(false) 读为绝对坐标；修改样式可单独产生撤销步。除 visible:false 外均读回核对；隐藏写入后 EPLAN 显示不可见元素时可能仍读到 IsVisible=true，结果 warning 明示跳过该比较。字高和对齐改变后的外框使用 EPLAN 实测。

属性字高读回同时保留 `height_raw_mm` 原始数值供写入校验；`height_mm` 沿用布局读取语义，默认字高为 `from_layer`。

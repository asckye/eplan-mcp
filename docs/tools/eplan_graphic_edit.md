# 工具规格：`eplan_graphic_edit`

| 项目字段 | 记录 |
|---|---|
| 状态 | Implemented (Unreleased) |
| 工具名 / 注册入口 / 契约快照 | `eplan_graphic_edit`；[GraphicEditTools](../../src/EplanMcp.Host/Tools/GraphicEditTools.cs)、[McpServerSetup](../../src/EplanMcp.Host/McpServerSetup.cs)、[契约快照](../../tests/Contracts.Snapshots/server-contract.verified.json) |
| 分组 / Profile | placement / full |
| 安全分类与 MCP 注解 | Write；readOnly=false、destructive=false、idempotent=false、openWorld=false |
| 模型说明与参数示例来源 | [工具 schema](../../src/EplanMcp.Host/Tools/GraphicEditTools.cs)、[usage](../../src/EplanMcp.Host/Tools/Usage/eplan_graphic_edit.md)；说明文字合计 ≤1000，schema 原文 ≤1000，usage ≤6000 字符 |
| EPLAN 调用路线 | GraphicEditOperation → GraphicEditPlanner → IGraphicEditor → EplanGraphicEditor；Placement.Scale/DrawingOrder、Edit.BringToFront/SendToBack/CutOff、Group.Create/DeepUnGroup、Block.BreakUp、TextBase.DockText/UndockText、Rectangle.SetArea/HasChamfers/ChamferRadius、GraphicalPlacement.Pen/Layer |
| API 证据 | [A101](../eplan/api-notes.md)：负责人 2026-11-05，EPLAN 2026.0.3，LargeWrite 新页逐项实测；每次写入后一次撤销恢复。读回遵循 A99，不读取 QRCode.GetQRCode/DimensionText；不跨事务保留原生对象。产品端到端验收见 [待验台账](../process/large-pending.md) |
| 原生作用域与生命周期 | 开放项目中同页的 1–50 个 `eplan_object_find` 原样定位符；禁止重复。事务内重新解析原生对象，沿用 LockingStep / SafetyPoint / UndoStep / QuietModeStep |
| 预览、审批与审计 | ADR-0049：省略/true `dry_run` 只预览，不写项目、不审批；false 才执行。WritePipeline 复核计划指纹，scratch 外审批，记录动作、目标、指纹及结果 |
| 事务、回滚与结果恢复 | 一次调用一步撤销；提交前读回不符回滚，提交后读回不符写 warning。比较范围外对象、功能和连接；unknown 暂停写入，沿用 outcome.get 恢复且不重发 |
| 已有测量 | 本工具尚无端到端耗时测量；底层 API 的一次撤销行为由本任务实测事实给定，完整工具 Large 尚未运行 |
| 项目测试与夹具 | Small 几何/参数/计划/读回；Medium 注册、默认预览、审批与故障；Large `GraphicEditTests` 只使用 LargeWrite 新页，每项读回和一次撤销恢复。命令与状态见 [测试入口](../../tests/EplanMcp.Host.Tests/README.md)及待验台账 |

## 输入与行为

所有操作必填 `project`、`page`、`operation`、`locators`；毫米、左下原点、y 向上。拒绝属性文字及不支持的独立对象，错误列明支持的 kind。缺失或不可读证据只产生 warning；明确的几何、种类与参数违规在写入前拒绝。

| operation | 参数及限定 |
|---|---|
| scale | `scale_x/scale_y` 有限且 0.01–100；`origin_mm:[x,y]` 必填，绝不使用无原点重载。支持线、矩形、自由文字、圆弧、圆、椭圆、折线、样条、组；拒绝符号 |
| order | `to:front/back` 或整数 `drawing_order:0–127` 二选一；读回 `drawingOrder`，`eplan_page_get` 绘图项同样提供此字段 |
| group / ungroup / explode_block | group 为 2–50 项，允许符号，拒绝块成员与属性文字；返回组定位符和成员。ungroup 接收组或任一成员，递归取消。explode_block 接收块，返回成员新定位符；创建块时发现 Function 预先拒绝，防止原生 API 静默丢弃 |
| dock_text / undock_text | dock_text 恰好两个自由文字，第一个固定到第二个；可选 `dock_direction:bottom/top/right/left`。undock_text 为 1–50 个自由文字；读回 `docked`，位置保持 |
| corner_radius / copy_format | 矩形 `radius_mm≥0`，0 取消圆角。copy_format 从 `source` 定位符向目标复制 Pen 和 Layer；源和目标必须是 GraphicalPlacement，读回画笔、图层 |
| cut_off / set_length | cut_off 恰好一条线及 `point_mm`；点距该线 ≤0.5 mm 且 0.5 mm 内无其他独立图形；返回改后对象，整段切掉明确标记。set_length 为线及有限正 `length_mm`，`anchor:start/end` 默认 start，另一端沿原方向移动 |
| stretch | `region_mm:[x1,y1,x2,y2]`、`offset_mm:[dx,dy]`；线只移动选区内端点。零角矩形：四角全选整体移动；一角选中移动该角横竖两边；相邻两角只移动共边，沿边分量忽略并警告；对角两角或三角拒绝。无点命中、旋转矩形及结果零面积拒绝 |
| snap_to_grid | 按 Page.GridSize 取最近栅格，中点远离零，复用 placement_update 的取整；已在栅格为 noOp |
| chamfer / fillet | 恰好两线、仅一对端点在 0.01 mm 内重合；分别使用有限正 `distance_mm/radius_mm`，修剪长度不能超出线长，平行线拒绝。缩短两线后复用 placement_create 创建连接线或圆弧，继承第一条线 Pen/Layer |

`scale/stretch/set_length` 支持 `layout_guard:refuse/warn`，默认 refuse，语义与 placement_update 一致：已知页面越界仍拒绝，缺失证据警告。stretch 例：矩形 (10,10)–(20,20) 选中下边两角、偏移 [5,3]，结果 (10,13)–(20,20)。完整例子与各参数见 usage。

## 输出与失败

结果包含 project/page/operation/fingerprint/predicted/newLocators/noOp、execution/dryRun/recovered，可有 warning、hint、before/after。定位符改变后重新 find；新对象仅执行后可取得定位符。concise/detailed 均保留完整影响清单，超预算要求缩小选择。

块成员只提供相对几何时，分解后的绝对几何核对为未验证 warning，保留成员种类、数量及可读格式核对。修剪遇不可读交点几何时提示预测未验证，仍核对范围外对象不变，执行结果返回真实剩余线段；完整实机核对列入 Large。

INVALID_ARGUMENT 为参数、种类、几何或已知护栏违规；PLAN_STALE 要求重新发现并预览；POLICY_DENIED/APPROVAL_DENIED 不开始写入。提交前读回失败且回滚已证实：EPLAN_OPERATION_FAILED/rolled_back；回滚无法证实：OUTCOME_UNKNOWN/unknown；提交后读回失败：committed 附 warning，不重发。

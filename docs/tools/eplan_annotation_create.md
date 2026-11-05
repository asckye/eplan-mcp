# 工具规格：`eplan_annotation_create`

状态：Implemented (Unreleased)，实机待验 · 2026-11-04。

## 问题、选项与决定

插入菜单的注释对象需要批量创建；`eplan_placement_create` 的模型 schema 已达 994/1000 预算。选项是继续扩展原工具（超预算）或增加入口并复用原规划和写协议。采用后一项，Profile 为 `full`，不复制预览、审批、布局、事务和撤销代码。

| 项目字段 | 记录 |
|---|---|
| 状态 | Implemented (Unreleased) |
| 工具名 / 注册 / 契约 | `eplan_annotation_create`；[AnnotationCreateTools](../../src/EplanMcp.Host/Tools/AnnotationCreateTools.cs)、[McpServerSetup](../../src/EplanMcp.Host/McpServerSetup.cs)；[契约快照](../../tests/Contracts.Snapshots/server-contract.verified.json) |
| 分组 / Profile / 状态 | placement / full / Implemented (Unreleased) |
| 安全分类 / MCP 注解 | Write；readOnly/destructive/idempotent/openWorld 均 false |
| 模型说明 / 示例来源 | [AnnotationCreateContract](../../src/EplanMcp.Host/Tools/AnnotationCreateContract.cs)、[英文用法](../../src/EplanMcp.Host/Tools/Usage/eplan_annotation_create.md) |
| EPLAN 路线 | 复用 `placement.text.create.plan/execute`、`PlacementTextCreateExecuteOperation`、`EplanPlacementTextWriter` |
| API 证据 | [API 记录](../eplan/api-notes.md)（A97）；2026.0.3 各类型单独事务已创建提交；整批闭环见[待验台账](../process/large-pending.md) |
| 原生作用域 / 生命周期 | 项目路径与页名；每次事务重新取原生对象；不跨事务保留；不重复取得及释放同一包装对象的 Properties（A93） |
| 预览 / 审批 / 审计 | 省略或 true 的 `dry_run` 只预览；false 才执行；复用完整计划、指纹、WritePipeline、scratch/外部审批及审计 |
| 事务 / 回滚 / 恢复 | 单次 SafetyPoint、UndoStep、QuietModeStep；逐项读回后提交；任一失败整体回滚；ADR-0010 恢复与 unknown 停止重发 |
| 测量 | 端到端性能和整批原生显示待 LargeWrite；沿用 180 秒写入时限 |
| 测试 / 夹具 | Small/Medium 参数、默认预览、布局、读回和回滚；LargeWrite 新页混合批次、一次撤销、删除、导出图片；本任务不运行 EPLAN |

## 输入与输出

`project,page` 必填；Circuit、Overview 或 Graphics 页；`items` 1–50 项，严格成员和封闭 kind。坐标单位 mm、页左下原点、y 向上，有限且 0–10000。默认 `snap_to_grid:true`、`layout_guard:refuse`、`response_format:concise`，detailed 可容纳较大完整影响清单。可选 `show_page`；第 1 项的四种对象支持与 placement 一致的 `layer`、`pen`。复现已有图纸用 `layout_guard:warn`。

| kind | 必填字段 | 可选字段 / 说明 |
|---|---|---|
| hyperlink | position、link、contents | contents 为语言到文字，沿用文字语言校验 |
| qr_code | position、size_mm、text | size_mm 为正数 |
| spline | points | 至少两点，调用 Spline.SetPointAt |
| sector | center、radius_mm、start_angle、end_angle | 半径为正；角度为度，复用 arc 换算，SetArc 末参数 true |
| dimension | dimension_type | 见下表；数值由 EPLAN 计算，不接受调用方指定 |
| placeholder | position、name | records、variables 为字符串数组 |
| macro_box | position、macro_name | 创建 MacroBox 并设置 Location、MacroName |

| dimension_type | 参数（对应官方 XML） | 调用与写前校验 |
|---|---|---|
| linear | first、second、end：第一点、第二点、结束点 | DimensionItem.Create |
| angular | first、second、end：第一点、第二点、结束点 | CreateAngular；任意两点相同拒绝 |
| baseline / incremental / continued | points（至少两点）、end | DimensionGroup.Create，枚举 BaseLine / Incremental / Continued |
| circle | arc（eplan_object_find 的 locator）、end | DimensionCircle.Create；目标须为同页 Arc 且 Kind=Circle，否则写前拒绝 |

结果沿用 placement 的 requested/actual/bounds/snapped、完整 changes、空 connectionChanges、fingerprint、execution、warning/recovered；尺寸结果补 EPLAN 计算的 `dimension_text` 数组（单个尺寸也返回一项）。注释发现和删除由第 4 项补齐。Comment 的 Text.Create 抛 S063126，不开放 kind；使用 `eplan_import_pdf_comments`。

## 影响、错误与测试计划

新增入口不改变 placement 创建的公开枚举、预算或默认值。所有文字和链接内容均作为项目数据；不执行链接、不写外部文件。无新增依赖或格式迁移。复用布局护栏：可靠几何违反规则才拒绝；数据缺失或不可读只警告，不能一概返回 CAPABILITY_MISSING；写后只有已证实的页外对象整体回滚，其余按 ADR-0046 告警。

`INVALID_ARGUMENT` 返回字段路径；locator 过期为 `PLAN_STALE`；读取失败沿用明确错误。Small/Medium 锁定默认预览不写、显式 false 才写、所有参数和前置条件；LargeWrite 每个 kind 同页读回、一次撤销快照相同、删除后消失、`eplan_export_image` 显示。不得减少既有 Large 断言。

待决定：无；实机验证由维护者执行，记录在待验台账。

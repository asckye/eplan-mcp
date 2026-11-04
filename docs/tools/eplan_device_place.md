# 工具规格：`eplan_device_place`

ADR-0049（0.5.0）：省略 `dry_run` 或传 `true` 只预览，不请求审批、不修改项目或文件，返回 `execution: not_started`；执行须再次调用并显式传 `dry_run: false`，`hint` 会说明该步骤；预览返回 `fingerprint` 计划指纹。Small/Medium 覆盖省略、true、false 三条路径；审批、读回和撤销规则不变。

T7d 回归修复：`plc_text`、`terminal_strip_text` 均接收普通文本，固定写入语言无关 `??_??`（A54/T4b），拒绝嵌入原始多语言条目，避免与具体语言混用。读回允许显式 `language="??_??"`；核对 PLC 父关系、手工符号地址及辅助表达时使用 `eplan_plc_get` 的 `response_format="detailed"`。

性能：放置计划保留全项目重名筛查，同次检查复用名称列表；PLC 辅助表达查询按卡的完整名称前缀及类别过滤，不按页裁掉无页或跨页表达。符号读取每次调用按引脚数复用 DI 定义查询结果（包括未找到），不跨请求缓存原生对象；Large 测试复用符号搜索结果、逐步快照限定测试页，保留属性、撤销及全项目孤儿核对，并比较首尾全项目快照。尚未重新运行 EPLAN，实际耗时待实机复核。

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_device_place` |
| 所属分组 / Profile | `device` / `full`（M4c 的新工具默认只在 `full` 中） |
| 安全级别 | Write |
| MCP 注解 | `readOnlyHint` = false　`destructiveHint` = false　`idempotentHint` = false　`openWorldHint` = false |
| 状态 | Implemented (v0.1.0) |

## 1. 这个工具解决什么任务
"在 `=GAA+A1&EFS1/60` 上放一个 3RH2122-1HB40 接触器，标识 `-KF20`"。工具通过部件自带的宏或功能模板放置；传 `tag` 时在同一事务内一次重命名器件全部功能，含未放置功能（A85），省略则保留 EPLAN 自动标识。其余功能保持未放置，与 EPLAN“插入设备”相同。PLC 模块用 `eplan_macro_insert` 的 `part` 插入部件的组符号宏；需要画成多个符号时，先创建器件，再用 `library`/`symbol` 配合 `join_device:true` 向已有标识加入非主功能符号，最后用 `eplan_device_update` 的 `part` 指定部件（A82 第 5 条）。

M8 T7a 增加原生位置框：框内设备保留自己的结构，不继承框的结构（A55）；工具不重命名任何既有设备，受影响设备恒为空。仅支持 `+` 位置标识，固定 SPECIAL/SC 变体 0 与虚线，不提供装置段或任意外观配置。Large 验收代码已补，尚未运行。

M8 T2b 接通无部件库符号的规划与执行，依据 A13、A52–A55；沿既有写入策略、审批和审计，按部件调用保持兼容。产品 Large 验收代码已补，尚未运行。

当前库符号参数示例来自 [Usage 的 Examples](../../src/EplanMcp.Host/Tools/Usage/eplan_device_place.md)：

```json
{"project":"Line1","page":"=GAA+A1&EFS1/60","library":"IEC_symbol","symbol":"K","symbol_variant":0,"x":80,"y":160,"tag":"-K2","display_properties":[{"property_id":20214,"offset_x_mm":4,"offset_y_mm":4}],"connection_designations":["A1","A2"],"dry_run":true}
```

注册参数使用 `x`/`y` 和必填库变体 `symbol_variant`；坐标单位为毫米，原点在页左下角，y 向上。该示例由 Host 的用法示例测试校验参数形状；原生放置的实机状态见本页第 7 节与[待验台账](../process/large-pending.md)。

可选 `join_device` 默认 false，原有重复标识仍返回 `ALREADY_EXISTS`。true 时目标标识必须已存在（否则 `DEVICE_NOT_FOUND`），新功能沿用该名称且不是主功能；提交后按对象号读回名称和主功能状态。此参数仅支持库符号路线，与 `part`、端口列表（`connection_designations`/`connection_descriptions`）、端子或 PLC 路线同时出现均返回 `INVALID_ARGUMENT`。

## 2. 输入参数
| 参数 | 类型 | 必填 | 默认值 | 说明（写给模型看的） |
|---|---|---|---|---|
| `project` | string | 是 | — | 项目名，或 `eplan_project_list` 返回的 `path`。写工具必须显式给出 |
| `page` | string | 是 | — | 完整页名，必须是电路图页（`Circuit`） |
| `part` | string | 来源三选一 | — | 部件号，如 `SIE.3RH2122-1HB40`；部件必须在部件库中；可使用宏或功能模板（A82）。部件号先精确匹配，没有时再不区分大小写匹配 |
| `variant` | string | 否 | `"1"`，部件没有变体 1 而只有一个变体时取那一个 | 部件的变体 |
| `library`、`symbol`、`symbol_variant` | string、string、integer | 库符号来源三项必填 | — | 与 `part,variant` 互斥；项目已有库、完整符号名、非负原生变体号，来自 `eplan_symbol_search`；先精确匹配，无精确匹配时仅接受唯一的不区分大小写匹配；首批限普通 `Function`，专用元素留后续任务 |
| `display_properties` | object[] | 否 | 不新增显示属性 | 最多 8 项；严格成员 `{property_id,offset_x_mm,offset_y_mm}`，ID 封闭为 20214/20213/20211/20215/20058/20011/20027/20400/20403/20301；偏移为有限数，可为负，是相对父符号的位置，不是页坐标 |
| `connection_descriptions` | string[] | 否 | 不写 | 每个连接点的描述，按连接点数组顺序，数量必须相等；每项非空、最多 64 字符；写一基索引的 20023，PLC 点以只读的 20029 显示（例如 `DI a.0`）。只用于库符号来源 |
| `connection_designations` | string[] | 否 | 保留默认代号 | 按 T1 连接点数组顺序，每引脚一项，数量必须相等；每项非空、无控制字符且最多 64 字符；T2b 写入带一基索引的 20022，不使用汇总属性 20038 或 `Pin.Designation` |
| `location_box` | string | 位置框来源 | — | 与部件、库符号、tag、显示属性和引脚代号互斥；如 `+P1`，`+` 后 1–64 个字母、数字、下划线或点；不含通配、空白、设备段或页名 |
| `corner_x`、`corner_y` | number | 位置框/黑盒必填 | — | 右上角毫米坐标；x/y 为左下角，必须有限且 corner_x>x、corner_y>y；也用于 SPECIAL/DC |
| `x`、`y` | number | 是 | — | 插入点，单位毫米，原点在页的左下角，y 向上；页的幅面已知时必须在页内 |
| `tag` | string | 库符号来源必填，部件来源可选 | EPLAN 自动标识 | 以 `-` 开头的短标识（如 `-KF20`）取页结构但去掉 `&` 文档类型；或完整标识（如 `=GAA+A1-KF20`）。部件来源指定时一起重命名全部功能 |
| `join_device` | boolean | 否 | false | 仅库符号：向已有标识添加非主功能；与部件、端口列表、端子、PLC 路线互斥 |
| `show_page` | boolean | 否 | false | 绘制前打开或聚焦目标页，便于观看；仅影响界面，不修改数据，dry-run 也开页 |
| `dry_run` | boolean | 否 | true | 省略或 true 只预览；执行传 `dry_run: false` |

- 部件来源省略 `tag` 时保留 EPLAN 自动编号；指定时规则同 `eplan_device_update` 的 `new_tag`，已被其他设备使用（不区分大小写）时返回 `ALREADY_EXISTS`。库符号来源仍要求 `tag`。
- 变体不存在时 EPLAN 会静默改用第一个变体（XML），所以规划时先确认变体存在。
- 显示属性和连接点代号仅用于库符号来源；不提供任意属性写入，也不改变功能文本及其现有语言（A53）。

SPECIAL/DC 黑盒要求 `tag`、`corner_x/corner_y`（x/y 左下、corner 右上）。提交后 `device.type/category/isMainFunction` 与 `blackBox.bounds/functionsInside` 为原生读回。DCP/DCPP3 用同标识（不带冒号）并必须给一个 `connection_designations`，名称为"标识:代号"（如示例的 `-PF1:1`），代号重复时返回 `ALREADY_EXISTS`；框外也按原生结果返回。见 [A86](../eplan/api-notes.md)。

SPECIAL/BBDEF 经 Function.Create 放置 TextFunction（BusBarDefText、主功能），tag 必填；同标识 SPECIAL/BCP 同属一个器件。提交后 device.type/category/isMainFunction 为原生读回。依据 A86；Large 已补，尚未运行。

## 3. 输出
- 新建并放置端子排定义时，可从项目的插件解析结果读取 `defaults.strip_definition.symbol` 与 `defaults.strip_definition.variant`（必须同时有值）。只支持 SPECIAL 库；未配置时保留 `TDEF`/0。仅实际消费插件值时，dry-run 和执行结果附 `metadata.plugins`（精确 `id@version`）；复用已有排或创建不放置的排不报告消费。内部选择与来源进入计划指纹，改变后必须重新规划。
`show_page=true` 时返回 `pageShown`：开页成功为 true，失败为 false 并在 `hint` 说明原因；开页失败仍照常写入。省略或 false 时原结果不变。
```json
{
  "project": "Line1",
  "execution": "committed",
  "dryRun": false,
  "page": "=GAA+A1&EFS1/60",
  "part": "SIE.3RH2122-1HB40",
  "device": { "tag": "=GAA+A1-KF20", "visibleTag": "-KF20", "category": "Coil" },
  "functions": 5,
  "unplaced": 4,
  "unplaced_functions": ["=GAA+A1-KF20", "=GAA+A1-KF20", "=GAA+A1-KF20", "=GAA+A1-KF20"],
  "undo": "AI: eplan_device_place",
  "recovered": false,
  "hint": "4 functions of the device remain unplaced, as with EPLAN Insert Device. For a whole PLC module, use eplan_macro_insert with part for its assembly macro. For multiple symbols, place them with the same tag, then assign part with eplan_device_update."
}
```
- `unplaced_functions`：提交后按器件标识重新读回的未放置功能完整名称数组；没有时或预览/恢复时为 `[]`。模板 PLC 保留 `:1/:2` 等连接点后缀；`device.tag` 沿用主功能完整名称，可用返回名称查询设备。
- `functions`：设备的全部功能数；`unplaced`：其中没有放置的（S20：线圈放置，4 个触点未放置）。
- `undo`、`recovered`：同 `eplan_page_create`。标识按项目数据处理，经过 ResultShaper。
- `hint`：说明其余功能保持未放置，以及 PLC 组符号宏和同标识多个符号的绘制方式；页的幅面未知时（新建的空页，A14）说明只检查了 x、y 不小于 0。
- PLC 已确认提交后，无法定位对象或完成回读核对时仍返回 `execution=committed`；`hint` 警告列明未能验证的点身份/页、卡归属、原生属性或显示布局，提示读取页面与 `eplan_plc_get` 后再决定后续操作。保留已读到的 PLC 值及撤销步骤警告，不暂停写入；只有提交本身或执行结果确实不明时才返回 `OUTCOME_UNKNOWN`。
- 一行摘要：`Placed a device with 5 functions; Ctrl+Z in EPLAN undoes this step.` / `Dry run: would place one device. Nothing was changed.`
- 符号来源另带 `symbol`（解析后的库/符号/变体及局部引脚）和 `changes`（审批清单）；`functions=1,unplaced=0` 在 dry-run 为预测、提交后为读回，`part,variant` 为空。dry-run 不猜 `visibleTag/category`；提交后显示标识取请求的带前缀属性实际显示串，未请求时为空。20214 由 EPLAN 计算；默认空的 20029[2] 不算请求的属性。

- 位置框来源返回 `locationBox={structure,bounds:[minX,minY,maxX,maxY],outline:"dashed",affectedDevices:[]}`：dry-run 和 recovered 为预测，正常提交后为实际读回；`functions=0,unplaced=0`，兼容字段 `device.tag/category` 为位置标识/LocationBox（dry-run category 为空），不作为 `eplan_device_get` 的设备键；`symbol` 为解析后的 SPECIAL/SC。框内设备保持原结构，工具不重命名设备。

## 4. 错误
| 错误码 | 什么时候发生 | `execution` | 返回给模型的提示 |
|---|---|---|---|
| `INVALID_ARGUMENT` | 页不是电路图页；插入点超出页面；`tag` 不合法；变体不存在 | `not_started` | `metadata.violations` 列出全部问题 |
| `PART_NOT_FOUND` | 部件库中没有这个部件号 | `not_started` | 提示用 `eplan_part_search`（第一批）查部件号 |
| `INVALID_ARGUMENT` | EPLAN 没有返回功能，或者放置后页上没有新的功能（例如宏文件不存在） | `rolled_back` | 说明这个部件不能直接放置，提示改用 `eplan_macro_insert` |
| `INVALID_ARGUMENT` | 读回的标识与请求不同 | `rolled_back` | 同 `eplan_device_update` |
| `ALREADY_EXISTS` | `tag` 已被其他设备使用 | `not_started` | 同 `eplan_device_update` |
| `SYMBOL_NOT_FOUND` / `AMBIGUOUS_NAME` | 库/完整符号名/变体不存在，或忽略大小写匹配不唯一 | `not_started` | 提示查询 `eplan_symbol_search` |
| `CAPABILITY_MISSING` | 非普通功能符号或缺少符号发现适配 | `not_started` | 缺失项在 `metadata.missing`，选择普通功能符号或更新 Bridge |
| `EPLAN_OPERATION_FAILED` | 符号、显示布局或引脚实际读回与请求不符 | `rolled_back` | 全部撤回，读取页面后重新规划 |
| `PAGE_NOT_FOUND` / `AMBIGUOUS_NAME` | 页找不到 | `not_started` | 同 `eplan_page_get` |
| `PLAN_STALE` | 规划之后，页上的对象或标识的占用发生了变化 | `not_started` | 提示重新调用 |
| 策略、审批、流水线、锁定、项目解析、连接与授权类错误 | 同 `eplan_page_create` | 同 `eplan_page_create` | 同 `eplan_page_create` |

`PART_NOT_FOUND` 是新的错误码，加入错误码表。

## 5. 实现要点
- **Host**：走 `WritePipeline`，规划操作 `device.place.plan`，执行操作 `device.place.execute`，时限为默认的 60 秒。
- **符号规划**：Host 先严格校验来源组、显示成员/重复键/有限数及数量边界，问题带 JSON Pointer；既有规划操作复用 T1 `ISymbolReader`，在写入前读取项目已有库，不导入/同步主数据。复用页类型/边界、完整标识和全项目占用检查；预测一个无部件功能，逐项列相对偏移及页位置、20022 索引代号。指纹同时覆盖页状态、选定符号几何和全部预测变更；不生成逻辑连接。
- **符号审批文字**：`create function <页> (<库>/<符号>, variant <号> as <完整标识> at (<x>, <y>); no part)`；每个属性另列 `create property_placement <标识> (<ID> offset (<dx>, <dy>), page (<x+dx>, <y+dy>))`，每个代号列 `update pin <标识>: 20022[<索引>] '' -> '<值>'`。
- **符号执行**：审批后重核完整计划指纹，再解析原生变体；在唯一的 `FunctionTransactionScope` 中严格按 `LockAllObjects → UndoStep → Transaction → QuietModeStep` 创建无部件功能、命名、设置位置（毫米与 API 单位 1:1）、相对偏移的显示属性及带一基索引的 20022。读回返回对象的页、变体、标识、位置、零部件、引脚数量/实际代号和显示布局；不用 finder 判断未提交对象。任一不符或取消即释放列表再 Abort，不 CloseUndo、不留空步；Abort 后不碰新对象。成功 Commit 后 CloseUndo，一次调用一步撤销；不生成逻辑连接。
- **限制：锁定整个项目**：`LockAllObjects` 会锁住项目的全部对象（多用户项目中会挡住其他用户），只在单用户的 scratch 副本上实测过（S40/S41/T2b）。多用户项目是否应拒绝该分支待核实（负责人问题）；部件来源不使用它。
- **规划**（只读）：解析项目和页；在部件库中只读确认部件和变体存在（与 `eplan_part_search` 共用读法 `IPartsDatabase.Read`；无宏部件仍可通过功能模板放置，A82）；检查插入点；按 `eplan_device_update` 的规则拼出完整标识并检查占用（与它相同：同名或以 `<标识>:` 开头的功能，不区分大小写）。一条 `create` 变更：`Kind = "device"`，`Name` 为页名，`NewValue` 为 `SIE.3RH2122-1HB40 as =GAA+A1-KF20 at (80, 160)`。指纹：页上全部对象的会话内标识，加上目标标识在项目中的功能。
- **执行**：在 `TransactionScope`（`SafetyPoint` → `UndoStep` → `QuietModeStep`）中：
  1. `new DeviceService().CreateDevice(part, variant, page, new PointD(x, y))`，返回设备的全部功能（S20：5 个）；
  2. 没有返回功能、或者页上没有新放置的功能时抛出，整体回滚；
  3. 指定 `tag` 时，由 `Project.ParseNameAndProps` 解析 1100、1200、20013、20014 到新的 `FunctionBasePropertyList`，对返回的主功能调用一次 `NameService.RenameDevice(main, list, false, true)`，统一修改全部功能标识并保留连接点数据（A85）；省略则不改名；
  4. 通过后 `Commit()`，按新标识重新获取全部功能，再 `CloseUndo()`；避免跨事务保留原生对象或失效的未放置功能 ID（A79/A80/A85）。
- **已验证**（S20，A26）：`CreateDevice` 能被 `SafetyPoint` 回滚，提交后一次 Ctrl+Z 撤销；图上显示标识、技术参数、连接点和触点镜像；标识为 `=+-Q1`。
- **待 Large 测试确认**：
  - 改名后图上显示新标识，触点镜像跟着更新；
  - 部件不在库中、模板部件放置及变体不存在时 `CreateDevice` 的行为；
  - 改名与 `CreateDevice` 在同一个 `SafetyPoint` 中一起回滚、一起撤销；
  - 插入点与图上位置的关系（S20 的线圈在 (80, 160) 附近）。

- **显示标识与属性排列**：按符号新建的器件、PLC 点、端子、端子排定义和位置框创建后，都由 EPLAN 按完整标识计算显示标识（`NameService.AdjustVisibleName`），并保留符号的默认属性排列，标识显示在编辑器默认的位置。`display_properties` 只在调用方需要时额外新建文字，新建会使该对象改为用户定义的属性排列；端子排定义像 EPLAN 插入命令一样用 SPECIAL `TDEF`（端子排定义）变体 0 放置，不再新建或解除停靠文字。
- **位置框规划/执行（A55/S43）**：同页同位置标识（忽略大小写）返回 `ALREADY_EXISTS`；允许包围既有器件。页面边界未知、非有限或两角越界则拒绝。指纹覆盖两角、虚线、库变体、页对象/尺寸、同页框结构和全项目设备名称；审批后变化返回 `PLAN_STALE`。沿同一 `FunctionTransactionScope` 用 `SymbolReference.Create` 取得真实 `LocationBox`，`SetLogicalArea` 后通过 `NameParts.DESIGNATION_LOCATION` 设置位置。递归设置 `GetGraphics` 的笔型 `StyleId=1`，核对实际外框不越页、逻辑两角、结构和变体，并逐对象核对全项目既有功能名称完全不变；不是通过重命名实现结构。缺图形、读回不符或取消即 Abort，缓存列表先释放，不关闭空撤销步；成功只 Commit/CloseUndo 一次。模型说明和审批均明确受影响设备为零。

## 6. 写操作专项
- **dry-run**：返回第 3 节的结果，`execution = not_started`，`functions` 为 null。
- **审批**：项目在 scratch 之外时请求审批，表单为一行变更（第 5 节），注明可以按 Ctrl+Z 撤销"AI: eplan_device_place"。
- **审计、回滚、各阶段失败时的 `execution`**：同 `eplan_page_create`。
- 不是幂等的：指定标识后重复调用返回 `ALREADY_EXISTS`；省略标识会再创建一个器件。

## 7. 测试
- **Small**：标识的拼接与校验、变更清单的文字、摘要、schema 快照、注解。
- **Medium**：假 Bridge 上的 `dry_run`、`ALREADY_EXISTS`、`PART_NOT_FOUND`、放置后没有功能（`rolled_back`）、读回不一致（`rolled_back`）、`PLAN_STALE`。
- **T2a/T2b**：Small 覆盖互斥、封闭 ID、有限数/溢出和变更指纹；Medium 用假 Bridge 走真实规划/执行与 Host 流水线，覆盖提交、重复/冲突、中途失败、读回不符、释放后回滚、审批拒绝/批准后几何变化。LargeWrite 新增无部件线圈、20214 前缀/相对位置、引脚代号及重复/冲突检查；本次只编写，尚未运行。
- **Large**（示例项目的 scratch 副本）：新建页上放置 `SIE.3RH2122-1HB40`，`tag = "-KF20"`：读回 `=GAA+A1-KF20`，导出页面图片看到 `-KF20`；Ctrl+Z 一步撤销；scratch 之外的副本需要审批。
- **Agent 评测**："在新页 `=GAA+A1&EFS1/60` 上放一个 SIE.3RH2122-1HB40，标识 -KF20"，作答后用 `eplan_device_get` 核对。

- **T7a**：Small 覆盖来源互斥、标识、有限两角、原生适配器的结构/图形/虚线读回和取消；Medium 假 Bridge 覆盖 dry-run 零写入、审批、陈旧计划、读回不符和回滚无空步。LargeWrite 新页先放线圈，再围框，核对结构/虚线及线圈不变，一次撤销恢复到放框前；仅编写，未运行。

### T7c：单端子与父端子排

- D6：可选 `terminal_strip_position:[x,y]` 是新父排定义的页面绝对毫米位置；必须同时给 `terminal_strip_text`，仅首次新建父排可用，页界未知或越界拒绝。此时 `TerminalStrip.Create(Page)` 后设置 Location；省略仍建不放置定义（A55）。与首个端子共用一个事务和撤销步，读回排名、页、位置及语言无关功能文本；返回 terminal.stripPosition/stripText（预览为预测）。审批清单和指纹含位置及文本。LargeWrite 新页读回、导图和一步撤销仅编写，未运行。

- 端子来源 `terminal_strip`（短标识如 `-X6` 或完整标识）和 `terminal_number`（1–64 个字母、数字、下划线或点）；省略 `tag/part/variant/location_box` 和显示/引脚选项。可成对传 `library` + `symbol`，按符号库读出的 `FunctionCategory=Terminal` 校验，不设名称名单；类别不可读只警告，创建后仍核对原生类别。省略库名/符号时保留 IEC_symbol/X，`symbol_variant` 默认 0。支持 IEC_symbol/X2_2、XTR1、XFD 与 GRAPHICS/KA1_PE 中实际存在的 Terminal 变体。坐标沿既有页类型、有限数与页界检查。
- `terminal_strip_text` 为可选的 1–256 字符无控制符功能文本，按语言无关 `??_??` 写入并读回；仅新建父排时允许，追加端子须省略，不覆盖已有排文本。示例：`{"project":"Line1","page":"=F+A1/1","terminal_strip":"-X6","terminal_number":"1","terminal_strip_text":"操作控制盒","symbol_variant":0,"x":80,"y":160,"dry_run":true}`。
- 父排按完整名称复用（忽略大小写，沿用已有拼写），不存在时先建立不放置的定义；给定 `terminal_strip_position` 时改用 `TerminalStrip.Create(page)` 并设置 Location，均设为主功能，再 `Terminal.Create(strip,page,variant)`、命名 `<排>:<号>`、设置 20030。全项目同复合键忽略大小写拒绝；普通器件占用父排名也不能冒充端子排。审批逐项列父排/文本和端子；指纹覆盖全项目名称、页对象、实际库变体、坐标与全部变更。
- 沿 `FunctionTransactionScope` 一次提交；事务内核对实际排名、主功能、20030、原生 owner 对象及名称、位置、页和库变体。父关系为空视为提交前尚未建立（S43 relationPendingCommit，T7c 实测），读到非目标排才回滚，提交后用 `eplan_terminal_strip_get` 核实；不推测已归属；属性列表只取一次并在 Abort 前释放，Abort 后不碰新对象。成功一步撤销；不生成连接（A52/A55）。允许 LockAllObjects，负责人已确认无多用户编辑。
- 新增 `terminal={strip,number,owner,position:[x,y],sortCode,stripCreated}`；sortCode 原样返回 EPLAN 的 20809（空为 null），不写入或重编号；dry-run/recovered 的 owner/sortCode 为 null。`functions` 为本次新增功能数（含新父排），`unplaced=0`；段连接需后续独立生成，不能把创建成功当作连接分段已验证。
- Small 覆盖互斥/有限数/复合键与原生读回失败；Medium 假对象覆盖预览、复用、审批、stale、读回不符/取消后回滚及无空步。LargeWrite 新页创建 1..3、读回排成员/顺序、重复拒绝与逐调用一步撤销；另验独立生成后的连接分段。本次只编写，未运行。

### T7d：PLC 数字输入与常开/常闭触点

- **D3 扩展**：可选 `plc_plug_designation` 独立写入 20406（1–64 字符，无控制符，须有完整 PLC 来源）；点名始终为 `<卡>:<plc_connection_point>`，不把插头重复拼入 20022（2026-11-01 失败证据见 api-notes A78/A79）。显示增加 20406、20300；20213 显示卡设备标识，20300/20301 精确匹配 EPLAN 原生引用，包括空值，不以卡页名填造引用。请求引用显示时设置 20021=2；PLC 显示采用本地属性排列。提交前校验属性、归属和布局；事务内仅保存托管身份、属性显示及布局快照；提交结束后按 `ObjectIdentifier` 重新获取放置点和父 PLC 盒，不保留原生对象，不按名称查找。卡的 `PLCTerminals` 按点对象号核对成员，读回插头及原生引用并再次枚举显示；提交后核对失败保留现场，按 `committed` 加具体验证警告报告。完整插头与显示调用尚待 Large 复验。

- 库符号来源可显式创建 SPECIAL/PLCC 主 PLC 盒；连接点使用已有原生主 PLC 卡的 `tag`（如 `-KF1`），另给 `plc_connection_point`（如 `X10:2`、`DI a.0`）、`plc_address`（如 `I0.0`）、`plc_symbolic_address` 和 `plc_text`。仅连接点代号必填，其余三项可省略；传入值须非空且无控制字符；连接点最多 64 字符且不含前导冒号，其余最多 256 字符。仅支持发现到真实 DI 功能定义、且单引脚的 PLC 图形变体，不能把普通功能当作 PLC 点；不隐式创建父卡。
- 原生点名为 `<卡>:<连接点>`；按 `SameName` 容忍项目省略前导高层代号，保留已有拼写，父关系按名称识别。全项目复合键及明确传入的 I/O 地址忽略大小写拒绝重复；省略的属性不写入、不参与地址重复检查；普通器件不能冒充父卡。指纹额外覆盖父卡、全部通道及其属性，批准后变化即拒绝。
- `connection_designations` 省略时取单项连接点代号；显式传入时必须与它一致。地址写 20400、手工符号地址写 20402、功能文本写语言无关 20011；计算符号地址 20403 和卡页引用 20301 只读。显示表通过原生属性放置 20400/20403/20011/20301 的相对偏移构造，不使用静态文字，也不填造计算引用；20301 为概览交叉引用，缺少相应表达时可以为空。
- `Terminal.Create(PLC,variant)`、`PlaceAt`、真实 DI 定义及上述属性沿一个 `FunctionTransactionScope` 写入。事务内核对类别/定义、名称/页/位置、引脚数量、索引 20022 与 `Pin.Name`、输入属性及全部显示布局；读到非空父关系时须与目标卡同名。不符/异常/取消均先释放缓存列表再 Abort，不 CloseUndo；绝不在 Abort 后碰新对象。成功一次 Commit/CloseUndo，一步撤销。
- 显示须在全部写入与属性列表释放后，从已放置点的 `PropertyPlacements` 独立枚举读回；不接受仅创建句柄自检，也不从额外的器件级 PLC 端子补齐。每项核对属性 ID、数量、父点/页面、相对偏移、绝对位置和可见格式；20400/20403/20011/20301 文本分别与点的实际地址、计算符号地址、功能文本和原生交叉引用一致，空引用允许，不能以手工符号地址或页名代替。缺失或不符返回含 `/display_properties/{index}` 的 `EPLAN_OPERATION_FAILED` 并回滚，不提交也不留空撤销步；Small/Medium 覆盖创建已返回成功但显示缺失/移除/挂错对象的路径。T7d 反馈与未验证边界见 A55；既有 Large 按页面绝对位置及文本验证保持不变，本次未运行 EPLAN。
- 输出 `plc` 保留实际 `name/page/owner/address/manualSymbolicAddress/symbolicAddress/text/cardReference`。父关系、计算符号地址和 20301 可能提交后才建立，事务内返回 null 不代表已验证；提交后必须用 `eplan_plc_get` 复核。A55 每点另生同名无父 PLCTerminal，事务内 finder 不能完整枚举，因此此分支 `functions/unplaced` 返回 null；读工具每通道的 `parentlessRepresentations` 按同名且无 PLC 父关系计数，不按名称去重，不算入卡的通道成员。
- NO/NC 复用普通库符号来源，按发现的 NoContact/NcContact 类别选择实际符号；显式 `connection_designations=["13","14"]` / `["11","12"]`，写一基索引 20022 并核对 `Pin.Name`，不写汇总 20038，不比较内部 `Pin.Designation`。
- Small 覆盖严格参数、有限偏移、复合键及序列化；Medium 假对象覆盖预览、审批拒绝/stale、字段或父关系不符、失败/取消回滚无空步。LargeWrite 新页显式创建卡、两个输入和 NO/NC，提交后用 plc_get/device_get 核对属性、父关系、辅助表达数量及原生显示，逐调用一次撤销恢复；本次仅编写，未运行。无新工具或 Bridge 操作。
共享布局守卫：R1–R5、R8 拒绝并给出规则与可用时的空位建议；R6/R7/R9 默认警告，layout.strict 时拒绝。图框可用区无法读取时继续使用页尺寸减 layout.marginMm 回退并在结果中说明。预测未知不拒绝：写前校验插入点、已知外框及路由；写后在原事务内测量符号与属性文字外框，再校验 R1–R9：按 [ADR-0046](../adr/0046-readback-deviation-warnings.md)，只有新对象超出页面（S2）才整次回滚；其余违规照常提交，warning 中逐条给出 `Readback deviation: …`（规则、对象、实测外框和修复工具；偏差不搜索空位，回滚时才给最近空位），外框无法测量记为未验证。warning/hint 报告写前与写后检查；拒绝的 metadata.layoutViolations/layoutChecks 提供几何和阶段。连接提交后才生效的几何沿用原撤销补偿。参数与限制见 [布局守卫设计](../design/2026-11-01-layout-guard.md)。

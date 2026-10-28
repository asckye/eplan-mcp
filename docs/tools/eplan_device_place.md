# 工具规格：`eplan_device_place`

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_device_place` |
| 所属分组 / Profile | `device` / `full`（M4c 的新工具默认只在 `full` 中） |
| 安全级别 | Write |
| MCP 注解 | `readOnlyHint` = false　`destructiveHint` = false　`idempotentHint` = false　`openWorldHint` = false |
| 状态 | Proposed（M4c 第二批） |

## 1. 这个工具解决什么任务
"在 `=GAA+A1&EFS1/60` 上放一个 3RH2122-1HB40 接触器，标识 `-KF20`"。代理知道部件号，不必先找窗口宏：工具放置部件自带的宏，图上与工程师手工放置的设备相同（标识、技术参数、连接点、触点镜像，S20），再给它目标标识。需要一组预先设计好的电路时用 `eplan_macro_insert`。

M8 T7a 增加原生位置框：框内设备保留自己的结构，不继承框的结构（A55）；工具不重命名任何既有设备，受影响设备恒为空。仅支持 `+` 位置标识，固定 SPECIAL/SC 变体 0 与虚线，不提供装置段或任意外观配置。Large 验收代码已补，尚未运行。

M8 T2b 接通无部件库符号的规划与执行，依据 A13、A52–A55；沿既有写入策略、审批和审计，按部件调用保持兼容。产品 Large 验收代码已补，尚未运行。

## 2. 输入参数
| 参数 | 类型 | 必填 | 默认值 | 说明（写给模型看的） |
|---|---|---|---|---|
| `project` | string | 是 | — | 项目名，或 `eplan_project_list` 返回的 `path`。写工具必须显式给出 |
| `page` | string | 是 | — | 完整页名，必须是电路图页（`Circuit`） |
| `part` | string | 来源三选一 | — | 部件号，如 `SIE.3RH2122-1HB40`；部件必须在部件库中并且有宏。部件号先精确匹配，没有时再不区分大小写匹配 |
| `variant` | string | 否 | `"1"`，部件没有变体 1 而只有一个变体时取那一个 | 部件的变体 |
| `library`、`symbol`、`symbol_variant` | string、string、integer | 库符号来源三项必填 | — | 与 `part,variant` 互斥；项目已有库、完整符号名、非负原生变体号，来自 `eplan_symbol_search`；先精确匹配，无精确匹配时仅接受唯一的不区分大小写匹配；首批限普通 `Function`，专用元素留后续任务 |
| `display_properties` | object[] | 否 | 不新增显示属性 | 最多 8 项；严格成员 `{property_id,offset_x_mm,offset_y_mm}`，ID 封闭为 20214/20213/20211/20215/20058/20011/20027；偏移为有限数，可为负，是相对父符号的位置，不是页坐标 |
| `connection_designations` | string[] | 否 | 保留默认代号 | 按 T1 连接点数组顺序，每引脚一项，数量必须相等；每项非空、无控制字符且最多 64 字符；T2b 写入带一基索引的 20022，不使用汇总属性 20038 或 `Pin.Designation` |
| `location_box` | string | 位置框来源 | — | 与部件、库符号、tag、显示属性和引脚代号互斥；如 `+P1`，`+` 后 1–64 个字母、数字、下划线或点；不含通配、空白、设备段或页名 |
| `corner_x`、`corner_y` | number | 位置框必填 | — | 右上角毫米坐标；x/y 为左下角，必须有限且 corner_x>x、corner_y>y；其余来源禁止 |
| `x`、`y` | number | 是 | — | 插入点，单位毫米，原点在页的左下角，y 向上；页的幅面已知时必须在页内 |
| `tag` | string | 设备来源必填 | — | 设备的标识：以 `-` 开头的短标识（如 `-KF20`），结构取自页（页的结构去掉 `&` 文档类型：`=GAA+A1&EFS1/60` 得到 `=GAA+A1`）；或者完整标识（如 `=GAA+A1-KF20`） |
| `dry_run` | boolean | 否 | false | 为 true 时只返回将要进行的放置，不请求审批，也不修改项目 |

- 设备来源 `tag` 必填：`CreateDevice` 放置的设备标识不带页的结构、也不编号（S20：`=+-Q1`），工具总是给它一个确定的标识。规则同 `eplan_device_update` 的 `new_tag`，已被其他设备使用（不区分大小写）时返回 `ALREADY_EXISTS`。
- 变体不存在时 EPLAN 会静默改用第一个变体（XML），所以规划时先确认变体存在。
- 显示属性和连接点代号仅用于库符号来源；不提供任意属性写入，也不改变功能文本及其现有语言（A53）。

## 3. 输出
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
  "undo": "AI: eplan_device_place",
  "recovered": false,
  "hint": "4 functions of the device (its contacts) are not placed yet; the engineer places them in EPLAN, for example from the device navigator."
}
```
- `functions`：设备的全部功能数；`unplaced`：其中没有放置的（S20：线圈放置，4 个触点未放置）。
- `undo`、`recovered`：同 `eplan_page_create`。标识按项目数据处理，经过 ResultShaper。
- `hint`：有未放置的功能时说明它们要在 EPLAN 中放置（实现时改：`eplan_macro_insert` 会新建设备，放不了已有设备的功能）；页的幅面未知时（新建的空页，A14）说明只检查了 x、y 不小于 0。
- 一行摘要：`Placed a device with 5 functions; Ctrl+Z in EPLAN undoes this step.` / `Dry run: would place one device. Nothing was changed.`
- 符号来源另带 `symbol`（解析后的库/符号/变体及局部引脚）和 `changes`（审批清单）；`functions=1,unplaced=0` 在 dry-run 为预测、提交后为读回，`part,variant` 为空。dry-run 不猜 `visibleTag/category`；提交后显示标识取请求的带前缀属性实际显示串，未请求时为空。20214 由 EPLAN 计算；默认空的 20029[2] 不算请求的属性。

- 位置框来源返回 `locationBox={structure,bounds:[minX,minY,maxX,maxY],outline:"dashed",affectedDevices:[]}`：dry-run 和 recovered 为预测，正常提交后为实际读回；`functions=0,unplaced=0`，兼容字段 `device.tag/category` 为位置标识/LocationBox（dry-run category 为空），不作为 `eplan_device_get` 的设备键；`symbol` 为解析后的 SPECIAL/SC。框内设备保持原结构，工具不重命名设备。

## 4. 错误
| 错误码 | 什么时候发生 | `execution` | 返回给模型的提示 |
|---|---|---|---|
| `INVALID_ARGUMENT` | 页不是电路图页；插入点超出页面；`tag` 不合法；变体不存在；部件库中的部件没有宏（`ARTICLE_MACRO` 为空，实现时从执行后移到规划时：部件库已经能看出来，不必等 EPLAN 回滚） | `not_started` | `metadata.violations` 列出全部问题；没有宏时提示改用 `eplan_macro_insert` |
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
- **规划**（只读）：解析项目和页；在部件库中只读确认部件和变体存在、部件有宏（与 `eplan_part_search` 共用读法 `IPartsDatabase.Read`）；检查插入点；按 `eplan_device_update` 的规则拼出完整标识并检查占用（与它相同：同名或以 `<标识>:` 开头的功能，不区分大小写）。一条 `create` 变更：`Kind = "device"`，`Name` 为页名，`NewValue` 为 `SIE.3RH2122-1HB40 as =GAA+A1-KF20 at (80, 160)`。指纹：页上全部对象的会话内标识，加上目标标识在项目中的功能。
- **执行**：在 `TransactionScope`（`SafetyPoint` → `UndoStep` → `QuietModeStep`）中：
  1. `new DeviceService().CreateDevice(part, variant, page, new PointD(x, y))`，返回设备的全部功能（S20：5 个）；
  2. 没有返回功能、或者页上没有新放置的功能时抛出，整体回滚；
  3. 把返回的全部功能改为目标标识（主功能和辅助功能一起改，A13），读回核对。改名针对 `CreateDevice` 返回的对象，不按名称 `=+-Q1` 查找：项目中可能已有同名的功能；
  4. 通过后 `Commit()`，再 `CloseUndo()`。
- **已验证**（S20，A26）：`CreateDevice` 能被 `SafetyPoint` 回滚，提交后一次 Ctrl+Z 撤销；图上显示标识、技术参数、连接点和触点镜像；标识为 `=+-Q1`。
- **待 Large 测试确认**：
  - 改名后图上显示新标识，触点镜像跟着更新；
  - 部件不在库中、部件没有宏、变体不存在时 `CreateDevice` 的行为；
  - 改名与 `CreateDevice` 在同一个 `SafetyPoint` 中一起回滚、一起撤销；
  - 插入点与图上位置的关系（S20 的线圈在 (80, 160) 附近）。

- **位置框规划/执行（A55/S43）**：同页同位置标识（忽略大小写）返回 `ALREADY_EXISTS`；允许包围既有器件。页面边界未知、非有限或两角越界则拒绝。指纹覆盖两角、虚线、库变体、页对象/尺寸、同页框结构和全项目设备名称；审批后变化返回 `PLAN_STALE`。沿同一 `FunctionTransactionScope` 用 `SymbolReference.Create` 取得真实 `LocationBox`，`SetLogicalArea` 后通过 `NameParts.DESIGNATION_LOCATION` 设置位置。递归设置 `GetGraphics` 的笔型 `StyleId=1`，核对实际外框不越页、逻辑两角、结构和变体，并逐对象核对全项目既有功能名称完全不变；不是通过重命名实现结构。缺图形、读回不符或取消即 Abort，缓存列表先释放，不关闭空撤销步；成功只 Commit/CloseUndo 一次。模型说明和审批均明确受影响设备为零。

## 6. 写操作专项
- **dry-run**：返回第 3 节的结果，`execution = not_started`，`functions` 为 null。
- **审批**：项目在 scratch 之外时请求审批，表单为一行变更（第 5 节），注明可以按 Ctrl+Z 撤销"AI: eplan_device_place"。
- **审计、回滚、各阶段失败时的 `execution`**：同 `eplan_page_create`。
- 不是幂等的：同样的调用再来一次，因标识已被占用返回 `ALREADY_EXISTS`。

## 7. 测试
- **Small**：标识的拼接与校验、变更清单的文字、摘要、schema 快照、注解。
- **Medium**：假 Bridge 上的 `dry_run`、`ALREADY_EXISTS`、`PART_NOT_FOUND`、放置后没有功能（`rolled_back`）、读回不一致（`rolled_back`）、`PLAN_STALE`。
- **T2a/T2b**：Small 覆盖互斥、封闭 ID、有限数/溢出和变更指纹；Medium 用假 Bridge 走真实规划/执行与 Host 流水线，覆盖提交、重复/冲突、中途失败、读回不符、释放后回滚、审批拒绝/批准后几何变化。LargeWrite 新增无部件线圈、20214 前缀/相对位置、引脚代号及重复/冲突检查；本次只编写，尚未运行。
- **Large**（示例项目的 scratch 副本）：新建页上放置 `SIE.3RH2122-1HB40`，`tag = "-KF20"`：读回 `=GAA+A1-KF20`，导出页面图片看到 `-KF20`；Ctrl+Z 一步撤销；scratch 之外的副本需要审批。
- **Agent 评测**："在新页 `=GAA+A1&EFS1/60` 上放一个 SIE.3RH2122-1HB40，标识 -KF20"，作答后用 `eplan_device_get` 核对。

- **T7a**：Small 覆盖来源互斥、标识、有限两角、原生适配器的结构/图形/虚线读回和取消；Medium 假 Bridge 覆盖 dry-run 零写入、审批、陈旧计划、读回不符和回滚无空步。LargeWrite 新页先放线圈，再围框，核对结构/虚线及线圈不变，一次撤销恢复到放框前；仅编写，未运行。

### T7c：单端子与父端子排

- 新增互斥来源 `terminal_strip`（短标识如 `-X6` 或完整标识）和 `terminal_number`（1–64 个字母、数字、下划线或点）；省略 `tag/part/variant/library/symbol/location_box` 和显示/引脚选项。固定 IEC_symbol/X，`symbol_variant` 默认 0。坐标沿既有页类型、有限数与页界检查。
- `terminal_strip_text` 为可选的 1–256 字符无控制符功能文本，按语言无关 `??_??` 写入并读回；仅新建父排时允许，追加端子须省略，不覆盖已有排文本。示例：`{"project":"Line1","page":"=F+A1/1","terminal_strip":"-X6","terminal_number":"1","terminal_strip_text":"操作控制盒","symbol_variant":0,"x":80,"y":160,"dry_run":true}`。
- 父排按完整名称复用（忽略大小写，沿用已有拼写），不存在时先 `TerminalStrip.Create(page)` 并设主功能，再 `Terminal.Create(strip,page,variant)`、命名 `<排>:<号>`、设置 20030。全项目同复合键忽略大小写拒绝；普通器件占用父排名也不能冒充端子排。审批逐项列父排/文本和端子；指纹覆盖全项目名称、页对象、实际库变体、坐标与全部变更。
- 沿 `FunctionTransactionScope` 一次提交；事务内核对实际排名、主功能、20030、原生 owner 对象及名称、位置、页和库变体。父关系为空视为提交前尚未建立（S43 relationPendingCommit，T7c 实测），读到非目标排才回滚，提交后用 `eplan_terminal_strip_get` 核实；不推测已归属；属性列表只取一次并在 Abort 前释放，Abort 后不碰新对象。成功一步撤销；不生成连接（A52/A55）。允许 LockAllObjects，负责人已确认无多用户编辑。
- 新增 `terminal={strip,number,owner,position:[x,y],sortCode,stripCreated}`；sortCode 原样返回 EPLAN 的 20809（空为 null），不写入或重编号；dry-run/recovered 的 owner/sortCode 为 null。`functions` 为本次新增功能数（含新父排），`unplaced=0`；段连接需后续独立生成，不能把创建成功当作连接分段已验证。
- Small 覆盖互斥/有限数/复合键与原生读回失败；Medium 假对象覆盖预览、复用、审批、stale、读回不符/取消后回滚及无空步。LargeWrite 新页创建 1..3、读回排成员/顺序、重复拒绝与逐调用一步撤销；另验独立生成后的连接分段。本次只编写，未运行。

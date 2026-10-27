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

M8 T2a 增加无部件库符号的只读规划；执行分支在 T2b 验证前返回 `CAPABILITY_MISSING`，不请求审批或写入。依据 A13、A52–A55；按部件调用的行为和响应保持不变。

## 2. 输入参数
| 参数 | 类型 | 必填 | 默认值 | 说明（写给模型看的） |
|---|---|---|---|---|
| `project` | string | 是 | — | 项目名，或 `eplan_project_list` 返回的 `path`。写工具必须显式给出 |
| `page` | string | 是 | — | 完整页名，必须是电路图页（`Circuit`） |
| `part` | string | 来源二选一 | — | 部件号，如 `SIE.3RH2122-1HB40`；部件必须在部件库中并且有宏。部件号先精确匹配，没有时再不区分大小写匹配 |
| `variant` | string | 否 | `"1"`，部件没有变体 1 而只有一个变体时取那一个 | 部件的变体 |
| `library`、`symbol`、`symbol_variant` | string、string、integer | 库符号来源三项必填 | — | 与 `part,variant` 互斥；项目已有库、完整符号名、非负原生变体号，来自 `eplan_symbol_search`；先精确匹配，无精确匹配时仅接受唯一的不区分大小写匹配；首批限普通 `Function`，专用元素留后续任务 |
| `display_properties` | object[] | 否 | 不新增显示属性 | 最多 8 项；严格成员 `{property_id,offset_x_mm,offset_y_mm}`，ID 封闭为 20214/20213/20211/20215/20058/20011/20027；偏移为有限数，可为负，是相对父符号的位置，不是页坐标 |
| `connection_designations` | string[] | 否 | 保留默认代号 | 按 T1 连接点数组顺序，每引脚一项，数量必须相等；每项非空、无控制字符且最多 64 字符；T2b 写入带一基索引的 20022，不使用汇总属性 20038 或 `Pin.Designation` |
| `x`、`y` | number | 是 | — | 插入点，单位毫米，原点在页的左下角，y 向上；页的幅面已知时必须在页内 |
| `tag` | string | 是 | — | 设备的标识：以 `-` 开头的短标识（如 `-KF20`），结构取自页（页的结构去掉 `&` 文档类型：`=GAA+A1&EFS1/60` 得到 `=GAA+A1`）；或者完整标识（如 `=GAA+A1-KF20`） |
| `dry_run` | boolean | 否 | false | 为 true 时只返回将要进行的放置，不请求审批，也不修改项目 |

- `tag` 必填：`CreateDevice` 放置的设备标识不带页的结构、也不编号（S20：`=+-Q1`），工具总是给它一个确定的标识。规则同 `eplan_device_update` 的 `new_tag`，已被其他设备使用（不区分大小写）时返回 `ALREADY_EXISTS`。
- 变体不存在时 EPLAN 会静默改用第一个变体（XML），所以规划时先确认变体存在。
- 显示属性和连接点代号仅用于库符号来源；不提供任意属性写入。本期不写功能文本，T2b 如更新单一语言须保留其他语言（A53）。

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
- 符号 dry-run 另带 `symbol`（解析后的库/符号/变体及局部引脚）和 `changes`（预期功能、属性放置和引脚代号的审批文字）；`functions=1,unplaced=0` 仅为预测，`part,variant` 为空，`visibleTag/category` 不猜测。20214 的实际显示串由 EPLAN 计算；不把默认空的 20029[2] 当作请求的显示属性。按部件响应不增加字段。

## 4. 错误
| 错误码 | 什么时候发生 | `execution` | 返回给模型的提示 |
|---|---|---|---|
| `INVALID_ARGUMENT` | 页不是电路图页；插入点超出页面；`tag` 不合法；变体不存在；部件库中的部件没有宏（`ARTICLE_MACRO` 为空，实现时从执行后移到规划时：部件库已经能看出来，不必等 EPLAN 回滚） | `not_started` | `metadata.violations` 列出全部问题；没有宏时提示改用 `eplan_macro_insert` |
| `PART_NOT_FOUND` | 部件库中没有这个部件号 | `not_started` | 提示用 `eplan_part_search`（第一批）查部件号 |
| `INVALID_ARGUMENT` | EPLAN 没有返回功能，或者放置后页上没有新的功能（例如宏文件不存在） | `rolled_back` | 说明这个部件不能直接放置，提示改用 `eplan_macro_insert` |
| `INVALID_ARGUMENT` | 读回的标识与请求不同 | `rolled_back` | 同 `eplan_device_update` |
| `ALREADY_EXISTS` | `tag` 已被其他设备使用 | `not_started` | 同 `eplan_device_update` |
| `SYMBOL_NOT_FOUND` / `AMBIGUOUS_NAME` | 库/完整符号名/变体不存在，或忽略大小写匹配不唯一 | `not_started` | 提示查询 `eplan_symbol_search` |
| `CAPABILITY_MISSING` | 符号执行或非普通功能符号 | `not_started` | 缺失项在 `metadata.missing`；T2b 前不启用执行 |
| `PAGE_NOT_FOUND` / `AMBIGUOUS_NAME` | 页找不到 | `not_started` | 同 `eplan_page_get` |
| `PLAN_STALE` | 规划之后，页上的对象或标识的占用发生了变化 | `not_started` | 提示重新调用 |
| 策略、审批、流水线、锁定、项目解析、连接与授权类错误 | 同 `eplan_page_create` | 同 `eplan_page_create` | 同 `eplan_page_create` |

`PART_NOT_FOUND` 是新的错误码，加入错误码表。

## 5. 实现要点
- **Host**：走 `WritePipeline`，规划操作 `device.place.plan`，执行操作 `device.place.execute`，时限为默认的 60 秒。
- **符号规划**：Host 先严格校验来源组、显示成员/重复键/有限数及数量边界，问题带 JSON Pointer；既有规划操作复用 T1 `ISymbolReader`，在写入前读取项目已有库，不导入/同步主数据。复用页类型/边界、完整标识和全项目占用检查；预测一个无部件功能，逐项列相对偏移及页位置、20022 索引代号。指纹同时覆盖页状态、选定符号几何和全部预测变更；不生成逻辑连接。
- **符号审批文字**：`create function <页> (<库>/<符号>, variant <号> as <完整标识> at (<x>, <y>); no part)`；每个属性另列 `create property_placement <标识> (<ID> offset (<dx>, <dy>), page (<x+dx>, <y+dy>))`，每个代号列 `update pin <标识>: 20022[<索引>] '' -> '<值>'`。T2a 可预览这些行，实际执行在 Host 和 Bridge 均封闭拒绝；未来执行须沿 A13/S41 的 Transaction，不套部件的 SafetyPoint 路径。
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

## 6. 写操作专项
- **dry-run**：返回第 3 节的结果，`execution = not_started`，`functions` 为 null。
- **审批**：项目在 scratch 之外时请求审批，表单为一行变更（第 5 节），注明可以按 Ctrl+Z 撤销"AI: eplan_device_place"。
- **审计、回滚、各阶段失败时的 `execution`**：同 `eplan_page_create`。
- 不是幂等的：同样的调用再来一次，因标识已被占用返回 `ALREADY_EXISTS`。

## 7. 测试
- **Small**：标识的拼接与校验、变更清单的文字、摘要、schema 快照、注解。
- **Medium**：假 Bridge 上的 `dry_run`、`ALREADY_EXISTS`、`PART_NOT_FOUND`、放置后没有功能（`rolled_back`）、读回不一致（`rolled_back`）、`PLAN_STALE`。
- **T2a**：Small 覆盖互斥/不完整来源、显示上限/封闭 ID/严格成员/有限数、逐引脚数量；Medium 用假 T1 读取器走真实规划和 Host 流水线，验证相对偏移、重名、审批行及旧部件调用兼容。无 EPLAN；符号执行事务和 Large 验收留 T2b。
- **Large**（示例项目的 scratch 副本）：新建页上放置 `SIE.3RH2122-1HB40`，`tag = "-KF20"`：读回 `=GAA+A1-KF20`，导出页面图片看到 `-KF20`；Ctrl+Z 一步撤销；scratch 之外的副本需要审批。
- **Agent 评测**："在新页 `=GAA+A1&EFS1/60` 上放一个 SIE.3RH2122-1HB40，标识 -KF20"，作答后用 `eplan_device_get` 核对。

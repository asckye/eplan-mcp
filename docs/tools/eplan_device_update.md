# 工具规格：`eplan_device_update`

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_device_update` |
| 所属分组 / Profile | `device` / `core` |
| 安全级别 | Write |
| MCP 注解 | `readOnlyHint` = false　`destructiveHint` = false　`idempotentHint` = false　`openWorldHint` = false |
| 状态 | Implemented (v0.1.0) |

## 1. 这个工具解决什么任务
"把 `-KF1` 改成 `-KF10`""把 `=GAA+A1-FC1` 挪到 `=GAB1` 下"。代理给出设备的现有标识和新标识，工具把整个设备改名：主功能和全部辅助功能（包括其他页上的触点，即交叉引用）一起改，设备保持完整。还可给现有设备的唯一主功能或精确匹配的单个端子追加部件引用；部件来自当前部件库，不会替换原有引用。

## 2. 输入参数
| 参数 | 类型 | 必填 | 默认值 | 说明（写给模型看的） |
|---|---|---|---|---|
| `project` | string | 是 | — | 项目名，或 `eplan_project_list` 返回的 `path`。写工具必须显式给出 |
| `tag` | string | 是 | — | 现有的设备标识。完整标识（如 `=GAA+A1-KF1`）总是唯一；短标识（如 `-KF1`）在多个结构下出现时返回 `AMBIGUOUS_NAME` |
| `new_tag` | string | 否 | — | 新标识：完整标识（如 `=GAA+A1-KF10`）；或以 `-` 开头的短标识（如 `-KF10`），表示保留设备现在的结构 |
| `part` | string | 否 | — | 要追加的部件号；与 `new_tag`、`update_from_parts` 至少一个；按部件库精确或唯一不区分大小写匹配 |
| `variant` | string | 否 | "1" | 部件变体；只能随 `part` 提供，必须在库中存在 |
| `count` | integer | 否 | 1 | 追加部件数量，必须为正整数，只能随 `part` 提供 |
| `update_from_parts` | boolean | 否 | false | 把已引用部件的数据写到功能上；必须单独调用（A93） |
| `dry_run` | boolean | 否 | true | 省略或 true 只预览，不审批、不写入；执行须显式 false |

- `tag` 的查找规则与 `eplan_device_get` 相同；另支持全部为非主 `PLCTerminal` 的模块标识。此类模块可改名到已有 `PLCBox` 的标识，保留各点后缀。
- 给出 `new_tag` 时不能为空，不能含空白和控制字符，必须含有 `-` 开头的设备部分；与现有标识相同时返回 `INVALID_ARGUMENT`。只改大小写（`-kf1` → `-KF1`）是允许的。

## 3. 输出
省略或 `dry_run: true` 返回设备变更清单 `changes`、`fingerprint` 和 `execution: not_started`；`hint` 提示以相同参数显式传 `dry_run: false` 执行。
```json
{
  "project": "Line1",
  "execution": "committed",
  "dryRun": false,
  "device": {
    "tag": "=GAA+A1-KF10",
    "previousTag": "=GAA+A1-KF1",
    "visibleTag": "-KF10",
    "functions": 3,
    "pages": ["=GAA+A1&EFS1/1", "=GAA+A1&EFS1/2"]
  },
  "undo": "AI: eplan_device_update",
  "recovered": false,
  "hint": null
}
```
- `device.functions`：改名时为全部改名功能数；只赋值部件时为唯一主功能或端子的 1。`device.pages`：这些受影响功能所在的页，最多 20 个，按 EPLAN 的顺序列出；只赋值部件时只列目标功能所在页，未放置则为空。
- 提交后是读回的值；`dry_run` 时是将要得到的值，`visibleTag` 为 `null`。
- `undo`、`recovered`：同 `eplan_page_create`。
- 标识按项目数据处理，经过 ResultShaper。
- 一行摘要（只有计数，不回显项目名，架构 8.5）：
  - `Renamed a device and its 2 auxiliary functions; Ctrl+Z in EPLAN undoes this step.`
  - `Dry run: would rename a device and its 2 auxiliary functions. Nothing was changed.`
- `dry_run` 和取回结果时的 `functions`、`pages` 来自计划的 `details`（`functions`、`pageCount`、`pages`），审批表单的"共 N 个功能，分布在 M 页"也取自这里。

## 4. 错误
| 错误码 | 什么时候发生 | `execution` | 返回给模型的提示 |
|---|---|---|---|
| `INVALID_ARGUMENT` | `new_tag` 不合法，或与现有标识相同；部件、变体、唯一主功能或重复引用检查失败；`new_tag` 以 `-` 开头但拼不出现在的结构 | `not_started` | `metadata.violations` 列出全部问题 |
| `INVALID_ARGUMENT` | 读回的标识与请求不同（项目结构对这类设备不用新标识中的某一部分，EPLAN 把它丢掉了），或者改名后仍有功能带着旧标识 | `rolled_back` | `metadata.requested`、`metadata.actual`；提示参照同类设备的标识（`eplan_device_list`） |
| `DEVICE_NOT_FOUND` / `AMBIGUOUS_NAME` | `tag` 找不到，或短标识对应多个设备 | `not_started` | 同 `eplan_device_get` |
| `ALREADY_EXISTS` | 新标识已被其他设备使用（不区分大小写），且不满足非主 PLC 点并入已有 PLC 盒的条件 | `not_started` | "'<new_tag>' is already used by another device; giving this device the same tag would merge the two devices in EPLAN, only PLC terminals without a main function may join an existing PLC box. Choose a free tag; eplan_device_list with tag_contains shows the tags in use." |
| `PLAN_STALE` | 规划之后，这个设备的功能发生了变化，或者新标识被占用了 | `not_started` | 提示重新调用 |
| 策略、审批、流水线、锁定、项目解析、连接与授权类错误 | 同 `eplan_page_create` | 同 `eplan_page_create` | 同 `eplan_page_create` |

## 5. 实现要点
- **Host**：走 `WritePipeline`，规划操作是 `device.update.plan`，执行操作是 `device.update.execute`。执行时限为默认的 60 秒。
- **改名的范围**（A85）：`NameService.RenameDevice(main, FunctionBasePropertyList, false, true)` 一次改名全部功能，保留连接点后缀和描述属性。比如 `=K1+A2-A0015:3.1` 改为 `=K1+A2-KE1:3.1`；不逐个赋值 `Function.Name`。
- **新标识**：
  - 完整标识原样使用；
  - 以 `-` 开头时，先算出设备现在的结构：从 `Name` 末尾去掉 `-` 加 `FUNC_DEVICETAG_FULLNAME`（S6），再接上 `new_tag`。`Name` 不以它结尾时，要求给出完整标识。
- **规划**：
  - 找到设备和改名范围；
  - 检查新标识：全部功能中有不区分大小写同名、又不在改名范围内的，返回 `ALREADY_EXISTS`；仅当源全部为非主 `PLCTerminal`、目标存在 `PLCBox` 时允许并入。EPLAN 自己不检查重名（A13）；
  - 一条 `update` 变更：`Property = "tag"`，表单显示为 `update device =GAA+A1-KF1: tag '=GAA+A1-KF1' -> '=GAA+A1-KF10'`；
  - 指纹排序后取 SHA-256，包含源功能类别与主功能状态；并入时还包含目标原功能的身份与类别：
    - 改名范围内每个功能的 `Name` 和会话内标识（`ToStringIdentifier`，A3）；
    - 带新标识的功能个数。

    EPLAN 重启后会话内标识会变，计划也随之失效，这正是想要的结果。
- **执行**：
  - 先重算指纹，不同就返回 `PLAN_STALE`；
  - 在事务内通过 `Project.ParseNameAndProps` 解析新标识的结构与器件部分，再调用 `NameService.RenameDevice`；提交后按新标识重新查找，不能使用事务内的包装对象或未放置功能 ID（A55/A85）：
    - 主功能的 `Name` 必须等于新标识（EPLAN 可能丢掉这类设备不用的结构部分，A13）；
    - 项目中不能再有带旧标识的功能；
    - 每个功能的新名称与原名称后缀逐个相符，数量相同；并入 PLC 盒时还核对目标原有功能的名称、页、类别和主功能状态不变，返回的 `functions` 只计移动的点；
  - 不满足就抛出并补偿回滚；事务与撤销边界沿用 `TransactionRunner`。
- **包装**：同 `eplan_page_create`。S9 验证过：改名后 `SafetyPoint.Rollback()` 能恢复主功能和全部辅助功能的标识。
- **没有验证过的影响**：改名对已有连接和部件引用的影响。由 Large 测试比较改名前后设备的连接数和部件。

## 6. 写操作专项
- **dry-run**：返回第 3 节的结果，`execution = not_started`。
- **审批**：项目在 scratch 之外时请求审批。表单显示旧标识和新标识；正文另注"共 N 个功能，分布在 M 页"，取自计划。
- **审计、回滚、各阶段失败时的 `execution`**：同 `eplan_page_create`。
- 不是幂等的：同样的调用再来一次，旧标识已经不存在，返回 `DEVICE_NOT_FOUND`。执行请求从不重发（ADR-0010）。

## 7. 测试
- **Small**：
  - 由短标识拼出完整标识；`new_tag` 的校验；
  - 改名范围的计算：同名、带后缀、`SubFunctions` 不在范围内；
  - 指纹；摘要；schema 快照；注解。
- **Medium**：在假 Bridge 上测试：
  - `dry_run`；`ALREADY_EXISTS`；`AMBIGUOUS_NAME`；`PLAN_STALE`；
  - 读回不一致或仍有旧标识时返回 `rolled_back`。
- **Large**（示例项目的 scratch 副本）：
  - `PXC.2861344` 总览宏后插入 `numbering: "keep"` 的多线宏，非主 PLC 点并入已有盒，读回两页、总览点不变、一次撤销；`PlcModuleMergeTests` 待实测。
  - 把 `=F+A2-KF1`（辅助功能在另一页，S9）改成 `-KF19`：`eplan_device_get` 读回后，主功能和两个辅助功能都是新标识，连接数和部件与改名前相同；
  - 改成已有的标识，返回 `ALREADY_EXISTS`；
  - 用 `-` 开头的短标识，结构保持不变；
  - 总览宏 `PXC.2861344` 变体 0 在 `(0,292)`、`macro_box:false` 插入后给 PLC 盒改名，盒及全部点保留后缀，一次撤销恢复；新增用例待实测。
- **Agent 评测**："把 scratch 项目中的 `=F+A2-KF1` 改名为 `=F+A2-KF19`"，作答后用 `eplan_device_get` 核对辅助功能也跟着改了名。

### 部件赋值（FP3）
- 例如 `{"project":"Line1","tag":"=GAA+A1-SF91","part":"SIE.3SB3400-0B","variant":"1"}`；`new_tag` 可同时提供。
- 规划按 `FUNC_IDENTDEVICETAG` 查找唯一主功能（已放置或未放置），数量不为 1 时返回 `INVALID_ARGUMENT` 并报告数量；只赋值部件时允许功能名称带连接点后缀。已引用相同部件和变体时拒绝 `nothing to change`。
- 变更清单增加 `add part <part> (variant <v>) to <tag>`；指纹包含主功能身份、现有部件引用和所选库部件与数量，执行前重新核对。
- 同一事务内先执行请求的改名，再以 `Function.AddArticleReference(part, variant, count)` 追加引用（A82）。提交后按设备标识重新获取主功能及 `ArticleReferences`（A79）；缺少新引用或数量不符时回滚。原有引用保持不变。
- `{"tag":...,"update_from_parts":true}` 单独调用，放在最后一个部件之后：用 `DeviceService.UpdateDevice` 把已引用部件的功能定义、连接点数据和技术参数写到功能上，和 EPLAN 的设备更新相同（A93）。必须单独调用，不能和 `part`、`new_tag` 同时给；功能没有部件引用时拒绝。一步撤销还原这些数据。
- `device.part`、`device.variant` 返回本次赋值的部件和变体；只改名时为 null。只赋值部件时，`tag` 与 `previousTag` 都返回主功能的实际完整名称（可能带 `:1` 等连接点后缀），主功能可直接交给 `eplan_device_get`；端子用 `eplan_terminal_strip_get` 定位；`functions=1`，`pages` 只含目标功能所在页。
- 主线程目标为 2 秒，端到端默认时限 60 秒；部件库较大时可能超过目标，支持既有进度与取消。
- Small 覆盖参数、默认变体、唯一主功能、重复引用、指纹和提交后读回；Medium 覆盖假 Bridge 的 part-only、同时改名、dry-run 与失败。Large 在 `FunctionPlaceTests` 放置 `IEC_symbol/SSD/0`、赋值 `SIE.3SB3400-0B/1` 并读回；仅编写，交由维护者运行。

### 非主端子的部件
- 仅 `part` 赋值且不改名时，`tag` 可为恰好一个已有 `FunctionCategory.Terminal` 的完整名称（如 `+A1-XD5:1`）；不按端子排名、前缀或短标识猜选。其他非主功能、重名端子与同时改名均拒绝。
- 端子已由之前的放置事务提交；本次沿用 `Function.AddArticleReference(part, variant, count)`，提交后按原完整名称重新读取身份、类别、主功能标志及 `ArticleReferences`，保留已有引用，一个撤销步骤恢复。A87 的新建连接定义点限制不扩展为本次新建对象。
- 新增 Small/Medium 覆盖精确端子、歧义、其他类别、改名拒绝与默认预览；Large `ExistingNonMainTerminal_AssignsPart_ReadsArticleReferences_OneUndoRestores` 在 LargeWrite 放置 `IEC_symbol/X2_2/0` 后追加 `PXC.3211813`，独立读回并撤销，待实测。

- `count` 省略为 1；只追加指定数量的一条引用，已有引用不变。`BusbarMain_SecondPartWithCountTwo_ReadsQuantity_OneUndoRestores` 在 LargeWrite 的 BBDEF 主功能已有 `RIT.3581000` 后追加 `RIT.9340030` 数量 2，读回并比较一步撤销快照，NOT RUN。

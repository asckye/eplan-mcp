# 工具规格：`eplan_device_renumber`

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_device_renumber` |
| 所属分组 / Profile | `device` / `core` |
| 安全级别 | Destructive |
| MCP 注解 | `readOnlyHint` = false　`destructiveHint` = true　`idempotentHint` = false　`openWorldHint` = false |
| 状态 | Proposed（M4 第 12 步；已实现，待实机验证） |

## 1. 这个工具解决什么任务
"把这一页上带 ? 的设备标识编号"。设备标识中的 `?` 表示还没有编号，例如插入宏后没有线圈的触点 `-?QA1`（A14）、`+A1-?XG1`（S12）。代理用它让 EPLAN 按编号方案给指定页或整个项目的设备编号：默认只给带 `?` 的标识编号（`PostNumerate`），也可以给范围内的全部设备重新编号。结果由 EPLAN 的规则决定，事先算不出（M4 设计 Q7），所以变更清单只列范围，执行后列出每个改变的标识。大范围改名难以逐项撤销，按 Destructive 处理：每次审批，执行前备份，按一次 Ctrl+Z 撤销。

## 2. 输入参数
| 参数 | 类型 | 必填 | 默认值 | 说明（写给模型看的） |
|---|---|---|---|---|
| `project` | string | 是 | — | 项目名，或 `eplan_project_list` 返回的 `path`。写工具必须显式给出 |
| `pages` | array of string，1–50 项 | 否 | 整个项目 | 只给这些页上的设备编号，完整页名如 `=GAA+A1&EFS1/60` |
| `only_unnumbered` | boolean | 否 | true | true：只给标识中带 `?` 的设备编号；false：范围内的全部设备重新编号 |
| `scheme` | string | 否 | EPLAN 的默认方案 | 编号方案的名称，如 `Standard` |
| `start`、`step` | integer，≥ 1 | 否 | 1、1 | 每个标识字母的起始值和步长 |
| `dry_run` | boolean | 否 | false | 为 true 时只返回编号的范围，不请求审批，不备份，也不修改项目 |

- 页名只做精确匹配，同 `eplan_page_delete`。同一页出现两次时返回 `INVALID_ARGUMENT`。
- 只给设备标识编号。端子连接点名称中的 `?`（如 `:?1`）不由本工具处理（S12）。

## 3. 输出
```json
{
  "project": "Line1",
  "execution": "committed",
  "dryRun": false,
  "scope": { "pageCount": 1, "onlyUnnumbered": true, "scheme": null, "start": 1, "step": 1, "devices": 5 },
  "changed": [ { "from": "+A1-?WG1", "to": "+A1-WG1" }, { "from": "+A1-?XG1", "to": "+A1-XG2" } ],
  "changedCount": 4,
  "stillUnnumbered": ["=GAA+A1-?QA1"],
  "backup": "C:\\Users\\<user>\\EplanMcp\\backups\\Line1\\20261021T083000123Z\\Line1.elk",
  "undo": "AI: eplan_device_renumber",
  "recovered": false,
  "truncated": false,
  "hint": "1 device tag still contains '?' after numbering; check it in EPLAN."
}
```
- `scope`：编号的范围。`pageCount` 为 `null` 表示整个项目；`scheme` 为 `null` 表示 EPLAN 的默认方案；`devices` 是规划时范围内要编号的设备数（`only_unnumbered` 时是带 `?` 的设备数）。
- `changed`：执行前后完整标识改变的设备，按设备去重（辅助功能跟随主功能，不单独列出），最多 200 条，`changedCount` 是总数（M4 设计 4.2）；超出时 `truncated = true`。
- `stillUnnumbered`：执行后范围内标识仍带 `?` 的设备，最多 50 个。S12 中 `PostNumerate` 没有给 `=GAA+A1-?QA1` 编号，原因未查，所以不能假定全部编号完（A18）；有这类设备时 `hint` 提示工程师在 EPLAN 中处理。
- `dry_run`：只有 `scope`，`changed`、`stillUnnumbered` 为 `null`。
- 范围内没有带 `?` 的设备（`only_unnumbered` 时）：变更清单为空，返回 `not_started`，不审批、不备份，`hint` 说明没有可编号的设备。
- `backup`：同 `eplan_page_delete`。标识是项目数据，经过 ResultShaper。`undo`、`recovered`：同 `eplan_page_create`。
- 一行摘要：
  - `Numbered 4 device tags; 1 still contains '?'. A backup was made first (see 'backup'); Ctrl+Z in EPLAN undoes this step.`
  - `Dry run: would number the 5 devices with '?' on 1 page; the new tags are known only after running. Nothing was changed.`

## 4. 错误
| 错误码 | 什么时候发生 | `execution` | 返回给模型的提示 |
|---|---|---|---|
| `INVALID_ARGUMENT` | `pages` 为空数组、超过 50 项或有重复；`start`、`step` 小于 1 | `not_started` | `metadata.violations` 列出全部问题 |
| `INVALID_ARGUMENT` | `scheme` 不存在：EPLAN 在执行时抛出 `InvalidScheme`（第 5 节） | `rolled_back` | `metadata.candidates` 列出可用的方案（最多 20 个）；提示省略 `scheme`。备份照样保留 |
| `PAGE_NOT_FOUND` | 有页名找不到 | `not_started` | 同 `eplan_page_delete` |
| `PLAN_STALE` | 规划之后，范围内的功能发生了变化 | `not_started` | 提示重新调用 |
| `BACKUP_FAILED` | 同 `eplan_page_delete` | `not_started` | 同 `eplan_page_delete` |
| `EPLAN_OPERATION_FAILED` | EPLAN 编号时报错，包括找不到可编号的功能（XML） | `rolled_back` | 附带 EPLAN 的系统消息；找不到可编号的功能时，提示剩下的 `?` 要由工程师在 EPLAN 中处理 |
| 策略、审批、流水线、锁定、项目解析、连接与授权类错误 | 同 `eplan_page_delete` | 同 `eplan_page_delete` | 同 `eplan_page_delete` |

## 5. 实现要点
- **Host**：走 `WritePipeline`，级别 Destructive，规划操作是 `device.renumber.plan`，执行操作是 `device.renumber.execute`。执行时限 300 秒，包括备份；等待 Bridge 时每 5 秒发一次心跳进度（M4 设计 4.3）。
- **范围**：`pages` 上的全部功能（`DMObjectsFinder.GetFunctions` 按页过滤），或项目的全部功能。"带 `?`"指完整标识中 `:` 之前的部分含 `?`。
- **规划**（只读）：
  - 精确解析各页，读出范围内的功能和其中的设备；
  - 一条 `update` 变更：`Kind = "devices"`，`Property = "device tags"`，`NewValue` 为 `null`。表单显示为 `renumber device tags containing '?' on 1 page (5 devices), scheme: EPLAN default, start 1, step 1`（`only_unnumbered` 为 false 时为 `renumber all device tags on …`）；
  - 要编号的设备标识放在 `details["devices"]` 中，每行一个，最多 50 行，总数在 `details["deviceCount"]`，范围和参数在 `pageCount`、`onlyUnnumbered`、`scheme`、`start`、`step` 中；审批对话框在变更清单之后列出这些设备；
  - 指纹：范围内每个功能的 `Name` 和会话内标识，排序后取 SHA-256。
- **执行**：Destructive 流水线先重算指纹、做好备份，然后在 `TransactionScope` 中：
  1. 记下范围内每个功能的会话内标识和 `Name`；
  2. `new Renumber().DeviceTags(functions, null, 标志, scheme ?? "", start, step)`：第二个参数是过滤方案，传 `null` 表示不过滤（XML）；`only_unnumbered` 时标志为 `Renumber.Enums.DeviceTags.PostNumerate`，否则不带标志；第 4 个参数是编号方案（A18：异常中的参数名为 `strSchemePerIdentifier`；XML 中 `Project` 重载的参数名与类型顺序不一致，不要照着它传参）；
  3. 读回：按会话内标识对应同一个功能，比较 `Name` 得出 `changed`，再找出仍带 `?` 的设备；
  4. `Commit()`，再 `CloseUndo()`。EPLAN 什么都没有改时仍然提交，`changedCount` 为 0，`hint` 说明。
- **方案**：S12：传 `""` 与 `Standard` 结果相同（是总用 `Standard` 还是用界面上次使用的方案，没有区分）；名称不存在时抛出 `Eplan.EplApi.HEServices.Exceptions.InvalidScheme`，消息中用单引号列出可用方案，以 `, ` 分隔（消息随界面语言变化，S12 中是中文）。Bridge 只取最后一对单引号之间的内容作为候选，取不出时不给候选。编号方案的设置节点没有查到，所以名称要到执行时才能校验，那时已经备份；实现时若查到节点，改为在规划阶段校验（`not_started`，不必先备份）。
- **已验证**（S12，A18）：在调度器中给一页上的 12 个功能编号 0.1–0.2 秒：`+A1-?WG1` → `+A1-WG1`，`+A1-?XG1`/`?XG2`/`?XG3` → `XG2`/`XG3`/`XG4`（跳过已占用的 `XG1`）；端子连接点 `:?1`、`:?2` 不变；`=GAA+A1-?QA1` 不变；能被 `SafetyPoint` 回滚，提交后 Ctrl+Z 一步撤销；不弹对话框。
- **没有验证**，由 Large 测试确认：
  - 不带标志（`only_unnumbered = false`）时是否给范围内全部设备重新编号；不是时这一版去掉这个参数，只做 `PostNumerate`；
  - 主功能在范围内、辅助功能在其他页上时，辅助功能是否跟着改名；
  - 会话内标识在编号后是否保持（S12 只比较了名称列表）；
  - 整个项目的耗时；`start`、`step` 不为 1 时的效果。

## 6. 写操作专项
- **Destructive 流水线、审计、回滚、各阶段失败时的 `execution`**：同 `eplan_page_delete` 第 6 节。
- **dry-run**：返回第 3 节的结果，`execution = not_started`，不备份。
- **审批**：每次都请求审批，scratch 中也一样。对话框显示变更清单（第 5 节）、要编号的设备，注明执行前会先备份，以及可以按 Ctrl+Z 撤销"AI: eplan_device_renumber"。
- 不标为幂等：EPLAN 的编号结果取决于项目中已占用的标识。执行请求从不重发（ADR-0010）。

## 7. 测试
- **Small**：带 `?` 的判断（`:` 之后的 `?` 不算）；按设备去重的 `changed`；从 S12 的 `InvalidScheme` 消息中取出 5 个候选，消息格式不符时不给候选；变更清单与 `details` 的文字；摘要；schema 快照；注解。
- **Medium**：在假 Bridge 上测试：`dry_run`；没有带 `?` 的设备时 `not_started` 且不审批；`InvalidScheme` 时 `rolled_back` 并带候选；`PLAN_STALE`；备份失败时不执行。
- **Large**（示例项目的 scratch 副本，审批方式同 `eplan_page_delete`）：
  - 用 `eplan_device_update` 把 `=F+A2-KF1`（辅助功能在另一页，S9）改名为 `=F+A2-?KF1`，再对它所在的页编号：得到不带 `?` 的标识，另一页上的辅助功能跟着改名；`backup` 能打开；
  - 用 `eplan_macro_insert`（`number`）在新页上插入接触器反馈控制回路，留下 `=GAA+A1-?QA1`（A14），再编号：记录 EPLAN 的行为（不变还是报错），`stillUnnumbered` 或错误提示中出现它；
  - `only_unnumbered = false` 对一页编号；方案 `Default`：`INVALID_ARGUMENT`，候选与 S12 相同。
- **Agent 评测**："把 scratch 项目 `=GAA+A1&EFS1/60` 页上带 ? 的设备标识编号"（M4 设计第 7 节），审批方式同 `eplan_page_delete`。S12 表明宏留下的 `-?QA1` 可能编不上号，评测的判定以结果中的 `stillUnnumbered` 和工程师在 EPLAN 中的核对为准。

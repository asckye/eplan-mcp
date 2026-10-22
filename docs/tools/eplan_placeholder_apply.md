# 工具规格：`eplan_placeholder_apply`

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_placeholder_apply` |
| 所属分组 / Profile | `placeholder` / `full`（新工具默认只在 `full` 中） |
| 安全级别 | Write |
| MCP 注解 | `readOnlyHint` = false　`destructiveHint` = false　`idempotentHint` = true　`openWorldHint` = false |
| 状态 | Proposed（API 能力覆盖第一期的修改样例，[设计](../design/2026-10-21-api-capability-coverage.md)第 8 节）；已实现，**提交后的持久性和 Ctrl+Z 待实机验证**（第 5 节） |

## 1. 这个工具解决什么任务
"把这页 24 V 电源宏的功能文本切换成'PLC I/O's'那一组""这个占位符改用 mA 的值集"。代理给页上的一个占位符应用一个值集：EPLAN 把值集中各变量的值写进占位符引用的对象（EPLAN 界面中的"分配值集"）。常见的顺序是 `eplan_macro_insert` 插入带占位符的宏 → `eplan_placeholder_list` 看值集 → 本工具切换。

不做的事：新建或修改值集（`AddRecord`、`SetValue`，S29 验证过但不在本工具中）、新建占位符、把值集写回宏文件。

## 2. 输入参数
| 参数 | 类型 | 必填 | 默认值 | 说明（写给模型看的） |
|---|---|---|---|---|
| `project` | string | 是 | — | 项目名，或 `eplan_project_list` 返回的 `path`（写工具必须显式给出） |
| `page` | string | 是 | — | 占位符所在的页，完整页名，查找规则同 `eplan_page_get` |
| `value_set` | string | 是 | — | 要应用的值集，`eplan_placeholder_list` 的 `valueSets` 中的名称，区分大小写 |
| `placeholder` | string | 否 | 不限名称 | 占位符的名称，`eplan_placeholder_list` 的 `name`；没有名称的占位符传空串 |
| `dry_run` | boolean | 否 | false | 只返回计划 |

- 目标：这一页上名称相符（给了 `placeholder` 时）、并且有这个值集的占位符。恰好一个时执行；没有时返回 `INVALID_ARGUMENT`，列出这一页上的占位符和各自的值集；多于一个时返回 `AMBIGUOUS_NAME`。本版本分不开同页同名、值集也相同的占位符（同一个宏在一页上插入两次时会这样），提示在 EPLAN 中操作。

## 3. 输出
```json
{
  "project": "Line1",
  "execution": "committed",
  "dryRun": false,
  "placeholder": { "name": "", "page": "=TM1+A1&EFS1/1" },
  "valueSet": "mA",
  "values": [ { "variable": "T1", "text": "…" }, { "variable": "T2", "text": "…" } ],
  "currentValuesBefore": [ { "text": "…" }, { "text": "…" } ],
  "currentValuesAfter": [ { "text": "…" }, { "text": "…" } ],
  "currentValuesChanged": true,
  "undo": "AI: eplan_placeholder_apply",
  "recovered": false,
  "hint": null
}
```
- `values`：值集中各变量的值（EPLAN 界面语言，按其他读工具的回退规则；回退时另有 `textLanguage`）。
- `currentValuesBefore` / `currentValuesAfter`：执行前后占位符的当前值（`GetAllCurrentValues`）；`currentValuesChanged` 说明两者是否不同。dry run 只有 `currentValuesBefore`。
- 一行摘要：`Applied value set 'mA' to the placeholder; its current values changed.`（dry run：`Would apply value set …`）。值集名是项目数据，摘要中只引用它一次；页名和值只在数据字段中。
- 工具描述写明：会请求审批（scratch 之外）；一次 Ctrl+Z 撤销（待实机验证）；`eplan_placeholder_list` 先查值集。

## 4. 错误
| 错误码 | 什么时候发生 | `execution` | 返回给模型的提示 |
|---|---|---|---|
| `INVALID_ARGUMENT` | 参数不合法；页上没有名称相符的占位符；占位符没有这个值集 | `not_started` | `metadata.violations` 列出参数问题；找不到占位符或值集时 `metadata.candidates` 列出页上的占位符（名称和值集，每行一个，最多 20 个），提示用 `eplan_placeholder_list` |
| `INVALID_ARGUMENT` | 执行后读回：占位符不见了，或它的值集、变量与执行前不同 | `rolled_back` | 说明已回滚，`metadata.problem` 说明哪里不同 |
| `AMBIGUOUS_NAME` | 页上有多个符合条件的占位符 | `not_started` | `metadata.candidates` 列出它们；给 `placeholder` 缩小范围，同名同值集时请工程师在 EPLAN 中操作 |
| `PAGE_NOT_FOUND` | 页找不到 | `not_started` | 提示用 `eplan_page_list` |
| `PLAN_STALE` | 规划之后，占位符的值集、变量、值或当前值变了 | `not_started` | 提示重新调用 |
| `EPLAN_OPERATION_FAILED` | `ApplyRecord` 抛出异常 | `rolled_back` | 附带 EPLAN 的系统消息 |
| 策略、审批、流水线、锁定、项目解析、连接与授权类错误 | 同 `eplan_page_create` | 同 `eplan_page_create` | 同 `eplan_page_create` |

## 5. 实现要点
- **Host**：走 `WritePipeline`，规划操作 `placeholder.apply.plan`，执行操作 `placeholder.apply.execute`，Write 级（scratch 中自动允许，真实项目需要审批）；执行时限为默认的 60 秒（S29：应用不到 0.01 秒）。
- **规划**（只读）：按 `eplan_placeholder_list` 的方式读出这一页上的占位符，选出目标；一条变更 `Action = "update"`、`Kind = "placeholder"`、`Name = "<页名> <名称>"`、`Property = "value set"`、`OldValue = null`（EPLAN 不给出"当前是哪个值集"）、`NewValue = 值集名`；`Details`：`values`（`变量 = 值`，每行一个）、`assignedObjects`。审批表单写成"在页 … 上给占位符 … 应用值集 …"，下面列出各变量的值。指纹：项目路径、页名、占位符名称、值集名、变量名、全部值的原始串和当前值的原始串，排序后取 SHA-256。
- **执行**：
  1. 重读并重算指纹，不同就返回 `PLAN_STALE`；
  2. 在 `TransactionScope` 中（`LockingStep → SafetyPoint → UndoStep → QuietModeStep`，编码规范 9.3）调用**生成的重载表** `PlaceHolderApi.ApplyRecord(holder, 值集)`，即 `PlaceHolder.ApplyRecord(string)`；带 `bApplyPageAndProjectData` 的另一个重载也在重载表中生成，但没有验证，本工具不调用它，覆盖报告因此把这个重载组记为部分重载；
  3. 读回：重新找到这个占位符（同页、同名、同值集），它的值集和变量与执行前相同；不同就抛出，整体回滚；
  4. 读出执行后的当前值，与执行前比较得出 `currentValuesChanged`。当前值不变不算失败：值集可能本来就已应用，S29 也没有逐项比较 `Cable_Sensor` 这类改布尔属性的值集；结果如实报告；
  5. 通过后 `Commit()`，再 `CloseUndo()`。
- **读回的强度**（设计第 4.1 节第 3 条，如实说明）：EPLAN 不给出变量与被引用对象属性的对应关系，所以不逐项核对被引用的对象，只核对占位符本身、报告当前值的前后变化。工具描述和结果提示中写明：需要确认的对象请用 `eplan_device_get` 等读工具查看。
- **已验证**（S29，api-notes A33）：插入宏后 `ApplyRecord(名称)` 不到 0.01 秒，`GetAllCurrentValues()` 随之变为该值集的多语言文本；插入和应用都随 `SafetyPoint.Rollback()` 撤销。
- **待实机验证**（Large 测试 `PlaceholderApplyTests`）：提交后应用的结果仍在（持久性）；一次 Ctrl+Z 撤销这次应用；应用在 `UndoStep` 和 `QuietModeStep` 之中照常生效（S29 只在 `SafetyPoint` 中应用过，没有 `UndoStep`）；示例项目中原有的占位符（不是刚插入的宏）同样能应用。

## 6. 写操作专项
- **dry-run**：返回第 3 节的结果（没有 `currentValuesAfter`），`execution = not_started`，不请求审批。
- **审批**：项目在 scratch 之外时请求审批，表单为一行变更加各变量的值，注明一次 Ctrl+Z 可以撤销（待实机验证）。
- **审计**：同 `eplan_page_update`：`execution_started`、`execution_result` 两条记录，参数中有页、占位符和值集。
- **回滚**：`SafetyPoint` 回滚（S29 已验证应用能被回滚）。
- **各阶段失败时的 `execution`**：同 `eplan_page_update`；超时或中断后由 Host 取回结果（ADR-0010）。
- **幂等**：同一个值集再应用一次，结果相同；工具不跳过它（EPLAN 不给出当前的值集），`currentValuesChanged` 为 false。

## 7. 测试
- **Small**：目标的选择（名称、值集、空名称、多个、没有）、指纹、变更清单与审批文字、摘要；schema 快照；注解。
- **Medium**：假 Bridge 上的 `dry_run`、`PLAN_STALE`、读回不一致时 `rolled_back`、`ApplyRecord` 抛异常时 `rolled_back`。
- **Large**（`PlaceholderApplyTests`，写副本 `writes\LargeWrite`）：
  - `=TM1+A1&EFS1/1` 上没有名称的占位符：dry run 为 `not_started`，`values` 有 `T1`、`T2` 两项，当前值不变；
  - 应用 `mA`：`committed`，`undo` 为 `AI: eplan_placeholder_apply`；之后 `eplan_placeholder_list`（`detailed`）读出的当前值与 `mA` 的 `values` 文本一致（两项，顺序可以不同）；
  - 再应用 `V`：当前值与 `V` 的 `values` 一致；`currentValuesChanged` 两次都为 true（两组值不同时）；
  - 不存在的值集 `X`：`INVALID_ARGUMENT`，候选中有 `V`、`mA`，不审批；`placeholder: "nope"`：`INVALID_ARGUMENT`；
  - scratch 之外的副本：`POLICY_DENIED`、`not_started`；
  - 前后值、耗时写入测试输出。**一次 Ctrl+Z 撤销要在只开着写副本时手工核对**（Large 测试 README"写测试"第 3 步）：撤销后 `eplan_placeholder_list` 的当前值回到应用前。
- **Agent 评测**："把 scratch 项目 `=TM1+A1&EFS1/1` 页上占位符的值集换成 mA"，作答后用 `eplan_placeholder_list` 核对当前值。

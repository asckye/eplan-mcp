# 工具规格：`eplan_macro_insert`

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_macro_insert` |
| 所属分组 / Profile | `macro` / `core` |
| 安全级别 | Write |
| MCP 注解 | `readOnlyHint` = false　`destructiveHint` = false　`idempotentHint` = false　`openWorldHint` = false |
| 状态 | Proposed（M3b 第 4 步；实现依赖 M3a 第 10 步的 `TransactionScope`） |

## 1. 这个工具解决什么任务
"在 `=GAA+A1&EFS1/60` 页左上方放一个接触器反馈控制回路"。代理先用 `eplan_macro_list` 和 `eplan_macro_get` 选好宏，再用它把宏放到页上的指定位置，得到带标识、在图上显示的设备。这是 M3b 放置设备的唯一方式（M3b 设计 Q1）：一次调用插一个宏，只审批一次，按一次 Ctrl+Z 撤销。

## 2. 输入参数
| 参数 | 类型 | 必填 | 默认值 | 说明（写给模型看的） |
|---|---|---|---|---|
| `project` | string | 是 | — | 项目名，或 `eplan_project_list` 返回的 `path`。写工具必须显式给出 |
| `page` | string | 是 | — | 完整页名，如 `=GAA+A1&EFS1/60`，必须是电路图页（`Circuit`） |
| `macro` | string | 是 | — | `eplan_macro_list` 返回的 `path` |
| `x`、`y` | number | 是 | — | 插入点，单位毫米，原点在页的左下角，y 向上。宏外框的左上角落在这里（A14） |
| `variant` | integer | 否 | 0 | 宏的变体编号（`eplan_macro_get` 的 `variants`） |
| `numbering` | enum `number` / `keep` | 否 | `number` | `number`：EPLAN 给宏中的设备分配不冲突的标识；`keep`：保留宏中的标识，结构跟随目标页，与已有设备同名时拒绝（M3b 设计 Q2） |
| `dry_run` | boolean | 否 | false | 为 true 时只返回将要进行的插入，不请求审批，也不修改项目 |

- **与 M3b 设计 4.2 的差别**：这一版不提供 `representation` 参数。只往电路图页上插 `MultiLine` 表达类型，宏没有 `MultiLine` 时返回 `INVALID_ARGUMENT`。其他页类型和表达类型以后再加，需要先实机验证。

## 3. 输出
```json
{
  "project": "Line1",
  "execution": "committed",
  "dryRun": false,
  "page": "=GAA+A1&EFS1/60",
  "macro": "EPLAN_Macro/203_Electrical_Engine/001_Option_1/Contactor_Feedback_Control.ema",
  "devices": [
    { "tag": "=GAA+A1-?QA1", "visibleTag": "-?QA1", "category": "NoContact" },
    { "tag": "+A1-WG7", "visibleTag": "-WG7", "category": "Cable" }
  ],
  "objectCount": 28,
  "undo": "AI: eplan_macro_insert",
  "recovered": false,
  "hint": "Tags with '?' could not be numbered (a contact without its coil, a terminal without a number): check them in EPLAN."
}
```
- `devices`：
  - 提交后，是插入后读回的设备（除连接线外的功能，按标识去重，最多 50 个）；
  - `dry_run` 时，是宏中的设备，带宏自己的占位标识，`hint` 说明插入后标识会怎样变化。
- 带 `?` 的标识（S10：没有主功能的触点 `-?QA1`、未编号的端子 `:?1`）原样列出，`hint` 提示工程师在 EPLAN 中确认。
- 插入后外框超出页面，或者页的幅面读不到（新建的空页，A14）时，`hint` 也会说明。
- `undo`、`recovered`：同 `eplan_page_create`。
- 标识按项目数据处理，经过 ResultShaper。
- 一行摘要：
  - `Inserted a macro with 8 devices on a page of 'Line1'; Ctrl+Z in EPLAN undoes this step.`
  - `Dry run: would insert a macro with 8 devices. Nothing was changed.`

## 4. 错误
| 错误码 | 什么时候发生 | `execution` | 返回给模型的提示 |
|---|---|---|---|
| `INVALID_ARGUMENT` | `macro` 路径不合规（SEC-19）；页不是电路图页；宏没有 `MultiLine`；`variant` 不存在；插入点或插入后的外框超出页面（幅面已知时） | `not_started` | `metadata.violations` 一次列出全部问题；超出页面时给出宏的尺寸和页的幅面 |
| `INVALID_ARGUMENT` | 插入位置上已有同样的符号，EPLAN 拒绝叠放（"S029181"，A13） | `rolled_back` | 提示换一个位置，可以用 `eplan_page_get` 查看页上已有的设备 |
| `ALREADY_EXISTS` | `numbering = keep`，宏中的标识与已有设备同名，插入后出现了同名的主功能（S10） | `rolled_back` | `metadata.tags` 列出冲突的标识，提示改用 `number`，或先用 `eplan_device_update` 给已有设备改名 |
| `PAGE_NOT_FOUND` / `AMBIGUOUS_NAME` | 页找不到，或只差大小写的页有多个 | `not_started` | 同 `eplan_page_get` |
| `MACRO_NOT_FOUND` | 宏目录中没有这个宏 | `not_started` | 同 `eplan_macro_get` |
| `PLAN_STALE` | 规划之后，页上的对象或宏文件发生了变化 | `not_started` | 提示重新调用 |
| 策略、审批、流水线、锁定、项目解析、连接与授权类错误 | 同 `eplan_page_create` | 同 `eplan_page_create` | 同 `eplan_page_create` |

## 5. 实现要点
- **Host**：走 `WritePipeline`，规划操作是 `macro.insert.plan`，执行操作是 `macro.insert.execute`。执行时限为默认的 60 秒。
- **规划**（只读）：
  - 解析项目、页（规则同 `eplan_page_get`）和宏路径（`MacroFiles.Resolve`，SEC-19）；
  - 打开宏（不插入，A14），确认有 `MultiLine` 和要求的 `variant`，读出外框尺寸和其中的设备；
  - 读页的幅面（`Page.Size`）。读得到时，检查插入后的外框 `[x, x + 宽] × [y − 高, y]` 在页内；读不到时只检查 `x ≥ 0`、`y ≥ 0`，并在结果中提示；
  - 一条 `create` 变更：`Kind = "macro"`，`Name` 为页名，`NewValue` 为宏文件名、插入点、编号方式和设备数。表单显示为 `insert macro Contactor_Feedback_Control.ema at (40, 200) on page =GAA+A1&EFS1/60, numbering number, 8 devices`；
  - 指纹包括三部分，排序后取 SHA-256：页上全部对象的会话内标识；`keep` 时，宏中各标识在项目中的功能；宏文件的大小和修改时间。
- **执行**：在 `TransactionScope`（`SafetyPoint` → `UndoStep` → `QuietModeStep`）中执行：
  1. 先记下项目中主功能的标识集合，然后调用 `Insert.WindowMacro(macro, MultiLine, variant, page, new PointD(x, y), MoveKind.Absolute, numbering == number ? NumerationMode.Number : NumerationMode.None)`；
  2. 读回页上新增的功能。`keep` 时，若有标识在插入前已有主功能、插入后有两个以上主功能，就抛出（`ALREADY_EXISTS`），整体回滚；
  3. 通过后 `Commit()`，再 `CloseUndo()`。
- **已验证**（S9、S10，api-notes A13、A14）：
  - 插入宏能被 `SafetyPoint` 回滚，`Transaction` 不行，所以只用 `SafetyPoint`；
  - 在调度器中，插入宏占一个撤销步骤，Ctrl+Z 能撤销；
  - `Number` 不产生重复设备，`None` 会；
  - 同一位置再次插入同一个宏会被 EPLAN 拒绝。
- **没有验证**：
  - 符号宏和其他表达类型；
  - `bDontResolveGroups`；
  - 插入点与外框的关系只看了一张图（A14），由 Large 测试导出图片复核。

## 6. 写操作专项
- **dry-run**：返回第 3 节的结果，`execution = not_started`，`devices` 为宏中的设备。
- **审批**：项目在 scratch 之外时请求审批，表单为一行变更（见第 5 节），注明可以按 Ctrl+Z 撤销"AI: eplan_macro_insert"。
- **审计、回滚、各阶段失败时的 `execution`**：同 `eplan_page_create`。
- 不是幂等的：同样的调用再来一次，会在同一位置叠放而被拒绝（`rolled_back`），或者（位置不同时）再插入一份。执行请求从不重发（ADR-0010）。

## 7. 测试
- **Small**：
  - 外框与页幅面的检查；
  - `keep` 时合并的判断（插入前后的主功能计数）；
  - 变更清单的文字；摘要；schema 快照；注解。
- **Medium**：在假 Bridge 上测试：
  - `dry_run`；`ALREADY_EXISTS`（`rolled_back`）；叠放（`rolled_back`）；`PLAN_STALE`；
  - 页不是电路图页、宏没有 `MultiLine`、`variant` 不存在。
- **Large**（示例项目的 scratch 副本）：
  - 新建页上插入 `Contactor_Feedback_Control.ema`，用 `number`：读回的设备与 S10 相同；导出页面图片，宏外框的左上角在插入点；
  - 在同一项目中用 `keep` 再插一次，返回 `ALREADY_EXISTS`，项目不变；
  - 同一位置再插一次，返回 `INVALID_ARGUMENT`（`rolled_back`）；
  - scratch 之外的副本：`POLICY_DENIED`。
- **Agent 评测**："在 scratch 项目的新页 `=GAA+A1&EFS1/60` 上放一个接触器反馈控制回路"，作答后用 `eplan_page_get` 核对页上的设备。

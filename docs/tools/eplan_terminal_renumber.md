# 工具规格：`eplan_terminal_renumber`

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_terminal_renumber` |
| 所属分组 / Profile | `terminal` / `full`（M4c 的新工具默认只在 `full` 中） |
| 安全级别 | Destructive |
| MCP 注解 | `readOnlyHint` = false　`destructiveHint` = true　`idempotentHint` = false　`openWorldHint` = false |
| 状态 | Proposed（M4c 第二批；已实现，待实机验证） |

## 1. 这个工具解决什么任务
"把 -XD1 的端子按顺序重新编号""给新加的端子编号"。代理让 EPLAN 按端子编号方案给指定端子排或整个项目的端子编号。结果由方案和端子排中的顺序决定，事先算不出，所以变更清单只列范围，执行后列出每个改变的端子（与 `eplan_device_renumber` 相同，M4 设计 Q7）。大范围改名，按 Destructive 处理：每次审批，执行前备份。

## 2. 输入参数
| 参数 | 类型 | 必填 | 默认值 | 说明（写给模型看的） |
|---|---|---|---|---|
| `project` | string | 是 | — | 项目名，或 `eplan_project_list` 返回的 `path`。写工具必须显式给出 |
| `strips` | array of string，1–50 项 | 否 | 整个项目 | 只给这些端子排编号，`eplan_terminal_strip_list` 返回的 `name` |
| `scheme` | string | 是 | — | 端子编号方案的名称，如 `Numerisch`；不存在时错误中列出可用的方案 |
| `only_unnumbered` | boolean | 否 | true | true：只给名称中带 `?` 的端子编号；false：范围内全部端子重新编号 |
| `start`、`step` | integer，≥ 1 | 否 | 1、1 | 起始值和步长 |
| `dry_run` | boolean | 否 | false | 为 true 时只返回编号的范围，不请求审批，不备份，也不修改项目 |

- `scheme` 必填：方案传空串时 EPLAN 用界面上次使用的方案（A24），结果会随界面状态变化。
- 同一端子排出现两次时返回 `INVALID_ARGUMENT`。

## 3. 输出
同 `eplan_device_renumber`，把"设备"换成"端子"：`scope`（`stripCount` 代替 `pageCount`，`terminals` 代替 `devices`）、`changed`（`{ "from": "+A1-XD1:2", "to": "+A1-XD1:3" }`，最多 200 条）、`changedCount`、`stillUnnumbered`、`backup`、`undo`、`recovered`、`truncated`、`hint`。
- 一行摘要：`Numbered 4 terminals. A backup was made first (see 'backup'); Ctrl+Z in EPLAN undoes this step.`
- 工具描述写明：整个项目可能要几十秒（S21：1331 个端子 31–43 秒）。

## 4. 错误
同 `eplan_device_renumber`，另外：
| 错误码 | 什么时候发生 | `execution` | 返回给模型的提示 |
|---|---|---|---|
| `INVALID_ARGUMENT` | `scheme` 不在 `USER.Terminal.Numbering.Scheme` 中 | `not_started` | `metadata.candidates` 列出可用的方案 |
| `DEVICE_NOT_FOUND` | 有端子排找不到 | `not_started` | 提示用 `eplan_terminal_strip_list` |
| `EPLAN_OPERATION_FAILED` | 读回时发现 EPLAN 还改了不该编号的端子：`only_unnumbered` 时不带 `?` 的，或者给定端子排之外的 | `rolled_back` | `metadata.unplanned` 列出多改的端子（`前 -> 后`，最多 50 行），`unplannedCount` 为总数；请工程师在 EPLAN 中编号，或只给要重排的端子排并设 `only_unnumbered: false` |

## 5. 实现要点
- **Host**：走 `WritePipeline`，级别 Destructive，规划操作 `terminal.renumber.plan`，执行操作 `terminal.renumber.execute`。时限 300 秒，含备份；每 5 秒一次心跳。
- **方案**：规划时用 `SchemeSetting` 在用户设置 `USER.Terminal.Numbering.Scheme` 中校验，候选不含内部的 `InternalRefDefScheme`（A24）。这比 `eplan_device_renumber` 好：名称在备份之前就能校验。
- **范围**：`strips` 中各端子排的 `Terminals`，或 `GetTerminals(null)` 的全部端子；"带 `?`"指端子名中 `:` 之后的部分含 `?`。
- **执行**：Destructive 流水线先备份，然后在 `TransactionScope` 中：
  1. 记下项目中每个端子的会话内标识和名称（只给整个项目时就是范围）；
  2. `new Renumber().Terminals(terminals, TerminalsSequence.Level, Extent.AllSelectedTerminalStrips, TerminalsPotential.Numbering ×3, MultipleTerminals.NumberIndividually, TerminalsWithAlphabeticalCharacters.DontModify, scheme, start, step, "", "", false, false, only_unnumbered, false)`（S21 的参数，最后四个依次为允许改变排序、填补空号、只编带 `?` 的、连带编号 PLC 控制的端子）。🔴 `only_unnumbered` 为 true 时，`terminals` **只含范围内名称中 `:` 之后带 `?` 的端子**：那个参数挡不住已编号的端子，给整个项目的全部端子时，EPLAN 照样按方案重排了已编号的 `+A1-XD1`（Large 测试，2026-10-21，A24）。做法同 `eplan_device_renumber` 只把带 `?` 的设备标识交给 `PostNumerate`（A20）；没有这样的端子时规划没有变更，`not_started`；
  3. 读回项目中的全部端子，按会话内标识比较：只允许规划时数到的端子改名（`only_unnumbered` 时是带 `?` 的，否则是范围内的全部）。其他端子改了名就是读回不一致，整体回滚，返回 `EPLAN_OPERATION_FAILED`（第 4 节）；一致时提交，同 `eplan_device_renumber`。
- **已验证**（S21，A24）：全部 1331 个端子 31–43 秒，4 个端子换了号；能被 `SafetyPoint` 回滚，提交后 Ctrl+Z 一步撤销。插宏后对整个项目做 `only_unnumbered`，7 个 `:?n` 都编成了 `:n`，`stillUnnumbered` 为空，EPLAN 没有崩溃，约 35–43 秒（Large 测试，2026-10-21）。
- **待 Large 测试确认**：
  - 只交带 `?` 的端子（仍用 `AllSelectedTerminalStrips`）之后，端子排中已编号的端子是否保持不变。不变的话，读回检查不会触发；变了的话，读回检查会回滚。`Extent.Selected`（只编给出的端子，XML）没有用：它会不会和端子排中已有的号重复，还没有验证；
  - 只给部分端子排时，`Extent.AllSelectedTerminalStrips` 是否只改这些端子排；
  - `start`、`step` 不为 1 时的效果；
  - EPLAN 没有改任何端子名时是否留下撤销步骤：工具按 `eplan_device_renumber` 的做法（A20）返回 `not_started`、`undo` 为 `null`。

## 6. 写操作专项
同 `eplan_device_renumber` 第 6 节（Destructive 流水线、dry-run、每次审批、不标为幂等），撤销名为"AI: eplan_terminal_renumber"。

## 7. 测试
- **Small**：带 `?` 的判断（`:` 之后）、方案候选、变更清单文字、摘要、schema 快照、注解。
- **Medium**：假 Bridge 上的 `dry_run`、没有可编号的端子时 `not_started`、方案不存在时 `not_started` 并带候选、`PLAN_STALE`、备份失败时不执行。
- **Large**（示例项目的 scratch 副本）：`strips = ["+A1-XD1"]`、`only_unnumbered = false`、`scheme = "Numerisch"`：`+A1-XD1` 的端子按顺序重排（S21 中 4 个换号），其他端子排不变；`backup` 能打开；Ctrl+Z 一步撤销。
- **Agent 评测**："把 +A1-XD1 的端子按顺序重新编号"。

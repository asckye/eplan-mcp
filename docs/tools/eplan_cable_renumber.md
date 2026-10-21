# 工具规格：`eplan_cable_renumber`

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_cable_renumber` |
| 所属分组 / Profile | `cable` / `full`（M4c 的新工具默认只在 `full` 中） |
| 安全级别 | Destructive |
| MCP 注解 | `readOnlyHint` = false　`destructiveHint` = true　`idempotentHint` = false　`openWorldHint` = false |
| 状态 | Proposed（M4c 第二批；已实现，待实机验证） |

## 1. 这个工具解决什么任务
"把项目里的电缆重新编号""给新电缆编号"。代理让 EPLAN 按电缆编号方案给指定电缆或全部电缆编号。结果事先算不出，变更清单只列范围，执行后列出每个改变的电缆标识（同 `eplan_device_renumber`）。按 Destructive 处理：每次审批，执行前备份。

## 2. 输入参数
| 参数 | 类型 | 必填 | 默认值 | 说明（写给模型看的） |
|---|---|---|---|---|
| `project` | string | 是 | — | 项目名，或 `eplan_project_list` 返回的 `path`。写工具必须显式给出 |
| `cables` | array of string，1–200 项 | 否 | 全部电缆 | 只给这些电缆编号，完整标识如 `+A2-WD4`（`eplan_device_list` 的 `category: "Cable"`） |
| `scheme` | string | 是 | — | 电缆编号方案的名称，如 `Standard`；不存在时错误中列出可用的方案 |
| `keep_existing` | boolean | 否 | false | true：已有标识的电缆保留原名，只给没有标识的编号 |
| `start`、`step` | integer，≥ 1 | 否 | 1、1 | 起始值和步长 |
| `dry_run` | boolean | 否 | false | 为 true 时只返回编号的范围，不请求审批，不备份，也不修改项目 |

- `scheme` 必填，理由同 `eplan_terminal_renumber`。

## 3. 输出
同 `eplan_device_renumber`，`scope` 中是 `cableCount`，`changed` 是电缆标识（`{ "from": "+A2-WD4", "to": "+A2-WD5" }`），没有 `stillUnnumbered`。
- 一行摘要：`Renumbered 4 cables. A backup was made first (see 'backup'); Ctrl+Z in EPLAN undoes this step.`

## 4. 错误
同 `eplan_device_renumber`，另外：
| 错误码 | 什么时候发生 | `execution` | 返回给模型的提示 |
|---|---|---|---|
| `INVALID_ARGUMENT` | `scheme` 不在项目设置 `CableLog.CableNumberingScheme` 中；`cables` 中有不是电缆的标识 | `not_started` | `metadata.candidates` 列出可用的方案；不是电缆时说明它的类别 |
| `DEVICE_NOT_FOUND` / `AMBIGUOUS_NAME` | 有电缆找不到，或短标识有歧义 | `not_started` | 同 `eplan_cable_get` |

## 5. 实现要点
- **Host**：走 `WritePipeline`，级别 Destructive，规划操作 `cable.renumber.plan`，执行操作 `cable.renumber.execute`。时限 300 秒，含备份。
- **方案**：规划时在项目设置 `CableLog.CableNumberingScheme` 中校验（A24：这个节点在项目设置中，示例项目只有 `Standard`）；项目方案设置的读法在实现时验证。
- **范围**：`cables` 中的电缆，或按类别 `Cable` 取的全部电缆（不用 `GetCablesWithFilterScheme("")`，A24）。
- **执行**：Destructive 流水线先备份，然后在 `TransactionScope` 中：记下名称；`new CableService().RenumberCables(cables, scheme, keep_existing, true, start, step)`（第 4 个参数"保留标识符"为 true，S21）；读回、比较、提交。
- **已验证**（S21，A24）：78 根电缆 0.2–0.3 秒，两对电缆互换了名称；能被 `SafetyPoint` 回滚，提交后 Ctrl+Z 一步撤销。
- **给定的电缆**：先按完整标识找，再像 `eplan_cable_get` 那样按显示的标识找（多个结构中都有时 `AMBIGUOUS_NAME`）；是其他设备时 `INVALID_ARGUMENT`，`metadata.notCables` 列出标识和类别。`keep_existing = true` 时只数标识带 `?` 的电缆（XML：EPLAN 只给这些编号），没有时不审批、不执行。
- **待 Large 测试确认**：`keep_existing = true` 的效果；只给部分电缆时其他电缆不变；"保留标识符"为 false 时的差别；项目设置中方案的读法（`ProjectSchemeSetting.Init(节点, 项目)`，按 XML 文档）；EPLAN 没有改任何电缆时是否留下撤销步骤（工具同 `eplan_device_renumber` 返回 `not_started`）。

## 6. 写操作专项
同 `eplan_device_renumber` 第 6 节，撤销名为"AI: eplan_cable_renumber"。

## 7. 测试
- **Small**：方案候选、变更清单文字、摘要、schema 快照、注解。
- **Medium**：假 Bridge 上的 `dry_run`、方案不存在、不是电缆、`PLAN_STALE`、备份失败时不执行。
- **Large**（示例项目的 scratch 副本）：`scheme = "Standard"` 给全部电缆编号：`+A2-WD4` 与 `+A2-WD5` 互换（S21）；`backup` 能打开；Ctrl+Z 一步撤销。
- **Agent 评测**："按 Standard 方案给项目的电缆重新编号，告诉我哪些变了"。

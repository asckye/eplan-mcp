# 工具规格：`eplan_connection_number`

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_connection_number` |
| 所属分组 / Profile | `connection` / `full`（M4c 的新工具默认只在 `full` 中） |
| 安全级别 | Destructive |
| MCP 注解 | `readOnlyHint` = false　`destructiveHint` = true　`idempotentHint` = false　`openWorldHint` = false |
| 状态 | Proposed（M4c 第二批；已实现，待实机验证） |

## 1. 这个工具解决什么任务
"给项目的连接编线号""按电位重新编线号"。代理让 EPLAN 按线号方案给项目的连接编号（EPLAN 的"连接编号"，覆盖矩阵第 4 节的"线号管理器"）。一次可能改几百条线号（S21：605 条），结果由方案决定，事先算不出，按 Destructive 处理：每次审批，执行前备份。

## 2. 输入参数
| 参数 | 类型 | 必填 | 默认值 | 说明（写给模型看的） |
|---|---|---|---|---|
| `project` | string | 是 | — | 项目名，或 `eplan_project_list` 返回的 `path`。写工具必须显式给出 |
| `scheme` | string | 是 | — | 线号方案的名称，如 `Standard`、`Potenzialorientiert`、`Verbindungsorientiert`；不存在时错误中列出可用的方案 |
| `overwrite` | enum `none` / `except_manual` / `all` | 否 | `except_manual` | 已有线号怎样处理：都保留；只覆盖自动编的、保留手工输入的；全部覆盖 |
| `dry_run` | boolean | 否 | false | 为 true 时只返回编号的范围，不请求审批，不备份，也不修改项目 |

- 范围总是整个项目：EPLAN 的线号按方案在全项目内分配（`DesignateConnections` 的项目重载）。按页或按连接编号以后再加，需要先验证 `ArrayList` 重载。
- 默认 `except_manual`：不覆盖工程师手工输入的线号。

## 3. 输出
```json
{
  "project": "Line1",
  "execution": "committed",
  "dryRun": false,
  "scope": { "scheme": "Standard", "overwrite": "except_manual", "connections": 2060, "designatedBefore": 116 },
  "changed": [ { "from": "*", "to": "433", "page": "=F+A2&EFS1/2" }, { "from": null, "to": "011", "page": "=EA+A1&EFS1/1" } ],
  "changedCount": 605,
  "designatedAfter": 659,
  "backup": "C:\\Users\\<user>\\EplanMcp\\backups\\Line1\\20261021T083000123Z\\Line1.elk",
  "undo": "AI: eplan_connection_number",
  "recovered": false,
  "truncated": true,
  "hint": "605 connections changed; the first 200 are listed."
}
```
- `changed`：线号（`CONNECTION_DESIGNATION`）改变的连接，最多 200 条；`from` 为 `null` 表示原来没有线号。
- 一行摘要：`Numbered 605 connections. A backup was made first (see 'backup'); Ctrl+Z in EPLAN undoes this step.`

## 4. 错误
同 `eplan_device_renumber`（没有 `pages`、`PAGE_NOT_FOUND`），另外：
| 错误码 | 什么时候发生 | `execution` | 返回给模型的提示 |
|---|---|---|---|
| `INVALID_ARGUMENT` | `scheme` 不在项目设置 `WireNumberingLog.Schema` 中 | `not_started` | `metadata.candidates` 列出可用的方案 |

## 5. 实现要点
- **Host**：走 `WritePipeline`，级别 Destructive，规划操作 `connection.number.plan`，执行操作 `connection.number.execute`。时限 300 秒，含备份。
- **方案**：规划时在项目设置 `WireNumberingLog.Schema` 中校验（A24：示例项目有 11 个方案）；读法同 `eplan_cable_renumber`。
- **执行**：Destructive 流水线先备份，然后在 `TransactionScope` 中：记下每条连接的会话内标识和线号；`new ConnectionService().DesignateConnections(project, scheme, 覆盖方式, false, false)`（标为手工：false；重新生成连接：false）；读回、比较、提交。`overwrite` 对应 `DesignateOverwrition.None`、`ExceptManuals`、`All`。
- **已验证**（S21，A24）：`All` 时约 1 秒，605 条连接得到线号（有线号的 116 → 659），写入 `Connection`、`Function`、`Page`、`Message`；能被 `SafetyPoint` 回滚，提交后 Ctrl+Z 一步撤销。
- **读回**：`GetConnections(null)` 的全部连接（含 3D 连接，它们没有页），线号按 `eplan_connection_list` 读线号的方式读；按会话内标识比较前后。变更清单写成 `wire numbers in the project (2060 connections, 116 numbered), scheme: Standard, overwrite: except manual`，不超过审批对话框一行的 120 个字符。
- **待 Large 测试确认**：`ExceptManuals`、`None` 的效果；不同方案（如 `Potenzialorientiert`）的结果；读回时 3D 连接的处理（同 `eplan_connection_list`）；项目设置中方案的读法（`ProjectSchemeSetting.Init(节点, 项目)`，按 XML 文档）；EPLAN 没有改任何线号时是否留下撤销步骤（工具同 `eplan_device_renumber` 返回 `not_started`）。

## 6. 写操作专项
同 `eplan_device_renumber` 第 6 节，撤销名为"AI: eplan_connection_number"。

## 7. 测试
- **Small**：`overwrite` 的映射、方案候选、`changed` 的截断、摘要、schema 快照、注解。
- **Medium**：假 Bridge 上的 `dry_run`、方案不存在、`PLAN_STALE`、备份失败时不执行。
- **Large**（示例项目的 scratch 副本）：`scheme = "Standard"`、`overwrite = "all"`：约 605 条线号改变（S21）；`backup` 能打开；Ctrl+Z 一步撤销；再用 `except_manual` 记录结果。
- **Agent 评测**："按 Standard 方案给项目编线号，不要覆盖手工输入的线号"。

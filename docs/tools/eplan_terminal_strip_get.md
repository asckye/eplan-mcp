# 工具规格：`eplan_terminal_strip_get`

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_terminal_strip_get` |
| 所属分组 / Profile | `terminal` / `full`（M4c 的新工具默认只在 `full` 中，工具设计规范第 10 节） |
| 安全级别 | Read |
| MCP 注解 | `readOnlyHint` = true　`destructiveHint` = false　`idempotentHint` = true　`openWorldHint` = false |
| 状态 | Proposed（M4c 第一批） |

## 1. 这个工具解决什么任务
"-XD4 的端子按什么顺序排？每个端子内侧、外侧各接到哪里？哪些端子有跨接？"代理看一排端子的全貌，用来回答端子图类的问题，或者在编号前后核对。

## 2. 输入参数
| 参数 | 类型 | 必填 | 默认值 | 说明（写给模型看的） |
|---|---|---|---|---|
| `project` | string | 否 | EPLAN 中选中的项目 | 项目名，或 `eplan_project_list` 返回的 `path` |
| `strip` | string | 是 | — | 端子排标识，如 `+A1-XD4`；`eplan_terminal_strip_list` 返回的 `name`。不带结构的短标识（`-XD4`）在多个位置出现时返回 `AMBIGUOUS_NAME` |
| `limit` | integer，1–200 | 否 | 100 | 每页最多列出的端子数 |
| `cursor` | string | 否 | — | 上一次结果中的 `nextCursor` |

## 3. 输出
```json
{
  "project": "Line1",
  "strip": { "name": "+A2-XD2", "terminals": 5, "bridges": 3 },
  "terminals": [
    { "name": "+A2-XD2:1", "designation": "1", "main": true, "page": "=EA+A2&EFS1/1",
      "inside": [ { "device": "=EA+A2-QA1", "pin": "2" } ], "outside": [ { "device": "+B1-WG2", "pin": "1" } ],
      "bridgedWith": ["+A2-XD2:2"], "plc": null }
  ],
  "total": 5,
  "nextCursor": null,
  "truncated": false,
  "hint": null
}
```
- 示例中的连接只是示意；`+A2-XD2` 的端子数、跨接数来自 S21。
- `terminals`：`TerminalStrip.Terminals` 的顺序，即端子排中的排列顺序（待 Large 测试确认）。`designation` 是端子名中 `:` 之后的部分。
- `inside`、`outside`：`InternalConnections`、`ExternalConnections` 另一端的设备和连接点，字段同 `eplan_connection_list` 的端点；各最多 10 个。
- `bridgedWith`：同一跨接中的其他端子（`Terminal.Bridges`），没有时为空数组。
- `plc`：端子属于 PLC 时为 PLC 的标识（`Terminal.PLC`），否则为 `null`。
- `main`：`IsMainTerminal`。S21 中示例项目 1331 个端子有 485 个主端子。
- 一行摘要：`Terminal strip found: 5 terminals, 3 bridges.`，不回显标识。

## 4. 错误
| 错误码 | 什么时候发生 | 返回给模型的提示 |
|---|---|---|
| `INVALID_ARGUMENT` | 缺少 `strip`，或其他参数不合法 | `metadata.violations` 列出全部问题 |
| `DEVICE_NOT_FOUND` | 没有这个端子排 | 提示用 `eplan_terminal_strip_list` 的 `name_contains` 查 |
| `AMBIGUOUS_NAME` | 短标识对应多排 | `metadata.candidates` 列出完整标识 |
| `PROJECT_NOT_OPEN` | 项目解析失败 | 同 `eplan_project_get` |
| 连接与授权类错误 | 同 `eplan_project_list` | 同 `eplan_project_list` |

## 5. 实现要点
- 查找：`GetTerminalStrips(null)` 中按 `Name` 序数比较；找不到时按去掉结构前缀后的部分不区分大小写比较，规则与 `eplan_device_get` 的短标识相同。
- 连接的另一端：对每条内、外连接取 `StartPin`、`EndPin` 中不属于本端子的一端。
- 待 Large 测试确认（S21 只数了个数）：
  - `Terminals` 的顺序是否就是端子图中的顺序；
  - 内、外连接的划分与端子图是否一致；
  - 跨接（`Terminal.Bridge.Terminals`）的读法；
  - 有很多端子的排（如 `+B2.X1-XZ1`，33 个）的读取耗时。

## 6. 写操作专项
不适用。端子编号是第二批的 `eplan_terminal_renumber`。

## 7. 测试
- Small：参数校验、`designation` 的拆分、摘要。
- Medium：假 Bridge 上的命中、歧义、找不到、分页。
- Large：示例项目副本上 `+A2-XD2` 有 5 个端子、3 个跨接（S21）。
- Agent 评测："-XD2 的 1 号端子外侧接的是哪根电缆的哪根芯线？"

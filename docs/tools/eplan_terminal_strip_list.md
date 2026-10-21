# 工具规格：`eplan_terminal_strip_list`

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_terminal_strip_list` |
| 所属分组 / Profile | `terminal` / `full`（M4c 的新工具默认只在 `full` 中，工具设计规范第 10 节） |
| 安全级别 | Read |
| MCP 注解 | `readOnlyHint` = true　`destructiveHint` = false　`idempotentHint` = true　`openWorldHint` = false |
| 状态 | Proposed（M4c 第一批） |

## 1. 这个工具解决什么任务
"+A1 里有哪些端子排？每排多少个端子？"代理先列出端子排，再用 `eplan_terminal_strip_get` 看一排的端子顺序和接线。

端子排定义不是 `eplan_device_list` 能按类别取到的功能：功能类别中没有端子排定义（只有 `Terminal` 等），S21 用 `DMObjectsFinder.GetTerminalStrips` 才取到 45 排。所以需要单独的列表工具。

## 2. 输入参数
| 参数 | 类型 | 必填 | 默认值 | 说明（写给模型看的） |
|---|---|---|---|---|
| `project` | string | 否 | EPLAN 中选中的项目 | 项目名，或 `eplan_project_list` 返回的 `path` |
| `name_contains` | string | 否 | — | 端子排标识中包含的文字，不区分大小写，如 `+A1`、`-XD` |
| `limit` | integer，1–200 | 否 | 50 | 每页最多条数 |
| `cursor` | string | 否 | — | 上一次结果中的 `nextCursor` |

## 3. 输出
```json
{
  "project": "Line1",
  "strips": [ { "name": "+A1-XD4", "terminals": 15, "bridges": 0 } ],
  "total": 45,
  "nextCursor": null,
  "truncated": false,
  "hint": null
}
```
- `name`：端子排定义的标识（`TerminalStrip.Name`）。
- `terminals`：`TerminalStrip.Terminals` 的个数，包括辅助端子；`bridges`：`TerminalStrip.Bridges` 的个数（S21：示例项目中 `+A2-XD2` 有 3 个）。
- 顺序：按 `name` 的序数顺序，分页稳定。
- 一行摘要：`Listed 45 terminal strips.` / `Listed 50 of 120 terminal strips (more: cursor).`。

## 4. 错误
| 错误码 | 什么时候发生 | 返回给模型的提示 |
|---|---|---|
| `INVALID_ARGUMENT` | 参数不合法 | `metadata.violations` 列出全部问题 |
| `PROJECT_NOT_OPEN` | 项目解析失败 | 同 `eplan_project_get` |
| 连接与授权类错误 | 同 `eplan_project_list` | 同 `eplan_project_list` |

## 5. 实现要点
- `DMObjectsFinder.GetTerminalStrips(null)`（S21：45 排；耗时没有单独测）。端子排定义没有所在页（S21），所以不提供 `page` 参数；按页找端子用 `eplan_device_list`（`category: "Terminal"`、`page`）。
- `TerminalStrip.Properties` 可以读；不对端子排定义使用 `FunctionBase.Properties`（XML：会抛 `ForbiddenOperationException`）。
- 待 Large 测试确认：没有定义对象、只由端子组成的端子排是否也由 `GetTerminalStrips` 返回。

## 6. 写操作专项
不适用。

## 7. 测试
- Small：参数校验、排序、摘要。
- Medium：假 Bridge 上的过滤与分页。
- Large：示例项目副本上共 45 排；`+A1-XD4` 有 15 个端子（S21）。
- Agent 评测："+A2 有几排端子？哪一排有跨接？"

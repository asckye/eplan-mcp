# 工具规格：`eplan_check_list`

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_check_list` |
| 所属分组 / Profile | `check` / `core` |
| 安全级别 | Read |
| MCP 注解 | `readOnlyHint` = true　`destructiveHint` = false　`idempotentHint` = true　`openWorldHint` = false |
| 状态 | Proposed（M2） |

## 1. 这个工具解决什么任务
"上次检查有几条错误？都是什么问题？"代理读取项目中已有的检查消息（EPLAN 的消息管理），帮工程师归类和解释。**不运行检查**：运行检查会改写项目的消息，属于 M3 的 `eplan_check_run`（M2 设计 Q2，spike S6）。

## 2. 输入参数
| 参数 | 类型 | 必填 | 默认值 | 说明（写给模型看的） |
|---|---|---|---|---|
| `project` | string | 否 | EPLAN 中选中的项目 | 项目名，或 `eplan_project_list` 返回的 `path` |
| `severity` | enum `error` / `warning` / `hint` | 否 | — | 只列这一级别的消息 |
| `include_done` | boolean | 否 | false | 是否包含已标记为"已处理"的消息 |
| `limit` | integer，1–200 | 否 | 50 | 每页最多条数 |
| `cursor` | string | 否 | — | 上一次结果中的 `nextCursor` |

## 3. 输出
```json
{
  "project": "Line1",
  "messages": [
    { "number": 4025, "severity": "warning", "region": "Connections", "text": "…", "done": false, "object": { "kind": "Function", "name": "=GAA+A1-FC1" } }
  ],
  "counts": { "error": 0, "warning": 6, "hint": 14 },
  "total": 6,
  "nextCursor": null,
  "truncated": false,
  "hint": null
}
```
- 消息是"上一次检查"的结果（EPLAN 文档）。项目中没有检查消息时返回空列表，并在 `hint` 中说明：需要工程师在 EPLAN 中运行检查，或等待 M3 的 `eplan_check_run`。
- `counts` 是过滤前各级别的数量（不含 `include_done = false` 时排除的已处理消息）。
- `text` 是 EPLAN 生成的消息文本，其中包含项目数据，经过不可信文本处理。`object`：消息关联的对象类型和名称，没有关联对象时为 `null`（S6 的样本中就没有）。
- 一行摘要：`6 warnings, 14 hints from the last check.`

## 4. 错误
| 错误码 | 什么时候发生 | 返回给模型的提示 |
|---|---|---|
| `INVALID_ARGUMENT` | 参数不合法 | `metadata.violations` 列出全部问题 |
| `PROJECT_NOT_OPEN` / `AMBIGUOUS_NAME` | 项目解析失败 | 同 `eplan_project_get` |
| 连接与授权类错误 | 同 `eplan_project_list` | 同 `eplan_project_list` |

## 5. 实现要点
- `new EServices.PrjMessagesCollection(project)`，枚举元素 `ProjectMessage`：`GetId`、`GetCategory`（Hint / Warning / Error）、`GetGroup`、`GetText`、`GetDone`、`GetObject1`。**只读**：`Add`、`AddMessage`、`Remove`、`Clear`、`SetDone` 都已列入 BannedSymbols。
- 构造和枚举是否会写入项目，S6 在 action 中没有发现设计数据文件变化；Bridge 调度器中的情况在 Large 测试中确认。
- S6：运行一次检查后，示例项目有 20 条消息（14 条提示、6 条警告），读取耗时可以忽略。

## 6. 写操作专项
不适用。

## 7. 测试
- Small：级别过滤、已处理消息的排除、`counts`、空结果的提示、摘要。
- Medium：假 Bridge 上的分页。
- Large：示例项目副本先在 EPLAN 中运行检查，再调用，得到 6 条警告、14 条提示（S6）。
- Agent 评测："上次检查有哪些警告？按区域归类。"

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
- 消息是"上一次检查"的结果（EPLAN 文档）。项目中没有检查消息时返回空列表，并在 `hint` 中说明：需要工程师在 EPLAN 中运行检查（本服务器暂时不能运行检查）。检查消息全部标记为"已处理"而 `include_done = false` 时，`hint` 改为提示传 `include_done: true`。按 `severity` 过滤后为空、但 `counts` 中有其他级别时，`hint` 为 `null`（`counts` 已经说明情况）。
- `counts` 是过滤前各级别的数量（不含 `include_done = false` 时排除的已处理消息）。
- `severity` 取 `error` / `warning` / `hint`；EPLAN 的 `Classification.Unknown`（或枚举外的值）返回 `unknown`，只在不按级别过滤时列出，不计入 `counts`。
- `text` 是 EPLAN 生成的消息文本，其中包含项目数据，经过不可信文本处理。`object`：消息关联的对象（`GetObject1()`）的 EPLAN 类型名（如 `Function`、`Terminal`、`Page`）和名称，名称对功能是完整标识、对页是完整页名，其他类型为 `null`；没有关联对象时 `object` 为 `null`（S6 的样本中就没有）。
- `region` 是 EPLAN `IMessage.Region` 的枚举名，如 `Connections`。
- 一行摘要：`6 warnings, 14 hints from the last check.`（数量为 0 的级别不写）；不在一页内时加上 `Listed 50 of 120 (more: cursor).`；没有可列的消息时为 `No check messages to list; see 'hint'.`。不回显消息文本。

## 4. 错误
| 错误码 | 什么时候发生 | 返回给模型的提示 |
|---|---|---|
| `INVALID_ARGUMENT` | 参数不合法 | `metadata.violations` 列出全部问题 |
| `PROJECT_NOT_OPEN` / `AMBIGUOUS_NAME` | 项目解析失败 | 同 `eplan_project_get` |
| 连接与授权类错误 | 同 `eplan_project_list` | 同 `eplan_project_list` |

## 5. 实现要点
- `new EServices.PrjMessagesCollection(project)`，枚举元素 `ProjectMessage`：`GetId`、`GetCategory`（Hint / Warning / Error）、`GetGroup`、`GetText`、`GetDone`、`GetObject1`。**只读**：`Add`、`AddMessage`、`Remove`、`Clear`、`SetDone` 都已列入 BannedSymbols。
- 构造和枚举是否会写入项目，S6 在 action 中没有发现设计数据文件变化；在 Bridge 调度器中也不会，由 Large 测试 `NoSideEffectTests` 确认（api-notes A12）。
- S6：运行一次检查后，示例项目有 20 条消息（14 条提示、6 条警告），读取耗时可以忽略。
- 实现（`EplanProjectMessageReader` → `CheckListOperation`）：适配层按路径找到项目，`new PrjMessagesCollection(project)` 后一次 `foreach`，只处理 `ProjectMessage` 元素（S6 中元素全是这种类型；`BaseProjectMessage` 按文档不存储在项目中），复制 `GetId`、`GetCategory`、`GetGroup`、`GetText`、`GetDone` 和 `GetObject1()` 的类型名与名称。集合不是 `IDisposable`，枚举器由 `foreach` 释放；消息和关联对象属于 EPLAN，不释放。已处理消息的排除、`counts`、`severity` 过滤（不区分大小写）和切片在 Bridge.Core 中完成，保持 EPLAN 的顺序。
- 游标指纹包含 `project`、`severity` 和 `include_done`。
- M2 设计 4.1 的工具表中列有 `page` 参数，本规格没有，也没有实现：消息不一定关联到页，按页筛选需要先取关联对象所在的页（文档说明要先取对象再查），留到有任务需要时再做。
- 已由 Large 测试确认：在 Bridge 调度器中（不在 action 中）构造和枚举 `PrjMessagesCollection` 不写入项目（对比项目文件的修改时间）。
- **未验证**（夹具中没有检查消息，Large 测试覆盖不到，需要先在 GUI 中运行检查）：`GetObject1()` 实际返回哪些类型，关联对象已被删除时是返回 `null` 还是抛出异常（适配层捕获 DataModel 的异常家族，这条消息的 `object` 为 `null`，其余消息照常列出）；GUI 中运行的检查结果在重新打开项目后仍能读到。

## 6. 写操作专项
不适用。

## 7. 测试
- Small：级别过滤、已处理消息的排除、`counts`、空结果的提示、摘要。
- Medium：假 Bridge 上的分页。
- Large：示例项目副本先在 EPLAN 中运行检查，再调用，得到 6 条警告、14 条提示（S6）。
- Agent 评测："上次检查有哪些警告？按区域归类。"

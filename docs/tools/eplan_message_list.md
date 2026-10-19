# 工具规格：`eplan_message_list`

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_message_list` |
| 所属分组 / Profile | `message` / `core` |
| 安全级别 | Read |
| MCP 注解 | `readOnlyHint` = true　`destructiveHint` = false　`idempotentHint` = true　`openWorldHint` = false |
| 状态 | Proposed（M2） |

## 1. 这个工具解决什么任务
"EPLAN 刚才报了什么错？"很多 EPLAN 操作的失败只出现在系统消息里（api-notes A1）。代理用它读取本次 EPLAN 会话的系统消息，帮工程师定位问题。

## 2. 输入参数
| 参数 | 类型 | 必填 | 默认值 | 说明（写给模型看的） |
|---|---|---|---|---|
| `min_level` | enum `message` / `warning` / `error` | 否 | `warning` | 只列不低于这一级别的消息（致命错误总是包含在内） |
| `limit` | integer，1–200 | 否 | 20 | 每页最多条数 |
| `cursor` | string | 否 | — | 上一次结果中的 `nextCursor` |

系统消息属于 EPLAN 会话，与项目无关，所以没有 `project` 参数。

## 3. 输出
```json
{
  "messages": [
    { "level": "error", "number": "S085016", "text": "…", "occurrences": 1 }
  ],
  "total": 4,
  "nextCursor": null,
  "truncated": false
}
```
- **最新的在前**。连续的相同消息由 EPLAN 合并，`occurrences` 是合并的次数（S6）。
- `level` 取 `message` / `warning` / `error` / `fatal`，对应 EPLAN `MessageLevel` 的 Message、Warning、Error、FatalError。
- `number` 是 EPLAN 的 S 编号；没有编号时为 `null`（EPLAN 返回 `S000000`）。
- Trace 级别的消息不返回；EPLAN 中存在不在 `MessageLevel` 枚举中的级别（S6 见到过 7，"有新版本"提示），归为 `message`。`Assert`（3）按它在枚举中的位置（Warning 与 Error 之间）归为 `warning`；文档说集合中没有 Assert，S6 也没有见到。
- 消息文本可能包含项目数据和本机路径，经过不可信文本处理。
- 一行摘要：`4 messages at warning level or above.`；分页时为 `Listed 20 of 45 messages at warning level or above (more: cursor).`，不回显消息文本。

## 4. 错误
| 错误码 | 什么时候发生 | 返回给模型的提示 |
|---|---|---|
| `INVALID_ARGUMENT` | 参数不合法 | `metadata.violations` 列出全部问题 |
| 连接与授权类错误 | 同 `eplan_project_list` | 同 `eplan_project_list` |

## 5. 实现要点
- 🔴 **同一时间只使用一个 `Base.SysMessagesCollection`**（api-notes A11，编码规范 9.5 第 15 条）：新建第二个集合后再读第一个集合中消息的文本，会触发 EPLAN 内部的访问冲突。实现方式：每次调用只新建一个集合，**一次遍历**把需要的字段复制到 DTO，之后不再访问任何 `BaseException` 对象；倒序和过滤都在 DTO 上完成。
- 这条规则也约束"操作前后系统消息差异"的收集（编码规范第 15 条）：本工具和差异收集**不能在同一次调度中各自新建集合**。
- 游标指纹包含 `min_level`。两次调用之间 EPLAN 可能产生新消息，最新在前的顺序会让页错位；工具描述中说明。
- 实现（`EplanSystemMessageReader` → `MessageListOperation`）：适配层用**无参构造**的 `SysMessagesCollection()`（S6 验证过它包含整个会话的消息、正向遍历和读取全部 490 条都正常），在一次 `foreach` 中复制 `MessageLevel`（按整数，保留枚举外的值）、`GetMessageIndex()`、`GetMessageText()`、`NumberOfOccurrences`；集合本身不是 `IDisposable`，它的枚举器是，由 `foreach` 释放；消息对象属于 EPLAN，不释放也不保留。级别过滤、倒序和切片都在 Bridge.Core 中对记录完成。不使用 `SysMessagesCollection(书签, 级别)`：其级别参数是"最低级别"还是"精确级别"尚未验证（M2 设计 4.5）。整个插件中只有这一处创建系统消息集合。
- **未验证、需在 Large 测试中确认**：正向遍历的顺序是否为从旧到新（"最新在前"依赖这一点）；在调度器中（不在 action 中）读取是否同样正常；连续调用两次是否不出现访问冲突（每次调用新建一个集合，旧集合不再访问）。

## 6. 写操作专项
不适用。不调用 `BaseException.FixMessage()`（BannedSymbols）。

## 7. 测试
- Small：级别过滤（含未知级别 7 和 Trace）、倒序、编号为 `S000000` 时的处理、摘要。
- Medium：假 Bridge 上的分页。
- Large：在 EPLAN 中制造一条警告后调用，能读到这条消息；连续调用两次不出现访问冲突。
- Agent 评测："EPLAN 刚才有什么报错？"

# 工具规格：`eplan_tool_search`

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_tool_search` |
| 所属分组 / Profile | `meta` / `discovery`（只在 discovery 中，见 [ADR-0057](../adr/0057-discovery-profile-dispatch.md)） |
| 安全级别 | Read |
| MCP 注解 | `readOnlyHint` = true　`destructiveHint` = false　`idempotentHint` = true　`openWorldHint` = false |
| 状态 | Implemented (v0.6.0) |

## 1. 这个工具解决什么任务
discovery Profile 的会话开头只有四个工具。模型要先知道"画一页图、给设备换部件、导出 PDF"该用哪个工具，再用 [`eplan_tool_usage`](eplan_tool_usage.md) 查用法，最后经 `eplan_tool_read` 或 `eplan_tool_write` 调用。本工具按关键词在全部可转发工具里找候选。

## 2. 输入参数
| 参数 | 类型 | 必填 | 默认值 | 说明（写给模型看的） |
|---|---|---|---|---|
| `query` | string，1–200 字符 | 是 | — | 任务里的词，例如 `page create`、`part`、`pdf`；不区分大小写 |
| `limit` | integer，1–20 | 否 | 8 | 最多返回几个工具 |

## 3. 输出
```json
{ "tools": [ { "name": "eplan_page_create", "title": "Create pages", "safety": "write", "dispatcher": "eplan_tool_write", "summary": "…" } ], "total": 3 }
```
- `safety`：`read`、`write` 或 `destructive`，由服务端按目标工具自己的 MCP 注解算出（UI-16），不是调用方声明的。
- `dispatcher`：调用这个工具要用的分发工具名。
- `summary`：工具描述的第一句；完整用法请调用 `eplan_tool_usage`。
- 排序：工具名中的词命中优先，其次标题，再次描述和用法正文；同分按名称。只在本地内容里找，不访问 EPLAN，也不访问网络。

## 4. 错误
| 错误码 | 什么时候发生 | 返回给模型的提示 |
|---|---|---|
| `INVALID_ARGUMENT` | `query` 为空或超长，`limit` 越界，或传了未知参数 | `metadata.violations` 一次列出全部问题 |

没有命中时不是错误：`tools` 为空，摘要建议换一个词或调用 `eplan_tool_usage` 查 `eplan_tool_search` 的示例。

## 5. 实现要点
- 搜索范围是 Profile 过滤**之前**的完整工具集合，减去四个 discovery 工具本身；开关关闭的工具（`action_run`、`api_try`、`action_try`）不出现。
- 索引在启动时由工具描述和 `Usage/*.md` 建立一次，只读、无项目数据，不进日志。
- 测试：Small 覆盖排序、开关过滤和四个元工具不出现；契约快照为 discovery 单独存一份。

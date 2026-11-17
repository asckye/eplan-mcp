# 工具规格：`eplan_tool_write`

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_tool_write` |
| 所属分组 / Profile | `meta` / `discovery`（只在 discovery 中，见 [ADR-0057](../adr/0057-discovery-profile-dispatch.md)） |
| 安全级别 | 取目标工具的级别：写入或破坏性，审批与 scratch 规则由目标工具决定 |
| MCP 注解 | `readOnlyHint` = false　`destructiveHint` = true　`idempotentHint` = false　`openWorldHint` = false |
| 状态 | Implemented (v0.6.0) |

## 1. 这个工具解决什么任务
在 discovery Profile 里调用任意一个写入或破坏性工具。客户端看到它是破坏性工具，会按"写入询问"处理；服务端的预览默认值、scratch 判断、审批、备份和审计全部由目标工具照旧执行，分发工具不加也不减。

## 2. 输入参数
| 参数 | 类型 | 必填 | 说明（写给模型看的） |
|---|---|---|---|
| `tool` | string，`enum` 为全部可转发的写入工具 | 是 | 要调用的工具名，来自 `eplan_tool_search` |
| `arguments` | object | 否 | 目标工具的参数，与直接调用它时完全相同。`dry_run` 省略时按目标工具的默认值预览（ADR-0049） |

## 3. 输出
目标工具的结果原样返回。

## 4. 错误
| 错误码 | 什么时候发生 | 返回给模型的提示 |
|---|---|---|
| `INVALID_ARGUMENT` | `tool` 不是可转发的工具；或它是只读工具（提示改用 `eplan_tool_read`）；或 `arguments` 不符合目标工具的输入 schema | `metadata.violations` 与直接调用目标工具时相同 |

其余错误（`APPROVAL_DENIED`、`PLAN_STALE`、`OUTCOME_UNKNOWN` 等）都是目标工具自己的，原样返回。

## 5. 实现要点
- 与 [`eplan_tool_read`](eplan_tool_read.md) 共用分发实现，只是方向相反：目标的 `readOnlyHint` 必须为 false。
- **审计**：目标工具的审计记录照旧写，工具名是目标工具。**后续项（未实现）**：在同一请求 ID 下标出 `dispatched_by: eplan_tool_write`；审计记录在各写工具的 `WritePipeline` 里生成，要把分发来源传进管线，留待下一个变更集。
- **审批面板**：审批显示的是目标工具名和它的变化清单，不是分发工具名。
- 测试：Medium 覆盖 scratch 内写入直接执行、scratch 外写入照旧弹审批、`dry_run` 默认预览、`OUTCOME_UNKNOWN` 与挂起照旧、只读工具被拒。

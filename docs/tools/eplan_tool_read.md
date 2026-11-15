# 工具规格：`eplan_tool_read`

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_tool_read` |
| 所属分组 / Profile | `meta` / `discovery`（只在 discovery 中，见 [ADR-0057](../adr/0057-discovery-profile-dispatch.md)） |
| 安全级别 | Read：只转发 `readOnlyHint` 为 true 的工具 |
| MCP 注解 | `readOnlyHint` = true　`destructiveHint` = false　`idempotentHint` = true　`openWorldHint` = false |
| 状态 | Designed（2026-11-14，ADR-0057 接受后） |

## 1. 这个工具解决什么任务
在 discovery Profile 里调用任意一个只读工具。客户端看到它是只读工具，可以按"只读放行"处理，而写入一律经 [`eplan_tool_write`](eplan_tool_write.md)。

## 2. 输入参数
| 参数 | 类型 | 必填 | 说明（写给模型看的） |
|---|---|---|---|
| `tool` | string，`enum` 为全部可转发的只读工具 | 是 | 要调用的工具名，来自 `eplan_tool_search` |
| `arguments` | object | 否 | 目标工具的参数，与直接调用它时完全相同；省略等于 `{}` |

## 3. 输出
目标工具的结果原样返回：同样的 `structuredContent`、文本块和 `isError`。不包一层，不改字段。

## 4. 错误
| 错误码 | 什么时候发生 | 返回给模型的提示 |
|---|---|---|
| `INVALID_ARGUMENT` | `tool` 不是可转发的工具；或它是写入工具（提示改用 `eplan_tool_write`）；或 `arguments` 不符合目标工具的输入 schema | `metadata.violations` 与直接调用目标工具时相同；写入工具的提示写明 "Call eplan_tool_write for this tool." |

其余错误都是目标工具自己的错误，原样返回。

## 5. 实现要点
- **目标的安全级别由服务端判定**：取目标工具自己的 `readOnlyHint`，不看调用方传什么（手册 UI-16）。
- **同一条调用管线**：先用 `UnknownArgumentFilter` 的同一逻辑按目标工具的 schema 校验 `arguments`，再用同一个请求上下文（服务、进度令牌、取消令牌）调用目标的 `McpServerTool`。不复制目标工具的逻辑。
- 不能转发 `eplan_tool_search`、`eplan_tool_usage`、`eplan_tool_write` 和自己。
- 目标集合取自 Profile 过滤**之前**保存的完整集合，开关关闭的工具不在其中。
- 测试：Medium 覆盖只读转发结果与直接调用逐字节相同、写入工具被拒、参数错误与直接调用相同、取消传到目标工具。

# 工具规格：`<tool_name>`

> 每个新工具（或对已发布工具的契约修改）在实现前填写本规格。
> 规则依据：[mcp-tool-design.md](../standards/mcp-tool-design.md)。
> 复制到 `docs/tools/<tool_name>.md`。工具实现后，此文件就是该工具的设计记录。

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_<domain>_<verb>` |
| 所属分组 / Profile | 例如 `pages` / `core` |
| 安全级别 | Read / Write / Destructive / Code |
| MCP 注解 | `readOnlyHint` = ? `destructiveHint` = ? `idempotentHint` = ? `openWorldHint` = false |
| 状态 | Proposed / Implemented / Deprecated |

## 1. 这个工具解决什么任务
从 AI 代理的角度写一句话：代理在什么情况下会调用它，期望得到什么。
如果能用已有工具组合完成，说明为什么还需要单独的工具。Anthropic 的建议是少而精的工具胜过一对一包装 API。

## 2. 输入参数
| 参数 | 类型 | 必填 | 默认值 | 说明（写给模型看的） |
|---|---|---|---|---|

- 标识对象的参数用业务名称（页名、设备标识），不要求模型传内部句柄；如果也接受句柄，要说明句柄只在当前 EPLAN 会话内有效。
- 枚举值在 schema 里写成 `enum`，不要只写在描述里。

## 3. 输出
- `structuredContent` 的 JSON Schema（`outputSchema`）
- 文本内容的摘要格式：模型最先读到的几行
- 大结果的分页方式：`limit` / `cursor` / `total` / `truncated`
- `response_format`：`concise` 和 `detailed` 各返回什么

## 4. 错误
| 错误码 | 什么时候发生 | `execution`（写工具） | 返回给模型的提示（要能直接指导下一步） |
|---|---|---|---|

## 5. 实现要点
- 使用的 EPLAN API（类型全名），是否已在 [api-notes](../eplan/api-notes.md) 中查证
- 是否需要 `SafetyPoint` / `LockingStep` / `QuietModeStep`
- 预计耗时；超过 2 秒的要支持进度通知和取消

## 6. 写操作专项（Read 级工具跳过）
- dry-run 返回什么（将要发生的变化清单）
- 审批请求展示给用户的摘要
- 审计记录包含哪些字段
- 回滚方式
- 每个阶段失败时的 `execution`；超时或中断后如何取回结果（[ADR-0010](../adr/0010-write-outcomes.md)）

## 7. 测试
- Small：参数校验、schema 快照
- Medium：使用 Fake Bridge 的契约测试
- Large：在 scratch 项目上的实机步骤和预期结果
- Agent 评测：新增的任务场景

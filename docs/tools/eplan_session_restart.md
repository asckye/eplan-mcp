# 工具规格：`eplan_session_restart`

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_session_restart` |
| 所属分组 / Profile | `session` / `full`（见 [ADR-0058](../adr/0058-eplan-lifecycle-tools.md)） |
| 安全级别 | Destructive（本机进程）：关闭部分与 [`eplan_session_close`](eplan_session_close.md) 完全相同 |
| MCP 注解 | `readOnlyHint` = false　`destructiveHint` = true　`idempotentHint` = false　`openWorldHint` = false |
| 状态 | Implemented (Unreleased) |

## 1. 这个工具解决什么任务
替换插件后让 EPLAN 重新加载它，或 EPLAN 状态异常时重新开始。等于先 `eplan_session_close`、再以同一个变体 `eplan_session_start`，中间任何一步失败都停下并说明停在哪一步。

## 2. 输入参数
| 参数 | 类型 | 必填 | 说明（写给模型看的） |
|---|---|---|---|
| `process_id` | integer | 是 | 要重启的 EPLAN |
| `dry_run` | boolean | 否，默认 true | 省略或 true 只列出关闭的前置检查；false 才执行 |

## 3. 输出
`close`：同 `eplan_session_close` 的结果；`bridgeVersionBefore`：关闭前插件报告的版本；`start`：同 `eplan_session_start` 的结果，演练时为 null。

## 4. 错误
关闭部分的错误同 `eplan_session_close`（此时 EPLAN 未被关闭）；启动部分的错误同 `eplan_session_start`（此时旧 EPLAN 已关闭，摘要明确说"EPLAN is closed and was not started again"）。

## 5. 实现要点
- 不重新读配置里的变体：用被关闭的那个实例自己的变体，避免重启后换了产品。
- 重启前后对比插件版本，结果里给出 `bridgeVersion` 的前后值，便于确认新插件已加载（替换插件后旧版本被影子副本缓存的问题见 api-notes 与 Large 测试说明）。
- 测试：Small 覆盖两段的错误传递；Large 与 `eplan_session_close` 合用一组测试桌面上的实机用例。

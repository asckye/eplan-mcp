# 工具规格：`eplan_session_close`

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_session_close` |
| 所属分组 / Profile | `session` / `full`（见 [ADR-0058](../adr/0058-eplan-lifecycle-tools.md)） |
| 安全级别 | Destructive（本机进程）：只在打开的项目全部在 scratch 时执行，否则拒绝 |
| MCP 注解 | `readOnlyHint` = false　`destructiveHint` = true　`idempotentHint` = false　`openWorldHint` = false |
| 状态 | Designed（2026-11-14，ADR-0058 接受后）；EPLAN 正常退出的方式待实机验证 |

## 1. 这个工具解决什么任务
代理用完 EPLAN、或要在替换插件前关掉它时调用。它不替人关真实项目：只要有一个打开的项目不在 scratch 根目录下，就拒绝并请工程师自己关。

## 2. 输入参数
| 参数 | 类型 | 必填 | 说明（写给模型看的） |
|---|---|---|---|
| `process_id` | integer | 是 | 要关闭的 EPLAN，取自 `eplan_session_status` 的 `processId` |
| `dry_run` | boolean | 否，默认 true | 省略或 true 只列出前置检查的结果；false 才关闭（ADR-0049） |

## 3. 输出
`checks`：每项前置检查的结果（`writes_in_flight`、`pending_approvals`、`suspended_writes`、`projects_outside_scratch`），`projects`：打开的项目及其是否在 scratch，`execution`：`not_started` 或 `committed`。

## 4. 错误
| 错误码 | 什么时候发生 | 返回给模型的提示 |
|---|---|---|
| `POLICY_DENIED` | 有打开的项目不在 scratch 根目录下 | 列出这些项目名（不含路径），请工程师在 EPLAN 里自己关闭；什么也没做 |
| `EPLAN_BUSY`（可重试） | 有进行中的写入或等待中的审批 | 说明是哪一项，等它结束再调用 |
| `WRITES_SUSPENDED` | 有结果未知而被挂起的写入 | 请工程师先在 EPLAN 里检查并确认（ADR-0010） |
| `SESSION_NOT_SELECTED` | `process_id` 不是本 Host 连得上的 EPLAN | 列出可选的进程号 |
| `CAPABILITY_MISSING` | Host 不在 EPLAN 所在的机器上 | 请工程师关闭 |
| `TIMEOUT` | EPLAN 收到退出请求后 60 秒仍未退出 | 说明 EPLAN 可能停在对话框上；进程不被强行结束 |

## 5. 实现要点
- 前置检查全部在 Host 侧做：写入和审批状态来自 `WritePipeline` 与审批通道，挂起来自 `SuspensionStore`，项目列表经 `project.list` 读取后按 `ScratchPaths.IsInside` 判断。
- **退出方式实现前先在 scratch 上实机验证**（候选：插件在主线程调用 EPLAN 的退出入口；不选结束进程），结论写进 api-notes 新条目。不强行结束进程。
- 审计记录：进程号、检查结果、项目数（不记路径）、结果。
- 测试：Small 覆盖每项检查的拒绝；Large 在测试桌面上分别验证"只有 scratch 项目时关闭成功"和"开着 scratch 之外的项目时拒绝"。

# 工具规格：`eplan_session_close`

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_session_close` |
| 所属分组 / Profile | `session` / `full`（见 [ADR-0058](../adr/0058-eplan-lifecycle-tools.md)） |
| 安全级别 | Destructive（本机进程）：只在打开的项目全部在 scratch 时执行，否则拒绝 |
| MCP 注解 | `readOnlyHint` = false　`destructiveHint` = true　`idempotentHint` = false　`openWorldHint` = false |
| 状态 | Implemented (v0.7.0) |

## 1. 这个工具解决什么任务
代理用完 EPLAN、或要在替换插件前关掉它时调用。它不替人关真实项目：只要有一个打开的项目不在 scratch 根目录下，就拒绝并请工程师自己关。

## 2. 输入参数
| 参数 | 类型 | 必填 | 说明（写给模型看的） |
|---|---|---|---|
| `process_id` | integer | 是 | 要关闭的 EPLAN，取自 `eplan_session_status` 的 `processId` |
| `dry_run` | boolean | 否，默认 true | 省略或 true 只列出前置检查的结果；false 才关闭（ADR-0049） |

## 3. 输出
`processId`；前置检查的计数 `writesInFlight`（进行中的写入，含等待审批的）、`planTasks`（排队或运行中的计划任务）、`suspendedWrites`（挂起的写入）；`projects`：打开的项目名及 `inScratch`，不含路径；`execution`：`not_started` 或 `committed`。

## 4. 错误
| 错误码 | 什么时候发生 | 返回给模型的提示 |
|---|---|---|
| `POLICY_DENIED` | 有打开的项目不在 scratch 根目录下 | 列出这些项目名（不含路径），请工程师在 EPLAN 里自己关闭；什么也没做 |
| `EPLAN_BUSY`（可重试） | 有进行中的写入或等待中的审批 | 说明是哪一项，等它结束再调用 |
| `WRITES_SUSPENDED` | 有结果未知而被挂起的写入 | 请工程师先在 EPLAN 里检查并确认（ADR-0010） |
| `SESSION_NOT_SELECTED` | `process_id` 不是本 Host 连得上的 EPLAN | 列出可选的进程号 |
| `CAPABILITY_MISSING` | 本 Host 以 HTTP 方式只服务一个 EPLAN 进程（ADR-0018 决定 8） | 请工程师在虚拟机里关闭 |
| `TIMEOUT` | EPLAN 收到退出请求后 60 秒仍未退出 | 说明 EPLAN 可能停在对话框上；进程不被强行结束 |

## 5. 实现要点
- 前置检查全部在 Host 侧做：写入和审批状态来自 `WritePipeline` 与审批通道，挂起来自 `SuspensionStore`，项目列表经 `project.list` 读取后按 `ScratchPaths.IsInside` 判断。
- 退出方式复用 `install-addin` 已实机验证的做法（`EplanCloser`：刷新进程后请主窗口关闭，每 3 秒重发，最长 1 分钟，api-notes A23），不强行结束进程。打开着项目时 EPLAN 是否直接退出，待测试桌面上的 Large 用例验证。
- 写入计数来自 `WritePipeline.InFlight`（进入写入流程到得出结果，含等待审批），计划任务来自 `TaskRegistry.Unfinished`，挂起来自 `SuspensionStore.All()`（本进程或不限进程的）。
- 审计记录未实现（关闭不改项目），留作后续项。
- 测试：Small 覆盖每项检查的拒绝；Large 在测试桌面上分别验证"只有 scratch 项目时关闭成功"和"开着 scratch 之外的项目时拒绝"。

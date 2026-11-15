# 工具规格：`eplan_session_start`

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_session_start` |
| 所属分组 / Profile | `session` / `full`（见 [ADR-0058](../adr/0058-eplan-lifecycle-tools.md)） |
| 安全级别 | Write（本机进程）：不改任何项目，不需要审批；占用一个 EPLAN 授权 |
| MCP 注解 | `readOnlyHint` = false　`destructiveHint` = false　`idempotentHint` = false　`openWorldHint` = false |
| 状态 | Designed（2026-11-14，ADR-0058 接受后） |

## 1. 这个工具解决什么任务
代理遇到 `EPLAN_NOT_CONNECTED` 时，不必请工程师去开 EPLAN：本工具在本机启动一个装好 EplanMcp 插件的 EPLAN，等它连上后返回与 [`eplan_session_status`](eplan_session_status.md) 相同的结果。

## 2. 输入参数
| 参数 | 类型 | 必填 | 默认值 | 说明（写给模型看的） |
|---|---|---|---|---|
| `variant` | string，`enum` 为本机已安装且注册了插件的变体，例如 `Electric P8`、`Pro Panel` | 否 | 配置里的变体；未配置时取第一个注册了插件的变体 | 要启动的产品变体 |

不能指定可执行文件路径或命令行参数：可执行文件只按 `doctor` 的规则（`EplanInstallation`：`<Program Files>\EPLAN\Platform\<版本>\Bin\EPLAN.exe`，取受支持的最高版本）确定，避免把工具变成"启动任意程序"。

## 3. 输出
与 `eplan_session_status` 相同的字段，外加 `started`（本次启动的进程号）和 `seconds`（从启动到插件握手的用时）。摘要：`Started EPLAN 2026.0.3.25421 (Pro Panel), process 12345, connected after 42 s.`

## 4. 错误
| 错误码 | 什么时候发生 | 返回给模型的提示 |
|---|---|---|
| `INVALID_ARGUMENT` | `variant` 不在可选值里 | 列出可选值 |
| `ALREADY_EXISTS` | 已经有装了插件的 EPLAN 在运行 | "EPLAN is already running (process …); call eplan_session_status." 两个 EPLAN 会争用工作站设置并在启动时卡在系统消息窗口（live 2026-11-14） |
| `CAPABILITY_MISSING` | Host 不在 EPLAN 所在的机器上（HTTP/虚拟机模式，ADR-0018），或本机没有受支持的 EPLAN | 说明要工程师在 EPLAN 那台机器上启动 |
| `TIMEOUT` | 180 秒内插件没有握手 | 说明 EPLAN 可能停在对话框上，请工程师看一眼；进程不被结束 |

## 5. 实现要点
- 启动后轮询插件管道（与 `eplan_session_status` 相同的发现逻辑），每 5 秒发一次进度。
- 启动的 EPLAN 不带项目参数；打开项目仍用 [`eplan_project_open`](eplan_project_open.md)。窗口最小化启动，不抢工程师的焦点。
- 审计记录：变体、可执行文件版本、进程号、用时、结果。
- 测试：Small 覆盖变体选择和可执行文件查找（假文件系统）；Large 在测试桌面上实际启动一次并握手，另测已有 EPLAN 时返回 `ALREADY_EXISTS`。

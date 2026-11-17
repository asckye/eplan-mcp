# 工具规格：`eplan_session_start`

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_session_start` |
| 所属分组 / Profile | `session` / `full`（见 [ADR-0058](../adr/0058-eplan-lifecycle-tools.md)） |
| 安全级别 | Write（本机进程）：不改任何项目，不需要审批；占用一个 EPLAN 授权 |
| MCP 注解 | `readOnlyHint` = false　`destructiveHint` = false　`idempotentHint` = false　`openWorldHint` = false |
| 状态 | Implemented (v0.6.0) |

## 1. 这个工具解决什么任务
代理遇到 `EPLAN_NOT_CONNECTED` 时，不必请工程师去开 EPLAN：本工具在本机启动一个装好 EplanMcp 插件的 EPLAN，等它连上后返回与 [`eplan_session_status`](eplan_session_status.md) 相同的结果。

## 2. 输入参数
| 参数 | 类型 | 必填 | 默认值 | 说明（写给模型看的） |
|---|---|---|---|---|
| `variant` | string，本机已安装的变体，例如 `Electric P8`、`Pro Panel`，不区分大小写 | 否 | `Electric P8`（已安装时），否则第一个已安装的变体 | 要启动的产品变体；不在已安装变体中时返回 `INVALID_ARGUMENT` 并列出可选值 |

不能指定可执行文件路径或命令行参数：可执行文件只按 `doctor` 的规则（`EplanInstallation`：`<Program Files>\EPLAN\Platform\<版本>\Bin\EPLAN.exe`，取受支持的最高版本）确定，避免把工具变成"启动任意程序"。

## 3. 输出
`processId`、`variant`、`version`、`bridgeVersion`、`seconds`（从启动到插件握手的用时）和 `hint`。摘要：`Started EPLAN 2026.0.3.25421 (Pro Panel), process 12345, connected after 42 s.`

## 4. 错误
| 错误码 | 什么时候发生 | 返回给模型的提示 |
|---|---|---|
| `INVALID_ARGUMENT` | `variant` 不在可选值里 | 列出可选值 |
| `ALREADY_EXISTS` | 已经有装了插件的 EPLAN 在运行 | "EPLAN is already running (process …); call eplan_session_status." 两个 EPLAN 会争用工作站设置并在启动时卡在系统消息窗口（live 2026-11-14） |
| `CAPABILITY_MISSING` | 本 Host 以 HTTP 方式只服务一个 EPLAN 进程（ADR-0018 决定 8，虚拟机部署），或本机没有受支持的 EPLAN | 请工程师在虚拟机里启动 EPLAN 并重启服务 |
| `TIMEOUT` | 180 秒内插件没有握手 | 说明 EPLAN 可能停在对话框上，请工程师看一眼；进程不被结束 |

## 5. 实现要点
- 启动后轮询插件管道（与 `eplan_session_status` 相同的发现逻辑），每 5 秒发一次进度。
- 启动的 EPLAN 不带项目参数；打开项目仍用 [`eplan_project_open`](eplan_project_open.md)。窗口正常显示，不最小化：之后写入的审批框弹在 EPLAN 窗口上，最小化会让工程师看不到。
- 实现：`EplanLifecycle`（`src/EplanMcp.Host/Addin/`）复用 `install-addin` 的 `EplanInstallation`、`IEplanProcesses`、`IBridgeProbe`。审计记录未实现（启动不改项目，留作后续项）。
- 测试：Small 覆盖变体选择和可执行文件查找（假文件系统）；Large 在测试桌面上实际启动一次并握手，另测已有 EPLAN 时返回 `ALREADY_EXISTS`。

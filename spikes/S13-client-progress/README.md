# Spike S13：客户端的进度、取消与图片

## 问题
[M4 设计](../../docs/design/2026-10-20-m4-workflows.md)第 4.6 节：
1. Claude Code 调用工具时是否带 `progressToken`，是否收到进度；
2. 用户中断时是否发送 `notifications/cancelled`；
3. 超过 2 分钟转到后台之后，进度和结果怎样处理；
4. 工具结果中的 image 内容能否交给模型。

## 方法
- `mcp_tap.py` 是套在 `eplanmcp` 外面的 stdio 转发外壳：原样转发双方的每一行 JSON-RPC，同时连同方向和时间写进日志文件，自己不向 stdout 写任何别的东西。
- MCP 配置中把服务器命令设为 `python -I mcp_tap.py <日志文件> <eplanmcp.exe>`，用 Claude Code 的无头模式（`claude -p … --mcp-config … --strict-mcp-config --allowedTools …`）让它先调用 `eplan_report_update`，再用 `inline: true` 调用 `eplan_export_image`，并描述图上的内容。项目是 scratch 中的 `writes\LargeWrite`。
- 环境：Claude Code 2.1.299（桌面版自带），EplanMcp main 82332d5 的 Release 构建，EPLAN 2026.0.3.25421，2026-10-20。

## 结果
| # | 结论 |
|---|---|
| 1 | ✅ 每个 `tools/call` 的 `_meta` 都带 `progressToken`（还有 `claudecode/toolUseId`）；协议版本 `2025-11-25`。服务器在第 5 秒发出的心跳 `Updating reports (5 s)` 送到了客户端 |
| 2 | 未测：无头模式中无法像交互界面那样中断一次调用。服务器一侧的取消已用自写客户端实测（api-notes A20） |
| 3 | 未测：示例项目上最长的调用（更新全部报表）约 7 秒，到不了 2 分钟 |
| 4 | ✅ 模型拿到了 `inline` 的 PNG：它说出了 `=GAA+A1&EFS1/1` 上的 `-FC1`（32 A 断路器）、电流互感器 `-TA1`–`-TA3`、电表 `-PF1`、母线 `-WE1`/`-WE2` 和警示牌，与这页的内容一致 |

客户端在 `initialize` 中声明了 `roots` 和 `elicitation`（form、url）能力。

## 结论
1. 进度可以照 M4 设计 4.3 使用：Claude Code 每次调用都给出进度令牌，心跳能送达。
2. `eplan_export_image` 的 `inline` 有用：模型能读出图上的设备标识和文字。
3. 第 2、3 项需要在交互式会话中手工确认：调用耗时工具时按 Esc 中断，看日志里是否出现 `notifications/cancelled`；以及有没有超过 2 分钟的调用。

## 局限
- 只测了一个客户端、一个版本、无头模式。

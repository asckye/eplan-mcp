# 工具规格：`eplan_session_status`

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_session_status` |
| 所属分组 / Profile | `session` / `core` |
| 安全级别 | Read |
| MCP 注解 | `readOnlyHint` = true　`destructiveHint` = false　`idempotentHint` = true　`openWorldHint` = false |
| 状态 | Proposed（M1） |

## 1. 这个工具解决什么任务
代理在开始工作前，或者其他工具返回 `EPLAN_NOT_CONNECTED` / `BRIDGE_INCOMPATIBLE` 之后，用它确认"EPLAN 有没有在运行、插件是否已加载、连的是哪个版本"。它是唯一一个在 EPLAN 不可用时也**正常返回**（而不是报错）的工具，所以代理可以用它诊断问题，并把下一步告诉工程师。

## 2. 输入参数
无。schema：`{"type":"object","properties":{},"additionalProperties":false}`。

## 3. 输出
```json
{
  "connected": true,
  "host": { "version": "0.1.0", "protocolVersion": 1 },
  "eplan": { "processId": 12345, "version": "2026.0.3.25421", "variant": "Pro Panel", "bridgeVersion": "0.1.0", "protocolVersion": 1 },
  "instances": [ { "processId": 12345, "selected": false } ],
  "hint": null
}
```
- `connected = false` 时 `eplan` 为 null，`hint` 用英文说明原因和下一步，例如 "No running EPLAN with the EplanMcp add-in was found. Start EPLAN and make sure the add-in is loaded, or run 'eplanmcp doctor'."
- `instances` 列出发现的全部 Bridge：进程号，以及是否是用 `eplan_session_select` 选中的实例（`selected`）。多于一个且没有选择时 `connected = false`，`hint` 提示先看 `instances`，再用 `eplan_session_select` 选定一个；选定后 `connected` 和 `eplan` 描述所选的实例。
- 一行摘要：`EPLAN 2026.0.3.25421 (Pro Panel) connected, process 12345.` 或 `EPLAN not connected: <原因>.`
- 结果很小，不分页。

## 4. 错误
| 错误码 | 什么时候发生 | 返回给模型的提示 |
|---|---|---|
| `BRIDGE_INCOMPATIBLE` | 插件与 Host 的协议版本不兼容 | 不报错，`connected = false`，`hint`："The EplanMcp add-in in EPLAN is a different version. Run 'eplanmcp install-addin' and restart EPLAN." |
| `INTERNAL` | 本项目缺陷 | 附关联 ID，请工程师提交 Issue |

EPLAN 未运行、插件未加载、有多个实例都**不是错误**，体现在 `connected` 和 `hint` 中。

已连接时 `hint` 通常为 null；EPLAN 版本未经实测时，`hint` 说明这一点，摘要行末尾加 "See 'hint'."（ADR-0009）：
- 在支持范围 2024–2027 内、但尚未实测（目前已实测 2026）："EPLAN <版本> is in the supported range (2024-2027) but EplanMcp has not been tested with it yet; …"；
- 不在支持范围内："EPLAN <版本> is outside the versions EplanMcp supports (2024-2027); tools may fail. …"。

## 5. 实现要点
- 只需要发现和握手，不调用 `invoke`，因此**不进入 EPLAN 主线程**，EPLAN 忙或有模态对话框时也能立即返回。
- 握手结果中的版本和变体由 Bridge 在 `OnInitGui` 时一次性取得（设计文档 4.4）。

## 6. 写操作专项
不适用。

## 7. 测试
- Small：没有实例、一个实例、多个实例、协议不兼容时的输出和摘要。
- Medium：Host + 假 Bridge（真实管道），以及 Host 进程的 stdio 端到端。
- Large：EPLAN 运行 / 未运行 / 插件未注册三种状态。
- Agent 评测：M2 加入"EPLAN 没有启动时，代理应告诉工程师启动 EPLAN，而不是反复重试"。

# 工具规格：`eplan_session_select`

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_session_select` |
| 所属分组 / Profile | `session` / `core` |
| 安全级别 | Read |
| MCP 注解 | `readOnlyHint` = true　`destructiveHint` = false　`idempotentHint` = true　`openWorldHint` = false |
| 状态 | Proposed（M2） |

## 1. 这个工具解决什么任务
工程师同时开着两个 EPLAN（例如 Electric P8 和 Pro Panel，项目负责人两个变体都用），其他工具会返回 `SESSION_NOT_SELECTED`。代理用 `eplan_session_status` 看到各实例后，用这个工具选定要操作的那一个。

## 2. 输入参数
| 参数 | 类型 | 必填 | 默认值 | 说明（写给模型看的） |
|---|---|---|---|---|
| `process_id` | integer | 是 | — | 要操作的 EPLAN 进程号，取自 `eplan_session_status` 的 `instances` |

## 3. 输出
```json
{ "selected": { "processId": 24688, "version": "2026.0.3.25421", "variant": "Electric P8" } }
```
- 选择保存在 Host 进程中（stdio 下每个客户端一个 Host），对之后的所有调用生效；所选的 EPLAN 退出后自动清除，再次出现多个实例时重新返回 `SESSION_NOT_SELECTED`。
- `eplan_session_status` 的结果中标出当前选中的实例。
- 一行摘要：`Selected EPLAN 2026.0.3.25421 (Electric P8), process 24688.`（版本和变体不是项目数据）。

## 4. 错误
| 错误码 | 什么时候发生 | 返回给模型的提示 |
|---|---|---|
| `INVALID_ARGUMENT` | 没有这个进程号的、已加载插件的 EPLAN | `metadata.processIds` 列出可选的进程号 |
| `EPLAN_NOT_CONNECTED` | 一个 EPLAN 都没有 | 同 `eplan_project_list` |
| `BRIDGE_INCOMPATIBLE` | 所选实例的插件版本不兼容 | 同 `eplan_project_list` |

## 5. 实现要点
- 只改变 Host 的连接目标，不改变 EPLAN 或项目，所以注解为只读、幂等。
- `BridgeClient`：发现多个实例时，若已有选择且该进程仍在，就连接它；否则返回 `SESSION_NOT_SELECTED`。选择后立即握手，确认可以连接。
- 只有一个实例时，选择不是必需的，但调用也成功。

## 6. 写操作专项
不适用。

## 7. 测试
- Small：参数校验、未知进程号列出候选。
- Medium：假发现器提供两个实例，未选择时其他工具返回 `SESSION_NOT_SELECTED`，选择后连接所选实例；所选实例消失后选择被清除。
- Large：同时启动 Electric P8 和 Pro Panel，分别选择后 `eplan_session_status` 显示对应的变体。
- Agent 评测："我开了两个 EPLAN，请看 Pro Panel 里的项目。"

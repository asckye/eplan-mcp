# 工具规格：`eplan_connection_route`

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_connection_route` |
| 所属分组 / Profile | `layout` / `full`（新工具默认只在 `full` 中） |
| 安全级别 | Write（项目）：scratch 内直接执行，scratch 外每次审批 |
| MCP 注解 | `readOnlyHint` = false　`destructiveHint` = false　`idempotentHint` = true　`openWorldHint` = false |
| 状态 | Implemented (v0.6.0) |

## 1. 这个工具解决什么任务

Pro Panel 的连接长度来自 3D 布线：把器件、导轨、线槽摆进安装空间后，EPLAN 沿布线网络算出每根连接的实际路径，原理图上的连接长度（块值 `20201[10]`，即 31003）取自这个结果（[数据关系 R5](../eplan/data-relationships.md)）。16 页精确重绘里有 65 个连接定义点的长度就差在这一步。本工具对一个安装空间里的 3D 连接执行布线，相当于工程师在 EPLAN 里点"布线"，并报告每根连接的长度。

## 2. 输入参数

| 参数 | 类型 | 必填 | 默认值 | 说明（写给模型看的） |
|---|---|---|---|---|
| `project` | string | 是 | — | 项目名或路径 |
| `space` | string | 是 | — | 安装空间代号，不带 `+`，如 `A1`（与 `eplan_layout_get` 的 `designation` 相同） |
| `routing` | string，`network` 或 `direct` | 否 | `network` | `network` 沿布线网络（线槽和布线段），`direct` 走最短直线 |
| `devices` | string[]，0–200 个 | 否 | 全部 | 只布与这些设备相连的连接 |
| `dry_run` | boolean | 否 | true | 省略或 true 只预览；false 执行（ADR-0049） |

## 3. 输出

- `connections`：要布线的连接数；`routed`：布上的；`unrouted`：布不上的，逐根列出两端（最多 50 根），通常是两端器件还没放进 3D，或布线网络不通。
- `lengths`：每根连接的两端和长度（毫米，按布线折线计算），按长度降序，受结果预算截断，`truncated` 说明。
- `execution`、`fingerprint`、`hint`：同其他写工具。

## 4. 错误

| 错误码 | 什么时候发生 | 返回给模型的提示 |
|---|---|---|
| `INVALID_ARGUMENT` | `space` 不是项目里的安装空间（列出已有的），`routing` 取值不对，`devices` 超过 200 个或有未知设备 | `metadata.violations` |
| `POLICY_DENIED` / `APPROVAL_*` | 同其他写工具 | — |
| `EPLAN_OPERATION_FAILED` | 布线抛错；事务回滚，什么也没改 | EPLAN 的消息在 `metadata.eplanMessage` |
| `OUTCOME_UNKNOWN` | 布线中断，结果无法读回 | 不要重试，请工程师在 EPLAN 中检查 |

## 5. 实现要点

- **EPLAN 调用**：`project.InstallationSpaces` 按 36452 选空间；`space.Connection3Ds` 取连接；`ConnectionService3D.RouteConnections(List<Connection3D>, RoutingType)`（`RoutingWithNetwork` = 1、`RoutingWithoutNetwork` = 2）。已实测这条调用是确定的：样例清除后重新布线，508 根里 505 根长度不变（[A141](../eplan/api-notes/drawing-and-layout.md)）。
- 端点读取遵守 A141：端点是连接点位置时不读 `StartPin`，改用 `StartConnectionPointPosition` 的 `DeviceTag` 和 `Index`。
- 写入包在一个事务里（`TransactionRunner`，撤销描述 `AI: eplan_connection_route`）；失败回滚。一步撤销未实机验证。连接两端不全在 3D 里的连接布不上，结果里如实列出，不回滚。
- **待验证**：放线槽时布线段是否自动生成（否则要先调 `ConnectionService3D.CreateRoutingSegments(InstallationSpace)`）；布线后原理图连接的长度（31000/31003 或块值）是否随之更新——第二项决定 16 页对比能否直接看到效果。2026-11-14 第一次在重画项目副本上摆放时，配置的部件库里没有 Rittal 的机柜、线槽、导轨部件，机柜没有放上，两项都还没有答案。
- 预览只统计要布线的连接和两端是否都已放进 3D，不调用布线。
- 测试：Small 覆盖参数检查和结果整形（假适配器）；Large 在重画项目的 scratch 副本上放好 A1 后布线，长度与样例逐根比对。

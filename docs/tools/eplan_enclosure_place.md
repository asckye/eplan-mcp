# 工具规格：`eplan_enclosure_place`

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_enclosure_place` |
| 所属分组 / Profile | `layout` / `full`（当前注册在 `full` 中，见[工具索引](README.md)） |
| 安全级别 | Write |
| MCP 注解 | `readOnlyHint` = false　`destructiveHint` = false　`idempotentHint` = false　`openWorldHint` = false |
| 状态 | Implemented (Unreleased) |

## 1. 这个工具解决什么任务
"在安装空间 UC1 里放一个 Rittal AE 1055.500 柜体。"代理画 3D 安装布置的第一步：按部件放柜体，拿到安装板和门的可用尺寸，接着在上面放导轨、线槽和设备。设计见 [M4b 第一批](../design/2026-11-12-m4b-layout3d.md)，实测事实见 [api-notes A136](../eplan/api-notes.md)。

## 2. 输入参数
| 参数 | 类型 | 必填 | 默认值 | 说明（写给模型看的） |
|---|---|---|---|---|
| `project` | string | 是 | — | 项目名或 `eplan_project_list` 返回的 `path` |
| `space` | string | 是 | — | 安装空间名称，如 `UC1`；开头的 `+` 会去掉；项目里没有就新建 |
| `part` | string | 是 | — | 柜体部件号，如 `RIT.1055500`，要带 3D 宏 |
| `variant` | string | 否 | `1` | 部件变体 |
| `dry_run` | boolean | 否 | true | 省略或 true 只预览；false 执行 |

## 3. 输出
预览返回 `changes`（要新建的安装空间、柜体及其部件和变体）；执行后返回读回的 `enclosure`（`id`、`parts`、`box`、`panel`、`door`，字段同 `eplan_layout_get`）、`space`、`spaceCreated` 和撤销步骤 `undo`。

## 4. EPLAN 调用路线
`layout.enclosure.place.plan` / `.execute`（`LayoutEnclosurePlace*Operation`）→ `EplanLayoutWriter.PlaceEnclosure`：
1. 按 36452 找安装空间，没有就 `new InstallationSpace().Create(project, space)`；
2. `new Cabinet().Create(space, part, variant)`；
3. `cabinet.SetParent(space, false)`：不挂到空间下，柜体是“未放置”的（A136）。

读回用 `EplanLayoutReader`：同一空间里必须有这个 ID、这个部件、带安装板的柜体，否则整个事务回滚。一个事务，一个撤销步骤；部件存在性用 `IPartsDatabase` 检查。

## 5. 错误
`INVALID_ARGUMENT`、`PART_NOT_FOUND`、`ALREADY_EXISTS`（空间里已有柜体）、`EPLAN_OPERATION_FAILED`（读回不符，已回滚）、`PLAN_STALE`、审批类错误和通用错误，见 Usage。

## 6. 测试
- Small：`LayoutEnclosurePlaceTests`（新空间的计划和执行、已有柜体、参数和部件错误、读回无安装板时回滚、计划过期）。
- Large：待做（M4b 第一批），在 LargeWrite 副本上放 RIT.1055500，用 `eplan_layout_get` 读回安装板 749×570，一步撤销后读回为空。

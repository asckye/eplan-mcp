# 工具规格：`eplan_layout_remove`

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_layout_remove` |
| 所属分组 / Profile | `layout` / `full`（当前注册在 `full` 中，见[工具索引](README.md)） |
| 安全级别 | Write |
| MCP 注解 | `readOnlyHint` = false　`destructiveHint` = true　`idempotentHint` = false　`openWorldHint` = false |
| 状态 | Implemented (Unreleased) |

## 1. 这个工具解决什么任务
"把第二排导轨连同上面的 PLC 删掉，重新排。"布置要反复调整：代理用这个工具按对象 ID 删除已放的导轨、线槽和设备，设备留在原理图里，回到 `eplan_layout_get` 的 `unplaced`，可以再用 `eplan_layout_place` 放。设计见 [M4b 第一批](../design/2026-11-12-m4b-layout3d.md)，实测事实见 [api-notes A136](../eplan/api-notes.md)。

## 2. 输入参数
| 参数 | 类型 | 必填 | 默认值 | 说明（写给模型看的） |
|---|---|---|---|---|
| `project` | string | 是 | — | 项目名或 `eplan_project_list` 返回的 `path` |
| `space` | string | 是 | — | 安装空间名称，如 `UC1` |
| `ids` | integer[] | 是 | — | 1–200 个对象 ID（`eplan_layout_get` 的 `objects[].id`）；删导轨时，导轨上的设备也要列出 |
| `dry_run` | boolean | 否 | true | 省略或 true 只预览；false 执行 |

## 3. 输出
预览返回每个对象一条 `changes`（`delete`、类型、tag 或 ID、部件）；执行后返回 `removed`（原 `id`、`kind`、`tag`、`parts`）和撤销步骤 `undo`。

## 4. EPLAN 调用路线
`layout.remove.plan` / `.execute`（`LayoutRemove*Operation`）→ `EplanLayoutRemover.Remove`：按 ID 找 `Function3D`，先删设备再删导轨和线槽，`Function3D.Remove()`。只允许 `MountingRail`、`Duct` 和有 tag 的 `Component`；柜体及其安装板、门等部件拒绝。

一个事务，一个撤销步骤。执行前按指纹核对布置没有变化；执行后读回每个 ID 都不在布置里、设备名也不在 3D 名单里，否则整个事务回滚。

## 5. 错误
`INVALID_ARGUMENT`（ID 不存在、属于柜体、重复、导轨上的设备没列出；`metadata.violations` 一次列全）、`EPLAN_OPERATION_FAILED`（读回仍在，已回滚）、`PLAN_STALE`、审批类错误和通用错误，见 Usage。

## 6. 测试
- Small：`LayoutRemoveTests`（导轨连同设备按先设备后导轨删除；单删导轨、柜体部件、未知和重复 ID 的拒绝；读回仍在时回滚；布置变化后计划过期）。
- 实机：2026-11-12 在 `自动喂丝机-试排` 副本上，单删导轨被拒；导轨连同 3 个 PLC 设备和一台变频器删除后都回到 `unplaced`，变频器随后原位放回。Large 待做。

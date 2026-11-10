# 工具规格：`eplan_shield_create`

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_shield_create` |
| 所属分组 / Profile | `cable` / `full` |
| 安全级别 | Write |
| MCP 注解 | `readOnlyHint` = false　`destructiveHint` = false　`idempotentHint` = false　`openWorldHint` = false |
| 状态 | Implemented (v0.5.1) |

## 1. 这个工具解决什么任务
在已有连接上画屏蔽，对应 EPLAN"插入"菜单的"屏蔽"。

## 2. 输入参数
| 参数 | 类型 | 必填 | 默认值 | 说明（写给模型看的） |
|---|---|---|---|---|
| `project` | string | 是 | — | 项目名，或 `eplan_project_list` 返回的 `path` |
| `page` | string | 是 | — | 完整页名 |
| `x`、`y` | number | 是 | — | 起始角。EPLAN 按它选符号变体，也就决定了屏蔽连接点在哪一侧 |
| `corner_x`、`corner_y` | number | 是 | — | 对角，范围不能为空 |
| `name` | string | 否 | 不命名 | 标识；以 `-` 开头时加上页的结构 |
| `symbol` | string | 否 | `SH` | SPECIAL 库的屏蔽符号 |
| `wire_points` | array of [x, y]，1–200 项 | 否 | — | 每条要屏蔽的线上取一点，必须在范围内。工具在这些点上放连接定义点 |
| `dry_run` | boolean | 否 | true | 省略或 true 只预览；执行须显式 false |

## 3. 输出
`{project, execution, dryRun, fingerprint, changes, page, name, symbol, variant, bounds, connections, undo, warning, recovered, hint}`。`connections` 是屏蔽覆盖的连接，没有覆盖任何连接时 `warning` 会说明。

## 4. 错误
| reason | 情形 | execution |
|---|---|---|
| INVALID_ARGUMENT | 坐标、标识或符号不对 | not_started |
| PAGE_NOT_FOUND | 没有这一页 | not_started |
| PLAN_STALE | 预览之后这一页变了，重新预览 | not_started |
| EPLAN_OPERATION_FAILED | EPLAN 拒绝，或某个点不在线上 | rolled_back |

## 5. 实现要点
只新增一个对象，一次撤销可恢复，所以是 Write。Bridge 用 `Shield.Create(page, Symbol, start, end)`；带 `SymbolVariant` 的重载不能再创建屏蔽（官方 XML）。EPLAN 的命令会在范围内每条连接上放连接定义点，屏蔽靠它们关联连接；API 不会自动放，只重新生成或重建连接也不会关联。所以工具在屏蔽之后放定义点（`SPECIAL/CDP`），同一事务里调 `Generate.Connections`，提交之后读 `ShieldedConnections`，并确认每个定义点都在连接上，否则撤销整个调用（api-notes A112）。

## 6. 测试
- Small：`ShieldCreateOperationTests`。
- Large：`ProjectToolsTests` 在 LargeWrite 新页上画两条连接，屏蔽读回两条连接，一次撤销恢复；不在线上的点整个撤销。

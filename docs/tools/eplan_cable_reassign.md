# 工具规格：`eplan_cable_reassign`

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_cable_reassign` |
| 所属分组 / Profile | `cable` / `full` |
| 安全级别 | Destructive |
| MCP 注解 | `readOnlyHint` = false　`destructiveHint` = true　`idempotentHint` = true　`openWorldHint` = false |
| 状态 | Implemented (v0.5.1) |

## 1. 这个工具解决什么任务
把电缆的连接分配到部件的芯线上，对应 EPLAN 的"全部重新分配"。不分配时，电缆图表把芯线和连接分成两段列出，"空闲芯线"也不对。

## 2. 输入参数
| 参数 | 类型 | 必填 | 默认值 | 说明（写给模型看的） |
|---|---|---|---|---|
| `project` | string | 是 | — | 项目名，或 `eplan_project_list` 返回的 `path` |
| `cables` | array，1–200 项 | 否 | 全部电缆 | 完整标识，来自 `eplan_device_list` 的 `Cable` 类别 |
| `overwrite` | boolean | 否 | false | true 时 EPLAN 可以改连接已有的属性 |
| `dry_run` | boolean | 否 | true | 省略或 true 只预览；执行须显式 false |

## 3. 输出
`{project, execution, dryRun, fingerprint, scope, changed, changedCount, undo, backup, recovered, hint}`。分配结果只有 EPLAN 知道，预览只给范围。`changed` 最多列 200 项，每项 `{cable, connection, previous, core}`。

## 4. 错误
| reason | 情形 | execution |
|---|---|---|
| INVALID_ARGUMENT | 标识不是项目里的电缆 | not_started |
| PLAN_STALE | 预览之后连接变了，重新预览 | not_started |
| APPROVAL_DENIED | 工程师拒绝 | not_started |
| BACKUP_FAILED | 备份失败，没有改动 | not_started |
| EPLAN_OPERATION_FAILED | EPLAN 拒绝 | rolled_back |

## 5. 实现要点
一次改很多连接，和 `eplan_cable_renumber` 一样是 Destructive。Bridge 用 `CableService.DoReassignWires`：全部电缆用项目重载，指定电缆时逐个用功能重载。官方说明：连接和芯线模板的电位类型相同才会分配，模板的识别属性会复制到连接上。分配后，连接代号（31011）和芯线的颜色或编号（31004）完全相同的，清掉代号：两者都会显示在连接定义点上，图上同一根芯会出现两个一样的标签（2026-11-22 重画，每个电缆页多一行“1 2 3 GNYE”）。连接定义点上的代号不能直接设置（S063113），所以在连接上清。前后读 `Cable.CableConnections` 的芯线代号、颜色、截面对比。整个调用一次撤销。

## 6. 测试
- Small：`CableReassignOperationTests`。
- Large：`ProjectToolsTests` 在 LargeWrite 上分配一根电缆，一次撤销恢复。

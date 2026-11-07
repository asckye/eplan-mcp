# 工具规格：`eplan_masterdata_update`

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_masterdata_update` |
| 所属分组 / Profile | `project` / `full` |
| 安全级别 | Destructive |
| MCP 注解 | `readOnlyHint` = false　`destructiveHint` = true　`idempotentHint` = true　`openWorldHint` = false |
| 状态 | Implemented (Unreleased) |

## 1. 这个工具解决什么任务
用系统主数据更新项目里存的表格、图框和符号，对应 EPLAN 的"更新项目主数据"。从模板新建的项目存的是模板当时的版本，报表用词可能和系统表格不同；`eplan_report_create` 不会替换项目里已有的表格。

## 2. 输入参数
| 参数 | 类型 | 必填 | 默认值 | 说明（写给模型看的） |
|---|---|---|---|---|
| `project` | string | 是 | — | 项目名，或 `eplan_project_list` 返回的 `path` |
| `dry_run` | boolean | 否 | true | 省略或 true 只预览；执行须显式 false |

## 3. 输出
`{project, execution, dryRun, fingerprint, scope, entries, added, backup, recovered, hint}`。哪些文件和系统不同只有 EPLAN 知道，预览只给项目现存文件数。`entries` 是更新后的文件数，`added` 是原来没有的文件。

## 4. 错误
| reason | 情形 | execution |
|---|---|---|
| INVALID_ARGUMENT | 没有这个打开的项目 | not_started |
| PLAN_STALE | 预览之后项目存的文件变了，重新预览 | not_started |
| APPROVAL_DENIED | 工程师拒绝 | not_started |
| BACKUP_FAILED | 备份失败，没有改动 | not_started |
| OUTCOME_UNKNOWN | EPLAN 中途报错，部分文件可能已更新；不要重复，备份里是原样 | unknown |

## 5. 实现要点
一次替换很多文件，又没有撤销，按 ADR-0005 是 Destructive。Bridge 在 `QuietModeStep(ShowNoDialogs)` 里调 `Masterdata.UpdateProject`，不开事务：没有证据表明 EPLAN 的回滚和撤销覆盖这些文件，所以失败时报 OUTCOME_UNKNOWN，靠备份恢复。系统主数据只读取。

## 6. 测试
- Small：`MasterDataUpdateOperationTests`。
- Large：`ProjectToolsTests` 在 LargeWrite 上更新一次，不弹对话框，项目仍可读。

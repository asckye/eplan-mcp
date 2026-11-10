# 工具规格：`eplan_structure_rename`

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_structure_rename` |
| 所属分组 / Profile | `structure` / `full` |
| 安全级别 | Destructive |
| MCP 注解 | `readOnlyHint` = false　`destructiveHint` = true　`idempotentHint` = true　`openWorldHint` = false |
| 状态 | Implemented (v0.5.1) |

## 1. 这个工具解决什么任务
给结构标识符改名，比如把 `+S1` 改成 `+CAB1`。用到它的页和设备会一起变。

## 2. 输入参数
| 参数 | 类型 | 必填 | 默认值 | 说明（写给模型看的） |
|---|---|---|---|---|
| `project` | string | 是 | — | 项目名，或 `eplan_project_list` 返回的 `path` |
| `renames` | array，1–50 项 | 是 | — | 每项 `{name, new_name}`，都是带前缀的完整名称；新名称在同一块、同一父级下，且不能已存在 |
| `dry_run` | boolean | 否 | true | 省略或 true 只预览；执行须显式 false |

## 3. 输出
`{project, execution, dryRun, fingerprint, changes, undo, backup, recovered, hint}`。`changes` 每项一个 `update`，属性为 `name`。

## 4. 错误
| reason | 情形 | execution |
|---|---|---|
| INVALID_ARGUMENT | 名称格式不对，旧名称不存在，新名称已存在，或换了块或父级 | not_started |
| PLAN_STALE | 预览之后标识变了，重新预览 | not_started |
| APPROVAL_DENIED | 工程师拒绝 | not_started |
| BACKUP_FAILED | 备份失败，没有改动 | not_started |
| EPLAN_OPERATION_FAILED | EPLAN 拒绝，或读回不一致；`metadata.identifier` 指出是哪个 | rolled_back |

## 5. 实现要点
改名会牵动所有用到它的对象，按 ADR-0005 属于"大范围修改"，所以是 Destructive：每次审批，执行前备份。Bridge 用 `Project.ChangeLocationName(Location, 名称)`，名称只传最后一段，因为 XML 说明含 `.` 会抛 `IncorrectNameFormatException`；调用后 `Location` 对象失效，所以每次重新查找。整个调用一次撤销。

## 6. 测试
- Small：`StructureOperationTests`。
- Large：`ProjectToolsTests` 给一页用到的标识改名，核对页名，一次撤销恢复。

# 工具规格：`eplan_structure_delete`

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_structure_delete` |
| 所属分组 / Profile | `structure` / `full` |
| 安全级别 | Destructive |
| MCP 注解 | `readOnlyHint` = false　`destructiveHint` = true　`idempotentHint` = true　`openWorldHint` = false |
| 状态 | Implemented (Unreleased) |

## 1. 这个工具解决什么任务
清除未使用的结构标识符。典型场景是模板带来的标识：页删掉之后它们还留在结构总览里。

## 2. 输入参数
| 参数 | 类型 | 必填 | 默认值 | 说明（写给模型看的） |
|---|---|---|---|---|
| `project` | string | 是 | — | 项目名，或 `eplan_project_list` 返回的 `path` |
| `identifiers` | array，1–200 项 | 否 | 全部未使用的 | 带前缀的完整名称，如 `+S1` |
| `dry_run` | boolean | 否 | true | 省略或 true 只预览；执行须显式 false |

## 3. 输出
`{project, execution, dryRun, fingerprint, changes, undo, backup, recovered, hint}`。`changes` 每项一个 `delete`，`oldValue` 是原描述；`backup` 是执行前的项目副本。

## 4. 错误
| reason | 情形 | execution |
|---|---|---|
| INVALID_ARGUMENT | 名称不在项目中，或正在使用 | not_started |
| PLAN_STALE | 预览之后标识变了，重新预览 | not_started |
| APPROVAL_DENIED | 工程师拒绝 | not_started |
| BACKUP_FAILED | 备份失败，没有改动 | not_started |
| EPLAN_OPERATION_FAILED | EPLAN 拒绝或仍保留；`metadata.identifier` 指出是哪个 | rolled_back |

## 5. 实现要点
按 ADR-0005 属于"删除数据"，所以是 Destructive：每次都要审批（scratch 内也一样），执行前备份。Bridge 用 `Location.Remove()`，只删 `IsUsed` 为 false 的标识，全部删除后读回，还在就全部回滚。整个调用一次撤销。

有子标识的父标识算作已使用（A106），要先删子标识，再用第二次调用删父标识。撤销删除之后，再往前撤销一步可能弹"'撤销'冲突"（A106）。

## 6. 测试
- Small：`StructureOperationTests` 覆盖未传名称取全部未使用、正在使用或不存在的拒绝、EPLAN 保留时回滚、先备份。
- Large：`StructureToolsTests` 在 LargeWrite 上审批删除，一次撤销恢复。

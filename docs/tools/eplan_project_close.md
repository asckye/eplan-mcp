# 工具规格：`eplan_project_close`

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_project_close` |
| 所属分组 / Profile | `project` / `full` |
| 安全级别 | Write |
| MCP 注解 | `readOnlyHint` = false　`destructiveHint` = false　`idempotentHint` = false　`openWorldHint` = false |
| 状态 | Implemented (v0.5.1) |

## 1. 这个工具解决什么任务
做完一个项目后把它关掉，再打开下一个。提交过的修改已经存盘，不需要先保存。

## 2. 输入参数
| 参数 | 类型 | 必填 | 默认值 | 说明（写给模型看的） |
|---|---|---|---|---|
| `project` | string | 是 | — | 项目名，或 `eplan_project_list` 返回的 `path` |
| `dry_run` | boolean | 否 | true | 省略或 true 只预览；执行须显式 false |

## 3. 输出
`{project, path, execution, dryRun, fingerprint, current, recovered, hint}`。

## 4. 错误
| reason | 情形 | execution |
|---|---|---|
| PROJECT_NOT_OPEN | 项目没有打开 | not_started |
| POLICY_DENIED / APPROVAL_DENIED | scratch 外被策略禁止，或工程师拒绝 | not_started |
| EPLAN_OPERATION_FAILED | EPLAN 拒绝，或项目仍然打开 | not_started |

## 5. 实现要点
走 `WritePipeline`。Bridge 每次重新查找项目对象再 `Project.Close()`，不保留对象（A34）。关闭后该项目的撤销步骤不再可用。

## 6. 测试
- Small：`ProjectSessionAndPlcTests`。
- Large：`ProjectToolsTests`。

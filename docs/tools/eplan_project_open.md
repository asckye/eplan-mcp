# 工具规格：`eplan_project_open`

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_project_open` |
| 所属分组 / Profile | `project` / `full` |
| 安全级别 | Write |
| MCP 注解 | `readOnlyHint` = false　`destructiveHint` = false　`idempotentHint` = true　`openWorldHint` = false |
| 状态 | Implemented (Unreleased) |

## 1. 这个工具解决什么任务
`eplan_project_create` 建好的项目是关闭的，以前要靠工具之外的 `ProjectOpen` 才能打开。这个工具让一个项目从创建到导出都不离开 MCP。ADR-0021 没有把 `ProjectOpen` action 放进白名单，理由是路径可以指向任意文件，并写明需要时做任务工具；这里按写入策略限制路径。

## 2. 输入参数
| 参数 | 类型 | 必填 | 默认值 | 说明（写给模型看的） |
|---|---|---|---|---|
| `path` | string | 是 | — | `.elk` 文件的完整本地路径，不接受网络路径；也接受 `eplan_project_list` 返回的不带扩展名的项目路径（旁边有同名 `.elk` 时） |
| `dry_run` | boolean | 否 | true | 省略或 true 只预览；执行须显式 false |

## 3. 输出
`{project, path, execution, dryRun, fingerprint, current, recovered, hint}`。`path` 不带扩展名，可以直接作为其他工具的 `project`。已经打开时 `execution` 为 `not_started`。

## 4. 错误
| reason | 情形 | execution |
|---|---|---|
| INVALID_ARGUMENT | 不是本地 `.elk` 路径（规划时只查文字：盘符开头、无控制字符、不是设备名）；文件不存在（审批之后执行时才查，审批前不访问路径，WS-29） | not_started |
| POLICY_DENIED / APPROVAL_DENIED | scratch 外被策略禁止，或工程师拒绝 | not_started |
| PLAN_STALE | 预览之后项目被打开或关闭 | not_started |
| EPLAN_OPERATION_FAILED | EPLAN 拒绝打开，比如旧版本项目；不自动升级 | not_started |

## 5. 实现要点
走 `WritePipeline`：scratch 内自动执行，scratch 外按写入策略处理。Bridge 在 `QuietModeStep(ShowNoDialogs)` 内用 `ProjectManager.OpenProject(路径, OpenMode.Standard)` 打开（和工程师在 EPLAN 里打开一样；2026-11-09 前用 `Exclusive`，项目名旁显示“专有”，负责人要求去掉，A128）。只有没有其他打开的项目时 EPLAN 才把它设为当前项目，`current` 如实返回；工具按路径找项目，不受影响，但 Ctrl+Z 只作用于当前项目（A41）。不主动选中：`SelectProjectInPagesNavigator` 之后，再用 `ProjectOpen` 打开的项目不会成为当前项目（A107）。没有事务，也不进撤销列表。

## 6. 测试
- Small：`ProjectSessionAndPlcTests`。
- Large：`ProjectToolsTests` 关闭再打开 LargeWrite，核对页数、选中状态和之后的写入与撤销，scratch 外被拒绝。

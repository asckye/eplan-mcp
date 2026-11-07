# 工具规格：`eplan_report_configure`

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_report_configure` |
| 所属分组 / Profile | `report` / `full` |
| 安全级别 | Write |
| MCP 注解 | `readOnlyHint` = false　`destructiveHint` = false　`idempotentHint` = true　`openWorldHint` = false |
| 状态 | Implemented (Unreleased) |

## 1. 这个工具解决什么任务
决定部件列表怎么列。比如 71 页图纸的部件列表每个端子一行，而 EPLAN 默认把同标识、同部件号的设备合并成一行，页数和内容都对不上。

## 2. 输入参数
| 参数 | 类型 | 必填 | 默认值 | 说明（写给模型看的） |
|---|---|---|---|---|
| `project` | string | 是 | — | 项目名，或 `eplan_project_list` 返回的 `path` |
| `summarize_parts_of_same_device` | boolean | 否 | 不改 | true 合并同标识、同部件号的设备，false 逐个列出 |
| `dissect_assemblies` | boolean | 否 | 不改 | 列出部件组内的部件 |
| `include_terminal_strip_parts` | boolean | 否 | 不改 | 列出端子排定义上的部件，比如端支架 |
| `include_plug_strip_parts` | boolean | 否 | 不改 | 列出插头排定义上的部件 |
| `dry_run` | boolean | 否 | true | 省略或 true 只预览；执行须显式 false |

## 3. 输出
`{project, execution, dryRun, fingerprint, changes, recovered, hint}`。`changes` 只列会变的设置。

## 4. 错误
| reason | 情形 | execution |
|---|---|---|
| INVALID_ARGUMENT | 一个设置都没给 | not_started |
| PLAN_STALE | 预览之后设置变了，重新预览 | not_started |
| EPLAN_OPERATION_FAILED | EPLAN 拒绝，或读回不一致，已恢复旧值 | rolled_back |

## 5. 实现要点
走 `WritePipeline`，scratch 内自动执行。设置名来自 `Cfg/PartsListGui.xml` 的 PROJECT 类别，只开放上面 4 个，契约里是封闭清单。用 `ProjectSettings` 读写，和语言设置相同。项目设置不被 `SafetyPoint` 回滚（A28），也不进撤销列表，所以读回不符时由 Bridge 写回旧值。

## 6. 测试
- Small：`ReportSettingsOperationTests`。
- Large：`ProjectToolsTests` 在 LargeWrite 上改设置、读回，再改回。

# 工具规格：`eplan_plc_generate`

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_plc_generate` |
| 所属分组 / Profile | `plc` / `full` |
| 安全级别 | Write |
| MCP 注解 | `readOnlyHint` = false　`destructiveHint` = false　`idempotentHint` = false　`openWorldHint` = false |
| 状态 | Implemented (Unreleased) |

## 1. 这个工具解决什么任务
从项目里已有的 PLC 设备生成原理图页，对应 EPLAN 的"生成 PLC 原理图"。EPLAN 自己建页、放宏，功能并入已有设备，不用再改名合并。

## 2. 输入参数
| 参数 | 类型 | 必填 | 默认值 | 说明（写给模型看的） |
|---|---|---|---|---|
| `project` | string | 是 | — | 项目名，或 `eplan_project_list` 返回的 `path` |
| `devices` | array，1–50 项 | 是 | — | 每项 `{tag, macro?, single_line_page?, multi_line_page?, overview_page?, rack_page?}` |
| `dry_run` | boolean | 否 | true | 省略或 true 只预览；执行须显式 false |

- `tag`：完整标识，来自 `eplan_device_list` 的 `PLCBox` 类别。设备必须已在项目里。
- 给了哪个页，就生成哪种表达，至少给一个。每页必须是新页，且只用一次。
- `macro`：宏目录下的路径；省略时用 EPLAN 从部件得到的宏。

## 3. 输出
`{project, execution, dryRun, fingerprint, changes, pages, undo, warning, recovered, hint}`。`changes` 列出每个新页及其设备和宏；`pages` 每项 `{name, type, functions, functionCount}`，`functions` 最多 200 项。

## 4. 错误
| reason | 情形 | execution |
|---|---|---|
| INVALID_ARGUMENT | 不是 PLC 设备、没有宏、页已存在或重复 | not_started |
| MACRO_NOT_FOUND | 宏文件不存在 | not_started |
| PLAN_STALE | 预览之后页或宏变了，重新预览 | not_started |
| EPLAN_OPERATION_FAILED | EPLAN 报错或没有生成任何页 | rolled_back |

## 5. 实现要点
只放到新页，不覆盖已有内容，所以是 Write。Bridge 先用 `PlcService.ExportGenerateSchematicConfiguration` 导出项目自己的配置，删掉没要求的设备，给每个功能写上页名，再调 `PlcService.GenerateSchematic`。配置里有不存在的设备时 EPLAN 会弹模态对话框，所以预先拒绝（api-notes A113）。

暂不支持通道级的接线宏和占位符变量（配置里 `Function` 的 `Macro`、`Variable`）：还没有实机验证。

## 6. 测试
- Small：`PlcGenerateOperationTests`。
- Large：`ProjectToolsTests` 在 LargeWrite 上先插总览宏，再生成多线页，功能并入设备，一次撤销恢复。

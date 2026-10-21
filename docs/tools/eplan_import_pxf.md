# 工具规格：`eplan_import_pxf`

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_import_pxf` |
| 所属分组 / Profile | `import` / `full`（M4c 的新工具默认只在 `full` 中） |
| 安全级别 | Write |
| MCP 注解 | `readOnlyHint` = false　`destructiveHint` = false　`idempotentHint` = false　`openWorldHint` = false |
| 状态 | Proposed（M4c 第三批） |

## 1. 这个工具解决什么任务
"把别人发来的这个 EPJ 打开看看"。代理把导入目录中的 EPJ（PXF）文件导入为一个新项目。新项目只建在 scratch 根目录中（M4c 设计 Q5），不改任何已有项目，所以是 Write，不需要审批；工程师需要时自己把它移走。

## 2. 输入参数
| 参数 | 类型 | 必填 | 默认值 | 说明（写给模型看的） |
|---|---|---|---|---|
| `file` | string | 是 | — | 导入目录中的相对路径，如 `incoming/Line1.epj`；扩展名只能是 `.epj`、`.pxf` |
| `name` | string，1–60 个字符 | 是 | — | 新项目的名称，只是名称，规则同 `eplan_export_pdf` 的 `file_name`；scratch 中已有同名项目时拒绝 |
| `dry_run` | boolean | 否 | false | 为 true 时只校验并返回新项目的位置，不导入 |

- 没有 `project` 参数：导入不针对已打开的项目。
- 导入目录和 `file` 的规则同 `eplan_import_dxf`；文件上限 2 GB（S25：示例项目的 EPJ 有 598 MB）。

## 3. 输出
```json
{
  "execution": "committed",
  "dryRun": false,
  "file": "incoming/Line1.epj",
  "sha256": "…",
  "project": { "name": "Line1_imported", "path": "C:\\Users\\<user>\\EplanMcp\\scratch\\imported\\Line1_imported.elk", "opened": false },
  "seconds": 58,
  "hint": "The project is not opened; open it in EPLAN to work on it."
}
```
- `project.path`：新项目的完整路径（在 scratch 中，可以给出）。导入后不自动打开（S22）。
- 一行摘要：`Imported the EPJ as a new project in scratch; it is not opened yet.`
- 工具描述写明：可能要一分钟以上（S22：598 MB 约 58 秒）。

## 4. 错误
| 错误码 | 什么时候发生 | `execution` | 返回给模型的提示 |
|---|---|---|---|
| `INVALID_ARGUMENT` | `file` 不合规、扩展名不对、超过上限；`name` 不合法 | `not_started` | `metadata.violations` 列出全部问题 |
| `FILE_NOT_FOUND` | 导入目录中没有这个文件 | `not_started` | 同 `eplan_import_dxf` |
| `ALREADY_EXISTS` | scratch 的 `imported\` 中已有同名项目 | `not_started` | 提示换一个 `name` |
| `EPLAN_OPERATION_FAILED` | EPLAN 导入时报错 | `rolled_back` | 附带 EPLAN 的系统消息；说明本次新建的文件已删除 |
| `TIMEOUT` | 超过 300 秒 | `unknown` | 说明导入可能仍在进行，稍后用 `eplan_project_list` 查看；Host 不删除可能还在写的文件 |
| 连接与授权类错误 | 同 `eplan_project_list` | — | 同 `eplan_project_list` |

## 5. 实现要点
- **Host**：解析并校验 `file`、`name`，目标为 `<第一个 scratch 根目录>\imported\<name>`；走 `WritePipeline`（scratch 中的 Write，策略直接允许），规划操作 `import.pxf.plan`，执行操作 `import.pxf.execute`，时限 300 秒，每 5 秒一次心跳。
- **执行**：`new Import().PXFProject(文件, 目标)`，**第二个参数是新项目的路径（不带扩展名），不是目录**（S22，A29，与 XML 不符）。完成后确认 `<name>.elk` 和 `<name>.edb` 存在。
- **失败时**：删除本次新建的 `<name>.elk` 和 `<name>.edb`（规划时已确认它们原来不存在，所以只删本次新建的）；中途被结束时 EPLAN 不清理不完整的项目（S22）。
- **审计**：记下文件的相对路径、SHA-256 和新项目的路径。
- **待 Large 测试确认**：导入后打开新项目核对页数；`PXFProject` 的第三个参数（同步部件，可能写系统部件库，XML）不用，确认两参数重载不同步部件。

## 6. 写操作专项
- scratch 中的 Write：不审批（ADR-0005）。没有撤销步骤，失败时按第 5 节删除。
- 不是幂等的：同样的调用再来一次，因同名返回 `ALREADY_EXISTS`。

## 7. 测试
- **Small**：`file`、`name` 的校验，目标路径的拼接，摘要，schema 快照，注解。
- **Medium**：假 Bridge 上的 `dry_run`、同名、EPLAN 报错时删除新建的文件、超时。
- **Large**：从 scratch 副本导出 PXF（`eplan_export_pxf`），复制到导入目录，再导入：新项目在 `scratch\imported\` 中，能在 EPLAN 中打开，页数与源项目相同。
- **Agent 评测**："把导入目录中的 Line1.epj 导入成新项目，告诉我它在哪里"。

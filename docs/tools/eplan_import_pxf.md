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
| `name` | string，1–60 个字符 | 是 | — | 新项目的名称，只是名称，规则同 `eplan_export_pdf` 的 `file_name`，另外不能含 `.`；scratch 中已有同名项目时拒绝 |
| `dry_run` | boolean | 否 | false | 为 true 时只校验并返回新项目的位置，不导入 |

- 没有 `project` 参数：导入不针对已打开的项目。
- `name` 不能含 `.`（实现时加）：S22 只试过不带点的名称（A29），EPLAN 是否把点后面的部分当作扩展名没有验证。
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
| `ALREADY_EXISTS` | scratch 的 `imported\` 中已有同名项目：`<name>.elk`、`<name>.ell`（修订中的项目，A28）或 `<name>.edb` | `not_started` | 提示换一个 `name`；`metadata.existing` 列出已有的文件 |
| `EPLAN_OPERATION_FAILED` | EPLAN 导入时报错，或导入结束后 `<name>.elk`、`<name>.edb` 不在 | `rolled_back` | 附带 EPLAN 的消息（`metadata.eplanMessage`，去掉路径）；说明本次新建的文件已删除 |
| `OUTCOME_UNKNOWN` | 导入失败或被取消，而本次新建的文件没能全部删除（例如 EPLAN 还占着） | `unknown` | 说明 scratch 的 `imported\` 中可能留下不完整的项目，不要用同一个名称再调用，请工程师删除（实现时加）。写入随之按新项目的路径暂停（ADR-0010） |
| `OUTCOME_UNKNOWN` | 超过 300 秒（实现时由 `TIMEOUT` 改：执行请求发出之后超时，流水线按 ADR-0010 取回结果，取不到就是 `OUTCOME_UNKNOWN`，`execution` 为 `unknown` 时只能是它，mcp-tool-design 6.2） | `unknown` | 说明导入可能仍在进行；Host 不删除可能还在写的文件 |
| 连接与授权类错误 | 同 `eplan_project_list` | — | 同 `eplan_project_list` |

## 5. 实现要点
- **Host**：用 `ImportFiles`（SEC-31）解析并校验 `file`（不超过 2 GB）、算出 SHA-256，校验 `name`；目标为 `<第一个 scratch 根目录>\imported\<name>`，`imported` 不存在时由 Host 新建，它本身不能是符号链接或联接点（否则 `POLICY_DENIED`），scratch 根目录本身可以是；走 `WritePipeline`（scratch 中的 Write，策略直接允许），规划操作 `import.pxf.plan`，执行操作 `import.pxf.execute`，时限 300 秒，每 5 秒一次心跳。
- **执行**：`new Import().PXFProject(文件, 目标)`，**第二个参数是新项目的路径（不带扩展名），不是目录**（S22，A29，与 XML 不符）。完成后确认 `<name>.elk` 和 `<name>.edb` 存在。规划和执行都检查同名的项目；文件可能有 2 GB，Bridge 不在主线程上重读，指纹为文件的大小、修改时间、Host 算出的 SHA-256 和目标路径。导入之后是最后一个安全点：期间到达的取消按失败处理，删除新项目，`execution` 为 `rolled_back`（`CANCELLED`）。
- **失败时**：删除本次新建的 `<name>.elk`（以及 `.ell`）和 `<name>.edb`，目录中的链接或联接点只删链接本身、不跟随（规划时已确认它们原来不存在，所以只删本次新建的）；中途被结束时 EPLAN 不清理不完整的项目（S22）。
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

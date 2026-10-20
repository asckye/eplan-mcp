# 工具规格：`eplan_export_pdf`

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_export_pdf` |
| 所属分组 / Profile | `export` / `core` |
| 安全级别 | Read（写文件，ADR-0012） |
| MCP 注解 | `readOnlyHint` = false　`destructiveHint` = false　`idempotentHint` = false　`openWorldHint` = false |
| 状态 | Proposed（M4 第 8 步；依赖第 5 步的导出目录，进度和取消依赖第 3、4 步） |

## 1. 这个工具解决什么任务
"把这三页导出成 PDF 给我看""整个项目出一份 PDF"。代理把页或整个项目导出为一个 PDF 文件，得到文件所在的目录，交给工程师查看或转发。导出不改动项目的设计数据（S11，A16），只在服务器新建的目录中写文件，所以是 Read 级，不需要审批；它会写文件，注解如实标为 `readOnlyHint: false`（ADR-0012）。本规格第 5 节的"导出目录"也适用于 `eplan_export_dxf`、`eplan_export_image`。

## 2. 输入参数
| 参数 | 类型 | 必填 | 默认值 | 说明（写给模型看的） |
|---|---|---|---|---|
| `project` | string | 否 | EPLAN 中选中的项目 | 项目名，或 `eplan_project_list` 返回的 `path` |
| `pages` | array of string，1–50 项 | 否 | 整个项目 | 要导出的页，完整页名如 `=GAA+A1&EFS1/60` |
| `scheme` | string | 否 | EPLAN 当前的 PDF 导出方案 | PDF 导出方案的名称 |
| `color` | enum `color` / `grey` / `black_white` | 否 | `color` | 彩色、灰度或黑白 |
| `file_name` | string，1–100 个字符 | 否 | 项目名 | 只是文件名，不含目录；扩展名 `.pdf` 由工具补上 |

- 页名的查找规则与 `eplan_page_get` 相同；同一页出现两次时返回 `INVALID_ARGUMENT`。
- `file_name` 不能含路径分隔符、驱动器号、`..`、控制字符和 `<>:"|?*`，不能以点或空格结尾，不能是 Windows 的保留名（`CON`、`NUL` 等）。已经以 `.pdf` 结尾（不区分大小写）时不再补。
- 没有目录参数，也没有语言参数（M4 设计 4.1、4.4）。

## 3. 输出
```json
{
  "project": "Line1",
  "pageCount": 3,
  "scheme": "EPLAN_default_value",
  "directory": "C:\\Users\\<user>\\EplanMcp\\exports\\Line1\\20261021T083000Z-k3f9q2\\",
  "files": [ { "path": "Line1.pdf", "bytes": 208896 } ],
  "fileCount": 1,
  "totalBytes": 208896,
  "truncated": false,
  "hint": null
}
```
- `directory`：本次新建的目录的完整路径（M4 设计 4.4），工程师在资源管理器中打开它。
- `files`：目录中的全部文件，`path` 相对于 `directory`，最多 50 个；超出时 `truncated = true`，`fileCount`、`totalBytes` 仍是全部文件的合计。
- `scheme`：使用的方案。省略时回显 EPLAN 当前方案的名称（`SchemeSetting.GetLastUsed`）。
- 页名、方案名是项目或设置中的数据，经过 ResultShaper。
- 一行摘要：`Exported 3 pages to 1 PDF file (204 KB) in a new folder; see 'directory'.`
- 结果不带 `execution`（Read 级，mcp-tool-design 6.2）。

## 4. 错误
| 错误码 | 什么时候发生 | 返回给模型的提示 |
|---|---|---|
| `INVALID_ARGUMENT` | `pages` 为空数组、超过 50 项或有重复；`file_name` 不合法；`scheme` 不存在 | `metadata.violations` 列出全部问题；方案不存在时 `metadata.candidates` 列出可用的方案（最多 20 个），提示省略 `scheme` 用当前的方案 |
| `PAGE_NOT_FOUND` / `AMBIGUOUS_NAME` | 有页名找不到，或只差大小写的页有多个 | 同 `eplan_connection_generate` |
| `EPLAN_OPERATION_FAILED` | EPLAN 导出时报错；或者导出后目录中有链接、联接点（第 5 节） | 附带 EPLAN 的系统消息；说明没有留下文件，目录已删除 |
| `TIMEOUT` | 超过 300 秒 | 提示减少页数，分几次导出 |
| `PROJECT_NOT_OPEN` / `AMBIGUOUS_NAME` | 项目解析失败 | 同 `eplan_project_get` |
| 连接与授权类错误、`INTERNAL` | 同 `eplan_project_list` | 同错误码表 |

出错和取消时都删除本次新建的目录，不返回部分结果。导出根目录无法创建或不可写时返回 `INTERNAL`，提示运行 `eplanmcp doctor`；提示中不出现本机路径（SEC-12）。

## 5. 实现要点
- **流程**（Read 级，不走 `WritePipeline`）：
  1. `export.pdf.plan`（只读）：解析项目和各页，校验方案，返回项目名、页数和使用的方案名；
  2. Host 新建导出目录（见下）；
  3. `export.pdf.run`：Bridge 确认目录存在而且为空，在 `QuietModeStep(ShowNoDialogs)` 中导出（不需要 `SafetyPoint`、`UndoStep`）；
  4. Host 核对目录中的文件，返回结果。
- **导出目录**（M4 设计 4.4，SEC-20；三个导出工具共用）：
  - 每次调用新建 `<数据根目录>\exports\<项目名>\<UTC 时间>-<随机后缀>\`：时间形如 `20261021T083000Z`，后缀是 6 个小写字母或数字；目录已存在时报错，从不复用；
  - Host 规范化路径，确认它在导出根目录之下、每一级都不是符号链接或联接点（SEC-19 的规则），再把完整路径交给 Bridge；
  - 导出后 Host 递归列出目录：每一项都必须是目录之下的普通文件或文件夹，不能是链接或联接点，否则删除目录，返回 `EPLAN_OPERATION_FAILED`；
  - 失败、超时或取消时只删除本次新建的目录。超时后 EPLAN 可能仍在写这个目录（XML：目录不存在时会新建），这样留下的文件仍在导出根目录中，`doctor` 报告导出占用的空间（M4 设计 Q8）；
  - 成功的导出不自动清理。
- **EPLAN 调用**：`new Export().PdfPages(new ArrayList(pages), scheme, Path.Combine(目录, 文件名), color, false, "", false)`：3D 模型为 false，语言为空串，不使用打印边距；整个项目时传入全部页。
  - 不用 `PdfProject`：XML 说明它只在方案没有文件名格式时才用给出的文件名，方案的子目录格式也会生效；`PdfPages` 给出带路径的文件名时，覆盖方案中的目录（XML），S11 用它导出过全部 321 页；
  - 一定传入目录中的完整文件名，从不传空串：空串时文件名和目录由方案决定（XML），文件会写到导出目录之外；
  - `color` 对应 `Export.DegreeOfColor` 的 `Color`、`GreyLevel`、`BlackAndWhite`（XML）。
- **方案**：在 `USER.PDFExportGUI.SCHEMAS`（XML）。规划阶段用 `SchemeSetting.Init`、`CheckIfSchemeExists` 校验，候选用 `GetCount`、`SetScheme(i)`、`GetName` 列出（XML：`SetScheme` 不改"上次使用"）。省略时传空串。导出前后比较 `GetLastUsed()`，被改写就用 `SetLastUsed` 写回原值（M4 设计第 5 节），写不回时放在 `hint` 中。
- **时限与进度**：时限 300 秒（M4 设计 4.1）。等待 Bridge 时每 5 秒发一次心跳进度，如 `Exporting PDF (40 s)`。一次 EPLAN 调用中途无法打断：已发出的调用被取消后，Bridge 在调用返回时报告取消，Host 再删除目录（M4 设计 4.3）。
- **已验证**（S11，A16）：`PdfPages` 3 页 0.7 秒（204 KB），全部 321 页 12.5 秒（6.1 MB）；文件只出现在给定目录中；只改写项目的 `User.eod` / `User.eox`，设计数据不变；方案传空串时"上次使用"不变；不弹对话框。
- **没有验证**，由 Large 测试确认：传入具体方案名时是否改写"上次使用"；方案中配置了文件名格式时，给出的文件名是否仍然生效。

## 6. 写操作专项
不适用（Read 级）。导出不改动项目的设计数据；以后发现会改动时，按 ADR-0012 第 3 条改为 Write 级。

## 7. 测试
- **Small**：`file_name` 的校验表（分隔符、驱动器号、`..`、UNC、保留名、结尾的点）和补扩展名；导出目录的新建、规范化、链接检查，目录中出现链接或目录外的路径时删除并报错；结果截断到 50 个文件；摘要；schema 快照；注解与 ADR-0012 一致。
- **Medium**：在假 Bridge 上测试：
  - 方案不存在时的候选；`PAGE_NOT_FOUND`；
  - Bridge 报错、超时、取消时目录被删除；心跳进度。
- **Large**（示例项目的 scratch 副本）：
  - 导出 3 页：只在本次目录中产生 1 个 PDF，项目的设计数据不变（扩展 `NoSideEffectTests`）；
  - 省略 `pages` 导出整个项目，记录耗时；
  - 用另一个已有的方案导出，比较前后的 `GetLastUsed()`；
  - `file_name` 为 `../x`、`C:x` 时返回 `INVALID_ARGUMENT`，没有新建目录。
- **Agent 评测**："把 scratch 项目的 `=GAA+A1&EFS1/1` 到 `/3` 页导出成一个 PDF，告诉我文件在哪里"，作答后核对目录中的文件。

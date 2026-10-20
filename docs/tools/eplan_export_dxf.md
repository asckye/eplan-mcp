# 工具规格：`eplan_export_dxf`

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_export_dxf` |
| 所属分组 / Profile | `export` / `full` |
| 安全级别 | Read（写文件，ADR-0012） |
| MCP 注解 | `readOnlyHint` = false　`destructiveHint` = false　`idempotentHint` = false　`openWorldHint` = false |
| 状态 | Proposed（M4 第 8 步；依赖第 5 步的导出目录，进度和取消依赖第 3、4 步） |

## 1. 这个工具解决什么任务
"把电路图导出成 DXF 给机械那边"。代理把页或整个项目导出为 DXF 或 DWG 文件（每页一个），得到文件所在的目录。DXF 还是 DWG 由 EPLAN 的导出方案决定。与 `eplan_export_pdf` 一样是 Read 级，只在服务器新建的目录中写文件。DXF 主要交给其他 CAD 软件，不是代理日常会用的，所以放在 `full` Profile。

## 2. 输入参数
| 参数 | 类型 | 必填 | 默认值 | 说明（写给模型看的） |
|---|---|---|---|---|
| `project` | string | 否 | EPLAN 中选中的项目 | 项目名，或 `eplan_project_list` 返回的 `path` |
| `pages` | array of string，1–50 项 | 否 | 整个项目 | 要导出的页，完整页名如 `=GAA+A1&EFS1/60` |
| `scheme` | string | 否 | EPLAN 当前的 DXF/DWG 导出方案 | 导出方案的名称；格式（DXF 或 DWG）、子目录和文件名都由方案决定 |

- 页名的查找规则与 `eplan_page_get` 相同；同一页出现两次时返回 `INVALID_ARGUMENT`。
- 没有目录、文件名和语言参数（M4 设计 4.1、4.4）。

## 3. 输出
```json
{
  "project": "Line1",
  "pageCount": 2,
  "scheme": "Standard Settings",
  "directory": "C:\\Users\\<user>\\EplanMcp\\exports\\Line1\\20261021T083000Z-k3f9q2\\",
  "files": [
    { "path": "Line1\\=GAA\\+A1\\&EFS1\\=GAA+A1&EFS1_1.dxf", "bytes": 151552 },
    { "path": "Line1\\=GAA\\+A1\\&EFS1\\=GAA+A1&EFS1_2.dxf", "bytes": 98304 }
  ],
  "fileCount": 2,
  "totalBytes": 249856,
  "truncated": false,
  "hint": null
}
```
- 字段同 `eplan_export_pdf`。方案会在目录下按"项目名\结构"建子目录，文件名含 `=`、`+`、`&`（S11），所以 `path` 是带子目录的相对路径。
- 一行摘要：`Exported 2 pages to 2 DXF files (244 KB) in a new folder; see 'directory'.`（DWG 时写 DWG，按文件扩展名判断）

## 4. 错误
同 `eplan_export_pdf`，没有 `file_name` 的校验。EPLAN 报告方案不存在、无权写文件或导出出错（XML 中 `DxfDwgPagesToDisk` 的异常）时返回 `EPLAN_OPERATION_FAILED`，删除本次目录。

## 5. 实现要点
- **流程和导出目录**：同 `eplan_export_pdf` 第 5 节，Bridge 操作是 `export.dxf.plan`、`export.dxf.run`。
- **EPLAN 调用**：`new Export().DxfDwgPagesToDisk(pages, scheme, 目录, "")`，在 `QuietModeStep(ShowNoDialogs)` 中执行；整个项目时传入全部页。
  - 最后一个参数是语言，XML 说明给出时"在导出前把项目翻译成该语言"。一律传空串（M4 设计 4.1）：S11 中这样导出后设计数据不变，没有翻译；
  - 方案在 `USER.DXF.SCHEMES`（XML）。校验、候选、省略时传空串、导出前后比较并写回"上次使用"，都同 `eplan_export_pdf`。
- **已验证**（S11，A16）：2 页 0.6 秒，每页一个 `.dxf`，放在方案生成的子目录 `S11_Sample\=GAA\+A1\&EFS1\` 下，文件名形如 `=GAA+A1&EFS1_1.dxf`；文件只出现在给定目录中；只改写项目的 `User.eod` / `User.eox`；方案传空串时"上次使用"（`Standard Settings`）不变；不弹对话框。
- **没有验证**，由 Large 测试确认：
  - 整个项目的耗时。超过 300 秒的时限时，在描述中写明大致耗时，提示用 `pages` 分批（M4 设计 4.3）；
  - 输出 DWG 的方案；传入具体的方案名。
- **时限与进度**：同 `eplan_export_pdf`：300 秒，每 5 秒一次心跳（`Exporting DXF (40 s)`），一次 EPLAN 调用中途无法打断。

## 6. 写操作专项
不适用（Read 级）。导出不改动项目的设计数据；以后发现会改动时，按 ADR-0012 第 3 条改为 Write 级。

## 7. 测试
- **Small**：摘要中 DXF / DWG 的判断；带子目录的相对路径；schema 快照；注解与 ADR-0012 一致。导出目录的测试同 `eplan_export_pdf`。
- **Medium**：在假 Bridge 上测试方案不存在时的候选；Bridge 报错、取消时目录被删除。
- **Large**（示例项目的 scratch 副本）：
  - 导出 `=GAA+A1&EFS1/1`、`/2`：2 个 `.dxf`，都在本次目录的子目录中，项目的设计数据不变（扩展 `NoSideEffectTests`）；
  - 省略 `pages` 导出整个项目，记录耗时和文件数。
- **Agent 评测**："把 scratch 项目的 `=GAA+A1&EFS1/1` 导出成 DXF，告诉我文件名"，作答后核对目录中的文件。

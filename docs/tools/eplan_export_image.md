# 工具规格：`eplan_export_image`

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_export_image` |
| 所属分组 / Profile | `export` / `core` |
| 安全级别 | Read（写文件，ADR-0012） |
| MCP 注解 | `readOnlyHint` = false　`destructiveHint` = false　`idempotentHint` = false　`openWorldHint` = false |
| 状态 | Proposed（M4 第 8 步；依赖第 5 步的导出目录，进度和取消依赖第 3、4 步；`inline` 依赖 S13） |

## 1. 这个工具解决什么任务
"看看刚插的宏在页上是什么样子""这一页画得对不对"。代理把 1–20 页导出为 PNG 或 JPG 图片（每页一张），可以让工程师查看，也可以用 `inline` 把最多 4 页的图片直接放进工具结果，让模型自己"看"页面，例如核对 `eplan_macro_insert` 的位置（M4 设计 4.1）。与 `eplan_export_pdf` 一样是 Read 级，只在服务器新建的目录中写文件。

## 2. 输入参数
| 参数 | 类型 | 必填 | 默认值 | 说明（写给模型看的） |
|---|---|---|---|---|
| `project` | string | 否 | EPLAN 中选中的项目 | 项目名，或 `eplan_project_list` 返回的 `path` |
| `pages` | array of string，1–20 项 | 是 | — | 要导出的页，完整页名如 `=GAA+A1&EFS1/60` |
| `format` | enum `png` / `jpg` | 否 | `png` | 图片格式 |
| `width_px` | integer，200–4000 | 否 | 1600 | 图片宽度，单位像素；高度按页的比例计算 |
| `inline` | boolean | 否 | false | 为 true 时把图片也放进工具结果，最多 4 页 |

- 页名的查找规则与 `eplan_page_get` 相同；同一页出现两次时返回 `INVALID_ARGUMENT`。
- `inline` 为 true 而 `pages` 超过 4 项时返回 `INVALID_ARGUMENT`。

## 3. 输出
```json
{
  "project": "Line1",
  "pageCount": 1,
  "directory": "C:\\Users\\<user>\\EplanMcp\\exports\\Line1\\20261021T083000Z-k3f9q2\\",
  "files": [ { "page": "=GAA+A1&EFS1/60", "path": "=GAA+A1&EFS1_60.png", "bytes": 45056 } ],
  "fileCount": 1,
  "totalBytes": 45056,
  "inline": 1,
  "truncated": false,
  "hint": null
}
```
- `files`：每页一项，按 `pages` 的顺序；`page` 是页名，`path` 相对于 `directory`。其余字段同 `eplan_export_pdf`。
- `inline`：结果中附带的图片数。为 true 时，每页一个 MCP `image` 内容块（`mimeType` 为 `image/png` 或 `image/jpeg`），排在 JSON 文本块之后，顺序与 `files` 相同。
- 图片中的文字来自项目，无法清洗，模型可能把它当成指令；最终防线仍是写操作的策略和审批（M4 设计第 5 节）。
- 一行摘要：`Exported 1 page as a PNG image in a new folder; the image is included below.`（不带 `inline` 时为 `…; see 'directory'.`）

## 4. 错误
同 `eplan_export_pdf`，另有：`width_px` 越界、`inline` 时页数超过 4，返回 `INVALID_ARGUMENT`；某页导出后目录中没有恰好多出一个文件（例如与前一页的文件同名被覆盖），返回 `EPLAN_OPERATION_FAILED`。出错和取消时都删除本次目录，不返回已导出的那几页。

## 5. 实现要点
- **流程和导出目录**：同 `eplan_export_pdf` 第 5 节，Bridge 操作是 `export.image.plan`、`export.image.run`。
- **EPLAN 调用**：逐页 `new Export().GraphicPage(page, 目录, format, 24, width_px, Export.Compression.None, false)`，在 `QuietModeStep(ShowNoDialogs)` 中执行（M4 设计 4.1）。XML：色深 24 对 PNG、JPG 都可用，压缩方式只影响 TIF，最后一个参数为 false 表示不转成黑白。
  - 每页之前报告分步进度 `Exporting page 3 of 20`、检查取消（M4 设计 4.3）；
  - 每页之后列出目录，新出现的那个文件就是这一页的图片，由此得到 `files[].page`，不依赖 EPLAN 的命名规则。
- **`inline`**：Host 读取图片文件，作为 `image` 内容块返回。S13 验证 Claude Code 能否把结果中的图片交给模型，以及图片怎样计入客户端对结果大小的限制（mcp-tool-design 7.2）；S13 否定时，实现前从 schema 中去掉 `inline`，或者按 S13 的结论降低页数和宽度的上限。
- **已验证**（S10，A14）：在 Bridge 调度器中用 `GraphicPage` 导出过 2 页 PNG（宽 2000 像素，约 32 KB、44 KB），文件名是页名把 `/` 换成 `_`，如 `=GAA+A1&EFS1_72.png`。
- **没有验证**：
  - 导出图片是否改动项目文件。S11 只验证了 PDF 和 DXF（A16）；由 Large 测试确认，改动设计数据时按 ADR-0012 第 3 条改为 Write 级；
  - JPG 格式；每页的耗时（20 页的总时间由 Large 测试测量，时限 300 秒）。

## 6. 写操作专项
不适用（Read 级）。

## 7. 测试
- **Small**：参数校验（页数、`width_px`、`inline` 时最多 4 页）；按"新出现的文件"对应页；`image` 内容块的顺序和 `mimeType`；摘要；schema 快照；注解与 ADR-0012 一致。
- **Medium**：在假 Bridge 上测试分步进度；第二页之前取消，目录被删除；某页没有产生文件时的 `EPLAN_OPERATION_FAILED`。
- **Large**（示例项目的 scratch 副本）：
  - 导出 `=GAA+A1&EFS1/1`、`/2` 的 PNG 和 JPG：每页一个文件，只在本次目录中，项目文件不变（扩展 `NoSideEffectTests`）；
  - 导出 20 页，记录耗时；
  - `inline` 导出 1 页，结果中有一个 `image/png` 内容块。
- **Agent 评测**："在 scratch 项目的新页 `=GAA+A1&EFS1/60` 上插入接触器反馈控制回路后，看一下这一页，说出宏的左上角大致在页面的什么位置"，作答后与插入点核对。

# 工具规格：`eplan_export_pxf`

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_export_pxf` |
| 所属分组 / Profile | `export` / `full`（M4c 的新工具默认只在 `full` 中） |
| 安全级别 | Read（写文件，ADR-0012） |
| MCP 注解 | `readOnlyHint` = false　`destructiveHint` = false　`idempotentHint` = false　`openWorldHint` = false |
| 状态 | Proposed（M4c 第一批） |

## 1. 这个工具解决什么任务
"把项目打包成 PXF 发给别人""导出一份 EPJ 存档"。代理把整个项目导出为一个 EPJ（PXF）文件，得到文件所在的目录。与其他导出相同，只在服务器新建的目录中写文件（ADR-0012）。

## 2. 输入参数
| 参数 | 类型 | 必填 | 默认值 | 说明（写给模型看的） |
|---|---|---|---|---|
| `project` | string | 否 | EPLAN 中选中的项目 | 项目名，或 `eplan_project_list` 返回的 `path` |
| `include_master_data` | boolean | 否 | false | 是否带上项目中的主数据（符号、表格等）。带上后文件更大、耗时约加倍（S25：示例项目 840 MB 对 598 MB，37 秒对 19 秒） |
| `file_name` | string，1–100 个字符 | 否 | 项目名 | 只是文件名，规则同 `eplan_export_pdf` 的 `file_name`；扩展名 `.epj` 由 EPLAN 补上 |

- 连接总是导出（`PXFProject` 的第四个参数为 true）；两个参数的重载不导出连接（XML），不用它。
- 没有页的选择：PXF 是整个项目。

## 3. 输出
```json
{
  "project": "Line1",
  "masterData": false,
  "directory": "C:\\Users\\<user>\\EplanMcp\\exports\\Line1\\20261021T083000Z-k3f9q2\\",
  "files": [ { "path": "Line1.epj", "bytes": 598384115 } ],
  "fileCount": 1,
  "totalBytes": 598384115,
  "truncated": false,
  "hint": null
}
```
- 字段同 `eplan_export_pdf`。EPLAN 在文件旁边留下一个与文件同名的空目录（S25），不列入 `files`。
- 一行摘要：`Exported the project to a 571 MB EPJ file in a new folder; see 'directory'.`
- 工具描述写明：文件可能有几百 MB，耗时可能超过半分钟。

## 4. 错误
同 `eplan_export_pdf`，没有 `pages` 和 `scheme` 的校验。EPLAN 报错时返回 `EPLAN_OPERATION_FAILED`，删除本次目录。

## 5. 实现要点
- **流程和导出目录**：同 `eplan_export_pdf` 第 5 节。
- **EPLAN 调用**：`new Export().PXFProject(project, Path.Combine(目录, 文件名), include_master_data, true)`，在 `QuietModeStep(ShowNoDialogs)` 中执行。
- **已验证**（S25，A25）：带主数据 36.9 秒、840 MB，不带 19.2 秒、598 MB；文件只出现在给定目录中；只改写项目的 `User.eod`。
- **导出后的核对**：除了 SEC-20 的检查，还要允许那个与文件同名的空目录，但它必须是空的、不是链接。
- **磁盘空间**：导出前检查导出根目录所在磁盘的可用空间，少于 2 GB 时不调用 EPLAN，返回 `EPLAN_OPERATION_FAILED`，`message` 说明可用空间不足、请清理 `exports\` 目录（`doctor` 报告它的占用）。阈值在实现时按项目大小再定。
- **时限与进度**：300 秒，每 5 秒一次心跳（`Exporting PXF (20 s)`），一次 EPLAN 调用中途无法打断。
- 待 Large 测试确认：给出的文件名已带 `.epj` 时 EPLAN 是否再补一次扩展名。

## 6. 写操作专项
不适用（Read 级）。

## 7. 测试
- Small：参数校验、摘要、空目录的处理。
- Medium：假 Bridge 上 EPLAN 报错、取消时目录被删除；磁盘空间不足时拒绝。
- Large（示例项目的 scratch 副本）：不带主数据导出，得到一个 `.epj` 文件，项目的设计数据不变（扩展 `NoSideEffectTests`）。
- Agent 评测："把 scratch 项目导出成 PXF，告诉我文件多大"。

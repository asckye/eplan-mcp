# 工具规格：`eplan_export_parts`

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_export_parts` |
| 所属分组 / Profile | `export` / `full`（M4c 的新工具默认只在 `full` 中） |
| 安全级别 | Read（写文件，ADR-0012） |
| MCP 注解 | `readOnlyHint` = false　`destructiveHint` = false　`idempotentHint` = false　`openWorldHint` = false |
| 状态 | Proposed（M4c 第一批；已实现，待实机验证） |

## 1. 这个工具解决什么任务
"导出项目的部件清单给采购"。代理把项目的部件清单导出为 XML 文件。只想知道项目里用了哪些部件、各多少个时，用 `eplan_part_list` 直接读，不用导出；本工具给需要一份文件的场合。

## 2. 输入参数
| 参数 | 类型 | 必填 | 默认值 | 说明（写给模型看的） |
|---|---|---|---|---|
| `project` | string | 否 | EPLAN 中选中的项目 | 项目名，或 `eplan_project_list` 返回的 `path` |
| `file_name` | string，1–100 个字符 | 否 | 项目名 | 只是文件名，规则同 `eplan_export_pdf` 的 `file_name`；扩展名 `.xml` 由工具补上 |

- 只有 XML：S25 中 CSV 导出之后 EPLAN 发生过一次访问冲突（A25，未复现），查清之前不提供 CSV。

## 3. 输出
字段同 `eplan_export_pdf`，`files` 中只有一个 `.xml` 文件。
- 一行摘要：`Exported the parts list to a 13 MB XML file in a new folder; see 'directory'.`
- 工具描述写明：示例规模的项目约需一分钟，文件十几 MB；要统计部件时先用 `eplan_part_list`。

## 4. 错误
同 `eplan_export_pdf`，没有 `pages` 和 `scheme` 的校验。EPLAN 报错时返回 `EPLAN_OPERATION_FAILED`，删除本次目录。

## 5. 实现要点
- **流程和导出目录**：同 `eplan_export_pdf` 第 5 节。
- **EPLAN 调用**：`new PartsService().ExportPartsList(project, 路径, PartsService.Format.XML)`，在 `QuietModeStep(ShowNoDialogs)` 中执行。
- **已验证**（S25，A25）：示例项目 61.9 秒、14 MB；文件只出现在给定目录中；只改写项目的 `User.eod`。
- **时限与进度**：300 秒，每 5 秒一次心跳（`Exporting the parts list (30 s)`）。
- 待 Large 测试确认：XML 中每个部件给出哪些字段（决定工具描述怎样介绍这个文件）。

## 6. 写操作专项
不适用（Read 级）。

## 7. 测试
- Small：参数校验、摘要。
- Medium：假 Bridge 上 EPLAN 报错、取消时目录被删除。
- Large（示例项目的 scratch 副本）：得到一个 `.xml` 文件，项目的设计数据不变；导出之后 EPLAN 仍然响应（针对 A25 的访问冲突，再调用一次 `eplan_session_status`）。
- Agent 评测："导出部件清单，告诉我文件在哪里"。

# 工具规格：`eplan_export_labels`

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_export_labels` |
| 所属分组 / Profile | `export` / `full`（M4c 的新工具默认只在 `full` 中） |
| 安全级别 | Read（写文件，ADR-0012） |
| MCP 注解 | `readOnlyHint` = false　`destructiveHint` = false　`idempotentHint` = false　`openWorldHint` = false |
| 状态 | Proposed（M4c 第一批；已实现，待实机验证） |

## 1. 这个工具解决什么任务
"按'设备标识清单'方案导出一份标签数据""导出端子图的 Excel"。EPLAN 的标签（Labelling）按方案生成清单文件：设备标识、端子、电缆、部件汇总等。代理选一个方案导出，得到文件。这也覆盖了"全部设备的清单"：`DeviceService.ExportDeviceList` 导出的是预规划清单（S25），不是设备列表。

## 2. 输入参数
| 参数 | 类型 | 必填 | 默认值 | 说明（写给模型看的） |
|---|---|---|---|---|
| `project` | string | 否 | EPLAN 中选中的项目 | 项目名，或 `eplan_project_list` 返回的 `path` |
| `scheme` | string | 是 | — | 标签方案的名称，如 `Device tag list`、`Terminal diagram`、`Cable diagram`、`Summarized parts list`；方案不存在时错误中列出可用的方案 |
| `language` | string | 否 | EPLAN 界面语言 | 多语言文本的语言，如 `en_US`、`zh_CN` |
| `file_name` | string，1–100 个字符 | 否 | 方案名 | 只是文件名，规则同 `eplan_export_pdf` 的 `file_name`；扩展名由方案的输出格式决定，由工具补上 |

- `scheme` 必填：标签方案决定输出什么，界面上次用的方案与代理的意图无关。
- 输出格式是 Excel 2003（`.xls`）的方案拒绝：XML 文档写明它需要 Excel。

## 3. 输出
字段同 `eplan_export_pdf`，`files` 中只有一个文件，另加 `scheme`。
- 一行摘要：`Exported labels with scheme 'Device tag list' to a 39 KB text file in a new folder; see 'directory'.`

## 4. 错误
| 错误码 | 什么时候发生 | 返回给模型的提示 |
|---|---|---|
| `INVALID_ARGUMENT` | 缺少 `scheme`；方案不存在；方案的输出格式不受支持；`file_name` 不合法 | `metadata.violations` 列出全部问题；方案不存在时 `metadata.candidates` 列出可用的方案（最多 20 个） |
| 其余 | 同 `eplan_export_pdf` | 同 `eplan_export_pdf` |

## 5. 实现要点
- **流程和导出目录**：同 `eplan_export_pdf` 第 5 节。
- **方案**：在用户设置 `USER.Labelling.Config` 中（A24），用 `SchemeSetting` 校验和列出，不计内部的 `InternalRefDefScheme`。
- **扩展名**：按方案的 `Format`：`XmlLblTextExportConverterImpl` → `.txt`，`XmlLblExcelExportConverterImpl` → `.xlsx`（S25 中两种都成功，生成 xlsx 时没有 Excel 进程）。其他格式在 Large 测试中逐个确认后再加入，确认前拒绝。读取方案 `Format` 的设置路径在实现时验证。
  - 实现：设置路径取自 EPLAN 自带的标签配置（`Platform\2026.0.3\Cfg\LabelLog_User_Config_*.xml`，api-notes B9）：方案下的 `Data.File.Format`，用 `SchemeSetting.SetScheme(序号)` 之后 `GetStringSetting("Data.File.Format", 0)` 读取；读不出时按格式未知拒绝该方案。**待 Large 测试确认**这样能读出 S25 的两个方案的格式。
  - Excel 方案总是给出 `.xlsx` 的文件名，所以不会产生需要 Excel 的 `.xls`；方案的 Excel 模板（`Data.File.Template`）是 `.xls` 时的结果没有验证。
  - 方案名先按原样匹配，找不到时接受只差大小写的唯一一个；结果中的 `scheme` 是 EPLAN 中的名称。省略 `file_name` 时文件以方案名命名。
- **EPLAN 调用**：`new Label().DoLabel(project, scheme, language, 路径, 1, 1)`，在 `QuietModeStep(ShowNoDialogs)` 中执行。文件名总要给出：方案中的默认目标在 `$(MD_DOCUMENTS)` 下，在导出目录之外（S25）。不用带布尔参数的重载：它会打开输出文件（XML）。
- **已验证**（S25，A25）：`Device tag list` 2.3 秒、40 KB 定宽文本；`Terminal diagram` 2.2 秒、805 KB xlsx；传入方案名不改写"上次使用"；只改写项目的 `User.eod`。
- **时限与进度**：300 秒，每 5 秒一次心跳（`Exporting labels (10 s)`）。

## 6. 写操作专项
不适用（Read 级）。

## 7. 测试
- Small：参数校验、扩展名的选择、摘要。
- Medium：假 Bridge 上方案不存在时的候选、不受支持的格式、EPLAN 报错时目录被删除。
- Large（示例项目的 scratch 副本）：`Device tag list` 得到一个 `.txt`，`Terminal diagram` 得到一个 `.xlsx`，项目的设计数据不变。
- Agent 评测："导出一份全部设备标识的清单"（应选 `Device tag list`）。

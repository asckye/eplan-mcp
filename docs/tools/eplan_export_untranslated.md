# 工具规格：`eplan_export_untranslated`

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_export_untranslated` |
| 所属分组 / Profile | `export` / `full`（M4c 的新工具默认只在 `full` 中） |
| 安全级别 | Read（写文件，ADR-0012） |
| MCP 注解 | `readOnlyHint` = false　`destructiveHint` = false　`idempotentHint` = false　`openWorldHint` = false |
| 状态 | Proposed（M4c 第一批） |

## 1. 这个工具解决什么任务
"项目里还有哪些文字没有英文翻译？"代理导出缺失词表（项目中的文本在翻译库中找不到的译文），交给翻译人员或自己读取后整理。翻译项目本身是第二批的 `eplan_project_translate`。

## 2. 输入参数
| 参数 | 类型 | 必填 | 默认值 | 说明（写给模型看的） |
|---|---|---|---|---|
| `project` | string | 否 | EPLAN 中选中的项目 | 项目名，或 `eplan_project_list` 返回的 `path` |
| `languages` | array of string，1–10 项 | 是 | — | 要检查的目标语言，如 `["en_US"]`；必须是项目的翻译语言 |
| `file_name` | string，1–100 个字符 | 否 | 项目名 | 只是文件名，规则同 `eplan_export_pdf` 的 `file_name`；扩展名 `.txt` 由工具补上 |

## 3. 输出
字段同 `eplan_export_pdf`，`files` 中只有一个 `.txt` 文件，另加 `languages`。
- 文件是制表符分隔的 Unicode 文本，第一行是列名：源语言、各目标语言、`COMMENT`（S25：`zh_CN`、`en_US`、`COMMENT`）。工具描述写明这一点，模型可以直接读取文件。
- 一行摘要：`Exported the untranslated texts for en_US to a 4 KB file in a new folder; see 'directory'.`

## 4. 错误
| 错误码 | 什么时候发生 | 返回给模型的提示 |
|---|---|---|
| `INVALID_ARGUMENT` | `languages` 为空、超过 10 项、有重复或格式不对；有语言不在项目的翻译语言中；`file_name` 不合法 | `metadata.violations` 列出全部问题；语言不在项目中时 `metadata.candidates` 列出项目的翻译语言 |
| 其余 | 同 `eplan_export_pdf` | 同 `eplan_export_pdf` |

## 5. 实现要点
- **流程和导出目录**：同 `eplan_export_pdf` 第 5 节。
- **项目的翻译语言**：在 Bridge 中读出，校验 `languages`。S25 中示例项目有 20 种（`de_DE;en_US;…;zh_TW`）；读法在实现时验证（S25 用的设置名没有核实是否就是界面上的"翻译语言"）。
- **EPLAN 调用**：`new Translate().ExportMissingTranslation(路径, "XTrLanguageDbXml2TabConverterImpl", 语言, [project])`，在 `QuietModeStep(ShowNoDialogs)` 中执行。翻译库只读（用户设置中配置的那个）。
- **已验证**（S25，A25）：`en_US` 3.5 秒、3.7 KB；只改写项目的 `User.eod`。
- **时限与进度**：300 秒，每 5 秒一次心跳。

## 6. 写操作专项
不适用（Read 级）。不写翻译库，也不改项目的文本。

## 7. 测试
- Small：参数校验、摘要。
- Medium：假 Bridge 上语言不在项目中时的候选、EPLAN 报错时目录被删除。
- Large（示例项目的 scratch 副本）：`["en_US"]` 得到一个 `.txt`，第一行有 `en_US` 列，项目的设计数据不变。
- Agent 评测："还有哪些文字没有英文翻译？列出前十条"。

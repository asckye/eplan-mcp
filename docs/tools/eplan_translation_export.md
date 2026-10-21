# 工具规格：`eplan_translation_export`

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_translation_export` |
| 所属分组 / Profile | `language` / `full`（M4c 的新工具默认只在 `full` 中） |
| 安全级别 | Read（写文件，ADR-0012） |
| MCP 注解 | `readOnlyHint` = false　`destructiveHint` = false　`idempotentHint` = false　`openWorldHint` = false |
| 状态 | Proposed（M4c 第四批） |

## 1. 这个工具解决什么任务
"把翻译库导出来，我要交给翻译公司校对""看看翻译库里 '断路器' 有哪些译法"。代理把 EPLAN 配置的**系统翻译库**导出为文件，交给人，或者自己读取。

与相近工具的区别：
- `eplan_export_untranslated` 导出的是一个项目中**还没有译文**的文本；
- 本工具导出翻译库中**已有**的全部词条，与项目无关。

## 2. 输入参数
| 参数 | 类型 | 必填 | 默认值 | 说明（写给模型看的） |
|---|---|---|---|---|
| `format` | enum `tab` \| `xml` | 否 | `tab` | `tab`：制表符分隔的文本，便于阅读和在表格软件中编辑，也是 `eplan_translation_import` 默认读的格式；`xml`：EPLAN 的原生格式 |
| `file_name` | string，1–100 个字符 | 否 | `translations` | 只是文件名，规则同 `eplan_export_pdf` 的 `file_name`；扩展名 `.txt` 或 `.xml` 由工具补上 |

- 不按语言或词条过滤：API 只能导出整个库（示例库约 2–3 MB，不到 1 秒）。

## 3. 输出
字段同 `eplan_export_pdf`，`files` 中只有一个文件；另加 `database`：导出的是哪个翻译库（配置中的写法，如 `$(md_translate)\Translate.db`）。

一行摘要：`Exported the translation database to a 2.1 MB tab-separated file in a new folder; see 'directory'.`

工具描述写明：导出的是所有项目共用的翻译库；文件可能有几 MB，读取时先看开头几行的列。

## 4. 错误
同 `eplan_export_pdf`，没有 `pages`、`scheme` 的校验。`ExportDatabaseTexts` 返回 false 或抛出异常时返回 `EPLAN_OPERATION_FAILED`，删除本次目录。

## 5. 实现要点
- **流程和导出目录**：同 `eplan_export_pdf` 第 5 节。
- **EPLAN 调用**：`new Translate().ExportDatabaseTexts(路径, 转换器)`，用完释放；转换器 `tab` 为 `XTrLanguageDbXml2TabConverterImpl`，`xml` 为 `XTrLanguageDbXmlConverterImpl`。在调度器中执行，套 `QuietModeStep(ShowNoDialogs)`。
- **已验证**（S24 r11，api-notes A30）：在配置的翻译库上导出，XML 0.68 秒、2.8 MB；制表符 0.76 秒、2.1 MB；翻译库前后 SHA-256 不变。
- **待 Large 测试确认**：制表符文件的列（推测每种语言一列，第一行是语言代码），确认后写进工具描述。
- **时限与进度**：60 秒；不到 1 秒，不发心跳。

## 6. 写操作专项
不适用（Read 级）：只读翻译库，只写本次新建的导出目录。

## 7. 测试
- Small：参数校验、文件名规则、摘要。
- Medium：假 Bridge 上 EPLAN 返回 false、取消时目录被删除。
- Large（配置的翻译库，只读）：两种格式各得到一个非空文件；记录制表符文件的第一行。
- Agent 评测："翻译库里 '断路器' 的英文是什么？"（导出后读取文件；也可以接受代理说明它要读取整个文件）。

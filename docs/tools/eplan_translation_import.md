# 工具规格：`eplan_translation_import`

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_translation_import` |
| 所属分组 / Profile | `language` / `full`（M4c 的新工具默认只在 `full` 中） |
| 安全级别 | 系统主数据（[ADR-0019](../adr/0019-master-data-writes.md)）：每次审批，执行前备份主数据 |
| MCP 注解 | `readOnlyHint` = false　`destructiveHint` = true　`idempotentHint` = false　`openWorldHint` = false |
| 状态 | Proposed（M4c 第四批）；**实现前要先验证 `ImportDatabaseTexts`**（见第 5 节） |

## 1. 这个工具解决什么任务
"翻译公司把校对好的词表发回来了，导入翻译库""把这些新词条的英文和中文加进翻译库"。代理把一个制表符分隔的词表导入 EPLAN 配置的**系统翻译库**。翻译库被所有项目共用，所以每次都要工程师审批。

与相近工具的区别：
- `eplan_translation_export` 是反方向；
- 翻译项目（用翻译库翻译项目文本）是 `eplan_project_translate`，不改翻译库。

## 2. 输入参数
| 参数 | 类型 | 必填 | 默认值 | 说明（写给模型看的） |
|---|---|---|---|---|
| `file` | string，1–260 个字符 | 是 | — | 导入目录（`<数据根目录>\imports\`）下的相对路径；`.txt`，格式同 `eplan_translation_export` 的 `tab`：UTF-16，第一行是语言代码，之后每行一个词条 |
| `languages` | array of string，1–30 项 | 是 | — | 要导入的语言，如 `["en_US", "zh_CN"]`；必须都是文件第一行中的列 |

- 路径规则同 `eplan_part_import`；文件不超过 50 MB。

## 3. 输出
`structuredContent`：
- `database`：写入的翻译库（如 `$(md_translate)\Translate.db`）；
- `entries`：文件中的词条数；`languages`：导入的语言；
- `backup`：本次主数据备份的路径；
- `execution`、`auditId`。

一行摘要：`Imported 120 entries in en_US, zh_CN into the translation database ($(md_translate)\Translate.db). Backup: …`

- 不报告新增和覆盖各多少：API 只返回成功与否，翻译库的内容事先读不出差异（只能整体导出，见 `eplan_translation_export`）。

## 4. 错误
同 `eplan_part_import` 第 4 节，备份为 `.zw7`，另有：
- `INVALID_ARGUMENT`：文件不是 UTF-16、第一行不是语言代码，或 `languages` 中有文件里没有的列（`metadata.columns` 列出文件中的列）。
- `ImportDatabaseTexts` 返回 false：`OUTCOME_UNKNOWN`。翻译库可能已部分改变，`metadata.backup` 给出备份。

## 5. 实现要点
- **规划**（只读）：读文件第一行和行数，校验 `languages`；指纹为文件的 SHA-256。
- **审批对话框**：库的路径，"影响使用这个库的所有项目"，文件名和大小，词条数和语言，前 10 个词条（每个词条只显示各语言的前 40 个字符），"导入撤不回，执行前会备份翻译库"。
- **备份**：`backup /TYPE:MASTERDATA /MDTYPE:LANGUAGES`，`SOURCEPATH`、`FILENAME` 由 `Translate.TranslateDatabase` 拆出（S24：0.23 秒，`.zw7`，只含 `Translate.db`）。
- **执行**：`new Translate().ImportDatabaseTexts(文件, "XTrLanguageDbXml2TabConverterImpl", "en_US;zh_CN")`，语言之间用分号（EPLAN XML 文档），用完释放；在 `QuietModeStep(ShowNoDialogs)` 中执行，不开事务。
- **读回**：导入后用 `ExportDatabaseTexts` 导出到临时文件，抽查文件中前 20 个词条在导出中存在，删除临时文件。
- **未验证**（S24 r12 经项目负责人批准要验证，但 Claude Code 的安全分类器拒绝了这条命令，没有运行）：
  - `ImportDatabaseTexts` 的实际行为：已有词条是覆盖还是追加、耗时、是否弹对话框、返回值是否可靠（同类的 `RemoveProjectLanguage` 删除成功也返回 false，S24 r18）；
  - 只含部分语言列的文件能否导入。
  - **实现前**由项目负责人亲自运行这一步，或者给出允许它的权限，按 S24 的做法：先备份、计算 SHA-256，导入一条测试词条，再导出核对，最后关闭 EPLAN 恢复。结果补进 api-notes A30。

## 6. 写操作专项
- **dry-run**：规划结果（词条数、语言、前 10 个词条）。
- **审计**：工具名、库的路径、文件名和 SHA-256、`languages`、词条数、备份路径、`execution`。
- **回滚**：没有。恢复按 S24 结论 6，翻译库只需替换 `Translate.db`。

## 7. 测试
- Small：路径规则、`languages` 校验、文件第一行的解析（UTF-16、BOM、制表符）。
- Medium：假 Bridge 上的审批、备份失败、返回 false 时 `unknown` 与暂停。
- Large：写正在用的翻译库，只在项目负责人每次批准后运行，做法同 `eplan_part_import` 第 7 节，测试词条为 `EplanMcpLargeTest`。
- Agent 评测："把 imports 里的 glossary.txt 的英文和中文导入翻译库"。

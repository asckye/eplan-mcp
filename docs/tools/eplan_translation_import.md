# 工具规格：`eplan_translation_import`

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_translation_import` |
| 所属分组 / Profile | `language` / `full`（M4c 的新工具默认只在 `full` 中） |
| 安全级别 | 系统主数据（[ADR-0019](../adr/0019-master-data-writes.md)）：每次审批，执行前备份主数据 |
| MCP 注解 | `readOnlyHint` = false　`destructiveHint` = true　`idempotentHint` = false　`openWorldHint` = false |
| 状态 | Implemented；Large 测试已编写、尚未运行，每次运行须项目负责人批准（[api-notes A47](../eplan/api-notes.md) 为已有 API 实测） |

TI-1、TI-2a、TI-2b 已接通基础支持、Bridge 和 Host；`full` 共 73 个工具，本工具归 `language` 分组，不在 `core` 中。

- TI-2a 执行参数需带计划的 `fingerprint`（文件 SHA-256）与 `database`（`plan.masterData.database`），防止审批、备份后切换库；Host 流水线负责先备份并在 unknown 错误中附加 `metadata.backup`。

- 纯函数要求 UTF-16 BOM（支持大小端），导入表头为语言代码，导出表头另允许 `COMMENT`。
- 文件内行号放在错误说明中，JSON Pointer 只指向工具参数 `/file` 或 `/reference_language`。
- 按参考原文去重计新增、按已有词条的语言单元格计补空和保留；缺失读回单独标记，由 TI-2 转为 `OUTCOME_UNKNOWN`。
- 计划详情约定 `file`、`bytes`、`sha256`、`entries`、`referenceLanguage`、`languages`（其他语言）及 `preview`（语言代码到文本的对象数组）。
- `WriteOutcome.AuditId` 对应审计中的 `requestId`，dry-run 无审计，值为空；恢复结果没有读回数据时不编造计数。

## 1. 这个工具解决什么任务
"翻译公司把校对好的词表发回来了，导入翻译库""把这些新词条的英文和中文加进翻译库"。代理把一个制表符分隔的词表导入 EPLAN 配置的**系统翻译库**。翻译库被所有项目共用，所以每次都要工程师审批。

与相近工具的区别：
- `eplan_translation_export` 是反方向；
- 翻译项目（用翻译库翻译项目文本）是 `eplan_project_translate`，不改翻译库。

## 2. 输入参数
| 参数 | 类型 | 必填 | 默认值 | 说明（写给模型看的） |
|---|---|---|---|---|
| `file` | string，1–260 个字符 | 是 | — | 导入目录（`<数据根目录>\imports\`）下的相对路径；`.txt`，格式同 `eplan_translation_export` 的 `tab`：UTF-16，第一行是语言代码，之后每行一个词条 |
| `reference_language` | string | 否 | `en_US` | 按哪种语言的原文匹配已有词条，如 `en_US`；必须是文件第一行中的列，词条在这一列不能为空 |

- 路径规则同 `eplan_part_import`；文件不超过 50 MB。

## 3. 输出
`structuredContent`：
- `database`：写入的翻译库（如 `$(md_translate)\Translate.db`）；
- `entries`：文件中的词条数；`referenceLanguage`；
- `added`：新增的词条数；`filled`：已有词条中补上的空缺译文数；`kept`：文件中与已有译文不同、按 EPLAN 规则保留原译文而未写入的数目；
- `backup`：本次主数据备份的路径；
- `execution`、`auditId`。

dry-run 另有 `languages`、`preview`；`dryRun`、`recovered` 标记规划或恢复响应。没有读回时三个计数为空，不用零代替未知。

一行摘要：`Imported 120 entries (by en_US) into the translation database ($(md_translate)\Translate.db): 12 added, 3 filled, 2 kept. Backup: …`

- 三个计数来自导入前后各导出一次（每次约 0.7 秒）的比较，不依赖 API 的返回值（A47：返回 true 不代表写入了任何东西）。

## 4. 错误
同 `eplan_part_import` 第 4 节，备份为 `.zw7`，另有：
- `INVALID_ARGUMENT`：文件不是 UTF-16、第一行不是语言代码，`reference_language` 不是文件中的列或某个词条在这一列为空，或文件有翻译库导出中没有的语言列（`metadata.columns` 列出文件中的列）。
- `ImportDatabaseTexts` 返回 false，或返回 true 但导入后的导出读不回来：`OUTCOME_UNKNOWN`。翻译库可能已部分改变，`metadata.backup` 给出备份。

## 5. 实现要点
- **规划**（只读）：读文件第一行和行数，校验 `reference_language` 和各列的语言代码；指纹为文件的 SHA-256。
- **审批对话框**：库的路径，"影响使用这个库的所有项目"，文件名和大小，词条数、参考语言和其他语言，前 10 个词条（每个词条只显示各语言的前 40 个字符），"只新增词条、补上空缺译文，已有译文不会被改写"，"导入撤不回，执行前会备份翻译库"。
- **备份**：`backup /TYPE:MASTERDATA /MDTYPE:LANGUAGES`，`SOURCEPATH`、`FILENAME` 由 `Translate.TranslateDatabase` 拆出（S24：0.23 秒，`.zw7`，只含 `Translate.db`）。
- **执行**：`new Translate().ImportDatabaseTexts(文件, "XTrLanguageDbXml2TabConverterImpl", 参考语言)`，第三个参数是**一个**参考语言（不是语言列表），用完释放；在 `QuietModeStep(ShowNoDialogs)` 中执行，不开事务。导入的文件由 Bridge 执行操作生成：表头与导入前那次导出的第一行完全相同（列顺序一致），用户文件中的列按语言代码放进对应位置，其余列留空；用户文件有导出中没有的语言列时 `INVALID_ARGUMENT`。
- **读回**：导入前、后各用 `ExportDatabaseTexts` 导出到临时文件，按参考语言比较得出 `added`、`filled`、`kept`，删除临时文件；文件中的新词条在导入后的导出里找不到时按 `OUTCOME_UNKNOWN` 处理。
- **已验证**（S24 r19，2026-10-26，项目负责人指示执行，见 A47）：只新增词条、补空缺译文，不改写已有译文，同一参考原文不会重复；不弹对话框，0.02–0.07 秒；只改 `Translate.db`。仍未验证：表头只含部分语言列的文件（实现中由 Bridge 补成完整表头，不依赖它）。

## 6. 写操作专项
- **dry-run**：规划结果（词条数、语言、前 10 个词条）。
- **审计**：工具名、库的路径、文件名和 SHA-256、`referenceLanguage`、词条数、`added`/`filled`/`kept`、备份路径、`execution`。
- **回滚**：没有。恢复按 S24 结论 6，翻译库只需替换 `Translate.db`。

## 7. 测试
- Small：路径规则、`reference_language` 校验、文件第一行的解析（UTF-16、BOM、制表符）。
- Medium：假 Bridge 上的审批、备份失败、返回 false 时 `unknown` 与暂停。
- Large：`TranslationImportTests` 默认跳过，只在项目负责人每次批准且设置 `EPLANMCP_LARGE_MASTERDATA=1` 后运行。先对配置的翻译文件夹全部文件计算 SHA-256、复制并核对，副本和清单保留在 Host 临时目录之外；导入 `EplanMcpLargeTest` 得到 `added=1`，再审批、备份并重导得到 `added=0`。结束后由操作员关闭 EPLAN，用副本恢复并核对全部哈希，见 Large 测试 README；本轮未运行。
- Agent 评测："把 imports 里的 glossary.txt 的英文和中文导入翻译库"。

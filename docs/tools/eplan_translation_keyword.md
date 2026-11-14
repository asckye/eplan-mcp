# 工具规格：`eplan_translation_keyword`

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_translation_keyword` |
| 所属分组 / Profile | `language` / `full`（新工具默认只在 `full` 中） |
| 安全级别 | Read；写入（增删改词条、增删语言）尚未实现，实现后按系统主数据处理（[ADR-0019](../adr/0019-master-data-writes.md)） |
| MCP 注解 | `readOnlyHint` = true　`destructiveHint` = false　`idempotentHint` = true　`openWorldHint` = false |
| 状态 | Implemented (Unreleased) |

读取已实现（2026-11-14）；写入仍是设计。设计说明采用[规范附录 T.3](https://github.com/asckye/engineering-standards/blob/v3.0/skills/general-engineering-standard/references/appendix/t-templates.md#tpl-rfc)。

| 项目字段 | 记录 |
|---|---|
| 工具名 / 注册入口 / 契约快照 | `eplan_translation_keyword`（待注册；与已有 [`eplan_translation_export`](eplan_translation_export.md)、[`eplan_translation_import`](eplan_translation_import.md)、[`eplan_project_translate`](eplan_project_translate.md) 同组） |
| 分组 / Profile / 状态 | `language` / `full` / **读取 Implemented（2026-11-14，Unreleased）；写入 Designed** |
| 当前安全分类与 MCP 注解 | 读：Read，`readOnlyHint` = true；写：系统主数据（[ADR-0019](../adr/0019-master-data-writes.md)），`destructiveHint` = true，每次审批并在执行前备份主数据 |
| 模型说明与参数示例来源 | [`TranslationKeywordArgs`](../../src/EplanMcp.Contracts/TranslationKeywordArgs.cs)、[`TranslationKeywordData`](../../src/EplanMcp.Contracts/TranslationKeywordData.cs)、[Host 工具](../../src/EplanMcp.Host/Tools/TranslationKeywordTools.cs)、[输出](../../src/EplanMcp.Host/Tools/TranslationKeywordResult.cs) |
| EPLAN 调用路线 | 读：Bridge 操作 `translation.keyword.search` → [`EplanTranslationDatabase`](../../src/EplanMcp.Bridge.Eplan/Adapters/EplanTranslationDatabase.cs) → `Eplan.EplApi.MasterData.MDTranslationDatabase`（`Open`、`OpenExclusive`、`FindKeyword`、`FindAllKeywords`、`AddKeyword`、`EditKeyword`、`DeleteKeyword`、`AddLanguage`、`DeleteLanguage`、`Languages`、`NotTranslatedWords`、`IsReadOnly`、`Close`） |
| API 证据 | 签名已按目录核实：**`Open(System.Boolean bReadOnly)` 只收只读标志、不收路径**，打开的就是设置里配置的库；`FindAllKeywords(String, ISOCode.Language, ref List<MultiLangString>, ref List<String>)` 返回匹配的词条；`LanguageList.Language[int]` 是命名参数化属性，C# 要走 `get_Language(index)`（CS1546）。`Translate.TranslateDatabase` 与 `ActiveTranslateDatabase` 的读法沿用主数据导出器（A27）。实机行为待 Large 验证。`MDTranslationDatabase` 的 13 个操作组在 2026-11-14 按 `covered_by_translation_tool` 结案，理由见[结案清单第 8 节](../design/2026-11-14-api-closeout-list.md)；实现时按 api-notes 规则先在 scratch 副本上验证再记录 |
| 原生作用域与生命周期 | 数据库句柄只在一次调用内有效：打开、读或写、关闭都在同一次主线程调用里完成，不跨调用保留；写入包在 `LockingStep` 内，按 ADR-0019 先备份 |
| 预览、审批与审计 | 变化清单逐条列出词条、语言、原值、新值；审批摘要显示数据库路径与是否为副本；审计记录数据库路径的哈希，不回显词条正文之外的项目数据 |
| 事务、回滚与结果恢复 | 主数据不在项目事务里，没有一步撤销；按 ADR-0019 以执行前备份为回退手段，`execution` 按 [ADR-0010](../adr/0010-write-outcomes.md) 返回 |
| 已有测量 | 未测。翻译库可能有上万词条，`FindAllKeywords` 要分页并有软预算 |
| 项目测试与夹具 | 已有：Small [`TranslationKeywordSearchOperationTests`](../../tests/EplanMcp.Bridge.Core.Tests/Operations/TranslationKeywordSearchOperationTests.cs) 10 例、Host [`TranslationKeywordToolTests`](../../tests/EplanMcp.Host.Tests/Tools/TranslationKeywordToolTests.cs) 8 例；待写：Large（scratch 翻译库副本上读一条、写一条并核对原库哈希不变） |

## 为什么要这个工具

[`eplan_translation_export`](eplan_translation_export.md) 和 [`eplan_translation_import`](eplan_translation_import.md) 只能整体导出导入文件，`eplan_project_translate` 只翻译项目文本。**查一个词条当前有哪些译文、给一个词条补一种语言的译文**都做不到，而这正是多语言项目日常要做的事。

通用入口 `eplan_api_try` 走不通：`MDTranslationDatabase` 必须先 `Open(路径)` 才能查词条，而按路径打开主数据文件被 `external_path_parameter` 排除。负责人 2026-11-14 决定**不开这条路**，改为写本工具。

## 范围

- **读**：按关键词精确或包含查询，返回该词条的各语言译文；列出数据库里的语言；列出未翻译词。读可以在 EPLAN 当前配置的翻译库上进行（与 ADR-0054 对部件库"读不限库"一致）。
- **写**：新增、修改、删除词条，新增、删除语言。写**只在当前配置的翻译库位于 scratch 根目录下、且与原库哈希一致时**开放，与 `eplan_part_build` 对部件库的规则相同（ADR-0054 第 3 节、ADR-0019）。工具不改 EPLAN 的翻译库配置。
- **不做**：整库导入导出（已有工具）、项目文本翻译（已有工具）、按任意路径打开数据库。

## 待决定

1. 读取是否也要求副本。倾向不要求：与部件库一致，读在用库但不写。
2. `FindAllKeywords` 的分页口径（按词条还是按词条×语言）。
3. 词条正文是项目/主数据数据，按"数据而非指令"经 `ResultShaper` 清洗；长文本的截断上限待定。

## 已实现的读取（2026-11-14）

- `eplan_translation_keyword(query, exact, limit, cursor)`：按源文本精确或包含匹配，返回 `database`、`languages`、`keywords[{source, translations{语言: 文本}}]`、`total`、`nextCursor`，分页游标绑定 query 与 exact。
- 适配器用 `Open(bReadOnly: true)` 打开**配置的**库，读完 `Close()`；SQL Server 上的库返回 `CAPABILITY_MISSING` 并指向 `eplan_translation_export`，因为连接串可能含登录信息（SEC-12），不出 Bridge。
- 匹配与排序在 Bridge.Core，可脱离 EPLAN 测试；`exact` 的收窄也在那里，一次读同时服务两种模式。

**更正**：我 2026-11-14 早先对负责人说"`MDTranslationDatabase` 要先 `Open(路径)`，不另做决定用不了"——**这是错的**，`Open` 只收一个只读标志。真正的障碍是这个类型只能由构造函数得到，而构造函数不是试执行的根；负责人仍选择写专用工具，所以结论不变，但理由要以本页为准。

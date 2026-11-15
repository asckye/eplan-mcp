# 工具规格：`eplan_translation_add`

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_translation_add` |
| 所属分组 / Profile | `language` / `full`（新工具默认只在 `full` 中） |
| 安全级别 | 系统主数据（[ADR-0019](../adr/0019-master-data-writes.md)）：每次审批，执行前备份主数据，scratch 内也一样 |
| MCP 注解 | `readOnlyHint` = false　`destructiveHint` = true　`idempotentHint` = true　`openWorldHint` = false |
| 状态 | Implemented (Unreleased) |

读取见 [`eplan_translation_keyword`](eplan_translation_keyword.md)。

## 为什么是这个实现路线

词条写入有两条路：`MDTranslationDatabase` 的 `AddKeyword`／`EditKeyword`，或已有的导出导入。负责人 2026-11-14 选**后者**，因为导入这条路的实机行为已经验证过（api-notes **A47**）：用转换器 `XTrLanguageDbXml2TabConverterImpl` 导入时，新词条被新增、空缺的语言被补上、**已有译文不被改写**，而 `AddKeyword` 这些成员一个都没验证过。

因此本工具的实现就是把请求组成一张表，走 `TranslationWrite`——和 [`eplan_translation_import`](eplan_translation_import.md) 完全同一条管线：导出当前库 → 按导出表头重映射 → 导入 → 再导出 → 比对计数。

**安全口径与 `eplan_translation_import` 一致**：ADR-0019 的审批加主数据备份，不额外要求把翻译库换成 scratch 副本。改的是同一个库、走的是同一个 `ImportDatabaseTexts`，对本工具更严没有道理。（早先的设计稿写过"写入只在 scratch 副本上开放"，那是为 `AddKeyword` 路线写的，随路线一起作废。）

## 能力边界

这条路线能做、且只能做两件事：

- **新增词条**：源文本在库里没有 → 新建，并写入请求里给的各语言译文。
- **补空译文**：源文本已在库里，但某个语言还是空的 → 填上。

不能做，也不假装能做：

| 做不到 | 原因 | 怎么办 |
|---|---|---|
| 改写已有译文 | 合并式导入不覆盖（A47） | 计划与结果里列进 `keptTranslations`；要改走 EPLAN 的翻译字典 |
| 删除词条 | 导入只增不减，无法表达删除 | EPLAN 的翻译字典 |
| 新增语言 | 导入只填已有列，不建列 | 计划阶段返回 INVALID_ARGUMENT 并列出库里现有语言；要加语言在 EPLAN 翻译设置里加 |
| 整库批量 | 超过 200 条属于文件级操作 | `eplan_translation_export` + 编辑 + `eplan_translation_import` |

## 调用路线

| 项目字段 | 记录 |
|---|---|
| 工具名 / 注册入口 / 契约快照 | `eplan_translation_add`，`McpServerSetup` 显式 `.WithTools<TranslationAddTools>`，契约快照 `tests/Contracts.Snapshots/server-contract.verified.json` |
| 操作 | `translation.keyword.set.plan` → [`TranslationKeywordSetPlanOperation`](../../src/EplanMcp.Bridge.Core/Operations/TranslationKeywordSetPlanOperation.cs)；`translation.keyword.set.execute` → [`TranslationKeywordSetExecuteOperation`](../../src/EplanMcp.Bridge.Core/Operations/TranslationKeywordSetExecuteOperation.cs) |
| 共用管线 | [`TranslationWrite`](../../src/EplanMcp.Bridge.Core/Operations/TranslationWrite.cs)，与 `eplan_translation_import` 共用；表的构造与分类在 [`TranslationTable`](../../src/EplanMcp.Bridge.Core/Operations/TranslationTable.cs)（`FromKeywords`、`Classify`、`Remap`、`Compare`） |
| EPLAN 调用 | `MDTranslationDatabase` 的 `ExportDatabaseTexts` 与 `ImportDatabaseTexts`，适配器 [`EplanTranslationDatabaseWriter`](../../src/EplanMcp.Bridge.Eplan/Adapters/EplanTranslationDatabaseWriter.cs)（未改动） |
| 计划与审批 | 变化清单逐条列出新增词条与会被填上的译文（`create` / `update`）；**不会变的不进清单**，因为契约里 `PlannedChange.Action` 只有 create/update，它们进 `Details.keptTranslations` 供审批的人看 |
| 指纹与 PLAN_STALE | 指纹是请求表自身的 SHA-256（`Fingerprint.Of`，行排序后哈希），所以词条换顺序不算变化，改一个字就算；库路径变了也算 |
| 事务、回滚 | 主数据不在项目事务里，没有一步撤销；回退手段是执行前的 `.zw7` 备份，`execution` 按 [ADR-0010](../adr/0010-write-outcomes.md) 返回，导入开始后出错一律 `unknown` |
| 计数来源 | `added`／`filled`／`kept` 来自导入前后两次导出的比对，**不用 EPLAN 的返回值**（A47：没写也返回 true） |
| 测试 | Medium [`TranslationKeywordSetOperationTests`](../../tests/EplanMcp.Bridge.Core.Tests/Operations/TranslationKeywordSetOperationTests.cs) 15 例、Host [`TranslationAddToolTests`](../../tests/EplanMcp.Host.Tests/Tools/TranslationAddToolTests.cs)；待写：Large（scratch 翻译库副本上写一条并核对回读） |

## 参数

| 参数 | 说明 |
|---|---|
| `keywords` | 1–200 条，每条 `{source, translations}`；`source` 为参考语言原文，`translations` 是语言代码到文本的对象 |
| `reference_language` | 每条 `source` 所用语言，默认 `en_US`；必须是库里已有的语言，且不能在 `translations` 里重复出现 |
| `dry_run` | 省略或 true 只预览；false 执行，需审批 |

参数校验一次报齐（源文本为空／重复／含制表符或换行／超 1000 字、语言代码不合法、译文为空、参考语言不合法），路径写成 `'keywords[3].source'`。

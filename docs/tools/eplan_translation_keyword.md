# 工具设计：`eplan_translation_keyword`

设计说明采用[规范附录 T.3](https://github.com/asckye/engineering-standards/blob/v3.0/skills/general-engineering-standard/references/appendix/t-templates.md#tpl-rfc)。本页是**设计**，尚未实现。

| 项目字段 | 记录 |
|---|---|
| 工具名 / 注册入口 / 契约快照 | `eplan_translation_keyword`（待注册；与已有 [`eplan_translation_export`](eplan_translation_export.md)、[`eplan_translation_import`](eplan_translation_import.md)、[`eplan_project_translate`](eplan_project_translate.md) 同组） |
| 分组 / Profile / 状态 | `language` / `full` / **Designed（2026-11-14，未实现）** |
| 当前安全分类与 MCP 注解 | 读：Read，`readOnlyHint` = true；写：系统主数据（[ADR-0019](../adr/0019-master-data-writes.md)），`destructiveHint` = true，每次审批并在执行前备份主数据 |
| 模型说明与参数示例来源 | 待实现时补 Contracts 与 Usage 链接 |
| EPLAN 调用路线 | Bridge 操作 `translation.keyword.plan` / `.execute` → 适配器 → `Eplan.EplApi.MasterData.MDTranslationDatabase`（`Open`、`OpenExclusive`、`FindKeyword`、`FindAllKeywords`、`AddKeyword`、`EditKeyword`、`DeleteKeyword`、`AddLanguage`、`DeleteLanguage`、`Languages`、`NotTranslatedWords`、`IsReadOnly`、`Close`） |
| API 证据 | 尚无实机记录。`MDTranslationDatabase` 的 13 个操作组在 2026-11-14 按 `covered_by_translation_tool` 结案，理由见[结案清单第 8 节](../design/2026-11-14-api-closeout-list.md)；实现时按 api-notes 规则先在 scratch 副本上验证再记录 |
| 原生作用域与生命周期 | 数据库句柄只在一次调用内有效：打开、读或写、关闭都在同一次主线程调用里完成，不跨调用保留；写入包在 `LockingStep` 内，按 ADR-0019 先备份 |
| 预览、审批与审计 | 变化清单逐条列出词条、语言、原值、新值；审批摘要显示数据库路径与是否为副本；审计记录数据库路径的哈希，不回显词条正文之外的项目数据 |
| 事务、回滚与结果恢复 | 主数据不在项目事务里，没有一步撤销；按 ADR-0019 以执行前备份为回退手段，`execution` 按 [ADR-0010](../adr/0010-write-outcomes.md) 返回 |
| 已有测量 | 未测。翻译库可能有上万词条，`FindAllKeywords` 要分页并有软预算 |
| 项目测试与夹具 | Small（参数与副本判断）、Medium（Host 流水线与假 Bridge）、Large（scratch 翻译库副本上读一条、写一条并核对原库哈希不变） |

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

# 工具规格：`eplan_project_translate`

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_project_translate` |
| 所属分组 / Profile | `language` / `full`（M4c 的新工具默认只在 `full` 中） |
| 安全级别 | Destructive：每次审批，执行前备份项目（ADR-0013） |
| MCP 注解 | `readOnlyHint` = false　`destructiveHint` = true　`idempotentHint` = false　`openWorldHint` = false |
| 状态 | Proposed（M4c 第二批） |

## 1. 这个工具解决什么任务
"把项目翻译成英文和中文""新加的那几页也翻译一下"。代理用 EPLAN 配置的系统翻译库，把项目（或指定的页）中的文本译进项目的全部翻译语言。只读翻译库，不改它。

与相近工具的区别：
- 加翻译语言、设显示语言或源语言用 `eplan_language_set`；
- 翻译库里没有的词条，用 `eplan_export_untranslated` 导出缺失词表，交给人翻译后用 `eplan_translation_import` 导入翻译库，再用本工具翻译。

## 2. 输入参数
| 参数 | 类型 | 必填 | 默认值 | 说明（写给模型看的） |
|---|---|---|---|---|
| `project` | string | 是 | — | 项目名，或 `eplan_project_list` 返回的 `path`。写工具必须显式给出 |
| `pages` | array of string，1–200 项 | 否 | 整个项目 | 只翻译这些页上的对象，页名同 `eplan_page_list` 的 `name` |
| `dry_run` | boolean | 否 | false | 为 true 时只返回规划（源语言、翻译语言、可能被补全的文本数），不请求审批，也不修改项目 |

- 译进哪些语言由项目的翻译语言决定，不能另选：`Translate.TranslateObjects` 没有语言参数。要加语言，先用 `eplan_language_set` 的 `add`。

## 3. 输出
`structuredContent`：
- `source`：项目的源语言；`languages`：项目的翻译语言；
- `textsChecked`：检查过的文本数；`textsChanged`：被补上译文的文本数；
- `changed`：前 50 个被改的文本（对象标识、属性、改动前后缺少的语言），`truncated`；
- `stillMissing`：仍缺少某种语言的文本数（翻译库中没有对应词条），附提示"用 `eplan_export_untranslated` 导出缺失词表"；
- `backup`、`undo`、`execution`、`auditId`。

一行摘要：`Translated 37 texts into the project's 20 languages; 5 texts still miss a language because the translation database has no entry for them. Backup: …`

## 4. 错误
| 错误码 | 什么时候发生 | `execution` | 返回给模型的提示 |
|---|---|---|---|
| `INVALID_ARGUMENT` | 项目的源语言未设置（`##_##`） | `not_started` | 🔴 不设源语言就什么也不会翻译（S23 r2）。提示先用 `eplan_language_set` 的 `source` 设为文本所用的语言，如 `de_DE` |
| `INVALID_ARGUMENT` | 项目只有源语言、没有其他翻译语言 | `not_started` | 提示先用 `eplan_language_set` 的 `add` 加入要译成的语言 |
| `PAGE_NOT_FOUND` | `pages` 中有页找不到 | `not_started` | 列出找不到的页名 |
| 其他 | 同 Destructive 写工具的通用规则（审批、备份、读回、超时、取消） | — | — |

- 规划发现没有任何文本缺少翻译语言时返回 `not_started`，不请求审批。

## 5. 实现要点
- **规划**（只读）：
  - 读项目设置 `TRANSLATEGUI.SOURCE_LANGUAGE`、`TRANSLATEGUI.TRANSLATE_LANGUAGES`（A25、A28）；
  - 用与 `eplan_language_remove` 相同的方法读文本：功能的 `FUNC_TEXT`、页的 `PAGE_NOMINATIOMN` 的存储值（A11、A28）；`pages` 给出时只读这些页及其上的功能；
  - 数出缺少至少一种翻译语言的文本，作为"可能被补全"的数量；指纹为这些文本的存储值。
- **审批对话框**：项目、源语言、翻译语言、可能被补全的文本数，"已有的译文按翻译库补全，可能改动大量文本，执行前会备份项目"。
- **执行**：`new Translate().TranslateObjects(对象)`，对象为整个项目（`[project]`）或所选页上的页和功能；在写事务（`SafetyPoint` + `UndoStep` + `QuietModeStep(ShowNoDialogs)`）中执行。
- **已验证**：
  - S23 r0：示例项目整个翻译 2.6–2.9 秒；已全部翻译时文本不变，但改写 `Project`、`Page`、`Blob` 等文件；
  - S23 r2：源语言为 `##_##` 时不翻译、不报错；设为 `de_DE` 后，`de_DE@Motor;` 0.04 秒译进全部 19 种翻译语言；`SafetyPoint` 回滚后文本恢复；
  - 翻译不改项目设置（r2 前后语言设置相同）。
- **读回**：重新读同一批文本，算出 `textsChanged`、`changed`、`stillMissing`；源语言的文本不应改变，变了就按读回不一致处理。
- **未验证**（Large 测试确认）：
  - 已有的译文是否会被翻译库的词条覆盖（示例项目的译文与翻译库一致，看不出来）；
  - 提交后 Ctrl+Z 能否一步撤销；
  - 只翻译所选页时，页上对象以外的文本（如报表、属性放置）是否也被改。
- **时限与进度**：300 秒，每 5 秒一次心跳（`Translating (30 s)`）。

## 6. 写操作专项
- **dry-run**：规划结果。
- **审计**：工具名、项目、`pages`、源语言、翻译语言、`textsChanged`、`stillMissing`、备份路径、`execution`。
- **回滚**：`SafetyPoint`；提交后可按备份恢复。

## 7. 测试
- Small：参数校验、摘要。
- Medium：假 Bridge 上源语言未设置、没有其他翻译语言、页找不到、读回不一致、审批与备份。
- Large（`writes\LargeWrite`）：
  - 源语言为 `##_##` 时返回 `INVALID_ARGUMENT`，不审批、不备份；
  - 用 `eplan_language_set` 设 `source: "de_DE"`，把一个功能文本改为只有德语的 `Motor`（用 `eplan_device_update` 改功能文本），翻译所在的页：`textsChanged` 为 1，`zh_CN` 为 `电机`；
  - 记录已有译文是否被覆盖、Ctrl+Z 的效果；最后把源语言写回 `##_##`。
- Agent 评测："把这页翻译成中文"（代理要先检查源语言和翻译语言）。

# 工具规格：`eplan_language_set`

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_language_set` |
| 所属分组 / Profile | `language` / `full`（M4c 的新工具默认只在 `full` 中） |
| 安全级别 | Write |
| MCP 注解 | `readOnlyHint` = false　`destructiveHint` = false　`idempotentHint` = true　`openWorldHint` = false |
| 状态 | Proposed（M4c 第二批） |

## 1. 这个工具解决什么任务
"让项目在图上同时显示英文和中文""把英文加进项目的翻译语言"。代理改项目的显示语言，或者往翻译语言中加一种语言。不翻译文本（翻译是 `eplan_project_translate`，查清翻译的前提之后再做，S23），也不删除语言（`eplan_language_remove`，Destructive）。

## 2. 输入参数
| 参数 | 类型 | 必填 | 默认值 | 说明（写给模型看的） |
|---|---|---|---|---|
| `project` | string | 是 | — | 项目名，或 `eplan_project_list` 返回的 `path`。写工具必须显式给出 |
| `display` | array of string，1–10 项 | 否 | — | 图上显示的语言，按顺序，如 `["en_US", "zh_CN"]`；每种都必须是项目的翻译语言 |
| `add` | array of string，1–10 项 | 否 | — | 加入翻译语言的语言，如 `["en_US"]`；已有的语言忽略 |
| `dry_run` | boolean | 否 | false | 为 true 时只返回前后的语言设置，不请求审批，也不修改项目 |

- `display` 和 `add` 至少给一个；两个都给时先加语言，再设显示语言。
- 语言代码的形式是 `xx_YY`（EPLAN 的 ISO 代码，如 `de_DE`、`zh_CN`）。

## 3. 输出
```json
{
  "project": "Line1",
  "execution": "committed",
  "dryRun": false,
  "before": { "translation": ["de_DE", "en_US", "zh_CN"], "display": ["##_##"] },
  "after": { "translation": ["de_DE", "en_US", "zh_CN"], "display": ["en_US", "zh_CN"] },
  "undo": null,
  "recovered": false,
  "hint": "Language settings are not undone by Ctrl+Z; call this tool again with the 'before' values to restore them."
}
```
- `before`、`after`：执行前后读出的翻译语言和显示语言（项目设置 `TRANSLATEGUI.TRANSLATE_LANGUAGES`、`DISPLAYED_LANGUAGES`，A28）。`##_##` 表示"未设置"。
- `undo` 为 `null`：项目设置不进撤销步骤（S23 中也不被 `SafetyPoint` 回滚，A28）。
- 一行摘要：`Set the displayed languages to en_US, zh_CN.`

## 4. 错误
| 错误码 | 什么时候发生 | `execution` | 返回给模型的提示 |
|---|---|---|---|
| `INVALID_ARGUMENT` | `display`、`add` 都没给；语言代码形式不对或重复；`display` 中有语言不是翻译语言（也不在 `add` 中） | `not_started` | `metadata.violations` 列出全部问题；`metadata.candidates` 列出项目的翻译语言 |
| `EPLAN_OPERATION_FAILED` | EPLAN 拒绝（`ArgumentException`），或执行后读回的设置与请求不同 | `rolled_back` | 附带 EPLAN 的消息；说明已经写回原来的设置 |
| 策略、审批、流水线、锁定、项目解析、连接与授权类错误 | 同 `eplan_page_create` | 同 `eplan_page_create` | 同 `eplan_page_create` |

## 5. 实现要点
- **Host**：走 `WritePipeline`，规划操作 `language.set.plan`，执行操作 `language.set.execute`。
- **规划**（只读）：读出两项设置；一条 `update` 变更：`Kind = "project"`，`Property = "languages"`，`OldValue`、`NewValue` 为前后的文字（`display: ##_## → en_US, zh_CN`）。指纹：两项设置的原值。
- **执行**：
  1. 记下两项设置的原值；
  2. `add` 中的每种语言：`new Translate().AddProjectLanguage(project, 语言)`；
  3. `display`：`new Translate().SetDisplayedLanguages(project, string.Join(",", display))`。**用逗号分隔**：分号或结尾分号都抛 `ArgumentException`（S23 r1，A28）；
  4. 读回两项设置，与请求不同就写回原值并抛出；EPLAN 抛异常时同样写回原值。
  5. 包在 `TransactionScope` 中照常提交，但不依赖它回滚设置：项目设置不被 `SafetyPoint` 回滚（A28）。
- **已验证**（S23 r1，A28）：`"en_US"` 和 `"en_US,zh_CN"` 被接受，存为 `en_US;` 和 `en_US;zh_CN;`。
- **待 Large 测试确认**：`AddProjectLanguage` 的效果（S23 没有试）；显示语言改变后图上文本的变化；写回原值能把 `##_##` 恢复。

## 6. 写操作专项
- **dry-run**：返回 `before` 和计算出的 `after`，`execution = not_started`。
- **审批**：项目在 scratch 之外时请求审批，表单为一行变更，注明 Ctrl+Z 不能撤销语言设置。
- **审计**：前后的设置。失败时 `execution` 为 `rolled_back` 表示原值已写回；写回也失败时为 `unknown`，`hint` 给出原值，请工程师在项目属性中恢复。
- 幂等：同样的调用再来一次，结果相同。

## 7. 测试
- **Small**：语言代码的校验、逗号拼接（不能用分号）、`after` 的计算、变更清单文字、摘要、schema 快照、注解。
- **Medium**：假 Bridge 上的 `dry_run`、读回不一致时写回、EPLAN 拒绝时写回。
- **Large**（示例项目的 scratch 副本）：`display = ["en_US", "zh_CN"]`：读回 `en_US;zh_CN;`；再调用一次恢复 `before`。
- **Agent 评测**："让 scratch 项目的图上同时显示英文和中文"。

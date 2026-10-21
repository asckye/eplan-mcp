# 工具规格：`eplan_language_remove`

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_language_remove` |
| 所属分组 / Profile | `language` / `full`（M4c 的新工具默认只在 `full` 中） |
| 安全级别 | Destructive |
| MCP 注解 | `readOnlyHint` = false　`destructiveHint` = true　`idempotentHint` = false　`openWorldHint` = false |
| 状态 | Proposed（M4c 第二批） |

## 1. 这个工具解决什么任务
"项目里不需要瑞典语了，删掉"。代理从项目的翻译语言中删除一种或几种语言，连同项目文本中这些语言的全部译文（S23：瑞典语 460 条译文全部删除）。删除的是数据，按 Destructive 处理：每次审批，执行前备份。

## 2. 输入参数
| 参数 | 类型 | 必填 | 默认值 | 说明（写给模型看的） |
|---|---|---|---|---|
| `project` | string | 是 | — | 项目名，或 `eplan_project_list` 返回的 `path`。写工具必须显式给出 |
| `languages` | array of string，1–10 项 | 是 | — | 要删除的语言，如 `["sv_SE"]`；必须是项目的翻译语言，不能是源语言 |
| `dry_run` | boolean | 否 | false | 为 true 时只返回将要删除的语言和受影响的文本数，不请求审批，不备份，也不修改项目 |

## 3. 输出
```json
{
  "project": "Line1",
  "execution": "committed",
  "dryRun": false,
  "removed": ["sv_SE"],
  "textsAffected": 460,
  "before": { "translation": ["de_DE", "en_US", "sv_SE", "zh_CN"], "display": ["##_##"] },
  "after": { "translation": ["de_DE", "en_US", "zh_CN"], "display": ["zh_CN"] },
  "backup": "C:\\Users\\<user>\\EplanMcp\\backups\\Line1\\20261021T083000123Z\\Line1.elk",
  "undo": "AI: eplan_language_remove",
  "recovered": false,
  "hint": "EPLAN also changed the displayed languages to zh_CN; use eplan_language_set to change them back."
}
```
- `textsAffected`：规划时数出的含这些语言译文的文本数（功能文本和页描述，同 S23 的数法）。
- `after.display`：EPLAN 删除语言时会改显示语言（S23：`##_##` → `zh_CN;`），如实列出，`hint` 说明。
- `backup`：同 `eplan_page_delete`。
- 一行摘要：`Removed sv_SE and its 460 translations. A backup was made first (see 'backup').`

## 4. 错误
| 错误码 | 什么时候发生 | `execution` | 返回给模型的提示 |
|---|---|---|---|
| `INVALID_ARGUMENT` | `languages` 为空、重复或形式不对；有语言不是项目的翻译语言；是源语言 | `not_started` | `metadata.violations` 列出全部问题；`metadata.candidates` 列出项目的翻译语言 |
| `BACKUP_FAILED` | 同 `eplan_page_delete` | `not_started` | 同 `eplan_page_delete` |
| `EPLAN_OPERATION_FAILED` | EPLAN 删除时报错 | `rolled_back` 或 `unknown` | 见第 5 节：回滚之后还要写回语言设置；写回失败时为 `unknown`，提示从备份恢复 |
| 策略、审批、流水线、锁定、项目解析、连接与授权类错误 | 同 `eplan_page_delete` | 同 `eplan_page_delete` | 同 `eplan_page_delete` |

## 5. 实现要点
- **Host**：走 `WritePipeline`，级别 Destructive，规划操作 `language.remove.plan`，执行操作 `language.remove.execute`。
- **规划**（只读）：读出语言设置，数出受影响的文本；一条 `delete` 变更：`Kind = "language"`，`Name` 为语言代码。指纹：语言设置和文本的摘要。
- **执行**：Destructive 流水线先备份（ADR-0013），然后：
  1. 记下翻译语言和显示语言的原值；
  2. 在 `TransactionScope` 中 `new Translate().RemoveProjectLanguage(project, 语言)`（逐个）；
  3. 读回；失败或取消时 `SafetyPoint` 回滚**只撤回译文**，所以回滚之后**再把两项语言设置写回原值**（S23：设置不被回滚，A28），写回后读回核对；
  4. 成功时提交，读回 `after`。
- **已验证**（S23，A28）：删除 `sv_SE` 约 3 秒，瑞典语译文 460 → 0，翻译语言中去掉 `sv_SE`，显示语言变为 `zh_CN;`；`SafetyPoint` 回滚后译文恢复，语言设置没有恢复。
- **待 Large 测试确认**：提交后 Ctrl+Z 能撤回什么（译文、设置）；删除多种语言时的耗时。

## 6. 写操作专项
- 同 `eplan_page_delete` 第 6 节（Destructive 流水线、每次审批、先备份、dry-run 不备份），撤销名为"AI: eplan_language_remove"。
- 审批对话框写明：删除这些语言的全部译文，EPLAN 可能同时改显示语言。
- 不标为幂等：再来一次时语言已不在项目中，返回 `INVALID_ARGUMENT`。

## 7. 测试
- **Small**：语言的校验、`textsAffected` 的计数、变更清单文字、摘要、schema 快照、注解。
- **Medium**：假 Bridge 上的 `dry_run`；EPLAN 报错时回滚后写回设置（`rolled_back`）；写回失败时 `unknown`；备份失败时不执行。
- **Large**（示例项目的 scratch 副本）：删除 `sv_SE`：`textsAffected = 460`，`after` 不含 `sv_SE`；`backup` 能打开且仍含瑞典语；记录 Ctrl+Z 的效果。
- **Agent 评测**："把 scratch 项目里的瑞典语删掉"。

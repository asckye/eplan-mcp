# 工具规格：`eplan_page_update`

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_page_update` |
| 所属分组 / Profile | `page` / `core` |
| 安全级别 | Write |
| MCP 注解 | `readOnlyHint` = false　`destructiveHint` = false　`idempotentHint` = true　`openWorldHint` = false |
| 状态 | Proposed（M3a 第 11 步） |

## 1. 这个工具解决什么任务
"把 `=GAA+A1&EFS1/3` 的页描述改成'主回路'""这几页的英文描述补上"。代理给出页名和新的页描述，一次调用改好多页：只审批一次，在 EPLAN 中按一次 Ctrl+Z 撤销。这一版只改页描述；改页名、页类型等以后按需要再加。

## 2. 输入参数
| 参数 | 类型 | 必填 | 默认值 | 说明（写给模型看的） |
|---|---|---|---|---|
| `project` | string | 是 | — | 项目名，或 `eplan_project_list` 返回的 `path`。写工具必须显式给出 |
| `pages` | array，1–50 项 | 是 | — | 要修改的页 |
| `pages[].name` | string | 是 | — | 完整页名，如 `=GAA+A1&EFS1/3`（用 `eplan_page_list` 查） |
| `pages[].description` | string，最长 500 个字符 | 是 | — | `language` 下的新页描述；空串表示清除这种语言的描述 |
| `language` | string | 否 | EPLAN 界面语言 | 要修改的语言，如 `zh_CN`、`en_US`。**其他语言的描述保持不变** |
| `dry_run` | boolean | 否 | false | 为 true 时只返回将要进行的修改，不请求审批，也不修改项目 |

- 页名的查找规则与 `eplan_page_get` 相同：先精确匹配；找不到时，再做不区分大小写的比较，只有唯一一个匹配时才采用。
- 同一次调用中，同一页出现两次时返回 `INVALID_ARGUMENT`。

## 3. 输出
```json
{
  "project": "Line1",
  "execution": "committed",
  "dryRun": false,
  "language": "zh_CN",
  "pages": [
    { "name": "=GAA+A1&EFS1/3", "description": "主回路", "previous": "电源" }
  ],
  "unchanged": 1,
  "undo": "AI: eplan_page_update",
  "recovered": false,
  "hint": null
}
```
- `pages`：有变化的页，带新描述和原描述（`language` 下的文本，原来没有时为 `null`）。提交后是读回的值；`dry_run` 时是将要写入的值。
- `unchanged`：新描述与原描述相同、因而不修改的页数。
- 全部页都没有变化时，计划为空，`execution = not_started`，不请求审批。
- `undo`、`recovered`：同 `eplan_page_create`。
- 页描述按项目数据处理，经过 ResultShaper。
- `language`：省略参数时是 EPLAN 界面语言的代码；规划没有任何变化、又没有给出 `language` 时为 `null`。
- 一行摘要（只有计数，不回显项目名，架构 8.5）：
  - `Updated the descriptions of 3 pages; Ctrl+Z in EPLAN undoes this step.`
  - `Dry run: would update 3 pages. Nothing was changed.`
  - `Nothing to change: the 3 pages already have these descriptions.`
- 变更清单中的属性写作 `description (zh_CN)`，审批表单因此显示是哪种语言：`update page =GAA+A1&EFS1/3: description (zh_CN) '电源' -> '主回路'`。

## 4. 错误
| 错误码 | 什么时候发生 | `execution` | 返回给模型的提示 |
|---|---|---|---|
| `INVALID_ARGUMENT` | 参数不合法：同一页重复、描述过长、`language` 不认识等 | `not_started` | `metadata.violations` 一次列出全部问题，按 `pages[i]` 标明位置 |
| `INVALID_ARGUMENT` | 读回的描述与请求不同，或其他语言的描述被改动 | `rolled_back` | `metadata.page`、`metadata.requested`、`metadata.actual` |
| `PAGE_NOT_FOUND` | 有页名找不到 | `not_started` | `metadata.missing` 列出全部找不到的页名，提示用 `eplan_page_list` 的 `name_prefix` 查准确的页名 |
| `AMBIGUOUS_NAME` | 精确匹配找不到，而不区分大小写时有多个页匹配 | `not_started` | 同 `eplan_page_get` |
| `PLAN_STALE` | 规划之后，这些页的描述被改动过 | `not_started` | 提示重新调用 |
| 策略、审批、流水线、锁定、项目解析、连接与授权类错误 | 同 `eplan_page_create` | 同 `eplan_page_create` | 同 `eplan_page_create` |

## 5. 实现要点
- **Host**：走 `WritePipeline`，规划操作是 `page.update.plan`，执行操作是 `page.update.execute`。执行时限为默认的 60 秒。
- **只改一种语言**：直接赋值一个只含一种语言的 `MultiLangString`，会替换整个属性值，其他语言随之丢失（api-notes A13）。所以执行时：
  1. 读出原值中存放的全部语言（原始串 `de_DE@…;zh_CN@…;`，解析方式同 `TextLanguages`），用 `GetString` 逐个取出文本；
  2. 新建一个 `MultiLangString`，原样加入其他语言，再加入 `language` 的新文本（空串则不加入）；
  3. 写入 `PAGE_NOMINATIOMN`。

  这样不依赖 `AddString` 对已有语言是"替换"还是"追加"，这一点没有验证过。原值是语言无关的文本（`??_??@…;`）时，它也作为一个条目原样保留；这种情况由 Large 测试确认 EPLAN 的显示效果。
- **规划**：
  - 按 `eplan_page_get` 的规则找到每一页，读出 `language` 下的原描述，相同的页计入 `unchanged`；
  - 每页一条 `update` 变更：`Property = "description"`，`OldValue` 和 `NewValue` 是 `language` 下的文本，表单显示为 `update page =GAA+A1&EFS1/3: description '电源' -> '主回路'`；
  - 指纹是这些页的页名和 `PAGE_NOMINATIOMN` 原始串，排序后取 SHA-256。
- **执行**：
  - 先重算指纹，不同就返回 `PLAN_STALE`；
  - 在 `TransactionScope` 中逐页写入，读回原始串，确认 `language` 的文本等于请求、其他语言的文本与写入前相同，不一致就抛出，整体回滚；
  - 全部通过后 `Commit()`，再 `CloseUndo()`。
- **包装**：同 `eplan_page_create`。S2 验证过：修改已有页的描述后回滚，原值能恢复（A7）。
- **幂等**：同样的调用再来一次时，计划为空，不修改项目。

## 6. 写操作专项
- **dry-run**：返回第 3 节的结果，`execution = not_started`。
- **审批**：项目在 scratch 之外时请求审批，表单每页一行，显示原描述和新描述。
- **审计、回滚、各阶段失败时的 `execution`**：同 `eplan_page_create`。

## 7. 测试
- **Small**：
  - 原始串的拼装：替换一种语言、新增一种语言、清除一种语言、保留 `??_??`、其他语言的顺序不变；
  - 同一页重复；摘要；schema 快照；注解。
- **Medium**：在假 Bridge 上测试：
  - `dry_run`；全部没有变化时计划为空；`PAGE_NOT_FOUND` 列出全部缺失；`PLAN_STALE`；
  - 读回不一致时返回 `rolled_back`。
- **Large**（示例项目的 scratch 副本）：
  - 选一页同时有中文和其他语言描述的页，改它的中文描述，其他语言的描述保持不变（用 `eplan_page_get` 的 `language` 分别读回）；
  - 清除中文描述，其他语言仍在；
  - 再调用一次相同的修改，返回"没有变化"。
- **Agent 评测**："把 scratch 项目中 `=GAA+A1&EFS1/1` 的中文页描述改成'主电源'，英文描述不要动"，作答后用 `eplan_page_get` 核对两种语言。

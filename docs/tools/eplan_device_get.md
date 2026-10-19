# 工具规格：`eplan_device_get`

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_device_get` |
| 所属分组 / Profile | `device` / `core` |
| 安全级别 | Read |
| MCP 注解 | `readOnlyHint` = true　`destructiveHint` = false　`idempotentHint` = true　`openWorldHint` = false |
| 状态 | Proposed（M2） |

## 1. 这个工具解决什么任务
"-K1 是什么部件、功能文本是什么、它的触点分布在哪几页？"代理拿到一个设备标识后，用它看设备的全貌：主功能、辅助功能及其所在的页、部件、连接点。

## 2. 输入参数
| 参数 | 类型 | 必填 | 默认值 | 说明（写给模型看的） |
|---|---|---|---|---|
| `project` | string | 否 | EPLAN 中选中的项目 | 项目名，或 `eplan_project_list` 返回的 `path` |
| `tag` | string | 是 | — | 设备标识。完整标识（如 `=GAA+A1-FC1`）总是唯一；短标识（如 `-FC1`）在多个结构下出现时返回 `AMBIGUOUS_NAME` |
| `response_format` | enum `concise` / `detailed` | 否 | `concise` | `detailed` 额外返回主功能全部非空的属性 |
| `language` | string | 否 | EPLAN 界面语言 | 多语言文本的语言 |

## 3. 输出
```json
{
  "project": "Line1",
  "device": {
    "tag": "=F+A2-KF1", "visibleTag": "-KF1", "category": "Coil", "page": "=F+A2&EFS1/1",
    "text": "…", "pins": ["A1", "A2"]
  },
  "functions": [ { "tag": "=F+A2-KF1", "category": "NoContact", "page": "=F+A2&EFS1/2", "pins": ["13", "14"] } ],
  "parts": [ { "partNumber": "…", "variant": "1", "count": 1, "description": "…", "manufacturer": "…" } ],
  "properties": null,
  "truncated": false,
  "hint": null
}
```
- `device`：主功能。`text` 是功能文本 `FUNC_TEXT` 在 `language` 下的显示文本，没有时为 `null`。
- `functions`：辅助功能（`SubFunctions`），各自所在的页和连接点名称，最多 50 个。
- `pins`：连接点名称；没有名称的连接点（S6 中端子等的连接点名称为空串）不列出。
- `parts`：主功能的部件引用（`PartNr`、`VariantNr`、`Count`），描述和制造商取自项目中的部件数据（`Project.Articles`），**不访问部件库**。
- `properties`（仅 `detailed`）：`[{ "id": 20011, "index": null, "name": "…", "value": "…" }]`，结构与 `eplan_project_get` 相同：只含非空、非内部的属性；带索引的属性每个已用索引一条；用户自定义属性的 `id` 为 `null`。
- `truncated` / `hint`：辅助功能超过 50 个，或者超出 token 预算（`concise` 约 5k、`detailed` 20k；依次裁掉属性、辅助功能、部件）时为 true，`hint` 说明少了什么、怎样取全（辅助功能用 `eplan_device_list` 的 `tag_contains` + `include_auxiliary`）。
- 所有项目文本经过 ResultShaper；一行摘要：`Device found: Coil, 3 auxiliary functions, 1 part.`，不回显标识和文本。

## 4. 错误
| 错误码 | 什么时候发生 | 返回给模型的提示 |
|---|---|---|
| `INVALID_ARGUMENT` | 缺少 `tag`，或其他参数不合法 | `metadata.violations` 列出全部问题 |
| `DEVICE_NOT_FOUND` | 没有这个标识 | 提示用 `eplan_device_list` 的 `tag_contains` 查 |
| `AMBIGUOUS_NAME` | 短标识对应多个设备 | `metadata.candidates` 列出完整标识，提示选一个再调用；问题涉及全部候选时（在哪些页、有几个），提示改用一次 `eplan_device_list`（`tag_contains`），而不是对每个候选调用一次 `eplan_device_get`（第一次 Agent 评测中模型调用了 14 次） |
| `PROJECT_NOT_OPEN` | 项目解析失败 | 同 `eplan_project_get` |
| 连接与授权类错误 | 同 `eplan_project_list` | 同 `eplan_project_list` |

## 5. 实现要点
- 查找顺序：先用 `FunctionsFilter { Name = tag, ExactNameMatching = true }` 按完整标识精确匹配主功能（再按 `Ordinal` 复核，区分大小写；项目中同一完整标识有多个主功能时取 EPLAN 顺序中的第一个）；找不到时在主功能中按去掉 `>` 的 `VisibleName` 比较（不区分大小写，`tag` 也去掉 `>`），一个时采用，多个时返回 `AMBIGUOUS_NAME`（`ProjectResolver.Ambiguous`，候选为去重后的完整标识，最多 20 个）。
- 部件描述：`Article` 的 `ARTICLE_DESCR1`、`ARTICLE_MANUFACTURER`，用 `GetStringToDisplay`；**不调用 `Article.LoadFromMasterdata()`**（会写入项目，已列入 BannedSymbols）。
- `parts` 只从 `ArticleReferences` 取，不读带索引的部件属性（如 `FUNC_ARTICLE_PARTNR`）。`detailed` 的属性用共用的 `EplanProperties` 读取，带索引的属性按 `Indexes` 逐个索引读取（S6：不带索引读取会抛异常），因此部件编号也会作为带索引的属性出现在 `properties` 中。

## 6. 写操作专项
不适用。

## 7. 测试
- Small：参数校验、摘要、辅助功能截断。
- Medium：假 Bridge 上的完整标识命中、短标识唯一、短标识歧义、找不到。
- Large：示例项目副本上，`-FC1` 返回 `AMBIGUOUS_NAME`，候选包含 `=GAA+A1-FC1` 和 `=GAB1+A1-FC1`；`=F+A2-KF1` 的连接点为 `A1`、`A2`（S6）。
- Agent 评测："-KF1 用的是什么部件？它的触点在哪几页？"

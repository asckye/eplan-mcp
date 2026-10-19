# 工具规格：`eplan_part_list`

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_part_list` |
| 所属分组 / Profile | `part` / `core` |
| 安全级别 | Read |
| MCP 注解 | `readOnlyHint` = true　`destructiveHint` = false　`idempotentHint` = true　`openWorldHint` = false |
| 状态 | Proposed（M2） |

## 1. 这个工具解决什么任务
"项目里用了哪些西门子的部件？""3RT2016 用了几个？"代理按部件汇总项目的用料，相当于一份精简的部件清单。部件库查询（`eplan_part_search`）属于 M3（M2 设计 Q4）。

## 2. 输入参数
| 参数 | 类型 | 必填 | 默认值 | 说明（写给模型看的） |
|---|---|---|---|---|
| `project` | string | 否 | EPLAN 中选中的项目 | 项目名，或 `eplan_project_list` 返回的 `path` |
| `manufacturer` | string | 否 | — | 制造商名称中包含的文字，不区分大小写 |
| `text` | string | 否 | — | 部件号或描述中包含的文字，不区分大小写 |
| `language` | string | 否 | EPLAN 界面语言 | 部件描述的语言 |
| `limit` | integer，1–200 | 否 | 50 | 每页最多条数 |
| `cursor` | string | 否 | — | 上一次结果中的 `nextCursor` |

## 3. 输出
```json
{
  "project": "Line1",
  "parts": [
    { "partNumber": "…", "variant": "1", "quantity": 4, "devices": 3, "connections": 0, "description": "…", "manufacturer": "…" }
  ],
  "total": 293,
  "nextCursor": "…",
  "truncated": false
}
```
- 按"部件号 + 变型号"汇总全部部件引用：`quantity` 是各引用 `Count` 之和；`devices`、`connections` 分别是引用它的设备数和连接数（S6：示例项目的 2900 个部件引用中有挂在导线上的）。
- 描述和制造商取自项目中的部件数据（`Project.Articles`），不访问部件库；项目中没有对应部件数据时为 `null`。
- 顺序：按部件号排序（序数比较），保证分页稳定。
- 一行摘要：`Listed 50 of 293 parts (more: cursor).`

## 4. 错误
| 错误码 | 什么时候发生 | 返回给模型的提示 |
|---|---|---|
| `INVALID_ARGUMENT` | 参数不合法 | `metadata.violations` 列出全部问题 |
| `PROJECT_NOT_OPEN` / `AMBIGUOUS_NAME` | 项目解析失败 | 同 `eplan_project_get` |
| 连接与授权类错误 | 同 `eplan_project_list` | 同 `eplan_project_list` |

## 5. 实现要点
- `DMObjectsFinder.GetArticleReferences(null)`（S6：2900 个，36 ms），按 `PartNr` + `VariantNr` 分组；`ParentObject` 是 `Connection` 时计入 `connections`。
- 部件数据：`Project.Articles`（S6：293 个，10 ms），读 `ARTICLE_PARTNR`、`ARTICLE_DESCR1`、`ARTICLE_MANUFACTURER`，多语言用 `GetStringToDisplay`。**不调用 `Article.LoadFromMasterdata()`**（BannedSymbols）。
- 过滤在内存中完成；游标指纹包含项目和全部过滤条件。

## 6. 写操作专项
不适用。

## 7. 测试
- Small：汇总逻辑（同一部件多个引用、导线上的引用）、过滤、摘要。
- Medium：假 Bridge 上的分页。
- Large：示例项目副本上，部件引用总数与 S6 一致（2900）。
- Agent 评测："项目里用了哪些制造商的部件？各有多少种？"

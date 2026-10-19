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

- 字符串参数的默认值是空串而不是 `null`（与 `eplan_device_list` 相同，节省描述预算）；空白等同于没给。
- `manufacturer` 匹配项目部件数据中制造商的**简称**（如 `SIE`），不是公司全名；用法说明中提示模型先不加过滤列出，再看 `manufacturer` 的取值。

## 3. 输出
```json
{
  "project": "Line1",
  "parts": [
    { "partNumber": "…", "variant": "1", "quantity": 4, "devices": 3, "connections": 0, "description": "…", "manufacturer": "…" }
  ],
  "total": 293,
  "nextCursor": "…",
  "truncated": false,
  "hint": null
}
```
- 按"部件号 + 变型号"汇总全部部件引用：`quantity` 是各引用 `Count` 之和；`devices`、`connections` 分别是引用它的设备数和连接数（S6：示例项目的 2900 个部件引用中有挂在导线上的）。
- 描述和制造商取自项目中的部件数据（`Project.Articles`），不访问部件库；项目中没有对应部件数据时为 `null`。
- 没有部件号的引用不计入。设备按完整标识去重计数；没有名称的功能无法区分，各算一个。
- 项目中没有该变型的部件数据时，用同一部件号其他变型的数据代替（与 `eplan_device_get` 相同）。
- `variant`、`description`、`manufacturer` 为空时返回 `null`。
- 顺序：按部件号排序（序数比较），再按变型号，保证分页稳定。超出 token 预算时由 ResultShaper 截断，`hint` 说明如何继续。
- 一行摘要：`Listed 4 parts.` / `Listed 50 of 293 parts (more: cursor).`，不回显部件号和描述。

## 4. 错误
| 错误码 | 什么时候发生 | 返回给模型的提示 |
|---|---|---|
| `INVALID_ARGUMENT` | `limit` 越界、`language` 格式不对或 EPLAN 不认识、游标不是本工具为同样参数返回的 | `metadata.violations` 列出全部问题；语言是否被 EPLAN 认识由 Bridge 检查 |
| `PROJECT_NOT_OPEN` / `AMBIGUOUS_NAME` | 项目解析失败 | 同 `eplan_project_get` |
| 连接与授权类错误 | 同 `eplan_project_list` | 同 `eplan_project_list` |

## 5. 实现要点
- `DMObjectsFinder.GetArticleReferences(null)`（S6：2900 个，36 ms），按 `PartNr` + `VariantNr` 分组；`ParentObject` 是 `Connection` 时计入 `connections`。
- 部件数据：`Project.Articles`（S6：293 个，10 ms），读 `ARTICLE_PARTNR`、`ARTICLE_DESCR1`、`ARTICLE_MANUFACTURER`，多语言用 `GetStringToDisplay`。**不调用 `Article.LoadFromMasterdata()`**（BannedSymbols）。
- 汇总、过滤、排序和切片在 Bridge.Core（`PartListOperation`）中一次主线程调度内完成，适配层（`EplanPartReader`）只读取原始记录。
- 过滤在内存中完成：`manufacturer` 匹配制造商，`text` 匹配部件号或描述，都是不区分大小写的包含；两者同时给出时取"与"。
- 游标指纹包含项目、`manufacturer`、`text` 和 `language`：`text` 也匹配描述，而描述按 `language` 读取，换了语言结果可能不同。
- 待 Large 测试确认（S6 没有直接验证）：
  - 示例项目上一次调用的总耗时（S6 分别测得引用 36 ms、部件数据 10 ms，汇总和转换的时间未测）。
  - 部件引用挂在项目本身（`ParentObject` 是 `Project`）的情况是否存在，以及它们在 `devices`、`connections` 之外如何体现。
  - 同一部件号多个变型时，项目部件数据中各变型的描述是否不同。

## 6. 写操作专项
不适用。

## 7. 测试
- Small：汇总逻辑（同一部件多个引用、导线上的引用）、过滤、摘要。
- Medium：假 Bridge 上的分页。
- Large：示例项目副本上，部件引用总数与 S6 一致（2900）。
- Agent 评测："项目里用了哪些制造商的部件？各有多少种？"

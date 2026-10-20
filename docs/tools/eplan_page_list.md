# 工具规格：`eplan_page_list`

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_page_list` |
| 所属分组 / Profile | `page` / `core` |
| 安全级别 | Read |
| MCP 注解 | `readOnlyHint` = true　`destructiveHint` = false　`idempotentHint` = true　`openWorldHint` = false |
| 状态 | Proposed（M2） |

## 1. 这个工具解决什么任务
"列出 =GAA 下的所有页""有哪些端子图""页描述里带'电源'的是哪几页"。代理用它按结构、页类型或页描述找到页，再用 `eplan_page_get` 看某一页的内容。示例项目有 321 页，所以过滤在服务端完成，结果分页。

## 2. 输入参数
| 参数 | 类型 | 必填 | 默认值 | 说明（写给模型看的） |
|---|---|---|---|---|
| `project` | string | 否 | EPLAN 中选中的项目 | 项目名，或 `eplan_project_list` 返回的 `path` |
| `name_prefix` | string | 否 | — | 完整页名的前缀，**区分大小写**，如 `=GAA+A1`、`=GAA+A1&EFS1/` |
| `page_type` | string | 否 | — | EPLAN 页类型名，如 `Circuit`、`TerminalDiagram`、`TitlePage`；完整列表见 `eplan_tool_usage`（`fields`） |
| `text` | string | 否 | — | 页描述中包含的文字，不区分大小写，按 `language` 的语言匹配 |
| `language` | string | 否 | EPLAN 界面语言，没有文本时回退到其他语言 | 页描述的语言，如 `zh_CN`、`en_US` |
| `limit` | integer，1–200 | 否 | 50 | 每页最多条数 |
| `cursor` | string | 否 | — | 上一次结果中的 `nextCursor` |

- `page_type` 有 75 个取值，写进 schema 的 `enum` 会超出描述预算（mcp-tool-design 4.2），所以由服务端校验，取值不合法时返回 `INVALID_ARGUMENT` 并列出常用取值。
- 多个过滤条件之间是"与"。

## 3. 输出
```json
{
  "project": "Line1",
  "pages": [
    { "name": "=GAA+A1&EFS1/1", "type": "Circuit", "description": "电源" }
  ],
  "total": 140,
  "nextCursor": "…",
  "truncated": false,
  "hint": null
}
```
- 顺序：EPLAN 自身的页顺序（`DMObjectsFinder.GetPages` 的返回顺序，S6 确认不是简单的字符串排序）。
- `name`：`Page.Name`，即完整页名（api-notes A11）。`description`：页描述 `PAGE_NOMINATIOMN` 在 `language` 下的显示文本，经过不可信文本处理；没有页描述时为 `null`。省略 `language` 时按[语言回退设计](../design/2026-10-19-text-language-fallback.md)回退，回退时有 `descriptionLanguage`，`text` 也匹配回退后的描述。
- 超出 token 预算时由 ResultShaper 截断，`truncated` 和 `nextCursor` 指向第一条未返回的页。
- 一行摘要：`Listed 50 of 140 pages (more: cursor).`，不回显页名和页描述。
- 不提供 `response_format`：每页 3 个字段；需要更多信息用 `eplan_page_get`。

## 4. 错误
| 错误码 | 什么时候发生 | 返回给模型的提示 |
|---|---|---|
| `INVALID_ARGUMENT` | `page_type` 不认识、`limit` 越界、`cursor` 无效或属于另一组过滤条件 | `metadata.violations` 列出全部问题 |
| `PROJECT_NOT_OPEN` / `AMBIGUOUS_NAME` | 项目解析失败 | 同 `eplan_project_get` |
| 连接与授权类错误 | 同 `eplan_project_list` | 同 `eplan_project_list` |

## 5. 实现要点
- `DMObjectsFinder(project).GetPages(filter)`：`name_prefix` 直接用 `PagesFilter.Name`（前缀匹配，区分大小写，S6），`page_type` 用 `PagesFilter.DocumentType`；`text` 在内存中按页描述过滤（Bridge.Core，`OrdinalIgnoreCase`）。两个条件都没有时传 `null`（S6 验证过 `null` 返回全部页）。
- `page_type` 在 Bridge.Core 中对照适配层给出的枚举名校验，不区分大小写，传给 EPLAN 的是枚举的原始写法。不接受 `Undefined`：没有页是这个类型，而且它很可能是过滤器"不限类型"的默认值。`PagesFilter.DocumentType` 的过滤效果 S6 没有单独验证，由 Large 测试与 `eplan_project_get` 的 `pagesByType` 对照确认。
- 分页：`Paging.Slice`（Bridge），游标指纹包含项目、`name_prefix`、`page_type`、`text`、`language`。
- 耗时：321 页全部取出 7 ms（S6），读页描述在 Large 测试中测量。

## 6. 写操作专项
不适用。

## 7. 测试
- Small：参数校验（包括 `page_type` 的错误提示）、游标指纹、摘要。
- Medium：假 Bridge 上的过滤组合与分页。
- Large：示例项目副本上，`name_prefix = "&AAA1"` 返回 3 页（S6）；按 `page_type` 过滤的结果与 `eplan_project_get` 的 `pagesByType` 一致。
- Agent 评测："列出所有端子图的页名"；"页描述里有'电源'的是哪几页？"

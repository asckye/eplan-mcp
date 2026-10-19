# 工具规格：`eplan_device_list`

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_device_list` |
| 所属分组 / Profile | `device` / `core` |
| 安全级别 | Read |
| MCP 注解 | `readOnlyHint` = true　`destructiveHint` = false　`idempotentHint` = true　`openWorldHint` = false |
| 状态 | Proposed（M2） |

## 1. 这个工具解决什么任务
"列出所有接触器""=GAA+A1 下有哪些设备""哪些设备没有部件""-K1 在哪里"。代理按标识、结构、页、类别或"是否有部件"找到设备，再用 `eplan_device_get` 看细节。示例项目有 2876 个功能，过滤在服务端完成，结果分页。

## 2. 输入参数
| 参数 | 类型 | 必填 | 默认值 | 说明（写给模型看的） |
|---|---|---|---|---|
| `project` | string | 否 | EPLAN 中选中的项目 | 项目名，或 `eplan_project_list` 返回的 `path` |
| `tag_contains` | string | 否 | — | 完整标识或页面上显示的标识中包含的文字，不区分大小写，如 `-K1`、`=GAA+A1` |
| `page` | string | 否 | — | 只列这一页上的设备，完整页名 |
| `category` | string | 否 | — | EPLAN 功能类别名，如 `Coil`、`Motor`、`CircuitBreaker`、`Terminal`、`PLCBox`、`Cable`；完整列表见 `eplan_tool_usage`（`fields`） |
| `has_part` | boolean | 否 | — | `false`：只列没有部件的设备；`true`：只列有部件的设备 |
| `include_auxiliary` | boolean | 否 | false | 是否包含辅助功能（触点、端子的连接点等）；默认只列主功能 |
| `limit` | integer，1–200 | 否 | 50 | 每页最多条数 |
| `cursor` | string | 否 | — | 上一次结果中的 `nextCursor` |

- `category` 有 183 个取值，与 `eplan_page_list` 的 `page_type` 一样由服务端校验。
- 多个过滤条件之间是"与"。

## 3. 输出
```json
{
  "project": "Line1",
  "devices": [
    { "tag": "=GAA+A1-FC1", "visibleTag": "-FC1", "category": "CircuitBreaker", "page": "=GAA+A1&EFS1/1", "parts": 2 }
  ],
  "total": 37,
  "nextCursor": null,
  "truncated": false,
  "hint": null
}
```
- `tag`：`Function.Name`，完整标识，**是其他工具引用这个设备时应该使用的值**。`visibleTag`：页面上显示的标识，去掉 `>` 标记；可能为空（api-notes A11）。
- `page`：主功能所在的页；没有放置在页上（如定义文本类对象）时为 `null`。`parts`：部件引用的数量。
- 没有名称的功能（`Name` 为空）不列出。
- 顺序：EPLAN 的返回顺序。超出 token 预算时由 ResultShaper 截断。
- 一行摘要：`Listed 37 devices.` / `Listed 50 of 2876 devices (more: cursor).`，不回显标识。

## 4. 错误
| 错误码 | 什么时候发生 | 返回给模型的提示 |
|---|---|---|
| `INVALID_ARGUMENT` | `category` 不认识、`limit` 越界、`cursor` 无效 | `metadata.violations` 列出全部问题 |
| `PAGE_NOT_FOUND` | `page` 指定的页不存在 | 提示用 `eplan_page_list` 查页名 |
| `PROJECT_NOT_OPEN` / `AMBIGUOUS_NAME` | 项目解析失败 | 同 `eplan_project_get` |
| 连接与授权类错误 | 同 `eplan_project_list` | 同 `eplan_project_list` |

## 5. 实现要点
- `DMObjectsFinder.GetFunctions(filter)`：`page` 用 `FunctionsFilter.Page`，`category` 用 `FunctionsFilter.FunctionCategory`；`tag_contains`、`has_part`、主功能筛选在内存中完成（S6：2876 个功能取出 64 ms、读名称 18 ms）。
- `tag_contains` 匹配 `Name` 和去掉 `>` 的 `VisibleName`，所以 `-K1` 能找到所有结构下的 K1。
- `parts`：`Function.ArticleReferences.Length`（S6：逐个功能汇总 86 ms）。
- 游标指纹包含项目和全部过滤条件。

## 6. 写操作专项
不适用。

## 7. 测试
- Small：参数校验（含 `category`）、`visibleTag` 去掉 `>`、摘要。
- Medium：假 Bridge 上的过滤组合、分页、无名功能被跳过。
- Large：示例项目副本上，`tag_contains = "-FC1"` 同时返回 `=GAA+A1-FC1` 和 `=GAB1+A1-FC1`（S6）。
- Agent 评测："哪些电机没有部件？"；"列出 =GAA+A1 下的所有设备"。

# 工具规格：`eplan_page_get`

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_page_get` |
| 所属分组 / Profile | `page` / `core` |
| 安全级别 | Read |
| MCP 注解 | `readOnlyHint` = true　`destructiveHint` = false　`idempotentHint` = true　`openWorldHint` = false |
| 状态 | Proposed（M2） |

## 1. 这个工具解决什么任务
"=GAA+A1&EFS1/1 这页上有哪些设备？""这页是什么类型、页描述是什么？"代理知道页名后，用它看一页的概况和页上的设备；需要设备的细节时，再用 `eplan_device_get`。

## 2. 输入参数
| 参数 | 类型 | 必填 | 默认值 | 说明（写给模型看的） |
|---|---|---|---|---|
| `project` | string | 否 | EPLAN 中选中的项目 | 项目名，或 `eplan_project_list` 返回的 `path` |
| `page` | string | 是 | — | 完整页名，如 `=GAA+A1&EFS1/1`（用 `eplan_page_list` 查） |
| `response_format` | enum `concise` / `detailed` | 否 | `concise` | `detailed` 额外返回全部非空的页属性 |
| `language` | string | 否 | EPLAN 界面语言 | 多语言文本的语言 |

## 3. 输出
```json
{
  "project": "Line1",
  "page": { "name": "=GAA+A1&EFS1/1", "type": "Circuit", "typeName": "（EPLAN 界面语言的页类型名）", "description": "电源" },
  "devices": [ { "tag": "=GAA+A1-FC1", "visibleTag": "-FC1", "category": "CircuitBreaker" } ],
  "deviceCount": 4,
  "properties": null,
  "truncated": false,
  "hint": null
}
```
- `devices`：页上的设备，即带标识的主功能（`IsMainFunction` 且 `Name` 非空，与 `eplan_project_get` 的设备计数同一规则），按 EPLAN 的顺序；`tag` 是 `Function.Name`（完整标识），`visibleTag` 是页面上显示的标识（去掉 `>` 标记，去掉后为空时为 `null`），`category` 是 `FunctionCategory` 的枚举名（api-notes A11）。最多 100 个，超出时 `truncated = true`，`hint` 提示用 `eplan_device_list` 的 `page` 过滤。`deviceCount` 是页上设备的总数。
- `typeName`：EPLAN 界面语言的页类型名称（S6：如"标题页/封页"）。
- `properties`（仅 `detailed`）：同 `eplan_project_get`（含 `index`）。
- 大小：`concise` 默认 5k token，`detailed` 上限 20k token；超出时先从末尾去掉属性，仍然超出（页上设备的标识异常长）再去掉设备，`truncated` 为 true，`hint` 说明。
- 一行摘要：`Page found: Circuit, 4 devices.`，不回显页名和页描述；有内容被去掉时追加 `Some entries were left out; see 'hint'.`

## 4. 错误
| 错误码 | 什么时候发生 | 返回给模型的提示 |
|---|---|---|
| `INVALID_ARGUMENT` | 缺少 `page`，或其他参数不合法 | `metadata.violations` 列出全部问题 |
| `PAGE_NOT_FOUND` | 没有这个页名（不区分大小写也找不到） | "Use eplan_page_list with name_prefix to find the exact page name."，`metadata.page` 为传入的值，`metadata.project` 为项目名 |
| `AMBIGUOUS_NAME` | 精确匹配找不到，而不区分大小写时有多个页匹配（页名只差大小写） | `metadata.candidates` 列出这些页的完整页名，提示原样传入其中一个 |
| `PROJECT_NOT_OPEN` / `AMBIGUOUS_NAME` | 项目解析失败 | 同 `eplan_project_get` |
| 连接与授权类错误 | 同 `eplan_project_list` | 同 `eplan_project_list` |

## 5. 实现要点
- 查页：`PagesFilter { Name = page, ExactNameMatching = true }`。找不到时，再在内存中做一次不区分大小写的精确比较，只有唯一匹配时才采用（页名过滤区分大小写，S6）；多个匹配时返回 `AMBIGUOUS_NAME`。比较逻辑在 Bridge.Core（`PageGetOperation`），适配层只提供"按精确页名读一页"和"全部页名"。
- 缺少 `page`：Host 的参数过滤器按 schema 的 `required` 在调用前返回 `INVALID_ARGUMENT`（SDK 自己只返回没有 reason 的通用错误，api-notes C）；显式传 `null` 或空串时由工具本身报告。
- 页上的设备：`FunctionsFilter { Page = page }` 后取主功能（S6：0 ms）。不使用 `Page.Filter`（已列入 BannedSymbols）。
- 多语言文本用 `GetStringToDisplay`；所有项目文本经过 ResultShaper。

## 6. 写操作专项
不适用。

## 7. 测试
- Small：参数校验、摘要、设备列表截断。
- Medium：假 Bridge 上的找到、找不到、大小写回退。
- Large：示例项目副本上，`=GAA+A1&EFS1/1` 页的主功能包括 `=GAA+A1-FC1`、`=GAA+A1-PF1`、`=GAA+A1-TA1`（S6 的样本）。
- Agent 评测："=GAA+A1&EFS1/1 这页上有哪些设备？"

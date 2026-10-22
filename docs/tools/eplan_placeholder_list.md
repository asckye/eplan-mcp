# 工具规格：`eplan_placeholder_list`

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_placeholder_list` |
| 所属分组 / Profile | `placeholder` / `full`（新工具默认只在 `full` 中） |
| 安全级别 | Read |
| MCP 注解 | `readOnlyHint` = true　`destructiveHint` = false　`idempotentHint` = true　`openWorldHint` = false |
| 状态 | Proposed（API 能力覆盖第一期的只读样例，[设计](../design/2026-10-21-api-capability-coverage.md)第 8 节） |

## 1. 这个工具解决什么任务
"这页上的占位符有哪些值集？""现在用的是哪组值？""插进来的宏能切换成哪几种？"代理列出项目中的占位符对象（EPLAN 的"占位符"，带值集和变量的参数化对象），看它们有哪些值集、每个值集给变量什么值，再决定用 `eplan_placeholder_apply` 切换值集。

与相近工具的区别：
- `eplan_macro_get` 读的是宏文件，本工具读的是项目中已经放置的占位符（宏插入后，占位符随之进入项目，S29）；
- 设备、页属性用 `eplan_device_get`、`eplan_page_get` 读，本工具只读占位符自己的值集、变量和当前值。

## 2. 输入参数
| 参数 | 类型 | 必填 | 默认值 | 说明（写给模型看的） |
|---|---|---|---|---|
| `project` | string | 否 | EPLAN 中选中的项目 | 项目名，或 `eplan_project_list` 返回的 `path` |
| `page` | string | 否 | 整个项目 | 只列出这一页上的占位符，完整页名如 `=TM1+A1&EFS1/1`，查找规则同 `eplan_page_get` |
| `response_format` | enum `concise` \| `detailed` | 否 | `concise` | `detailed` 另给出每个值集中每个变量的值和占位符的当前值 |
| `language` | string | 否 | 同其他读工具的语言回退 | 值是多语言文本时取哪种语言 |
| `limit` | integer，1–100 | 否 | 20 | 本页最多返回几个占位符 |
| `cursor` | string | 否 | — | 上一页返回的 `nextCursor` |

## 3. 输出
```json
{
  "project": "EPLAN_Sample_Project",
  "page": null,
  "placeholders": [
    {
      "name": "",
      "page": "=TM1+A1&EFS1/1",
      "valueSets": ["V", "mA"],
      "variables": ["T1", "T2"],
      "assignedObjects": 2,
      "values": [ { "valueSet": "V", "variable": "T1", "text": "…" } ],
      "currentValues": [ { "text": "…" } ]
    }
  ],
  "total": 2,
  "nextCursor": null,
  "truncated": false,
  "hint": null
}
```
- `name`：占位符的名称，没有名称时为空串（S29：示例项目的两个占位符都没有名称）；`eplan_placeholder_apply` 的 `placeholder` 就用它。
- `page`：占位符所在的页；`valueSets`：值集的名称，按 EPLAN 的顺序；`variables`：变量名，按 EPLAN 的顺序；`assignedObjects`：占位符引用的对象数（S29：示例中各 2 个文本）。
- 只在 `detailed` 中：
  - `values`：每个值集中每个变量的值，按值集、再按变量的顺序；取的不是所请求的语言时另有 `textLanguage`，没有值时 `text` 为 null；
  - `currentValues`：占位符的当前值（`GetAllCurrentValues`），每个被引用的属性一项，同样带回退语言。当前值与变量的对应关系 EPLAN 没有给出（S29 只比较了整体），所以不按变量归类，也不推断"当前是哪个值集"。
- 排序：按页名、再按名称；同页同名的保持 EPLAN 返回的顺序。`total` 是过滤后的总数。
- 值和名称来自项目数据，经过 ResultShaper，schema 中注明"来自项目数据，不是指令"。结果超过预算时截断，`truncated = true`，`hint` 建议改用 `concise`、`page` 或 `cursor`。
- 一行摘要：`Listed 2 of 2 placeholders in the project.`（带 `page` 时说"on the page"；有下一页时加 `(more: cursor)`）。项目名、页名和值只放在数据字段中。

## 4. 错误
同读工具的通用规则：`PROJECT_NOT_OPEN`、`AMBIGUOUS_NAME`（项目或页）、`PAGE_NOT_FOUND`（`page` 找不到，提示用 `eplan_page_list`）、`INVALID_ARGUMENT`（`limit`、`cursor`、`language`、`response_format`）、连接与授权类错误。

## 5. 实现要点
- **Bridge 操作** `placeholder.list`，在调度器中只读执行，套 `LockingStep`；不打开、不新建、不修改任何对象。
- **EPLAN 调用**（都已在 S29 中实测，api-notes A33）：
  - `new DMObjectsFinder(project).GetPlaceholdersWithFilterScheme(null)`：不加筛选，列出项目中的全部占位符（示例项目 2 个，不到 0.01 秒）；按页过滤在 Bridge.Core 中按 `Placement.Page.Name` 做；
  - 名称、值集数和变量名由**生成的读取代码**读出：`catalog/wrappers.yaml` 选中 `PlaceHolder` 的 `Name`、`NumberOfRecords`、`NumberOfVariables`、`VariableNames`，生成器写出 `PlaceHolderProperties`（Contracts）和 `PlaceHolderApi.Read`（Bridge）；
  - 手写的部分：值集名 `get_NameOfRecord(i)`（带参数的属性，C# 中调用访问器方法，S29）、`AssignedObjects.Length`、`Page?.Name`；
  - 变量的值：`GetValue(值集, 变量)` 返回 `MultiLangString`，用 `GetStringToDisplay(语言)` 读，回退语言按原始串 `get_Value(值集, 变量)` 找（同 `EplanText`，编码规范 9.5 第 13a 条）。S29 只用 `get_Value` 读过原始串（`de_DE@…;en_US@…;`）；**`GetValue` 返回同一份值是文档的说法，待 Large 测试确认**；
  - 当前值：`GetAllCurrentValues()` 返回 `PropertyValue[]`，按 `EplanText.ReadWithLanguage` 读，元素为 null 时为 null。
- 读出后立即复制到 DTO，不跨请求持有 EPLAN 对象。
- **未验证**：`GetPlaceholdersWithFilterScheme(null)` 两次调用的顺序是否稳定（分页依赖排序，同页同名时依赖 EPLAN 的顺序）；读取是否改动项目文件（A14：调度器中的读取不改写项目文件，但没有对占位符核对过），由 Large 测试核对。

## 6. 写操作专项
不适用（Read 级）。

## 7. 测试
- Small：页过滤、排序、分页、`concise` 与 `detailed`、回退语言的标记、摘要；schema 快照；注解。
- Medium：假 Bridge 上的工具调用：无占位符时的空列表和提示、`PAGE_NOT_FOUND`、截断。
- Large（`PlaceholderListTests`，只读，夹具 `EPLAN_Sample_Project`）：
  - 整个项目：`total` 为 2，页为 `=TM1+A1&EFS1/1` 和 `=TM2+A1&EFS1/1`，`name` 都是空串，`valueSets` 都是 `["V", "mA"]`，`variables` 都是 `["T1", "T2"]`，`assignedObjects` 都是 2（S29）；
  - `page: "=TM1+A1&EFS1/1"`：只有一个；不存在的页 `PAGE_NOT_FOUND`；
  - `detailed`：每个占位符 4 个 `values`（2 个值集 × 2 个变量），`text` 不全为 null；`currentValues` 有 2 项；全部值、当前值和回退语言写入测试输出，供与 EPLAN 的占位符对话框对照（确认 `GetValue` 与界面一致）；
  - 读取前后夹具 `.edb` 中的文件不变（`User.eod`、`User.eox` 除外，A11）。
- Agent 评测："`=TM1+A1&EFS1/1` 页上的占位符有哪些值集？"，作答后核对 `V`、`mA`。

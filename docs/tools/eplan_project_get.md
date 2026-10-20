# 工具规格：`eplan_project_get`

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_project_get` |
| 所属分组 / Profile | `project` / `core` |
| 安全级别 | Read |
| MCP 注解 | `readOnlyHint` = true　`destructiveHint` = false　`idempotentHint` = true　`openWorldHint` = false |
| 状态 | Proposed（M2） |

## 1. 这个工具解决什么任务
代理接手一个项目时，先要知道它的规模和构成："这个项目有多少页""有哪些类型的页""有多少设备"。一次调用给出概况，代理再决定用哪个列表工具、带什么过滤条件，而不是盲目地把全部页面或设备列出来。

## 2. 输入参数
| 参数 | 类型 | 必填 | 默认值 | 说明（写给模型看的） |
|---|---|---|---|---|
| `project` | string | 否 | EPLAN 中选中的项目 | 项目名，或 `eplan_project_list` 返回的 `path` |
| `response_format` | enum `concise` / `detailed` | 否 | `concise` | `detailed` 额外返回全部非空的项目属性 |
| `language` | string | 否 | EPLAN 界面语言，没有文本时回退到其他语言 | 多语言文本的语言，如 `zh_CN`、`en_US`（M2 设计 Q3）。回退时属性的值旁边有 `language`（[语言回退设计](../design/2026-10-19-text-language-fallback.md)） |

## 3. 输出
```json
{
  "project": { "name": "Line1", "path": "D:\\Projects\\Line1", "readOnly": false },
  "counts": { "pages": 321, "devices": 420, "connections": 2060, "partReferences": 2900 },
  "pagesByType": [ { "type": "Circuit", "count": 140 }, { "type": "TerminalDiagram", "count": 25 } ],
  "properties": null,
  "truncated": false,
  "hint": null
}
```
- `devices`：带标识的主功能数量（`IsMainFunction` 且 `Name` 非空），与 `eplan_device_list` 默认列出的范围一致。
- `pagesByType`：按数量降序（数量相同时按类型名），最多 15 种。`type` 用 EPLAN 页类型的枚举名（英文，稳定），与 `eplan_page_list` 的 `page_type` 参数一致。
- `properties`（仅 `detailed`）：`[{ "id": 10000, "index": null, "name": "…", "value": "…" }]`，只含非空属性，不含 EPLAN 标为内部（`IsInternal`）的属性；带索引的属性（如附加字段）每个已用索引一条，`index` 是 EPLAN 的索引号，不带索引的属性 `index` 为 `null`；用户自定义属性没有编号，`id` 为 `null`，`name` 是它的标识名。单个属性按列出的方式读不出来时（抛出 `IndexedPropertyException` 等）只跳过这一个。文本经过 ResultShaper 的不可信文本处理，整体受 20k token 上限约束。
- `truncated` / `hint`：`detailed` 的结果超过 20k token 时，从末尾去掉属性，`truncated` 为 true，`hint` 说明去掉了什么（[工具设计规范 7.2](../standards/mcp-tool-design.md#72-大小控制)）。
- 一行摘要只给数字，不回显项目名和属性等项目数据（架构 8.5）：`Project has 321 pages, 420 devices, 2060 connections.`

## 4. 错误
| 错误码 | 什么时候发生 | 返回给模型的提示 |
|---|---|---|
| `INVALID_ARGUMENT` | `response_format` 或 `language` 不合法 | `metadata.violations` 列出问题 |
| `PROJECT_NOT_OPEN` | 没有打开项目，或指定的项目没有打开 | 提示调用 `eplan_project_list` |
| `AMBIGUOUS_NAME` | 项目重名，或没有选中项目而打开了好几个 | `metadata.candidates` 列出各项目的路径 |
| `EPLAN_NOT_CONNECTED` / `SESSION_NOT_SELECTED` / `BRIDGE_INCOMPATIBLE` / `EPLAN_BUSY` / `LICENCE_MISSING` / `TIMEOUT` / `INTERNAL` | 同 `eplan_project_list` | 同 `eplan_project_list` |

## 5. 实现要点
- 项目解析：`ProjectResolver.Resolve(ListOpenProjects(), project)`，再在适配层按路径取 `Project`（不依赖当前项目，api-notes A11）。
- 计数：一次 `DMObjectsFinder` 调用分别取页、功能、连接、部件引用（S6：合计约 200 ms 冷启动）；页类型按 `Page.PageType` 分组。
- 多语言：`ToMultiLangString().GetStringToDisplay(语言)`（编码规范 9.5 第 13a 条）。
- 属性名：`PropertyDefinition` 的名称是否可读、是否按界面语言返回，S6 没有测，在 Large 测试中确认；名称为空时 `name` 用 `AnyPropertyId.AsString`（内置属性是编号，用户自定义属性是标识名）。
- 属性：`ExistingIds` / `ExistingValues` 逐个读取；带索引的属性先按 `PropertyDefinition.IsIndexed` 判断，再按 `PropertyValue.Indexes` 逐个读取（S6：不带索引读取会抛 `IndexedPropertyException`，连 `IsEmpty` 也会）。`IsInternal`、`IsIndexed`、`Indexes` 以及非文本类型（Bool、Point、Time 等）经 `ToMultiLangString` 的显示结果都没有实机验证，在 Large 测试中确认。
- 只读，`LockingStep` 由调度器创建；不使用 BannedSymbols 中的任何成员。

## 6. 写操作专项
不适用。

## 7. 测试
- Small：参数校验、摘要只含数字、`pagesByType` 的排序和截断。
- Medium：Host + 假 Bridge（真实管道），覆盖项目解析的各种错误。
- Large：示例项目副本上，页数、连接数、部件引用数与 S6 的结果一致（321、2060、2900）；设备数在首次运行时记录到 api-notes。
- Agent 评测："这个项目有多少页？其中原理图有几页？"

# 工具规格：`eplan_structure_list`

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_structure_list` |
| 所属分组 / Profile | `structure` / `full` |
| 安全级别 | Read |
| MCP 注解 | `readOnlyHint` = true　`destructiveHint` = false　`idempotentHint` = true　`openWorldHint` = false |
| 状态 | Implemented (v0.5.1) |

## 1. 这个工具解决什么任务
一次读出项目的全部结构标识符（`=`、`==`、`++`、`+`、`&`、`#`）：描述、是否被使用。结构总览报表和页名都依赖这些数据，不需要打开 EPLAN 的"结构标识符管理"对话框。

## 2. 输入参数
| 参数 | 类型 | 必填 | 默认值 | 说明（写给模型看的） |
|---|---|---|---|---|
| `project` | string | 否 | EPLAN 中选中的项目 | 项目名，或 `eplan_project_list` 返回的 `path` |
| `block` | string | 否 | — | 只看一个块：`=`、`==`、`++`、`+`、`&`、`#` |
| `unused_only` | boolean | 否 | false | 只看未被使用的标识 |
| `limit` | integer，1–500 | 否 | 100 | 每页最多条数 |
| `cursor` | string | 否 | — | 上一次结果中的 `nextCursor` |

## 3. 输出
```json
{
  "project": "Line1",
  "identifiers": [ { "name": "+S1", "description": "zh_CN@箱柜 1;en_US@Cabinet 1;", "used": true } ],
  "total": 15,
  "nextCursor": null,
  "truncated": false,
  "hint": null
}
```
- `name`：带前缀的完整名称（`LOCATION_FULLNAME_WITHPREFIX`）。每个块自带的空标识不列出。
- `description`：`LOCATION_DESCRIPTION`，序列化的多语言文本。
- `used`：`Location.IsUsed`。
- 顺序：EPLAN 的顺序（`Project.GetLocationObjects`）。

## 4. 错误
| reason | 情形 |
|---|---|
| PROJECT_NOT_FOUND | 项目没有打开 |
| INVALID_ARGUMENT | `limit` 或 `cursor` 不对 |

## 5. 实现要点
只读，在主线程一次取完再分页。对象不跨调用保存（A106）。

## 6. 测试
- Small：`StructureOperationTests` 按块和使用情况过滤，`=` 与 `==`、`+` 与 `++` 不混淆。
- Large：`StructureToolsTests`。

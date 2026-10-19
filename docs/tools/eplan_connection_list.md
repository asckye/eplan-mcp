# 工具规格：`eplan_connection_list`

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_connection_list` |
| 所属分组 / Profile | `connection` / `core` |
| 安全级别 | Read |
| MCP 注解 | `readOnlyHint` = true　`destructiveHint` = false　`idempotentHint` = true　`openWorldHint` = false |
| 状态 | Proposed（M2） |

## 1. 这个工具解决什么任务
"-FC1 接到了哪里？""这一页上有哪些连接、用的什么截面？"代理按设备或按页列出连接，回答接线类的问题。

## 2. 输入参数
| 参数 | 类型 | 必填 | 默认值 | 说明（写给模型看的） |
|---|---|---|---|---|
| `project` | string | 否 | EPLAN 中选中的项目 | 项目名，或 `eplan_project_list` 返回的 `path` |
| `device` | string | 二选一 | — | 设备标识，规则同 `eplan_device_get` 的 `tag` |
| `page` | string | 二选一 | — | 完整页名 |
| `limit` | integer，1–200 | 否 | 50 | 每页最多条数 |
| `cursor` | string | 否 | — | 上一次结果中的 `nextCursor` |

`device` 和 `page` 至少给一个；两个都给时取交集。不提供"列出整个项目的连接"：示例项目有 2060 个连接，这类需求属于 M4 的导出工具。

## 3. 输出
```json
{
  "project": "Line1",
  "connections": [
    { "from": { "device": "=GAA+A1-FC1", "pin": "2" }, "to": { "device": "=GAA+A1-XD1", "pin": "1" },
      "page": "=GAA+A1&EFS1/1", "crossSection": "1,5", "colour": "GY", "kind": "2d" }
  ],
  "total": 40,
  "nextCursor": null,
  "truncated": false
}
```
- `crossSection`、`colour`：EPLAN 给出的文本，**不按区域解析**（S6：截面是 `1,5` 这样的区域格式）。`colour` 来自 `CONNECTION_WIRENUMBER`：示例项目中它存的是颜色代码（S6）；在其他项目中的含义在 Large 测试中确认，不确定时字段改名为 `wireNumber`。
- `kind`：`2d` 或 `3d`。3D 连接（`Connection3D`）没有可读的端点和页（S6），返回 `kind = "3d"`，`from`、`to`、`page` 为 `null`。
- 一行摘要：`Listed 40 connections.`，不回显标识。

## 4. 错误
| 错误码 | 什么时候发生 | 返回给模型的提示 |
|---|---|---|
| `INVALID_ARGUMENT` | `device` 和 `page` 都没给，或其他参数不合法 | `metadata.violations` 列出全部问题 |
| `DEVICE_NOT_FOUND` / `AMBIGUOUS_NAME` | 设备查找失败 | 同 `eplan_device_get` |
| `PAGE_NOT_FOUND` | 页不存在 | 提示用 `eplan_page_list` |
| `PROJECT_NOT_OPEN` | 项目解析失败 | 同 `eplan_project_get` |
| 连接与授权类错误 | 同 `eplan_project_list` | 同 `eplan_project_list` |

## 5. 实现要点
- 按设备：`ConnectionsFilter { Function = 主功能 }`，再加上各辅助功能的连接（触点、端子的连接分布在辅助功能上；Large 测试确认 `ConnectionsFilter.Function` 是否已包含它们）。
- 按页：取全部连接后在内存中按 `Connection.Page` 过滤（S6：已过时的 `ConnectionsFilter.Page` 会漏掉连接，35 个对 40 个）。
- 端点：`StartPin` / `EndPin` 的 `ParentFunction.Name` 和 `Name`；对 `Connection3D` 不读取端点（会抛 `NotSupportedException`）。

## 6. 写操作专项
不适用。

## 7. 测试
- Small：参数校验（`device`、`page` 都缺）、`kind` 与空端点、摘要。
- Medium：假 Bridge 上按设备、按页、两者交集、分页。
- Large：示例项目副本上，`page = "=GAA+A1&EFS1/1"` 返回 40 个连接（S6）。
- Agent 评测："-FC1 的 2 号端子接到哪里？"

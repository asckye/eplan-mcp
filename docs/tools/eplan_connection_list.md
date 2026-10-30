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
| `response_format` | `concise` / `detailed` | 否 | `concise` | `detailed` 增加连接定位器 |

`device` 和 `page` 至少给一个；两个都给时取交集。不提供"列出整个项目的连接"：示例项目有 2060 个连接，这类需求属于 M4 的导出工具。

- 字符串参数的默认值是空串而不是 `null`（与 `eplan_device_list` 相同，节省描述预算）；空白等同于没给。
- `page` 按完整页名精确匹配（区分大小写，与 `eplan_device_list` 的 `page` 相同），不做 `eplan_page_get` 那样的大小写回退。

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
  "truncated": false,
  "hint": null
}
```
- `from`、`to`：两端的 `device`（连接点所属功能的 `Name`，辅助功能可能带连接点后缀）和 `pin`（连接点名称）。名称为空时该字段为 `null`；一端未连接（`StartPin` / `EndPin` 为 `null`）或两个名称都为空时，整个端点为 `null`。
- `page`：连接所在页的完整页名；未放置时为 `null`。
- `crossSection`、`colour`：EPLAN 给出的文本，**不按区域解析**（S6：截面是 `1,5` 这样的区域格式）。`colour` 来自 `CONNECTION_WIRENUMBER`：示例项目中它存的是颜色代码（S6）；在其他项目中的含义在 Large 测试中确认，不确定时字段改名为 `wireNumber`。
- `kind`：`2d` 或 `3d`。3D 连接（`Connection3D`）没有可读的端点和页（S6），返回 `kind = "3d"`，`from`、`to`、`page` 为 `null`；`crossSection`、`colour` 照常读取（S6 的样本中 3D 连接的这两个属性可读）。
- `locator`：仅 `detailed` 返回已放置二维连接的 `{project,page,type,oid,digest}`；无页或 3D 时省略。`project` 是解析后的项目路径，`digest` 与电缆规划 inventory 使用同一读取与 SHA-256 算法，覆盖端点几何、引脚、归属、芯号、颜色及截面；工程变化后应重新查询。可将定位器用于 `eplan_connection_create` 的 `cable_definition.cores[].connection.locator`。默认 `concise` 输出保持不变。
- 顺序：按页列出时是 `GetConnections(null)` 的顺序；按设备列出时先是主功能的连接，再按 `SubFunctions` 的顺序是各辅助功能的连接，同一个连接只出现一次。超出 token 预算时由 ResultShaper 截断。
- 一行摘要：`Listed 40 connections.` / `Listed 50 of 120 connections (more: cursor).`，不回显标识。

## 4. 错误
| 错误码 | 什么时候发生 | 返回给模型的提示 |
|---|---|---|
| `INVALID_ARGUMENT` | `device` 和 `page` 都没给，或其他参数不合法 | `metadata.violations` 列出全部问题 |
| `DEVICE_NOT_FOUND` / `AMBIGUOUS_NAME` | 设备查找失败 | 同 `eplan_device_get`；`DEVICE_NOT_FOUND` 的 `metadata.device` 为传入的值，`AMBIGUOUS_NAME` 提示把候选中的完整标识作为 `device` 传入 |
| `PAGE_NOT_FOUND` | 页不存在 | 提示用 `eplan_page_list`；`metadata.page` 为传入的值，`metadata.project` 为项目名 |
| `PROJECT_NOT_OPEN` | 项目解析失败 | 同 `eplan_project_get` |
| 连接与授权类错误 | 同 `eplan_project_list` | 同 `eplan_project_list` |

## 5. 实现要点
- 查设备（Bridge.Core，`ConnectionListOperation`）：规则同 `eplan_device_get`。先在全部主功能的名称中按完整标识精确比较（序数），找不到时用 `DeviceTags.FindByShownTag` 按去掉 `>` 的 `VisibleName` 不区分大小写比较；适配层只拿到确定存在的完整标识。
- 按设备（适配层）：主功能和每个辅助功能各用一次 `ConnectionsFilter { Function = 该功能 }`，合并后去重。XML 文档说明 `ConnectionsFilter.Function` 匹配"起点或终点是这个功能"的连接，触点、端子的连接分布在辅助功能上，所以逐个功能查询；即使主功能的查询已包含辅助功能的连接，去重后结果也一样。去重依赖 `StorableObject` 重写的 `Equals` / `GetHashCode`（反射确认；S6 用 `Distinct()` 汇总一页功能的连接得到 40 个，与按页过滤一致）。
- 按页：取全部连接（设备和页都给时，取该设备的连接）后在内存中按 `Connection.Page` 过滤，与目标页用 `Equals` 比较，做法与 S6 相同（S6：已过时的 `ConnectionsFilter.Page` 会漏掉连接，35 个对 40 个）。目标页用 `PagesFilter { Name, ExactNameMatching = true }` 加序数比较查找，与 `eplan_device_list` 共用 `EplanDeviceReader.FindPage`。
- 端点：`StartPin` / `EndPin` 的 `ParentFunction.Name` 和 `Name`；对 `Connection3D` 不读取端点和页（会抛 `NotSupportedException`，S6）。
- `crossSection`、`colour` 用 `GetStringToDisplay`（EPLAN 界面语言）读取：S6 中它们是与语言无关的值（`??_??@1,5;`）。
- 待 Large 测试确认（S6 没有直接验证）：
  - 按设备查询的结果：`ConnectionsFilter.Function` 是否也返回 `Connection3D`；主功能的查询是否已包含辅助功能的连接；辅助功能很多的设备（如 PLC 卡）逐个查询的耗时。
  - 示例项目中所有 2D 连接读取 `Connection.Page` 都不抛异常（XML 文档写明失败时抛 `BaseException`；S6 的按页过滤包了异常处理，没有记录是否发生）。
  - 连接点所属功能的 `Name` 的实际形式（主功能、辅助功能、端子）。
  - `CONNECTION_WIRENUMBER` 在其他项目中的含义（见第 3 节）。

## 6. 写操作专项
不适用。

## 7. 测试
- Small：参数校验（`device`、`page` 都缺）、`kind` 与空端点、摘要。
- Medium：假 Bridge 上按设备、按页、两者交集、分页。
- Medium：详细列表定位器可直接用于同连接的电缆 dry-run；Large：新 LargeWrite 页生成导线后，经详细列表定位器规划电缆，不执行电缆写入（本次未运行）。
- Large：示例项目副本上，`page = "=GAA+A1&EFS1/1"` 返回 40 个连接（S6）。
- Agent 评测："-FC1 的 2 号端子接到哪里？"

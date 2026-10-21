# 工具规格：`eplan_plc_get`

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_plc_get` |
| 所属分组 / Profile | `plc` / `full`（M4c 的新工具默认只在 `full` 中，工具设计规范第 10 节） |
| 安全级别 | Read |
| MCP 注解 | `readOnlyHint` = true　`destructiveHint` = false　`idempotentHint` = true　`openWorldHint` = false |
| 状态 | Proposed（M4c 第一批；已实现，待实机验证） |

## 1. 这个工具解决什么任务
"-KE1 这块 PLC 卡有哪些通道？每个通道的地址、符号地址是什么、接到哪个设备？哪些通道还空着？"代理看一个 PLC 盒的通道与地址，用来核对 I/O 分配。

列出 PLC 盒不需要新工具：`eplan_device_list` 的 `category` 取 `PLCBox`（示例项目 20 个，S21）。所以 M4c 设计 4.1 中的 `eplan_plc_list` 改为本工具。

## 2. 输入参数
| 参数 | 类型 | 必填 | 默认值 | 说明（写给模型看的） |
|---|---|---|---|---|
| `project` | string | 否 | EPLAN 中选中的项目 | 项目名，或 `eplan_project_list` 返回的 `path` |
| `plc` | string | 是 | — | PLC 盒的设备标识，规则同 `eplan_device_get` 的 `tag`，如 `=K1+A2-KE1` |
| `language` | string | 否 | EPLAN 界面语言，没有文本时回退到其他语言 | 多语言文本的语言 |
| `limit` | integer，1–200 | 否 | 100 | 每页最多列出的通道数 |
| `cursor` | string | 否 | — | 上一次结果中的 `nextCursor` |

## 3. 输出
```json
{
  "project": "Line1",
  "plc": { "tag": "=K1+A2-KE1", "visibleTag": "-KE1", "page": "=K1+A2&EFA1/1", "parts": ["PXC.2703994"],
           "station": null, "rack": null, "slot": null, "cpu": false },
  "channels": [
    { "name": "=K1+A2-KE1:2.3", "address": "I0.3", "symbolicAddress": null, "dataType": "BOOL", "text": "…",
      "connected": [ { "device": "=K1+A2-SF1", "pin": "13" } ] }
  ],
  "total": 34,
  "nextCursor": null,
  "truncated": false,
  "hint": null
}
```
- 示例中的地址、数据类型和连接只是示意，示例项目中的实际值由 Large 测试确认。
- `plc`：PLC 盒。`station`、`rack`、`slot` 是 `FUNC_PLCSTATIONNAME`、`FUNC_PLCRACK`、`FUNC_PLCSOCKET` 的显示文本；`cpu` 是 `PLC.IsCPU`（S21：示例项目中都是 false）。
- `channels`：`PLC.PLCTerminals`（S21：每个 PLC 盒 34 个）。`address`、`symbolicAddress`、`dataType` 是 `FUNC_PLCADDRESS`、`FUNC_PLCSYMBOLICADDRESS_CALCULATED`、`FUNC_PLCDATATYPE` 的显示文本，`text` 是功能文本；没有值时为 `null`。省略 `language` 而用了回退语言时，`text` 旁边有 `textLanguage`，同 `eplan_device_get`（[语言回退设计](../design/2026-10-19-text-language-fallback.md)）。
- `connected`：通道连接点上的连接另一端，字段同 `eplan_connection_list` 的端点；没有连接时为空数组，用来找空闲通道。
- 一行摘要：`PLC found: 34 channels, 20 connected.`，不回显标识和文本。

## 4. 错误
| 错误码 | 什么时候发生 | 返回给模型的提示 |
|---|---|---|
| `INVALID_ARGUMENT` | 缺少 `plc`，或其他参数不合法；标识找到了，但不是 PLC 盒 | `metadata.violations` 列出全部问题；不是 PLC 盒时说明它的类别 |
| `DEVICE_NOT_FOUND` | 没有这个标识 | 提示用 `eplan_device_list`（`category: "PLCBox"`）查 |
| `AMBIGUOUS_NAME` | 短标识对应多个 PLC 盒 | 同 `eplan_device_get` |
| `PROJECT_NOT_OPEN` | 项目解析失败 | 同 `eplan_project_get` |
| 连接与授权类错误 | 同 `eplan_project_list` | 同 `eplan_project_list` |

## 5. 实现要点
- 查找同 `eplan_device_get`，然后要求对象是 `EObjects.PLC`（类别 `PLCBox`）。
- `PlcIOEntry` 在 S21 中都是 `null`，不用它；地址类信息从上面列出的属性读取。
- 通道的连接用 `ConnectionsFilter.Function` 按 PLC 端子本身查（与 `eplan_connection_list` 按设备查询的做法相同），取每条连接中不属于该端子的一端；每个通道最多 10 个。
- 待 Large 测试确认（S21 只读了名称和个数）：
  - 示例项目中这些 PLC 属性是否有值、显示文本的形式；`FUNC_PLCSYMBOLICADDRESS_CALCULATED` 与 `_MANUAL`、`_AUTOMATIC` 中哪一个是界面上显示的符号地址；
  - 通道的连接从 `PLCTerminal` 的哪个对象读取（端子本身还是它的连接点）。

## 6. 写操作专项
不适用。PLC 编址（`PlcService.ReaddressTerminals`）在第二批之后单独设计。

## 7. 测试
- Small：参数校验、摘要、分页。
- Medium：假 Bridge 上的命中、不是 PLC 盒、找不到。
- Large：示例项目副本上 `=K1+A2-KE1` 有 34 个通道（S21）。
- Agent 评测："-KE1 还有哪些空闲的输入通道？"

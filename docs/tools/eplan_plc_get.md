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
    { "name": "=K1+A2-KE1:2.3", "page": "=K1+A2&EFS1/3", "main": true, "address": "%IX1.2", "symbolicAddress": "%IX1.2",
      "dataType": "BOOL", "text": "…", "connected": [ { "device": "+A2-XD4:3", "pin": "1" } ] }
  ],
  "total": 34,
  "nextCursor": null,
  "truncated": false,
  "hint": null
}
```
- 示例中的页、地址和连接只是示意；值的形式见第 5 节的实测结果。
- `plc`：PLC 盒。`station`、`rack`、`slot` 是 `FUNC_PLCSTATIONNAME`、`FUNC_PLCRACK`、`FUNC_PLCSOCKET` 的显示文本；`cpu` 是 `PLC.IsCPU`（S21：示例项目中都是 false）。
- `channels`：`PLC.PLCTerminals` 中的每个对象，按 EPLAN 的顺序，不按名称去重（S21：每个 PLC 盒 34 个）。**一个连接点名称可以对应多个通道对象**：输入和输出（`=K1+A2-KE1:1.1` 既是输出 `%QX2.0`，又是输入 `%IX2.0`），或者主表达和辅助表达（一个有地址、一个没有）；用 `address`、`page` 和 `main`（`IsMainFunction`）区分。`page` 是通道所在的页，没有放置时为 `null`。`address`、`symbolicAddress`、`dataType` 是 `FUNC_PLCADDRESS`、`FUNC_PLCSYMBOLICADDRESS_CALCULATED`、`FUNC_PLCDATATYPE` 的显示文本，`text` 是功能文本；没有值时为 `null`。省略 `language` 而用了回退语言时，`text` 旁边有 `textLanguage`，同 `eplan_device_get`（[语言回退设计](../design/2026-10-19-text-language-fallback.md)）。
- `connected`：连接到这个通道对象的连接的另一端，字段同 `eplan_connection_list` 的端点，最多 10 个；没有连接时为空数组，用来找空闲通道。同名的另一个通道对象可能有连接，判断空闲时按名称一起看。
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
- 通道的连接：与 `eplan_connection_list` 按设备查询相同，用 `ConnectionsFilter.Function` 查 PLC 盒的主功能、全部辅助功能和每个通道对象的连接，去重；取每条连接在 PLC 盒上的一端和另一端。这一端交给它所属的通道对象（连接点的 `ParentFunction` 就是该对象，或者连接点在该对象的 `Pins` 中）；对不上对象时，交给同名的通道对象，其中有在连接所在页上的，只交给这些。PLC 盒自身的连接点（如电源）不属于任何通道。每个通道最多 10 个。
- **已实测**（2026-10-21，Large 测试，示例项目夹具的 `=K1+A2-KE1`）：
  - `PLC.PLCTerminals` 有 34 个对象、17 个名称（见第 3 节）；
  - 只按 `PLC.PLCTerminals` 中的对象用 `ConnectionsFilter.Function` 查，34 个通道都查不到连接；`eplan_connection_list` 按设备查到 22 条（如 `+A2-XD4:3` → `=K1+A2-KE1:2.3`，连接点 `2.3`；`=K1+A2-FC2:2` → `=K1+A2-KE1:1.2`），所以改为上面的读法；
  - 有地址时 `symbolicAddress` 与 `address` 相同（`FUNC_PLCSYMBOLICADDRESS_CALCULATED` 读出 `%QX2.0`）；`dataType` 为 `BOOL`；
  - `station` 读出 `I/O Buskoppler PHX 1 `（原样，带末尾空格），`rack`、`slot` 为 `null`，`cpu` 为 false。
- 待 Large 测试确认：新的读法下通道 `2.3` 的连接到 `+A2-XD4:3`，以及每条连接交给了哪个同名的通道对象。

## 6. 写操作专项
不适用。PLC 编址（`PlcService.ReaddressTerminals`）在第二批之后单独设计。

## 7. 测试
- Small：参数校验、摘要、分页。
- Medium：假 Bridge 上的命中、不是 PLC 盒、找不到。
- Large：示例项目副本上 `=K1+A2-KE1` 有 34 个通道（S21）。
- Agent 评测："-KE1 还有哪些空闲的输入通道？"

# 工具规格：`eplan_cable_get`

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_cable_get` |
| 所属分组 / Profile | `cable` / `full`（M4c 的新工具默认只在 `full` 中，工具设计规范第 10 节） |
| 安全级别 | Read |
| MCP 注解 | `readOnlyHint` = true　`destructiveHint` = false　`idempotentHint` = true　`openWorldHint` = false |
| 状态 | Proposed（M4c 第一批；已实现，待实机验证） |

## 1. 这个工具解决什么任务
"-WG2 是什么电缆、多长、从哪里接到哪里、每根芯线接在哪个端子上？"代理拿到一个电缆标识后，看电缆的全貌：型号和规格、两端、芯线及其连接。

列出电缆不需要新工具：`eplan_device_list` 的 `category` 取 `Cable` 就能按结构、页、是否有部件过滤（示例项目 78 根，S21）。所以 M4c 设计 4.1 中的 `eplan_cable_list` 不做。

## 2. 输入参数
| 参数 | 类型 | 必填 | 默认值 | 说明（写给模型看的） |
|---|---|---|---|---|
| `project` | string | 否 | EPLAN 中选中的项目 | 项目名，或 `eplan_project_list` 返回的 `path` |
| `cable` | string | 是 | — | 电缆标识，规则同 `eplan_device_get` 的 `tag`：完整标识（如 `+B1-WG2`）总是唯一，短标识（如 `-WG2`）可能有歧义 |
| `language` | string | 否 | EPLAN 界面语言，没有文本时回退到其他语言 | 多语言文本的语言 |
| `limit` | integer，1–200 | 否 | 100 | 每页最多列出的芯线数 |
| `cursor` | string | 否 | — | 上一次结果中的 `nextCursor` |

## 3. 输出
```json
{
  "project": "Line1",
  "cable": {
    "tag": "+B1-WG2", "visibleTag": "-WG2", "page": "=F+A2&EFS1/2",
    "type": "ÖLFLEX CLASSIC 110", "wireCount": "3", "crossSection": "1,5", "length": "5 m",
    "sources": ["=F+B1-SG1"], "targets": ["+A2-XG4:2"], "parts": ["LAPP.00101264"]
  },
  "cores": [
    { "colour": "BK", "designation": "433", "crossSection": "1,5",
      "from": { "device": "=F+B1-SG1", "pin": "1" }, "to": { "device": "+A2-XG4", "pin": "2" }, "page": "=F+A2&EFS1/2" }
  ],
  "total": 2,
  "nextCursor": null,
  "truncated": false,
  "hint": null
}
```
- `cable`：电缆功能。`type`、`wireCount`、`crossSection`、`length` 是 `FUNC_CABLETYPE`、`FUNC_CABLEWIRECOUNT`、`FUNC_CABLEWIRECROSSSECTION`、`FUNC_CABLELENGTH_FULL` 的显示文本，**不解析数值**（截面是 `1,5` 这样的区域格式，同 `eplan_connection_list`）；没有值时为 `null`。
- `sources`、`targets`：`Cable.GetSourcesAndTargets` 给出的两端对象的名称，原样返回（端子带连接点后缀，如 `+A2-XG4:2`），去重，各最多 20 个；S21 中示例电缆两端各一个。
- `parts`：电缆的部件号，与 `eplan_device_get` 的 `parts` 来源相同，这里只给部件号。
- `cores`：`Cable.CableConnections` 中的芯线连接，字段同 `eplan_connection_list`，另加 `designation`（线号，`CONNECTION_DESIGNATION`，没有时为 `null`）。顺序同 `CableConnections`。
- 所有项目文本经过 ResultShaper；一行摘要：`Cable found: 2 cores.`，不回显标识和文本。

## 4. 错误
| 错误码 | 什么时候发生 | 返回给模型的提示 |
|---|---|---|
| `INVALID_ARGUMENT` | 缺少 `cable`，或其他参数不合法；标识找到了，但不是电缆 | `metadata.violations` 列出全部问题；不是电缆时说明它的类别，并提示改用 `eplan_device_get` |
| `DEVICE_NOT_FOUND` | 没有这个标识 | 提示用 `eplan_device_list`（`category: "Cable"`）查 |
| `AMBIGUOUS_NAME` | 短标识对应多根电缆 | 同 `eplan_device_get` |
| `PROJECT_NOT_OPEN` | 项目解析失败 | 同 `eplan_project_get` |
| 连接与授权类错误 | 同 `eplan_project_list` | 同 `eplan_project_list` |

## 5. 实现要点
- 查找与 `eplan_device_get` 相同（完整标识精确匹配，再按显示标识），然后要求 `FunctionCategory` 为 `Cable` 并且对象是 `EObjects.Cable`。屏蔽（类别 Shielding）不当作电缆。
- 只按类别取电缆，不用 `GetCablesWithFilterScheme("")`：空串用导航器当前的过滤方案，结果随界面状态变化（A24）。
- 芯线的端点读法同 `eplan_connection_list`（`StartPin`、`EndPin` 的 `ParentFunction.Name` 和 `Name`），所以芯线也带 `kind`（3D 连接没有端点和页）。
- `sources`、`targets` 只列出其中的功能（S21 得到的都是功能），其他类型的对象不列出。
- 待 Large 测试确认（S21 没有验证）：
  - 四个电缆属性在示例项目中是否有值、显示文本的形式；
  - `GetSourcesAndTargets` 对一根电缆的耗时；
  - `CableConnections` 是否包含屏蔽的连接，`CONNECTION_WIRENUMBER` 对芯线是否是颜色。

## 6. 写操作专项
不适用。

## 7. 测试
- Small：参数校验、摘要、分页。
- Medium：假 Bridge 上的命中、短标识歧义、不是电缆、找不到。
- Large：示例项目副本上，`+B1-WG2` 有 2 根芯线，源为 `=F+B1-SG1`，目标为端子 `+A2-XG4:2`（S21）。
- Agent 评测："-WG2 是什么型号、几芯？每根芯线接在哪里？"

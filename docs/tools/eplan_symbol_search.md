# 工具规格：`eplan_symbol_search`

T7d 兼容输出增加 `functionCategory`（原生功能类别）与 `plcInputDefinition`（匹配图形引脚数的数字输入定义 group/id，未发现时为 null）；PLC 连接点放置使用该发现结果，不能仅按图形外观推断数字输入。

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_symbol_search` |
| 所属分组 / Profile | `symbol` / `full` |
| 安全级别 | Read |
| MCP 注解 | `readOnlyHint=true`、`destructiveHint=false`、`idempotentHint=true`、`openWorldHint=false` |
| 状态 | Implemented；新增 Large 测试待运行 |

## 1. 这个工具解决什么任务
查询指定项目已可用的库符号、变体和连接点，为绘图选择符号；现有部件查询不能发现无部件符号。

## 2. 输入参数
| 参数 | 类型 | 必填 | 默认值 | 说明 |
|---|---|---|---|---|
| `project` | string | 是 | — | 已打开项目的名称或路径 |
| `library` | string | 否 | 全部 | 库名完整匹配，不区分大小写 |
| `name_contains` | string | 否 | 全部 | 符号名包含文字，不区分大小写 |
| `kind` | 封闭 string enum | 否 | 全部 | `function/routing/t_node/potential_point/potential_definition/interruption_point/location_box/cable_definition/plc_box/terminal/other` |
| `limit` | integer，1–200 | 否 | 50 | 每页变体数 |
| `cursor` | string | 否 | — | 上次结果的 `nextCursor`；绑定项目和全部过滤条件 |

## 3. 输出
结构化结果及同内容 JSON 文本：`project, symbols, total, nextCursor, truncated, hint`。每个变体一行：`library, name, variant, type, kind, connectionPoints`；连接点含符号局部毫米坐标 `location=[x,y]` 与 EPLAN 原始 `direction`。按库名、符号名的序数顺序及变体号排序；`total` 是匹配变体总数。摘要只含数量。结果清洗并按既有双份预算分页；没有匹配返回空页。没有 `response_format`，始终返回连接点。

## 4. 错误
`INVALID_ARGUMENT`：缺少项目、非法 kind/limit、未知参数或不匹配/过期游标；一次报告全部参数问题。`PROJECT_NOT_OPEN/AMBIGUOUS_NAME` 提示先查项目列表；连接、授权、忙碌与超时错误沿用项目读取工具。非有限坐标返回 `EPLAN_OPERATION_FAILED`，不返回畸形几何。

## 5. 实现要点
依据 [api-notes](../eplan/api-notes.md) 的 A52、A53、A55 / S40、S41、S43：仅从 `Project.SymbolLibraries` 读 `SymbolLibrary.Symbols`、`Symbol.Type/Variants`、`SymbolVariant.ConnectionPoints`、`PinBase.Location/Direction`。在既有主线程 `LockingStep` 内、任何写事务之外发现；库容器不 Initialize/Close/Dispose，不导入库或修改设置。类型映射区分 PotentialTerminal 与 PotentialDefinition；Function/BoxFunction 结合 S43 的 20188 功能定义类别前缀细分 PLCBox、Terminal，其余类型归 other，原始 type 始终保留，不凭名称猜测电气语义。只对筛选后的变体读取连接点。

## 7. 测试
Small：参数聚合、游标绑定、稳定分页、过滤及类型映射、非有限坐标拒绝。Medium：真实 Host 协议与假 Bridge 的契约及分页。Large：示例夹具查库/符号/变体/连接点，比较读取前后项目文件；本任务只编写，不运行。Agent 验收：查询角连接、端子及 PLC 盒并解释实际连接点，后续绘图验收使用本结果。

# 工具规格：`eplan_property_schema`

| 项 | 内容 |
|---|---|
| 工具名 / title | `eplan_property_schema` / Describe EPLAN properties |
| 所属分组 / Profile | `property` / `full`；工具集选择尚未实现，沿用 ADR-0022 的 Profile 过渡规则；不加入 `core` |
| 安全级别 | Read |
| MCP 注解 | `readOnlyHint` = true，`destructiveHint` = false，`idempotentHint` = true，`openWorldHint` = false |
| 状态 | Implemented（P2：仅发布目录） |

## 1. 这个工具解决什么任务
代理按对象类型查属性 ID、正式名称及读写限制；现有对象读取工具不能提供完整目录。
依据[通用属性设计](../design/2026-10-25-property-tools.md) 4.1–4.4；目录分类不是实例适用性或实机验证证据。

## 2. 输入参数
| 参数 | 类型 | 必填 | 默认值 | 说明 |
|---|---|---|---|---|
| `object_type` | string enum | 是 | — | 发布字典的精确对象类型，不是运行时数字 type |
| `properties` | selector[]，1–200 | 否 | — | 每项恰选 `property_id` 正整数或 `property_name` 正式名称；可带 int32 `index` |
| `name_contains` | string，1–500 字符 | 否 | — | 正式名称的子串，OrdinalIgnoreCase；与 `properties` 互斥 |
| `response_format` | concise / detailed | 否 | concise | detailed 加目录事实及显式未知字段 |
| `limit` | integer，1–200 | 否 | 50 | 本页结果行上限；预算可能使实际行数更少 |
| `cursor` | string | 否 | — | 原样传回上一页的 `cursor`，其他参数保持一致 |

所有输入对象拒绝未知字段；ID/正式名称只在该类型内精确解析，重复属性拒绝。
两种筛选均省略时仅列 readable 定义；显式选择或名称筛选可查看排除分类，但绝不读取属性值。
`index` 只作 selector 形状校验，不展开实例索引、不推断合法范围；每个属性仍只返回一行。
P2 不接受 `target` 或 `project`，未知参数返回 `INVALID_ARGUMENT`：实例定义依赖 P3/P4 的 Bridge 适配器，当前无法诚实提供。

## 3. 输出
`outputSchema` 声明 `{catalog_version, object_type, items, total, cursor, truncated}`，字段名使用 snake_case。
每项包含 `property_id`（无公开 ID 时 null）、`property_name`、`read`（readable 或发布的拒绝分类）、`write`（`write_allowed`、`api_writable`、`reason`）。
`write_allowed` 只查询发布白名单的版本/对象/ID；当前名单为空，全部 false；`api_writable` 始终 null。
detailed 增加 `obsolete`、`definition`、`source`、`version`、`runtime_differences`；definition 使用既有元数据 DTO，所有未知类型、边界、索引、单位、语言、许可等字段显式为 null。
来源为 `catalog/eplan-2026.0.3/data-dictionary.jsonl`，版本为 2026.0.3；没有运行时比较，`runtime_differences` 为 null。
按 numeric ID 升序、null ID 最后、正式名称 Ordinal 排序；`total` 是筛选后的定义行数，不是实例值或全项目总数。
游标经签名，绑定当前 Host 会话、目录版本及查询（含 selector JSON、limit/format），10 分钟过期；目录不可变，无项目身份或实例快照承诺。
摘要仅含数量：`Listed N of M property definitions.`；未完时附 `Pass cursor for the rest or narrow the filters.`。
structuredContent 与 JSON 文本相同，两个副本加摘要一起预算：concise 约 5k token，detailed 硬上限 20k；整行分页，不截断属性定义，仍有行时 `truncated: true` 且 cursor 非 null。

## 4. 错误
| reason | 情形 | 下一步 |
|---|---|---|
| `INVALID_ARGUMENT` | 未知类型/ID/正式名、selector 形状、互斥条件、重复项、limit/format/index 错误、游标失效或篡改、未知参数 | `metadata.violations` 汇总问题；修正参数；游标问题去掉 cursor 重新查询 |

成功结果不带 execution；错误沿用共享 Read 信封的 execution:null。被排除的定义返回分类，不伪装成值读取成功，也不作为整次策略错误。

## 5. 实现要点
仅 Host 查询嵌入的 PropertyCatalog 与 PropertyWriteWhitelist；无 EPLAN API、Bridge invoke、磁盘旁路或项目访问。
不需要 SafetyPoint / LockingStep / QuietModeStep；预期低于 2 秒，分页循环检查取消。
目录没有类型、范围、索引、可写性；不从正式名称或 setter 猜测，未知不授权。

## 6. 写操作专项
不适用（Read 级）。

## 7. 测试
- Small：稳定排序、筛选、分页/签名/绑定/过期、拒绝分类、未知字段为 null、空白名单、预算和注解。
- Medium：真实 MCP Server 的发现/调用/未知参数/输出 schema，以及契约快照；不连接 EPLAN。
- Large：本 PR 不运行；实例元数据与读取证据属于 P3/P4。
- Agent 评测场景：查 Function 的 FUNC_TEXT，再查 PROPUSER_* 的拒绝原因，解释为什么未知 API 可写性不能授予写权限；不执行写入。

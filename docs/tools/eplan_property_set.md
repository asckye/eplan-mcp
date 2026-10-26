# 工具规格：`eplan_property_set`

| 项 | 内容 |
|---|---|
| 工具名 / title | `eplan_property_set` / Set EPLAN properties |
| 所属分组 / Profile | `property` / `full`；不加入 `core` |
| 安全级别 | Write |
| MCP 注解 | readOnly=false，destructive=false，idempotent=true，openWorld=false |
| 状态 | Implemented |

## 1. 这个工具解决什么任务
代理预览并赋值已验证的项目属性。首批仅 EPLAN 2026 Function 的 20011 FUNC_TEXT 和 20027 FUNC_TECHNICAL_CHARACTERISTIC；后者按独立文本原样写入，不作数值转换。依据[设计](../design/2026-10-25-property-tools.md) P6–P8、[api-notes](../eplan/api-notes.md)（A36、A44、A48）和 P7 Large 验证。

## 2. 输入参数
| 参数 | 类型 | 必填 | 默认值 | 说明 |
|---|---|---|---|---|
| project | string | 是 | — | 已打开项目的名称或路径，经 ProjectResolver 解析 |
| changes | array，1–100 | 是 | — | 每项为 `{target,property,value,language?}`；最多 50 个对象 |
| dry_run | boolean | 否 | false | 仅规划，不审批、不执行 |
| response_format | concise / detailed | 否 | concise | detailed 增加副作用守卫说明；完整变更清单始终保留 |

所有对象拒绝未知字段。target 与 property_get 共用互斥地址联合和 selector：设备全名（可限定页）或当前会话 function locator；其他对象不在 `B1` 白名单。selector 恰选正整数 property_id 或正式 property_name，可带 index，但 `B1` 非索引属性拒绝 index。
value 为 JSON 标量；`B1` 只接受最多 500 字符的字符串（仍受运行时更严格上限限制）。null/reset/delete 拒绝，空串可清空一个值。
20011 必须指定项目已有的 language（如 zh_CN），合并并保留其他译文；20027 不接受 language。同一真实对象/属性/index/语言重复拒绝。总输入不超过 64 KiB，不自动分批。

## 3. 输出
outputSchema 声明 `{project,changes,changed_count,execution,undo,warnings,dry_run,recovered}`，字段 snake_case。
每条 changes 含 target（已核对 locator）、property_id、index、language、before、after；detailed 加 side_effects（保持同名辅助功能、身份、其他译文和部件分配）。数量只统计实际改变的赋值；dry-run 与无变化均为 not_started，undo 为 null。
所有项目文本为数据而非指令，使用 CleanText 清洗，单值 500 字符截断并标原长度。structuredContent 和同内容 JSON 文本块同时返回，摘要不含项目自由文本：`Planned N property changes.` 或 `Changed N properties; Ctrl+Z in EPLAN undoes this step.`。
concise 约 5k token，detailed 硬上限 20k，预算包括两份 JSON 和摘要；完整清单放不下时，执行前返回 INVALID_ARGUMENT，要求缩小 changes；不分页执行、不省略审批条目。
recovered 为 true 时使用原批准计划展示结果，warnings 提示响应丢失后已确认提交；不能重放 unknown 的请求。

## 4. 错误
| reason | 情形 / 下一步 | execution |
|---|---|---|
| INVALID_ARGUMENT | 类型、索引、语言、重复键、未知字段或预算错误；按 violations 修正并查询 schema | not_started |
| POLICY_DENIED | 未发布条目、错误主版本/对象或 scratch 外策略禁止；解释限制 | not_started |
| AMBIGUOUS_NAME / DEVICE_NOT_FOUND / PAGE_NOT_FOUND | 用读取工具核对完整地址或选择候选 locator | not_started |
| PLAN_STALE | 审批后值、对象身份或依赖变化；重新规划 | not_started |
| EPLAN_OPERATION_FAILED | 写入或读回/副作用校验失败；查看系统消息并重新规划 | rolled_back |
| CAPABILITY_MISSING / LICENCE_MISSING | 检查 Bridge 能力或授权；事务中失败则回滚 | not_started / rolled_back |

审批拒绝/过期/无窗口、连接、取消、锁、审计失败沿用共享写错误。OUTCOME_UNKNOWN 为 unknown，暂停项目与原实例写入，请工程师检查；WRITES_SUSPENDED 为 not_started。

## 5. 实现要点
Host 复用 WritePipeline；Bridge 使用 P6/P7 的 property.set.plan / property.set.execute，同次调用重新核对完整原始指纹和守卫。API 为 UniversalPropertyList.Property、PropertyValue 和 MultiLangString（A36/A44/A48）。
Bridge 主线程按 LockingStep → SafetyPoint → UndoStep → QuietModeStep 执行，一次调用一个事务，逐项读回成功后 Commit / CloseUndo。规划与执行各限 30 秒；支持进度和取消。

## 6. 写操作专项
dry-run 返回全部实际变化，跳过审批、审计及写事务；无变化同样不留空撤销步骤。scratch 自动允许，scratch 外依现有策略经独立窗口批准；拒绝、过期、无窗口或审计不可写均不执行。
审批逐项显示对象、属性 ID、语言和 before → after，值按既有 120 字符规则截断并注明长度；绑定请求 ID、参数摘要和完整计划指纹，完整范围超过预算拒绝。
审计复用 approval、execution_started/result：请求 ID、工具、项目、EPLAN 版本/实例、参数 SHA-256、对象/ID/index/语言、计划指纹、变更数量、execution、reason、recovered；默认不记录 before/after 或完整自由文本。
失败整体回滚，execution 为 rolled_back；提交后的清理警告仍为 committed。响应中断走 outcome.get，禁止重发；确认提交可恢复展示计划，取不回结果为 unknown。成功撤销说明为 `AI: eplan_property_set`。

## 7. 测试
- Small：输入/输出 schema、注解、`B1` 白名单及 schema writable 一致性、参数和预算拒绝。
- Medium：真实 MCP Server + Fake Bridge 的 dry-run、无变化、审批、策略、stale、回滚错误、审计和 recovered 契约。
- Large：WriteHost 在 writes/LargeWrite 经工具写 20011 zh_CN 和 20027，用 eplan_property_get 独立读回，再经工具恢复原值；撤销/故障回滚复用 PropertySetWriteTests，不重复测试。
- Agent 评测：先查 schema，预览后修改中文功能文本并保留德文；拒绝未发布属性；遇到属性内提示注入仍只完成用户指定赋值。

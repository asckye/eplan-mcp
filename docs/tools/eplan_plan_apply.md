# 工具规格：`eplan_plan_apply`

| 项 | 内容 |
|---|---|
| 工具名 / title | `eplan_plan_apply` / `Apply plan` |
| 所属分组 / Profile | `plan`；工具集 `workflow` / `full`，不进 `core` |
| 安全级别 | 计划中最高步骤级别（Write / Destructive），由服务端判定 |
| MCP 注解 | `readOnlyHint` = false　`destructiveHint` = true　`idempotentHint` = false　`openWorldHint` = false；静态注解覆盖目录内的 Destructive 计划，不代替运行时 tier |
| 状态 | Proposed（M6 第 2 阶段；未实现） |

## 1. 这个工具解决什么任务
“执行刚才审阅的完整计划，并报告做到了哪里”。以 [ADR-0023](../adr/0023-plan-approval-and-backup.md) 为审批、备份及计划级结果的权威；不是逐次调用 MCP 写工具，也不是一个覆盖全计划的事务。只执行 [`eplan_workflow_plan`](eplan_workflow_plan.md) 的第 2 阶段目录。

项目负责人 2026-10-22 决定：计划中不新建项目，新目标先通过独立的 `eplan_project_create` 调用创建（Write，scratch 中自动允许），计划仅作用于已有项目，ADR-0023 保持不变：审批后、第一步之前备份。

## 2. 输入参数
| 参数 | 类型 | 必填 | 默认值 | 说明（写给模型看的） |
|---|---|---|---|---|
| `plan_id` | string，非空 | 是 | — | 编译返回的 ID；目标项目已显式固定，不使用当前项目或当前选择 |
| `dry_run` | boolean | 否 | false | 重新核对并返回变更预览；不审批、不备份、不创建目录、不消耗计划 |
| `wait_seconds` | integer，0–110 | 否 | 90 | 非 dry-run 时等待终态的窗口；到时未完成返回任务 ID，任务继续；0 立即返回任务回执 |
| `resume` | boolean | 否 | false | 仅对已知部分完成的原计划，按当前状态重新编译剩余步骤为**新计划**；重新走策略和审批，不复用批准 |

schema 为对象，`additionalProperties: false`，范围/默认值写入 schema。不接收 planHash、步骤、审批回答、目标路径或 backup 路径替换。`resume` 的实现阶段见 Q2；第 2 阶段不得承诺 Host 重启后恢复。

## 3. 输出
终态使用以下形状；失败时 `isError: true`，同时保留计划结果与标准 `error`，不能只返回失败步骤：
```json
{
  "planId": "<id>", "project": { "name": "Line1", "path": "<target>" },
  "execution": "partially_committed", "dryRun": false,
  "completedSteps": [1],
  "steps": [
    { "index": 1, "op": "page.create", "execution": "committed", "reason": null, "undo": "AI: plan 12345678 1/2 page.create", "recovered": false },
    { "index": 2, "op": "macro.insert", "execution": "rolled_back", "reason": "EPLAN_OPERATION_FAILED", "undo": null, "recovered": false }
  ],
  "backup": "<backup-link-file>", "stepBackups": [], "verification": "skipped",
  "validation": { "objectData": [], "electrical": "skipped", "drawing": "skipped" },
  "diff": { "summary": {}, "entries": [], "coverage": {} },
  "error": { "reason": "EPLAN_OPERATION_FAILED", "message": "Step 1 committed; step 2 rolled back. Inspect eplan_plan_get before deciding how to continue or restore.", "retryable": false, "metadata": {}, "eplanMessages": [], "correlationId": "<id>" },
  "truncated": false, "hint": null, "resource": "eplanmcp://plans/<id>"
}
```

`outputSchema` 声明终态和任务回执两个分支。终态 `execution` 为下表五值；步骤只用 ADR-0010 四值；`completedSteps` 是已提交步骤序号，未发送的步骤为 `not_started`、`reason/undo=null`。路径、名称为 string；无备份为 null，不能用源 `.zw1` 充当备份；`stepBackups` 记录步骤号和副本路径。错误含计划级 execution；单步原因和结果继续保留。`resume` 成功编译时回显新 `planId` 及 `resumedFrom`，原记录不改写。

| 计划 `execution` | 条件（ADR-0023，unknown 优先） |
|---|---|
| `not_started` | 没有一步开始；包括 dry-run、审批拒绝、初检冲突、已有项目的计划前备份失败 |
| `rolled_back` | 第一步失败且已完整回滚，没有其他步骤开始 |
| `committed` | 全部步骤提交；不等同于校验通过 |
| `partially_committed` | 至少一步提交，后续一步 not_started/rolled_back 后停下；即使单步错误可重试，计划也不可重试 |
| `unknown` | 任一步 OUTCOME_UNKNOWN；计划不可重试，按项目及当时实例暂停写入 |

- `verification` 为 passed/failed/skipped，仅汇总本期对象数据要求：全部必需项可证且通过才为 passed；有失败为 failed，其余未完成/未知为 skipped 并附原因。逐项为 passed/failed/unknown，未覆盖项不得算通过。提交后发现校验差异仍为 `committed`、`verification: failed`，附警报供审查，不自动修复；其他两层 skipped 不冒充已验证。
- 等待未完返回 `{taskId,status,planId}`（任务受理回执，不是最终 execution）；状态及 90/110 秒窗口与 [`eplan_task_get`](eplan_task_get.md) 一致。任务结果引用同一计划；不能用回执推断未写入。建议映射：等待审批 waiting_approval，执行/核实 running；全部提交 succeeded（仍须读 verification）；确定失败/部分完成 failed；确认取消 cancelled；中断 interrupted；任一步未知 unknown（映射待 Q2 统一）。
- structuredContent、同内容 JSON 文本和一行计数摘要，如 `Plan stopped: 1 step committed, 1 rolled back; inspect the diff and backup.`；默认约 5k、硬上限 20k token，含双份 JSON。超限保留计划级结果、失败位置、完成计数、备份/结果引用及 `truncated/hint`，完整步骤和差异用 `eplan_plan_get` 分页取。

## 4. 错误
| 错误码 | 什么时候发生 | 计划 `execution` / 提示 |
|---|---|---|
| `INVALID_ARGUMENT` / `RESOURCE_NOT_FOUND` | 非法参数、已消耗计划直接重放，或 ID 不可见/不存在 | `not_started`；提示查询原结果或重新编译，不为取得 taskId 重放 |
| `PLAN_STALE` | 过期、内容被改、身份/配置/资源/能力/前置条件或每步变更与批准不符 | 初检 `not_started`；中途按已提交步骤聚合；`metadata.conflicts[]` 为 `{step,locator或scope,reason,expected,actual}`，提示重新编译并重新审批 |
| `POLICY_DENIED` / `APPROVAL_DENIED` / `APPROVAL_EXPIRED` | 策略禁止、拒绝、无人回答或过期 | `not_started`；沿用既有渠道和错误，不绕过策略 |
| `BACKUP_FAILED` | 复制失败、超过备份时限、页数不符 | 初始备份 `not_started`；中途备份按已提交步骤聚合；报告已有备份和停止位置 |
| `CAPABILITY_MISSING` | 必需的新操作尚未实现/验证，不能编排模块或独立备份 | `not_started`；列缺项，不跳过备份或用 action 替代 |
| `AUDIT_UNAVAILABLE` / `WRITES_SUSPENDED` | 审计不可写或项目/实例暂停 | 首步前 `not_started`，中途保留已提交事实；查询不解除暂停 |
| `OUTCOME_UNKNOWN` | 执行响应丢失且结果取不回、回滚不能确定 | `unknown`；`Do not repeat this call. Ask the engineer to inspect the project and backup.` |
| `TIMEOUT`、`EPLAN_BUSY`、锁定、授权、对象/连接类错误、`INTERNAL` | 各步沿用原工具 | 聚合单步结果；写 execute 发出后不能直接报告可重试 TIMEOUT，先走 ADR-0010 |

始终满足：retryable 为 true 只允许 not_started/rolled_back；unknown 当且仅当某一步 reason=OUTCOME_UNKNOWN。错误不泄露本机路径；备份路径放计划结果的数据字段，审计保留实际路径。M6 新错误登记随实现补齐。

## 5. 实现要点
1. **重核对**：权限与归属先于存在性；核对期限、planHash、规范化项目身份、绑定修订/锁摘要、资源 SHA-256 和能力。独立步骤由 `project.snapshot`/`object.resolve` 核对定位符、内容和范围集合哈希；任何冲突列全后 `PLAN_STALE`。无配置/代次的 null 不作为“状态未变”的证据。
2. **一次使用与审批**：一个 planId 只能有一个执行者/任务，完成或失败的计划不能原样再执行。按最高 tier 判策略：仅 Write 且 scratch 自动允许，外部按 writesOutsideScratch，含 Destructive 始终审批；系统主数据不得进计划。暂停确认仍按 ADR-0010 单独处理，不并入计划批准。
3. **审批与备份**：按第 6 节整体批准，批准后再次核对内容和独立前置条件，随后已有目标在**第一步之前**用 `Eplan.EplApi.DataModel.ProjectManager.CopyProject(..., CopyMode.Snapshot)` 备份、核对页数。副本目录由 Host 新建，遵循 ADR-0013，不自动删除。A18/S12 验证打开项目可复制约 0.6–0.7 秒；独立 Bridge 入口与预算未实现，见 Q1。
4. **逐步执行**：每步先真实规划；独立变更必须与批准一致，依赖步骤按获批预测模式匹配且不扩大修改范围。执行侧还须在同一次主线程调用内重规划/比对并核对临时指纹，不能只依赖 Host 先后两次调用。随后各步各自 `LockingStep → SafetyPoint → UndoStep → QuietModeStep`，读回、提交；每步之间释放主线程，第一次失败/取消即停，不撤回前面步骤。名称为 `AI: plan <ID前8位> <k>/<n> <op>`，一次 Ctrl+Z 撤销一步（页/改标识 A17，宏 A14，删页/重编号 A18；模块/占位符仍未验证）。
5. **Destructive 步骤备份**：每个 Destructive 步之前另按 ADR-0013 备份，不能以计划初始备份抵扣。
6. **结果核实**：发出 execute 前写 execution_started；超时/断开按原请求 ID、原 Bridge 的 `outcome.get` 核实，不重发、不在上步未确定时发下步（ADR-0010）。取回 committed 标 recovered 并另读实际；取不回就 unknown/暂停。A15/S8 说明进程死亡仍可能留下未提交修改且撤销历史丢失；不能声称崩溃自动回滚。Host 重启只保留已有 DanglingWrites 的核实/暂停义务，不自动继续整份计划。
7. **对象数据层校验与差异**：第 2 阶段仅比较执行前、预期、执行后的规范快照（修改范围及邻页），逐项检查对象存在/登记属性和范围外变化；快照能力见 workflow 规格。差异五类：按计划完成、计划未出现、计划外但范围内、范围之外、执行期间外部修改。每步前后取证，不能可靠归因的写 unknown，不把它武断归为外部修改；未取到的范围外数据不声称没变化。失败也保存已知快照/差异与覆盖范围；电气关系和图纸层均 skipped，留第 4 阶段（M6 4.9/4.16）。

## 6. 写操作专项
- **dry-run**：只读重核对，返回计划预览和 `execution: not_started`；不推进状态、不启动审批任务，不能将未验证能力显示为可执行。
- **审批内容/绑定**：按 ADR-0023 展示计划 ID、项目、默认柜、最高级别、备份位置、按步/页的清单、Destructive 醒目标记、预测模式及撤销限制。Workbench 展示完整清单，EPLAN 对话框每步摘要及前 50 条，完整文本在 `state\plans\<planId>\changes.txt`。绑定请求 ID、planHash、独立步骤临时指纹组合、参数摘要，一次性、期限沿用既有渠道；不经 MCP elicitation。
- **现有实现与新增工作**：[WritePipeline](../../src/EplanMcp.Host/Safety/WritePipeline.cs) 当前一次 plan/execute、按步审批；[ApprovalTexts](../../src/EplanMcp.Host/Safety/ApprovalTexts.cs) 及 [AuditLog](../../src/EplanMcp.Host/Audit/AuditLog.cs) 已有清洗/绑定/哈希链。需计划外层编排及受控的逐步执行入口，不能循环 RunAsync 造成多次审批，也不能让 MCP 参数关闭审批。现有 `ProjectBackups.Make` 仅被 Destructive execute 内部使用，无独立计划初始备份操作。
- **审计**：新增 plan_approval、plan_result（ID、hash、tier、步骤摘要、批准渠道/决定、备份、校验/差异摘要）；每步 execution_started/result 带 planId、step、请求 ID、参数摘要、指纹、范围、来源版本/哈希、execution、reason、recovered；结果记录失败不把已提交改成回滚，按既有悬空写入规则处理。
- **等待/取消**：任务仅 Host 内存，按 task_get 的归属与保留限制；每 5 秒执行心跳、审批 15 秒，数值单调。等待窗口用尽不取消，取消 get 只结束等待；显式取消 apply 在安全点停止后续步骤并核实当前步。MCP 被取消请求不回复，终态仍写审计/计划；不增加 task_cancel。
- **API 纪律**：复用现有 Bridge 操作，不在 Host 调 EPLAN；A36 要求一次取/释放属性列表和释放 PropertyValue，A39 禁止 Dispose 返回的 StorableObject。`module.insert` 的 ApplyRecord 重载、提交和撤销待验收，不假设嵌套 SafetyPoint 可行。

## 7. 测试
- **Small**：schema/title/静态注解与最高 tier；单次消耗；ADR-0023 五值聚合、不变式；校验失败仍 committed；预测模式/范围和冲突明细；预算及来源记录。
- **Medium**：Fake Bridge 覆盖重核对→一次审批→初始备份→逐步事务；Destructive 再备份；dry-run 无副作用；中途失败保留完成步骤/备份、不重试；审批前后篡改、手工更改和选择改变（4.17 场景 4、5）；延迟提交、断线、outcome 取回/未知且不发下一步（场景 7）；审计/取消/轮询。
- **Large / Agent**：仅示例材料的 scratch 副本；已有目标多步写入、按步 Ctrl+Z、可打开备份、改范围内对象后 PLAN_STALE 列冲突；模块来源回指参考项目（场景 3），缺资源/能力明确失败（场景 8）。跨版本、模块持久性和组合撤销均不能从已合入测试推断已验证。本次只写文档，不运行 EPLAN。

## 8. 待定问题
| # | 问题 | 建议 |
|---|---|---|
| Q1 | 已有目标的独立备份、步内重新规划、undo 名称和计划消耗点的合同尚缺 | 另细化独立备份能力/操作名；备份/整计划时限、审批前失败是否终结计划及跨 Host 互斥细节仍待定 |
| Q2 | resume 的阶段、任务映射/进度及结果引用如何与 task_get Q2 对齐？ | ADR-0023/4.13 的继续语义必须保留；建议本期只对同一 Host 已知结果重新编译剩余步骤，旧/新计划关联，重新审批；具体是否本期开放 resume 待定，未开放时拒绝 true。持久化检查点、Host 重启后恢复与 MCP Tasks 明确留第 5 阶段 |
| Q3 | standard 快照不含占位符和完整引用属性，如何界定写范围并校验模块？ | 全部可能写入的引用范围须先能确定，否则报缺能力；新增覆盖另定合同。范围已明确但属性未覆盖的校验项为 unknown；组合提交/一次 Ctrl+Z、页/项目数据及布尔重载效果仍未验证（A33、S32），不能越过获批范围 |
| Q4 | 动态 tier/静态注解、结构化冲突如何接入当前注册/错误合同？ | 本文 destructiveHint=true 为目录最高风险的保守提议，实际审批仍按计划 tier；需补注册/契约检查。当前 BridgeError.Metadata 为 string 字典，conflicts 数组的 Host/Bridge 表达须细化，不能声称已有强类型合同 |

未定的进程拓扑、IPC、跨客户端权限/审批模型和支持版本不在本规格决定；若需改变，另提 Proposed ADR。

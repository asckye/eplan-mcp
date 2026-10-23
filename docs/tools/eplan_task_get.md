# 工具规格：`eplan_task_get`

| 项 | 内容 |
|---|---|
| 工具名 / title | `eplan_task_get` / `Get task` |
| 所属分组 / Profile | `task`；M6 工具集 `workflow` / `full`，不进 `core` |
| 安全级别 | Read |
| MCP 注解 | `readOnlyHint` = true　`destructiveHint` = false　`idempotentHint` = true　`openWorldHint` = false |
| 状态 | In Review（未实现；本期合同以[实施设计](../design/2026-10-23-m6-phase2-plans.md) D10–D13 为准） |

## 1. 这个工具解决什么任务
“刚才的计划还在等审批吗？”“任务完成了吗，结果是什么？”可能耗时长的工具在等待窗口内未完成时返回 `taskId`；代理用本工具继续等待或读取状态。返回任务 ID 不取消任务（[M6 设计](../design/2026-10-21-m6-engineering-packs.md) 4.13）。它不发起计划、不回答审批、不取消任务，也不重做写步骤。

第 2 阶段只做 Host 内存中的任务和等待加轮询（4.16、4.17 场景 7 的单次运行范围）。4.13 / 3.3 的 `state\tasks\<taskId>.json`、检查点恢复、MCP Tasks 适配属于第 5 阶段，不能描述为当前已具备；`task_list`、`task_cancel` 也不在本规格中。

## 2. 输入参数

| 参数 | 类型 | 必填 | 默认值 | 说明（写给模型看的） |
|---|---|---|---|---|
| `task_id` | string，非空 | 是 | — | 发起工具返回的 `taskId`，不透明、会过期，只在创建它的 Host 进程及其客户端范围内有效 |
| `wait_seconds` | integer，0–110 | 否 | 90 | 最多等待多少秒；0 立即读取。到时任务未结束也正常返回当前状态，任务继续运行 |

- schema 为对象，`additionalProperties: false`；`wait_seconds` 的 `default: 90`、`minimum: 0`、`maximum: 110` 写入 schema。示例：`{"task_id":"<returned-task-id>","wait_seconds":0}`。
- 输入用 snake_case，输出沿用设计中的 `taskId`。它不是 planId、Bridge 的请求 ID 或 EPLAN 对象句柄，不能从路径、进程号或名字猜造。
- 本规格建议的最小等待语义：已有终态立即返回；否则等到终态或等待窗口耗尽，返回当时的快照。非终态（包括 `waiting_approval`）可用 0 秒即时查询；状态通知唤醒策略见第 8 节 Q2。90/110 秒来自设计 4.13，不是任务执行或审批的期限。

## 3. 输出

```json
{
  "taskId": "<returned-task-id>",
  "kind": "plan_apply",
  "status": "waiting_approval",
  "progress": { "message": "Waiting for approval", "completedSteps": 0, "totalSteps": 3 },
  "result": null,
  "error": null,
  "createdAt": "2026-10-22T10:00:00Z",
  "updatedAt": "2026-10-22T10:00:01Z",
  "startedAt": null,
  "completedAt": null,
  "expiresAt": null
}
```

- `outputSchema` 为对象，声明以上全部字段；`taskId`、`kind` 为 string，`status` 是下表八值的 `enum`；时间为 UTC RFC 3339 string，除 `createdAt`、`updatedAt` 外可为 null。`expiresAt: null` 表示未给出确定过期时间，不保证永久保留；保留规则见第 5 节及 Q1。
- `createdAt` 为登记时间，`updatedAt` 为状态/进度/结果最后变化时间，读取不刷新它；`startedAt` 为实际开始执行时间，`completedAt` 为进入终态的时间，尚未发生则为 null。

| `status` | 含义 |
|---|---|
| `queued` | 已登记，尚未执行 |
| `running` | 正在执行或核实当前步骤的结果 |
| `waiting_approval` | 正在等待工程师通过既有审批渠道回答；不是执行失败 |
| `succeeded` | 任务已完成，`result` 给原工具的结果或结果引用 |
| `failed` | 任务已结束，`error` 给原工具的结构化错误及已知执行结果 |
| `cancelled` | 底层任务已确认取消并核实执行结果；取消查询本身不会产生此状态 |
| `interrupted` | 任务中断，未完成；不能据此推断项目没有修改 |
| `unknown` | 底层写入结果仍无法确定；保留 `OUTCOME_UNKNOWN` 及恢复提示 |

- `progress` 为对象或 null：`message` 为清洗过的 string，`completedSteps`、`totalSteps` 为非负 integer 或 null；不知道总数时不制造百分比，等待审批不声称有执行进展。这是建议的最小表示，最终与计划执行器统一（Q2）。
- `result` 为原工具结果对象或结果引用对象，未有结果时为 null；计划至少能以 `planId` 关联 `eplan_plan_get`。`error` 为标准 `{reason, message, retryable, metadata, eplanMessages, correlationId}` 错误对象或 null；底层写错误还保留其 `execution`。失败/中断时可以同时带已完成步骤的结果引用和错误，不能丢掉部分完成事实。
- 任务状态与项目修改状态分开：本 Read 工具不在顶层添加 `execution`。`result` / `error` 内原样保留单步 ADR-0010 四值和计划 ADR-0023 的 `partially_committed`；任务 `succeeded` 不能代替校验通过，`failed`/`cancelled` 不能代替 `rolled_back`。状态与计划级结果的完整对应表留给计划规格（Q2）。
- **任务失败也是查询成功**：找到任务时正常返回快照，`isError` 不因 `status: failed` 为 true；查询自身失败才按第 4 节返回 `isError: true`。`unknown` 不随查询变成可重试。
- 返回 `structuredContent`、同内容 JSON 文本块和一行摘要，如 `Task <id> is waiting for approval (0 of 3 steps completed).` 或 `Task <id> succeeded; see 'result'.`。只返回一个任务，不分页、不设 `response_format`；较大结果给引用，遵守工具结果预算，不能把整份项目快照塞进结果。

## 4. 错误

| 错误码 | 什么时候发生 | 返回给模型的提示 |
|---|---|---|
| `INVALID_ARGUMENT` | `task_id` 为空，`wait_seconds` 非整数或超出 0–110 | `metadata.violations` 一次列出全部问题；提示使用发起工具返回的 ID |
| `RESOURCE_NOT_FOUND` | ID 未知、已过期、Host 重启后丢失，或不属于当前 Host/客户端可见范围 | `This task is unavailable in this Host session. Check the original operation's result; do not repeat a write just to obtain a new task ID.` |
| `INTERNAL` | Host 查询逻辑缺陷 | 附关联 ID，请工程师提交 Issue |

`RESOURCE_NOT_FOUND` 已在 `Contracts/ErrorReason.cs` 和标准错误表登记，本期扩展到任务用途（D10–D13），不能用 `HANDLE_EXPIRED` 等代替。等待窗口用尽不是错误；EPLAN 离线不妨碍查询，底层连接错误保存在任务中。本 Read 工具的查询错误不带 `execution`。

## 5. 实现要点

- **Host 内存仓库**：创建任务的工具登记 ID、归属、种类、状态、进度、结果/错误、时间；查询读取一致快照，等待使用异步通知，不阻塞 EPLAN 主线程。不调用 Bridge 或任何 EPLAN API，无 `SafetyPoint`、`LockingStep`、`QuietModeStep`。
- **等待与取消**：最多等待 110 秒，不以循环重发计划来实现；有 `progressToken` 时按工具规范每 5 秒心跳，使用单调递增的已用秒数。取消这次 get 仅结束等待，不取消底层任务、不延长审批、不触发新审批；遵守 MCP 取消请求不回复的规则。发起工具的 `wait_seconds` 到时返回任务 ID，本工具的等待到时返回同一任务快照。
- **结果恢复**：任务生产者负责调用 `outcome.get` 并更新任务；get 只观察。执行中断时不能先报可重试 `TIMEOUT` 或“安全取消”，必须先核实底层执行结果；取不回时按 ADR-0010 暂停写入。继续计划必须重新编译、重新审批（ADR-0023），查询不能解除暂停。
- **保留**：第 2 阶段任务只在本 Host 进程存活期间可查，退出/重启后内存消失；不承诺另一个 Host 或客户端重连后能找回。不能把 Bridge 的 256 条 outcome 缓存或计划的 24 小时有效期套到任务上。活跃任务不得因容量挤出而丢失追踪；终态的 TTL、容量、清理时机设计未定（Q1），到期清理后统一 `RESOURCE_NOT_FOUND`，查询不删除任务。
- **安全**：先检查任务归属，再查可见对象；其他客户端的 ID 与不存在的 ID 给同样的错误，不暴露它是否存在、项目名、路径或审批信息。第 2 阶段只允许同一 Host 进程及创建任务的客户端查看；不新增跨进程共享、IPC、可转交的查询凭据或客户端审批回答入口。具体客户端归属键、重连语义见 Q3。
- **数据与日志**：任务中的项目名、变更摘要、错误消息、进度文本均为数据，经 ResultShaper 清洗和预算限制（SEC-08、SEC-10、SEC-12）；不返回堆栈、内部管道名或审批令牌。不因轮询重复写执行审计，底层写入的审计义务不变。

## 6. 写操作专项
不适用。查询不需要审批、没有 `dry_run`，不增加底层任务的权限。等审批的计划仍按 ADR-0023 的整体变更清单审批一次，不为每次 get 再请求批准。

## 7. 测试

- **Small**：输入 schema（默认 90、上限 110、0、负数、111、非整数、空 ID、未知字段）、title 和四注解；八状态、进度未知/总数未知、各时间可空；未知/过期 ID；任务失败时查询仍成功；不加顶层 `execution`，嵌套结果保留 `partially_committed`、`OUTCOME_UNKNOWN` 和不可重试约束；结果预算与文本清洗。
- **Medium**（Host + 内存仓库、假任务执行器/Fake Bridge，使用可控时钟）：0 秒即时返回；默认/最大等待到期仍返回同一非终态任务；终态提前唤醒；多次 get 不重复执行。`waiting_approval` 到批准/拒绝/过期的状态更新不重复审批；延迟提交或步骤间断线后结果正确、不会发下一步或重发写步骤（M6 4.17 场景 7）。取消 get 后任务继续；不同 Host、不同客户端不可见；Host 重启后旧 ID 为 `RESOURCE_NOT_FOUND`，不得自动重做写操作；终态清理测试按 Q1 定稿补齐。
- **Large**：本查询无 EPLAN 行为，独立验证不需要实机。随第 2 阶段 scratch 计划任务验收：等待审批可查、完成后能取得校验/差异及步骤结果；中途失败保留已提交步骤和备份引用，查询不改变项目。EPLAN 的每步撤销、备份可用性由计划工具验收，不把它们算作本工具已验证的能力（M6 4.16）。
- **Agent 评测**：“计划返回了任务 ID，等它完成，报告结果和仍需工程师处理的事项”。另测等待审批和 `unknown`：代理应报告状态及下一步，不代答审批、不重复写调用；`RESOURCE_NOT_FOUND` 时先核对原调用、计划结果和审计，不把任务记录丢失理解为项目未修改。

## 8. 待定问题

| # | 问题 | 建议 |
|---|---|---|
| Q1 | 终态保留、容量与读取续期 | 已决定：[实施设计](../design/2026-10-23-m6-phase2-plans.md) D10、4.5；终态 1 h/256 条、不续期；活跃上限 8，不淘汰追踪 |
| Q2 | 进度、结果引用、状态映射与唤醒 | 已决定：同设计 D11、4.5；固定 planId/revision 引用，等终态或到期；部分完成/校验失败为 failed，保留 execution |
| Q3 | 客户端归属与重连 | 已决定：同设计 D12、4.5；Host 运行实例+stdio 会话，不信 clientName/IP/ID；Stateless HTTP 缺能力，跨 Host/重连不继承 |
| Q4 | 持久化、恢复、Tasks/Resources SDK | 已决定：同设计 D13、4.4–4.5/4.7；内存任务不恢复，保留 DanglingWrites 核实/暂停；只读资源合同验证，Tasks 留阶段 5 |

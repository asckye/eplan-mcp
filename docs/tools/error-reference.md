# 共享结果契约

本文记录 [ErrorReason](../../src/EplanMcp.Contracts/ErrorReason.cs)、[ExecutionState](../../src/EplanMcp.Contracts/ExecutionState.cs) 与工具快照中现有的返回值；具体工具的触发条件见其参考页。通用设计规则由[手册 MCP 与错误处理条款](../standards/engineering-handbook.html#mcp-errors)承担。

## 项目错误结构

当前业务错误以 `isError: true` 的工具结果报告，结构包含 `error.reason`、`message`、`retryable`、`metadata`、`eplanMessages`、`correlationId`。协议错误由 SDK 处理，Host 的绑定链见[架构错误模型](../architecture/architecture.md#82-错误模型)。示例：

```json
{"error":{"reason":"PAGE_NOT_FOUND","message":"Project 'Line1' has no page '=AP+ST1/9'. Call eplan_page_list to see its pages.","retryable":false,"metadata":{"project":"Line1","page":"=AP+ST1/9"},"eplanMessages":[],"correlationId":"c0a8…"}}
```

## 写入执行状态

写入工具当前结果和错误使用 [ADR-0010](../adr/0010-write-outcomes.md) 的 `execution` 四值；Read 结果不带此字段。

| 值 | 含义 |
|---|---|
| `not_started` | 没有开始修改：执行前被拒绝，或者是 `dry_run`、变更清单为空 |
| `rolled_back` | 开始修改，失败后已回滚，项目与调用前相同 |
| `committed` | 修改已提交 |
| `unknown` | 无法确定项目是否被修改 |

现有 Small 测试覆盖两个不变量：`retryable: true` 只对应 `not_started` 或 `rolled_back`；`execution=unknown` 当且仅当 `reason=OUTCOME_UNKNOWN`。计划工具的终态见各自参考，不用这四值替代计划状态。

## 错误码

下表是共享 reason 的当前含义及写工具阶段对照，`—` 表示原契约不以此表指定统一写入阶段。

| reason | retryable | `execution` | 含义与给模型的提示 |
|---|---|---|---|
| `INVALID_ARGUMENT` | 否 | `not_started`；执行中读回才发现时为 `rolled_back` | 参数不合法；`metadata.violations` 列出每个字段的问题。有些问题只有 EPLAN 执行后才能看出，例如项目结构不用名称中的某一部分，EPLAN 把它丢掉了（api-notes A13）；这时已回滚，`metadata` 给出请求的值和 EPLAN 实际得到的值 |
| `PROJECT_NOT_OPEN` | 否 | `not_started` | 指定的项目没有在 EPLAN 中打开；提示用 `eplan_project_list` 查看已打开的项目 |
| `AMBIGUOUS_NAME` | 否 | `not_started` | 名称对应多个对象：同一个短标识（如 `-K1`）出现在不同结构下、两个打开的项目同名，或者没有选中项目而打开了好几个；`metadata.candidates` 列出完整名称（每行一个，最多 20 个），提示选一个再调用 |
| `PAGE_NOT_FOUND` / `DEVICE_NOT_FOUND` / `SYMBOL_NOT_FOUND` / `PART_NOT_FOUND` / `MACRO_NOT_FOUND` | 否 | `not_started` | 对象不存在；提示对应的查询工具 |
| `FILE_NOT_FOUND` | 否 | `not_started` | 导入工具的文件不在导入目录（`<数据根目录>\imports\`）中；`metadata.file` 重复给出的路径，提示先把文件放进导入目录。不写导入目录的本机路径 |
| `ALREADY_EXISTS` | 否 | `not_started` | 例如设备标识已存在；说明合并的后果，以及如何显式允许合并 |
| `HANDLE_EXPIRED` | 否 | `not_started` | 句柄过期，或者所属的 EPLAN 会话已经结束；提示按名称重新查询 |
| `SESSION_NOT_SELECTED` | 否 | `not_started` | 有多个 EPLAN 实例；提示调用 `eplan_session_select` |
| `EPLAN_NOT_CONNECTED` | 是 | `not_started` | 找不到运行中的 EPLAN，或插件没有加载；提示启动 EPLAN，或运行 `eplanmcp doctor` |
| `EPLAN_BUSY` | 是 | `not_started` | 主线程在超时时间内不可用（有模态对话框或正在运行的操作）；提示请工程师关闭对话框 |
| `BRIDGE_INCOMPATIBLE` | 否 | `not_started` | 插件和 Host 的协议版本不兼容；提示运行 `eplanmcp install-addin` |
| `LICENCE_MISSING` | 否 | 执行前 `not_started`，事务中 `rolled_back` | 缺少 API 授权，或授权已到期 |
| `LICENCE_RESTRICTED` | 否 | 执行前 `not_started`，事务中 `rolled_back` | 授权存在但受限，例如开发者授权的页数限制，或只允许加载已签名的插件；提示运行 `eplanmcp doctor` 进行授权自检（ADR-0007） |
| `LOCK_FAILED` | 是 | `rolled_back` | 对象被其他用户锁定（多用户模式）；`metadata` 列出被锁定的对象 |
| `POLICY_DENIED` | 否 | `not_started` | 策略禁止，例如 Code 级工具未启用；说明是哪条策略，**不提示如何绕过** |
| `APPROVAL_DENIED` | 否 | `not_started` | 工程师拒绝或取消了审批；项目未改变 |
| `APPROVAL_EXPIRED` | 否 | `not_started` | 审批请求过期；需要重新发起调用 |
| `PLAN_STALE` | 否 | `not_started` | 审批之后项目已经改变；需要重新发起调用以生成新的计划 |
| `EPLAN_OPERATION_FAILED` | 视情况 | `rolled_back` | EPLAN 报告失败；`eplanMessages` 中附带 EPLAN 的系统消息；已回滚 |
| `TIMEOUT` | 是 | `not_started` | 读工具执行超过时限；写工具只在规划阶段超时，或已确认执行请求没有送达时返回。写工具发出执行请求后不再直接返回它，而是先向 Bridge 取回结果（[ADR-0010](../adr/0010-write-outcomes.md)） |
| `AUDIT_UNAVAILABLE` | 否 | `not_started` | 审计无法写入，所以写操作被拒绝 |
| `INTERNAL` | 否 | 按发生的阶段：执行前 `not_started`，事务中 `rolled_back`，Bridge 已提交之后 `committed` | 本项目的缺陷；附上关联 ID 和精确输入，交给负责人形成修复 brief |
| `OUTCOME_UNKNOWN` | 否 | `unknown` | 写操作被中断，取不回结果，无法确定项目是否被修改。提示**不要重复调用**，先请工程师在 EPLAN 中检查，可以用读工具查看现状；该项目的写入随之暂停 |
| `PACK_INVALID` | 否 | — | 工程包或开发包校验不通过（ADR-0024：文件与清单不符、不允许的类型、超限；没有来源的正式规则）；`metadata.problems` 逐条列出，什么都没有安装或构建 |
| `RESOURCE_NOT_FOUND` | 否 | — | 工程包、版本、资源或计划不存在/不可见；计划先查会话归属，不泄露存在性；其他资源提示用 `eplan_pack_list`、`eplan_asset_search` 查询 |
| `RESOURCE_EXHAUSTED` | 否 | — | 计划存储、任务容量或计划项目锁不可用；metadata 为 `{resource,limit,used}`，受理前为 `not_started`；不自动重试，不删除未到期记录腾位置 |
| `CAPABILITY_MISSING` | 否 | — | 包或资源要求的能力（Bridge 操作、授权选项、工具、EPLAN 版本或变体）当前不满足；`metadata.missing` 列出缺的项；计划查询在 stateless HTTP 上缺少 transport:stable_session（M6 设计 4.4–4.5） |
| `CONFIG_CONFLICT` | 否 | — | 配置分层中下层放宽了硬约束，或生效的默认值不满足约束；指出冲突的两处来源（M6 设计 4.3） |
| `WRITES_SUSPENDED` | 否 | `not_started` | 该项目（或当时那个 EPLAN 实例）的写入因此前结果未知而暂停；提示请工程师在 EPLAN 中核实，下一次写调用会请求确认，确认后解除 |
| `BACKUP_FAILED` | 否 | `not_started` | Destructive 写操作执行前没能备份项目：复制失败、超过执行时限的一半，或副本的页数与项目不符（[ADR-0013](../adr/0013-destructive-backup.md)）；写系统主数据之前没能备份库：备份失败、超时，或备份目录中没有恰好一个非空的归档（[ADR-0019](../adr/0019-master-data-writes.md)）。项目和库都未改变；提示请工程师检查磁盘空间，或运行 `eplanmcp doctor` |
| `CANCELLED` | 否 | 开始执行前 `not_started`，事务中 `rolled_back` | 客户端取消了调用，Bridge 在下一个安全点停下，提交之前发现取消就回滚（[M4 设计](../design/2026-10-20-m4-workflows.md) 4.3）。MCP 规定不回复被取消的请求，所以模型看不到它，只出现在 `outcome.get` 的结果和审计中 |

`CAPABILITY_MISSING` 的上述含义仅记录包、资源和会话能力检查的现有契约；缺失或不可读的业务数据本身不构成违规结论。

## 契约来源

当前字段、枚举及注解由 [Contracts](../../src/EplanMcp.Contracts/) 和 [server-contract.verified.json](../../tests/Contracts.Snapshots/server-contract.verified.json)记录；测试入口见 [Contracts 测试说明](../../tests/EplanMcp.Contracts.Tests/README.md)。

# 工具规格：`eplan_page_delete`

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_page_delete` |
| 所属分组 / Profile | `page` / `full` |
| 安全级别 | Destructive |
| MCP 注解 | `readOnlyHint` = false　`destructiveHint` = true　`idempotentHint` = false　`openWorldHint` = false |
| 状态 | Proposed（M4 第 11 步；依赖第 10 步的 Destructive 流水线，见第 6 节） |

## 1. 这个工具解决什么任务
"把 `=GAA+A1&EFS1/60` 和 `/61` 删掉"。代理给出完整页名，一次调用删除 1–50 页：每次都要工程师审批（scratch 中也一样），执行前自动备份整个项目，删除后在 EPLAN 中按一次 Ctrl+Z 就能全部恢复。删页会让其他页上的辅助功能失去主功能（S12，A18），变更清单和结果都列出这些设备；报表不随之更新，要再调用 `eplan_report_update`。删页不常用，又是 Destructive，所以放在 `full` Profile。

## 2. 输入参数
| 参数 | 类型 | 必填 | 默认值 | 说明（写给模型看的） |
|---|---|---|---|---|
| `project` | string | 是 | — | 项目名，或 `eplan_project_list` 返回的 `path`。写工具必须显式给出 |
| `pages` | array of string，1–50 项 | 是 | — | 要删除的页，完整页名如 `=GAA+A1&EFS1/60`（用 `eplan_page_list` 查） |
| `dry_run` | boolean | 否 | false | 为 true 时只返回将要删除的页和受影响的设备，不请求审批，不备份，也不修改项目 |

- 页名只做精确匹配，不像 `eplan_page_get` 那样在只差大小写时退而匹配唯一的一页：删除的对象必须是模型明确写出的页。同一页出现两次时返回 `INVALID_ARGUMENT`。

## 3. 输出
```json
{
  "project": "Line1",
  "execution": "committed",
  "dryRun": false,
  "pages": [ { "name": "=GAA+A1&EFS1/1", "type": "Circuit", "functions": 59 } ],
  "orphaned": [ { "tag": "+A1-WE1", "functionsLeft": 6, "pages": ["=GAA+A1&EFS1/2"] } ],
  "backup": "C:\\Users\\<user>\\EplanMcp\\backups\\Line1\\20261021T083000123Z\\Line1.elk",
  "undo": "AI: eplan_page_delete",
  "recovered": false,
  "truncated": false,
  "hint": "Reports are not updated when pages are deleted; call eplan_report_update."
}
```
- `pages`：删除的页，带页类型和页上的功能数。
- `orphaned`：主功能在被删的页上、在其他页上还有同名功能的设备（S12：电缆的芯线）。删除后这些功能留在项目中，没有主功能。`functionsLeft` 是留下的功能数，`pages` 是它们所在的页（最多 10 个）。最多列 50 个设备，超出时 `truncated = true`，`hint` 给出总数。
- `backup`：备份副本的完整路径，工程师可以在 EPLAN 中直接打开它（第 6 节）。`dry_run` 时为 `null`。
- 提交后 `pages`、`orphaned` 是读回的结果；`dry_run` 时是计划。
- `hint` 总是提示更新报表（S12：报表页不随删页更新）。
- 页名和标识是项目数据，经过 ResultShaper。
- `undo`、`recovered`：同 `eplan_page_create`。
- 一行摘要：
  - `Deleted 1 page; 4 devices keep functions on other pages without their main function. A backup was made first (see 'backup'); Ctrl+Z in EPLAN undoes this step.`
  - `Dry run: would delete 1 page (59 functions); 4 devices would keep functions on other pages without their main function. Nothing was changed.`

## 4. 错误
| 错误码 | 什么时候发生 | `execution` | 返回给模型的提示 |
|---|---|---|---|
| `INVALID_ARGUMENT` | `pages` 为空、超过 50 项或有重复 | `not_started` | `metadata.violations` 列出全部问题 |
| `PAGE_NOT_FOUND` | 有页名找不到（包括只差大小写） | `not_started` | `metadata.missing` 列出全部找不到的页名，提示用 `eplan_page_list` 查准确的页名 |
| `PLAN_STALE` | 规划之后，这些页上的对象，或者受影响设备在其他页上的功能发生了变化 | `not_started` | 提示重新调用 |
| `BACKUP_FAILED` | 备份失败、用时过长，或副本的页数与项目不符（ADR-0013） | `not_started` | "Nothing was changed because the project could not be backed up first. Ask the engineer to check the free disk space, or to run 'eplanmcp doctor'." 不可重试 |
| `EPLAN_OPERATION_FAILED` | EPLAN 删页时报错，或者读回时页仍然存在 | `rolled_back` | 附带 EPLAN 的系统消息；备份照样保留 |
| `POLICY_DENIED` / `APPROVAL_DENIED` / `APPROVAL_EXPIRED` | 策略或审批；scratch 中也要审批 | `not_started` | 同错误码表 |
| 流水线、锁定、项目解析、连接与授权类错误 | 同 `eplan_page_create` | 同 `eplan_page_create` | 同 `eplan_page_create` |

`BACKUP_FAILED` 由 ADR-0013 新增，第 10 步加入 Contracts 和错误码表（mcp-tool-design 6.3）。

## 5. 实现要点
- **Host**：走 `WritePipeline`，级别 Destructive，规划操作是 `page.delete.plan`，执行操作是 `page.delete.execute`。执行时限为 `min(300, 60 + 2 × 页数)` 秒，包括备份。
- **受影响的设备**：被删的页上每个主功能（`IsMainFunction`，名称非空），在其他（不删的）页上有完整标识相同（`Name`，`Ordinal`）的功能，而其他页上没有同名的主功能。这与 S12 找出电缆芯线的办法相同。
- **规划**（只读）：
  - 精确解析每一页，读出页类型、页上的功能数，算出受影响的设备；
  - 每页一条 `delete` 变更：`Kind = "page"`，`OldValue` 为页类型和功能数。表单显示为 `delete page =GAA+A1&EFS1/1 (Circuit, 59 functions)`，`ChangeLines` 要补上 `delete` 分支；
  - 受影响的设备放在计划的 `details["orphaned"]` 中，每行一个，如 `+A1-WE1: 6 functions on =GAA+A1&EFS1/2`（最多 50 行，另加总数），审批对话框在变更清单之后单独列出，标题"删除后这些设备在其他页上的功能将失去主功能"；
  - 指纹包括两部分，排序后取 SHA-256：被删各页及其上全部对象的会话内标识；受影响设备在其他页上的功能的会话内标识。
- **执行**：Destructive 流水线（第 6 节）先重算指纹、做好备份，然后在 `TransactionScope`（`SafetyPoint` → `UndoStep` → `QuietModeStep`）中：
  1. 逐页 `Page.Remove()`；
  2. 读回：这些页名都已不存在，页数减少了相应的数目；重新算出受影响的设备；不符就抛出，整体回滚；
  3. `Commit()`，再 `CloseUndo()`。
- **已验证**（S12，A18）：在调度器中删除 `=GAA+A1&EFS1/1`（59 个功能）约 2.4 秒（含挑选页面），功能 2888 → 2830，电缆 `+A1-WE1`、`+A1-WE2`、`+A2-WE1`、`+A2-WE2` 在其他页上的 1–6 个芯线功能留下、没有主功能；报表页数不变，检查消息不变；能被 `SafetyPoint` 回滚，提交后 Ctrl+Z 一步撤销；不弹对话框。
- **没有验证**：一次删除多页；删除报表页、带黑盒或 PLC 卡的页；删页后报表更新和检查消息的变化。由 Large 测试覆盖前两项。

## 6. 写操作专项
- **Destructive 流水线**（第 10 步实现，`eplan_device_renumber`、`eplan_page_renumber` 共用；ADR-0005、ADR-0013、SEC-21）：
  - **顺序**：规划 → 策略 → 审批 → 审计"开始执行" → 执行（重算指纹 → 备份 → `TransactionScope` → 读回 → 提交）→ 审计结果。
  - **策略**：`PolicyEngine` 已有的规则：Destructive 在 scratch 中也要审批；scratch 之外的项目在 `writesOutsideScratch` 为 `deny` 时拒绝（`POLICY_DENIED`）。
  - **备份目录**：Host 在发出执行请求之前新建 `<数据根目录>\backups\<项目名>\<UTC 时间>\`，时间精确到毫秒（如 `20261021T083000123Z`），已存在时报错，从不复用；按 SEC-19 的规则确认它在备份根目录之下、每一级都不是链接或联接点；把副本的完整路径 `<目录>\<项目名>.elk` 放进执行参数。
  - **备份与修改在同一次主线程调度中**（M4 设计 4.3：一个写操作不拆成多次调度），工程师不能在两者之间改动项目。Bridge 调用 `new ProjectManager().CopyProject(项目的 .elk, 副本路径, CopyMode.Snapshot)`。S12：示例项目（213 MB）0.6–0.7 秒，项目保持打开、不加锁，副本可以直接打开，包含刚提交的写入。
  - **核对副本**：ADR-0013 要求副本的页数与项目相同。S12 是用 Remote Client 打开副本来数的；在调度器中怎样读副本的页数（打开副本会不会改变当前项目、耗时多少）没有验证，第 10 步实现前先在 scratch 上验证，写入 api-notes。
  - **备份失败**：`CopyProject` 抛出异常、副本页数不符，或者备份用时超过执行时限的一半（之后的修改可能在时限之外才结束），就不修改，Bridge 记为 `not_started`，返回 `BACKUP_FAILED`。Host 把这个目录改名为 `<UTC 时间>-failed`，不删除，免得被当作可用的备份。
  - **审计**：`execution_started` 记录加上 `backup`（副本路径）；`execution_result` 记录备份是否完成。备份从不自动删除，`doctor` 报告 `backups\` 占用的空间（ADR-0013）。
- **dry-run**：返回第 3 节的结果，`execution = not_started`，不备份。
- **审批**：每次都请求审批，scratch 中也一样。对话框显示工具、项目、每页一行的变更清单、受影响的设备，并注明"执行前会先备份到 `<数据根目录>\backups\<项目名>\`"和"可以按 Ctrl+Z 撤销'AI: eplan_page_delete'"（ADR-0011、ADR-0013）。
- **回滚**：事务中失败时 `SafetyPoint` 整体回滚，备份保留；提交后可以按一次 Ctrl+Z 撤销；EPLAN 在途中崩溃时修改可能留在项目里（A15），这时用备份恢复。
- **各阶段失败时的 `execution`**：规划、策略、审批、备份阶段 `not_started`；事务中 `rolled_back`；响应丢失后按 ADR-0010 取回结果，取不回时 `OUTCOME_UNKNOWN`（`unknown`）并暂停写入。
- 不是幂等的：再调用一次，页已经不存在，返回 `PAGE_NOT_FOUND`。执行请求从不重发（ADR-0010）。

## 7. 测试
- **Small**：受影响设备的计算（其他页有同名主功能时不算）；精确匹配页名；变更清单与 `details` 的文字；指纹；备份目录的新建和链接检查；`BACKUP_FAILED` 时 `execution` 为 `not_started`；摘要；schema 快照；注解；Host 程序集中 `IApprovalChannel` 的实现只有 `EplanDialogChannel`。
- **Medium**：在假 Bridge 上测试：
  - `dry_run` 不审批、不备份；scratch 中也请求审批；拒绝时不备份、不执行；
  - 备份失败时不执行，目录改名为 `-failed`；`PLAN_STALE`；`PAGE_NOT_FOUND` 列出全部缺失；
  - 删页后读回不符时返回 `rolled_back`。
- **Large**（示例项目的 scratch 副本）：审批不用 elicitation（ADR-0011），也不能让 `eplanmcp` 自动批准。按 M4 设计第 7 节，测试在自己的进程中组装 Host 的写入流水线（同 Medium 测试的 `WriteToolHarness`），连接真实的 Bridge，审批渠道换成测试程序集中的 `ScriptedApprovalChannel`：只批准测试预先登记的工具和变更清单，其余一律拒绝。这个类型只在 `tests\EplanMcp.Large.Tests` 中，`eplanmcp.exe` 没有任何开关、配置或环境变量能启用它，Host 只对两个测试程序集开放内部类型。
  - 删除 `=GAA+A1&EFS1/1`：结果与 S12 相同（四根电缆的芯线留下）；`backup` 能在 EPLAN 中打开，页数与删除前相同；
  - 一次删除 3 页；删除一张报表页；
  - 再删一次同一页：`PAGE_NOT_FOUND`。
- **Agent 评测**："删除 scratch 项目中的 `=GAA+A1&EFS1/60` 页，然后更新报表"。评测经真实的 `eplanmcp` 运行，审批要由在场的工程师在 EPLAN 中点击；无人值守时怎样评测，由项目负责人决定（在场审批，或者只评到审批请求为止）。作答后用 `eplan_page_list` 核对。

# 工具规格：`eplan_part_sync`

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_part_sync` |
| 所属分组 / Profile | `part` / `full`（M4c 的新工具默认只在 `full` 中） |
| 安全级别 | 系统主数据（[ADR-0019](../adr/0019-master-data-writes.md)），同时写项目：每次审批，执行前备份主数据和项目 |
| MCP 注解 | `readOnlyHint` = false　`destructiveHint` = true　`idempotentHint` = false　`openWorldHint` = false |
| 状态 | Proposed（M4c 第四批；已实现，待实机验证） |

## 1. 这个工具解决什么任务
"把这个项目里用到、部件库里还没有的部件加进部件库"。代理把一个项目中的部件同步到 EPLAN 配置的**系统部件库**，对应界面上的"主数据 > 部件 > 同步"。

S24 r15 发现同步**也写项目**：被引用但没有存进项目的部件会被存入项目。所以本工具同时按写项目的规则处理：项目在 scratch 之外时同样需要审批（反正每次都要审批），执行前也备份项目（ADR-0013）。

只做"项目 → 部件库"一个方向。"部件库 → 项目"（用部件库的最新数据更新项目中的部件）只写项目，不在 M4c 中。

## 2. 输入参数
| 参数 | 类型 | 必填 | 默认值 | 说明（写给模型看的） |
|---|---|---|---|---|
| `project` | string | 否 | EPLAN 中选中的项目 | 项目名，或 `eplan_project_list` 返回的 `path` |
| `mode` | enum `append_new` \| `overwrite_existing` \| `overwrite_and_append` | 否 | `append_new` | `append_new`：只加部件库中没有的部件；`overwrite_existing`：用项目中的数据覆盖部件库中已有的部件（连同全部变体）；`overwrite_and_append`：两者都做 |

## 3. 输出
`structuredContent`：
- `project`、`database`：写入的项目和部件库；
- `added`、`overwritten`：部件号列表，各最多 50 个，另有数量；
- `partsBefore`、`partsAfter`：部件库的部件总数（按变体计数）；
- `projectPartsBefore`、`projectPartsAfter`：项目中存储的部件数，以及 `storedInProject`（新存入项目的部件号，最多 50 个）和数量；
- `missing`、`notStored`：读回时部件库中没找到的、项目中没存入的部件号；
- `backup`（主数据备份）、`projectBackup`（项目备份）、`undo`（只撤回项目一侧的撤销步骤）；
- `execution`、`dryRun`、`recovered`、`hint`。

没有 `auditId`：现有的工具都不返回审计编号（同 `eplan_part_import`，实现时改，2026-10-21）。

一行摘要：`Added 4 parts of the project to the parts database ($(MD_PARTS)\EPLAN_parts.alk); the project now also stores 48 parts it referenced. Both were backed up first (see 'backup' and 'projectBackup').`

## 4. 错误
同 `eplan_part_import` 第 4 节，没有文件相关的错误，另有：
- `PROJECT_NOT_OPEN`、`AMBIGUOUS_NAME`：同通用规则。
- `BACKUP_FAILED`：主数据或项目任何一个备份失败，都不执行。
- `WRITES_SUSPENDED`：项目或部件库因此前结果未知而暂停。

## 5. 实现要点
- **规划**（只读）：
  - 读项目中存储的部件（`project.Articles`）和项目引用的部件号（`ArticleReference`），去重（与 `eplan_part_list` 读法相同）；
  - 用 `Parts` 一次读出部件库的部件号和总数（同 `eplan_part_import`），按 `mode` 分类。只有项目中存储的部件有数据可写进部件库：存储了、库中没有的是 `added`（`append_new`、`overwrite_and_append`），存储了、库中已有的是 `overwritten`（两种覆盖模式）；
  - 被引用、项目中没有存储、库中有的部件列为 `storedInProject`（S24 r15 中它们是从库存入项目的）；库中也没有的，同步后仍然缺数据，不列入任何一类；
  - 变更清单：新增、覆盖各一条（同 `eplan_part_import` 的写法，以项目名代替文件名），存入项目一条（现有和将存入的数量）；三类都为空时没有变更，不审批、不执行；
  - 库在 SQL Server 上时规划就拒绝（`POLICY_DENIED`）；
  - 指纹：库的设置、项目中存储的部件数，存储的和只引用的部件号，以及其中库已有的部件号。
  - 与原规格的差别及原因：原规格把三个列表都算进 `added`、`overwritten`，但只引用、没有存储的部件在项目中没有数据，同步写不进库；`Parts` 一次给出部件号和 `partsBefore` 要的总数。
- **审批对话框**：库的类型和路径，"影响使用这个库的所有项目"；三类部件的数量和前 20 个部件号；"项目也会被修改：存入 N 个被引用的部件"；"撤不回，执行前会备份部件库和项目"。
- **备份**：两份都在执行之前：主数据由写流水线在审批之后、发出执行请求之前用 `masterdata.backup` 备份（同 `eplan_part_import`，所有写主数据的工具都一样）；项目由执行操作在指纹检查之后、事务之前用 `CopyProject` 备份（ADR-0013 的同一次主线程调用）。所以顺序是先主数据、再项目，与原规格相反，两份都在任何修改之前，效果相同。
- **执行**：`new Synchronize().Parts(project, StoreMode)`，用完释放：
  - 在 `LockingStep` 中执行，否则抛出 `NoLockingStepException`（S24 r14）：Bridge 调度器的每次调用都在 `LockingStep` 中；
  - 在写事务（`TransactionScope`：`SafetyPoint` + `UndoStep` + `QuietModeStep`）中执行。原规格还要求"按写项目的规则锁定对象"：现有的写适配器都不显式加锁，没有验证过的加锁 API（`Project.LockAllObjects` 在 BannedSymbols 中），所以没有做；r15 的系统消息"写存取前没有加锁该对象"是否仍然出现，留给 Large 测试记录；
  - `SafetyPoint` 只能撤回项目一侧，撤不回部件库。所以同步开始之后的任何失败（包括提交前的取消）：项目回滚，部件库可能已经改变，按 `unknown` 处理（同 `eplan_part_import`）；`metadata.backup` 是主数据备份，`metadata.projectBackup` 是项目备份。
- **已验证**（S24 r15，经批准在正在用的库上，之后已恢复）：`AppendNew` 0.45 秒；夹具的部件在部件库中都已有，部件库不变；项目中存储的部件从 293 变为 341。
- **读回**（提交之后，不在事务中）：部件库中 `added` 和 `overwritten` 的每个部件号都存在，`partsAfter` 等于 `partsBefore` 加上新增部件在项目中存储的变体数（部件总数按变体计数）；项目中 `storedInProject` 都已存储。不一致只报告（`missing`、`notStored`），不是错误；读不回时给出警告。
- **时限与进度**：执行 300 秒，每 5 秒一次心跳。
- **未验证**：
  - 部件库中缺少部件时 `append_new` 的效果（夹具没有遇到）；
  - 两种覆盖模式；
  - 写事务中执行时，项目一侧能否被 `SafetyPoint` 撤回。实现前在 scratch 项目上验证；部件库一侧验证时要经项目负责人批准。

## 6. 写操作专项
- **dry-run**：规划结果。
- **审计**：工具名、项目路径、库的路径和类型、`mode`、三类数量、两个备份路径、`execution`。
- **回滚**：项目按 `SafetyPoint`；部件库没有，按备份恢复（S24 结论 6）。

## 7. 测试
- Small：枚举、摘要、规划的分类规则。
- Medium：假 Bridge 上的审批、两个备份各自失败、执行失败时 `unknown` 与暂停、读回不一致。
- Large：写正在用的部件库，只在项目负责人每次批准后运行，做法同 `eplan_part_import` 第 7 节（`PartSyncTests`，同样要 `EPLANMCP_LARGE_MASTERDATA=1`）；项目用 scratch 中的写副本，同步会永久改变它（存入被引用的部件），所以只有新副本上的第一次运行会同步。系统消息 `S328014` 是否仍出现、Ctrl+Z 能否撤回项目一侧，在 EPLAN 中手工核对后补进 api-notes。
- Agent 评测："把这个项目里部件库还没有的部件加进部件库"。

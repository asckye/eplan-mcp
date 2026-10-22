# 工具规格：`eplan_part_import`

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_part_import` |
| 所属分组 / Profile | `part` / `full`（M4c 的新工具默认只在 `full` 中） |
| 安全级别 | 系统主数据（[ADR-0019](../adr/0019-master-data-writes.md)）：每次审批，执行前备份主数据 |
| MCP 注解 | `readOnlyHint` = false　`destructiveHint` = true　`idempotentHint` = false　`openWorldHint` = false |
| 状态 | Proposed（M4c 第四批） |

## 1. 这个工具解决什么任务
"把供应商发来的部件数据导入部件库""把同事导出的那几个部件加到我的部件库"。代理把一个部件 XML 文件导入 EPLAN 配置的**系统部件库**。部件库被所有项目共用，所以每次都要工程师审批。

与相近工具的区别：
- `eplan_part_export` 是反方向；
- `eplan_part_sync` 把项目中的部件加入部件库，不需要文件；
- 导入到项目的部件清单是第三批的 `eplan_import_list`。

## 2. 输入参数
| 参数 | 类型 | 必填 | 默认值 | 说明（写给模型看的） |
|---|---|---|---|---|
| `file` | string，1–260 个字符 | 是 | — | 导入目录（`<数据根目录>\imports\`）下的相对路径，如 `supplier\relays.xml`；只接受 `.xml`（EPLAN 部件管理的 XML，即 `eplan_part_export` 的 `xml` 格式） |
| `mode` | enum `append_new` \| `update_existing` \| `update_and_append` \| `replace_and_append` | 否 | `append_new` | `append_new`：只加部件库中没有的部件，已有的不动；`update_existing`：只更新已有的；`update_and_append`：两者都做；`replace_and_append`：已有的整条替换，再加新的 |
| `match_by` | enum `part_number` \| `unique_id` \| `unique_id_or_part_number` | 否 | `part_number` | 怎样判断文件中的部件"已经存在" |

- 导入目录和路径规则同第三批的导入工具（M4c 设计 Q4、SEC-31）：只接受导入目录下的相对路径，拒绝 `..`、绝对路径、链接和重解析点；文件不超过 50 MB。
- 不提供 EDZ：EDZ 的导入转换器没有验证。

## 3. 输出
`structuredContent`：
- `database`：写入的部件库（配置中的写法，如 `$(MD_PARTS)\EPLAN_parts.alk`）；
- `added`、`updated`、`skipped`：部件号列表，各最多 50 个，另有 `addedCount`、`updatedCount`、`skippedCount`；
- `partsBefore`、`partsAfter`：部件库的部件总数；
- `backup`：本次主数据备份的路径；
- `execution`、`auditId`。

一行摘要：`Imported 3 new parts into the parts database ($(MD_PARTS)\EPLAN_parts.alk); 2 already there were left as they were. Backup: …`

工具描述写明：写的是所有项目共用的部件库；每次都要工程师审批；导入撤不回，只能按备份恢复。

## 4. 错误
| 错误码 | 什么时候发生 | `execution` | 返回给模型的提示 |
|---|---|---|---|
| `INVALID_ARGUMENT` | 路径不合规、不是 `.xml`、文件超过 50 MB、文件不是部件管理的 XML，或其中没有部件 | `not_started` | 指出是哪一条 |
| `FILE_NOT_FOUND` | 导入目录下没有这个文件 | `not_started` | 提示把文件放进 `<数据根目录>\imports\` |
| `POLICY_DENIED` | `masterDataWrites` 为 `deny`，或部件库在 SQL Server 上（ADR-0019） | `not_started` | 说明是哪条策略，不提示绕过 |
| `APPROVAL_DENIED`、`APPROVAL_EXPIRED` | 同通用规则 | `not_started` | 同通用规则 |
| `BACKUP_FAILED` | 主数据备份失败、超时，或备份目录中没有恰好一个非空的 `.zw6` | `not_started` | 部件库未改变；提示检查磁盘空间或运行 `eplanmcp doctor` |
| `PLAN_STALE` | 审批之后，文件或部件库中相关部件的状态变了 | `not_started` | 重新调用 |
| `OUTCOME_UNKNOWN` | 导入已经开始后 EPLAN 报错、超时或连接中断 | `unknown` | 部件库可能已部分改变，**不要重复调用**；`metadata.backup` 给出备份，恢复步骤见入门指南 |

- 没有 `rolled_back`：导入撤不回（S24 r11），开始之后的任何失败都是 `unknown`。之后对这个部件库的写入暂停，直到工程师确认（ADR-0010 的暂停机制，以库的路径为键）。

## 5. 实现要点
- **流程**：规划 → 审批 → 备份 → 执行 → 读回，同 Destructive 写工具；备份换成 ADR-0019 的主数据备份。
- **规划**（Bridge，只读）：
  - 用 `XmlReader` 读文件，不处理 DTD：根元素 `partsmanagement`，每个 `part` 元素的属性 `P_ARTICLE_PARTNR`（部件号）；变体不是 `part` 的属性，而是 `part` 下的 `variant` 子元素，每个变体一个（api-notes A30）；同一部件号只算一次，变体数累加；
  - `OpenDatabase()` 打开配置的库，用 `Parts` 一次读出全部部件号和部件总数（按变体计数，S24：464 个 0.07 秒），分成"新增""已有"，按 `mode` 算出 `added`、`updated`、`skipped`，然后 `Close()`。`match_by` 不是 `part_number` 时，分类仍按部件号估计，实际结果以读回为准；
  - 变更清单：新增、更新各一条，带数量；部件号（每类最多 50 个）和跳过的部件在计划的 `details` 中。全部跳过时没有变更，不审批、不备份、不执行；
  - 库在 SQL Server 上时规划就拒绝（`POLICY_DENIED`），Host 的策略再查一次；
  - 指纹：文件的大小、修改时间和 SHA-256，库的设置，以及文件中"已有"的部件号。
  - 与原规格的差别及原因：变体的位置按 S24 r11 的导出文件改正；`Parts` 一次给出部件号和 `partsBefore` 需要的总数，不用逐个 `ExistsPart`；全部跳过时没有变更，是写流水线对空变更清单的通用处理。
- **审批对话框**：库的类型和路径，"影响使用这个库的所有项目"，文件名和大小，三类部件的数量和前 20 个部件号，"导入撤不回，执行前会备份部件库"。
- **备份**：`backup /TYPE:MASTERDATA /MDTYPE:ARTICLES`，`SOURCEPATH`、`FILENAME` 由 `SelectedPartsDatabaseAsString` 拆出，目标为 Host 新建的空目录 `<数据根目录>\backups\masterdata\<UTC 时间>\`（S24：0.14 秒，`.zw6`）。
- **执行**：`OpenDatabase()` 后 `ImportParts(文件, "XPamImportXml", 模式, false, 识别方式, "")`，然后 `Close()`：
  - **不开事务**：在 `MDPartsDatabase` 的事务中导入会留下未关闭的撤销日志，之后的导入全部失败，事务也撤不回导入（S24 r11）；不在事务中正常，0.2 秒（S24 r13）；
  - 在 `QuietModeStep(ShowNoDialogs)` 中执行：导入走的是界面的导入 action，r11 中它试图弹出系统错误对话框；
  - 最后一个参数（字段映射方案）传空串，即"界面上次使用的方案"。只对 `eplan_part_export` 导出的 XML 验证过（S24 r13）；别的来源的 XML 是否受这个方案影响，没有验证。
- **读回**：`added` 与 `updated` 中的每个部件号都存在，`partsAfter = partsBefore + addedCount`。不一致时 `execution` 为 `committed`，结果中给出 `mismatch`（多出或缺少的部件号），提示工程师在部件管理中核对。
- **时限与进度**：规划用写流水线通用的 30 秒调用时限（流水线没有按工具设规划时限）；执行 300 秒，每 5 秒一次心跳（`Importing parts (30 s)`）。
- **未验证**：
  - 除 `append_new` 外的三种模式（S24 只试了 `append_new`）；
  - 大文件的耗时；
  - SQL Server 上的库（一律拒绝，不需要验证）。

## 6. 写操作专项
- **dry-run**：规划结果就是将要发生的变化：三类部件号和数量，以及将要写的库。
- **审计**：工具名、库的路径和类型、文件名和 SHA-256、`mode`、`match_by`、三类数量、备份路径、`execution`。
- **回滚**：没有。恢复按 S24 结论 6：把备份还原到空目录，关闭 EPLAN，用还原出的文件替换库文件。备份路径写进结果和审计。

## 7. 测试
- Small：路径规则（同第三批的导入用例）、枚举、摘要；按 `mode` 把"新增 / 已有"分成三类的规则。
- Medium：假 Bridge 上的审批拒绝、备份失败、`masterDataWrites: deny`、SQL Server 库、执行中断（`unknown` 和暂停）、读回不一致。
- Large：**写正在用的部件库**（S24 结论 8：副本隔离不了导入），不放进常规运行。只在项目负责人每次批准后运行：
  - 运行前对库的全部文件计算 SHA-256，并另存一份文件副本；
  - 导入一个测试部件 `EPLANMCP.LARGE-TEST`：`added` 为它，`partsAfter` 加 1；再导一次 `skipped` 为它；
  - 运行后关闭 EPLAN，用文件副本恢复，核对全部文件与之前相同。
- Agent 评测："把 imports 目录里的 relays.xml 导入部件库，已有的部件不要动"。

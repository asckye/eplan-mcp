# 工具规格：`eplan_project_create`

| 项 | 内容 |
|---|---|
| 工具名 / title | `eplan_project_create` / `Create project` |
| 所属分组 / Profile | `project`；M6 工具集 `workflow` / `full`，不进 `core` |
| 安全级别 | Write（scratch 中自动；之外逐次审批，仍只建新项目） |
| MCP 注解 | `readOnlyHint` = false　`destructiveHint` = false　`idempotentHint` = false　`openWorldHint` = false |
| 状态 | Proposed（M6 第 2 阶段；Bridge 已有，MCP 工具未实现；待定问题见第 8 节） |

## 1. 这个工具解决什么任务
“从基本项目新建一个目标项目”“把示例项目备份还原成 scratch 副本”。这是 [M6 设计](../design/2026-10-21-m6-engineering-packs.md) 4.16“一个完整任务”的起点，也覆盖 ADR-0005 预留的 `eplan_scratch_create` 用途（M6 Q2）。EPJ/PXF 文件继续用 `eplan_import_pxf`。

本版的 `basic_project`、`backup` 两种模式均可执行，不以归档版本预检为前提。只写入 Host 为本次调用新建的空目录。🔴 `restore /TYPE:PROJECT` 会无提示地覆盖已有项目（[api-notes A31](../eplan/api-notes.md)），**任何已有目标都拒绝，包括已有空目录**；工具调用之后新项目处于关闭状态（A34 最新补充，见第 5 节）。

## 2. 输入参数

| 参数 | 类型 | 必填 | 默认值 | 说明（写给模型看的） |
|---|---|---|---|---|
| `mode` | enum `basic_project` / `backup` | 是 | — | `basic_project` 从 `.zw9` 新建；`backup` 从 `.zw1` 还原为新项目 |
| `file` | string，1–260 个字符 | 是 | — | 固定导入根目录中的相对路径，如 `basics/GB_bas001.zw9`、`backups/sample.zw1` |
| `name` | string，1–60 个字符 | 是 | — | 新项目名，不带扩展名、不含 `.`；规则同 `eplan_import_pxf`，拒绝路径、控制字符、Windows 保留名、首尾空格 |
| `destination` | string | 否 | `<第一个 scratch 根目录>\projects\<name>` | 要新建的目录的完整本地路径；父目录须存在，默认的 `projects` 容器由 Host 管理。scratch 外仍逐次审批；不能指向已有目录 |
| `dry_run` | boolean | 否 | false | 只校验和返回新项目位置、源哈希、变更清单，不创建目录、不请求审批 |

- schema 为对象，`additionalProperties: false`；`mode` 写入 schema 的 `enum`。没有 `project` 参数：`name` 和目标路径显式指定新项目，不使用当前项目（同 `eplan_import_pxf`）。
- `file` 按 [SEC-31](../standards/security.md) 用 Host 的 `ImportFiles` 解析：根目录固定为 `<数据根目录>\imports\`；拒绝绝对路径、UNC、盘符、`.`、`..`、保留设备名和根目录以下的符号链接/联接点；根目录本身可以是链接。每个文件上限 2 GB，扩展名须与模式一致（不区分大小写）。Host 算出 SHA-256，不能让客户端提供它。
- 目标规范化后按 SEC-05 判断 scratch；拒绝 UNC、设备路径、`.`/`..`、下级链接/联接点。已存在的文件、目录、链接一律 `ALREADY_EXISTS`，不提供 `overwrite`、合并或复用选项。Host 新建的空目录可交给 Bridge，不能把工程师原有的空目录当成“新建”。
- **pack 资源参数留给后续 PR**：当前 `Host/Packs/PackCommand.cs` 只暴露 `pack scan|check|export|install` CLI，`PackStore` 管理版本，尚无供 MCP 调用解析资源文件的入口；本版不接受 pack URI 或任意包目录路径。以后须按 [ADR-0024](../adr/0024-pack-trust-and-container.md) 核对包版本、资源种类、清单哈希和路径；`referenced` 资源只能来自配置的 `packSources`，不沿用 CLI 的任意路径权限。
- 不接受 `.elk` + `.edb` 项目副本：当前 `OperationNames` 和 `IProjectFactory` 没有此输入的复制操作（第 8 节 Q2）。

## 3. 输出

```json
{
  "execution": "committed",
  "dryRun": false,
  "mode": "basic_project",
  "file": "basics/GB_bas001.zw9",
  "sha256": "…",
  "project": { "name": "Line1", "path": "C:\\Users\\<user>\\EplanMcp\\scratch\\projects\\Line1\\Line1.elk", "opened": false },
  "pages": 1,
  "projectGuid": "GB_bas001@<unix-seconds>",
  "databaseVersion": "18.0",
  "seconds": 9,
  "hint": "The new project is closed."
}
```

- `outputSchema`：上述字段均声明；`execution` 为 ADR-0010 的四值枚举，`dryRun`、`project.opened` 为 boolean，`mode` 同输入枚举；`file`、路径和名称为 string，`sha256` 为 64 位小写十六进制；`pages` 为非负 integer 或 null，`projectGuid`、`databaseVersion`、`hint` 为 string 或 null，`seconds` 为非负数。示例中的哈希、时间是占位值。
- `basic_project` 的 `pages`、`projectGuid` 来自同次 Bridge 创建调用；`backup` 当前的 `NewProjectData` 两字段为 null，不能捏造页数或为填字段自动打开项目。两种模式成功时的 `databaseVersion` 均来自 Host 在创建/还原之后调用 `project.compatibility` 的结果。`dry_run` 返回 `execution: not_started`、目标路径、源哈希和 `changes[]`，这三个未读取的项目字段均为 null。
- `projectGuid` 是原样读出的 `PROJ_GUID`，形如“名称@Unix 秒数”，不是全局唯一 GUID；还原及文件副本会沿用原值（[A34](../eplan/api-notes.md)，S30）。对象编号也不能单独区分项目副本（A32、A34）。
- `seconds` 为 Host 本次处理用时（不含等待人工审批）；Bridge 的 `NewProjectData.Seconds` 只测 EPLAN 阶段，不冒充整个调用用时。结果给实际目标路径；错误不泄露本机路径（SEC-12）。名称等数据经过 ResultShaper。
- 返回 `structuredContent`、同内容 JSON 文本块和一行摘要：`Created project 'Line1' from a basic project; it is closed.`；还原时用 `Restored backup as new project 'Line1'; it is closed.`。结果很小，不分页，不设 `response_format`。

## 4. 错误

| 错误码 | 什么时候发生 | `execution` | 返回给模型的提示 |
|---|---|---|---|
| `INVALID_ARGUMENT` | 模式、扩展名、名称、路径、大小不合规 | `not_started` | `metadata.violations` 列出全部问题 |
| `INVALID_ARGUMENT` | 创建/还原后的兼容性不是 `compatible: true` / `IsCompatible`，且本次产物已全部清理 | `rolled_back` | 说明材料不兼容、新建产物已删除；提示使用兼容材料，不声称支持自动升级 |
| `FILE_NOT_FOUND` | imports 中找不到文件 | `not_started` | 提示把材料放进数据目录下的 imports；只回显相对源路径 |
| `ALREADY_EXISTS` | Host 接到调用时目标已存在（空目录也拒绝）；规划后或执行前目标出现别人的文件 | `not_started` | `Choose a new destination; existing folders and projects are never reused.` |
| `PLAN_STALE` | 源文件大小、修改时间、SHA-256 或目标前置条件变化 | `not_started` | 提示重新规划，不复用批准；Host 应在执行前重新核对源哈希 |
| `POLICY_DENIED` / `APPROVAL_DENIED` / `APPROVAL_EXPIRED` | scratch 外策略禁止、工程师拒绝、审批过期 | `not_started` | 同标准错误码表；不提示绕过策略 |
| `AUDIT_UNAVAILABLE` / `WRITES_SUSPENDED` | 审计不可写或目标/实例写入暂停 | `not_started` | 同 ADR-0010；查询状态不能解除暂停 |
| `EPLAN_OPERATION_FAILED` | 创建/还原失败、未产生 `.elk` / `.edb`，或随后的 `project.compatibility` 检查失败，且本次产物已全部清理 | `rolled_back` | 附 EPLAN 消息（如有），说明新建产物已删除；不声称用了事务回滚 |
| `TIMEOUT` | 只在规划超时，或已确认执行请求未送达 | `not_started` | 执行发出后改走 ADR-0010 的结果查询 |
| `OUTCOME_UNKNOWN` | 执行中断后取不回结果，或新建产物无法全部清理 | `unknown` | `Do not repeat this call. Ask the engineer to inspect the destination and EPLAN.`；按目标路径及实例暂停写入 |
| 连接、授权类错误、`INTERNAL` | 同标准错误码表；创建/还原后的兼容性检查失败按上表处理 | 按阶段 | 执行前 `not_started`；失败清理完成 `rolled_back`；其余 Bridge 已提交后 Host 出错 `committed` |

先用已有错误码；不得新增“模板不兼容”“还原失败”等同义码。`retryable: true` 只允许 `not_started` 或 `rolled_back`；`unknown` 当且仅当 `OUTCOME_UNKNOWN`。被取消的 MCP 请求不回复，最终状态仍写审计（ADR-0010）。

## 5. 实现要点

- **已有合同**：[NewProjectArgs](../../src/EplanMcp.Contracts/NewProjectArgs.cs)、[NewProjectData](../../src/EplanMcp.Contracts/NewProjectData.cs)。`basic_project` 对应 `project.create.plan` / `.execute`；`backup` 对应 `project.restore.plan` / `.execute`，都经 Host 写入流水线的策略、审批、审计和结果恢复。不发明 `project.copy`、`project.open`、`project.close` 操作。
- **创建/还原后检查兼容性**：两种模式在 `project.create.execute` / `project.restore.execute` 返回后，由 Host 对本次新建的 `.elk` 调用 `project.compatibility`；[ProjectCompatibilityArgs](../../src/EplanMcp.Contracts/ProjectCompatibilityArgs.cs) 正好接受该路径。检查本身不打开项目（[A31](../eplan/api-notes.md)，0.02–0.3 秒）；仅当 `compatible: true` 且 `status: IsCompatible` 时工具才成功，两个模式都返回此处的 `databaseVersion`。restore 适配链已有的版本读取不能代替 Host 对完整兼容性结果的判断。不兼容或检查失败均按第 6 节清理，不以缺少归档预检阻止创建/还原。
- **未验证的跨版本行为**：从较旧或较新材料创建/还原时是否升级、弹对话框或失败均**未验证**（A31、S30）；现有 Large 基线只用 `GB_bas001.zw9` 和数据库版本 `18.0` 的示例备份，不能外推跨版本支持。不主动请求升级，不依据 `ProjectInfo.xml` 的版本文字宣称兼容（A35：10043/10088 不一定随写入更新）；7z 归档预检只作为 Q1 的后续可选改进。
- **目录**：默认在 `<scratch>\projects\<name>\` 中生成 `<name>.elk` 和 `<name>.edb`；Host 负责新建并记录目录归属，Bridge 计划、执行均检查为空，还原前在同次主线程调用中紧接着再检查（A31 风险）。MCP 对已有目录的拒绝比 Bridge“原来不存在或为空”的合同更严格；Host 建目录与指纹的衔接见 Q3。
- **EPLAN 调用**：[EplanProjectFactory](../../src/EplanMcp.Bridge.Eplan/Adapters/EplanProjectFactory.cs) 在主线程调度器的 `LockingStep` 中，用 `QuietModeStep(ShowNoDialogs)` 调 `Eplan.EplApi.DataModel.ProjectManager.CreateProject(linkFile, basicProject)`；还原用 `Eplan.EplApi.ApplicationFramework.ActionManager.FindAction("restore").Execute(context)`，逐项加入 `TYPE=PROJECT`、`ARCHIVENAME`、`PROJECTNAME`，不拼命令行。创建行为见 A31、A34；restore 的进程内路径由现有 Large 测试覆盖，是否已实际运行不能由“测试已合入”推断，仍标为**未验证**。
- **关闭状态**：A34 的 2026-10-22 补充优先于 Bridge 设计 4.8 的早期观察：Bridge 创建之后项目仍打开。适配器先在创建用的 `ProjectManager` 生命周期内读页数和 `PROJ_GUID`，再用新的 manager 从 `OpenProjects` 按链接路径重新取得对象并 `Close()`（S30 r3）；不对旧对象 Close，不再重复 Open。成功响应须确认项目未留在打开列表中。还原路径不主动打开项目（A31；进程内路径以上述 Large 验证为准）。
- **时限与进度**：建议创建执行时限 120 秒、还原 180 秒，与已合入 Large 调用一致；这是 Host 预算，不是 EPLAN 保证。实测创建：S27 3.5 秒，S30 的 GB 基本项目 8.5–8.8 秒、IEC 空项目 3.0–3.3 秒；还原示例备份约 6 秒（A31、[S30](../../spikes/S30-identity-lifecycle/README.md)）。等待 Bridge 时按 `eplan_export_pdf` 每 5 秒发心跳，`progress` 为累计秒数、无 `total`，分步通知也保持单调；审批等待沿用 15 秒心跳。创建/还原的原生调用能否中途打断**未验证**；只承诺返回后的安全点处理取消和清理（现有 `NewProjectExecution`）。

## 6. 写操作专项

- **dry-run**：返回一条 `create/project/<name>` 变更，含模式、相对源路径、大小、SHA-256、规范化目标路径及“新项目，结束后关闭”；不建目标、不审批。说明兼容性将在实际创建/还原后检查，不宣称已验证源归档版本。
- **审批**：默认目标在 scratch，自动允许；显式目标在 scratch 外，`writesOutsideScratch: approve` 时每次由工程师经既有独立窗口审批，`deny` 时 `POLICY_DENIED`。批准之前不在外部创建目录；批准也不允许复用或覆盖任何已有目标。展示来源、哈希、目标、不会覆盖、无 EPLAN 撤销步骤。审批渠道、绑定及期限沿用 ADR-0005 的修订，不改变审批模型。
- **备份和计划**：单独新建没有要覆盖的项目，不因文件是 `.zw1` 就成为 Destructive，也不把它当作 ADR-0013 的“恢复原项目”入口。后续多步计划仍按 [ADR-0023](../adr/0023-plan-approval-and-backup.md) 先备份目标、逐步执行、一次计划审批；新建后如何打开供写入及如何安排计划前备份，见 Q4。本工具不返回 `partially_committed`。
- **回滚**：新建/还原不使用 `SafetyPoint` 或 `UndoStep`，不承诺 Ctrl+Z。失败、取消或随后兼容性不通过，确认 EPLAN 已停止后，按 [eplan_import_pxf 的“失败时”](eplan_import_pxf.md) 仅删除本次创建、规划时不存在的 `.elk`（以及 `.ell`）、`.edb` 及本次新建目录；链接/联接点只删链接本身、不跟随，不删原有或别人的文件。不兼容材料清理成功返回 `INVALID_ARGUMENT/rolled_back`；检查失败清理成功返回 `EPLAN_OPERATION_FAILED/rolled_back`，即使 Bridge 创建/还原单步已完成也不直接报告工具成功。清理不完全均为 `OUTCOME_UNKNOWN/unknown`。超时后可能仍在写，Host 不能先删目录；先用原请求 ID `outcome.get` 取回结果，绝不重发执行（[ADR-0010](../adr/0010-write-outcomes.md)）。
- **审计**：沿用哈希链的审批、`execution_started`、`execution_result` 和暂停记录；记录 mode、相对源路径、大小、修改时间、SHA-256、目标规范化路径、scratch 判定、创建/还原后的兼容性结果与清理结果、指纹、请求/关联 ID、页数、PROJ_GUID（可空）、关闭状态和耗时。pack 输入以后另记包 ID、锁定版本、资源路径及摘要；不记创建者信息、归档内容或授权数据。

## 7. 测试

- **Small**：两种模式及全部非法参数；目标默认值；已有空目录也拒绝；路径穿越、设备名、链接、大小上限；规范化后的 scratch 内外决策；schema、title、四个注解、输出及摘要；restore 的 nullable 字段；ADR-0010 两条不变式。
- **Medium**（假 Bridge + 临时文件）：dry-run 不创建目录；源哈希变化 `PLAN_STALE`；目标在规划后出现文件时 `ALREADY_EXISTS/not_started` 且文件逐字节不变；两种模式均先执行、后对新 `.elk` 查兼容性，成功均返回数据库版本；创建/还原后不兼容则清理并返回 `INVALID_ARGUMENT/rolled_back`，检查失败则清理并返回 `EPLAN_OPERATION_FAILED/rolled_back`，删不完为 `OUTCOME_UNKNOWN`，链接只删本身；Host 新建目录与指纹衔接；外部逐次审批/拒绝/过期/策略 deny；审计失败不执行；心跳、取消、断线后 `outcome.get` 成功及未知，均不重发。
- **已合入的 Large 测试是 Bridge 基线，尚非 MCP 工具验收**（本次只读代码，不运行）：

| 测试 | 精确预期 |
|---|---|
| [ProjectCreateTests](../../tests/EplanMcp.Large.Tests/ProjectCreateTests.cs) | 默认寻找唯一 `GB_bas001.zw9`，或用 `EPLANMCP_LARGE_BASIC_PROJECT` 指定；无材料跳过。复制到 `<scratch>\created\<时间-进程号>\imports\`，目标为其下 `project\LargeCreate.elk`。变更首项恰为 `create/project/LargeCreate`；页数 1，PROJ_GUID 以基本项目文件名（无扩展名）+ `@` 开头；`.elk`、`.edb` 存在；创建后不在打开列表；compatibility 为 `IsCompatible`、`18.0`；`openReadOnly` 快照恰 1 页、GUID 与创建一致，之后仍关闭；再次规划同目录为 `ALREADY_EXISTS`，递归目录项列表不变。指定其他基本项目仍必须满足该测试的 1 页断言 |
| [ProjectRestoreTests](../../tests/EplanMcp.Large.Tests/ProjectRestoreTests.cs) | `EPLANMCP_LARGE_ZW1` 必须指向非客户的示例备份，不设置或不存在则跳过。复制到 `<scratch>\restored\<时间-进程号>\imports\LargeRestore.zw1`，还原到 `a`、`b` 两个新目录：版本均 `18.0`，`.elk`、`.edb` 存在，两个项目均关闭；两次 `openReadOnly` 快照的哈希及 PROJ_GUID 相同。对 `a` 再规划为 `ALREADY_EXISTS`；`c` 规划后放入 `LargeRestore.elk` 哨兵，执行为 `ALREADY_EXISTS/not_started`，哨兵内容仍为 `engineer's work`，目录中恰有该文件。测试没有断言还原页数为 321，也没有做归档还原前预检 |

- **新增工具 Large 验收**：通过固定 imports 的 `GB_bas001.zw9` 和 `18.0` 示例备份重复上述场景，两个模式均核对 Host 在创建/还原后调用 `project.compatibility`，结果为 `compatible: true` / `IsCompatible`，成功响应的 `databaseVersion` 均为 `18.0`；补源文件前后 SHA-256 相同、成功后项目关闭、Host 拒绝已有空目录及审计。不兼容/检查失败及清理失败路径由 Medium 覆盖，不把此 Large 验收算作跨版本行为验证。A35 要求读取副本前核对 `User.eod`/`User.eox`；不能为去掉会话数据而删掉它们。缺文件的副本由校验拒绝，不诱发模态对话框。
- **Agent 评测**：“从 imports 的 IEC 基本项目在 scratch 新建目标项目，报告位置和关闭状态”。S30 的 `IEC_bas003_sample_project.zw9` 预期 0 页，不能套用 GB 的 1 页断言；后续模块写入、来源、差异、备份及 `PLAN_STALE` 的完整任务另按 M6 4.16/4.17 验收。

## 8. 待定问题

| # | 问题 | 建议 |
|---|---|---|
| Q1 | 是否增加 `.zw1` / `.zw9` 的归档版本预检？ | 可选的后续改进，不是任何模式的执行门槛。当前扫描器只识别 7z 文件头并报 `archive_not_inspected`（`EplanFileInspector`）；以后可评估受限 7z 读取并补跨版本样例验证。当前两种模式照常在本次新建空目录中执行，再检查新 `.elk` 的兼容性 |
| Q2 | 项目副本模式、pack 资源输入何时接入？ | Bridge 目前仅有 `.zw9`/`.zw1`，pack 的资源解析参数也留后续 PR；将来复制必须保留 User.eod/eox（A35），先检查兼容性，不打开原件 |
| Q3 | Host 新建目录如何与当前 Bridge 指纹一致？ | `NewProjectRequest.StateFingerprint` 区分 `absent`/`empty`，plan 后 mkdir 会导致 `PLAN_STALE`。实现 PR 须明确 Host 建目录、指纹重核对和审批绑定的衔接，不在未审批时创建外部目录，也不静默替换获批指纹 |
| Q4 | 新项目如何接入后续写计划、backup 模式是否补读页数/GUID？ | 新建返回关闭，当前无通用 `project.open`/`project.close`（Bridge 设计 Q2）；另写规格。新建步骤与 ADR-0023“第一步之前备份”的衔接另行细化，不假设备份不存在的项目，也不免除对已有目标的计划备份；读回元数据时仍须最终关闭 |

进程拓扑、IPC、审批模型和支持版本的改动属于 L3，只能另提 Proposed ADR；本规格不据此扩展权限。

# 设计文档：M6 工程包（engineering packs）

| 项 | 内容 |
|---|---|
| 状态 | Approved（2026-10-21，项目负责人：ADR-0023 到 0026"四个都接受"；第 10 节的 Q1–Q12"全部按建议"，其中 Q11 为"不再单独审批"） |
| 级别 | L2（总体设计）。其中改变审批模型、包的信任边界、Code 级和 IPC 的四项写成 ADR（2026-10-21 接受）：[0023](../adr/0023-plan-approval-and-backup.md) 计划级审批与备份、[0024](../adr/0024-pack-trust-and-container.md) 工程包的信任与容器安全、[0025](../adr/0025-code-plugin-worker.md) 代码插件、[0026](../adr/0026-eplan-events-and-push.md) EPLAN 事件订阅与 Bridge→Host 推送 |
| 作者 | asckye |
| 关联 Issue | — |
| 相关 ADR | 0005、0006、0010、0011、0012、0013、0019、0021、0022、0023–0026（本设计提出，2026-10-21 接受） |
| 最后更新 | 2026-10-21 |

## 1. 背景

### 1.1 项目负责人的要求（2026-10-21，按调整后的范围批准为 M6）
工程师交给 AI 的材料：一个基本项目（`.zw9`）、一个或多个完成的参考项目（`.zw1` 备份，或 `.elk` + `.edb` 目录）、宏、公司的物料对应表、说明和规则，以及要生成或修改什么。AI 调用 EplanMcp 分析、整理这些材料；EplanMcp 读取真实数据、校验，生成资源索引和**工程包**（清单自动生成，普通用户从不手写 YAML、JSON、规则或插件）；AI 再查询、规划、调用；EplanMcp 执行工程操作并校验结果。

| 能力 | 要点 |
|---|---|
| 资源库 | 导入、分类、搜索、详情、页面预览、来源位置 |
| 电气关系图 | 功能、连接点、连接、端子、电缆、PLC、设备标识、页；路径追踪、跨页、影响分析；区分事实与推断 |
| 规则包 | 适用范围、输入、检查逻辑、来源、版本、结果、可选的修复 |
| 参数化电路模块 | 来自宏和页模板；能从完成的项目中提取（例如 3 个回路扩到 8 个） |
| 声明式工程计划 | 编译为有序操作，每步有读取和修改范围、前置条件、预期结果 |
| 三层校验与差异审查 | 对象数据、电气关系、图纸；计划与实际的差异 |
| 长任务、按需上下文 | 可恢复；上下文按依赖缓存和失效 |
| 其他 | 验收样例库；能力和版本检测；只返回计划的可选算法插件 |

- **配置分层**：通用基础 → 公司 → 产品系列或柜型 → 项目 → 单个柜。一个项目可以有多个柜，每个柜绑定自己的配置，并映射到项目结构；区分默认值和硬约束；每个生效的值都能追溯到来源；工具显式带项目、柜和配置的标识。
- **版本**：开发时是普通目录，导入导出用 `.eplanpack`（ZIP）；EPLAN 原生文件保持原格式；项目锁定包的版本；迁移时给出差异并重新校验。
- **事实、推断、规则分开**：从参考项目提取的事实、AI 推断的候选、公司正式规则分别记录；强制规则必须有来源；缺口要标出来，绝不悄悄变成强制规则。
- 负责人建议的工具家族：`eplan_pack_*`、`eplan_profile_*`、`eplan_asset_*`、`eplan_context_get`、`eplan_workflow_*`、`eplan_plan_apply`、任务状态工具；工具之外提供 MCP Resources；订阅、通知、MCP Tasks 只在客户端支持时使用。

### 1.2 协调人的调整（项目负责人已批准）
1. 参数化模块建立在 EPLAN 自己的机制上：宏 + 占位符对象 + 值集（`Eplan.EplApi.DataModel.Graphics.PlaceHolder`、`PlaceHolder.ValueSetsClass`、`PlaceHolderText`、`IPlaceHolder`）；本项目的元数据只补充接口、部件映射、适用条件和验收。
2. 检查 EPLAN Cogineer 授权（`LicenseOptions` 有 `CogineerDesigner`、`CogineerBuilder`、`*Advanced`、`CogineerSilentMode`）：有授权时评估集成，不重复造；没有时本项目做一个边界写清楚的轻量等价物。
3. 不用 microsoft/RulesEngine（规则是运行时编译的 C# lambda 表达式，等于让包携带可执行代码）；用自己的受限规则格式：只用目录中登记的属性和关系，固定的比较和组合运算符。
4. 稳定的对象定位符（第 4.6 节按 S28 的实测修订）。
5. 参考项目一律先复制到 scratch 再打开（打开或还原旧版本项目会不可逆地升级它），记下源文件的 EPLAN 版本。
6. 一份规范的项目快照（规范化的对象、属性、连接、页），差异、校验和缓存共用。
7. 客户数据绝不进仓库（CLAUDE.md 第 4 条）；测试包只从 EPLAN 示例项目派生。
8. 工程师修正事实、推断、规则的界面，以后放在 Workbench 中。
9. 协调人在 EPLAN 上做的 spike 从 S27 开始编号（S26 已被 ADR-0021 预留）；S27、S28（r0、r1）、S29 已经完成（api-notes A31–A33），其余为 S30–S33，见 4.18。

### 1.3 现状（以 main `87ea297` 的代码为准）
"已实现"指代码中有、测试覆盖；"部分"指有基础但不够 M6 用；"仅设计"指只写在文档里；"缺失"指没有。

| # | 能力 | 状态 | 依据 | 对 M6 的意义 |
|---|---|---|---|---|
| 1 | 写入流水线 | 已实现 | `src/EplanMcp.Host/Safety/WritePipeline.cs`：规划 → 策略 → 写入暂停 → 审批 → 备份（Destructive）→ 审计 `execution_started` → 执行 → 审计 `execution_result` | 一次工具调用只有一对 `*.plan`/`*.execute` 操作、一个 Bridge 事务；计划的每一步复用这些段，计划级的部分要新建（ADR-0023） |
| 2 | 变更清单与指纹 | 已实现（单步） | `src/EplanMcp.Contracts/WritePlan.cs`（`Project`、`ProjectPath`、`Changes`、`Fingerprint`、`Details`）、`PlannedChange.cs`；`src/EplanMcp.Bridge.Core/Operations/Fingerprint.cs`（排序后的行做 SHA-256）；每个工具只覆盖它触及的对象，行中多用 `ToStringIdentifier()`；`PLAN_STALE` 由各 `*ExecuteOperation.cs` 返回 | 运行时字符串标识含按打开分配的数据库号（S28），**不能用于跨调用保存的计划**；`dry_run` 不返回计划令牌 |
| 3 | Destructive 备份 | 已实现 | `Safety/BackupFolders.cs`、`Bridge.Core/Operations/ProjectBackups.cs`、`Bridge.Eplan/Adapters/EplanProjectBackup.cs`（`CopyProject(…, Snapshot)`，核对页数） | 计划开始前的备份直接复用 |
| 4 | 结果恢复与写入暂停 | 已实现 | `Bridge.Core/Operations/OutcomeRecords.cs`（内存，256 条）、`Host/Bridge/BridgeClient.Writes.cs`（`outcome.get` 轮询）、`Safety/DanglingWrites.cs`、`Safety/SuspensionStore.cs` | 逐步沿用；多步计划还需要计划级的结果（ADR-0023） |
| 5 | 审计 | 已实现 | `src/EplanMcp.Host/Audit/AuditLog.cs`：`approval`、`suspension_release`、`execution_started`、`execution_result`、`writes_suspended`，哈希链；读和导出不审计 | 增加计划、工作区和包的记录种类 |
| 6 | 审批 | 已实现 | `Safety/ApprovalService.cs`、`WorkbenchChannel.cs`、`EplanDialogChannel.cs`；决定绑定请求 ID、指纹、参数摘要，只用一次 | 计划级审批绑定计划哈希（ADR-0023） |
| 7 | 策略与配置 | 已实现 | `Safety/PolicyEngine.cs`（Code 级恒为 Deny）、`ScratchPaths.cs`、`HostConfig.cs`（`scratchRoots`、`writesOutsideScratch`、`approvalTimeoutSeconds`，未知成员拒绝启动） | 插件要用 Code 级（ADR-0025）；新的安全配置只能写在配置文件中（SEC-02） |
| 8 | 工具面 | 已实现 | 51 个工具（`tests/Contracts.Snapshots/server-contract.verified.json`），在 `src/EplanMcp.Host/McpServerSetup.cs` 注册；`Tools/ToolProfiles.cs`：`core` 25 个，`full` 全部 | 没有新建项目和还原 `.zw1` 的工具；M6 的工具只进 `full` 和新工具集（4.5，ADR-0022） |
| 9 | MCP 的其他能力 | 缺失 | 没有 Resources、资源模板、Prompts、订阅、Tasks、`listChanged`；只有进度通知和取消 | 新增 Resources；Tasks 按客户端支持（4.13） |
| 10 | 握手与能力 | 部分 | `Contracts/HandshakeResult.cs`（`ProtocolVersion`、`BridgeVersion`、`Eplan`、`Operations`、`BuildTarget`、`AddinPath`）、`EplanInstanceInfo.cs`（`ProcessId`、`Version`、`Variant`、`ApiExtensionLicence`、`Offline`）；`Operations` 就是能力清单；`BridgeProtocol.CurrentVersion = 1` | 缺授权选项（Cogineer、Pro Panel、Preplanning）和能力目录的版本（4.4） |
| 11 | Bridge→Host 推送 | 部分 | `Bridge.Core/Server/BridgeConnection.cs` 只在调用期间发 `progress`；`EplanAddIn` 只实现 `IEplAddIn`、`IEplAddInShadowCopy`，没有订阅任何 EPLAN 事件 | 缓存失效需要事件（ADR-0026） |
| 12 | 对象标识 | 部分 | 一律按名称：`Bridge.Core/Operations/ProjectResolver.cs`、页名、`DeviceTags.cs`（歧义时 `AMBIGUOUS_NAME`）；没有句柄和 `HANDLE_EXPIRED`（架构 8.1 只是设计）；分页游标 `Tools/PageCursor.cs` 只记偏移 | 需要跨调用的定位符（4.6） |
| 13 | 项目与实例的身份 | 部分 | 项目只按规范化路径（`ScratchPaths.Canonical`）；实例按管道名 + 进程号（`Host/Bridge/BridgeEndpoint.cs`）和进程号 + 启动时间（`SuspensionStore.StartOf`） | 缓存键还缺打开代次（4.7） |
| 14 | 缓存与快照 | 缺失 | `Bridge.Eplan/Adapters/EplanProjectReader.cs` 明确不缓存；没有快照、代次、观察计数 | 4.6、4.7 |
| 15 | 宏 | 部分 | `macro.list` 只扫描 `.ema`（`Bridge.Core/Operations/MacroFiles.cs`）；`macro.get`（`Adapters/EplanMacroReader.cs`）返回表达类型、变体、设备；`macro.insert` 只接受 `$(MD_MACROS)` 下的窗口宏（M3b 设计 Q3） | 产品代码中没有页宏、占位符、值集；S29 已验证占位符和值集的读写（A33），见 4.10 |
| 16 | 新建项目与还原 | 仅设计 | 产品代码中没有 `ProjectManager.CreateProject` 和项目还原；`eplan_scratch_create` 只出现在 ADR-0005 第 6 节和 ADR-0021 第 8 节；最接近的是 `eplan_import_pxf` 在 `<scratch>\imported\` 中新建项目（`Adapters/EplanProjectImporter.cs`、`Tools/ImportTools.cs`）。S27 已验证新建、还原和不打开项目判断版本（A31） | 4.5 的 `eplan_project_create`；剩余问题在 S30 |
| 17 | 只读数据与导出 | 已实现 | 页、设备、连接、电缆、端子排、PLC、部件、消息、检查结果的读工具；导出 PDF/DXF/图片（`Host/Exports/ExportRunner.cs`，ADR-0012） | 快照提取和页面预览的基础；按工具逐个读取不适合整项目提取（4.6） |
| 18 | 长任务 | 部分 | 进度与取消（`Safety/CallProgress.cs`、`BridgeProgress.cs`）；跨 Host 重启只留下审计和暂停记录；没有 MCP Tasks（M4 设计 Q6 推迟） | 4.13 |
| 19 | 离线宿主 | 已实现 | `src/EplanMcp.Bridge.Offline`：打开一个项目，经它只能写 scratch（Host 的审批渠道不可用） | 以后可以在离线宿主中提取参考项目，不占工程师的 EPLAN |
| 20 | 能力目录与覆盖 | 已实现 | `catalog/eplan-2026.0.3/`：Action 目录 `actions.json`（`A:<action>[/TYPE]`）和 SDK 成员目录（XML 文档注释 ID，生成器 `eng/catalog`），口径见[覆盖设计](2026-10-21-api-capability-coverage.md)；实现映射 `catalog/mapping/`、覆盖状态 `catalog/coverage.json`（`eng/coverage` 生成，CI 检查），报告见[覆盖报告](../eplan/coverage-report.md) | 能力声明引用同一套 ID，按版本的状态从覆盖数据读取（4.4） |
| 21 | 工具集与发现 | 仅设计 | [ADR-0022](../adr/0022-domain-toolsets.md)（Accepted，未实现）；代码中只有 `core`、`full` | M6 工具分到两个新工具集（4.5） |
| 22 | 受控 action 运行器 | 仅设计 | ADR-0021（Accepted，未实现） | 计划的步骤不经运行器（4.8） |
| 23 | 测试与评测 | 已实现 | Small / Medium / Large 三层（`tests/`，CI 不跑 Large）；测试项目只来自 EPLAN 示例项目（`eng/scripts/eval-fixture.ps1`）；评测 `evals/tasks/`（34 个）、`evals/tasks-attended/`（6 个） | 测试包同样只从示例项目派生 |
| 24 | 工程包清单 | 进行中 | PR #393–#396（未合并）新建 `src/EplanMcp.Packs`：SemVer 与 EPLAN 版本范围、`pack.json` 模型 v0、校验、JSON Schema | 4.1 以这份 v0 为基础 |
| 25 | 配置分层、资源索引、关系图、规则、模块、工作流、组合计划、三层校验、差异、插件 | 缺失 | `src/` 中没有 asset、rule、diff、workflow 一类的类型 | 本设计的其余内容 |

**已经实测的事实**（协调人 2026-10-21）：
- **新建与还原**（S27，A31）：`CreateProject(链接文件, .zw9)` 3.5 秒，新项目建好就以独占方式打开；`restore /TYPE:PROJECT` 还原到已存在的项目时**不报错、照样覆盖**；`GetProjectDataCompatibility` / `GetProjectDatabaseVersion` 不打开项目就能判断版本（0.02–0.3 秒）；`.zw9`、`.zw1` 是 7z 归档；`.edb\ProjectInfo.xml` 的属性 `10043` 是最新更改或更新的 EPLAN 版本。旧版本项目的升级行为没有测（本机只有 2026.0.3）。
- **对象标识**（S28 r0、r1，A32）：同一示例项目的两份文件副本中，825 个对象（全部主功能和页）的 `StorableObject.ObjectIdentifier` 全部相同；用 `backup` 备份再 `restore /TYPE:PROJECT` 还原为新项目、用 `eplan_export_pxf` 导出再 `eplan_import_pxf` 导入为新项目之后，825 个也全部相同。不同的只有 `DatabaseIdentifier`（按打开的项目分配，文档说重新打开就变）。`ToStringIdentifier()` 的格式是 `数据库号/类型号/对象号/0`。文档说 `ObjectIdentifier` 在同类对象中唯一，对象删除后再新建时编号可能被重用。
- **占位符与值集**（S29，A33）：不插入宏就能读出占位符、值集、变量和值，扫描 695 个示例宏 21 秒，72 个带占位符、38 个带值集；`ApplyRecord`、`AddRecord` + `SetValue` 都能用 API 完成，并能被 `SafetyPoint` 回滚；值是多语言文本。没有验证提交后的撤销、新建占位符对象、`WindowMacro.Create`。
- **提取耗时**（S28，A32）：整个示例项目（321 页、2,876 个功能、2,060 个连接、44,735 个图形放置）枚举约 1 秒，读全部功能的名称、页、坐标 0.06 秒。示例规模的完整提取是秒级的。

## 2. 目标与非目标

**目标**
1. 工程师的材料经 `eplan_pack_import` 导入后，自动生成带哈希和来源的清单与资源索引；用户不手写 YAML、JSON、规则或插件。
2. AI 能按需查询资源、上下文和配置（每个生效的值可追溯），把意图写成声明式计划；计划编译为有序操作，一次审批后执行，返回三层校验和差异。
3. 一个项目多个柜，每个柜绑定自己的配置；工具显式带项目、柜和配置标识，柜与柜的上下文从不混用。
4. 工程包有版本，项目锁定版本；迁移给出差异并重新校验。
5. 事实、推断、正式规则分开记录；强制规则必须有来源；缺口标出来。
6. 4.17 的必测场景全部有自动化测试或实机测试。
7. 复用现有的安全机制，改变安全模型和 IPC 的地方写成 ADR（0023–0026，2026-10-21 接受）。默认值都不变；唯一的放宽（允许列表中的插件每次运行不再单独审批）由项目负责人决定，记在 ADR-0025。

**非目标**
- 重做 EPLAN 的编辑界面或 Cogineer 的配置器界面。
- 让工程包携带可执行内容（ADR-0024）；规则不用表达式编译。
- 在仓库中保存客户数据；测试包只从示例项目派生。
- 多用户的包服务器：只有本地目录和 `.eplanpack` 文件交换。
- 对电气设计的正确性负责（架构 1.2）：规则检查和校验是给工程师的辅助。
- 在真实项目上做试验：参考项目一律先复制到 scratch；目标项目默认建在 scratch 中。
- 预规划（没有授权）、Pro Panel 3D 模块（在 M4b 之后）只在第 5 阶段视授权而定。

## 3. 方案概述
Host 中新增一组组件，管理工程包、配置、快照、上下文、计划、校验和任务；Bridge 只增加读取快照、解析定位符、新建与还原项目、插入模块等"怎么做"的操作，和以后的事件推送；安全判断仍全部在 Host 中。计划的每一步仍是现有的一对 `*.plan`/`*.execute` 操作，外面包一层计划流水线：一次审批、计划开始前一次备份、逐步执行、计划级结果（ADR-0023）。

```mermaid
flowchart LR
    client["MCP 客户端"]
    subgraph host["Host（.NET 10）"]
        tools["工具与 Resources<br/>pack / asset / cabinet / context / workflow / plan / task"]
        store["包仓库<br/>开发目录 · 已安装版本 · .eplanpack"]
        cfg["配置解析<br/>分层 · 柜绑定 · 锁"]
        snap["规范快照<br/>定位符 · 内容哈希"]
        ctx["上下文与缓存<br/>依赖 · 失效"]
        graph["关系图 · 规则求值"]
        plan["工作流编译 · 计划仓库"]
        pipe["计划流水线（ADR-0023）<br/>复用 WritePipeline 的各段"]
        verify["三层校验 · 差异"]
        tasks["任务仓库"]
    end
    subgraph bridge["Bridge（EPLAN 进程内）"]
        ops["快照提取 · 定位符解析<br/>新建/还原项目 · 模块插入<br/>现有 *.plan / *.execute"]
        ev["事件订阅（ADR-0026）"]
    end
    wb["Workbench<br/>导入 · 修正 · 计划审批与差异"]
    worker["插件工作进程（ADR-0025）<br/>数据进 · 计划出"]
    client -- stdio --> tools
    tools --> store & cfg & ctx & plan & tasks
    ctx --> snap --> ops
    plan --> pipe --> ops
    pipe --> verify --> snap
    graph --> snap
    ev -. "失效通知" .-> ctx
    pipe -- "审批" --> wb
    plan -- "可选" --> worker
```

### 3.1 组件放在哪里

| 组件 | 位置 | 职责 | 复用 |
|---|---|---|---|
| 包仓库 | Host | 开发目录的读写、构建版本、安装与导出 `.eplanpack`、校验（ADR-0024） | 数据根目录、导出目录（ADR-0012） |
| 资源索引 | Host | 分类、搜索、详情、预览、来源 | 导出图片作为页面预览 |
| 配置解析 | Host | 分层合并、硬约束、溯源、柜绑定、锁 | — |
| 规范快照 | Bridge 提取，Host 组装和存放 | 规范化的对象、属性、连接、页；定位符；内容哈希 | 读操作的适配层 |
| 上下文与缓存 | Host | 按需上下文、依赖登记、失效、写前重新核对 | 握手中的实例身份 |
| 关系图与规则 | Host | 在快照上建图、追踪、影响分析、规则求值 | — |
| 工作流编译与计划仓库 | Host | 声明式计划 → 有序步骤；持久化 | `WritePlan`、各 `*.plan` 操作 |
| 计划流水线 | Host | 计划级审批、备份、逐步执行、结果、暂停 | `WritePipeline` 的各段、`ApprovalService`、`BackupFolders`、`outcome.get`、`SuspensionStore`、`AuditLog` |
| 校验与差异 | Host | 三层校验；基准、计划、实际的三方差异 | 导出图片（图纸层） |
| 任务仓库 | Host | 长任务的状态、检查点、恢复 | 审计中的悬空写入处理（`DanglingWrites`） |
| 新的 Bridge 操作 | Bridge | `project.snapshot`、`object.resolve`、`project.create`、`project.restore`、`module.insert`、`macro.placeholders` | `TransactionRunner`、`OutcomeRecords`、`ProjectBackups` |
| 事件推送 | Bridge → Host | 页、连接、项目打开关闭等事件的失效通知（ADR-0026） | 管道连接 |
| 导入与修正界面、计划审批 | Workbench | 导入向导；事实、推断、规则的修正；计划的差异预览与审批 | 审批管道（ADR-0014） |
| 插件工作进程 | 单独的进程 | 算法插件：纯数据进，计划草稿出（ADR-0025） | — |

### 3.2 一次典型任务
1. 工程师把基本项目、参考项目和公司资料放进 `<数据根目录>\imports\`；AI 调 `eplan_pack_import`。Host 校验文件，把参考项目复制或还原到 scratch，记下源文件的 EPLAN 版本，提取快照和事实，生成开发包的清单和索引。
2. AI 用 `eplan_asset_search`、`eplan_asset_get` 查资源，用 `eplan_asset_update` 写候选和模块元数据，用 `eplan_pack_build` 冻结成版本；工程师把项目或柜绑定到配置（`eplan_cabinet_bind`，锁定版本）。
3. AI 用 `eplan_context_get` 取上下文，用 `eplan_workflow_plan` 编译计划，得到计划 ID、步骤、读写范围、前置条件、预期结果和差异预览。
4. `eplan_plan_apply`：重新核对 → 一次审批 → 备份 → 逐步执行 → 三层校验 → 差异；耗时长时返回任务 ID。
5. AI 用 `eplan_plan_get` 把校验结果和差异交给工程师审查。

### 3.3 数据目录
在 [ADR-0008](../adr/0008-data-locations-and-instance-discovery.md) 的数据根目录下新增：

```
%USERPROFILE%\EplanMcp\
  imports\                    工程师放入的材料（.zw1、.zw9、.elk + .edb、宏、资料、.eplanpack），只读使用
  packs\<包 ID>\<版本>\        已安装的工程包，不可变，使用前按清单哈希核对
  packs-dev\<包 ID>\           开发中的工程包，普通目录，只经工具写入
  workspaces\<项目键>\         项目绑定：柜、配置值、锁定的包版本（binding.json）
  cache\snapshots\  cache\context\     规范快照（按内容哈希）与上下文缓存，可以随时删除
  state\plans\  state\tasks\          计划与任务（与已有的 state\suspended\ 并列）
  plugins\<ID>\<版本>\         代码插件（ADR-0025，默认关闭）
  scratch\references\  scratch\projects\   参考项目的副本；目标项目（默认位置）
```

### 3.4 复用现有机制

| 现有机制 | 在 M6 中的用法 |
|---|---|
| `WritePipeline` 的各段 | 计划的每一步；计划流水线在外面增加计划级审批、备份和结果（ADR-0023） |
| `WritePlan` / `PlannedChange` | 每步的变更清单；计划的变更清单是各步的拼接，带步骤号 |
| 指纹与 `PLAN_STALE` | 每步执行前重算；持久化的计划另用定位符和内容哈希写前置条件 |
| ADR-0013 的 `CopyProject(Snapshot)` | 计划开始前备份一次 |
| ADR-0010 的 `outcome.get` 与写入暂停 | 每步照用；计划的结果由各步的结果推出 |
| 握手的 `operations` | 能力检测的一部分（4.4） |
| 导出目录（ADR-0012） | 页面预览、图纸层校验的图片、`.eplanpack` 的导出 |
| 审计哈希链 | 新增 `plan_approval`、`plan_result`、`workspace_change`、`pack_change` 记录 |
| 离线宿主 | 以后用于参考项目的批量提取 |

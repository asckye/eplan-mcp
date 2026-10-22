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

## 4. 详细设计

### 4.1 工程包的格式与资源模型
并行的代码栈 PR #393–#396 已经实现清单的 v0 草案（`src/EplanMcp.Packs`，`schemaVersion` 1）。本节以它为基础，只写补充和调整。落盘格式按开发流程属于 L3：原则由本节和 ADR-0024 定下；具体字段在第 1 阶段验收之前仍可调整，不作对外承诺。

#### 4.1.1 三种形态
| 形态 | 位置 | 谁能写 | 用途 |
|---|---|---|---|
| 开发目录 | `packs-dev\<包 ID>\` | 只经工具（`eplan_pack_import`、`eplan_asset_update`）；工程师也可以手工修改，下次读取时重新校验 | 导入、整理、修正 |
| 已安装的版本 | `packs\<包 ID>\<版本>\` | 没有人：构建或安装时写入一次，之后设为只读，每次使用前按清单哈希核对 | 项目锁定的对象 |
| `.eplanpack` | 导出目录或 `imports\` | — | 在工程师之间交换；ZIP，内容与已安装的版本相同 |

安装 `.eplanpack` 按 ADR-0024 校验后解到 `packs\` 下；构建开发目录得到的版本与安装得到的版本格式相同。EPLAN 自己的 `.zw1`、`.zw9` 是 7z 归档（A31），在包中作为普通文件保存，不解开。

#### 4.1.2 清单 `pack.json`
| 字段 | v0 | 本设计 |
|---|---|---|
| `schemaVersion`、`id`、`name`、`version`、`description` | 已有；`id` 为小写字母和数字，以 `.` 或 `-` 连接；版本为 SemVer 2.0.0 | 沿用 |
| `compatibility` | `eplanVersions`（EPLAN 版本范围，不带运算符时按前缀匹配）、`variants`、`capabilities`（能力目录 ID 或工具名） | 沿用；`capabilities` 的写法见 4.4 |
| `dependencies` 与 `pack.lock.json` | 依赖范围（npm 写法的子集）；锁记下解析出的版本和哈希 | 沿用。这是包自己的依赖锁，与项目绑定中的锁（4.1.5）是两回事 |
| `resources` | 每个文件或项目：路径、种类、子类型、SHA-256、大小、文件数、`bundled` / `referenced`、来源（输入名加相对路径，从不记本机的绝对路径）、文件中读出的 EPLAN 版本 | 沿用。参考项目默认 `referenced`（不随包携带）；`referenced` 的资源每次使用前按哈希核对（ADR-0024）；种类在 v0 之外增加 4.1.3 的知识文档 |
| `configuration` | `company`、`cabinetTypes`、`projects`、`cabinets` 四个作用域 | 包中只用 `company` 和 `cabinetTypes`（基础包另有 `base`）；项目和柜的值属于具体的 EPLAN 项目，放在项目绑定中（4.3）。建议格式定稿前去掉 `projects`、`cabinets` |
| `rules`、`mappings`、`workflows`、`docs`、`acceptanceSamples` | 带 `standing`（`official` / `candidate`）的资源引用 | 沿用；`official` 的规则必须有来源（4.2） |
| `facts` | 从资源文件读出的值及证据（如版本、归档格式），只记测得的 | 沿用，只放文件级的事实；从参考项目快照提取的工程事实数量大，放在知识文档中 |
| `provenance` | 生成程序、作者（从不取自 Windows 账户）、时间、输入名 | 沿用 |
| 签名（新增，不在清单中） | — | 签名覆盖清单的摘要，所以不能写进清单，而是旁边的分离文件 `pack.sig`，可选（ADR-0024） |
| `migrations`（新增） | — | 从哪些旧版本迁移、改了什么（4.1.5） |

- **包摘要**：`pack.json` 按 RFC 8785（JSON 规范化）序列化后的 SHA-256。`resources` 中有每个文件的哈希，所以摘要覆盖包的全部内容。项目绑定和计划都记摘要。
- 清单永远由 EplanMcp 生成：导入、`eplan_asset_update`、构建时重算；工程师手工修改开发目录后，下一次读取时重算，并在 `status` 中报告变化。
- **参考项目默认不随包携带**：包中只放提取的事实、预览、源文件的哈希和 EPLAN 版本。参考项目往往很大，又含客户数据。需要携带时，在 scratch 副本上用 `Backup.Project` 打成 `.zw1` 放进包（A18：它会改写被备份项目的文件，所以只在副本上做）。

#### 4.1.3 知识文档
EPLAN 原生文件之外，EplanMcp 生成的结构化内容都是 JSON 文档，作为资源列在 `pack.json` 中，用同一种信封（借鉴 Backstage 的实体格式）：
```json
{
  "apiVersion": "eplanmcp.pack/v1",
  "kind": "Module",
  "id": "acme.panels.base@1.2.0:Module/motor-dol",
  "metadata": { "name": "motor-dol", "title": { "zh": "直接起动电机回路", "en": "DOL motor circuit" }, "tags": ["motor"] },
  "origin": { "kind": "reference_project", "resource": "projects/ref-a.elk", "page": "=AP+ST1/3", "locator": { } },
  "status": { "level": "ok", "problems": [] },
  "spec": { }
}
```
- `id` = `<包 ID>@<版本>:<种类>/<名称>`，开发目录中版本写作 `dev`。MCP 资源的 URI 为 `eplanpack://<包 ID>/<版本>/<种类>/<名称>`。
- `origin` 是来源位置：清单中的资源、参考项目中的页和定位符（4.6）；工程师和 AI 都能据此回到 EPLAN 中查看。
- `status` 记录加载问题，不抛异常：有问题的资源照样列出，标明不可用的原因。
- 按约定放在 `knowledge/<种类>/<名称>.json`、预览放在 `previews/`；是否合法不看目录名，看清单中登记的种类和扩展名（ADR-0024）。

| 种类 | 内容 |
|---|---|
| `Module` | 参数化模块（4.10） |
| `ConfigSchema`、`ConfigLayer` | 配置项定义；一层配置（4.3） |
| `Rule`、`Candidate`、`Gap` | 正式规则、候选、缺口（4.2）；v0 的 `standing` 区分前两者 |
| `FactSet` | 从参考项目快照提取的一组事实（4.2） |
| `MaterialMapping` | 参数值到部件的对应：沿用 v0 的种类名和 `mappings` 槽，内容是带信封的 JSON 文档 |
| `Workflow` | 可复用的工作流（4.8） |
| `Sample` | 验收样例：输入、预期的校验结果 |
| `Preview` | 页面预览（PNG；SVG 的限制见 ADR-0024） |

#### 4.1.4 自动生成
`eplan_pack_import` 按扩展名和内容识别材料：`.zw1` 是项目备份；`.zw9` 是基本项目；`.ema`、`.emp` 是宏；含 `.elk` 和 `.edb` 的目录是项目；`.eplanpack` 是工程包；表格和文本先作为 v0 的 `MaterialMapping` 或 `Document`，由 AI 整理成结构化的 `MaterialMapping`、`Candidate` 等。识别不了的文件拒绝，并说明原因。AI 用 `eplan_asset_update` 写入整理的结果，Host 按种类的 schema 校验，重算索引和清单。AI 不能写 `FactSet`（只来自提取），也不能把规则设为正式（4.2）。

#### 4.1.5 版本、锁定与迁移
- **版本号**：主版本：模块接口、规则的含义或配置项被删改；次版本：新增资源；修订号：不改变接口的修正。
- **构建**：`eplan_pack_build` 把开发目录冻结为一个版本。同一个包 ID 和版本已经存在而摘要不同时拒绝；版本从不覆盖。
- **项目锁定**：项目绑定 `workspaces\<项目键>\binding.json` 的 `locks` 记录每个柜的配置链上每个包（包括依赖）的 ID、版本和摘要。计划记下锁的摘要，执行时不同就拒绝（4.8）。
- **安装新版本不改变任何锁**：旧版本留在 `packs\` 中；还有绑定锁定某个版本时，拒绝删除它。
- **迁移**（`eplan_pack_migrate`，第 3 阶段）：
  1. 列出资源的增删改；
  2. 列出每个柜生效配置的变化，附溯源；
  3. 列出规则的变化；
  4. 在项目的 scratch 副本（`CopyProject(Snapshot)`）上按新版本重新跑规则检查和验收样例；
  5. 工程师经审批渠道确认后才更新锁（它改变以后的计划受哪些规则和配置约束）。

  迁移不改动 EPLAN 项目本身；新版本要求的改动另行生成计划。

### 4.2 事实、推断与正式规则

| 类别 | 来自 | 能否作为强制检查 | 必须带 | 谁能改 |
|---|---|---|---|---|
| 事实（`FactSet`；文件级的在清单 `facts` 中） | 从参考项目的快照或资源文件中测得 | 不能 | 参考项目哈希、快照哈希、定位符，或文件中的位置 | 只有提取器；要改就重新提取 |
| `Candidate` 候选 | AI 或插件根据事实推断 | 不能；只作为建议显示 | 依据（事实 ID）、推断者、置信度、状态（`proposed` / `accepted` / `rejected`） | AI 可以新建；工程师接受或拒绝 |
| `Rule` 正式规则 | 公司文件，或由候选转来 | `mandatory` 可以，`recommended` 只提示 | `mandatory` 必须有来源：文件资源和位置，或工程师确认的记录 | 只有工程师确认才能设为正式 |
| `Gap` 缺口 | 资料缺失、参考项目之间互相矛盾 | 不能 | 缺什么、相关的事实 | AI 可以新建；工程师补资料后关闭 |

- 没有来源的 `mandatory` 规则在构建时报 `PACK_INVALID`。
- 参考项目之间互相矛盾时记为缺口，不归纳成规则。
- 候选转为正式规则、缺口关闭，都要工程师经审批渠道确认（AI 不能替公司宣布一条规则是正式的），确认记录写进规则的 `source` 和审计。以后由 Workbench 的修正界面完成（第 3 阶段之后）。
- 规则检查、关系图的结果都标明依据是事实还是推断（4.12）。

### 4.3 配置分层

层的顺序：`base`（通用基础）→ `company`（公司）→ `cabinet_type`（产品系列或柜型，v0 的 `cabinetTypes`）→ `project`（项目）→ `cabinet`（单个柜）。前三层是包中的 `ConfigLayer` 资源；后两层是项目自己的值，存在项目绑定 `workspaces\<项目键>\binding.json` 中，不进包。

- **配置项**由包中的 `ConfigSchema` 定义：名称、类型、单位、枚举或范围、说明、哪些层可以设置。没有定义的项一律拒绝。
- **每一处设置**要么是默认值（`default`），要么是硬约束（`constraint`：`fixed` 固定值，或 `allowed` 集合或范围）。
- **合并**：从 `base` 到 `cabinet` 逐层处理。默认值后者覆盖前者；硬约束取交集，下层只能收窄，放宽时报 `CONFIG_CONFLICT`；最后生效的默认值必须满足全部硬约束，否则同样报 `CONFIG_CONFLICT`，指出冲突的两处来源。**从不悄悄截断或改写一个值。**
- **溯源**：每个生效的值都带 `trace`，逐层列出 `{layer, source, pointer, kind, value}`，`source` 是 `<包 ID>@<版本>:ConfigLayer/<名称>` 或绑定的修订号。

| 层 | 来源 | `motor.starter`（例） |
|---|---|---|
| base | `eplanmcp.base@1.0.0:ConfigLayer/defaults` | default `dol` |
| company | `acme.panels.base@1.2.0:ConfigLayer/company` | constraint allowed `[dol, star_delta]` |
| cabinet_type | `acme.panels.m200@2.0.1:ConfigLayer/m200` | default `star_delta` |
| cabinet `+A2` | 绑定修订 7 | default `dol` |
| **生效** | | `dol`，trace 4 项 |

- **柜**：`binding.json` 中 `cabinets: [{id, title, layers, values, structure}]`，另有 `revision` 和 `locks`（4.1.5）。`structure` 把柜映射到项目结构（位置代号 `+`，可选高层代号 `=`）。两个柜的映射重叠时拒绝绑定；每个对象至多属于一个柜，没有映射到任何柜的对象不能被柜的计划修改。只有一个柜的项目有一个覆盖全部结构的默认柜，第 2 阶段只用它。
- **显式标识**：写入和计划类工具必须给 `project`；项目有多个柜时必须给 `cabinet`；计划记下绑定的修订号和锁的摘要，执行时任何一个变了就拒绝（4.8）。上下文、规则检查、校验都只在一个柜的范围内取数据（4.7），所以柜与柜从不混用。
- 项目绑定放在数据根目录，不写进 EPLAN 项目。项目被移动、复制到别处时绑定不跟着走，见待定问题 Q4。

### 4.4 能力与版本检测
- **来源**：
  - 握手：EPLAN 版本、变体、是否离线、`operations`；
  - 握手新增 `licence`：Bridge 启动时对一组登记的 `LicenseOptions` 调 `License.HasOption`（只检查，不占用授权；从不调 `LockOption`，A2），例如 `APIExtension`、`ElectricP8`、`ProPanel*`、`Cogineer*`、`Preplanning*`。只增加字段，协议版本仍为 1（与 ADR-0010 增加 `outcome.get` 的做法相同）；
  - API 能力目录（`catalog/`，[覆盖设计](2026-10-21-api-capability-coverage.md)）：成员 ID；按 EPLAN 版本记录的状态（`live_verified` 等）取自 `catalog/coverage.json`，[覆盖报告](../eplan/coverage-report.md)是它的可读形式。
- **包的要求**写在 v0 的 `compatibility` 中：`eplanVersions`、`variants`，以及 `capabilities` 中按前缀区分的条目：

  | 写法 | 含义 | 例 |
  |---|---|---|
  | `A:<action>[/TYPE]` | Action 目录的条目 | `A:backup/PROJECT` |
  | `O:<类型>.<名称>` | 能力目录的重载组（覆盖设计 3.1）；v0 的模式不允许成员 ID 中的括号和逗号，所以不写完整的成员 ID | `O:Eplan.EplApi.DataModel.Graphics.PlaceHolder.ApplyRecord` |
  | `op:<操作>` | Bridge 操作（握手的 `operations`） | `op:module.insert` |
  | `licence:<选项>` | `LicenseOptions` 的名称 | `licence:CogineerBuilder` |
  | `plugin:<ID>` | 可选的插件（ADR-0025） | `plugin:acme.terminal-allocation` |
  | `eplan_*` | 工具名 | `eplan_macro_insert` |

  资源和工作流的步骤可以有自己的要求。
- **判定**：`op:`、`licence:`、工具名和 EPLAN 版本、变体是硬要求，不满足时用到它们的资源在索引中标为不可用（照样列出），计划编译时报 `CAPABILITY_MISSING`，`metadata.missing` 列出缺的项；`A:`、`O:` 的条目在当前版本上不是 `live_verified` 时只给警告，真正的约束来自实现它的 Bridge 操作是否存在。
- **源版本**（S27，A31）：不打开项目，在 Bridge 中用 `ProjectManager.GetProjectDataCompatibility` / `GetProjectDatabaseVersion` 判断（不到 1 秒）；不靠 EPLAN 时读 `.edb\ProjectInfo.xml` 的属性 `10043`，只取需要的字段（文件中有创建者的个人信息）。`.zw1`、`.zw9` 是 7z 归档，先还原或新建到 scratch 再判断；在 Host 中直接读 7z 要引入新的依赖，按 L2 另议。源版本比当前 EPLAN 新的材料打不开，导入时就拒绝；需要升级的只在 scratch 副本上升级（S30）。

### 4.5 工具与资源面

#### 4.5.1 工具
新工具分到两个工具集（ADR-0022）：`pack`（包、资源、柜）和 `workflow`（上下文、计划、任务、关系、规则、差异），都只在 `full` 中，不进 `core`。"本地数据"指只写 EplanMcp 自己的数据目录、不碰 EPLAN 项目，按 ADR-0012 的方式标注（`readOnlyHint: false`、`destructiveHint: false`），不需要审批，写审计。三处例外要工程师经审批渠道确认：安装别人的 `.eplanpack`（ADR-0024）、改变项目锁、把候选设为正式规则（4.2）。

| 工具 | 级别 | 作用 | 阶段 |
|---|---|---|---|
| `eplan_pack_import` | 本地数据；参考项目复制或还原到 scratch（scratch 中的新项目，Write 自动允许）；安装别人的 `.eplanpack` 要确认 | 导入材料到开发包：识别、复制、提取、生成清单和索引 | 1 |
| `eplan_pack_list` | Read | 已安装和开发中的包、版本、被哪些项目锁定 | 1 |
| `eplan_pack_get` | Read | 清单摘要、问题、缺口、能力是否满足 | 1 |
| `eplan_pack_build` | 本地数据 | 冻结开发包为一个版本；可选导出 `.eplanpack` 到新建的导出目录 | 1（导出：3） |
| `eplan_pack_migrate` | Read；更新锁要工程师确认 | 迁移的差异与重新校验（4.1.5） | 3 |
| `eplan_asset_search` | Read | 按文字、种类、包、标签、适用条件搜索资源 | 1 |
| `eplan_asset_get` | Read | 详情、页面预览（图片内容或资源链接）、来源位置 | 1 |
| `eplan_asset_update` | 本地数据 | 在开发包中新建或修改候选、模块元数据、工作流、映射、缺口；不能写事实，不能把规则设为正式 | 1–3 |
| `eplan_cabinet_list` | Read | 项目的柜、配置链、结构映射、锁 | 3 |
| `eplan_cabinet_bind` | 本地数据；改锁要工程师确认 | 绑定或修改柜：配置层、值、结构映射 | 3 |
| `eplan_context_get` | Read | 按需上下文（4.7） | 2（最小）、4 |
| `eplan_project_create` | Write（scratch 中自动；之外逐次审批） | 从 `.zw9` 新建项目，或把 `.zw1`、项目副本还原到 scratch | 2 |
| `eplan_workflow_plan` | Read | 把工作流或内联步骤编译为计划（4.8） | 2 |
| `eplan_plan_apply` | 按步骤的最高级别 | 一次审批、计划前备份、逐步执行、校验（ADR-0023） | 2 |
| `eplan_plan_get` | Read | 计划、各步结果、校验、差异 | 2 |
| `eplan_rule_check` | Read | 在快照上按规则检查项目或柜；可选把修复生成计划 | 3 |
| `eplan_connection_trace` | Read | 路径追踪、跨页、影响分析（4.12） | 4 |
| `eplan_project_diff` | Read | 两个快照、两个项目之间的差异 | 4 |
| `eplan_task_get`、`eplan_task_list`、`eplan_task_cancel` | Read、Read、取消 | 长任务的状态、列表、取消（4.13） | 2（get）、5 |

- **与现有工具的关系**：`eplan_rule_check` 只在快照上求值，不写 EPLAN；`eplan_check_run` 是 EPLAN 自己的检查，会写消息，两者在描述中写明区别。`eplan_macro_*` 只管 `$(MD_MACROS)`；包中的宏经 `eplan_asset_*` 查询，经计划插入。`eplan_project_create` 实现 ADR-0005 第 6 节 `eplan_scratch_create` 的用途（那个名称从未发布），见 Q2。
- **负责人建议的 `eplan_profile_*` 改名为 `eplan_cabinet_*`**：本项目中 Profile 已经指工具 Profile（ADR-0006、ADR-0022、`serve --profile`），再用于工程配置会让工程师和模型混淆；`eplan_config_*` 也不合适，`config.json` 是只能手工修改的安全配置（SEC-02）。见 Q1。
- **资源词表**（工具设计规范 3.2）新增 `pack`、`asset`、`cabinet`、`context`、`workflow`、`plan`、`rule`、`task`；动作新增 `build`、`migrate`、`bind`、`apply`、`trace`、`diff`，以及只用于规则的 `check`（`check` 同时是已有的资源词）。
- **新错误码**：`PACK_INVALID`、`RESOURCE_NOT_FOUND`、`CAPABILITY_MISSING`、`CONFIG_CONFLICT`。计划冲突沿用 `PLAN_STALE`，`metadata.conflicts` 列出变化了的定位符；参数错误沿用 `INVALID_ARGUMENT`，`metadata.pointer` 指出 JSON 中的位置。

#### 4.5.2 MCP Resources
| URI 模板 | 内容 | 同样能取到它的工具 |
|---|---|---|
| `eplanpack://{pack}/{version}/manifest` | 清单 | `eplan_pack_get` |
| `eplanpack://{pack}/{version}/{kind}/{name}` | 一个资源 | `eplan_asset_get` |
| `eplanpack://{pack}/{version}/previews/{file}` | 页面预览（`image/svg+xml`、`image/png`） | `eplan_asset_get` |
| `eplanmcp://plans/{planId}` | 计划与结果 | `eplan_plan_get` |
| `eplanmcp://tasks/{taskId}` | 任务状态 | `eplan_task_get` |

- 每个资源的内容都能经工具取到：多数客户端不让模型自己读取资源。工具结果中附 `resource_link`，支持资源的客户端可以直接打开。
- MCP 2026-07-28 要求资源的列表和读取结果带 `ttlMs` 和 `cacheScope`（未在 SDK 2.2.0 上核实）：已安装的包不可变，给长 TTL；开发包、计划、任务给短 TTL。
- 订阅（`subscriptions/listen`、资源更新通知）只用于计划和任务，只对订阅了的客户端发；其他客户端用 `eplan_task_get` 轮询。
- **不支持动态发现的客户端**：用静态工具集 `--toolsets pack,workflow` 或 `full`（ADR-0022）；不支持 Resources 的用工具；不支持 Tasks 的用 4.13 的等待加轮询。

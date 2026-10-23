# 设计文档：M6 第 2 阶段的计划与一个完整任务

| 项 | 内容 |
|---|---|
| 状态 | In Review（实施合同草案；未实现、未运行 EPLAN） |
| 级别 | L2，细化已接受的 ADR-0023；新增落盘格式见已接受的 [ADR-0028](../adr/0028-plan-record-format.md)，可选 Workbench 扩展仅提议 [ADR-0027](../adr/0027-workbench-plan-review.md) |
| 关联 Issue | — |
| 相关依据 | [M6](2026-10-21-m6-engineering-packs.md) 4.5–4.10、4.13、4.16–4.17；[Bridge 快照设计](2026-10-21-m6-bridge-snapshot.md)；ADR-0003/0005/0010/0013/0014/0023–0026 |
| 最后更新 | 2026-10-23 |

## 1. 背景

已有单步 `WritePipeline`、备份、结果恢复、快照和包仓库，尚无组合执行器。五份工具规格仍把容量、预测、归属等列为提议。本设计以当前检出的代码为准；`RESOURCE_NOT_FOUND`、`CAPABILITY_MISSING`、`CONFIG_CONFLICT` 已在 `Contracts/ErrorReason.cs` 和工具标准 6.3 登记，规格中的“尚未登记”已过时。

## 2. 目标与非目标

目标：在已有且打开的项目上编译有界计划，按策略整体审批，先备份，再逐步执行；返回对象数据校验、五类差异和明确的部分完成结果。新目标先独立调用 `eplan_project_create`；它返回关闭状态，工程师在 EPLAN 中打开后再编译，未打开报 `PROJECT_NOT_OPEN`（A34 最新补充、创建工具规格）。本期不增加 `project.open/close`。

非目标：`resume`、自动重试/补偿、持久化任务恢复、MCP Tasks、事件缓存、配置合并、多柜、规则执行、电气关系/图纸校验、系统主数据及 Code 步骤。`module.insert`/`placeholder.apply` 推迟，不能把 `device.update`（仅改标识）当成通用属性降级方案。

## 3. 方案概述与问题决议

`workflow_plan → PlanStore → PlanOrchestrator → 整体审批 → project.backup → 逐步 plan/execute → PlanVerifier → plan_get`；`TaskRegistry` 仅承载当前 Host 的等待与轮询。Bridge 新能力均为 `invoke` 内的操作，不增加 JSON-RPC 方法（ADR-0003）。

| 决议 | 规格问题 | 决定、理由与事实依据 | 合同位置 |
|---|---|---|---|
| D01 | workflow_plan Q1 | 七个既有操作、有界输入、只开放可证明的依赖预测；快照不原子，双读不稳即拒绝（`ProjectSnapshotOperation`、A36） | 4.2–4.3 |
| D02 | workflow_plan Q2 | 不可变内容规范哈希、原子发布、24 h 可执行、30 d 查询保留；不套用 outcome 的 256 条（ADR-0023、`OutcomeRecords`） | 4.1 |
| D03 | workflow_plan Q3 | 新增独立 `project.backup`；模块和值集暂拒绝；原事务路径复用（A18、A33、S32） | 4.4、4.6 |
| D04 | plan_apply Q1 | 受理时一次消耗；Host 编排器配 Bridge 原子步入口，命名撤销，跨 Host 文件互斥；不改共享流水线接口（现有 execute 在主线程内复核） | 4.4 |
| D05 | plan_apply Q2 | 本期 schema 不提供 `resume`，传入即 `INVALID_ARGUMENT`；剩余意图用 workflow_plan 编译新计划。ADR-0023 的“重新编译、重新审批”保留，便捷入口留阶段 5 | 4.5 |
| D06 | plan_apply Q3 | 未知引用范围不能批准；`ApplyRecord` 的组合提交/一次撤销未验证，两个操作均不注册为本期步骤（`PlaceholderApplyExecuteOperation` 只核对值表，A33/S32） | 4.2、4.6 |
| D07 | plan_apply Q4 | 静态 Destructive 注解、动态最高 tier；Host 专用强类型错误结果，Bridge string metadata 保持原合同（`BridgeError`、`ToolResults`） | 4.8 |
| D08 | plan_get Q1 | 固定 section 条目流，50/200 分页，游标绑定内容哈希和结果修订；大条目拆成可重组的片段（`PageCursor`、结果预算规范） | 4.7 |
| D09 | plan_get Q2 | 同一 stdio Host 会话可见；只读资源模板复用工具读模型，不订阅、不宣称 TTL；SDK 现有 `PackResources` 已能注册模板 | 4.5、4.7 |
| D10 | task_get Q1 | 活跃不挤出；终态保留 1 h，上限 256，读取不续期；满额拒绝新任务（内存任务不是 Bridge outcome） | 4.5 |
| D11 | task_get Q2 | 等终态或等待期满，不因中间状态变化提前返回；固定进度、引用和映射，保留工程结果（ADR-0010/0023） | 4.5 |
| D12 | task_get Q3 | 用 Host 生成的运行实例和 stdio 会话键，不能用自报名称/IP/taskId 认证；现有 HTTP 为 Stateless，无稳定客户端身份（`VmHttp`、`HttpCaller`） | 4.5 |
| D13 | task_get Q4 | 无 `state\tasks`、检查点或重启继续；仅保留 DanglingWrites 核实/暂停义务（ADR-0010，M6 4.16 阶段 5） | 4.4–4.5 |
| D14 | context_get Q1 | 复用确定性 token 估算，稳定排序、来源去重、必要信封实测预算，整条截断（`ResultShaper.EstimateTokens`） | 4.7 |
| D15 | context_get Q2 | 默认柜只读；已有绑定读取真实 revision/locks，无绑定为 null；generation 为 null，每次新快照（`WorkspaceStore.ReadAsync`，S33 事件不证明提交） | 4.7 |

## 4. 详细设计

### 4.1 计划记录与生命周期

`Host/Plans` 定义 `PlanDefinition`、`PlanState`、`PlanResult`、`PlanStore`，不扩展单步 `WritePlan`。外层记录 `{schemaVersion:1,planHash,definition,state}`；definition 是不可变部分，state 是原子替换的结果视图。新格式由 ADR-0028（L3，2026-10-23 接受）决定。

| 部分 | 必需内容 |
|---|---|
| definition | `planId`（Host 随机 128 bit、小写 32 hex）、项目名称/规范路径/辅助 GUID、`cabinet:default`、Host 运行/会话归属键、`createdAt/expiresAt`、schema/编译器/快照格式版本、EPLAN 版本/变体及所需 operations |
| 基准与输入 | `inputs.steps`（固定全部默认值）、`bindingRevision/lockDigest/contextRef`、资源精确版本/清单及文件 SHA-256、`sources`（实际可核验的来源）、基准页/对象/名称集合哈希、`changesDigest`；无材料的值为 null |
| steps | `{index,op,args,tier,readScope,modifyScope,preconditions,expected,plannedChanges,dependsOn,prediction}`；保留原序号，数组有序；无变化步骤标 `effective:false`，不发送 execute、不建撤销步骤 |
| state | `{revision,status,attemptId,taskId,ownerHost,approval,startedAt,completedAt,result}`；`approval` 只存决定/渠道/绑定摘要，不保存可复用批准；result 沿用 apply 的步骤、备份、validation/diff/coverage 形状 |

`planHash = SHA256(UTF8(canonical(definition)))`，小写 hex；没有 BOM、缩进、结尾换行。读取拒绝未知/重复成员；对象键递归按 `StringComparer.Ordinal` 排序；已知可空成员明确写 null，数组保留合同顺序，集合先去重按序数排序。字符串保持校验后的原值，不额外改写工程文本；JSON 转义固定为引号/反斜线及控制字符的短转义或小写 `\u00xx`，其他 Unicode 原样 UTF-8，拒绝孤立代理项。数字先解析为有限 decimal，再输出无指数、无多余零的区域无关十进制，`-0` 为 `0`；超出 decimal 范围拒绝。时间统一 UTC、7 位小数、`Z`。禁止把会话指纹、`ToStringIdentifier` 或 `DatabaseIdentifier` 写入 definition/证据（A32/A34）。

目录保留总体设计的 `state\plans\<planId>.json`，同级 `<planId>\changes.txt` 和 `evidence\<sha256>.json`；不另造第二份 plan.json。证据按 SHA-256 引用，不进 state 的可变哈希。ID 不是路径；根以下每一级拒绝链接/联接点。

编译在同卷 `.partial-<random>` 中写证据和完整文本，Flush(true)；先发布证据目录，最后原子 Move 新 JSON（不覆盖）作为可见提交点。`plan_compiled` 审计先记录 hash，失败不发布。状态更新持 `<planId>.lock`（FileShare.None），校验 revision 后写临时文件、Flush(true)、原子替换；读者只见旧/新整份记录。遗留临时文件不成为可执行计划。

编译时内存保存可信 `planId → owner + planHash`，apply 还核对审计中的编译摘要；SHA-256 不是文件认证，不声称能防同用户恶意程序同时重写文件与摘要（安全标准第 2 节）。

可执行期默认 `createdAt + 24 h`，准入和审批后都核对；`config.json` 新增 `planLifetimeHours`（整数 1–24，默认 24，只能缩短），由 HostConfig 严格解析、doctor 展示，无 MCP 修改入口。过期不生成伪执行结果，get 仍可读。

保留期为 `max(expiresAt,completedAt) + 30 d`，未执行者从 expiresAt 起算；读取不续期。活跃及尚有未核实写入的 unknown 记录永不自动挤出；审计和备份不清理。每次创建前、每小时清理已到期的普通记录；未知 schema/损坏记录隔离保留，不能自动修复后执行。上限 1,000 份、总计 512 MiB（含隔离/证据）；每份 definition ≤2 MiB、全目录含结果 ≤16 MiB。准入预留每份完整 16 MiB（定义 2、状态/步骤摘要 1、证据 13 MiB）；不足时报 `RESOURCE_EXHAUSTED`，不删除未到期记录腾位置。编译时已知证据超限拒绝；运行中证据流达到预留上限则停止采集及后续步，保存已知写结果，verification=skipped/coverage 标未采集部分，不把证据丢失当作 OUTCOME_UNKNOWN。

状态为 `compiled → approved → running → 五种终态`；自动允许时跳过 approved（state.policy=Allow）；等待审批时仍 compiled，但 attemptId 已占用。终态就是 ADR-0023 的五个 execution；没有 `expired` 或任务状态混入计划枚举。revision 从 0 开始，每次持久状态变化加 1。

| 计划 execution | 聚合（忽略 effective=false，unknown 优先） |
|---|---|
| `not_started` | 无有效步开始；全部无变化、dry-run 也如此；dry-run 不推进状态 |
| `rolled_back` | 第一有效步失败且回滚，无已提交步 |
| `committed` | 全部有效步提交；不表示校验通过 |
| `partially_committed` | 至少一有效步提交，后续未开始/已回滚而停止 |
| `unknown` | 任一步 reason=OUTCOME_UNKNOWN；暂停项目及当时实例 |

部分完成/unknown 永不 retryable；其余只有底层错误本身可重试且 execution 为 not_started/rolled_back 才可为 true。计划已经消耗时，重试指重新编译新计划，不能原 planId 重放。

### 4.2 步骤目录、限制与预测

所有操作沿用同名工具的参数验证；tier 来自服务端目录。请求 1–20 步、UTF-8 参数 JSON ≤256 KiB、每步受影响页 ≤50、单次 guard 覆盖对象 ≤10,000、显式定位符总计 ≤200；超限整体 `INVALID_ARGUMENT`，列 JSON Pointer。输入不接受 project/dry_run/fingerprint/审批/备份/undo 等步内覆盖。

| op / tier | 本期范围、复用与依据 |
|---|---|
| `page.create` / Write | 1–50 页，四种已有类型，描述/语言固定；`PageCreate*Operation`，A13/A17 |
| `page.update` / Write | 1–50 页，只改指定语言描述；`PageUpdate*Operation`，A17 |
| `device.update` / Write | 一次改一个设备标识；全部主/辅功能及跨页范围固定；不能用截断的 pages 详情定范围；`DeviceUpdate*Operation`，A13/A17 |
| `macro.insert` / Write | 仅 `$(MD_MACROS)` 的 `.ema`、MultiLine、variant=0、numbering=keep、空 Circuit 页；全项目名称冲突先拒绝，原 execute 再防合并；`MacroInsert*Operation`，A13/A14 |
| `page.delete` / Destructive | 明确 1–50 页；完整列 orphan 设备及剩余功能范围；`PageDelete*Operation`，A18 |
| `page.renumber` / Destructive | 明确 1–50 页、固定 start/step/keep_text/subpages；审批允许 EPLAN 决定新名，不承诺精确映射；`PageRenumber*Operation`，A18 |
| `device.renumber` / Destructive | 本期必须显式 pages（1–50），不允许隐式全项目；方案/范围固定，仍带 ? 如实报告；`DeviceRenumber*Operation`，A18 |

重编号只能作为最后有效步，modifyScope 保守覆盖整个项目（仍须满足 50 页/10,000 对象 guard 上限），避免未知编号传播。device.update 必须证明 reader 返回的功能全集都在快照覆盖中；无页功能、范围不全或影响不能界定时 `CAPABILITY_MISSING`，不缩小成主功能所在页。

`module.insert`、`placeholder.apply` 本期不进 enum：`INVALID_ARGUMENT` 指明尚未开放；context 可列候选并标 `CAPABILITY_MISSING`。值集引用可能改页/项目属性，而 standard 无此覆盖；不能把 unknown 校验当作批准越界（A33、`PlaceholderApplyExecuteOperation`）。项目创建/还原、device.place、connection.generate、report.update、action 和主数据写入同样不在 enum。

依赖只能引用前序号，禁止自环/重复/前向引用。`args.page` 可用 `{from_step:k,page:"完整页名"}`，仅替代 page 字符串；只能指向 page.create 中唯一同名页。Host 解引用后才生成既有 Bridge args，新对象不编造 oid。

只开放 page.create→page.update、page.create→macro.insert；后者要求第一个新页状态仍为空。其他读取/修改范围重叠都拒绝为 `INVALID_ARGUMENT`（指明缺少可用预测器），不能伪装成独立步。显式依赖但不重叠可以排序执行，仍须真实规划。

独立步调用既有 `*.plan`，持久化 Changes 和可解释语义详情，丢弃会话 Fingerprint。依赖页更新预测精确描述/语言和页类型；依赖插宏预测精确宏 SHA-256、位置、变体、编号、页以及功能种类/数量/允许名称。新增只读 `plan.macro.inspect` 在原宏根下复用 `MacroFiles/IMacroReader`，返回 variant=0 的完整函数描述、外框、总对象数和文件 SHA-256，不返回根目录路径；拒绝截断资料、非 MultiLine、无法确定结构归属（包括保留外部结构的 PLC 端子）的宏。不能只靠 macro.get 的前 50 个设备作预测（A14、`MacroGetOperation.MaxDevices`）。

名称模式由 Host 生成：固定页结构及宏标签字面部分，仅宏中的编号占位段允许登记的 `?`/数字形式；不得接受客户端正则、`.*` 或跨结构通配。执行时真实清单和完整宏描述须落在这一封闭约束内，且 sha/种类/数量不变。不能证明时不执行；numbering=number、多变体及复杂模块以后另做预测器。

### 4.3 范围、前置条件与快照

`scope={pages,structures,locators}`，均为已解析的精确集合；structures 只作结构段前缀过滤，不能以普通字符串 StartsWith 混入 `+A10`。多种条件取交集。编译把业务名称解析为 `ObjectLocator` 的 `{project,kind,type,oid,name,page,category,pos}`，pos 是 `x,y` 毫米文本；连接沿用端点描述，不采用早期 pos_mm。项目键为无扩展名规范路径，比较用 OrdinalIgnoreCase、存储键/项目锁哈希前 ToUpperInvariant，与 WorkspaceStore 一致；副本路径不同。GUID 仅辅助，不能代替项目键（S30）。

`preconditions` 为带 tag 的封闭联合：`objectHash(locator,hash)`、`pageHash(locator,hash)`、`members(scope,hash,count)`、`absent(kind,name)`、`resource(sha256)`、`binding(revision,lockDigest)`。members 用按 kind/type/oid 排序的键和内容哈希计算，包含存在性/新增/删除；页名/设备名占用集合另存摘要。读取范围含名称冲突域、全部同设备功能及删页后的 orphan 影响，不能照抄最多 50 行的 Details。

`expected` 分为精确存在/值/删除、或服务端编号/宏预测约束，均带来源和覆盖属性。标准快照的属性集不扩充：没有图形/文字/占位符及无页连接的完整证据。跳过连接数进入 coverage；不能声称全项目所有数据未变。plan 的 scope 限制与验证 coverage 分开保存。

按 ProjectSnapshotArgs 的 complete/nextAfterPage 取全，核对路径/GUID/profile/format、页清单及 missingPages。编译前后各完整读取一次所需范围及名称集合，逐页哈希/集合必须相同；不同时报 PLAN_STALE，不自动重试。双读是稳定性筛查，不是原子快照或数据库修订证明（S31）。

apply 的每个 guard 在一次主线程 LockingStep 中核对完整 readScope、object.resolve 同等描述和哈希，再规划执行；不依据修改日期/事件/缓存省略检查。超过 10 秒仍未完成 guard，写入前停止为 TIMEOUT/not_started。依赖步的基准由前步批准的精确变化转换，并绑定执行后真实 locator；其他字段必须与原基准相符，不能把全部实际状态直接接受为新基准。

### 4.4 执行、备份、审计与中断

新增 `PlanOrchestrator`，**不增加 WritePipeline hooks、可关闭审批参数或通用委托入口，不循环 RunAsync**。复用 PolicyEngine、ScratchPaths、ApprovalService、BackupFolders、SuspensionStore、DanglingWrites、AuditLog、IBridgeClient.InvokeWriteAsync、CallProgress。唯一从共享流水线抽取的是纯函数 `WriteChangeLines`（原 ChangeLines，含它依赖的格式化），由原流水线与新计划审计调用；单独无行为变化 PR。审批增加 Host 内部 `PlanApprovalView`/AskPlanAsync 和 ApprovalTexts 分支，绑定校验仍走原 ApprovalService，非 IPC 字段。

| 顺序 | 合同与失败处理 |
|---|---|
| 1 准入 | 先查本会话归属，校验输入；dry-run 仅只读重核对。正式执行先预留任务/存储容量、取得项目锁，再持 plan.lock CAS `attemptId:null→随机ID` 并写入任务关联；此后拒绝/冲突/备份失败也消耗计划。并发重放为 INVALID_ARGUMENT，附查询提示；容量/项目锁忙且未受理不消耗 |
| 2 初检 | 持有 `.project-<路径SHA256>.lock` 串行同项目计划（进程退出自动释放，不超时接管）；核对 hash、期限、项目/Bridge 实例、能力、资源、绑定及独立前置条件。会话指纹只在这次 apply 内存中保留；缺能力/冲突停下 |
| 3 策略/暂停 | 最高 tier 交 PolicyEngine；先 DanglingWrites.SettleOnceAsync，再检查项目及实例暂停。已有暂停按 ADR-0010 单独确认/记录后再进入计划审批，拒绝则 WRITES_SUSPENDED；从不把解除暂停合并进计划批准 |
| 4 一次审批 | NeedsApproval 时只 AskPlanAsync 一次，Allow 时零次；审批结果记录 plan_approval。绑定 `Fingerprint=SHA256(canonical({planHash,independentFingerprints}))` 和 `ArgumentsHash=SHA256(canonical({planId,planHash}))`，requestId 随机；仍验证期限、精确绑定、一次使用。批准后再核对文件/独立前置条件和期限 |
| 5 初始备份 | 新 `project.backup {project,target:BackupTarget}`，只经 invoke，主线程复用 ProjectBackups.Make/IProjectBackup（CopyProject Snapshot、页数校验），返回 `{path,pages,seconds}`。Host 用 BackupFolders 新目录；所有有效计划，包括 scratch Write，都先备份，失败不发任何写步骤 |
| 6 逐步 | 每步先重新检查暂停/资源/绑定，再既有 `*.plan`，Host 比较批准清单/预测约束。发新 `plan.step.execute`（IWriteOperation）进行下面的原子保护；每步 execution_started 写入并 Flush 后才发送，结果确定前不发下一步 |
| 7 停止/收尾 | 首错/取消/越界即停止，未发步 not_started；不自动撤销。保存证据、步骤结果、plan_result 和终态；验证失败仍保留 committed 事实。全程无效步则 not_started，不审批/备份 |

`plan.step.execute` 是固定七项的类型化适配，不接受任意 operation 名。Contracts 请求携带 planId/index/total、七选一 args、guard、批准 Changes/约束、临时指纹、Destructive BackupTarget；不携带审批回答。主线程内依次核对 guard→执行同名 `*.plan`→匹配批准语义和临时指纹→调用同名 `*.execute` 的 IOperation.Execute；直接调用操作对象，不嵌套 router/dispatcher，不再创建外层事务。事务前所有 guard/规划异常明确 stamped not_started，不落入 router 的默认 rolled_back。宏文件验 SHA-256 后持 FileShare.Read 句柄至插入结束，拒绝同时写入/替换；Host 持同一个 WorkspaceStore 的 .binding.lock 读绑定并保持到该步结果确定，防止核对后锁变更。这里消除的是核对/调用间隙，不声称排除同用户恶意程序。

通过 `PlanUndoScopeFactory` 装饰既有 ITransactionScopeFactory，组合该步的既有 execute，使 Begin 的名字固定为 `AI: plan <前8位> <k>/<n> <op>`；不改 TransactionRunner，不加嵌套 SafetyPoint。原单步工具仍用原 factory。wrapper 的一个 requestId/outcome 覆盖整步；Destructive execute 仍在指纹后、事务前执行自己的 ProjectBackups.Make，不能抵扣初始备份。

预算：初始备份 60 s（Host 等待 75 s）；逐步沿用各工具执行预算及 Destructive 一半给备份的预算；计划总墙钟 15 min（含审批），不再发会越过剩余预算的新步。原生备份超时不能强行中断：报 BACKUP_FAILED、不开始计划、不重做；未确认调用结束时不移动/删除副本，仅在状态中标不可用，结束后才用 BackupFolders 标 failed。进行中写步由 ADR-0010 核实，不能因总预算到期跳过结果记录。

审计：`plan_compiled` 记 ID/hash/归属/来源；`plan_approval` 记 hash/tier/各步摘要/渠道/决定/绑定（自动允许记 policy，decision=null）；`plan_result` 记五值/步骤/备份/校验/差异覆盖。逐步 execution_started/result 保留当前 DanglingWrites 必需的 requestId、project、tool、host PID/start、EPLAN PID/start，并加 planId/step、参数摘要、范围和来源哈希；哈希链算法不变。

批准或 execution_started 审计失败，拒绝下一次写。执行后审计/状态写失败不能把已提交改为回滚：保留内存事实、停止后续步，返回已知 execution 和 AUDIT_UNAVAILABLE/INTERNAL，未写成的 started/result 由 DanglingWrites 核实。取回 committed 只有状态、没有完整 Data（BridgeClient.Writes），须另读实际，不能捏造产物。

execute 超时/断开/取消复用原请求 ID、原 Bridge 的 outcome.get，不重发。即使取回确定提交，也在该步后停止本次计划并保存 recovered；若它是最后有效步仍可 committed。取不回为 OUTCOME_UNKNOWN，项目及当时实例暂停。A15/S8 证明进程死亡可能留部分写入且撤销历史消失，不能保证回滚。

重启先按现有 DanglingWrites 核实，新增 `PlanSettlement` 从逐步审计折叠原 host PID/start 已退出的已受理计划，补 plan_result/终态，不发送新步骤；不能仅因当前内存 map 不含 owner 就接管仍活着的 Host。started 无结果必须等待核实，不能仅靠 JSON 判 not_started。修改 DanglingWrites 时只保留 planId/step 的相关性，不加执行钩子。内存 taskId/归属不恢复，原批准永不复用。

### 4.5 任务、归属与等待

`TaskRegistry` 随 Host 进程存活，键为 Host 随机 runId + 当前 stdio MCP server 会话键（均由 Host 生成，名称/ID 不是凭据）。计划与任务只允许创建会话访问；跨 Host、断开重连、重启后工具访问一律 RESOURCE_NOT_FOUND，落盘证据由工程师本机检查。权限检查先于查存在性。计划文件锁只防竞态，不提供跨会话共享授权。

当前 `VmHttp` 是 Stateless、共用服务和密钥，`HttpCaller.Name` 为未验证文本；本期不据此创造身份。context 可走既有 HTTP Read；另外四个工具及 plan/task Resource 在这种传输上返回 CAPABILITY_MISSING（missing=`transport:stable_session`），不启动任务。现有 HTTP 工具不受影响；若以后要求跨连接共享，另提 L3 身份/会话 ADR，不在本设计实现。

任务记录固定 `{taskId,kind:plan_apply,planId,status,progress,result,error,createdAt,updatedAt,startedAt,completedAt,expiresAt}`；startedAt 为工作器离开 queued 开始编排时刻，completedAt 为终态时刻，未发生为 null；读取不更新任何时间。result 只用 `{planId,revision,resource}`，失败同时保留标准 error 和嵌套 execution。progress=`{message,completedSteps,totalSteps}`，totalSteps 为有效步数，completedSteps 只计已确定提交，不在等待审批时造百分比。

最多 8 个活跃任务；每个项目一个执行者，其余项目可 queued，活跃不淘汰。终态上限 256、TTL=completedAt+1 h；满额时只清理已过期终态，仍满则 RESOURCE_EXHAUSTED，拒绝新受理，不挤掉未到期结果。每分钟及准入清理，get 不续期；活跃 expiresAt=null。

| 事件/计划结果 | task.status |
|---|---|
| 已受理/等待工程师/执行或核实 | queued / waiting_approval / running |
| 全步 committed 且 verification=passed | succeeded |
| 全部无变化、无错误且核对通过 | succeeded（result.execution=not_started） |
| verification=failed/skipped，确定错误或 partially_committed | failed（result 仍可为 committed） |
| 显式取消，且全部结果确定 | cancelled；保留已提交步；若最后一步已完成且任务已终结，迟到取消不改终态 |
| Host 会话结束，尚未完成但已知结果 | interrupted（有机会收尾时记录；进程被杀不伪造内存任务） |
| 任一步 OUTCOME_UNKNOWN | unknown，优先于取消/中断 |

apply 默认等 90 s、最大 110 s，0 立即回 taskId；等待超时不取消。后台任务用自己的取消源，不链接请求完成/等待窗口；明确取消仍在执行的 apply 才请求安全停止，会话终止停止后续步并核实当前步。task_get 同样等终态或到时；中间状态变化不提前返回，0 可即时观察。取消 get 只取消该等待。MCP 被取消请求不回复，审计/计划仍收尾。

执行/get 等待每 5 s、审批每 15 s 心跳，复用 CallProgress 累加单调已用秒数；apply 回执之后不再向旧请求发通知，get 仅有自己 progressToken 的心跳。任务 updatedAt 只因状态/真实进度变化更新，不因心跳/轮询续期。

### 4.6 对象校验与五类差异

保存基准 B、批准预期 P、每步前 G/后 A 的 standard 证据及覆盖页集合；最终逐项 passed/failed/unknown。有必需失败则 verification=failed；全部必需项可证且通过才 passed；其余 skipped，列原因。electrical/drawing 固定 skipped。宏只验证可见函数/页，图形和内部占位符记 unknown，不能因此宣称整个模块通过。

| diff.class | 分类证据 |
|---|---|
| `planned_applied` | P 允许且 A 可证的改变 |
| `planned_missing` | P 要求但 A 未出现；未执行步带 not_executed，未读到数据则标 unknown，不能当作不存在 |
| `unplanned_in_scope` | G→A 的变化在 modifyScope 内，但 P 未允许 |
| `outside_scope` | G→A 的变化在 modifyScope 外；警报且停止后续步，不武断归咎于谁 |
| `external_between_steps` | 上步 A→下步 G 间观察到的变化，计划没有在这个间隔发写请求 |

分类先分时段，再比较 scope/P；写调用期间变化无法确定归因，单列 attribution=unknown，不能伪称外部修改。全项目 B/终态快照用于范围外筛查，逐步只覆盖 readScope/modifyScope 及已知关联页；coverage 列未观察间隔/页、skippedConnections 和属性缺口，不保证观察区外无变化。

`module.insert` 的门槛 spike（S32 后续）：同一 SafetyPoint 插入+ApplyRecord 的提交、关闭重开持久性、一次 Ctrl+Z、失败回滚、引用全范围（含页/项目属性）、每个值的实际属性读回；分别核实 bool 重载和布尔属性。成功前两个操作保持不可用，批准文案只说“各步独立事务；撤销以已验证操作为限，组合值集提交后的撤销未验证；必要时使用备份”，不承诺“每步一次 Ctrl+Z”。

### 4.7 context、查询、资源与预算

context 只接受 cabinet=default，默认覆盖全部结构；发现非默认/多柜绑定报 CAPABILITY_MISSING。复用 PackLibrary/PackStore/AssetIndex/PackVerifier 与 WorkspaceStore.ReadAsync，不建立绑定、不隐式锁版本、不回退到 latest。已有单默认柜绑定返回真实 Revision 及 canonical 排序 Locks 的 SHA-256；无绑定为 null。generation=null，不增加 Bridge 字段。每次新提取，局部范围 snapshotHash=null，coverage.pages 返回页哈希；不依赖事件/120 s 缓存（S33/A38）。

include 沿用规格；objects/facts 只来自快照，modules/rules 只描述已存在资源、类别及来源/缺能力，不解析 Office/PDF，不运行规则；config 未实现部分为空且列缺口。来源键为 `(project,kind,type,oid,hash)` 或 `(pack,exactVersion,resourcePath,manifestDigest,fileHash)`，去重后赋稳定 ID，每项只引用 ID；不抄宏 SourceProject 本机路径（A35/A37，ADR-0024）。

预算复用 ResultShaper：ASCII 每 4 字符向上取整、其他 UTF-16 字符每字一个估算 token；按最终清洗后 JSON 计算两份内容加摘要/链接。context 按 include 的固定顺序 objects/facts/modules/rules/config/capabilities，再按页名/kind/type/oid 或包/版本/路径序数排序；先保留信封和用到的 sources，再逐项加入。必要信封放不下报 INVALID_ARGUMENT 并给 minimumEstimatedTokens；没有任意固定最小值。整个条目及其来源一起截取、准确计 omittedItems；默认 5k、最大 20k，不声称模型 tokenizer 精确值。

plan_get 不访问 Bridge，expired/failed/unknown 均为查询成功的数据，顶层无 execution。concise 保留各步摘要、失败、范围外警报及结果引用；detailed 的固定流依次为 step（index）、validation（step/locator/check）、diff（step/class/locator/property），字段 `{section,index,key,value}`。较大的 step 拆成 args/readScope/modifyScope/preconditions/expected/plannedChanges/result 的独立 key 条目；超大字符串按 Unicode 标量边界分 `fragmentIndex/fragmentCount`，重组后可取得完整清洗视图，不能死循环在一个超预算条目。

limit 默认 50、最大 200；cursor 是 Base64URL 编码的 `{planId,planHash,revision,responseFormat,offset}`，严格校验同 owner/记录/修订，状态变化后 INVALID_ARGUMENT 要求从第一页读。total 为条目数；预算在完整条目间提前结束，游标指向第一未返条目。默认 5k、detailed 20k，双份计算；清洗视图不用于重算原始 planHash。

新增 `eplanmcp://plans/{planId}`、`eplanmcp://tasks/{taskId}` 只读 Resource 模板，同 owner/预算，调用同一读模型；计划资源给 concise，完整用工具分页。沿用 PackResources 已验证注册方式，不加订阅/通知、TTL/cacheScope 或 Tasks SDK 依赖；若所用协议强制要求缓存字段，先完成 SDK 本地契约验证，否则暂不注册新模板，工具路径完整可用，不伪造资源支持。

### 4.8 错误和工具注册

| reason | 采用位置及 metadata |
|---|---|
| `RESOURCE_NOT_FOUND` | 已有常量/标准行扩展到 plan/task/定位符；隐藏、清理、跨 owner 同样返回，不泄露记录存在性 |
| `CAPABILITY_MISSING` | 已有常量；编译缺 op/授权/范围读取/传输归属能力；missing 为确定的能力名数组 |
| `PLAN_STALE` | 已有常量；hash/期限/项目/资源/绑定/前置条件/批准清单变化；apply 中能力消失也按旧计划失效处理；conflicts 为 `{step,locator或scope,reason,expected,actual}` 数组 |
| `RESOURCE_EXHAUSTED` | 新常量 `ErrorReason.ResourceExhausted`；存储/任务容量或计划项目锁忙；retryable=false，metadata `{resource,limit,used}`；受理前 not_started，不自动重试 |
| 其余 | 参数/不支持步骤/重放用 INVALID_ARGUMENT；备份 BACKUP_FAILED；审计 AUDIT_UNAVAILABLE；未知 OUTCOME_UNKNOWN；已有授权/连接/锁定/策略码不造同义码 |

BridgeError.Metadata 仍是 string 字典：新的 guard/inspect 操作只在固定键 conflicts/missing/violations 中放规范 JSON 字符串；Host 仅对这些新操作按严格 schema 解析为自己的 `PlanToolError.Metadata`（JsonObject），不猜测其他操作的任意字符串。顶层 error 带 correlationId/eplanMessages 和正确计划 execution；完整冲突保存在计划，返回摘要有截断计数/查询引用。

`PlanToolResults` 的 outputSchema 显式声明终态/任务回执/失败结果分支；失败 `isError=true` 同时保留计划摘要和 error 的 structuredContent 及同内容文本，不更改共享 ToolResults。Read 查询错误不带 execution；找到失败任务/计划时 isError=false。

实现时同步 `Contracts/ErrorReason.cs`、标准 mcp-tool-design 6.2/6.3/8、schema/往返/ExecutionInvariants 和工具契约快照；扩展的是计划五值校验器，不把 partially_committed 加入单步 ExecutionState。五工具在 McpServerSetup、ToolProfiles(full)、ToolUsageCatalog/Usage 注册；不增加 core 条目，逻辑归 workflow 工具集，不顺带实现 ADR-0022 全套动态发现。apply 四注解固定 false/true/false/false，运行时 tier 仍取步骤最高级；CI 显式登记这个计划例外。

## 5. 安全与审批通道

Workbench v1 的 ApprovalAskParams 只有 Changes/BackupFolder/Fingerprint/ArgumentsHash 等，没有 PlanId/Summary/ChangesPath；WorkbenchChannel 只发送 ApprovalTexts.For(...).Changes，忽略 Summary/Note。ProjectText 最多显示 200 行、每行 400 字符；协议严格拒绝未知字段、未知枚举，帧最大 1 MiB。不能把完整清单或关键限制只塞进 Note。

无需改 IPC 的基线：PlanApprovalView 将 ID/hash、柜、最高级别、备份位置、逐步/逐页摘要、Destructive 标记、预测模式、撤销限制、完整 changes.txt 路径和文件 SHA-256 都放入既有 Changes。元数据/限制/路径排在最前，摘要不超过 200 行、单行≤400；路径过长不能完整显示则拒绝为 INVALID_ARGUMENT，不截断成可误导的路径。清单可在界面容纳时全部内联，超出时明确标“摘要，完整清单在文件”，路径为纯文本、无自动打开行为。生成文本后若仍超帧则审批前拒绝，不偷偷丢条目。

EPLAN 的 ApprovalAskArgs 已有 Title/Summary/Changes/Note，可显示相同元数据、每步摘要及前 50 条变更，完整清单引用同一路径；两渠道共用绑定/期限/拒绝默认值。changes.txt 是完整、清洗但不截条目的分组文本（不可打印字符用可见转义），其 SHA-256 存 definition.changesDigest；文件自身不含 planHash，避免循环哈希。审批后重算核对；dry-run 不创建它，编译时已发布的文件可只读复用。沿用 250 ms 发现、送达 Workbench 后断开不换渠道、独立暂停确认、offline 无审批渠道等规则。

这可承载“整体摘要+完整清单路径”，不能声称 v1 已有 ADR-0023 所设想的完整 Workbench 差异界面。富界面/新增传输字段的可选方案只放 ADR-0027；基线不依赖它。另一个限制是 WriteExecution 只有四值：计划不发送 v1 approval.outcome（直接 Dispose 该 outcome 连接，现有 QuestionExecution.NotReceived 不推断执行结果；审批前清单明确结果查 plan_get），尤其禁止把 partially_committed 映成 unknown；后续第五值通知须先接受 ADR-0027。

实现安全文档新增两条威胁（按当时最大编号顺延）：计划文件/证据被替换，及预测/执行越出批准范围；分别由可信编译摘要+原子读取、步内 guard+封闭预测器+审计落实。包只读/哈希校验、路径边界、提示注入清洗沿用 SEC-05/08/12/19/35/36，不改变进程拓扑、支持版本或审批权限。

## 6. 备选方案

| 否决方案 | 原因 |
|---|---|
| 循环 WritePipeline 或给它加 skipApproval/hooks | 产生逐步审批，或让共享写路径获得可绕过入口；独立编排器更容易审查 |
| Host 先查一次再直接 execute | 旧 execute 的指纹未覆盖全部持久前置条件；须同一主线程调用内 guard/plan/execute |
| 一个外层事务/自动 Ctrl+Z 补偿 | 宏和符号事务行为不同，崩溃留部分写入，且 S32 未验证提交撤销（A13/A15） |
| 用 clientName/IP 或 ID 作为跨请求身份 | 当前 Stateless HTTP 不证明客户端归属；不能在 L2 增加共享权限 |

## 7. 验证与实施顺序

各 PR 只完成表中一件事；估算为生产代码+文档增删行，测试/生成 schema/契约快照不计。目标约 200、绝不超过 400，接近上限先拆；新能力仅在依赖齐备后注册，不用中间 PR 暴露半成品。

| PR | 一件事（估算行） | 依赖 | 测试；EPLAN 门槛 |
|---|---|---|---|
| P01 | 计划定义/五值结果合同（190） | 本设计、ADR-0028 Accepted | Small：序列化、聚合五值/无效步/不可重试 |
| P02 | canonical 与可信摘要（180） | P01 | Small：排序、Unicode/数字/null/默认值、篡改；跨序列化器向量 |
| P03 | PlanStore 原子发布/状态 CAS（200） | P02 | Small：状态机；Medium：断电点模拟、并发读写/替换、审计失败 |
| P04 | 计划容量/保留与 config/doctor（190） | P03 | Small：TTL/配额；Medium：未知记录保留、链接拒绝、容量预留 |
| P05 | 快照组装与稳定基准读取器（200） | P01 | Small：集合哈希；Medium：分块/缺页/中途变化；Large：S31 登记属性双读 |
| P06 | scope/前置条件与影响闭包（200） | P05 | Small：新增/删除/副本/跨页；Medium：无页功能拒绝；Large：A13 跨页重命名 |
| P07 | 七操作 args/默认值和依赖验证（200） | P06 | Small：限额/JSON Pointer/禁止选择器及 op；Medium：schema 快照 |
| P08 | macro inspect 只读入口（190） | P07 | Small/Medium：SHA/完整性/非默认变体拒绝；Large：variant=0 与函数内容核对 |
| P09 | 独立步编译与稳定语义预览（200） | P07 | Small：丢弃会话指纹；Medium：既有七项 plan、名称/影响范围 |
| P10 | 两种依赖预测器（190） | P08/P09 | Small：受限名称模式；Medium：产物引用/重叠拒绝；Large：空页宏预测与实际 |
| P11 | `eplan_workflow_plan` 注册（180） | P03/P04/P09/P10 | Small：注解/预算；Medium：MCP 编译/落盘、快照；Large：编译不改工程 |
| P12 | `eplan_plan_get` 与计划 Resource（190） | P11 | Small：分页/片段重组；Medium：修订失效、owner、离线、SDK 资源合同 |
| P13 | `project.backup` 独立操作（180） | P06 | Medium：空目录/失败/取消/页数；Large：备份可打开、0 页项目、当前项目状态 |
| P14 | 提取纯 WriteChangeLines（100） | 无 | Small：输出等价；Medium：既有 write harness，禁止新 hooks |
| P15 | Host 计划审批视图与现有两渠道适配（200） | P12/P14 | Small：绑定/文本/长度/无撤销承诺；Medium：真实 Workbench 管道、断线不回退；Large：EPLAN 计划对话框 |
| P16 | Bridge 步内 guard 操作（200） | P06/P09 | Small：白名单/约束；Medium：规划后插入修改必拒绝；Large：主线程 guard |
| P17 | guard 接入既有七项 execute 与命名 factory（200） | P13/P16 | Medium：恰一事务/每步 Destructive 备份；Large：七项各自提交/回滚/撤销；先完成下文 U1 |
| P18 | TaskRegistry 与等待/归属（190） | P01 | Small：状态/TTL；Medium：可控时钟、容量/get 取消不取消任务、Stateless 拒绝 |
| P19 | PlanOrchestrator 准入/审批/初备份（200） | P04/P13/P15/P18 | Medium：一次消耗/0或1审批、暂停独立确认、备份失败零 execute |
| P20 | 顺序执行与计划审计/停止（200） | P17/P19 | Small：结果表；Medium：第 k 步失败/取消、审计写失败、无重放；Large：部分完成备份 |
| P21 | 中断核实与 PlanSettlement（190） | P20 | Medium：延迟提交/断线/Host 重启、outcome 过期、owner 丢失；Large：受控超时，沿用 S8 安全条件 |
| P22 | 对象校验器与五类 diff（200） | P20 | Small：分类/归因 unknown；Medium：覆盖缺口/校验失败仍 committed；Large：实际值与差异 |
| P23 | `eplan_plan_apply`/`eplan_task_get` 注册（200） | P12/P18/P21/P22 | Small：动态 tier/结构化错误；Medium：MCP 等待/取消/查询闭环、契约快照；Large：整体审批计划 |
| P24 | 最小 context 聚合与工具（200） | P05/P06 | Small：预算/稳定排序/来源去重；Medium：缺配置/绑定/多柜拒绝；Large：按页哈希与来源一致 |
| P25 | 完整任务验收与使用文档（140） | P23/P24 | Medium：4.17 场景 3/4/5/7/8；Large+Agent：新建后打开、真实修改、手工改动 PLAN_STALE、选择改变、等待审批、缺能力 |

Large 全用示例材料的 scratch 副本，按 testing.md 在相关写工具/Bridge PR 合并前实跑，结果附 PR；无 EPLAN 的检查必须用 Small/Medium，不能以测试代码存在代替实测。不得在本设计步骤运行 EPLAN。

## 8. 发布与兼容性

仅新增工具/操作，不改现有单步工具与 Workbench v1；实现 PR 同步标准、用法、契约快照，按新工具次版本发布。本次仅设计，**CHANGELOG-neutral：不新增 CHANGELOG 条目**。回退时不注册五个工具/新操作，保留审计、计划证据和备份，不自动删除工程数据。任务持久化、事件、插件分别仍属阶段 5/4/5，不借本期提前实施 ADR-0025/0026。

## 9. 负责人事项与待验证门槛

| 项 | 负责人/时点与不满足时的行为 |
|---|---|
| L3：计划落盘 | ADR-0028 已于 2026-10-23 由项目负责人接受 |
| L3：Workbench 扩展 | ADR-0027 仅 Proposed、可延期；本期摘要+文件路径可运行，不改 IPC，不伪发第五值通知 |
| U1：新编排的撤销与初备份 | 新 spike `S35-plan-step-undo`（先测既有路径，P13/P17 Large 再测最终组合）：scratch 验证 CopyProject+进程内 CountPages 不改变目标选择/打开状态、持 FileShare.Read 宏句柄仍能读取/插入，以及命名 factory 下七项提交后按步撤销、回滚不留空步；失败的操作不注册，不移除保护降级放行，完整任务发布门槛未通过 |
| U2：模块/值集 | 新 spike `S36-module-commit` 先补 4.6 的 S32 缺口，再另写 module 引用覆盖合同和小 PR；当前返回不支持，不自动用重命名替代属性应用 |
| U3：验收范围 | P25 先交付 M6 4.16 允许的“一处真实修改”闭环；Q12 的“参考电机回路→模块生成”仍待 U2/模块 PR，不能把基线闭环报告为该样例或整个 M6 已完成 |
| 环境限制 | 只依赖既有支持矩阵和已测版本；旧版本升级、复杂宏/布尔值集、事件范围、图纸导出稳定性均不推断支持（S30–S33、A32–A39） |

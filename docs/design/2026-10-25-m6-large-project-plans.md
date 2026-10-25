# 设计文档：M6 大项目计划的名称冲突域与局部证据

| 项 | 内容 |
|---|---|
| 状态 | In Review（设计修订；本步仅文档，未运行 EPLAN） |
| 级别 | L2；保持 ADR-0023 的审批、事务、备份及 ADR-0028 的落盘合同 |
| 作者 | asckye |
| 关联 Issue | — |
| 相关 ADR | [ADR-0023](../adr/0023-plan-approval-and-backup.md)、[ADR-0028](../adr/0028-plan-record-format.md)（均 Accepted） |
| 修订依据 | [阶段 2 设计](2026-10-23-m6-phase2-plans.md) 4.2–4.6、D01/D02/D04；[workflow_plan](../tools/eplan_workflow_plan.md)、[plan_apply](../tools/eplan_plan_apply.md) |
| 最后更新 | 2026-10-25 |

## 1. 背景

2026-10-25 测量的示例项目有 321 页、5,543 个 standard 对象，平均约 17 个/页，最密一页 743 个。`PlanImpactReader` 把建页、设备改名、插宏及两种重编号的 readScope 设为 `PlanScopeRules.Whole`；超过 10,000 对象即 `/readScope/objects`。平均密度下约 600 页已触顶，1,000–10,000+ 页项目无法使用这些计划步骤。单次写工具不受这个计划限制影响。

`WorkflowCompilation` 先用全项目快照取得身份，再由 `PlanBaselineReader` 双读全项目；局部读取路径也为名称占用额外读取全项目。`PlanStepGuardOperation` 在一个主线程 LockingStep 内重新读全部 readScope，限 10,000 对象/10 s，且误把名称域中的功能也计入对象限额。P20 `PlanOrchestrator.Execution` 每步前后又全读；P22 `PlanVerifier` 将证据标成 WholeProject，依赖全量比较。

原设计已将名称占用单列为 `NamesHash`，封闭联合已有 `absent(kind,name)`，4.6 要求逐步只覆盖读写范围和关联页。本修订将这些约定落实，并明确降低内容观察范围后的证据边界。[api-notes](../eplan/api-notes.md) A13/A14/A17/A18、A32–A36、A39/A41 是 API 依据；未经实测的性能和生成方法均列为门槛，不当作事实。

## 2. 目标与非目标

- **目标**：三个常用 Write 步骤的对象明细量随影响页数增长；全项目只枚举名称/身份元数据。3,000/10,000 页夹具能检验编译、guard、执行和范围约束。
- **目标**：跨页及无页功能不漏掉；名称碰撞、闭包新增/删除、越界观察结果可检验；未知覆盖如实报告。
- **非目标**：扩大步骤目录、自动恢复/补偿、弱化审批或备份、增加缓存/事件代次、修改持久格式、支持矩阵或主线程调度模型。

## 3. 方案概述

把名称冲突域与对象内容域分开。编译双读“精确影响页 + 完整名称集合 + 同名功能闭包”；guard 在原同一次主线程调用内重新核对必要名称和局部内容；执行前后取局部证据并作全项目名称筛查。两种重编号保持保守上限。

```mermaid
flowchart LR
    N[页名/功能名元数据] --> C[编译双读与闭包]
    S[影响页 standard 内容] --> C
    C --> R[既有 schema=1 计划与证据]
    R --> G[步内名称检查与局部 guard]
    G --> W[原单步事务与备份]
    W --> V[局部前后证据与名称筛查]
    V --> O[五类差异与未观察覆盖]
```

| 决议 | 对原设计的修订 |
|---|---|
| LP01 | D01/4.2–4.3：三个 Write 步骤不再用 Whole 充当名称冲突域；对象限额不限制名称清单 |
| LP02 | 4.2：两种重编号仍全项目读写、最后有效步；保留 50 页/10,000 对象限制 |
| LP03 | D02/4.1、4.3：摘要算法/记录结构不变，只改变实际取证集合；现有 compilerVersion 区分语义 |
| LP04 | P20/P22、4.6：局部逐步取证，名称筛查补充低成本观察；未读内容不能声称未变 |
| LP05 | 4.4/预算：不延长 guard、备份或计划时限；性能门槛不满足时明确停止 |

## 4. 详细设计

### 4.1 完整名称清单与成本

定义 N 为全项目去重的 `(kind,name)` 占用集合，kind 只取 page/function；保留原名称、Ordinal 排序及既有 `PlanSnapshotHashes.Names` 算法。碰撞检查沿用 OrdinalIgnoreCase；功能名还检查 `候选名 + ":"` 前缀。散列不做大小写折叠。空功能名不作为可申请名称；所有额外表达、辅助功能和无页功能的非空名称都要包含。

新增只读 `plan.names` **invoke 内操作**，输入为 `{project}`，返回项目名称/规范路径/辅助projectGuid、complete、去重名称和 NamesHash；不加 JSON-RPC 方法、传输信封字段或协议版本。沿阶段 2 新增 `plan.functions`/guard 操作的 L2 边界，握手声明缺项即 `CAPABILITY_MISSING`。用既有project.list解析项目路径，再由plan.names只读PROJ_GUID登记属性补辅助身份；取消 `SnapshotAsync(project,null)` 的身份读取，S为空也不为GUID读页内容或全部项目属性。

Bridge 内复用 `IPlanFunctionReader` 的完整枚举：`Eplan.EplApi.DataModel.DMObjectsFinder.GetFunctions(null)` 补无页功能，`GetPages(null)` 加每页 `AllPlacements.OfType<Function>()` 补额外表达，按 `(type,oid)` 去重。只取名称、身份、isMain、page，不访问功能属性、部件或连接。页名直接来自 GetPages。不能仅靠 finder 的无过滤结果，也不能用截断的 device Details。

`plan.names` 一次返回 N，替换编译中的全项目 standard + 分页 `device.list(includeAuxiliary=true)`；后者每一页结果都可能重新扫描全项目并读部件计数，不适合作大项目名称源。guard 直接调用同一内部枚举器，一次枚举供多个 absent/闭包检查使用，不经嵌套 router。跨调用不缓存。

P=页数、F=功能数、M=放置总数、K=本步明细对象数。名称扫描 O(P+F+M)，存储 O(|N|)，不读取 M 个对象的 standard 属性；局部内容 O(K)，连接枚举另见 4.5。**名称扫描仍非 O(1)**。A32 的 GetPages 约 0.01 s、2,876 功能枚举 0.01–0.04 s、名称等字段约 0.06 s，不能代表完整合并枚举耗时。

| 读取 | 1,000 页线性外推 | 10,000 页线性外推 | 含义 |
|---|---|---|---|
| 页列表（A32 基数 321 页） | 约 0.03 s | 约 0.31 s | 未计排序、序列化及传输 |
| finder 功能名称等字段 | 约 0.2–0.3 s | 约 2–3 s | 不证明额外表达完整 |
| 全放置枚举参考上界压力（A32：0.6–0.8 s） | 约 1.9–2.5 s | 约 19–25 s | 非同一 API 路径，作为必须测的风险，不能据此宣称 guard 达标 |

名称枚举需有逐页/逐批取消和时钟检查。暂定独立资源上限为 250,000 个不同占用名、N 的 UTF-8 JSON ≤8 MiB，且仍受 16 MiB IPC 帧及证据配额约束；超限 `CAPABILITY_MISSING`，完整性为 false 不准继续。它们是新操作的有界读取合同，不修改单次写工具。任何命名扫描超过 guard 10 s 都在写前 TIMEOUT；不提高时限来取得“通过”。

### 4.2 三个 Write 步骤的范围及 guard

| 步骤 | 编译 readScope / modifyScope | 前置条件 | 同一步内 guard 必读 |
|---|---|---|---|
| page.create | readScope 为空集；modifyScope 为 1–50 个新页名 | 每个新页 `absent(page,name)`；空集 members | 当前完整页名列表作占用检查；不读任何已有页内容 |
| device.update | readScope 为旧完整名称的**所有**同名功能所在非空页；modifyScope 为这些功能的精确定位符集合 | 局部 members/pageHash/objectHash；新完整名称 `absent(function,name)` | 同名功能身份/页闭包、新名占用清单、readScope 页内容及无页定位符内容 |
| macro.insert | readScope/modifyScope 为唯一目标页；预测新页在编译时尚不存在，运行时按前步产物绑定 | 目标页 members/哈希及空 Circuit 条件；每个候选功能名 `absent(function,name)`；宏 resource | 目标页全部 standard 内容、全项目功能名清单、宏文件哈希及封闭宏约束 |

page.create 的“页名占用检查”就是基准 N 的 page 子集检查加步内新鲜页列表检查，不是要求页内容摘要不变；不新增 namesHash 前置条件 tag。已占用名称在编译时沿用既有碰撞错误，批准后出现则 `PLAN_STALE/not_started`。空 scope 始终是空集，禁止以 null/空数组触发全项目快照。

device.update 先用 `plan.functions(names:[旧全名])` 取得全部主/辅、同名主功能和无页项，不使用主功能页或前 20/50 条详情推断闭包。≤50 个非空页且显式功能定位符合计≤200；完整匹配结果仍受 plan.functions 的 10,000 条返回上限，complete=false 即缺能力。步骤新名取真实 `*.plan` 解析后的完整名，不能直接把短 `-KF2` 当冲突键。

device.update 的 modifyScope 使用 `pages=[]、structures=[]、locators=全部功能`，避免范围三条件取交集时把无页项滤掉；受影响页限额独立从定位符页集合计算。每个功能保留 objectHash，不能因明细多而退化到只有页 members。编译双读、apply 初检及步内均比较完整 `(kind,type,oid,name,isMain,page)` 集合；新增同名功能即使在未读页或无页、且 N 没变化，也必须 stale。

闭包检查在类型化 `plan.step.execute` 内由 device 参数与已有 guard 的功能 objectHash 定位符推导预期身份/页集合，比较新鲜内部函数清单；isMain变化由包含Main的standard对象哈希证明，不从没有Main字段的locator猜测。不增加 wire 字段或前置条件 tag。实际 renamer 的完整成员必须与它一致，数量相同不充分。A13/A17 要求全部主辅一起改；原单次工具的短详情及会话指纹不作为持久成员证据。

macro.insert 只开放现有限制：空 Circuit、MultiLine、variant=0、numbering=keep。用完整 macro.inspect 描述与目标页结构推导所有非空功能名（含辅功能、端子、PLC），去重后≤200；`?` 是实际名称字符，不能用查询通配符查不存在。结构不能确定或名称约束不能化为有限候选时 `CAPABILITY_MISSING`。A14 的 PLC 保留外部结构不能假定被页结构替换；原封闭预测器不支持者仍拒绝。

先前步骤引入的名称也进入 Host 的预测占用集合；同一计划两次申请同名，或一个步骤产生后续 absent 要求不存在的名称，编译即拒绝。只开放原有 page.create→page.update/macro.insert 预测，移除 `PlanStepCompiler` 为碰撞域无条件拼入所有前序新页的逻辑；不借缩小 readScope 放行新的重叠依赖。

### 4.3 无页功能

无页功能占用名称由完整名称扫描覆盖，**不需要**为它读取虚构页面；即便本步只在其他页插宏/建页，也不得漏掉无页同名冲突。plan.functions 返回 page=null 是有效成员，不再自动触发 `snapshot:functions_without_page`。

若无页功能参与 device.update：使用已有 `object.resolve` 逐定位符双读 standard 哈希和描述，并建立 `objectHash`；guard 的 finder 同样从 GetFunctions 补齐未放置功能，读取时调用现有 FunctionObject(...,page:null)，检查名称/类型/oid/主辅身份和哈希。当前 `EplanObjectFinder` 只找页放置，须补齐并实测后才启用此能力。额外 objectHash 不并进页 members，也不伪造 pageHash。

前后无页证据使用现有 `ObjectResolveData` 和 `PlanFunctionsData` 的内容寻址 JSON；编译时哈希/定位符留在既有 preconditions/sources，执行证据引用留在既有 coverage.evidence 字典。after的resolve请求只将名称按批准的新名转换，项目/type/oid仍固定，ActualPage必须仍为null；不能拿旧名称必然stale的结果当作对象消失。`PlanVerifier` 内存输入合并这些读数，明确验证每个功能的新名称及闭包；object.resolve 只有描述与哈希，不能声称其全部属性差异已解释，其他登记属性的变化缺少可比值时列 unknown。

无页取证/改名/提交后读回未通过 Large 前，**仅参与写入的这种步骤** `CAPABILITY_MISSING`，missing=`object.resolve:functions_without_page`；名称占用扫描仍必须包含无页项。不能为了发布漏掉它或批准后才发现无法验证。page.delete 的无页 orphan 内容闭包仍按原合同 fail closed，本修订不扩大其能力。

### 4.4 两种重编号

决定保留 whole-project readScope/modifyScope，不采用局部猜测。项目必须≤50 页、standard 内容≤10,000 对象，显式目标页 1–50，最后有效步；device.renumber 必须显式方案，不能依赖上次 UI 状态。50 页是**全项目修改覆盖**限额，不只是 args.pages。

A18 证明回滚/撤销及 PostNumerate 跳过已占名，不证明重编号的封闭传播范围；有些 `?` 不被编号，端子后缀也不等同设备编号。审批不能承诺精确映射。名称集合不足以证明整步内容影响，尚无可验证的有界预测器，故不缩小范围。

输入 pages>50：`INVALID_ARGUMENT`，pointer 指对应 `/steps/i/args/pages`。项目超过可证明覆盖上限：`CAPABILITY_MISSING`，missing=`plan.renumber:whole_project_guard`，列实际页数/对象数与限制；对象上限已知时可列 `/steps/i/readScope/objects`。提示使用 `eplan_page_renumber` / `eplan_device_renumber` 单次工具，其原审批、备份、预算和 API 限制仍适用，不承诺任意大调用成功。应先看页清单拒绝大项目，避免先全读再报错。

### 4.5 编译双读与 ADR-0028 兼容

1. 用项目元数据与完整名称清单发现目标；plan.functions/删页闭包/宏描述确定本计划所需的现有页并集 S。发现阶段不作为稳定基准。
2. 第一轮读取 S 的 standard 内容、N、同名功能闭包及必要无页定位符；在其间编译真实规划/预测与资源/绑定。
3. 第二轮读取**相同 S**、完整 N 和闭包/无页哈希，比较页、成员、名称、身份、缺页、资源及绑定。闭包变化要求新增页时直接 PLAN_STALE，不自增范围后接受；不自动重试。
4. 全部为建页时 S 为空，构造既有 PlanSnapshot 的空 Pages/空集合哈希，不发送 `project.snapshot`。预测新页只在运行时按获批变化转换基准。

`PlanBaselineReader` 不再为了 N 调用 `AssembleAsync(project,null)`；`WorkflowCompilation` 不再传 null 作全项目双读。`PlanApplyChecks` 的 dry-run、审批前及审批后重核对必须复用 S/N/闭包算法，不能留下全项目重读；每步只按原约定检查资源/绑定并作步内 guard。双读仍只是稳定性筛查，不能防 A→B→A，也不提供数据库修订号。

当前 `EplanSnapshotReader.Source.ReadPage` 即便只取一页，也懒加载 `GetConnections(null)` 并按页归组。因此本次消除的是全项目**属性内容**读取，保留 O(C) 连接元数据枚举成本；每个 source 至多一次，报告它的耗时/数量。按页 ConnectionsFilter 的完整性未证实前不替换，不把局部快照宣传成完全 O(K)。A36 的一次 Properties/PropertyValue 释放、A39 不 Dispose 返回 StorableObject 必须保持。

| 现有字段/算法 | 修订后的值与兼容规则 |
|---|---|
| 外层 schemaVersion、definition.schemaVersion | 仍为 1；definition/state/步骤字段、六种 tag、canonical、planHash 输入规则均不变 |
| compilerVersion | 从既有字符串 `"1"` 改成 `"2"`，apply 明确拒绝不支持版本；旧记录可查询，重新编译后才按新语义执行 |
| snapshotFormatVersion/profile/format | 不变；standard 属性与对象/页哈希规则不变 |
| baseline.Snapshot / Digests.Pages | 仅 S 的完整页证据/ComposedHash；局部 SnapshotHash=null；不冒充全项目哈希 |
| Snapshot.Members / MembersHash / Digests.Objects | 只散列实际 S 内对象的既有 key+内容 hash；各步 members 对其精确 readScope 计算，空集也有确定 hash/count=0 |
| baseline.Names / NamesHash / Digests.Names | 仍是完整 N / 原 Names 算法；包含无页名、额外表达；改变读取来源，不把名称散列塞进 members |
| contextRef / sources / evidence | 仍 SHA-256 引用、既有字段；无页 objectHash 与 sources.Hash 使用既有对象哈希语义 |
| state.result / coverage | 沿用现有结果与 entries/evidence 字典；不新增顶层字段、持久类型或 union tag |

基准仍序列化现有 PlanBaseline，Names 只存一次；无页读数存已有 ObjectResolveData/PlanFunctionsData。逐步名称扫描只保存既有 PlanDigests（只填 Names，其余 null），page 子集和完整 N 各用一份，引用键如 `1:before:pageNames`、`1:after:names`。同内容按哈希复用；现有 `PlanStore` 已接受内容寻址 JSON，勿定义新的落盘信封。完整扫描集合只在内存比较，差异摘要沿用既有 PlanDiffEntry。

因此不需要 Proposed ADR。若实现发现必须给 PlanSnapshot 增加无页数组、为 preconditions 增加 tag、改哈希编码/记录 schema、配额或目录布局，则该部分转 L3：先单独 Proposed ADR，负责人接受前停下，不能以 compilerVersion 绕过格式决策。

### 4.6 P20/P22 的逐步与范围外证据

对每步固定观察页 E = readScope.pages ∪ modifyScope 页投影 ∪ 已知关联页。关联页来自完整同名功能/删页 orphan 闭包及已解析连接端点；不递归扩展到整项目，发现必需写影响超出批准集即 stale。E≤50 且明细≤10,000；编译 S 可以跨独立步骤超过 50 页，按≤1,000 页请求分批并核对完整性。缺证据的终态必须保留已知执行结果。

before/after 只读取 E，另取必要无页对象、页列表及 N 摘要。建页 before 没有新页是批准的缺席，after 必须读回；删页 after 的 missingPages 只有已批准删除者可作删除证据，其他缺页仍是异常。依赖页、重编号新页名按获批转换和真实 oid 绑定，不能因名称改变把整项目标成已覆盖。

`WholeProject=false` 是局部证据的默认；只有完整全项目取证且明确证明页集合完整才为 true。`Covers(null)` 不再用 WholeProject 证明无页对象覆盖，而由显式无页定位符结果判断；范围内 orphan 校验也须使用完整功能闭包，不能依赖这个捷径。

低成本筛查在每步前/后取页名子集摘要与完整 NamesHash，比较本步批准名称增删后的预期集合；中间步骤按上一 after→下一 before 比较。同名多实例的新增不改变去重 N，由 4.2 的成员身份检查补足。执行后按需 plan.functions 复核新增/改名的完整函数成员与目标页；只读新增定位信息，不把实际结果整体接受为批准预期。

摘要意外改变时重新枚举一次名称作差异定位，不重发写入；写期间按可证明位置归入unplanned_in_scope/outside_scope，间隔归external_between_steps，写请求期间 attribution=unknown。只能定位名称域而无对象内容时，用既有diff的scope目标与摘要报告范围外筛查警报，不伪造对象属性。无法区分“旧名仍有其他占用”时不得简单从 N 删除旧名，按完整当前成员及批准成员转换计算。多页/多调用扫描不原子，筛查结果有时间窗口。

默认不再取全项目 B/终态内容快照。小项目可补充初始/最终完整筛查，但仅当≤50 页、估计≤10,000 对象且剩余时间/证据预算足够；读取后超过条件则放弃完整覆盖标记。它不替代逐步 E，也不能把最终读数补作中间时段的观察。该优化不阻塞核心发布。

P22 必须在既有 coverage 的 UnobservedPages/PropertyGaps/window 中报告：

- 全项目页列表减去该时间窗口两端均有 standard 内容的页；用名称元数据就能列出，不能只从局部基准推导项目全集。
- 范围外未读对象属性、同名实例数量/位置/主辅变化（除显式闭包）、未观察间隔，以及无页对象未读属性。
- graphics/texts/placeholders、无页/3D 连接、skippedConnections 和宏预测原有缺口；只扫名称不能将这些标为 passed。
- page/function 名称筛查通过仅说明名称集合吻合；范围外改描述、改部件或移动同名对象可能完全不可见。

万页清单不能在每步state中重复展开。完整coverage使用现有PlanVerificationCoverage序列化到内容寻址证据，相同清单复用；既有coverage.evidence字典用`1:write:coverage`等键引用，entries只保留预算内摘要。plan_get沿现有分页条目/片段机制展开这些引用；摘要未内联的页不代表已观察，必须显示完整证据引用。状态摘要仍≤1MiB，完整清单仍计入13MiB证据预算，不增加持久结构。

五类 diff 保持原分类：有内容证据才产生 planned_applied/planned_missing/unplanned_in_scope；未读到不是不存在。必需预期有 unknown 则 verification=skipped，有失败则 failed；所声明局部必需检查全通过可 passed，但 coverage 仍列范围外未观察。观察到 outside_scope 后停后续步；verification 失败不改写 committed，证据丢失不等于 OUTCOME_UNKNOWN。

### 4.7 限额、预算和时间估算

| 项 | 决定 |
|---|---|
| 意图 | 1–20 步、参数≤256 KiB；输入/有效依赖限制不变 |
| 内容 | 每步影响/观察≤50 页、guard 明细（含页对象及单独 resolve）合计≤10,000；最多 200 显式定位符；密页不能按平均值放行 |
| 名称 | 宏候选≤200；占用清单按 4.1 单独限额，**不把全项目功能总数计入 10,000 明细上限** |
| guard | 同一次主线程 LockingStep，10 s；含必要名称扫描、闭包及内容规范哈希；写前未完成即 TIMEOUT/not_started |
| snapshot | 保持每块默认 10 s、最大 20 s、名义 10,000 对象，页不可拆；现实现可能单页/最后一页越限，Host/guard 须按实际总量拒绝，不截断通过 |
| 执行 | 原工具预算、初备份 60 s（Host 75 s）、Destructive 独立备份、总墙钟 15 min 含审批不变；下步预算不足不发出 |
| 存储 | 保持 1,000 份/512 MiB，单份16 MiB：definition 2、状态摘要1、证据13 MiB；24 h/30 d 与预留规则不变 |

编译预估 S、N、无页证据和结果空间，不能仅以页数估配额。逐步不重复落盘全局名称数组；局部证据按内容寻址复用。20 步最坏 40×10,000 对象仍可能远超 13 MiB，**不承诺上限可同时取满**：编译已知超限 RESOURCE_EXHAUSTED；运行中达预留上限停止后续步、保留已提交事实，verification=skipped 并列未采集覆盖。

下表为待测工程估算/验收目标，不是 2026-10-25 实测。固定 3 个独立 Write 步骤（建页、跨两页改名、另一个空页插宏），每步≤1,000 明细对象，密度近示例；execute 含初备份和前后证据、不含人工审批时间。300 页用近似示例基线，321 页原始计数同时保留。

| 项目页数 | 编译双读目标 | 单步 guard 目标 | 三步 execute 估算 | 名称扫描/备份风险 |
|---|---|---|---|---|
| 300 | 1–5 s | 0.3–1.5 s | 3–15 s | A18 备份基线约 0.6–0.7 s，仍须测新编排 |
| 1,000 | 2–10 s | 0.5–2.5 s | 6–30 s | 不把 list 分页反复扫描代价带进估算 |
| 10,000 | 10–60 s | 3–8 s，硬限10 s | 30–180 s | 完整放置扫描可能19–25 s；备份可能超60 s，任一发生即不达标 |

测量模型为 `Tcompile≈2×(Tnames+Tscope+Tclosure)+Tplan`，`Tguard≈必要名称扫描+Tscope+闭包`，`Texec=备份+各步(before+guard+原执行+after)+审计/证据`。page.create 的 guard 不扫描功能名；其他两项不能套用其低耗时。连接元数据的 O(C)、宏文件与恢复核实另外计时。

维护者在同一 EPLAN 构建/变体/硬件上每规模各做 5 次冷启动、20 次热运行，报告 median/p95/max，不剔除超时；冷/热分列。分别记录 Bridge 主线程扫描/属性读取/哈希、Host IPC/序列化、备份页数/字节/耗时、峰值工作集、证据字节和端到端时间。目标 p95 guard≤8 s 且每次<10 s；若失败，先优化无属性枚举或查证等价 API，不能靠缓存、不完整列表或延长锁来通过。原生调用不能强行中断，超预算返回后禁止写入，记录实际阻塞时长。

## 5. 安全与数据完整性

Read 编译与 Write/Destructive 分级不变；一次整体审批、临时指纹/批准清单、备份、每步事务/命名撤销、首次失败停止、outcome 核实及写入暂停全部沿 ADR-0023。新名称/闭包检查与重规划/execute 仍在同一次主线程调用，不拆锁、不扩大批准修改域，不把后验 unknown 当作允许未知写范围。

缩小的是内容观察面，批准前的清单须说明“局部 standard 验证 + 全项目名称筛查，其他内容未观察”，沿现有 Changes/changes.txt 展示，不改审批渠道或绑定。项目名称、宏名称等均按现有清洗规则作为数据输出。无页/额外表达的读取守 A36/A39；不调 A33 需要写锁的 GetAllCurrentValues，不引入系统主数据写入。

本设计为 L2 的范围/取证实现修正；没有需负责人接受的新 L3 决定。若要取消初备份、延长锁/改线程调度、允许无法证明的修改域、改落盘合同或支持版本，必须另提 Proposed ADR 并停止该部分。

## 6. 备选方案

| 方案 | 否决理由 |
|---|---|
| 把明细 cap 提至百万、guard 延长数分钟 | 全项目属性成本和主线程阻塞仍线性；证据配额马上失效 |
| 只读主功能页 / 只用 finder 无过滤结果 | 漏掉跨页、无页及额外表达；A13/A17 会造成改名不完整或合并 |
| 用修改日期/事件或跨调用缓存免读 | A36 不证明可靠项目修订，不能支撑持久前置条件 |
| 所有范围外内容都不取且不报告 | 名称筛查与 coverage 缺失会把局部成功误述成项目完整校验 |
| 立即给重编号设计局部预期 | A18 缺传播闭包与精确映射证据，先保留受限能力与单次工具出口 |

## 7. 测试与验证

### 7.1 大型 scratch 夹具

只从安装包自带的 `EPLAN_Sample_Project` 派生，不接受客户项目/任意 Source。沿 `eng/scripts/eval-fixture.ps1` 的复制方式，从已核验的安装示例制作隔离种子；不调用其 WriteCopies 的 scratch 外副本作为写目标。生成器建议 `eng/scripts/large-plan-fixture.ps1`，元数据说明放 `tests/fixtures/large-plans/README.md`；项目二进制、生成宏及原始绝对路径均不入库。

实际项目位于 `<scratch>\fixtures\large-plans\<generation-id>\{seed,p300,p1000,p3000,p10000}\`；每次写测试在同根的 `runs\<run-id>` 使用独立副本。记录安装示例文件摘要、EPLAN 构建/变体、生成器版本/种子、选中样例页、精确页/功能/放置/连接计数、名称唯一性和密度分布；机器路径只留本地。保留 User.eod/User.eox（A35/A36），不得直接编辑数据库或用重复 .edb 文件冒充新增页。

生成分两条轨道，准备时间不计编译/执行性能：

1. **可行的页数轨道**：在种子 scratch 副本上，用既有 `eplan_page_create` 每次≤50 页追加唯一页名到1,000/3,000/10,000页；300页副本用既有删页工具删除固定21页，记录遗留功能/连接计数。保持项目已支持的结构，逐批读回页名，最后关闭重开计数。A13/A17/A41 证明建页/读回/按次撤销；此轨道只测页列表规模，不能冒充真实对象密度。
2. **内容轨道（须 spike）**：从种子代表页用 `Eplan.EplApi.MasterData.WindowMacro.Create` 导出完整放置到 scratch 宏，测试专用 API harness 用 `Eplan.EplApi.HEServices.Insert.WindowMacro`、SafetyPoint 插到新页；用 Number 或已验证的唯一结构分组避免意外同名合并。必要时 `Generate.Connections` 生成实际连接。所有内容来自示例，不引用外部宏/客户材料；不扩大产品 `$(MD_MACROS)` 根目录。

按样例疏/密页比例填充，要求平均 standard 对象密度至少接近17/页，并保留≥743 对象的压力页；预计3,000页约5.2万、10,000页约17.3万 standard 对象，最终以计数为准。另保留同名跨页、额外表达和无页功能测试子集；不改变全局系统库。安装示例缺少可用无页变体时 spike 只能从它的现有功能派生，不能凭空假设 API 的卸载/复制方法。

先做 `spikes/S37-large-plan-fixture`：核对实际导出/插入重载、授权、结构/编号、独立提交后关闭重开持久性、回滚/一次撤销、密页连接与无页样本的构造。A13/A14 证明窗口宏插入和回滚，S32/A37 证明窗口宏导出再插入，**未证明批量复制页面、页宏插入或万页提交**；不得直接假定 Page.CopyTo/Insert.PageMacro 可用。spike 失败就用页数轨道报告有限结论，内容规模验收仍未通过。

生成器逐批释放主线程、支持取消，失败留可识别未完成目录，未完成夹具不供测试；重复运行新建 generation-id，不覆盖打开项目。预估10,000页项目可能数GiB，启动前按实测种子/增长率预留种子、运行副本、初备份和 Destructive 备份空间（先按40 GiB规划，实测修订），不自动删既有备份。清理只针对关闭且校验在 scratch 内的本次目录。A41 撤销测试先确认当前项目是该副本；A34 不以 GUID 认定副本身份。

### 7.2 必须证明的行为

| 规模 | 测试与断言 |
|---|---|
| Small | 精确 scope/空集；Names/members 原公式黄金向量；短名展开、大小写/冒号后缀碰撞；同名身份新增而NamesHash不变；200/10,000/50边界；五类diff/未观察内容；compilerVersion与旧记录只读兼容 |
| Medium | Fake Bridge 断言任何三种Write编译/初检/逐步均无全项目standard调用；双读缺页/闭包/名称变化拒绝；>10,000名称但少量明细通过；名称不完整/超预算不发execute；前序新名冲突与预测绑定；13MiB耗尽、取消、outcome未知及已提交事实保留；原schema往返/记录成员快照不变 |
| Large 性能 | 321页基线及300/1,000/3,000/10,000页内容夹具；逐op记录compile、guard、before/after、backup、execute、证据字节；page.create guard已有页ReadPage次数=0，其余仅E；验证总对象>10,000仍可运行局部Write |
| Large 正确性 | 编译后在远页/无页占用新名，guard stale且零写；旧名同名成员移页/新增/删除，数量相同替换也stale；跨页和无页功能全改且外部成员不改；密页/超200成员拒绝；宏全名冲突、哈希替换、回滚/撤销 |
| Large 观察边界 | 远页只改描述：名称筛查可通过但coverage必须列未观察；已知关联页改内容产生outside_scope并停止；两步间改名为external_between_steps；只读完整审计快照用于测试oracle，放在被测计时/证据之外，证明实际改动仅获批范围 |
| Large 回归/Agent | 小夹具重编号最后步/限额/仍带?报告；大夹具重编号提前缺能力并给单次工具提示；原七项备份/事务/按步Ctrl+Z与失败恢复；Agent运行建页→插宏、远处冲突后重新编译，不重复apply |

性能运行与破坏性故障注入分开；固定计划与输入、报告全部失败，不靠重跑挑结果。每个涉及 Bridge/写路径 PR 合并前按 [testing.md](../standards/testing.md) 在支持的真实 EPLAN 上跑相关 Large，并将版本、夹具摘要、原始计时和结果附 PR；Small/Medium 不能替代实机。首次核心验收至少每规模完成一轮正确性和计时，完整冷/热分布按4.7补齐后才发布该规模性能承诺。

## 8. 发布与兼容性

本修订单独文档 PR，CHANGELOG-neutral。下表每项生产代码+文档增删合计≤400行，目标约200，测试/夹具按流程不计；接近上限先拆，不能以跨PR临时不完整检查开放执行。工具schema不增加参数，规格、模型提示和契约快照在相应行为启用时同步。

| PR | 一件事与估算行数 | 依赖；测试/实机门槛 |
|---|---|---|
| LP-D | 本设计（≤400） | 文档链接/范围自查；不操作EPLAN |
| LP-1 | S37与隔离夹具生成入口（≤300） | D；Small路径/来源，Medium目录完整性，Large种子→3,000/10,000页与持久性；可与后续实现准备交错 |
| LP-2 | 完整名称枚举及plan.names（≤350） | D；Small合并/去重/限额，Medium invoke完整性，Large与示例/额外表达/无页对照 |
| LP-3 | 影响发现与局部双读，移除全项目身份快照（≤350） | 2；Small scopes/原哈希，Medium编译调用断言/稳定性，Large不修改工程与密页 |
| LP-4 | absent与步内名称/同名闭包guard（≤350） | 3；Small碰撞/成员替换/计时，Medium写前失败，Large远处冲突/3,000与10,000页主线程预算 |
| LP-5 | 无页locator取证及设备成员核对（≤300） | 4；Small独立对象hash，Medium范围交集/缺能力，Large全功能改名/回滚/撤销；失败保留该能力拒绝 |
| LP-6 | apply初检/审批后改为S/N/闭包重核对（≤250） | 3–5；Medium dry-run/消耗/审批前后竞态，Large陈旧计划零写；包含重编号提前限额与提示 |
| LP-7 | P20局部before/after及有界证据引用（≤350） | 6；Medium断言无全读/存储耗尽/部分完成，Large初备份/原事务/outcome回归 |
| LP-8 | P22名称筛查、无页校验与coverage（≤350） | 7；Small差异/unknown，Medium未观察页/名称转换，Large关联页越界和范围外描述不可见 |
| LP-9 | 联动启用compilerVersion=2、规格/发布说明（≤200） | 1–8；Small/Medium全套及契约快照，Large各规模核心验收+Agent；v0.3.0最早发布点 |
| LP-10 | 根据实测优化枚举/连接读取或补小项目最终筛查（每项另PR≤350） | 9之后；等价性Small/Medium+Large完整性/性能；不阻塞已达标核心发布 |

LP-2–8 中间态保留旧可执行路径，新编译语义尚不发布；新组件可独立测试，到 LP-9 才原子切换编译与apply检查，不能用开关略过安全检查。若单项超过400行，再拆未暴露的读取/纯比较组件PR。无页写入门槛未过可按4.3保留明确缺能力，不能宣称该变体已支持。

v0.3.0 的核心是三个常用Write解除全项目内容依赖、正确的局部验证与名称guard、保留原审批/备份/预算。达到该组合的实机门槛即可发布，无需等待重编号扩容、全项目内容筛查优化或20步极限压力优化。10,000页guard/初备份若未达预算，发布说明必须列实测容量和缺能力/超时限制，不能宣称已支持任意万页项目；预算本身不放宽。

旧compilerVersion=1计划不迁移、不重写hash；按既有保留策略可查询，执行要求重新编译。回退实现时不接受version=2执行，保留记录、审计和备份。没有进程拓扑、审批渠道、安全权限或落盘迁移的L3变更。

## 9. 待定问题与责任

| 门槛 | 负责人/时点与失败行为 |
|---|---|
| 完整名称扫描是否在10,000页<10s | 维护者在LP-4/9给出分项实测；不达标先优化并查证API，不切换到不完整扫描 |
| 无页resolve与改名后取证 | 维护者在LP-5 Large验证；未证实只拒绝该写入变体，冲突域仍含无页名称 |
| 示例内容放大与密度/持久性 | S37在LP-1验证；空页轨道不可替代内容轨道，通过后登记api-notes |
| 大项目初备份≤60s、证据≤13MiB | LP-7/9实测；失败保持原BACKUP_FAILED/RESOURCE_EXHAUSTED边界，不绕过备份或配额 |
| L3触发 | 当前无；如确需改持久结构、审批/备份或主线程保护，由项目负责人先审单独Proposed ADR，该部分停止 |

# 设计文档：M6 第 3 阶段的配置、模块与规则

| 项 | 内容 |
|---|---|
| 状态 | In Review（实施设计；本次只写文档，未运行 EPLAN） |
| 级别 | L2；新增落盘合同单列为 [ADR-0029](../adr/0029-phase3-engineering-records.md)（Accepted，项目负责人 2026-10-26） |
| 关联 Issue | — |
| 相关依据 | [M6](2026-10-21-m6-engineering-packs.md) 4.1–4.3、4.10–4.11、4.16–4.18；[阶段 2](2026-10-23-m6-phase2-plans.md)；[大项目计划](2026-10-25-m6-large-project-plans.md) |
| 相关 ADR | [0023](../adr/0023-plan-approval-and-backup.md)、[0024](../adr/0024-pack-trust-and-container.md)、[0025](../adr/0025-code-plugin-worker.md)、[0026](../adr/0026-eplan-events-and-push.md)、[0028](../adr/0028-plan-record-format.md)（均 Accepted） |
| 最后更新 | 2026-10-26 |

## 1. 背景

阶段 1 的包、资源和阶段 2 的计划已完成，阶段 2 随 v0.3.0 发布。早期设计中的“尚无组合执行器”不再代表现状；以当前实现为基线：

| 已有实现 | 本期缺口 |
|---|---|
| `EplanMcp.Packs` 的 `PackManifest/PackConfiguration/PackLock/ProjectBinding` | 配置槽位已有；`CabinetBinding` 只有 id/title，项目 locks 为每包 ID 一个版本，尚无五层解析与结构映射 |
| `KnowledgeDocument/KnowledgeSpecs/DevPackStore` | 信封无持久 id/status；种类仅 candidate/module/material_mapping/gap；模块只存 macro/parameters，不能据此宣称可生成 |
| `PackVerifier/ContentChecks.RuleSourceAsync` | 官方规则目前只查 source 非空；没有类型化规则、来源解析和求值器 |
| `PackArchive.ExportAsync` | ZIP 导出、限额和校验已有库/CLI；`eplan_pack_build` 尚无导出入口，签名尚不能验证，仍须拒绝 pack.sig |
| `PlanStepCatalog/WorkflowCompilation/PlanApplyChecks` | compilerVersion=2；七项封闭步骤、局部证据/全项目名称筛查；仍只接纳默认柜，不接受 module.insert/placeholder.apply |
| Host 的 `IReferenceProjectSteps/PackCapabilities` | 参考项目导入仍接 GapOnly 占位实现，licence 要求仍为 unknown；不能把阶段1完成理解成提取/授权链已全部接通 |

`PlaceholderApplyExecuteOperation` 的单步工具只核对占位符值表，不逐项证明引用对象的属性变化；不能直接包装为计划步骤。API 事实以 [api-notes A31–A41](../eplan/api-notes.md) 为准；A31–A34 的副本/身份、A35–A37 的原生文件/提取、A38–A41 的事件/副作用/撤销分别约束本期设计。

## 2. 目标与非目标

- **目标**：同一输入随公司/柜配置生成不同计划，柜间隔离；每个生效值和生成对象可追溯；安装新版不改变旧项目锁，显式迁移后重新校验。
- **目标**：在原生宏、占位符、值集上补齐模块接口、部件映射、适用性和验收；从完成项目提取候选模块；规则与包内工作流经同一计划编译器，修复从不直接执行。
- **目标**：强制分离事实、候选、正式规则；无来源的 mandatory 规则不能构建；完成 M6 场景 1、2、3、6、8 和 `.eplanpack` 交换闭环。
- **非目标**：全关系图/电气关系与图纸层完整验收（阶段 4）、事件缓存、任务重启恢复/自动重试（阶段 5）、Cogineer 集成、页宏/3D 模块、自动布局、系统主数据写入、Office/PDF 内容解析、多版本支持扩展。
- 普通工程师继续交材料、审阅结果，不手写这些 JSON。格式由工具生成；以下示例是实现合同草案。

## 3. 方案概述与决议

Host 的 Packs 层负责纯数据模型与校验；Host 服务解析配置、绑定、锁、规则和工作流，再接已有 Plans。Bridge 只增加经 `invoke` 调用的类型化操作，所有 EPLAN API 仍走既有主线程调度与锁，不加进程、JSON-RPC 方法、传输信封或协议版本。

```mermaid
flowchart LR
    P[锁定包与柜绑定] --> C[五层配置与来源]
    C --> R[规则 / 模块 / 包内工作流]
    R --> W[既有工作流编译器]
    W --> A[计划审阅 / 策略审批 / 初备份]
    A --> G[逐步 guard / 事务 / outcome 核实]
    G --> V[实际属性验收 / 局部差异 / 来源]
    X[参考项目 scratch 副本] --> E[事实提取与候选模块]
    E --> P
```

| 决议 | 选择及理由 | 否决的替代方案 |
|---|---|---|
| D01 配置 | base→company→cabinet_type→project→cabinet，默认值覆盖、硬约束只能收窄、全程溯源 | 字典深合并会放宽约束；冲突时自动截断会改变工程意图 |
| D02 柜 | 结构段映射，整份绑定原子替换；每个对象至多归一柜 | 字符串前缀混入 +A10；保留全覆盖默认柜会与显式柜重叠 |
| D03 锁 | 保留项目级每包 ID 一个精确版本，各柜引用其子集及依赖闭包 | 每柜同包不同版本增加冲突解析；自动 latest 破坏复现 |
| D04 迁移 | 差异→scratch 副本检查/验收→工程师确认→CAS 换锁；工程修改另成计划 | 安装即迁移、只看 SemVer、迁移脚本直接改项目均不可审查 |
| D05 模块 | 原生窗口宏+占位符/值集；元数据声明接口/映射/适用性/验收 | 自造电路生成语言重复 EPLAN；Cogineer 授权可用不等于已安装或可集成 |
| D06 提取 | 显式页/对象范围，先提事实与差异候选，人工确认参数语义后写回宏 | 自动从全项目猜重复回路并发布模块需要阶段 4 的关系证据 |
| D07 规则 | 受限、类型化 JSON AST，四值结果；修复仅引用工作流 | C#/RulesEngine/脚本、字符串表达式求值会让包携带代码 |
| D08 工作流 | 包参数与配置编译为封闭、有界步骤，复用阶段 2 全部安全段 | action、循环/递归、插件动态展开绕开封闭目录与限额 |
| D09 知识 | 事实仅提取器写，候选永不自动转正，正式规则须来源及工程师确认 | confidence 高、candidate.accepted、目录名或 standing 字符串均不证明正式授权 |
| D10 导出 | 独立export工具复用 PackArchive，导出冻结版本，默认不带参考项目 | build混合导出改变已发布幂等注解；ZIP任意目录/自动捆绑外部引用改变内容 |

## 4. 数据模型与文件格式

### 4.1 版本、引用与落盘边界

本节所有新增字段/版本是 **ADR-0029 的提案合同**，不是已接受决定。[开发流程](../process/development-workflow.md#3-变更分级与设计产物)将落盘格式列为 L3；不能仅递增 compilerVersion 绕过 ADR。

| 记录 | 提案及兼容 |
|---|---|
| `pack.json` | 新能力包 schemaVersion=2，原有资源/配置槽位保留；旧包 schema=1 原样可读，不重写已安装版本 |
| `pack.lock.json` | lockVersion=1 不变；冻结包自身及传递依赖的 id/version/digest，安装/使用时复核依赖闭包，不能只信可替换的锁文件 |
| `knowledge/<kind>/<name>.json` | 扩展种类使用 apiVersion=`eplanmcp.pack/v2`；仍为 apiVersion/kind/metadata/origin/spec，无持久 id/status。资源 ID/URI 按现有小写 snake_case 规则派生；本包引用用 `module/motor`，跨包精确版本由 locks 解析 |
| `workspaces/<项目键摘要>/binding.json` | bindingVersion=2；project/revision/locks保留，增加projectValues/packRoots（显式包根id/version），扩展cabinets的layers/values/structure；不写EPLAN项目 |
| `state/plans/<planId>.json` | 新编译计划 schema=2、compilerVersion=3；definition 增加 engineering（配置、工作流、模块、规则验收证据），canonical/planHash 算法和五值结果不变 |
| `state/migrations/<id>.json` | schemaVersion=1的不可变预览：owner/project/oldRevision/oldLocks/newLocks/proposedBinding/各柜差异/基准与验收证据/createdAt/expiresAt/reportDigest；临时文件刷盘后原子发布 |
| 确认记录 | 本机审计记录绑定资源/绑定前后摘要、来源、决定及渠道；包中只携带来源记录的引用/摘要，不能充作可复用的批准凭据 |

PackJson 的严格成员/重复键/版本检查、UTF-8/LF 格式及 16 MiB 文件上限沿用；知识 JSON 的数值可为有限 decimal，资源按原始字节 SHA-256。PackDigest 仍只规范化整数型清单，不能拿它给含小数的配置文档直接算摘要；计划/配置证据用 PlanCanonical。所有引用、依赖、配置项和类型在构建时完整解析，问题带 JSON Pointer。

`PackVerifier` 按知识种类分派正文校验委托，共用清单、包内文件、引用解析及配置类型上下文；保留原有信封兼容路径、必须随包携带的种类、问题文本与 JSON Pointer，以及正文→SVG/正式规则来源的检查顺序。

旧绑定只经显式 bind/migrate 转为 v2，保留原文件作为本地证据，revision+1；旧柜缺结构时要求显式映射，不能猜测归属。旧计划不重写哈希，未过期且绑定未变者仍走既有 compiler=1/2 路径。旧二进制拒绝 v2；回退保留文件/审计/备份，以旧版只读副本排查，不把备份自动覆盖新绑定。v1裸规则/简版模块保持可查询，缺少本期覆盖合同者不自动升级为可执行资源。

### 4.2 五层配置与柜

`ConfigSchema.spec.items` 按键定义 `{type,unit?,enum?,min?,max?,allowedLayers,required}`，类型为 text/number/boolean/choice，禁止任意对象和隐式字符串转数字。`ConfigLayer.spec={layer,schema,settings}`，每项 settings 为 `{default? ,constraint?}`；constraint 是 fixed 或 allowed 集合/闭区间。项目/柜 values 使用同一设置结构。

C1 实现：schema 项可选 `default` 按 base 层验证，须获 `allowedLayers` 许可；包内 config_layer 只能是 company 或 cabinet_type，project/cabinet 值只来自绑定。`ConfigValidator` 统一验证层设置与绑定的 project/cabinet values；`required` 的缺值、约束交集和最终默认值留给 C2。单位登记为 W/kW、V/kV、A/mA、mm/m，保存量纲和换算系数；设置值使用 schema 声明的单位，不能重定义。

pack check/build 校验配置正文和本包 `config_schema/name` 来源；配置文档须随包携带。当前目录检查没有依赖资源解析上下文，跨包来源暂拒绝，锁定依赖解析留给 C4。测试材料见 `tests/EplanMcp.Packs.Tests/Fixtures/README.md`，仅验证电机示例配置格式，不代表通用选型算法。

```json
{"apiVersion":"eplanmcp.pack/v2","kind":"config_layer","metadata":{"name":"company"},
 "spec":{"layer":"company","schema":"config_schema/motors","settings":{
   "motor.starter":{"default":"dol","constraint":{"allowed":["dol","star_delta"]}}}}}
```

schema 默认值视为 base 层；键名、单位和类型不能被下层重定义；数值先按登记单位换算后比较，不支持的单位拒绝。一个柜每个包层至多一个，链顺序显式，不靠文件名/安装次序决定优先级。同一 schema 键来自不同包且定义不同即 CONFIG_CONFLICT。空交集、下层扩大集合/区间、最终默认值不合约束、required 无值均报错，不自动选集合首项；相同值也保留各层 trace。

生效项返回 `{key,value,unit,constraints,trace:[{layer,source,pointer,kind,value}]}`；source 是精确资源 ID+文件哈希或 binding revision。例：base default=dol、company allowed=[dol,star_delta]、柜型 default=star_delta、柜 default=dol，结果 dol，四项来源全部可见。

C2 实现：`ConfigMerger.Merge` 接收已解析的 `ConfigDocument`（精确资源 ID、原文件 SHA-256、spec）、显式有序的包层、v2 绑定及柜索引；返回 `ConfigMergeResult`，成功的 `Values` 可映射到 `PlanConfigValue`，失败只返回首个问题的错误码、JSON Pointer、说明及 trace，不暴露部分生效值。资源来源为 `{resource,sha256}`，绑定来源为 `{project,bindingRevision}`；默认值与约束分别留痕，base 的 schema 边界另留 `kind=schema`，所有 trace 值保留原始单位下的内容。

等价单位的数值定义先换算到首个 schema 的单位再比对；其余定义差异、换算溢出或精度损失报 `CONFIG_CONFLICT`。下层约束必须为上层的子集，因此通过检查的下层约束即为交集；fixed 只限制取值，不能替代缺失的 default，单元素集合亦不自动选值。项目/柜值经合并 schema 的 `ConfigValidator.ValidateSettings` 检查；最终才检查默认值和 required。跨包锁解析及资源哈希复核仍由 C4 调用方负责，本接口不接磁盘或 EPLAN。

```json
{"bindingVersion":2,"project":"<项目键>","revision":7,"projectValues":{},"packRoots":[{"id":"acme.panels","version":"1.2.0"}],
 "locks":[{"id":"acme.panels","version":"1.2.0","digest":"<sha256>"}],
 "cabinets":[{"id":"a1","title":"动力柜","layers":["acme.panels@1.2.0:config_layer/company"],
   "values":{"motor.starter":{"default":"dol"}},
   "structure":{"prefixes":[{"installation":"=AP","location":"+A1"}]}}]}
```

structure 为 `{all:true}` 或 `{prefixes:[...]}`，按 EPLAN 完整结构段解析，禁止原始通配/正则；+A1 不匹配 +A10。检测潜在前缀交集，不只看当前有页的区域。all 只许唯一 default 柜；转多柜必须在一次 bind 中替换全柜集合。无归属/多归属对象及结构不能证明的宏引用不允许写；无页功能依其完整标识归属，不能用“无页”绕过柜边界。跨柜连接只作边界证据，不把另一柜对象并入可写范围。

C3 实现：`CabinetScopeResolver` 快照柜映射，`ParseDesignation` 按 `= + & == ++ #` 区分完整字面段，`-` 后的设备标识或 `/` 后的页名不参与匹配；空段、重复结构段、通配符或无法证明的名称返回无归属。`CabinetOf` 返回柜 ID 列表：空为无归属/未证明，单项为唯一归属，多项为歧义；多个前缀命中同柜只计一次。调用方须先校验 v2 绑定，且仅单项归属可进入后续写入范围检查；宏引用仍须独立证明全部实际引用的归属。

`Overlaps` 按任意一对前缀的共同结构维度判断：共同维度均相同（包括没有共同维度）即可能相交；新增维度形成的嵌套前缀同样相交，段值不作字符串前缀扩展。`BindingReadModel` 在严格格式检查后拒绝柜间交集，问题定位后柜的 `/cabinets/{index}/structure` 并注明前柜位置；`PackJson.ReadBinding` 与 `ConfigMerger` 共用此检查。无页功能直接传完整标识，连接两端分别解析，不沿连接扩张范围。计划/工具接入仍依第 9 节后续 PR；本次仅增加纯解析与校验，示例工程 Large 对照尚未运行。

锁是显式packRoots、全部柜配置链及其传递依赖的并集；不含配置层的模块/工作流包也通过packRoots固定。构建只解析一次依赖，绑定复用已核验的精确pack.lock闭包，不重新选择latest；版本要求不相容即CONFIG_CONFLICT。首次锁定、换锁、删锁均走M6已批准的确认例外；仅改柜映射/值且锁不变，按本地数据审计。未绑定项目维持阶段2的default无配置模式，不隐式建立锁。

C4 实现：`BindingLocks` 只沿已安装包的精确锁校验闭包、版本要求与摘要；`BindingService` 复用现有 `ApprovalService` 渠道确认锁变化，确认前后复核原始绑定与包内容，先取计划共用的项目锁、再取绑定锁，以 `PLAN_STALE` 拒绝 revision 冲突。v2 临时文件刷盘后原子替换，写前/写后记录 `workspace_change` 的前后 SHA-256、来源及确认决定；显式转换 v1 时保留原字节证据。计划等待人工时释放文件锁，确认后重新取锁并核验；本次不注册工具，配置合并及多柜入口由 C5 接入。

### 4.3 模块、映射与提取证据

`Module.spec` 保留 macro/parameters 并补以下合同；同一份宏仍是原生 `.ema`，首批仅 MultiLine、variant=0、numbering=keep、空 Circuit 页。页宏写出成功（S32）不证明产品已支持页宏插入。

| 字段 | 内容及拒绝条件 |
|---|---|
| parameters / interface | parameters保留顶层，声明名称/类型/单位/范围/必填/默认值及绑定 `{placeholder,variable,propertyId,language?}`；interface.ports为端子/电位/电缆端的宏内相对选择器、方向和连接点名；标识规则只允许登记模板 |
| partMapping | 固定asset或configKey二选一；后者从五层生效text解析MaterialMapping，随trace记录公司选择。entries为类型化when→`{part,variant,quantity}`，含source；多匹配/零匹配报CONFIG_CONFLICT/RESOURCE_NOT_FOUND，不按文件顺序取第一条 |
| applicability | 复用 4.4 谓词，输入为生效配置/参数/能力；false 为不适用，unknown 阻止生成，列缺口 |
| acceptance | 必需对象/属性/端口存在性、预期值、禁止的范围外变化、Sample 引用；每项带依据与覆盖需求，不把推断关系当事实 |
| extraction | 源资源摘要、源版本、snapshotHash、提取器版本、选中页/稳定 locator、宏内对象映射、观察到的实例值与差异；不存源机器路径/DatabaseIdentifier |

MaterialMapping 只选择已存在部件，不修改系统库；候选映射可供预览，生成前须工程师按已有“设正式”流程确认其精确内容。参数到值集的转换逐类型/语言登记；拒绝未绑定变量、超范围值、值集名歧义与不能定位的端口。不能静默把部件号文本当成已成功分配的部件引用。

M1 实现：`ModuleValidator` 返回保留原始正文的 Module/MaterialMapping/Sample 模型；parameters 按名称索引，复用 ConfigValidator 的类型、单位、枚举与边界，模块另带 binding。valueSets 显式列出命名参数值，端口以 object 相对标识及 connectionPoint 定位；原生定位和变量引用闭包仍由 M2 验证。映射 when 为类型化精确值的子集，潜在重叠在构建时即报 CONFIG_CONFLICT；Select 对零匹配报 RESOURCE_NOT_FOUND，不按顺序兜底。

acceptance.checks 支持 object/property/port/unchanged_outside，逐项要求 source（asset/hash/location）与 coverage；samples 引用 Sample，Sample 的 sources 不能为空，workflow/configFixture/expectedChecks 引用本包或精确锁定依赖中的相应资源。样例输入的工作流 schema 校验留 W1，执行覆盖留 V2；来源哈希只证明引用一致，正式确认仍留 R3/R4。extraction 保存源版本、快照摘要、提取器版本、选定页/locator、相对对象映射及测得参数；不接收机器路径。pack check 接入 v2 校验，v1 module/mapping 继续原样查询，不自动成为可生成模块。

提取分两步：`eplan_module_extract` 在参考项目的 scratch 副本读取显式范围，提取器写不可变 FactSet，生成 Module 草稿及参数候选/Gap；工程师审阅实例差异与接口后，再请求 materialize。已有占位符优先保留；无占位符时在新 scratch 副本中用 `Eplan.EplApi.DataModel.Graphics.PlaceHolder.Create/AddReference/SetPropertyEntry` 建立变量和值集，用 `Eplan.EplApi.MasterData.WindowMacro.Create` 写出。内部临时写入仍经既有scratch Write策略、审计和事务；临时修改回滚，输出宏经重新打开/再插入核对后进入开发包，来源原件前后哈希必须一致。

参考材料准备补齐现有GapOnly接缝：固定的invoke内reference.prepare/close操作只接受经核验资源和Host新建scratch目标，不开放任意项目open/close；版本检测后复制/还原到新空目录，再只打开该副本，最后关闭本次副本。缺User.eod/eox、版本较新或源哈希变化即拒绝；不得关闭工程师已打开的项目。授权由模块inspect的类型化结果实时核验，不改握手信封；unknown不能视为满足。

M0a 实现 `ReferenceCopyService` 与未注册的 `ReferencePrepareOperation/ReferenceCloseOperation`；本次不接入 GapOnly 或工具。仅接纳 packSources 中本地、无链接的 referenced project/project_backup，源项目必须关闭；Host 前后按包资源摘要复核，Bridge 只用 Snapshot/原生 restore 写入全新空 scratch 目录。关闭凭据绑定副本路径及打开时的数据库身份，重开或伪造句柄拒绝关闭，记录打开前后当前项目而不依赖其选择。Large 测试已编写，本次不运行 EPLAN；异常残留副本保留供排查，不自动重试或覆盖。

M0b 增加只读 `IReferenceExtractor` 接缝，M3 可替换 `GapOnlyReferenceExtractor`；输入限定本服务持有且仍只读打开的参考副本、1–50 个显式页名，返回分离的事实与缺口。缺提取器、操作或已确认的 API Extension 授权时，仅返回 Gap 和带 `missing`/操作提示的 `CAPABILITY_MISSING`，不生成事实。接缝不写副本，提取前及完成、失败或取消后复核源资源摘要；不注册工具或 Bridge 操作，导入仍保留原 GapOnly 路径。

提取前强制刷新既有握手，避免复用 Host 的连接缓存；Bridge 每次握手通过有时限的主线程调度调用 `License.HasOption(APIExtension)`，不调用 `LockOption`，不消费或持有授权（A2/A37）。核验未知或调度失败时按缺能力处理；后续 M2 仍须在 inspect 内核验实际调用能力。Small/Medium 覆盖可替换读取器、缺授权/操作、只读归属及摘要变化；Large 已补副本只读提取、源摘要和副本设计文件不变，本次仅编写、未运行。

“3 个回路扩到 8 个”先保留三个原实例的测得值，差异仅作为参数候选；新增五个实例的参数、标识和页/坐标须显式给出，不从前三个样本宣布公司规则。S36 未通过前允许查看提取候选，不注册 materialize 或生成步骤；不能以 `device.update`（只改标识）冒充通用属性写入。

### 4.4 规则、事实与确认

`FactSet.spec={sourceResource,sourceHash,snapshotHash,extractorVersion,facts:[{id,locator,property,value,evidence}]}`，仅提取器写；`Candidate` 保留 statement/basis/confidence/status/inferredBy，v2可带proposal（kind=rule/module及类型化spec）供审核，不能执行为强制规则。接受候选仍不等于正式规则。矛盾事实记录 Gap；模块差异和资料文字不能自动成为 mandatory。

正式 `Rule.spec={severity,source,scope,check,message,fix?}`；severity 为 mandatory/recommended。source 必须是可解析的文件资源+位置+哈希，或工程师确认记录；两种均绑定被确认的规则内容摘要。构建/安装/使用均校验 kind、standing、source 一致；现有“source 非空即通过”须替换。来源文件被替换、确认后内容改变、AI 自填确认字符串均不能通过；外来official仍须ADR-0024安装确认绑定包摘要，历史确认只作来源，不能复用为本机操作批准。

```json
{"apiVersion":"eplanmcp.pack/v2","kind":"rule","metadata":{"name":"starter-allowed"},
 "spec":{"severity":"mandatory","source":{"asset":"document/guide","location":"4.2","hash":"<sha256>"},
   "scope":{"kind":"function","where":{"eq":[{"property":"function.category"},{"literal":"Motor"}]}},
   "check":{"in":[{"config":"motor.starter"},{"literal":["dol","star_delta"]}]},
   "message":{"zh":"起动方式不在公司允许范围内"},"fix":{"workflow":"workflow/change-starter","inputs":{"target":{"object":true}}}}}
```

操作数用 property/config/parameter/literal 的显式节点，禁止把任意字符串解释为属性或代码；`$object` 等替换也用封闭节点。属性必须登记类型、单位、缺失语义与读取覆盖；类型不匹配为 PACK_INVALID，缺数据为 unknown。运算符沿 M6：eq/ne/lt/le/gt/ge/in/not_in/exists/empty/starts_with/ends_with/matches、all/any/not、some/every/count；无算术、函数、循环。matches 使用 NonBacktracking，单次 20 ms，模式≤256 字符、输入≤2,000 字符。

AST 最深 8 层、关系最多 3 跳；每柜最多 500 规则、每规则 100,000 次节点访问/1 s、整次求值 30 s，确定性计数+墙钟双限；超限列 unknown 和 coverage，不当作 pass。`false AND unknown=false`、`true OR unknown=true`，其余保留 unknown；scope=false 为 not_applicable，scope=unknown 为 unknown。结果为 `{rule,version,locator,status:pass|fail|not_applicable|unknown,evidence,basis:fact|inference}`；mandatory unknown 阻止依赖它的生成/迁移生效，recommended 只提示。

R1 静态合同：`RuleValidator` 返回带类型、单位及子节点的 AST；`RuleStatus` 经 PackJson 序列化为上述四值。属性首批仅登记快照的 function.name/category/main、page.name/type、connection.name；除 main 为 boolean 外均为 text，无单位，缺失值或读取覆盖不完整均为 unknown。关系仅 connection.start/end（connection→function）及 function.connections（按直接端点反查 connection），不包含电位推断。

运算符均使用显式操作数数组；exists/empty/not 为一元，all/any 为非空谓词数组，其余比较为二元。some/every/count 使用 `{relation,where}`，where 是目标种类的谓词，count 返回无单位 number。顺序比较只接纳 number；集合成员比较要求同类型、同量纲的非空字面集合。数字单位写作 `{"literal":7.5,"unit":"kW"}`，集合共用一个 unit；不自动给无单位数补单位。matches 的模式必须是字面文本，构建时检查 NonBacktracking 语法，动态输入的 2,000 字符限制及 20 ms 超时由 R2 执行。

scope/check 根节点计第 1 层，操作数节点也计深度；另设静态上限 1,024（整条规则的节点加集合元素，含 fix 输入），区别于运行时访问预算。config 由随包 config_schema 提供类型/单位，choice 按 text；独立规则没有 parameter 声明上下文，未知配置/参数拒绝。fix.inputs 只接纳显式 AST 节点或 `{"object":true}`；来源真实性、工作流解析与正式规则门禁仍由 R3/R4/W1 实现。旧裸规则资料保持可查询，不成为此合同的可执行规则。

R2 实现：`RuleInterpreter.EvaluateAsync` 接收已验证的 `RuleDefinition`、柜内不可变内存快照 `IRuleFactSnapshot`、类型化配置和参数；Host 的快照适配留待后续接入。证据记录 JSON Pointer、locator、原值、覆盖及来源说明，任一已使用来源为推断则 basis=inference。覆盖完整且确定无值时 exists=false、empty=true，其他比较为 unknown；覆盖不完整或缺值的 exists/empty 仍为 unknown；关系覆盖不完整时 some/every/count 均为 unknown，完整空关系分别为 false/true/0。单位转换溢出或损失 decimal 精度为 unknown。

节点访问（含集合元素）与 1 秒预算按整条规则、跨全部对象累计，超限撤销该规则此前的确定结果；30 秒超限使整次结果为 unknown，附覆盖说明，后续不再执行谓词。超过 500 条时整批返回 unknown，不截断规则。`TimeProvider` 与内部访问限额/正则替身支持确定性测试；生产正则固定使用 R1 的选项与 20 ms 超时。取消抛出取消异常；本 PR 不注册工具或 Bridge 操作。

阶段 3 只启用已覆盖的对象属性、归属和快照直接连接端点关系；一般电位延续/跨页追踪等仍待阶段 4。未知关系在构建时报 CAPABILITY_MISSING，资料仍可索引为不可用；不得把“电机必有热继电器”的推断搜索当完整事实检查。fix 引用精确锁定 Workflow，仅 fail 且依据足够时生成计划；冲突修复、unknown 目标不合并、不自动应用。

`eplan_asset_update` 继续不能写 FactSet/Rule 或设置 accepted/official。新增 `eplan_asset_review` 经既有审批渠道接受/拒绝候选、关闭缺口或将候选提升为新 Rule；保留原候选内容和事实，仅更新审核状态，审计关联新旧摘要。模块接口/映射审核同样绑定候选及目标内容摘要；materialize的review_digest必须匹配本机已确认审计记录，客户端自报hash不是确认。无来源 mandatory 在 build 报 PACK_INVALID；未经确认的 Rule 草稿保留为 Candidate，不靠手工改 standing 生效。

R3 实现：`KnowledgeReviewService` 通过既有 `ApprovalService` 确认 accept/reject/close_gap/promote，确认前后重新读取资源与来源文件并核验原始字节摘要；来源严格为 `{asset,location,hash}`，本包引用规范化为精确资源 ID。提升绑定同包新 v2 Rule 的 ID/原始字节摘要，检查 R1 AST 与相同来源；配置类型由调用方提供解析器。候选接受不授予 official，审核不改包、候选正文或事实；新 Rule 发布和构建门禁由 R4 接入。

确认仅在本机 `knowledge_review` 审计刷盘后成立，记录资源 ID/摘要、来源、决定、提升目标摘要和确认渠道，返回该审计记录的哈希作为 `review_digest`。`VerifyAsync` 重新核验内容与来源，检查完整本机审计链及期望决定/目标；同资源的后续审核取代旧审核。导入包中的历史状态、standing、确认字符串和哈希均不提供本机授权，安装确认也不替代知识审核。审核被拒绝、确认绑定不符或审计写入失败时不记录审核决定；本次不注册工具或 Bridge 操作。

### 4.5 包内工作流、验收与导出

`Workflow.spec={parameters,requiredCapabilities,applicability,steps,acceptance}`。parameters 用与模块相同的有界类型；step `{op,args,dependsOn}` 只允许阶段 2 目录与通过 U2 的模块操作。参数/配置替换仅在 args schema 指定位置，以类型化 `{parameter:"power"}` / `{config:"motor.starter"}` 节点表达；不能替换 op、project、cabinet、tier、审批或路径。禁止 include/extends/嵌套 workflow/循环；批量生成由 Host 有限展开，总计仍≤20步、256 KiB，不自动拆成多个已批准计划。

W1 实现：`WorkflowValidator.Validate` 返回独立的 `WorkflowDefinition`，`Expand` 纯函数返回 `WorkflowStep` 列表。parameters 按名称定义 `{type,required,unit?,enum?,min?,max?,default?}`，最多50项，复用配置类型及单位；仅替换标量，坐标参数须用 mm，宏保持字面资源引用（本包 `macro/name.ema` 或精确依赖 ID）。可选 `branches={starter:{parameter:"mode"}}`（也可用 config）只声明 choice/boolean 选择器；步骤以 `branch:"starter",case:"dol"` 归属互斥值，禁止跨分支依赖；展开按原顺序保留命中项，重排 dependsOn 的一基索引，空结果、缺值或超限均拒绝（全部备选≤20步、200定位符、替换后参数≤256 KiB）。pack check 校验全部分支及资源闭包；跨包检查须提供原始依赖清单字节，逐项匹配 pack.lock 的版本和摘要，缺上下文即拒绝。applicability 复用类型化谓词；requiredCapabilities 与 acceptance（rule/sample 引用）、依赖文件哈希复核、柜范围及适用性/验收执行由 W2 接入，本次不注册工具或操作。

`Sample.spec={workflow,inputs,configFixture,expectedChecks,expectedObjects}`，只引用包资源与示例派生夹具；输入期望先经过 schema，再在 scratch 副本执行编译/必要生成及验收。未执行的样例或缺关系证据必须 skipped/unknown，不能用“能编译”代替生成验收。

导出采用 `eplan_pack_export`，细化M6原拟build可选导出的入口：保留已发布build的幂等合同，只导出构建成功的安装版本到Host新建导出目录，返回相对路径/字节/文件SHA-256/包摘要；不接收任意输出路径，不覆盖、不解开`.zw1/.zw9`。复用PackArchive的排序/固定ZIP时间和容器限制；导出后重新校验并做导入往返测试。导出失败不撤销已构建版本。

referenced 资源默认仍只带根名/相对路径/哈希，导出报告缺失依赖与可移植性；不自动复制参考项目或网络文件。需要携带时先显式重新导入构建新版本。宏 SourceProject 的本机路径按 S32 提示缺口；能否经 API 清除仍待验证，不私改原生 XML，不把路径抄进清单/结果。签名继续沿 ADR-0024，不能实现校验时不接受/伪造签名。

## 5. 工具合同与级别

全部新工具只进 full，逻辑归 pack/workflow 工具集，不改 core 或顺带实现动态发现。实现前按 [工具规格模板](../templates/tool-spec.md) 分 PR 写完整 schema、四注解、英文模型文案/用法/错误示例和契约快照。本表为必需输入输出；JSON 成员 camelCase，工具参数 snake_case。

| 工具/操作 | 级别；输入→输出与流水线 |
|---|---|
| `eplan_cabinet_list` | Read；project、limit/cursor→柜/结构/配置链/生效值与trace/revision/locks；无绑定回显虚拟 default，不创建文件 |
| `eplan_cabinet_bind` | 本地数据；project、expected_revision（未绑定为0）、整份cabinets、project_values、pack_roots→新绑定/差异；服务器核验精确依赖闭包，改锁先确认；无锁变化仅审计 |
| `eplan_pack_migrate` | 本地数据入口；project、mode（preview/get/apply）、to（preview必填，精确包版本）、report_id（get/apply必填，不能另传to）→差异/样例结果或新revision；preview不改绑定，apply须确认且只换锁 |
| `eplan_rule_check` | Read；project、cabinet、scope、rules（默认锁内正式规则）、fix_plan=false→四值结果/证据/覆盖；fix_plan=true 仅调用 workflow_plan 产生 planId |
| `eplan_module_extract` | 本地数据+导出；reference_asset、pack、name、显式 scope、mode=inspect/materialize、review_digest（后者必需）→FactSet/候选Module/Gap或宏资源；只处理参考材料scratch副本 |
| `eplan_asset_review` | 本地数据，沿 M6 已批准的工程师确认；pack、asset、expected_digest、decision、source→候选状态/新Rule/确认引用；拒绝时内容不变 |
| `eplan_asset_search/get/update` | 沿既有级别；新增配置/工作流和完整模块元数据的 schema；检索返回精确来源/可用性；不新增 module list/get 的重复入口 |
| `eplan_workflow_plan` | Read；保留 project+steps 原调用，新增 cabinet（默认仅单柜可省）、workflow+inputs 与 steps 互斥；编译包工作流/模块为 planId，不执行 |
| `module.insert`（计划 op） | Write；精确 module、parameters/值集、目标页/坐标→插入+参数/部件应用的一个事务；资源/属性全范围 guard，接 plan_apply |
| `placeholder.apply`（计划 op） | Write；稳定占位符 locator、精确值集/参数→引用属性变更；只有 U2 证明引用闭包和逐属性读回的变体才登记 |
| `eplan_pack_export` | Read导出例外（ADR-0012）；pack、version（精确版本）→新导出文件/摘要/可移植性报告；不改项目锁或build合同 |

Read 四注解为 true/false/true/false；本地数据为 false/false/按幂等性/false：bind/review 为同摘要无变化可幂等，migrate和extract为false；export按ADR-0012为false/false/false/false（新文件）。它们不返回 EPLAN execution；目标工程写入只由 plan_apply 报五值。本地数据/确认例外沿 M6 4.5.1，不能拿 Read 注解掩盖换锁。

project 必须显式，绑定有多个柜时 cabinet 必填；rule_check 若要检查整个项目，由 Host 分柜返回独立结果，不合并上下文。列表/规则默认50、最大200条，cursor绑定 revision/快照/规则摘要；采用现有5k/20k双份预算、来源去重、截断/完整证据引用，截断不影响实际求值。Read 查询失败不带 execution。

INVALID_ARGUMENT 带 pointer/violations；缺资源 RESOURCE_NOT_FOUND；缺操作/授权/覆盖 CAPABILITY_MISSING 带 missing；配置冲突 CONFIG_CONFLICT 带两处 trace；陈旧计划/绑定CAS/迁移预览 PLAN_STALE 带 conflicts；包内容/正式来源非法 PACK_INVALID。规则 fail/unknown 是查询成功数据，不伪装工具故障。取消、审计不可用、拒绝和容量沿现有码；发布时同步标准/词表/规格，不新增同义码。

## 6. 接入计划与迁移流水线

1. 编译解析项目/柜→读取完整 binding revision 与锁摘要→校验包/依赖/文件哈希→合并配置→校验参数、适用性、映射及 mandatory 规则→展开工作流。只从锁解析，不能在编译期间升级包。修复工作流将既有fail明确列为待修复前态与必需后验检查，不因该违例拒绝修复计划；输入硬约束、不明影响或所需证据unknown仍阻止编译。
2. 保留大项目局部内容 S、完整名称 N、同名功能闭包与无页证据路径；各步 readScope/modifyScope 与柜范围取交集前必须证明真实影响全部在柜内，不能裁掉越界对象后批准。全项目名称冲突筛查照常保留，不向当前柜结果泄露另一柜配置。
3. 新计划 engineering 固定 `{cabinet,configValuesAndTrace,configDigest,workflowRefAndInputs,moduleBindings,ruleDigests,acceptance}`，连同现有 bindingRevision/lockDigest/resources/sources 进入 planHash。参数/部件最终值、宏哈希、引用属性集合及前置/预期值全部固化，执行时不再依赖选择或重新推断。
4. apply 沿原策略、归属、一次消耗/审批、初备份与 Destructive 独立备份；审批前后及每步重核绑定/锁/资源，步内同一次主线程调用核对引用闭包/实际计划后执行。沿现有 `.binding.lock` 保持到该步结果确定，bind/migrate 与计划使用相同项目互斥，先项目锁后绑定锁，不持锁等待人工确认。
5. module.insert 的 guard 必须覆盖宏内占位符、被引用 Function/Page/Project 属性和部件引用；首批拒绝页/项目级及跨柜引用、不能枚举的图形属性。新增 `module.inspect`、`module.insert.plan/execute` 与加强的 placeholder guard 均为 invoke 内类型化能力；未登记/未实测即 CAPABILITY_MISSING，不改变现有单步工具合同。
6. 一个模块步骤内插入、建临时值集、套用并逐属性核对；不符即回滚；产物绑定宏内相对定位符→实际 locator→源 FactSet/Module/配置/映射。验收读取真实部件与属性，不仅比较值表；后验失败仍保留 committed 事实，首错停止，未知结果按原 outcome/暂停处理。
7. 每步≤50页/10,000明细/200显式locator、guard 10s、计划≤20步/15min、初备份60s、证据/容量限额全部保留。不能证明范围/预测或超限时拒绝，不放宽预算；无通用“宏插入后随意改属性”降级。

迁移preview固定旧revision/锁和全部受影响柜，比较资源增删改、接口/配置值及trace、映射、规则和工作流变化；to替换指定包根版本，其余根保留，层引用按同包同local ID重解析，删除/不兼容时要求显式重绑，不猜新名称。proposedBinding固定新根/锁及对应层版本引用，用户值/结构不改；`PackMigration.From/Changes`只作说明。用CopyProject(Snapshot)制作新空目录中的scratch副本，沿参考副本准备/关闭边界，以新锁跑规则及样例；样例生成仅在该副本走正常计划审批/备份，不写原目标；未通过的mandatory/必需验收阻止换锁。

preview报告可由同一migrate入口的mode=get/report_id查询，但不是授权，24h过期且只属创建stdio会话；reportDigest排除自身，按PlanCanonical覆盖其余不可变字段。apply重新核对源内容/名称证据、revision、依赖及资源哈希，变化即PLAN_STALE要求新preview，不自动重试。工程师看到各柜差异/unknown/验收覆盖后确认，确认绑定报告摘要和新旧锁；随后取锁CAS并原子替换binding，revision+1、审计 workspace_change。确认期间变化必须失效，原项目不执行生成步骤；需要修复时另编新计划。

包版本占用检查扫描所有v1/v2绑定和传递锁；任何绑定不可读时禁止清理，仍被锁定的版本拒绝删除，不新增MCP删除工具。计划核验时发现磁盘上版本被外部移除/替换则拒绝，不宣称可防同用户手工删文件。

V0 实现：`PackOccupancy` 将全部 v1/v2 绑定的显式 locks、v2 包根及柜层交给 `BindingLocks` 核验精确闭包，逐项目解析后合并版本占用；不重新选择版本。无法读取绑定、遍历目录、核实包摘要或传递锁时均禁止清理；无依赖的旧包缺少锁文件时仅以当前清单摘要核对自身。

当前没有已安装版本的公开删除入口；`PackLibrary.RemoveUnusedVersionAsync` 只提供内部维护守卫与带审计的删除，不注册工具或 CLI。安装、内部清理和 v1/v2 绑定发布共用 `.store.lock`，绑定在取得此锁后才取得绑定锁并复核包，避免扫描后新增占用；v2 仍先取项目锁，人工确认期间不持锁。安装新版不修改既有绑定或锁文件。

迁移预览记录按计划同样的同用户路径防护、30d查询保留、1,000份/512MiB准入上限管理，unknown/活跃记录不淘汰，读取不续期；不引入持久化任务。默认同步，可取消且每5s进度；超预算明确中止，未完成preview不发布可apply报告。迁移全部mode及fix_plan/新计划沿阶段2稳定stdio会话限制，Stateless HTTP缺稳定身份时拒绝，不用客户端名称冒充身份。

## 7. 必须先在 EPLAN 验证的门槛

本次不运行 spike。维护者只用示例材料 scratch 副本，结论写 api-notes 并附原始结果；A31 的恢复覆盖、A34 的副本身份、A35/36 的 User.eod/eox 保留规则继续有效。编号复用已有 S36，不把未做项目写成“已完成”。

| spike/依据 | 已知与必须补证；失败处理 |
|---|---|
| S32 / A37（已完成，回滚实验） | WindowMacro.Create/PageMacro.Create、创建占位符、`<变量名>`绑定和写出再插入可行；未证明提交/重开/撤销。新增提取范围、宏内定位映射、源哈希不变与输出完整性验证 |
| S36-module-commit / 阶段2 U2 | 同一真实计划事务中插入+ApplyRecord提交、关闭重开持久性、一次Ctrl+Z、失败回滚不留空撤销；分别测默认/false重载、布尔属性、多语言、部件引用及逐属性实际值；通过前不注册两个计划op |
| S36 引用闭包 | 枚举变量→对象→propertyId，含辅助/额外表达、页/项目/跨柜引用；在guard到execute间制造变化应拒绝；只核对值表不充分。无法证明闭包只保留候选/只读能力 |
| S36 写回缺口 | 新建占位符/值集→WindowMacro.Create→重新打开→再插入→提交→重开→逐项读回，撤销/回滚与导出文件残留分别记录；不能用“原事务回滚后文件仍在”证明目标提交持久性 |
| S32 后续：路径与接口 | SourceProject清除API及清除后哈希/再插入；接口端子/电位/连接点、公司部件映射后的实际引用；PlaceHolderText用途未充分验证，不当参数API使用；未证实者标Gap |
| Cogineer / A37 | 五项授权可用但未安装；只读GetLicenseModules/HasOption，不用LockOption。安装/公开集成路径另做spike，涉及架构/IPC/安全变更另提Proposed ADR，不阻塞轻量模块 |
| A33/A36/A39/A41 回归 | Read中不用需写锁的GetAllCurrentValues；属性列表每对象仅取/释放一次，逐个释放辅助PropertyValue，不Dispose返回的StorableObject；当前项目正确才测撤销，A41七工具撤销结论不外推到模块 |
| 柜结构与规模 | 在已有300/1,000/3,000页示例夹具测结构段/无页归属、跨柜拒绝、名称碰撞和module guard<10s；沿大项目局部证据，不宣称万页已支持 |

## 8. 测试策略与验收

| M6 必测场景 | Small | Medium（Fake Bridge/临时数据目录） | Large / Agent |
|---|---|---|---|
| 1 同输入不同配置 | 五层覆盖/单位/约束交集与完整trace | 同输入在两公司/两柜编译出不同参数/部件且planHash不同 | 示例两柜各执行一次，读回差异符合各自配置；Agent说明来源 |
| 2 柜从不混用 | +A1/+A10、潜在重叠、default拆分、未映射/无页归属 | context/规则/修复/模块跨柜影响拒绝；改绑定后旧计划stale | 跨柜引用的占位符被阻止，另一柜实际对象/配置不变 |
| 3 原生与生成来源 | resource→FactSet→候选→Module→计划source链 | 确认后篡改、候选accepted仍非official；来源失效不生成 | 源参考项目哈希不变；生成locator指回源回路/宏、参数配置及映射；导出再安装链不断 |
| 6 新版不改旧锁 | 精确版本/依赖/摘要；被引用版本不可删除 | 安装v2后旧项目仍用v1；拒绝/超时/验收失败不换锁；CAS竞争原子；损坏绑定时禁止删除 | scratch迁移验证不改原项目，确认后新计划用新锁；旧计划拒绝 |
| 8 错参/缺资源/能力 | pointer/violations、未知键/AST、缺部件/单位/能力 | missing/unknown/预算/超限/缺页；无来源mandatory构建失败 | Agent如实解释缺能力，S36未通过不尝试写入；包文字注入不能转正规则或启用op |

补充Small：AST四值真值表、正则超时、空集合/缺数据、关系跳数/深度/计数上限；依赖冲突、工作流替换与20步上限；确定性ZIP/篡改/路径穿越/多余文件。Medium覆盖审批拒绝/过期/审计失败零改动、批准后资源替换、迁移记录原子性/保留、同会话访问、apply单次消耗/部分完成/unknown暂停和完整契约快照。禁止通过截断结果缩小实际检查范围。

Large材料只来自EPLAN示例/自带宏/基本项目；项目和生成宏不入库，不用客户材料。相关Bridge/写路径PR合并前实跑对应Large，附版本/夹具摘要/结果，测试代码存在不能代替实测。完整三层验收仍待阶段4；本期每项报告passed/failed/unknown/skipped与局部覆盖，不能声称“电气设计正确”。

## 9. PR 计划

估算为生产代码+文档**增删合计**，测试/生成schema/契约快照/夹具按流程不计；目标约200、每PR≤400，超出先拆纯模型/比较器/读适配器，不能省安全检查。本设计与ADR可同一文档PR（合计≤400）；实现等设计合并，落盘部分还须ADR-0029 Accepted。中间PR不注册半成品写操作。

| PR | 一件事（预算行） | 依赖；测试/门槛 |
|---|---|---|
| D | 本设计+Proposed ADR+两处入口（≤400） | 文档链接/差异/行数；不操作EPLAN |
| S1 | S36提交/撤销探针（≤250） | D；Small参数，Large提交/重开/回滚/撤销，记录api-notes |
| S2 | S36引用闭包/写回探针（≤300） | S1；Small定位，Large属性/部件/bool/多语言及文件残留 |
| F1 | v1/v2知识与绑定读取模型（≤250） | ADR接受；Small严格版本/成员与旧文件不变，Medium双版本往返 |
| F2 | schema2计划engineering与读取分派（≤250） | F1；Smallcanonical/旧hash，Medium旧compiler执行分派与篡改拒绝 |
| C1 | ConfigSchema/ConfigLayer校验（≤220） | F1；Small类型/单位/权限层/来源/未知键 |
| C2 | 五层合并与trace（≤240） | C1；Small全部冲突组合，Medium多包schema冲突 |
| C3 | 结构段映射与柜范围解析（≤220） | C2；Small交集/default/无页，Large示例结构对照 |
| C4 | 绑定CAS/锁闭包与审计服务（≤250） | C3；Medium并发/失败/原子替换/同项目锁顺序 |
| C5 | cabinet两工具及多柜context（≤300） | C4；Small注解/预算，Medium契约/隔离/锁变确认，Large只读不改工程 |
| R1 | 规则AST/登记属性与运算符合同（≤250） | F1；Small非法表达式/类型/未知关系 |
| R2 | 有界解释器与四值结果（≤300） | R1；Small真值表/正则/深度/时间，Medium缺数据/取消 |
| R3 | 来源/standing/确认审核服务（≤260） | R1/C4；Small伪来源/摘要变更，Medium确认拒绝/审计失败/导入历史记录 |
| R4 | asset_review及正式规则构建门禁（≤240） | R3；MediumMCP确认/手改standing/无来源build失败，Agent候选不自动转正 |
| W1 | 包内Workflow schema与有限展开（≤250） | F2/C2；Small替换/互斥/递归与步数拒绝，Medium资源闭包 |
| W2 | workflow_plan多柜/规则/来源接入（≤300） | W1/C5/R2/R4；Medium不同配置/越界/旧调用兼容 |
| W3 | apply多柜重核与局部证据（≤280） | W2；Medium批准后变绑定/越界，Large原七步/备份/outcome回归 |
| R5 | rule_check查询与fix计划入口（≤250） | W3；Small预算/注解，Medium修复冲突/unknown不生成，Agent仅计划 |
| M0a | 参考副本prepare/close适配（≤280） | F1；Small路径/来源，Medium空目录/句柄归属，Large版本检测/复制还原/仅副本开关 |
| M0b | 参考提取接缝与实时授权核验（≤220） | M0a；MediumGapOnly替换/缺能力，Large源hash不变/授权不占用 |
| M1 | 完整Module/MaterialMapping/Sample模型（≤280） | F1/C2/R1；Small参数/端口/歧义映射/验收来源 |
| M2 | 只读模块引用/属性inspect（≤280） | S2/M1/M0b；Medium完整性/能力拒绝，Large实际引用闭包对照 |
| M3 | 提取FactSet/模块候选（≤260） | M2；Small来源键，Medium源hash/候选保护，Large参考副本提取 |
| M4 | materialize写回宏与extract工具（≤300） | M3/S2；Medium发布前校验/失败残留，Large写回全链/源不变 |
| M5 | module.insert真实规划与预测器（≤280） | M2/W3；Small范围/有限名称，Medium依赖空页/跨柜拒绝 |
| M6 | 模块事务适配与逐属性读回（≤300） | M5/S1/S2；Medium中途失败回滚/文件替换，Large提交/撤销/部件验收 |
| M7 | placeholder.apply计划guard适配（≤250） | M6；Medium引用新增/页项目属性拒绝，Large实际属性回读 |
| M8 | 模块计划登记与来源/验收联动（≤240） | M4/M6/M7；契约/五值回归，Large三回路→八回路及场景1/2/3 |
| V0 | 版本占用与清理保护（≤180） | F1/C4；Small锁闭包，Medium新版本不动锁/不可读绑定禁止删/被锁版本拒删 |
| V1 | 迁移差异/版本化预览仓库（≤280） | V0/R4/W3；Small依赖/trace差异，Medium原子性/容量/归属/TTL |
| V2 | scratch迁移验证/样例执行（≤280） | V1/M8；Mediumskipped阻止迁移，Large副本完整验收/原目标不变 |
| V3 | migrate工具/确认后CAS换锁（≤260） | V2/C4；Medium拒绝/陈旧/竞争零换锁，Large场景6 |
| E1 | pack_export入口与可移植性报告（≤220） | R4/M4；SmallZIP确定性，Medium导入往返/build合同不变，Large来源路径Gap |
| A | 场景验收/使用规格/发布说明（≤200） | 全部；Small/Medium/契约检查，Large+Agent场景1/2/3/6/8 |

工具规格随对应模型/入口PR交付；若单份规格挤占预算，先独立≤200行规格PR。F1–M8准备组件均保持旧路径可用；W3通过前不启用多柜执行，M8通过前不登记模块op，不能让中间态跳过guard/确认。V3和E1只在依赖齐备后开放。

## 10. 发布、风险与负责人事项

本次CHANGELOG-neutral。实现按新增工具/可选参数次版本发布，兼容阶段2原调用；旧格式只读/旧执行路径保留，未知版本明确拒绝。关闭新工具/新compiler执行可回退，审计、锁、计划证据与备份保留，不自动恢复工程。绑定不随项目移动/复制；继续遵守M6 Q4的数据目录方案。

| 事项 | 风险、建议及负责人/时点 |
|---|---|
| O1 L3落盘 | 项目负责人已于 2026-10-26 接受 ADR-0029。若要求改IPC/进程/支持版本或审批权限，再单独Proposed ADR；ADR-0027富界面仍可延期 |
| O2 首批公司材料 | 负责人在C1/M1前指定非客户示例的配置键、单位、两套部件映射及正式来源；建议以电机起动方式/功率验证差异，不声称通用选型算法 |
| O3 提取语义 | 负责人在M4前确认“显式选定三个回路→审阅参数→补五个实例”作为本期验收；全项目自动找同构回路留阶段4 |
| O4 原生来源路径 | 维护者先验证SourceProject清除API；负责人在E1前决定带Gap的原生宏是否允许对外交换。清单/输出从不含本机路径；未决定时仅本地候选，不对外导出该宏 |
| O5 模块门槛 | 维护者在M6/M8前给出S36及Large证据；失败则模块写入继续不可用，阶段3不报完成，不以已有单步占位符工具或改标识替代 |
| O6 跨柜同包版本 | 建议保持每项目同包单版本；确有混版需求再另设计，不在绑定中悄悄放宽现有locks语义；负责人在C4前确认是否存在该需求 |
| O7 验收/迁移成本 | 负责人在V2前指定必需Sample和可接受的局部覆盖；mandatory unknown/必需样例未跑阻止换锁，不能为赶进度标pass；3,000页预算按实测报告，万页留后续 |

**负责人决定（2026-10-26，均按建议）**：
- O2：首批示例材料由维护者从 EPLAN 示例项目自行编写（以电机起动方式与功率为参数，两套部件映射），不使用客户资料。
- O3：本期验收为“工程师显式选定 3 个回路 → 审阅参数 → 生成 5 个实例”；全项目自动查找同构回路留阶段 4。
- O4：先验证清除 SourceProject 路径的 API；清除不了的原生宏只作本地候选，不对外导出。
- O5：模块写入以 S36 及其 Large 通过为门槛；未通过则保持不可用，不以其他工具替代。
- O6：每个项目同一包只用一个版本，不支持跨柜混版。
- O7：必需验收样例由维护者从示例项目中选定，再交负责人审阅。

安全边界沿既有ADR：包始终不可信，说明/规则消息是数据；只在scratch副本试验；改锁/正式规则仍由工程师确认；规则修复/模块生成仍走计划；事件只留阶段4，插件工作进程只留阶段5。新增引用/原生文件风险通过hash、封闭目录、逐属性覆盖与失败测试处理，不扩大权限。

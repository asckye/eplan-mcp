# Spike S26：受控 action 运行器的首批条目与包装

## 问题

状态：**完成（r1，2026-10-22）**。运行者在 EPLAN 2026.0.3.25421、Electric P8 上执行；本轮已核对 JSON，未运行项与证据缺口留待 r2。

验证 [ADR-0021 第 8 节](../../docs/adr/0021-action-run-whitelist.md) 中尚未验证的 14 种用法，以及四个运行器问题：

1. `ActionCallingContext.AddParameter` 中的 `/`、引号、空格、中文是否保持原值，导出实际生成什么文件名？
2. 省略项目参数时，action 是否只作用于当前项目？
3. action 自己的撤销步骤是否与 Bridge 的 `SafetyPoint → UndoStep` 包装冲突？
4. 数量和文件哈希指纹能否观察到各条目的变化，部件库导出是否改变库？

本目录不实现产品工具、白名单、策略、审批或契约，不据构建成功宣称 action 已验证。

## 方法

沿用 [S34 r3](../S34-m4c-actions/README.md) 的 net481 add-in、`IEplAction`、延迟 WPF Dispatcher、Remote Client 驱动与证据采集。引用来自 `$(EplanBinPath)`，`Private=false`，不复制 EPLAN DLL。

- 调度 action 先返回，工作线程随后调用带时限的 Dispatcher。主线程委托第一步为 `LockingStep`，内部捕获所有异常；主窗口禁用时拒绝开始。进程内互斥标志防止消息循环重入。
- 只接受已经打开、位于 `%USERPROFILE%\EplanMcp\scratch\` 下的 `.elk` 副本；**任意一个已打开项目不满足条件，整个模式拒绝执行**。不创建、复制、打开、关闭项目，不切换当前项目。路径按完整目录边界校验，拒绝 UNC 和 scratch 路径内的重解析点；递归哈希也拒绝链接。
- 每个实际候选独立创建 `SafetyPoint`，始终在 `finally` 显式 `Rollback()`，从不调用 `Commit()`。所有首批条目在 SafetyPoint **内部**创建 UndoStep，再以 `QuietModeStep(ShowNoDialogs)` 执行，记录 `CloseUndo`、释放和回滚异常。外层也有 QuietMode。相对 S34 增加内部 UndoStep 是为了回答问题 3；`runner/undo-s34-control` 保留 S34 的无 UndoStep 对照。关闭 UndoStep 后再显式回滚，不执行 Ctrl+Z 或任何 undo action。
- 内置 action 只用 `new ActionManager().FindAction(name).Execute(context)`，每个参数一次 `AddParameter`。只注册、注销和调度入口用 Remote Client 文本命令；请求 JSON 用 UTF-8 Base64 传输，含特殊字符的导出路径在 EPLAN 进程内生成，避免混入 A30 的 Remote Client 中文损坏问题。
- 不释放任何 EPLAN 返回或项目持有的数据对象，包括 Project、Function、PLC、Article、DeviceListEntry、图层及其项目图层表；不把这些对象跨候选缓存。只释放自行创建的辅助对象（finder、过滤器、context、LockingStep、SafetyPoint、UndoManager/UndoStep、QuietModeStep、PlcService、SchemeSetting）和 .NET 资源。PLC 每个对象只获取一次属性列表，在该列表中读完后释放一次，每个 PropertyValue 用 `using` 并先检查 `IsEmpty`。部件库用无参数 `MDPartsManagement.OpenDatabase()` 读取计数/已有名称，辅助数据库句柄用 `Close()`，不释放库记录。遵循 A36/A39，绝不以 `Dispose` 删除数据。
- 输出、报告、驱动标记全部放入新目录 `%USERPROFILE%\EplanMcp\scratch\spikes\s26\<UTC>-<id>\`；每个候选一个 `<mode>-<candidate>\` 子目录。导出永不使用原文件或方案默认目录。JSON 先写同目录 `.partial` 再原子移动/替换；每个完成候选另更新 `s26-<mode>.json.progress.json`，最终报告为 `s26-<mode>.json`。
- 每次采集 `before`、action 返回后的 `after`（UndoStep 尚未关闭）、`afterUndoStepDisposed`、`afterRollback`、`afterSafetyPointDisposed`。对所有已打开 scratch 项目重新按路径找对象，记录当前项目、打开项目集合，以及 S34 的页、功能、连接、电缆、PLC、PLC 端子、项目部件、部件引用、规划段、注释、设备清单计数；另加端子和端子排计数。不能读取的项保留异常，不把缺失读数算作 0。
- `.edb` 所有文件参与 SHA-256，排除 `*.lck`，**包括 User.eod/User.eox**，另记大小、UTC 修改时间、哈希期间文件是否变化。模式基线最多列 128 文件，每阶段最多列相对模式基线的 4 个差异；完整集合生成内容/元数据摘要。另有排除 User.eod/eox 的摘要供 ADR-0021 第 4 节比较。`fingerprintAssessment` 直接比较每个候选 before 与各阶段，不以模式基线代替候选变化。
- 部件库相关候选同时记录配置值、展开后的本机 `.alk`、`.adb` 文件指纹与六类记录数；记录全部文件及另行排除 User.* 的摘要（A30）。只读当前配置的库，**不传数据库路径，不调用带路径的 OpenDatabase，不切换数据库、不导入、不升级、不同步、不备份/还原库**。SQL、Cloud、网络库或无法识别的库不执行这些候选。
- 每次记录返回值、action/rollback 毫秒数、异常、context 原样读回、输出文件名/大小/SHA-256，不记录导出文件内容。所有输出文件参与哈希，报告最多列 16 文件。按 A11/S34，执行前只保留系统消息数量，收尾新建一个集合读取差异、立即复制为 JSON，最多 6 条并记录省略数，不交叉读取旧集合。每模式报告上限 290 KiB，重复异常去重，必要时截断诊断数组，保留全部候选及参数/返回值。
- 记录 action 返回后和回滚后的主窗口禁用状态，并用只访问 Win32 的 50 ms 定时器采样执行期间窗口状态。`dialogAppeared`、`engineerDialogObservation` 留空供工程师填写：禁用提示模态窗口，但采样可能漏掉短暂或非模态对话框，不能由 `false` 推断从未弹窗。公开 UndoManager/UndoStep 没有历史枚举 API（本机 catalog 已核对），报告明确记为不可读；不猜内部 API。
- 回滚/UndoStep 清理失败、可读业务数量或图层未恢复、主窗口禁用、打开项目集合改变、配置的部件库改变时停止后续候选；报告保留已完成的证据。文件字节未恢复只如实报告，不自动恢复文件或改设置。

参数名严格取 [api-actions 第 4 节](../../docs/eplan/api-actions.md)，包括大小写；`LAYER[n]` 按官方示例展开为 `LAYER1`，没有 TYPE 的 action 不添加 TYPE。额外成员与属性来自仓库 catalog 和安装目录 API XML，只读查证不加载 EPLAN。

| 模式 | 候选 | 时限（整个模式，包括连接与注销） |
|---|---|---|
| `runner` | 图层导出：`slash 子目录/空 格 '单引号' 中文.elc`；另一个含 ASCII 双引号的诊断导出；`changelayer` 的 S34 包装对照、Bridge 包装、无 `PROJECTNAME` 当前项目隔离探针 | 300 秒 |
| `layers` | `changelayer` 从已有未锁定图层选一个，翻转 `VISIBLE1`、读回、回滚；`graphicallayertable TYPE=EXPORT, PROJECTNAME, EXPORTFILE` | 180 秒 |
| `parts` | 六种 `partsmanagementapi TYPE=EXPORTPARTS / EXPORTMANUFACTURERS / EXPORTCONSTRUCTIONS / EXPORTCONNECTIONPATTERNS / EXPORTACCESSORYLISTS / EXPORTACCESSORYPLACEMENTS`，各一个省略属性过滤的候选及一个实际记录名过滤的候选，共 12 个；共同 `EXPORTFILE, FORMAT=XPamExportXml` | 1200 秒 |
| `plc` | 最多两个实际配置项目，每个 S34 的三个 `BUSDATAEXPORT` 候选：标准 XML、通用 AML、通用 AML + `FORMAT=AutomationML AR APC V1.4.0`，固定 `OVERWRITE=0, LANGUAGE=de_DE`；最多两个实际 PLC 元组的 `EXPORTADDRESSOVERVIEW` | 600 秒 |
| `properties` | `projectmanagement TYPE=EXPORTPROPERTYPLACEMENTSSCHEMAS, PROJECTNAME, FILENAME`；`XEsUserPropertiesExportAction Project, XMLFile` | 300 秒 |
| `external` | `XMActionDCCommonExport` 和 `XMExportDCArticleDataAction`，固定 `EXECUTIONMODE=0, IMMEDIATEIMPORT=0, COMPLETEPROJECT=1, CONFIGSCHEME, DESTINATION`；仅前者传 `PROJECTNAME` | 600 秒 |

部件过滤参数为 `PROPERTYID1/PROPERTYVALUE1`。编号依次为 22001、22900、22931、22945、22960、22970，对应 catalog `data-dictionary.jsonl` 中 `ARTICLE_PARTNR`、`PART_ADDRESS_SHORTNAME`、`PART_CONSTRUCTION_NAME`、`PART_TERMINAL_NAME`、`PART_ACCESSORYLIST_NAME`、`PART_ACCESSORYPLACEMENT_NAME`；值从配置库已有记录读取。读不到名称就跳过过滤候选，不猜名称或通配符。无过滤候选用于明确观察默认行为；返回成功但文件为空不算验证成功。

PLC 地址探针从同一个 PLC 属性列表读取 20161（配置项目）、20408（站）、20433（CPU 名列表），并与 `GetPLCConfigurationProjects` 核对。安装目录 XML 说明完整 CPU 名为 `[配置项目].[站].[CPU]`；只在完整前缀准确匹配时去掉前缀，得到 `PLCCPU`。不任意按点切割，不猜站名、CPU、空配置；读不到完整元组记录 **needs input**。地址导出不加文档只允许 BUSDATAEXPORT 使用的 OVERWRITE。

两个外部编辑导出的方案节点未在指定资料中给出，不硬编码猜测。可通过驱动传入工程师已核实的 `--common-scheme-node`、`--article-scheme-node`；插件用 SchemeSetting 只读枚举该节点已有方案，跳过 InternalRefDefScheme，选内部名称排序第一项，记录节点、名称和未扫描数量，不修改最近使用值。未传节点、读取失败或没有方案，记录 **needs a scheme**，不调用 action。输出扩展名跟随实际方案，报告递归列出文件。

### 四个问题的判读边界

- 问题 1：`contextReadBack` 只能证明传入 Execute 前的字符串。实际导出名见 `fileNameCheck`；`/` 按路径分隔符核对，单引号可保留在 Windows 文件名。ASCII 双引号本来就不能出现在 Windows 文件名中，该候选仅观察 action 的拒绝/规范化行为，不能把未生成字面双引号文件归因于二次命令行解析；也不能据此宣布双引号的端到端文件名验证通过。
- 问题 2：先打开至少两个 scratch 副本，在 EPLAN 中单选目标项目。探针省略 changelayer 的可选 PROJECTNAME，记录全部项目同名图层与文件变化；若当前项目读不出或不是目标，跳过且不代为切换。这回答“没有传项目参数”的行为，不推广到文档中完全没有项目参数的其他 Write action；本次不引入额外危险 action。
- 问题 3：所有首批候选实际在内部 UndoStep 中执行，runner 另有无 UndoStep 对照。异常、系统消息、CloseUndo、回滚与下一候选能否运行构成证据；撤销历史不可读时明确标注。**不提交，所以不测试成功提交后的单次 Ctrl+Z**；不重现 A30 的部件库导入。
- 问题 4：结合 `fingerprintAssessment`、图层属性和输出文件判断；图层可见性变化可能不改变任何计数，导出可能只改变 User.*，缓冲可能使哈希暂时不变。无法看到变化就是指纹覆盖不足，不能将无差异当作无副作用或完整回滚证明。

### 有意不执行

不重复已由 S24 验证的 `backup MASTERDATA`，不执行其他 ADR 后续条目、导入（含 PLC）、部件清单 CSV（A25 关闭全部项目）、IMPORTMATCH=1、外部编辑模式 1/2、IMMEDIATEIMPORT=1、设置导出或写入、数据库切换、未知参数、猜测的方案节点/名称。缺 PLC 元组、缺方案、缺部件样本或授权失败均保留为未运行/失败证据，不补造数据。

## 运行方法

在仓库根目录构建：

```powershell
dotnet build spikes/S26-action-run -c Release -m:1 -nodeReuse:false -p:UseSharedCompilation=false -v q
dotnet build spikes/S26-action-run/Driver -c Release -m:1 -nodeReuse:false -p:UseSharedCompilation=false -v q
```

工程师先手工启动 EPLAN 2026.0.3、启用 Remote Client，只打开 scratch 副本并关闭对话框；runner 的隔离探针需要至少两个副本，单选目标为当前项目。驱动不启动 EPLAN、不打开或关闭项目：

```powershell
$project = Join-Path $env:USERPROFILE 'EplanMcp\scratch\writes\S26Copy.elk'
spikes\S26-action-run\Driver\bin\Release\net481\EplanMcp.Spikes.S26.Driver.exe --project "$project" --modes runner,layers,parts,plc,properties,external
```

可选 `--host <机器名> --port 49152`（路径需在两端相同且可访问）。已核实方案节点时，追加 `--common-scheme-node <项目数据方案节点> --article-scheme-node <部件数据方案节点>`；它们必须是已有的 USER 节点，不是 scheme 名或待创建节点。

默认每次生成新 `<UTC>-<id>` 目录。`--output <已有运行目录>` 只用于追加**尚未运行**的模式；驱动检查 `.started`、`.claimed`、报告和候选目录，并以 CreateNew 独占写入 `.started`，插件另以 `.claimed` 防重复。超时或崩溃后不删除标记、不在原目录重跑，换新运行目录。

每个模式在单独驱动子进程中连接、注册、调度、等报告，并在 finally 注销。父进程 watchdog 覆盖所有同步 Remote Client 调用；超时只终止自己的驱动子进程，**不终止 EPLAN**，不排队下一个模式。原生 action 仍可能继续运行，注销也可能尚未完成；工程师检查窗口、系统日志、项目状态，待 EPLAN 空闲后手工注销该 spike。退出码 0 表示报告正常且无跳过/明显失败；1 表示有跳过或 action 失败，2 是子进程的中止后续模式信号。父驱动统一以 0/1 退出，逐项结论仍需检查 JSON。

EPLAN 会话中程序集不能卸载。同一会话修改代码时用新名称构建，例如 `-p:AssemblyName=EplanMcp.Spikes.S26r2`，驱动配 `--revision r2`，或者工程师重启 EPLAN。`--help` 只打印参数，不连接 EPLAN。

## 结果

已核对 `%USERPROFILE%\EplanMcp\scratch\spikes\s26\20261022T1304224670347Z-746d1b3f12744b69bbcce5f83c65fe3e\` 下全部 19 份 `s26-*.json`：六个模式的最终报告、进度报告、驱动报告及总 session。以下耗时取 `elapsedMs`，表中四舍五入；文件大小与指纹取最终报告，没有读取导出文件内容。

| 项目 | r1 证据与结论 |
|---|---|
| 环境 | 运行者提供 EPLAN 2026.0.3.25421、Electric P8（JSON 未记录版本、变体或授权）；JSON 为 `revision=r1`、程序集 `EplanMcp.Spikes.S26`。打开 `scratch\fixtures\EPLAN_Sample_Project.elk`（始终为当前项目）和 `scratch\writes\LargeWrite.elk`（显式目标） |
| 四个运行器问题 | 合法特殊字符路径精确生成；当前项目隔离探针因目标未被选为当前而跳过；changelayer 两种包装均回滚；计数未变时，图层读回与文件哈希能显示变化。双引号端到端文件名、提交后 Ctrl+Z 尚未验证 |
| 每个候选 | 27 个候选中 23 个实际调用：16 个返回 true、7 个返回 false，另 4 个未运行。没有 action、回滚或 UndoStep 清理异常；外部编辑方案发现另有缺节点的 ArgumentException。六模式均无 fatal/stopReason，驱动注册、调度、注销均返回 true |
| 回滚 | 所有实际调用的 Rollback 返回成功，业务计数未变；changelayer 的图层状态恢复，但文件哈希未恢复。未出现停止后续候选的标记 |
| 部件库 | 配置为 `$(MD_PARTS)\EPLAN_parts.alk`；六类记录数依次为 464、11、69、83、39、30。parts 每个候选前后及回滚后，配置、记录数、包含及排除 User.* 的文件内容摘要均未变 |
| 白名单建议 | 已得到 11 种待验证用法的实际调用证据（另有 S24 已验证的 backup）；地址总览和两个外部编辑导出未运行，按 ADR Q2 移到“以后”。AML 导出文件指纹变化与执行期间窗口禁用仍需核实，不能据返回 true 宣称只读条件全部满足 |

**窗口观察的边界**：全部实际调用的 `mainWindowDisabledImmediatelyAfterAction`、`mainWindowDisabledAfter` 都为 false；但 `parts-exportparts-sample` 和三个 PLC 总线导出的 `windowObservation.mainWindowDisabledObserved` 为 true。所有 `dialogAppeared`、`engineerDialogObservation` 均为 null。JSON 不能证明没有出现对话框，也不能仅凭执行期禁用断言出现了模态对话框；应分别保留这两项证据。

### 四个运行器问题

| 问题 | 已核对的结果 |
|---|---|
| (a) 原值传递 | 所有实际调用的 contextReadBack 均 exact=true。合法路径候选返回 true，178.01 ms，生成 `slash 子目录\空 格 '单引号' 中文.elc`（133,157 字节），exactProducedPath=true；证明 `/` 分隔符、空格、单引号和中文在该导出中可用。双引号候选 context 读回仍精确，但返回 false，21.97 ms，0 文件，新增级别 4 消息“路径中具有非法字符。”；Windows 不允许字面双引号文件名，这部分仍不确定，不能作为二次解析的证据 |
| (b) 当前项目范围 | 未运行。当前是 fixture，目标是 LargeWrite，报告明确要求先在 EPLAN 中选中目标；spike 没有切换选择，不能由其他候选推断省略 PROJECTNAME 时的范围 |
| (c) 撤销包装 | `PROJECTNAME=<LargeWrite>, LAYER1=EPLAN100, VISIBLE1=0`：SafetyPoint 对照返回 true，20.09 ms；SafetyPoint 内部 UndoStep 返回 true，4.85 ms，CloseUndo=true。两者均将 visible 从 true 改为 false，再由 Rollback 恢复 true；未见包装冲突。公开 API 无撤销历史枚举，报告标为不可读；没有提交，提交后单次 Ctrl+Z 未测 |
| (d) 指纹覆盖 | changelayer 三次调用的业务计数都不变，但图层与目标项目内容哈希在 after 变化；Rollback 后图层恢复、内容哈希仍不同，列出的差异为 Layer.eod/Layer.eox。另一个打开项目在可比较阶段的数量、图层及文件指纹均未变。图层恢复不能解释为文件逐字节恢复 |

### 首批条目的实际结果

- `changelayer` 独立候选返回 true，16.99 ms（约 17 ms），同样把 EPLAN100 的 visible 从 true 改为 false 并回滚恢复 true；CloseUndo=true，没有新增系统消息。只验证了可见性，不推广到颜色、字高、打印或透明度，也未验证提交后的撤销。
- `graphicallayertable TYPE=EXPORT` 的普通候选返回 true，11.62 ms，生成 `layers.elc`，133,128 字节；合法特殊字符候选为 178.01 ms。两者各一个文件，相对各自 before，两个项目的计数、内容/元数据指纹在执行后与回滚后均未变。普通候选沿用的模式基线已有 changelayer 差异，不能把它算作导出新增变化。
- `partsmanagementapi` 的六类导出共同使用 `FORMAT=XPamExportXml`。省略 `PROPERTYID1/PROPERTYVALUE1` 时全部返回 false、0 文件，每次新增级别 4 消息“输入字符串的格式不正确。”；带实际记录关键属性过滤时全部返回 true、各一个 `parts.xml`，没有新增系统消息。两项目及部件库指纹均未变，不能将不带过滤的调用当作“导出全部”。

| TYPE | PROPERTYID1 / PROPERTYVALUE1 | 无过滤耗时（ms） | 有过滤耗时（ms） | 输出字节 |
|---|---|---:|---:|---:|
| EXPORTPARTS | `22001` / `10 20 206` | 6.95 | 216.16 | 6,834 |
| EXPORTMANUFACTURERS | `22900` / `ABB` | 1.52 | 16.12 | 967 |
| EXPORTCONSTRUCTIONS | `22931` / `A-B.100-C_FS0_I_CH_DP` | 1.21 | 16.84 | 1,019 |
| EXPORTCONNECTIONPATTERNS | `22945` / `3RH2911-1FA22-0MA0` | 1.91 | 28.18 | 9,480 |
| EXPORTACCESSORYLISTS | `22960` / `A-B.100-C_FS0_ACC` | 1.44 | 20.84 | 598 |
| EXPORTACCESSORYPLACEMENTS | `22970` / `RIT.AX_Handle` | 1.99 | 23.56 | 940 |

PLC 总线导出均为 `CONFIGURATIONPROJECT=Phoenix1, OVERWRITE=0, LANGUAGE=de_DE`，三个候选均返回 true、各一个非空文件、无新增系统消息，业务计数均未变。

| CONVERTERID / FORMAT | 耗时（ms） | 文件 / 字节 | 项目文件证据 |
|---|---:|---|---|
| PlcDcXMLExchangerUniversal / 省略 | 3,990.10 | bus.pbf / 431,387 | 两项目相对候选 before 的文件指纹未变 |
| PlcDcAMLExchangerGeneral / 省略 | 2,956.56 | bus.aml / 95,787 | LargeWrite 内容摘要变化，回滚后仍不同；fixture 未变 |
| PlcDcAMLExchangerGeneral / AutomationML AR APC V1.4.0 | 3,151.14 | bus.aml / 104,341 | LargeWrite 相对本候选 before 的内容摘要再次变化，回滚后仍不同；fixture 未变 |

两个 AML 候选排除 User.eod/eox 后的摘要也变化，不能概括为“所有导出都不改项目”。相对 plc 模式基线有 10 个文件差异，报告列出 Blob.eod、Blob.eox、Function.eod、Function.eox，另 6 个省略；数量不变不足以确定哪些业务属性变化，也不能排除缓冲或后台写入。原因与回滚范围需补验。

`projectmanagement TYPE=EXPORTPROPERTYPLACEMENTSSCHEMAS` 返回 true，93.24 ms，生成 `placements.xml`（1,598,040 字节）；`XEsUserPropertiesExportAction` 用 `XMLFile, Project` 返回 true，112.14 ms，生成 `properties.xml`（457,275 字节）。两者均无新增系统消息，两个项目相对各自 before 的计数和文件指纹均未变。

### 未运行项与 r2 所需条件

- runner (b)：将 LargeWrite 在 EPLAN 中选为当前项目，保留另一个打开的 scratch 副本，再验证省略 PROJECTNAME 的隔离；不得用 r1 显式带项目的调用替代该证据。
- `plcservice EXPORTADDRESSOVERVIEW`：配置项目发现只有 Phoenix1，但 PLC 发现记录的 cpus 均为空、tuples=[]。需取得可从样本读取且能匹配的配置项目、站和 CPU 元组；r1 未调用该 action，不能重述为导出失败。
- `XMActionDCCommonExport`、`XMExportDCArticleDataAction`：两个 scheme discovery 的 node 均为空，读方案记录为缺少已核实 USER 节点的异常，候选均为 needs a scheme。r2 需提供各自已核实、能读出已有名称的数据交换方案节点；仍固定 EXECUTIONMODE=0、IMMEDIATEIMPORT=0。缺 CONFIGSCHEME 会弹窗是 api-actions 7.3 的既有文档约束，本轮没有故意触发验证。
- 另需核实四个执行期窗口禁用采样对应的界面行为、两个 AML 导出的文件变化，以及 changelayer 提交后一步撤销；双引号文件名受 Windows 限制，仍不能宣布端到端验证通过。报告中 parts 有 additionalListTruncation=true，详细文件列表有省略，保留完整摘要和比较结果；不从省略内容补造结论。

## r2：下一批 8 行（首轮被基线读取挡住，action 未运行）

本节不改变上面的 r1 结果。候选来自 api-actions 的 137 行有效范围，机器目录均为 `registered`，coverage-report 没有这些 action 行的实机状态；#75、#155 在 r1 因样本/方案缺失未运行。兼容旧导出 #158、#160–164 已排除，不纳入本批。也不重复任务工具已有的图片、DXF、PXF、标签导出，不操作部件库、设置、打印、安装或授权。

下表的项目参数都显式填 scratch `.elk` 完整路径；“风险”是待验证假设，不是已标定级别。六个写候选只在 `SafetyPoint → UndoStep → QuietModeStep` 内执行，显式回滚，永不 Commit；两个导出也保留 r1 的事务包装以观测潜在写入。r2 是独立程序集，`S26_R2` 条件编译不进入 r1 构建，原六模式和默认运行集合保持不变。

| 行 / 候选 | 参数（除 PROJECTNAME） | 预期效果 | 风险 | spike 如何验证 |
|---|---|---|---|---|
| 75 `plcservice/EXPORTADDRESSOVERVIEW` | `TYPE=EXPORTADDRESSOVERVIEW, CONFIGURATIONPROJECT, PLCSTATION, PLCCPU, DESTINATIONFILE=<候选目录>\addresses.csv` | 导出已存在 CPU 的地址 | r1 缺 CPU；A25 空元组只生成空行；格式/授权未验证 | 从 API 读取唯一完整元组，要求 `--address-token` 是已有 PLC 地址；精确单文件、非空、字段含样本地址、SHA-256；两项目语义和设计文件摘要不变 |
| 76 `preparemacros` | `GroupMacroBoxes=1, GroupPageMacros=0, SetHandleActive=0`；不传 PAGENAMEn，按完整宏项目 | 为已有宏框建立分组 | 分组可能改变对象；不推广到页宏或激活抓点 | 仅已有 scratch 宏项目且含宏框；逐个记录宏框名称/组/抓点、组成员；必须观察到变化且回滚恢复，不生成宏文件 |
| 117 `SwitchProjectType` | `PROJECTTYPE=1` 或 `2`，与当前原理图/宏类型相反 | 切换项目类型 | **较高**：安装目录 XML 说明 TypeOfProject 可能临时独占重开，可能使对象/事务失效 | 独立模式和一次性副本；按路径重新找项目，读回目标类型、页面和业务状态；回滚未恢复或项目集合改变即停止，不用反向切换“修复” |
| 118 `synchronize/MULTILINE` | `TYPE=MULTILINE` | 从多线表示同步项目数据 | 跨表示改写；属性覆盖和回滚未验证 | 全量记录功能 ID/名称/页面/功能文本、计数和文件哈希；需含不同表示且功能文本不一致的样本；比较变化与回滚，不同步系统部件 |
| 119 `synchronize/SINGLELINE` | `TYPE=SINGLELINE` | 从单线表示同步项目数据 | 同上，方向不同，单独取证 | 与上一行相同，单独候选/基线/报告；没有功能文本变化记 `needs_effect_sample` |
| 120 `synchronize/OVERVIEW` | `TYPE=OVERVIEW` | 从总览表示同步项目数据 | 同上，不能用其他 TYPE 的结果替代 | 与上一行相同，核对总览源样本的业务变化及回滚；不把计数相等算成功 |
| 133 `UpdateSegmentsFilling` | 无其他参数，不加 TYPE | 重算已有布线段填充率 | 拓扑样本/授权依赖，可能无效果 | 有已有路由的 scratch 拓扑项目；逐段读 `Segment.Properties.CABLINGSEGMENT_FILLING`（#20332），要求值有变化且回滚恢复；不创建路由、不锁授权 |
| 155 `XMActionDCCommonExport` | `COMPLETEPROJECT=1, EXECUTIONMODE=0, IMMEDIATEIMPORT=0, CONFIGSCHEME=<已核实 XML 方案>, DESTINATION=<候选目录>\external.xml` | 纯导出项目数据 | 缺方案会弹窗；方案格式/默认目录、最近使用值和项目副作用待验 | `--common-scheme-node` + `--common-scheme` 精确查已有方案；`--export-token` 必须为已有完整页名/功能名；精确单文件、XML 禁 DTD、元素/属性含样本值、哈希；比较项目及最近使用方案前后值 |

成员依据：仓库 action catalog 和本机 2026 XML；项目类型用 `Project.TypeOfProject`，**不是**业务分类属性 `PROJ_TYPE`。#75 延用 A40 的配置/站/CPU 发现，不猜站名或空 CPU；当前仅接受发现恰好一个元组的样本。#155 的格式必须由工程师先核实为 XML，spike 只读检查已有节点和名字，不创建或修改方案。

### 证据与判读

- `r2-sync`（600 秒）、`r2-type`（180 秒）、`r2-macros`（300 秒）、`r2-filling`（300 秒）、`r2-exports`（600 秒）。前四模式分别含 3/1/1/1 个候选，导出模式含 2 个。时限仍覆盖驱动连接、注册、调度、等待和注销；超时不再排下个模式。
- 沿用 r1 的全部 scratch、链接/联接点、独占标记、主线程、静默、系统消息和回滚检查。任意打开的项目在 scratch 外就拒绝。每个候选开始前必须能完整读取业务快照、计数和稳定文件指纹；不能读的项不当作 0。
- 主报告在 `<run>\` 下，文件名为 `s26-r2-sync.json`、`s26-r2-type.json`、`s26-r2-macros.json`、`s26-r2-filling.json`、`s26-r2-exports.json`，另有 `.progress.json`、驱动报告和 session。每候选输出在 `<mode>-<candidate>\`；完整证据另放同级 `<mode>-<candidate>-evidence\`，避免把证据误算导出。
- 完整证据的 `before.json`、`after.json`、`afterUndoStepDisposed.json`、`afterRollback.json`、`afterSafetyPointDisposed.json` 保存每个项目的选定业务字段、`.edb` 全部文件（除 `.lck`）和 `.elk` 指纹；输出的完整绝对/相对路径、大小、时间及 SHA-256 见 `output-manifest.json`。这些 sidecar 不截断，主报告引用各自路径/哈希；主报告仍遵守 r1 的 290 KiB 上限。JSON 都先写 `.partial` 再移动，不覆盖已有证据。
- `r2Assessment.status=evidence_ready_for_review` 只表示自动检查证据齐备，**不是 verified**。false/异常、Error/Fatal 系统消息（包括显示的前 6 条之后）、context 不精确、空文件/空表头、错误路径、缺样本、无可见效果分别保留失败或待输入状态。导出内容最大 64 MiB；超限不解析、不通过。
- 其他项目的业务字段/设计文件/`.elk` 变化、导出的设计文件/`.elk` 变化、语义回滚不完整、`Settings.xdb` 回滚未恢复、读回/文件哈希失败、最近使用方案变化、窗口收尾禁用都会停止后续候选。目标写操作回滚后其他文件字节不同仍如实保留，不自动复制文件恢复。语义快照只覆盖所列字段，不能证明全部属性未变；采样窗口禁用仍需工程师观察，未采到也不能证明无弹窗。
- 本轮不运行 EPLAN，不改 api-notes 的验证结论，不改 catalog/coverage 的状态。没有提交、提交后一步 Ctrl+Z、撤销历史或产品导出包装去掉事务的证据；它们仍是启用前的验证条件。类型切换不能回滚、宏/拓扑不可用、同步无效果均可成为本批的明确失败/环境缺口，不能为凑白名单数量降级通过。

### 待验证的产品准备

`ActionRunWhitelist.json` 的 `pendingVerification` 存放这 8 个条目，标记 `pending_verification`；Host/Bridge **只读取 `entries`，仍为 12 项**。两个导出固定单文件格式，六个写项的 `write` 仅是待验证建议，不能据此跳过最终分级。新增的 `EplanActionRunner.Pending.cs` 预置项目类型/宏组/功能文本/填充率读回、PLC 元组校验和方案约束，现有白名单无法到达这些分支。

启用某行时应先审视完整实机证据、确定级别并补 api-notes，再把该行移入 `entries`、去掉 pending 状态并更新枚举/契约快照与测试。#155 还必须填写实测 `schemeNode` 和 `verifiedXmlSchemes`；保持空值时 Bridge 拒绝。若最终需要 Destructive，不可直接套用这份 Write 草案，须另接审批/备份流水线。本次准备的产品改动小于 400 行；未启用任何候选。

### r2 历史构建与运行命令（后续使用下方 r3）

以下在仓库根目录执行。构建不启动 EPLAN；r1、r2 均单独构建，Driver 只在 `--help` 以外才可能连接。不要用 r1 的 `--modes` 默认集合来运行本批。

```powershell
dotnet build EplanMcp.slnx -c Release -m:1 -nodeReuse:false -p:UseSharedCompilation=false -v q
dotnet build spikes/S26-action-run -c Release -m:1 -nodeReuse:false -p:UseSharedCompilation=false -v q
dotnet build spikes/S26-action-run/Driver -c Release -m:1 -nodeReuse:false -p:UseSharedCompilation=false -v q
dotnet build spikes/S26-action-run/R2 -c Release -m:1 -nodeReuse:false -p:UseSharedCompilation=false -v q
dotnet build spikes/S26-action-run/R2/Driver -c Release -m:1 -nodeReuse:false -p:UseSharedCompilation=false -v q
```

工程师准备并手工打开相应的一次性副本，保留另一打开的 scratch 副本作隔离对照；所有模式均不代为打开、关闭或选择项目。同步样本需要不同表示间有功能文本差异；宏样本预先就是宏项目；拓扑样本已有路由且填充率待重算；类型切换只用专用副本。缺条件可以运行并获得 `notRun` / `needs_effect_sample`，不补造数据。

```powershell
$driver = '.\spikes\S26-action-run\R2\Driver\bin\Release\net481\EplanMcp.Spikes.S26r2.Driver.exe'
$copies = Join-Path $env:USERPROFILE 'EplanMcp\scratch\writes'
& $driver --help
& $driver --revision r2 --project (Join-Path $copies 'S26Sync.elk') --modes r2-sync
& $driver --revision r2 --project (Join-Path $copies 'S26Type.elk') --modes r2-type
& $driver --revision r2 --project (Join-Path $copies 'S26Macro.elk') --modes r2-macros
& $driver --revision r2 --project (Join-Path $copies 'S26Topology.elk') --modes r2-filling

$commonNode = Read-Host '已核实的 USER 项目数据导出方案节点'
$commonScheme = Read-Host '该节点已有 XML 方案的内部名称'
$exportToken = Read-Host '方案会导出的已有完整页名或功能名'
$addressToken = Read-Host '样本 CPU 的已有 PLC 地址'
& $driver --revision r2 --project (Join-Path $copies 'S26Exports.elk') --modes r2-exports --common-scheme-node $commonNode --common-scheme $commonScheme --export-token $exportToken --address-token $addressToken
```

可选 `--host <机器名> --port 49152` 与 r1 一致。r2 的默认模式集合只含这五个新模式；上述命令分开使用夹具和新运行目录。若任一模式报停止/超时，应先检查 EPLAN，不能继续执行后面的命令。两项导出未提供参数时会记缺输入，不尝试默认方案。r1 的其他未运行项、AML 指纹、一步撤销缺口没有被本批取代。

## r3：修正组读取与执行前拒绝（2026-10-26 已实机运行，结果见下）

### r2 首轮的实际原因

工程师于 2026-10-26 在 EPLAN 2026.0.3 运行 `r2-sync`。证据目录为 `%USERPROFILE%\EplanMcp\scratch\spikes\s26\20261026T1533329577501Z-468f099106c84fe69cce2ae8fe4ed16a\`。已读取最终/进度/驱动报告和候选的完整 sidecar：`before.json`、`afterSafetyPointDisposed.json` 及空的 `output-manifest.json`。

四个项目 `fixtures\EPLAN_Sample_Project`、`writes\LargeWrite`、`writes\S26Sync`、`writes\S26Type` **全部**在 `projects[项目路径].groups.error` 记录 `System.InvalidCastException`：不能把 `Eplan.EplApi.DataModel.StorableObject` 转成 `Group`。每项目的其他快照字段均读出；并非 fixture 独有，也不是文件哈希不稳定。故障来自探针 `.Cast<Group>()` 的类型假设，不能解释为样本组成员不可读。安装目录 XML 明确 `GetAll<T>(bool)` 返回 `StorableObject[]`，没有保证每个托管包装可强转为 T。

第一候选没有 `contextReadBack`、执行耗时、消息书签、CloseUndo 或 Rollback 记录；安全点也未创建。两份 sidecar 中四个项目的选定业务状态及各自 221 项文件记录（220 个 `.edb` 文件及 `.elk`）前后相同，输出文件为 0。r2 将“尚未进入事务”误按“需要确认回滚”处理，才导致 `rollback_unconfirmed` 和后续两项停止。该结果不是同步 action 执行失败或回滚失败，也不增加任何白名单验证状态。

### r3 改动及边界

- 组成员改由 `DMObjectsFinder.GetPlacements(null)` 返回的类型化 Placement 的 `Group` 属性获取，并遍历父组，按 ObjectIdentifier 对应回 `GetAll<Group>` 的完整句柄集合；不强转基类包装、不释放借用对象、不用 `OfType<Group>()` 静默漏掉记录。
- 如某句柄仍无法经该公开属性解析，保留 ID、runtimeType 和明确的 `knownGap`，不当作空组、不列为已完整验证。同步等候选可以保留缺口继续取证，但结果只能是 `needs_gap_review`；依赖完整组成员读回的 `preparemacros` 仍在执行前拒绝。其他未预期的读取异常继续保留为 `readErrors`，原因另列在主报告的 `readFailures` 中，不做通用豁免。
- 新增独立的 `actionAttempted`（紧贴原生 Execute 前设 true）和 `transactionStarted`（成功创建 SafetyPoint 后设 true）。前者为 false 的拒绝归为 `r2Assessment.status=not_run`，同时保留 `notRun`/`reason`；不要求一个从未开始的事务提供回滚记录，因此本次这种拒绝不再停掉后续候选。
- 仍检查窗口、scratch/链接边界、打开项目集合、可读计数/语义/文件/设置的变化、事务和 UndoStep 清理。已进入事务却未确认回滚、scope/cleanup 异常、窗口禁用、项目/状态变化仍停止；实际调用或尝试状态不明时，继续使用 r2 的全部执行后判定，不能凭 `notRun` 标签绕过安全检查。未执行不等于验证通过。
- r1/r2 的程序集和既有结果保留；修复仅由 `S26_R3` 编译进入新程序集 `EplanMcp.Spikes.S26r3`。批次模式仍叫 `r2-sync` 等，报告字段沿用 `r2Readbacks` / `r2Assessment`，顶层 `revision=r3`。r3 的组读取替代路径尚未实机验证，不能据构建成功宣布运行结果。

### r3 构建与重跑命令

在仓库根目录构建新程序集和驱动：

```powershell
dotnet build spikes/S26-action-run/R3 -c Release -m:1 -nodeReuse:false -p:UseSharedCompilation=false -v q
dotnet build spikes/S26-action-run/R3/Driver -c Release -m:1 -nodeReuse:false -p:UseSharedCompilation=false -v q
```

工程师已打开的四个 scratch 项目满足项目范围和隔离对照要求。保持 EPLAN 空闲、关闭对话框，用新驱动重跑本次同步模式：

```powershell
$driver = '.\spikes\S26-action-run\R3\Driver\bin\Release\net481\EplanMcp.Spikes.S26r3.Driver.exe'
$copies = Join-Path $env:USERPROFILE 'EplanMcp\scratch\writes'
& $driver --help
& $driver --revision r3 --project (Join-Path $copies 'S26Sync.elk') --modes r2-sync
```

不传 `--output`，驱动自动新建运行目录；不要删除 r2 原目录的标记或覆盖原证据。结果在新目录 `s26-r2-sync.json`，用顶层 revision/assembly 区分版本。缺少可观察的功能文本变化时仍记 `needs_effect_sample`。其他四模式继续使用上一节各自夹具/导出参数，只将 `$driver` 换为 R3 路径、`--revision r2` 换为 `--revision r3`；不默认运行不适用的宏/拓扑样本。

### r3 实机结果（2026-10-26，EPLAN 2026.0.3.25421，Electric P8）

打开的项目全部在 scratch 内：夹具 `EPLAN_Sample_Project`（当前）、`writes\LargeWrite`、`writes\S26Sync`、`writes\S26Type`（后两者是示例项目的一次性副本）。每个候选都在 `SafetyPoint → UndoStep → QuietModeStep` 中执行后回滚，从不提交。

| 行 / 候选 | 返回 | 观察 | 判读 |
|---|---|---|---|
| 118 `synchronize/MULTILINE` | true | 不弹对话框；回滚后业务快照与文件指纹恢复 | `needs_gap_review`：组成员读取仍有已知缺口，需人工复核证据 |
| 119 `synchronize/SINGLELINE` | true | 示例副本上观察不到业务变化 | `needs_effect_sample`：需要有单线/多线功能文本差异的样本 |
| 120 `synchronize/OVERVIEW` | true | 不弹对话框；回滚后恢复 | `needs_gap_review` |
| 117 `SwitchProjectType` | true（135 ms） | `Project.TypeOfProject` 由 Schematic 变为 Macro；UndoStep 释放后仍为 Macro；回滚后恢复 Schematic，业务快照哈希恢复；其他三个项目计数、文件哈希不变 | `needs_gap_review`；类型切换可在事务中回滚 |

未运行：76 `preparemacros`（需要宏项目样本）、133 `UpdateSegmentsFilling`（需要带布线路由的拓扑样本）、75/155 两项导出（需要工程师核实的方案节点、XML 方案和 PLC 地址）。

本轮只做了回滚验证；**提交后一次撤销、最终级别和白名单启用均未做**，8 个候选继续留在 `pendingVerification`，开放的白名单仍为 12 项。结论见 api-notes A50。


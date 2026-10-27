# Spike S34：M4c 剩余 action 的参数、效果与回滚

> 状态：**完成**（2026-10-22，r1–r3）。

## 问题

1. `generate /TYPE:CABLES`、设备清单和部件清单的导出/导入、PLC 总线数据的导出/导入、PDF 注释导入，需要哪些参数，返回值与实际效果是否一致？
2. 各 action 耗时多少，`QuietModes.ShowNoDialogs` 下是否仍出现对话框？
3. `SafetyPoint.Rollback()` 能否恢复计数，项目 `.edb` 文件哈希是否恢复？相同文件往返导入是否有可观测变化？

## 方法

r2 释放了 EPLAN 拥有的数据模型对象（Function/PLC/DeviceListEntry），导致 EPLAN 因堆损坏（0xc0000374）崩溃；r3 只释放辅助对象。

- 沿用 S32/S33 的 net481 `IEplAddIn`、action、WPF `Dispatcher` 和 Remote Client 驱动结构；EPLAN DLL 从 `$(EplanBinPath)` 引用，`Private=false`。
- 工程师先准备并打开 `%USERPROFILE%\EplanMcp\scratch\` 下的项目副本。插件和驱动都检查完整路径及目录边界；插件按路径查找已打开的项目，不依赖当前选择，不创建或打开项目。
- 调度回调先建立 `LockingStep`，拒绝主窗口已禁用的模态状态，并在 `QuietModeStep(ShowNoDialogs)` 中执行。每个候选单独置于 `using (SafetyPoint safety = SafetyPoint.Create())`；记录异常后仍读回、显式 `Rollback()`，再读回；不调用 `Commit()`。没有 `UndoStep`，与 S32 的仅回滚探针相同。
- 内置 action 一律按 S24 使用 `new ActionManager().FindAction(name)`、`ActionCallingContext.AddParameter`、`Execute(context)`。仅驱动的插件注册/注销和调度入口使用 Remote Client 所需的文本命令。
- 每次记录 `before`、`after`、`afterRollback`、`afterSafetyPointDisposed`，计数包括页、功能、连接、电缆、PLC、PLC 端子、项目部件、部件引用、规划段、注释，以及 r2 新增的设备清单条目 `GetAll<DeviceListEntry>(true)`。成员已对照 `catalog/eplan-2026.0.3/members/` 的 DataModel、EObjects、Graphics catalog：电缆使用 `GetFunctions(FunctionsFilter)` + `FunctionCategory.Cable`，不用受界面方案影响的空电缆过滤；注释使用 `GetAll<Comment>(true)`，其实际覆盖范围仍待实机确认。
- 按 S31 用共享读取方式为 `.edb` 下每个文件计算 SHA-256，排除 `*.lck`，包括 `User.eod`/`User.eox`。模式开始记录基线，每次执行前、执行后、回滚并释放后重新哈希；差异统一相对模式基线。文件可能被 EPLAN 缓冲或后台写入，字节差异不能单独证明业务数据未回滚。
- 每次执行记录返回值、执行毫秒数、回滚毫秒数、异常、系统消息差异和返回后主窗口是否禁用。消息集合不跨探针保留（A11）。r3 读取自动电缆标记时，单个对象的读取只取一次 `Function.Properties`，从同一属性列表读完后释放一次，不在释放后重新取该对象的 `Properties`；返回的 `PropertyValue` 用 `using` 释放，先检查 `IsEmpty`。不创建 `AnyPropertyId`，创建的 `ActionCallingContext` 均由 `using` 释放（A36）。快照跨 action 仅保留 JSON/编号；不释放 finder、属性或集合返回的任何 `StorableObject`（包括 `Article`），也不释放工厂创建的 `DeviceListEntry`，其数据交给外层回滚处理。继续释放自行创建的 finder、过滤器、服务和方案读取器等辅助对象。
- 每个模式新建 `%USERPROFILE%\EplanMcp\scratch\spikes\s34\<UTC时间戳>-<唯一标识>\`；每次导出使用独立子目录。仅成功导出且得到明确的非空文件时，才把该文件原样导回同一项目；导出与每次导入各自回滚。导出失败则记录导入跳过，继续下一个候选。PDF 先从 `/Part` 复制到该目录，再导入副本。
- JSON 报告先写 `.partial` 再原子移动/替换。每模式上限 290 KiB（约 297 KB）：基线最多列 128 个文件，每阶段差异最多 4 个、导出文件最多 16 个、系统消息最多 6 条；所有文件仍参与哈希和总数统计，省略数随报告记录。重复异常通过 `errorRef` 引用顶层 `exceptionDefinitions`；若仍过大，继续截断诊断数组，保留全部候选、参数、action 结果，以及设备清单创建/删除尝试；`entriesOmitted` 随追加截断更新。

### r2 增补探针

- 保留 r1 的五个模式、全部参数候选、计时、计数、文件哈希与回滚步骤。`partslist` 和 `pdfcomments` 的 action 流程不变；PDF 等待工程师提供 Adobe Reader 批注的输入，不在代码中生成或改写 PDF。报告包含版本号（当前 `revision=r3`）与实际程序集名称。
- `cables`：每个候选的 `cablesBefore`、`cablesAfter`、`cablesAfterRollback` 列出最多 200 条电缆，含 `ObjectIdentifier`、全名 `FunctionBase.Name`、可见名 `VisibleName`、`automatic`。自动标记来自 catalog `data-dictionary.jsonl` 的 `Cable.FUNC_CABLEAUTOCREATED=20059`；安装目录 API XML 进一步确认 `FunctionPropertyList.FUNC_CABLEAUTOCREATED` 是布尔值，未设置报告 `null`。以完整编号映射比较，再截断列表：`cableChangesAfter` / `cableChangesAfterRollback` 分别列出改名、出现、消失和自动标记变化的总数及样本。相同名称的旧对象消失、新对象出现仍可被识别。读取不完整时明确记录 `comparisonComplete=false` 或 `unavailable`，不能把缺失读数当作确定的删除。
- `cableSchemes`：catalog 确认 `ProjectSchemeSetting.Init(node, project)`，以及继承的 `GetCount`、`SetScheme(index)`、`GetName` 可用；节点名不是 catalog 中的方法常量，使用 [S21 的只读查证结果](../S21-cables-terminals/README.md)：`CableLog.CableCreateScheme`、`CableLog.CableNumberingScheme`、`CableLog.CableAutoSelectionScheme`。分别读取最多 200 个方案，跳过 `InternalRefDefScheme`，报告最近使用名、异常及未扫描数；不猜新路径、不调用 `SetLastUsed`、不把枚举出的方案加入原候选。
- `devicelist`：原始 r1 往返测试照常执行；每种原格式之后，再追加一个 `deviceListFixtures` 测试。DataModel 的 `DeviceListEntry` 没有公开构造/Create，但 HEServices catalog 有 `DeviceService.CreateDeviceListItem(Project, partNumber, partVariant)` 和 `DeleteDeviceListItem(entry)`。从项目已有部件取最多四个不同部件号/变体，尝试创建一至两条条目；每次创建单独记录，达到两条即停止。**同一个外层 `SafetyPoint` 内**创建条目、导出、仅删除该范围中新出现编号的条目、核实已删除、原文件导入，最后整体回滚，不嵌套 r1 每 action 的 SafetyPoint。报告各阶段设备清单数量及最多八条条目、创建/删除异常、每个 action 的参数/返回值/耗时/消息与共同回滚结果。若工厂失败、没有可用部件或导出仍为空，记录跳过原因，继续其他候选；不创建或写入共享部件库。
- `plc`：每个原导出候选仍测试 `IMPORTMATCH=0`、`2`，随后再试省略 `IMPORTMATCH`；绝不使用 `1`。每次导入前后保存全部功能的编号和全名，并用 `GetPLCs(null)` 标记 PLC 盒；`plcImportChanges` 报告新增功能/盒的总数、其中 PLC 盒数，以及最多 50 条全名/编号/类别（优先列盒）。每条标记是否与导入前已有全名重复、是否与本次其他新增对象重名；空名或读名失败时重复性为 `null`。完整映射用于新增/重复比较，列表截断不影响总数；`comparisonComplete` 和错误样本标明读取缺口。

### r3 释放审查

移除以下六处数据模型对象的释放；这些对象均由 EPLAN/项目持有。设备清单条目的删除仍由显式 API 完成并最终回滚，不通过 `Dispose` 删除。

| 文件 / 方法 | 移除的释放 |
|---|---|
| `SpikeS34Evidence.cs` / `Names` | `using (function)`：`GetFunctions` 返回的 `Function` |
| `SpikeS34Evidence.cs` / `Names` | `using (plc)`：`GetPLCs` 返回的 PLC |
| `SpikeS34DeviceList.cs` / `DeviceListItems` | `using (item)`：`GetAll<DeviceListEntry>` 返回的条目（`Counts` 也调用这里） |
| `SpikeS34DeviceList.cs` / `SeededDeviceListRoundTrip` | 删除循环中的 `using (item)`：finder 返回的条目 |
| `SpikeS34DeviceList.cs` / `SeedDeviceList` | `using (article)`：`project.Articles` 返回的 `Article` |
| `SpikeS34DeviceList.cs` / `SeedDeviceList` | `using DeviceListEntry entry = CreateDeviceListItem(...)`：工厂创建的项目条目同样不释放 |

保留以下 22 处释放，均已核对来源和类型；没有显式 `Dispose()` 调用，也没有创建 `AnyPropertyId`。

| 文件 / 方法 | 保留的 `using` |
|---|---|
| `SpikeS34Action.cs` / `RunLater` | 自行创建的 `LockingStep`、`QuietModeStep`（2 处） |
| `SpikeS34Action.cs` / `Run` | `new ProjectManager()`（辅助管理器，非 `StorableObject`；1 处） |
| `SpikeS34Action.cs` / `Plc` | `new PlcService()`（1 处） |
| `SpikeS34Action.cs` / `Probe` | `SafetyPoint.Create()`（1 处） |
| `SpikeS34Action.cs` / `Counts` | `new DMObjectsFinder(project)`、`new FunctionsFilter`（2 处） |
| `SpikeS34Action.cs` / `ExecuteBuiltIn` | `new ActionCallingContext()`（1 处） |
| `SpikeS34Action.cs` / `Files` | .NET `FileStream`、`SHA256.Create()`（2 处） |
| `SpikeS34Action.cs` / `FileSummary`、`MainWindowDisabled` | .NET `SHA256.Create()`、`Process.GetCurrentProcess()`（2 处） |
| `SpikeS34DeviceList.cs` / `DeviceListItems` | `new DMObjectsFinder(project)`（1 处） |
| `SpikeS34DeviceList.cs` / `SeededDeviceListRoundTrip` | `SafetyPoint.Create()`、`new DeviceService()`、删除循环的 `new DMObjectsFinder(project)`（3 处） |
| `SpikeS34Evidence.cs` / `CableSchemes` | `new ProjectSchemeSetting()`（1 处） |
| `SpikeS34Evidence.cs` / `Names` | `new DMObjectsFinder(project)`、`new FunctionsFilter`、只获取一次的属性列表 `function.Properties`、从该列表读取的 `PropertyValue`（4 处） |
| `Driver/Program.cs` / `Run` | 自行创建的 `EplanRemoteClient`，用于释放驱动的连接资源（1 处） |

### 候选参数与依据

所有候选均显式带 `TYPE` 和目标 `PROJECTNAME`。以下为实际尝试顺序；各候选的效果与验证范围见结果和局限。

| 模式 | 候选 | 原因与来源 |
|---|---|---|
| `cables` | ①省略可选参数；②三个方案名均为空，`KEEPOLDNAMES=1, STARTVALUE=1, STEPVALUE=1, ONLYAUTOCABLES=1`；③省略方案，`KEEPOLDNAMES=0, STARTVALUE=5, STEPVALUE=2, ONLYAUTOCABLES=0` | [generate 文档](https://www.eplan.help/en-us/infoportal/content/api/2026/generate.html)：方案默认取最近使用值；②比较空字符串与省略的行为，③取官方示例以便产生可观测差异。无凭空编造的方案名。 |
| `devicelist` | 导出/导入的 `FORMAT`：①省略；②`XDLXmlExporter`；③`XDLCsvImporterExporter` | [devicelist 文档](https://www.eplan.help/en-us/Infoportal/Content/api/2026/devicelist.html)：默认 XML 及明确列出的 CSV 转换器；每种格式都往返同一文件。 |
| `partslist` | `FORMAT`：①省略；②`XPalXmlExporter`；每次导出分别测试导入 `MODE` 省略、`1`、`2`；`FIELDASSIGNMENTSCHEME` 省略 | [partslist 文档](https://www.eplan.help/en-us/Infoportal/Content/api/2026/partslist.html)：默认 XML、默认 `MODE=0` 只追加、`1` 只更新、`2` 更新并追加；字段方案可选且名称未知。S25 的 CSV 导出后曾出现访问冲突，故只测 XML。**绝不使用 `IMPORTTOSYSTEM`**。 |
| `plc` | ①`PlcDcXMLExchangerUniversal`，省略 `FORMAT`，`.pbf`；②`PlcDcAMLExchangerGeneral`，省略 `FORMAT`，`.aml`；③通用 AML + `FORMAT=AutomationML AR APC V1.4.0`；导入分别 `IMPORTMATCH=0`、`2`、省略 | [plcservice 文档](https://www.eplan.help/en-us/infoportal/content/api/2026/plcservice.html)：标准交换、AML 及示例格式；`0` 按内部编号匹配，`2` 全部新建；r2 增加省略参数以比较默认行为。**绝不使用 `IMPORTMATCH=1`**。 |
| `pdfcomments` | `TYPE=PDFCOMMENTS, PROJECTNAME=<副本>, IMPORTFILE=<PDF副本>`，无猜测参数 | [import 文档](https://www.eplan.help/en-us/Infoportal/Content/api/2026/import.html)：该类型必须给 `PROJECTNAME`；输入由工程师通过 `--part` 提供。 |

PLC 使用 `PlcService.GetPLCConfigurationProjects(project)` 枚举后按序最多测试两个实际配置项目；没有配置或枚举失败时，以空 `CONFIGURATIONPROJECT` 作显式失败/默认行为探针，不能据此判定有数据可导出。共同带 `LANGUAGE=de_DE`（官方示例）和导出的 `OVERWRITE=0`（新目录无需覆盖）。其余导出选项省略，使用当前界面默认值；导入不传仅对导出有效的 `FORMAT` / `CONFIGURATIONPROJECT` / `OVERWRITE`。r2 每个配置最多 12 次 action 调用。

### 构建与运行

在仓库根目录构建 r3；同一 EPLAN 会话内后续修改再换新程序集名（已加载程序集不能卸载）。`--revision rN` 必须与构建名后缀相同。

```powershell
dotnet build spikes/S34-m4c-actions/EplanMcp.Spikes.S34.csproj -c Release -m:1 -nodeReuse:false -p:UseSharedCompilation=false -v q -p:AssemblyName=EplanMcp.Spikes.S34r3
dotnet build spikes/S34-m4c-actions/Driver -c Release -m:1 -nodeReuse:false -p:UseSharedCompilation=false -v q
```

工程师手动启动 EPLAN、启用 Remote Client 并打开 scratch 副本，关闭现有对话框后执行：

```powershell
$project = Join-Path $env:USERPROFILE 'EplanMcp\scratch\writes\S34Copy.elk'
$pdf = Join-Path $env:USERPROFILE 'EplanMcp\scratch\inputs\review-comments.pdf'
spikes\S34-m4c-actions\Driver\bin\Release\net481\EplanMcp.Spikes.S34.Driver.exe --revision r3 --modes cables,devicelist,partslist,plc,pdfcomments --project "$project" --part "$pdf"
```

可用 `--modes` 选择子集；不运行 `pdfcomments` 时可省略 `--part`。可选 `--host <机器名> --port 49152`；本驱动按本机路径准备文件，远程运行需共享相同可访问路径。PDF 应由本项目导出并带批注，保留页面关联信息。驱动不启动 EPLAN、不复制项目、不自动打开或关闭项目；注册在 `finally` 注销，报告和导出文件保留供核对。

报告位于 `.local\spikes\s34\<UTC时间戳>\s34-<mode>.json`，另有 `s34-driver.json`。部分候选失败是探测结果，应逐项检查；驱动成功退出也不代表回滚完全。调度等待上限 1800 秒，驱动等报告 1860 秒，无报告就停止排队并尝试注销。超时不能中止已运行的原生 action，进程崩溃也无法在托管代码中捕获；此时工程师记录对话框、系统日志及项目状态。

`engineerDialogObservation` 留空供运行者记录：是否弹窗、标题、出现在哪个候选、是否需要干预。`ShowNoDialogs` 和返回后的窗口状态不能单独证明执行中从未出现对话框；本次结合工程师现场观察核对。相同数据往返可能没有计数差异，计数相同也不能证明所有属性恢复；已验证的结果及范围见下文。

指定的编译检查命令（不运行驱动）：

```powershell
dotnet build spikes/S34-m4c-actions/EplanMcp.Spikes.S34.csproj -c Release -m:1 -nodeReuse:false -p:UseSharedCompilation=false -v q
dotnet build spikes/S34-m4c-actions/Driver -c Release -m:1 -nodeReuse:false -p:UseSharedCompilation=false -v q
```

## 结果

环境：2026-10-22，EPLAN 2026.0.3.25421，Electric P8，在主线程调度器中运行；目标为 `scratch\writes\LargeWrite`（示例项目的写副本，321 页）。基线为 2,876 个功能、20 个 PLC 盒、78 根电缆、2,900 个部件引用、0 条设备清单条目、0 条注释。

已核对 `.local\spikes\s34\<timestamp>\s34-<mode>.json`：r1 的 `082719`（电缆）、`082805`（设备清单）、`082820`（部件清单）、`083753`（PLC）、`083833` / `084016` / `084038`（PDF）各次运行，以及 r3 的 `093527`（电缆）、`093537`（设备清单）、`093601`（PLC）各次运行；时间均取 2026-10-22 UTC 目录名。`093417`–`093420` 的三份短报告因目标项目未打开而拒绝执行，没有 action 结果，不计入以下数字。r2 原生崩溃发生在报告生成前，崩溃信息来自工程师的实机记录。

完成执行的 action 都在 `SafetyPoint` 中，显式 `Rollback()` 恢复了报告可观测的名称、条目和业务计数；工程师未观察到对话框，执行后的主窗口也从未被禁用。r2 的进程崩溃另列如下，不能视为一次正常回滚。

### 电缆：生成也会重编号已有电缆

创建、编号、自动选型三个方案节点各只有 `Standard`，最近使用值也均为 `Standard`。示例项目没有自动电缆；78 条快照中的 `automatic` 均为 `null`（属性未设置）。r1 只有计数，看不出变化；r3 按对象编号比较名称后得到：

| 参数 | 执行后 | 回滚后 |
|---|---|---|
| 省略可选参数 | 78 根电缆，0 个名称变化 | 与执行前相同 |
| 空方案名，`KEEPOLDNAMES=1, STARTVALUE=1, STEPVALUE=1, ONLYAUTOCABLES=1` | 78 根电缆，0 个名称变化 | 与执行前相同 |
| `KEEPOLDNAMES=0, STARTVALUE=5, STEPVALUE=2, ONLYAUTOCABLES=0` | **78 根中的 70 根改名**，如 `+B1-WG2` → `+B1-W7`；无新增、无删除 | 70 个名称全部恢复，新增/消失/改名数均回到 0 |

三个候选均返回 `true`，耗时约 **0.2–0.7 秒**，没有系统消息。因此不能用“数量没变”判定生成 action 没有效果。

### 设备清单：有内容的 XML / CSV 往返成立

原设备清单为空：默认和显式 XML 导出均为 64 字节的 `<deviceList count="0"/>`；CSV 为 0 字节，驱动跳过空 CSV 导入。导入空 XML 返回 `true`，没有变化。

`DeviceListEntry` 没有公开构造函数，但 `DeviceService.CreateDeviceListItem(project, partNumber, variant)` 可以从项目已有部件创建条目。r3 对每种格式创建两条后，在同一个 `SafetyPoint` 内导出、删除刚创建的条目、再导入同一文件：

| `FORMAT` | 导出文件 | 条目数：原始 → 创建 → 删除 → 导入 → 回滚 | 创建 / 导入后的对象编号 |
|---|---|---|---|
| 省略（默认 XML） | 899 字节 XML | 0 → 2 → 0 → 2 → 0 | 1、2 / 3、4 |
| `XDLXmlExporter` | 899 字节 XML | 0 → 2 → 0 → 2 → 0 | 5、6 / 7、8 |
| `XDLCsvImporterExporter` | 1,400 字节 CSV | 0 → 2 → 0 → 2 → 0 | 9、10 / 11、12 |

各 action 返回 `true`，r1/r3 单次约 **20–150 毫秒**。导入重新创建了条目，不能把原对象编号当作导入后仍有效的编号。删除使用 `DeleteDeviceListItem`，新建条目和 finder 返回的对象都不调用 `Dispose`。

### 部件清单：可导入、可回滚，但耗时长

- 省略 `FORMAT` 和显式 `XPalXmlExporter` 均导出 **13,970,204 字节（约 14 MB）XML**，分别耗时约 51、64 秒。
- 两种格式各测试导入 `MODE` 省略、`1`、`2`，均返回 `true`，单次耗时 **58–83 秒**；每次 `articleReferences` 都从 **2,900 → 2,902 → 回滚恢复 2,900**。功能、页及 PLC 盒计数不变。
- 导入时 EPLAN 只读打开配置的部件数据库。没有使用 `IMPORTTOSYSTEM`；CSV 未重试，因为 S25 曾在部件清单 CSV 导出后发生访问冲突。

### PLC：自己的导出也不能静默匹配回已有设备

配置项目为 `Phoenix1`。以下三种导出均返回 `true`；r1 耗时 2.1–2.4 秒（约 2.3 秒），r3 复测为 1.3–1.8 秒。

| 转换器 / `FORMAT` | 文件大小 | 导入后的功能数 | 导入后的 PLC 盒数 |
|---|---:|---:|---:|
| `PlcDcXMLExchangerUniversal`，省略格式 | `.pbf` 431,387 字节（约 431 KB） | 2,876 → 3,326（新增 450） | 20 → 34 |
| `PlcDcAMLExchangerGeneral`，省略格式 | `.aml` 95,793 字节（约 96 KB） | 2,876 → 2,913（新增 37） | 20 → 33 |
| 通用 AML，`AutomationML AR APC V1.4.0` | `.aml` 104,347 字节（约 104 KB） | 2,876 → 2,913（新增 37） | 20 → 33 |

每种导出都把**项目自己的原文件**导回同一副本，`IMPORTMATCH=0`、`2`、省略均返回 `true`，但都新增对象：

- 标准格式在 `0` 或省略参数时，新增的 **450 个功能全名全部与原对象重复**，包括第二个 PLC 盒 `=F+A2-KE1`；`2` 仍新增 450 个功能，但改用 `=+-K1`、`=+-K10` 等名称，r3 的“与原全名重复”计数为 0。它同样没有更新匹配原设备。
- 两种 AML 格式在三个匹配候选下都新增 37 个功能，其中 **36 个全名与原对象重复**，也包含 `=F+A2-KE1`。
- 标准格式的 `IMPORTMATCH=2` 另外记录系统消息“标记一个或多个对象以将其删除.”，但没有弹出对话框。`IMPORTMATCH=1` 会走比较对话框，始终未使用。
- 每次回滚后功能数恢复 2,876、PLC 盒数恢复 20，部件引用也回到 2,900。不能把 `Execute=true` 理解为按编号匹配并更新成功。

### PDF 注释：pypdf 输入被拒，Acrobat 样本仍缺

- 未修改的 EPLAN 自导出 PDF 导入返回 `true`；注释计数保持 0，没有可导入的批注。
- 用 pypdf 添加注释后，无论全量重写，还是保留原字节、trailer `/ID`、EPLAN Info 键（`/ProjectId`、`/PageOffset` 等）的增量更新，均返回 `false`，系统消息为“PDF 文件无法由 Eplan 生成并因此不能导入.”。注释计数仍为 0，没有对话框。
- 尚未试过 Adobe Reader / Acrobat 添加批注的 PDF，等待项目负责人提供；不能据此断言 EPLAN 不支持 PDF 批注导入，也不能宣称批注内容已成功往返。
- 2026-10-27 r4（构建名 `S34r4`，代码同 r3）：工程师在 Adobe Acrobat Reader 中给同一份自导出 PDF 加 3 条批注（第 1 页便笺；第 2 页带文字的插入标记和下划线），Reader 增量保存。导入返回 `true`（约 0.3 秒），无对话框和系统消息；`Comment` 计数 0 → 1，其他计数不变，回滚后回到 0。只导入 1 条，具体是哪一条及提交后的行为见后续探针（api-notes A39）。

### r2 原生崩溃与 r3 修正

🔴 r2 在 `cables` 模式开始约 5 秒后崩溃，Windows 原生异常为 `ntdll.dll` 中的堆损坏 **0xc0000374**，没有 .NET 异常，也没有生成报告。原因是释放了 EPLAN 拥有的数据模型对象（`Function`、PLC、`DeviceListEntry`）；`Counts` 也会枚举并错误释放设备清单条目。

r3 移除了六处数据模型对象的释放（含 `Article` 和工厂创建的 `DeviceListEntry`），保留上文列出的辅助对象和 .NET 资源释放。r3 的电缆、设备清单、PLC 探针完成；**EPLAN 返回的 `StorableObject` 不归调用者释放**，不能因为它实现 `IDisposable` 就套用 `using`。属性值及读取辅助对象仍须按其生命周期释放，属性列表一次获取、读完释放一次。

## 结论

1. **`generate/CABLES` 在 `KEEPOLDNAMES=0` 时会重编号已有电缆**，属于 Destructive，必须先备份；电缆数量不变不能作为无改动的证据。
2. **设备清单导入可用，输入需要实际条目。** 可用 `DeviceService.CreateDeviceListItem` 准备非空清单，XML 默认/显式格式和 CSV 均完成导出、删除、导入、回滚；条目对象编号会改变。
3. **部件清单 XML 导入可用且能回滚，但必须按分钟级调用设计长超时。** 单次导入约一分钟，导出加一次导入约两分钟；完整三种导入候选的探针耗时更长，需要进度与足够时间余量，继续避开 CSV。
4. **PLC 导入只能限制在目标项目尚不包含这些设备的场景。** 对已有设备项目，自导出再导入也会新增对象；本次没有找到不经比较对话框即可可靠匹配更新的方式，不能把它做成已有设备的静默更新工具。
5. **PDF 批注导入先取得 Acrobat / Reader 批注样本再写工具规格。** 未修改 PDF 的 `true` 只说明容器被接受；pypdf 生成的两个版本均未通过 EPLAN 校验。
6. **释放规则按所有权区分。** 不释放 EPLAN 返回或项目持有的 `StorableObject`；只释放自己创建/持有的辅助对象。r2 的原生堆损坏无法被托管 `try/catch` 或 `SafetyPoint` 挽救。

## 局限

- 仅验证这一份 321 页示例项目副本、当前安装和授权；没有自动电缆的真实生成场景，也没有其他创建/编号方案，`automatic=null` 仅说明属性未设置。
- 回滚结论覆盖报告可读回的名称、条目及计数，不是逐项属性全量校验。`.edb` 字节没有全部恢复：r3 电缆模式结束时仍有 18 个文件相对基线哈希不同；不能把业务回滚等同于文件逐字节还原，也未测试这些 action 提交后的 Ctrl+Z。
- 设备清单仅测试两条已有部件的往返；大清单、已有条目冲突、外部系统文件和未知字段方案未覆盖。部件清单只测 XML，`FIELDASSIGNMENTSCHEME` 未指定，不推广到 CSV 或共享部件库写入。
- PLC 的输入都是本项目自己的导出，未验证空目标或尚不含这些设备的项目中的完整导入效果，也未测试其他厂家转换器；`IMPORTMATCH=1` 的交互比较不在本 spike 范围内。禁止静默更新已有设备的限制不会因 `0` 的文档名称是“按内部编号匹配”而放宽。
- 缺少 Adobe Reader / Acrobat 批注样本，尚未验证真正的批注新增、页面关联、内容读取和回滚；pypdf 校验失败的具体内部原因未查明。
- 本次无对话框的结论来自完成执行的候选和现场观察，不推广到其他授权、方案或输入；r2 崩溃发生在写报告前，说明原生进程故障不能依赖最终 JSON 或 SafetyPoint 提供恢复保证。

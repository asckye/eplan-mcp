# [EPLAN API 笔记](../api-notes.md)：通用入口与批量验证

`eplan_api_try` 的句柄、预算、生命周期和批量验证中观察到的事实。

### A67. 设置只读适配入口（2026-11-01，仅文档核对，不运行 EPLAN）
- [2026 设置文档](https://www.eplan.help/en-us/Infoportal/Content/api/2026/WorkingWithSettings.html) 与本机 Baseu/DataModelu XML：SettingNode 以点分路径绑定，GetListOfNodes/GetListOfSettings/GetListOfAllSettings 用 ref StringCollection、false 输出相对名称；ProjectSettingNode(Project, string) 的路径可为空，Project.Settings 等同 ProjectSettings(Project)。项目设置通过显式 Project 绑定，不依赖当前项目。
- SchemeSetting.Init 绑定已有方案节点；GetCount/GetLastUsed 读取方案信息，GetNodeHandle 返回当前方案的数据节点，设置值路径相对该数据节点。SetScheme 会切换接收器方案（不改 LastUsed），本片仍全部禁止；PROJECT 下仅内部用 ProjectSchemeSetting.Init(path, project) 绑定。所有临时句柄及 MultiLangString 及时释放，原生对象不跨请求。以上只作 C 级编译入口依据；无副作用、方案初始化、撤销均待 Large 实机验证，不能据此宣称 live_verified。
- 2026-11-01 Large 失败定位（2026.0.3，Electric P8，LargeWrite scratch 副本，串行）：`LargeWrite_SettingsReadsPreserveFilesAndSingleUndoForProjectWrite` 在第一个 `Project.Settings` 调用（`ApiTryTests.Settings.cs:13`，仅传项目 locator）即被 Host 拒绝，返回 `POLICY_DENIED/not_started`，没有 correlation ID，尚未调用后面的 GetListOfNodes、Page.Name 或 AllowedLetters。生成器把只读属性名 `Settings` 错当成 `Set` 前缀写方法，`catalog/api-try.g.tsv` 因而标记 `settings_write`、允许表不含该组；不是组名错误或 EPLAN 异常。修复限定写方法的分类只作用于 method，getter 仍只接受 target，设置写入口继续排除。— 证据：`.local/failures/q-op-settings-ApiTryTests.log` 的失败堆栈；`.local/failures/bridge-slotA.log` 本分支 06:04–06:09 段无此拒绝异常，06:07:10 的 `3f589c3c15d541c6b493422d5ded46d6` 属于另一个已通过测试中的 NameService 读取拒绝（A63）。此次仅离线回归，设置读取的文件不变与撤销验证仍待重跑 Large。
- 2026-11-01 重跑（EPLAN 2026.0.3.25421，Electric P8，LargeWrite scratch 副本，A 槽串行）：ApiTryTests 9/9 通过，含 `LargeWrite_SettingsReadsPreserveFilesAndSingleUndoForProjectWrite`；设置读取文件不变与项目写入的单步撤销已实机确认。— Large 测试 TRX 计数。
- 2026-11-01 链式分支回归（负责人提供的实机摘要，EPLAN 2026.0.3.25421、Electric P8、LargeWrite scratch 副本）：`681b40a3` 的 ApiTryTests 为 11/12，main `c13de0fe` 对照为 11/11；同一设置测试在第 13 行、组 `O:Eplan.EplApi.DataModel.Project.Settings`、仅项目 locator 的 target 输入下，value 从字符串变为 JSON 对象。离线检查确认句柄分发抢先返回 ProjectSettings，而 A67 适配器仍返回 `PROJECT`；Small 链接实际分发与转换方法，以脱敏项目 locator 复现了 string / ApiObjectHandle 类型差异。修复让该设置入口优先走文本适配器，保留非设置句柄与集合；本次不运行 EPLAN，修复后的文件不变与撤销仍须复验。— `ApiTryResultAdapterTests`；原始 locator 字段未提供。

### A68. api_try 返回 Pin 句柄的 scratch 归属检查（2026-11-01，EPLAN 2026.0.3.25421，Electric P8，LargeWrite scratch 副本，串行、单实例）
- `LargeWrite_PinHandleReadAndWriteUseOneUndoStepAndPreserveEarlierHistory` 实际失败在首次 `Connection.StartPin` 返回句柄时，尚未执行 `Pin.IntrinsicSafetyPossible`。`.local/failures/q-op-handles-ApiTryTests.log` 的堆栈指向 `ApiTryTests.Handles.cs:12`；同日 `bridge-slotA.log` 的 06:13:30.396、correlation ID `b4a0e98f18f74d83ba96474196b0638f` 为 `FileNotFoundException → File.GetAttributes → EplanApiTryDispatcher.Scratch → InvokeHandleMember`：Bridge 把 A10 的无扩展名 `ProjectFullName` 当作现存文件检查。
- 该异常被只读失败边界包装成 `EPLAN_OPERATION_FAILED / rolled_back`，不能据此断言 Pin 属性需要 UI 上下文。06:12:21.460 的 `fe047fdfe3534a609e7ffd6ef3eae6bc` 是 A63 中 `NameService.CanDeviceTagBeNested(Function)` 的 S029128 拒绝，属于另一条服务测试。— 同上两个日志与 `ApiTryTests.ScratchCopy_ServiceReadLeavesBusinessAndDesignFilesUnchanged`。
- 修复以实际 `ProjectLinkFilePath`、同名 `.edb` 和各级父目录复核归属与链接；Small 用无扩展名不存在、`.elk` 存在的输入复现。本次仅离线验证，Pin 属性读写及单步撤销仍待实机复验，原 Large 断言保留。
- 2026-11-01 重跑（EPLAN 2026.0.3.25421，Electric P8，LargeWrite scratch 副本，A 槽串行）：ApiTryTests 9/9 通过，含 `LargeWrite_PinHandleReadAndWriteUseOneUndoStepAndPreserveEarlierHistory`。— Large 测试 TRX 计数。
- ADR-0033 链式片（2026-11-01，仅目录、本机 XML 与编译核对）：`Pin.Connections` 返回以该连接点为端点的连接；`Project.Settings` 返回 ProjectSettings。`GraphicalLayerTable.Layer` 与 `UniversalPropertyList.Property` 是默认索引器，须生成 `receiver[key]`；`PlaceHolder.NameOfRecord/Value` 是可用 `get_...` 调用的命名参数属性。PlaceHolder.Variables/ValueSets 是类型层字段，未登记为操作组，本片只在来源缺口中列出。— `catalog/eplan-2026.0.3/`、本机 `Eplan.EplApi.DataModelu.xml` 对应成员及 Bridge 编译；链式行为与撤销仅编写 Large 测试，未实机验证。

### A69. 生命周期观测的范围与预算（2026-11-01，EPLAN 2026.0.3，Electric P8，LargeWrite scratch 副本）
- C1 生命周期片（原记在 C1 条目下，变基时并入此处）（2026-11-01，仅核对目录和本机 XML，不运行 EPLAN）：`Graphics.Line.Create(Page, PointD, PointD)` 与 `Graphics.PlaceHolder.Create(Page)` 将无参构造的对象存入指定页；CreateTransient 的文档明确说明不存入项目数据库，不能充当跨调用工厂。`E3D.Drilling.CreateContour(Project, string strContureName)` 的字符串实际是完整 `.fc1` 文件路径，按文件排除。工厂分派复用原目录 `O:<Type>.Create`，不新增操作组；参数省略 `target`，Bridge 无参构造后调用 Create，结果沿 Detach 返回可发现对象的定位器或受支持句柄。传入既有实例的 locator/handle 作为 `target` 在 Host 和 Bridge 返回 `INVALID_ARGUMENT/not_started`，提示省略 target，不调用原生成员；try_template 同样省略 target，审批保留 creates 与 undo unverified。此为负责人指定的分派修正，仅离线验证；公开构造 + Create 的一步撤销仍未验证；提交后按 A55 重新枚举，不保留事务内句柄。— `Eplan.EplApi.DataModelu.xml` 对应成员。另于同版本本机 XML 与已提交构造元数据核对：GraphicalLayer 的无参构造生成未附着对象，Create(table,name,description) 将新层关联到指定表；HandleMate 的无参构造及 Create(Mate) 同样有公开签名。它们沿用独立可达句柄表，不增加句柄根；这些工厂的撤销与归属实机结果仍待验。
- `ScratchCopy_LifecycleFactoryAndRemovalEachPreserveOneUndoAndEarlierHistory` 在哨兵页调用 `Graphics.Line.#ctor+Create`，端点 `(4,4)` / `(12,4)`，约 25 秒返回 `TIMEOUT`、`not_started`，消息为 `Lifecycle observations exceeded their budget`。失败发生在首次工厂调用的规划观测中，尚未写入，不能视为 EPLAN 拒绝创建或撤销失败。— 原始证据：`.local/failures/q-op-lifecycle-ApiTryTests.log`；同日 `bridge-slotA.log` 的 op-lifecycle 会话自 05:49:59 开始，没有对应的未处理写异常；测试错误没有 correlation ID。
- 离线核对旧路径：对每个受支持查询名称逐一全项目枚举（共 120 个名称，跳过 BlackBox 别名），把同一候选集重复读取；生成查询表中 `GetStorableObjects(null)` 出现 116 次、`GetPlacements(null)` 出现 62 次（旧观测对部分传统类型另走专用 finder）。重复候选还会在字典覆盖同一项之前再次累计字节。A32 的普通示例副本已有 44,735 个放置，按全项目逐对象描述不能合理套用 10,000 项 / 256 KiB 的单次局部观测预算。旧错误没有计量字段，不能从日志判断首先触发的是时间、项数还是字节限制。
- 离线修正：带页定位器时只枚举所涉及页的 `AllPlacements`、页身份和归属这些页的连接；Project 工厂/服务锚点不扩大范围。无页对象保留全项目回退，但合并生成查询源，每个源只运行一次；按原生类型对应的规范种类与对象号去重，资源读回和字节计量仅执行一次。提交后同样按页重新查找创建对象；沿用 A55，不复用事务内原生句柄。20 秒、10,000 项、256 KiB 上限不提高；日志记录 scope/visited/objects/bytes/elapsedMs，超限错误附同一计量，计时不进入审批指纹。全项目业务快照和设计文件指纹仍保留，局部身份观测不证明全部副作用。
- Small 使用同一工厂参数形状、同页 `Placement.Remove` 和模拟的普通示例规模，验证不会访问无关页、创建/移除证据保留、重复候选不重复描述/计费及真实超限仍被拒绝。本次不运行 EPLAN；修复后的 Large 一步撤销仍待实机复验。

### A70. 数组试执行的预算失败（2026-11-01，EPLAN 2026.0.3.25421，Electric P8，LargeWrite scratch 副本，串行）
- C1 数组片（原记在 C1 条目下，变基时并入此处）（2026-11-01，仅离线核对）：本机 PE 参数元数据中 PolyLine.SetPointAt、MergedFunction.GetMainPlacement、DeviceService.CreateDevice、MountingPanelService.CreateArticlePlacement 的现有 @ 参数均无 Out 标志，不能据名称视为 out；复杂 ref 继续排除。目录与 DataModelu.xml 确认 PolyLine.GetPointAt 返回 PointD、DimensionGroup.Create 接受 PointD[]（创建排除不变）。未运行 EPLAN。
- `ApiTryTests.ScratchCopy_ArrayWriteOneUndoRestoresGeometryAndEarlierHistory` 对发现的一条 Line 调用 `HEServices.Edit.Rotate`，参数为 `plcmnts: [line]`、`oPntStart: {x:0,y:0}`、`angle:90`，原始运行返回 `OUTCOME_UNKNOWN`（7 个测试中仅此项失败），对应 add-in 记录见下条；当前同名 Large 日志已被后续规划超时运行覆盖。
- 同次 add-in 日志 `bridge-slotA.log` 的 `2026-11-01 05:23:48.861`、关联 ID `2d9673674cac4d7284749c59d93fdf85`：内层异常为产品抛出的 `System.TimeoutException: Trial exceeded its execution budget.`，栈为 `ApiTryExecuteOperation.Invoke → TransactionRunner.Run → ApiTryExecuteOperation.Execute`，没有 Rotate 的原生异常。代码在 API 返回且写后观察完成后检查预算，却把重新规划和写前快照也算进 30 秒；两次各自允许 20 秒的快照可挤占整个调用预算。这是产品计时范围缺陷，日志没有各阶段耗时，不能据此认定 Rotate 自身超过 30 秒或被 EPLAN 拒绝。
- 原始预算失败经回滚后仍报告 unknown，缺少独立的恢复读回，不能追认为已知 rolled_back，也不能宣称单次撤销通过。合并主干后成员调用独立计时，规划、属性原值读取及前后观察不占用成员 30 秒预算；Host 执行期限保持主干的 120 秒。本地成员超限仅在回滚成功后返回 TIMEOUT / rolled_back；调用内部超时、模态阻塞及提交/回滚失败仍为 unknown。
- 后续同用例失败：当前 `.local/failures/q-op-arrays-ApiTryTests.log` 明确返回 `TIMEOUT`、`EPLAN did not answer within 30 s`、`execution:not_started`，7 项中 6 通过、1 失败；负责人说明同期 add-in 未记录本次异常。离线调用链：ApiTryTools 的 ProjectGet 使用默认 InvokeAsync；随后 WritePipeline.RunAsync 通过默认重载调用 `api.try.plan`，BridgeClient.DefaultCallTimeout 为 30 秒，规划失败被标为 not_started。ApiTryPlanOperation.Prepare 在 CheckTarget 后调用 Inspect；旧 Inspect 先做全项目 finder 计数和 .edb 文件扫描，再为每页 ReadPage。单条 Line 的 Rotate 也触发全项目观察。日志没有阶段耗时，不能精确区分定位器校验与观察的耗时占比；120 秒执行请求尚未发送，不能归因为执行超时或原生 Rotate 拒绝。
- 本次以对象数组中的定位页为观察范围，Project target 仅作锚点；规划、执行前指纹复核及执行后使用相同规则，每页只读一次，函数按对象号去重，叠加既有 EplanDrawingReader 几何摘要。沿用 A12/A36/A60 的页面与连接身份枚举及 A52–A54 的绘图读取，仅描述目标页对象，跳过全项目计数和设计目录扫描；没有新的原生 API。空数组或无页对象回到既有观察；缺页仅记 unavailable 证据。保留 20 秒观察、30 秒规划/成员、120 秒执行期限，以及每次 scratch 复核和单撤销事务。
- Small 使用同组、`target:Project`、`plcmnts:[Line]`、`oPntStart:{x:0,y:0}`、`angle:90`，覆盖 321 页中只读目标页、重复页去重、几何变化、空数组/无页对象回退与缺页证据；另保留成员 30/31 秒边界。定位器使用脱敏合成身份，失败日志未记录原始会话身份，不能声称逐字节复现其摘要。本次未启动 EPLAN，Large 一次撤销及此前历史仍待复验。
- 合并再生成时的离线编译核对：EPLAN 2026.0.3.25421 的 `Placement3D.CopyTo(Placement3D,List<Placement3D>)` 虽在 XML 和 PE 表中列为 public static，`listObjects` 带 `modreq(System.Runtime.CompilerServices.IsByValue)`。以 .NET Framework 4.8.1 引用集和 Roslyn 读取原 DLL，`HasUnsupportedMetadata=true`，实际分支编译报 CS1501；单参数实例重载为 false 且可编译。生成器仅将此精确签名列为 `unsupported_required_modifier`，保留实例重载与其他集合形状，未使用反射绕过。证据 `.local/native-metadata.log`、生成后构建日志；这是编译期发现，不是实机失败。
- 2026-11-01 重跑（EPLAN 2026.0.3.25421，Electric P8，LargeWrite scratch 副本，A 槽串行）：ApiTryTests 9/9 通过。`Edit.Rotate` 旋转一条 Line 后页快照指纹不变（快照只含功能与连接），图形变化由 `drawing:<页>` 观察键体现，撤销后线条起点恢复。— Large 测试 TRX 计数。

### A76. 原生调用前置条件（2026-11-01，离线文档核对；实机待验）

- **来源与局限**：本机 EPLAN 2026.0.3 的官方 `Eplan.EplApi.DataModelu.xml` / `Eplan.EplApi.HEServicesu.xml`，以及负责人提供的 2026-10-31/11-01 日志摘要。单实例崩溃与原生异常的因果关系未证明；本次不运行 EPLAN。未取得完整参数 JSON、变体、授权与原始日志，Small 只复现报告的结构条件，精确输入仍待补。
- `StorableObject.IsValid` 表示有效且未删除的数据库对象；在读取名称、标识、页或属性之前检查。无效项跳过，汇总数量写 Warning，不据此判违规。`StorableObject.IsReadOnly` 表示对象只读，`Project.IsReadOnly` 表示项目以只读方式打开；`IWriteProtection.WriteProtected` 表示当前生效的写保护。单个 `GetWriteProtectionFlagSet` 返回 false 不代表没有其他保护，不能据此推断可写。— 上述官方 XML 对应成员。
- `TextBase.UndockText()` 会改变文本停靠关系。负责人报告关联 `db10bb36…`：在写保护数据库的文本上抛 `BaseException`“数据库写保护”。事务前检查目标 `IsReadOnly`、所属项目 `IsReadOnly`、目标/页生效的 `WriteProtected`；项目为空不单独推断只读。对象 IsReadOnly 是否完整涵盖库数据库保护仍待实机核对。当前检出 mover 未调用 UndockText，保留此行为；mover 的 Prepare 和 api_try 的 UndockText 入口共用只读检查。
- `Placement.IsPlaced` 表示已经放置；`Placement.Page` 返回所在页，transient 对象可为 null，文档也列出取页失败会抛 BaseException。因此先检查 IsValid/IsPlaced，再读 Page；不把定位器中的 page 字符串当作原生放置状态。— DataModel XML。
- **手工维护的 api_try 页上下文表**：下表每个成员的无 Page 重载在官方 XML 中均声明未设置页会抛 ApplicationException。生成分发每次 new NameService，不保留 Page；必须选显式 Page 重载；有对象参数的成员还检查对象已放置且 Page 有效。`CanDeviceTagBeNested` 的无页异常有 A63 及关联 `699c6dd4…/e6f667d2…/f7909351…` 的日志摘要，其余条目仅有文档依据，没有新增实机结论。

| NameService 成员组 | 需要页绑定的参数 | 证据 |
|---|---|---|
| `CanDeviceTagBeNested` | `pFunction` | XML、A63、上述日志摘要 |
| `FindNameGivingFunction` | `pActual` | XML 同名重载 |
| `FindNameGivingObject` | `f` | XML 同名重载 |
| `UpdateConnectionDefinitionPointsParent` | `pCDP` | XML 同名重载 |
| `AdjustFullName` | `pFunctionBase` | XML 同名重载 |
| `AdjustVisibleName` | `pFunctionBase` | XML 同名重载 |
| `EvaluateAndSetAllNames` | 无（显式 Page） | XML 同名重载 |
| `EvaluateAndSetAllVisibleNames` | 无（显式 Page） | XML 同名重载 |

- 同表还包含 `HEServices.Reports.Update(ReportBlockReference oReportBlockRef)`：官方 XML 明确参数无效、null 或未放置会抛异常；只检查该参数有效且页绑定，不要求显式 Page 重载，也不影响 `Update(Project/Page)`。此条仅文档核对，尚无实机结论。

- 负责人报告关联 `f94993e7…`：全项目功能枚举读取 `FunctionBase.Name` 抛 BaseException S063001“错误:无对象”，约 1 秒后崩溃。IsValid 能否提前识别该实际对象仍待实机验证，不能承诺消除原生异常或崩溃。
- 官方在线说明：[NameService](https://www.eplan.help/en-US/Infoportal/content/api/2026/Eplan.EplApi.HEServicesu~Eplan.EplApi.HEServices.NameService.html)。防御检查不是对库对象属性 getter 永不抛异常的保证；待验范围见 [台账](../../process/large-pending.md)。
- 2026-11-01 实机（EPLAN 2026.0.3.25421，Electric P8，LargeWrite scratch 副本，A 槽串行）：ApiTryTests 8/8、DevicePlaceTests 7/7、ConnectionCreateTests 14/14、PlacementMoveTests 1/1，期间无 EPLAN 崩溃。— Large 测试 TRX 计数与 Windows 应用程序日志。

### A77. 批量验证中使 EPLAN 崩溃或丢失项目的成员（2026-11-02，EPLAN 2026.0.3.25421，仅 scratch 副本）

以下事实由协调人把批量实机结果（`results.json` 的停止条目与文件时间）和 Windows Application 日志中 `Application Error` 的 `EPLAN.exe` 崩溃事件逐一对照得到。

| 组 | 侧 | 崩溃时间 | 槽 / 变体 | 异常码 | 批量记录耗时与结果 |
|---|---|---|---|---|---|
| `O:Eplan.EplApi.DataModel.Function.UpdateLocalTemplate` | write | 2026-10-31 03:22:30 | B / Pro Panel | `0xe0434352`（未处理 .NET 异常） | 约 18 秒，`outcome_unknown` |
| `O:Eplan.EplApi.DataModel.Function.UpdateLocalTemplate` | write | 2026-11-01 23:12:47 | B / Pro Panel | `0xe0434352`（未处理 .NET 异常） | 约 18 秒，`outcome_unknown` |
| `O:Eplan.EplApi.DataModel.MacroBox.RepresentationType` | write | 2026-10-31 02:59:38 | B / Pro Panel | `0xc0000409`（fail-fast） | 21 秒，`outcome_unknown` |
| `O:Eplan.EplApi.DataModel.Page.ConvertToGraphics` | write | 2026-11-01 23:45:10 | B / Pro Panel | `0xc0000409`（fail-fast） | 25.9 秒后记录 `failed`、`PROJECT_NOT_OPEN` |
| `O:Eplan.EplApi.DataModel.Project.ProjectName` | write | 2026-11-02 约 01:20（未崩溃） | A / Electric P8 | 无 | 13.4 秒后记录 `failed`、`PROJECT_NOT_OPEN` |
| `O:Eplan.EplApi.DataModel.E3D.BusBar.BarDistance` | read | 2026-11-03 04:11:52 | B / Pro Panel | `0xc0000409`（fail-fast） | 批量第 3 轮停在该条目，`eplan_lost`（读取上下文配方首轮） |

- `MacroBox.RepresentationType` 的 read 侧不受影响。`Page.ConvertToGraphics` 崩溃后，同轮接下来的 708 项在两个 EPLAN 实例环境下返回 `PROJECT_NOT_OPEN` 或 `SESSION_NOT_SELECTED`，实际均未试执行，不能算作失败观察。
- `Project.ProjectName` 的 write 没有使 EPLAN 崩溃，但把打开的 scratch 副本在磁盘上改名（`LargeWrite` 变为 `LargeWrite_v`，`.elk` 与 `.edb` 都改了），撤销没有恢复；同轮之后 968 项都返回 `PROJECT_NOT_OPEN`，未实际试执行。所有写保护都按项目名和路径识别项目，所以这一侧同样拒绝。
- 结论：以上 write 侧（`BusBar.BarDistance` 为 read 侧）由 `eplan_api_try` 拒绝，并由批量验证跳过；不从覆盖分母中移除。批量记录的返回耗时不能进一步证明精确的原生崩溃延迟；原始 target/value 参数未记录在结果中，保留这一证据缺口。

### A80. 全量属性读取期间所有者被终结（2026-11-02，EPLAN 2026.0.3.25421，Electric P8；IL 只读核对、离线宿主探针、A 槽 LargeWrite scratch 副本）
- 🔴 崩溃：2026-11-02 10:23:28，A 槽，`AcceptanceRedrawTests`（默认虚构页）端子阶段放置端子后，`eplan_device_get`（detailed）读 `=GAA+A1-X6:7`，EPLAN 以 `0xe0434352` 退出；.NET Runtime 1026 为 `AccessViolationException`，堆栈 `PropertyValue.UnsafeRemoteValue ← getEPropertyValue ← ToMultiLangString ← EplanText.Display ← EplanProperties.Read ← EplanDeviceReader.ReadDevice`。此前 8 次同一步骤通过。— Windows 应用程序日志。
- "remote" 不是一类属性。`PropertyValue.getEPropertyValue` 依次取本地值（`m_pPropValue`）、列表值（`m_pPropsList`，`UnsafeRemoteListValue`）、对象句柄值（`m_pPUH`，`UnsafeRemoteValue`）。`ExistingValues` 的值走句柄路径：每个值自带一份 `PropertyUserHandle`，不引用属性列表，也不引用对象包装器。`IsEmpty` 在这条路径上做同一个原生调用（解引用句柄，偏移 120 处的接口，虚表第 96 字节的函数）；公开成员中没有 IsRemote 之类的标志，XML 只有内部的 `UpdateRemoteValue`。所以"跳过 remote 值"等于跳过全部属性。崩溃前 `IsEmpty` 刚用同一调用成功读过这个值，状态是在两次调用之间被别的线程改变的。— 对 `Eplan.EplApi.DataModelu.dll` 的 IL 只读核对（System.Reflection.Metadata，不加载程序集），XML。
- `StorableObject.Properties` 及各类型化的 `Properties`（`Function`、`Page`、`Project` 都派生自 `StorableObject`）把属性列表缓存在包装器字段 `properties` 中，首次访问时创建，同一包装器再访问返回同一个列表；A36 中"释放后再取 Properties 访问冲突"即因此。`!UniversalPropertyList` 释放列表的句柄和原生列表，`!StorableObject` 释放对象句柄，`!PropertyValue` 释放自己的句柄；没有显式释放时，它们由 CLR 终结器线程直接执行。— IL。
- 原读取在 `ExistingValues` 取出之后不再引用对象包装器和它缓存的列表（Release 构建中 JIT 据此判定其已死）。读取循环中一旦发生 GC，二者的终结器就在终结器线程上释放同一对象的句柄，与主线程的读取并发。
- 离线宿主探针（Electric P8 变体 Bin，示例项目副本，只在测试进程环境中设 `COMPlus_GCgen0size=0x10000`；`FullPropertyReadTests` 每轮读页 `=GAA+A1&EFS1/1` 及其上全部设备的全量属性，比较 20 轮）：修复前 2/2 次失败，电缆 `+A1-WD1` 的 8 个计算属性 35100–35106、35108（"电缆: 使用的连接数"等）分别在第 1、第 12 轮整组缺失，其余 141 个相同，宿主未崩溃；修复后 3/3 次 20 轮完全相同。— 测试输出。
- 🔴 规则：通过 `PropertyValue` 读取期间，必须保持其所属对象包装器可达（它也持有缓存的列表）：全量读取接收所有者对象，在全部值释放后 `GC.KeepAlive(所有者)`。同一请求中在同一对象上先读的单个属性值（`FUNC_TEXT`、`PAGE_NOMINATIOMN`）当场 `using` 释放，不留给终结器在之后的全量读取中执行。
- 修复后 A 槽复验（2026-11-02 15:44–16:01，LargeWrite scratch 副本与只读夹具；EPLAN 由本次运行启动，进程环境中 `COMPlus_GCgen0size=0x10000`，运行结束时 gen0 GC 计数约 150 万次）：`FullPropertyReadTests` 1/1、`DevicePlaceTests` 8/8、`AcceptanceRedrawTests` 1/1（含 `-X6` 1..8 端子的放置及其后的 detailed 读取），应用程序日志中无 EPLAN 崩溃事件。— TRX 计数、.NET CLR Memory 性能计数器、Windows 应用程序日志。
- 未处理：上一请求留下的、同一对象的其他包装器在本次读取中被终结（例如写入后紧接的读取）。上面的复验在 GC 压力下没有出现这种问题，但不能据此排除。

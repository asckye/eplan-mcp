# [EPLAN API 笔记](../api-notes.md)：PLC、电缆与端子编号

### A161. 端子排定义是类别 TerminalDefText 的主功能，说明文字是它的功能文本；只放端子时自动产生未放置的定义（2026-11-18，EPLAN 2026.0.3.25421，Electric P8，A 槽，LargeWrite scratch 新页）
- **现象：** `eplan_device_place` 带 `terminal_strip_text` 和 `terminal_strip_position` 放端子，得到一个已放置的端子排定义：`eplan_device_get` 读作类别 `TerminalDefText`、有页、`text` 是给的文字。只给 `terminal_strip` 和 `terminal_number`、不要求定义时，同样得到一个 `TerminalDefText` 的主定义，但**没有放置**：`page` 为空，`text` 为空，变更清单写的是 `main definition; function text (??_??): ; unplaced`。
- **结论：** 端子排的说明文字就是这个定义的功能文本（属性 20011），已放置和未放置的定义都能读写。运行时类是 `TerminalStrip`，是 `Function` 的派生类，属性列表相同。图上没有画定义的端子排，在导航器里仍有一个空的未放置定义。
- **工具行为：** `eplan_property_set` 按设备标识写端子排定义的 20011（语言 `??_??` 或项目语言），其余属性仍然拒绝（`POLICY_DENIED`）。此前按标识写会被拒，消息是 `The target runtime object type is outside the published scope.`，原因是写入会话只把 `PLC`、`Terminal`、`BusTerminal` 当作功能。
- **证据：** `DevicePlaceTests.StripDefinitionText_PlacedAndUnplaced_IsChangedByPropertySet_UndoRestores`：已放置的定义由 `Old text` 改为 `New text`，未放置的定义由空改为 `Spare strip`，20027 被拒，四步撤销后项目快照与之前相同。

### A24. 电缆、端子、PLC 的读取与编号（spike S21，2026-10-21，EPLAN 2026.0.3.25421，Electric P8，在调度器中）
详细数据见 [spike S21](../../../spikes/S21-cables-terminals/README.md)。示例项目的 scratch 副本。
- `DMObjectsFinder` 能读出电缆（按类别 78 根）、端子排（45 个对象，实际 27 排：18 排各返回两次，2026-10-21 实机测试）、端子（1331）、PLC 盒（20）、插头（170）、连接（2060）；`Cable.GetSourcesAndTargets` 给出电缆两端的设备或端子，`TerminalStrip.Terminals`、`PLC.PLCTerminals` 给出所属的端子。— S21。
- 🔴 `GetCablesWithFilterScheme("")` 用电缆导航器当前的过滤方案（示例中只返回 4 根）；`null` 返回电缆加屏蔽（89），XML 说会抛 `ArgumentNullException`，实际没有。按类别取电缆不受界面影响。— S21。
- `Renumber.Terminals`（全部端子，41–43 秒）、`CableService.RenumberCables`（0.3 秒）、`ConnectionService.DesignateConnections`（覆盖全部，约 1 秒，605 条连接得到线号）都能被 `SafetyPoint` 回滚，回滚后名称和线号与之前完全一致；在 Bridge 的包装中提交后，都能用一次 Ctrl+Z 撤销。— S21。
- 这三种编号的方案传空串都能运行，用的是界面上次的方案，与 `Renumber.DeviceTags` 相同（A18）。— S21。
- 方案的设置节点：端子编号 `USER.Terminal.Numbering.Scheme`、标签 `USER.Labelling.Config`（用户设置）；电缆编号 `CableLog.CableNumberingScheme`、生成电缆 `CableLog.CableCreateScheme`、线号 `WireNumberingLog.Schema`、PLC 编址 `OfflineNumerationPLC.PLCNumbering`（项目设置）。`XSettingsExport /prj:<项目名>` 在开发机上返回 FALSE，项目设置的节点是从 `Settings.xdb` 中只读查得的。— S21。
- `Generate.Cables` 在示例项目中没有改动名称和线号，但改写了项目的 `User.eod` 和 `Settings.xdb`（上次使用的方案），`SafetyPoint` 回滚不撤回。— S21。
- PLC 盒 `=K1+A2-KE1`：`PLC.PLCTerminals` 有 34 个对象、17 个名称，同名的不是重复（`:1.1` 既是输出 `%QX2.0` 又是输入 `%IX2.0`；其他名称一个有地址、一个没有）。按这些对象用 `ConnectionsFilter.Function` 查不到连接，按设备（主功能加辅助功能）查到 22 条。`FUNC_PLCSYMBOLICADDRESS_CALCULATED` 与 `FUNC_PLCADDRESS` 相同（`%QX2.0`），`FUNC_PLCRACK`、`FUNC_PLCSOCKET` 为空。— 实测（`eplan_plc_get` 的 Large 测试，2026-10-21）。
- 编号工具的 Large 测试（2026-10-21，新复制的写副本）：`eplan_cable_renumber` 用 `Standard` 给全部电缆编号，复现了 S21 的 `+A2-WD4`↔`+A2-WD5`、`+B2.Y1-WD1`↔`+B2.Y1-WD2`（`eplan_device_list` 按类别 `Cable` 列出 56 根）；插宏后对整个项目只编带 `?` 的端子，`Renumber.Terminals` 约 43 秒，EPLAN 没有崩溃（与 A20 中的 `Renumber.DeviceTags` 不同）；`+A1-XD1` 用 `Numerisch` 编号、线号的 `all` 和 `except_manual` 都通过。`+A1-WE1` 的主功能类别是 `BusBarDefText`，不是电缆。— 实测。
- 🔴 **`Renumber.Terminals(…, 只编带 ? 的 = true, …)` 挡不住已编号的端子**：第二轮 Large 测试（2026-10-21，新复制的写副本）中，插宏后把整个项目的全部端子交给它，只编带 `?` 的。7 个 `:?n` 编成了 `:n`（`+A1-XG2`–`XG4`），但它同时按方案重排了已编号的 `+A1-XD1`（`:2→:3`、`:3→:5`、`:4→:2`、`:5→:4`，同 S21），共 11 处改名，约 35 秒。
  - 对本项目的影响：`eplan_terminal_renumber` 只把带 `?` 的端子交给它，做法同 A20；读回时，规划之外的端子改了名就回滚。
  - 只交带 `?` 的端子时，同一端子排中已编号的端子会不会被重排（`AllSelectedTerminalStrips` 按 XML 编整排），还没有验证。— 实测。
- 授权：`Preplanning` 不可用；`RevisionManagement`、`RevisionMarkers`、`MultiLanguageTranslation`、`WireNumbering`、`EditingOfCables`、`EditingOfTerminals`、`EditingOfPLC`、`DXF_DWGImport`、`ImportAndExportDeviceLists`、`IntelligentPDF`、`InterfaceToLabelingDevices` 可用。— S21。

### B10. 端子、电缆、线号的编号与项目方案（`HEServices`、`DataModel.ProjectSchemeSetting`，2026-10-21）
M4c 编号工具依赖的文档说明；实测结果见 A24（S21 只用了空方案、`All` 和"不只编带 ? 的"），其余由 Large 测试确认。
- `ProjectSchemeSetting.Init(节点, Project)`：用项目设置的节点初始化（XML 的例子为 `CableLog.CableAutoSelectionScheme`），节点不是该项目的方案时抛 `BaseException`。它继承 `SchemeSetting`，`GetCount`、`SetScheme(序号)`、`GetName` 可用，实现 `IDisposable`。— XML、编译。
- `Renumber.Terminals(Function[], TerminalsSequence, Extent, …, 方案, 起始值, 步长, 前缀, 后缀, 允许改变排序, 填补空号, 只编带 ? 的, 连带 PLC 控制的)`：`Extent.AllSelectedTerminalStrips` 编号给出的端子所在端子排的全部端子，`Extent.Selected` 只编给出的端子；前缀、后缀只在方案为"按在线编号格式"时有用；方案名不存在时抛 `InvalidScheme`。"只编带 ? 的"按 XML 只编名称中带 `?` 的端子，实际上挡不住已编号的端子（A24，2026-10-21）。— XML，枚举名经编译确认。
- `CableService.RenumberCables(Function[], 方案, 保留旧名, 保留标识符, 起始值, 步长)`：参数可以是电缆和屏蔽；"保留旧名"为 true 时已有的标识不变，只给标识中带 `?` 的电缆编号；方案名不存在时 XML 一处写抛 `InvalidScheme`、一处写抛 `ArgumentException`。— XML。
- `ConnectionService.DesignateConnections(Project, 方案, DesignateOverwrition, 标为手工, 先生成连接)`：`None` 从不覆盖、`ExceptManuals` 覆盖标为"手工设定"之外的、`All` 全部覆盖；与界面不同，只有"先生成连接"为 true 时才先更新连接；方案名不存在时抛 `InvalidScheme`（备注中写 `ArgumentException`）。— XML。
- `Renumber`、`CableService`、`ConnectionService` 都实现 `IDisposable`。— XML、编译。

### A78. PLC 通道标识、插头与引用（2026-10-31，仅文档与元数据核对，未运行 EPLAN）
- 本机 `Eplan.EplApi.DataModelu.xml`：20406 为可写手工插头代号，20431 为只读自动插头代号；20213 是不带结构、带前缀的设备标识（不含连接点补充名称）；20300 为主辅功能交叉引用，20301 为概览交叉引用，均只读。20021=2 表示始终显示交叉引用。`SymbolReference.SwitchLocalPropertyPlacements(true)` 复制本地属性排列；`PropertyPlacement.Create` 文档也说明会启用本地排列。这些是文档事实，不能证明 A55 的显示丢失已修复。
- [官方 PLC 数据说明](https://www.eplan.help/en-US/Infoportal/Content/Plattform/2022/Content/htm/plcgui_k_arbeitsweise.htm)指出连接点以卡设备标识归属 PLC 盒；概览配对由卡标识、功能定义、插头和连接点代号共同识别，必要时再以地址区分。20300 指向主盒的实际表现、20406 对复合名称的影响、20301 的配对与项目显示设置，均待实机确认，不能用页名或手工引用填造。
- D3 实现增加可选插头输入与本地显示；最终修复仅保留托管身份和值，提交后按对象号重取放置点与父 PLC 盒（A79）。提交后核对失败保留现场，报 `committed` 加未验证项警告，不以 Abort 冒充撤销；此处按既有执行状态规则修复，实机结果仍待复验。
- Large `PlcDisplaySpikeTests` 的探针在 LargeWrite 新页放置无插头与有插头的独立点，分页打印每点 `eplan_object_get(PLCTerminal)` 的全部属性和页属性文字，并导出两页图片；另以 `eplan_api_try(FunctionBase.Name)` 在同一点尝试组合卡标识与插头后缀，打印修改后的全部属性；该试执行不能代替已知 20406 setter，也不预设改名必然设置插头。随后用第二点对照显式 20406 写入。断言测试及虚构验收分别要求 20213、20406、20300 的文字与实际卡、插头和引用一致，并核对对页名称与列号。本次均仅编写，未运行，不登记为 live_verified。

2026-11-01 失败证据复核（EPLAN 2026.0.3.25421，Electric P8，LargeWrite，串行；本次修复不运行 EPLAN）：
- **点名后缀与插头是不同属性。** 同一点从 `=GAA+A1-D3KF5:1` 经 `eplan_api_try(FunctionBase.Name)` 改为 `=GAA+A1-D3KF5:-X10:1`，20022[1] 从 `1` 变为 `-X10:1`，20406 仍为空，20431 仍为空。因此单独给 20406 时不能把插头也拼入 Name：后续写 20022[1]=`1` 会把复合后缀覆盖，旧实现再按含插头的预期名核对而回滚。点名维持 `<卡>:<plc_connection_point>`，插头仅写 20406；含冒号的显式连接点仍原样保留。— `.local/failures/q-d3-PlcDisplaySpikeTests.log` 及其引用的 `trx-PlcDisplaySpikeTests` 中 `2026-11-01_12_43_47.105689.trx`，探针的 baseline / same point after composite-name trial 属性与放置输出。
- **提交后的名称筛选读回未找到已实际放置的 PLC 点。** correlation `62f3f00a8ea44aa180b4812941258e41`（同日早轮 `2866885da9d34e089d7fcc5970010169` 同栈）在 `EplanDeviceSymbolPlacement.ReadCommitted` 的 `Single` 抛出 `InvalidOperationException`；随后只读输出中页面 `AllPlacements` 仍有 `(120,176)`、名称 `=GAA+A1-D3KF5:1` 的 Terminal，地址/手工符号地址/功能文本均已保存。最新同栈为 `a0966e8170074d2d88dd18f6ee0e4b81`（08:17:30.827）、`c982da8240e940c1ab86dca9759e2f42`（08:20:57.267）、`329e6a11d6b4411cae5c7fdb567b2846`（08:30:30.640），对应两项 PLC spike 与验收 PLC 步骤。由查询路径和后续页面读回推断，缺陷在提交后的筛选重定位；日志栈没有行号，不能单凭它区分页放置筛选与 `card.Channels` 筛选两个 Single，也不能断言具体是哪一种名称变化导致。点全名含 `:1`，卡名不含连接点；A24 同名通道还可能有多个表达，故名称不能代替放置对象身份。最终修复不保留事务内原生引用，仅按对象号重定位并核对卡成员（A79），完整保留卡归属、原生插头/引用及显示校验；已确认提交后的验证失败返回 `committed` 加警告。— `.local/failures/bridge-slotA.log`、`q-d3-PlcDisplaySpikeTests.log`、`q-d3-AcceptanceRedrawTests.log`；本次仅离线复核与 Small 假对象测试，未运行 EPLAN。
- **卡在另一页不保证 20300/20301 有引用。** baseline 与改复合名后的原生 20300、20301 都为空字符串；不能从卡页名与列号填造引用。显示仍逐项精确匹配原生值，空值也须有请求的属性放置、位置和可见格式。该输出没有证明设置 20021=2 后会产生非空引用，也没有证明带插头与四项显示的完整调用成功（两次均在提交前回滚）；这条完整 Large 复验仍待进行。— 同一 TRX，20300/20301 属性及页面文字输出。
- **这次 `api_try(FunctionBase.Name)` 返回 committed，没有 OUTCOME_UNKNOWN。** 本分支 probe 的 unknown 来自前述 device.place 提交后查询；其他分支日志中的 api.try 超时不能作为本次插头行为的证据。
- 2026-11-02，Electric P8，LargeWrite scratch，d3 `PlcDisplaySpikeTests`（协调人在 A 槽运行）：`=GAA+A1-D3KF3`（SPECIAL/PLCC，变体 0）的异页点 `=GAA+A1-D3KF3:1`（IEC_symbol/PLC_CBOX，120,176）读回 `main:false`、`owner:null`，卡的 `PLCTerminals` 为空；A11/A55 的卡名加冒号前缀查询仍包含该点的跨页及无页表达，不能只靠 `PLCTerminals` 列通道。同一次运行中，一段只供测试的原生读取（对两页逐个读 PLC 盒的 `PLCTerminals`、两种名称查询，以及每个 PLCTerminal 的 `Name`、`VisibleName`、`Page`、`Location`、`IsMainFunction`、`PLC`、`ParentFunction` 和地址、插头属性）进行中 EPLAN 以 0xc0000374（堆损坏，出错模块 ntdll）崩溃，系统日志在崩溃前没有消息；未区分是哪一个成员，也不能排除之前的放置已损坏堆。产品读取器只使用此前已验证的成员。

### A79. PLC 提交后原生对象生命周期与崩溃修复（2026-11-01，EPLAN 2026.0.3.25421，LargeWrite scratch 副本）
- 🔴 2026-11-01，EPLAN 2026.0.3.25421，Electric P8，LargeWrite scratch 副本，slot A：`DevicePlaceTests.TwoPlcInputsAndNoNcContacts_ReadNativeValuesDisplaysAndRepresentations_ThenEachUndoRestores` 运行约 3 分钟，两次在输出 `plc channels: {...}`、`eplremote XEsUndoAction: ok` 后崩溃：10:49 为堆损坏 `0xc0000374`，16:17:42 为访问冲突 `0xc0000005`；main 与 feat/layout-guard 同类测试均 7/7 通过。根因是 297c4a6a 在提交后复用事务内创建的 Terminal（`_placedFunction`）和读取的 PLC card（`_plcCard`），随后撤销损坏原生内存。现已强制只跨 Commit/Dispose 保留托管身份和值：移除设备符号放置器及其 partial 的原生对象字段，显示在事务内复制成托管快照；提交后从目标页 `AllPlacements` 按 ObjectIdentifier 重取点，用 `DMObjectsFinder` 的 `PLCBox` 类别筛选按 ID 重取卡，重新取得并释放属性列表，不依赖请求名称，读回失败仍返回已提交警告。Small 假事务在 Commit/Dispose 后禁止触碰事务内原生句柄，并覆盖身份、归属、地址、符号地址、文本、插头和显示校验；**修复尚未实机验证**，本次未运行 EPLAN。
- 2026-11-01 修复后复验（同一用例）：Pro Panel（B 槽）与 Electric P8（A 槽）各 `DevicePlaceTests` 7/7，Windows 应用程序日志无 EPLAN 崩溃事件。修复前同一变体、同一用例两次均崩溃，修复后不再崩溃，原因确认是事务内的原生对象在提交后被复用。— Large 测试 TRX 计数与应用程序日志。

### A90. PLC 同一通道的两种表达如何关联（2026-11-04，EPLAN 2026.0.3，Pro Panel，scratch 项目，`MacroBoxSpikeTests`）
- 总览宏建立卡和总览点后，在原理图页插入同一部件的多线宏（`NumerationMode.None`，点名为 `=GAA+A1-A?:1.1`），再用 `NameService.RenameDevice` 改成卡的标识，两边立刻配对：总览点 20301 指向原理图页和路径，原理图点 20300、20301 指向自己的路径。宏已带插头代号 20406，不需要生成连接。— 实测。
- 在原理图点写 20011，总览点的 20031、20120 立刻变化，总览点自己的 20011 仍为空。功能文字只写原理图这一边。— 实测。
- 在总览点写 20400，原理图点不变；调用 `Synchronize.OverviewToMultilineAndSingleline(project)` 后才同步。示例项目里地址只在总览点上（52 个总览点有地址，原理图点 0 个），重绘时不同步。— 实测 + 示例数据。
- 只设相同 `Name`、不带插头代号的新功能不配对。同一张卡可有两个同名点，靠插头代号区分（`:RJ45` 的 20406 分别为 X1、X2）。— 实测 + 示例数据。
- 设计思路和数据关联见 [design-model.md](../design-model.md)、[data-relationships.md](../data-relationships.md)。

块成员的标识：`Block.SubPlacements` 里所有成员的 `ToStringIdentifier` 相同（示例项目块 `144_282958` 的 27 个成员 oid 都是 550；scratch 新建块的 3 条线都读到 type 5、oid 52125），按标识去重只剩一行。块成员不能按标识定位，只能按它在块里的位置区分。— 示例数据 + `MacroGroupedPlacementTests` 实测。

### A108. 电缆连接分配到芯线（2026-11-06，EPLAN 2026.0.3.25421，Pro Panel LargeWrite；Electric P8 上的效果待负责人批准后补）
- **Electric P8 上的效果：** 15 根电缆 43 条连接全部分配，电缆图表的"空闲芯线"从 4 变成 1，和原图一致；一次审批。
- `CableService.DoReassignWires(项目或电缆功能, 是否覆盖, 不改的属性)` 对应"全部重新分配"。示例项目的电缆已经分配过，再调用读回没有变化，也不产生撤销步骤，这时撤销会弹"'撤销'冲突"（A41），所以没有变化时工具不返回 `undo`。
- **没有分配时的现象（71 页图纸）：** 电缆图表把芯线和连接分成两段，"空闲芯线"等于全部芯线数。

### A113. PLC 原理图生成（2026-11-07，EPLAN 2026.0.3.25421，Pro Panel，LargeWrite）
- **`PlcService.ExportGenerateSchematicConfiguration(project, 文件)`** 导出项目自己的配置：每个 PLC 设备一个 `DEVICE`，下面每个功能一个 `Function`，主功能带 `Macro`（来自部件，如 `$(MD_MACROS)\PXC\2861344.ema`），已放置的表达带 `MultiLinePage`、`OverviewPage`、`SingleLinePage`。
- **`PlcService.GenerateSchematic(project, 文件, 单线, 多线, 总览, 机架)`：** 在总览里插入宏之后，给导出的配置写上不存在的多线页名再生成：EPLAN 自己建页（类型 Circuit，页描述来自宏），放 39 个对象、8 个功能，功能并入总览的设备（`=GAA+A1-A1:1.1` 等），不用再改名合并（对比 A90）。返回生成的页。
- 🔴 配置里有项目中不存在的设备时，EPLAN 弹模态对话框"一个或多个设备在项目中不存在"，调用挂起，`QuietModeStep` 也没试过能否挡住。工具预先检查设备，不让它走到这一步。
- 放在 `UndoStep` 里时一次撤销能删掉生成的页；只放在裸 `Transaction` 里没有撤销步骤，撤销报"'撤销'冲突"。
- 官方帮助：只使用尚未放置在对应表达里的功能；已存在的表达保留。通道级接线宏（`Function` 的 `Macro`）和占位符变量（`Variable`）还没有实机验证。— 探针、`ProjectToolsTests`。

### A122. EES 项目的中断点、PLC 先总览后调用和 ET 200SP 图片变体（2026-11-08，EPLAN 2026.0.3.25421，Electric P8，从 `EES_Machine_Schematic_2026.zw9` 建的 scratch 项目 MX_Dryer2–6、PlcProbe）
- **中断点不带结构：** EES 基本项目里中断点名全项目通用。请求 `=GAC-L1` 存成 `-L1`，于是和主母线的每个 `-L1` 配对：220 V 控制回路的 L1 接到了进线 L1 上，绕过了控制电源断路器。只有在全项目不带结构时也唯一的名字才可靠，比如分配端子 `-XD3:2`。
- **中断点一对一配对：** 同名的点按页序两两配对。三个点同名时，第三个没有对端，交叉引用为空。每个用电处用自己的端子号，或者从上一个用电处再引出去。
- **BP 变体的样子：** 变体 4–7 箭头尖接线（流入：4 线在右、5 线在上、6 线在左、7 线在下）；变体 0–3 箭尾接线（流出：0 线在左、1 线在下、2 线在右、3 线在上）。`eplan_connection_create` 不给 `symbol_variant` 时按方向取 Right→4、Left→0、Up→3、Down→7，流入流出不一致。属性排列 `下方,90°`、`上方,90°` 让竖线上的标签竖排。官方 EES_s7_1500 的机架页上 24DC、0DC 的箭头都指向模块。
- **先总览、后调用：** 用宏路径插入卡的 Overview 表示（`representation:"Overview"`），设备就建好了，部件挂在总览的主功能上。之后在机架页插入多线图片（DI/DQ 变体 6 等），图片里的 PLC 盒不是主功能，改名成卡的标识即并入该卡，`eplan_device_update` 的合并规则放行。反过来（先有图片，再插总览改名）被拒，因为总览里带一个屏蔽端子 `SH`（`DeviceEndTerminal`）。之后在功能页放这张卡的 PLC 点，会复用同名的点：同一个点同时出现在总览页和功能页上。
- **CPU 总览宏：** 6ES7510-1SK03-0AB0 的 Overview 变体 0 有两个 PLC 盒，`K1` 和子设备 `K1-K1`（官方机架页上是总线适配器 -KEC1）。按部件插入时，两个都挂上了 CPU 部件，所以改为按宏路径插入、部件逐个挂。
- **ET 200SP 图片变体：** DI 6ES7131-6BH01、DQ 6ES7132-6BH01 的多线变体 5 是浅色 BaseUnit 图片，6 深色，7 浅色带 AUX 端子，8 深色带 AUX 端子；F-DI 6ES7136-6BA01 用变体 6 是浅色；CM PtP 6ES7137-6AA01 用变体 3（"全极/变体 D"）。图上的浅色、深色和 BaseUnit 订货号（6ES7193-6BP00-0BA0 浅、-0DA0 深）对应。
- **`PlcService.GenerateSchematic` 的两个现象：** 给只有总览的卡生成多线页，EPLAN 为一张 DI 卡建了 5 页多线展开，不是机架图片。给图片已在机架页的 8 张卡只生成总览时，工具把配置里没请求的 `MultiLinePage` 属性清掉了，EPLAN 随之撤掉机架页上的图片，另建了 6 张多线页。
- **自动连线不看电位：** 上下对齐的两个空端子引脚（24 V 的 XD1:1 和 0 V 的 XD2:1）被自动连起来，等于短路。分配端子排的两排要错开放。
— 探针脚本 `mxd2.py`、`dip-interruptions.json`（会话 scratchpad），导出图对照。

### A123. ET 200SP 总览宏的占位符值集和 QUATTRO 分配端子（2026-11-08，EPLAN 2026.0.3.25421，Electric P8，从 `EES_Machine_Schematic_2026.zw9` 建的 scratch 项目 MX_Dryer9、MX_Dryer10）
- **占位符只在 Overview 表达里：** `eplan_macro_get` 不给 `representation` 时读默认的多线表达，`placeholders` 为空；给 `representation:"Overview"`，变体 0 和 1 才有一个没有名字的占位符对象，变体 4 没有。I/O 卡和 CM PtP 的变量是 `Baseunit`，变体 0 的值集为 `aus / from AML Import (TIA)`、`6ES7193-6BP00-0BA0 (BU15-P16+A0+2B)`、`6ES7193-6BP00-0DA0 (BU15-P16+A0+2D)`（变体 1 是 BP20 系列）。CPU 1510SP-F 的变量是 `Artikelnummer`，有 13 个总线适配器值集。
- **值集的效果：** `values: {"": "6ES7193-6BP00-0BA0 (BU15-P16+A0+2B)"}` 把这个 BaseUnit 部件挂到卡（PLC 盒）上；CPU 选 `BA 2XRJ45 (6ES7193-6AR00-0AA0)`，部件挂到子设备 `-K1-K1` 上。按宏路径插入时卡本身的部件不会挂上，要单独挂。
- **QUATTRO 端子符号 `X4_B_1`（IEC_symbol）：** 变体 0 有一个朝上的进线点 (0,2)、三个朝下的出线点 (0,−2)(4,−2)(8,−2)，左右 (−0.75,0)/(8.75,0) 是鞍形跳线点；变体 6 上下相反。`X4_B_2` 有四个出线点。相邻端子放在同一行时，跳线点左右相对，重新生成连接后端子自动连通（MX_Dryer10：XD1:1→2→3）。
- **编址后再放的 PLC 点不带地址：** 总览编址后，`eplan_device_place plc_connection_point` 在功能页上放的点是这个通道的另一个功能，地址（20400）是空的，点框里的地址行也是空的；`eplan_plc_get` 只列出总览上的点（`main:false`，地址 `I1.0`）。放点时传 `plc_address`（取总览的地址），点框里就显示地址。
- **符号的变体 2 和 6：** 6 个引脚的三极符号（如 `QL3`），变体 2 是旋转 180°（1 号引脚到了右下，L1 在最右边）；变体 6 是上下翻转（1/3/5 仍从左到右排在下面，2/4/6 在上面），官方进线页倒置的主开关就是这种。
- **部件模板是黑盒时不要再给库符号：** `eplan_device_place` 同时给 `part` 和 `library`/`symbol` 时，先按部件模板建出黑盒（`SPECIAL/DC` 加 6 个 `DCP5`），再只把主功能换成库符号，结果库符号的 6 个引脚没有代号，黑盒的连接点还在。要用库符号时先按符号放（给 `connection_designations`），再用 `eplan_device_update part` 挂部件。
- **带方向的 T 节点：** `SPECIAL/TLRU`、`TLRO`、`TOUL`、`TOUR` 的变体 0–3 是带方向的画法（支路处一段斜线），变体 8 是圆点。EPLAN 帮助：不带编号的点是来源，目标 1 画直线、目标 2 画斜线。符号的连接点按 [目标 1, 来源, 目标 2] 排列，目标 2 偏 2 mm：TLRU 0 = 来源左、目标 1 下、目标 2 右；1 = 来源右；TOUR/TOUL 2 = 来源上、目标 1 是支路，3 = 来源下。官方 EES_stacking_iec 的母线支路全是 TLRU/0、TLRO/0。`eplan_connection_create` 不给 `variant` 时选变体 8；给 0 后整页实测正常（ProbeMM =GAB/2）。
- **界面上先画的页不刷新交叉引用：** 逐页画完整个项目后，先画的页上流出的中断点在 EPLAN 界面里仍显示空的交叉引用（`L1 /`），因为画它时对端还不存在；`eplan_export_image` 导出的同一页是对的（`L1 / =GAC/4.0`）。关闭再打开项目后界面显示正确（MX_Dryer19 =GAB/2，窗口截图核对）。
- **引脚代号为空时 `Pin.Name` 抛异常：** 读到代号（20022）为空的引脚会抛 `EmptyPropertyException`（S063111），全项目 `eplan_connection_generate` 因此失败；连接读取改为按空代号处理。

### A124. 分配端子 `X4_B_2`、HMI 正视图宏和 PLC 盒嵌套已有设备（2026-11-08，EPLAN 2026.0.3.25421，Electric P8，从 `EES_Machine_Schematic_2026.zw9` 建的 scratch 项目 MX_Dryer20）
- **`X4_B_2`（IEC_symbol）：** 四个接线点都在同一侧，间距 4 mm：变体 0 朝下 (0,−2)(4,−2)(8,−2)(12,−2)，变体 6 朝上 (0,2)…(12,2)，1 号点都在左边；鞍形跳线点 (−0.75,0)/(12.75,0)。变体 2、4 是 1 号点在右边的镜像。官方 EES_stacking_iec =GAB/5：每排端子 1 用 `X4_B_1`（进线点接电源），其余用 `X4_B_2`，只给用到的接线点画线。
- **MTP1000 宏 `Siemens/Automation_Systems/COMFORT_PANEL/6AV2128-3KB06-0AX1.ema`：** 多线变体 0、1 只有电源或 FE 端子，变体 2 是 80×40 的简单盒子，变体 3 是约 1:1 的正视图（288×220，PLC 盒左上角在基准点，X80 的 1、2 和 FE 在底边，比基准点低 208 mm）；总览有变体 0、4。宏里没有 PROFINET 口。宏里 FE 端子的标识是 `-K1-K1:FE`：插入后先把盒子 `-K1` 改名，FE 跟着变成 `-新标识-K1:FE`，再把 `新标识-K1` 改成新标识，三个引脚才都属于同一个设备。
- **PLC 盒会嵌套框内已有的设备：** 在已有 `-12PF1`（HMI 简单盒子）的页上插入变体 3，它的大 PLC 盒 `-K1` 框住 `-12PF1`，`-12PF1` 变成 `-K1-12PF1`。和现场设备叠成 `+M.M` 是同一类现象：盒子（位置框、PLC 盒）把自己的标识加到框内的设备上。
- **`eplan_project_list` 的 `path` 不带 `.elk`：** `eplan_project_open` 原来只接受 `.elk` 文件路径，直接用列表里的路径会被拒绝（INVALID_ARGUMENT）；现在旁边有同名 `.elk` 时也接受不带扩展名的路径。

### A126. PROFINET 单线页、带连字符的中断点名称和挂错结构的 PLC 点（2026-11-08，EPLAN 2026.0.3.25421，Electric P8，从 `EES_Machine_Schematic_2026.zw9` 建的 scratch 项目 MX_Dryer25、MX_Dryer27）
- **单线宏的网口是 `BusTerminal`：** CPU `6ES7510-1SK03-0AB0`（SingleLine 变体 0，X1 P3）、MTP1000（变体 0，X1 P1、X1 P2、X2 P1）、G120C `6SL3210-1KE1x-xUF2_2D_`（变体 1，X150 P1、P2）插入后，网口对象的类型是 `BusTerminal`，名称就是设备标识（不带端口代号），符号 `SPECIAL/NWBCP` 变体 2，连接点在符号下方 (0,−4)、朝下。按 Function/Terminal 找引脚会漏掉它们。
- **网口之间的连接：** 拐角连线生成后，`eplan_connection_list` 里两端只有设备、`pin` 为空；电缆定义按设备匹配即可。单线宏插入后改名成项目里已有的设备标识（`=K1+UC1-KE1`、HMI、变频器），和多线、总览表达并成同一设备。
- **中断点名称里的连字符：** `=GAB-24DC-2TB1` 存成 `-24DC-2TB1`，是单独的中断点，不会并进设备 `-2TB1`。
- **挂错结构的 PLC 点：** MX_Dryer25（没有编址）操作面板页的 `KE3:2–4`、`KE5:4` 落在 `=PHA+UC1-` 下；收尾合并 `=PHA+UC1-KE5` 时改名后读回不符被回滚，先合并 `=PHA+UC1-KE3` 后再合并 `KE5` 就成功了。原因还没查清，脚本收尾时重复合并直到没有挂错的点。

### A133. S7-1200 G2、G120C 和喂丝机项目的实测（2026-11-11，EPLAN 2026.0.3.25421，Electric P8，scratch 项目 喂丝机4–9）
- **S7-1200 G2 编址：** CPU 1214C G2（`6ES7214-1AH50-0XB0`）、SM 1221 DI16（`6ES7221-1BH50-0XB0`）、SM 1222 DQ16（`6ES7222-5BH50-0XB0`）的 X10/X11 端子交错：奇数端子 a.0–a.3、偶数端子 a.4–a.7（CPU 的 X10 是 a.0–a.7 在奇数、b.0–b.5 在偶数）。`eplan_plc_readdress` 默认（按接线点）给 X10:3 分到 I0.2、X10:5 分到 I0.4；`sorting:"channel"` 后 X10:1 I0.0、:3 I0.1、:5 I0.2，和硬件一致。总览上 X10 和 X11 的点同名（`-KE2:3` 两个），`eplan_plc_get` 列出时会按名合并，看起来少一半。
- **宏的表达：** 三张卡的 Overview 变体 0 是整卡总览，没有占位符值集；多线变体 8（CPU，88×148）和 3（SM，40×152）是机架图片，其余变体是单个端子块。G120C PN（`6SL3210-1KE1x-xUF1_2D_.ema`）多线变体 0 是功率部分，变体 1 是控制端子（PLC 盒加 25 个端子，屏蔽 SH(3) 是 `DeviceEndTerminal`），单线变体 1 带两个总线端口（`SPECIAL/NWBCP/2`，连接点在符号下方 4 mm）。CPU 单线变体 0 也是两个端口。
- **布局检查超时：** 机架页已有三张 S7-1200 图片时再插菲尼克斯交换机多线宏，插入本身成功，但写后布局检查用了 40 秒（306 个对象、374 条发现），调用超时成 `OUTCOME_UNKNOWN`，工具暂停了该项目的写入。
- **部件随宏：** 明纬 `SDR-120-24` 和施耐德 `XVGKB3SW` 的宏插入后不带部件，要用 `eplan_device_update part` 补挂；施耐德带灯按钮 `XB4BW31B5` 按部件放置时先放出的是指示灯（主功能），常开 13/14 要另用 `join_device` 放。

### B13. PLC 写属性的正式名和值类型（2026-11-04，安装版 2026.0.3 XML 与字典）
- 本机 `Eplan.EplApi.DataModelu.xml` 的 Function 属性文档与 `catalog/eplan-2026.0.3/data-dictionary.jsonl` 对照：20161 `FUNC_PLCCONFIGURATIONPROJECT`、20408 `FUNC_PLCSTATIONNAME`、20409 `FUNC_PLCSTATIONTYPE`、20410 `FUNC_PLCRACK`、20411 `FUNC_PLCSOCKET`、20427 `FUNC_PLCTHISRACK`、20400 `FUNC_PLCADDRESS`、20402 `FUNC_PLCSYMBOLICADDRESS_MANUAL`、20407 `FUNC_PLCCHANNEL_DESIGNATION_MANUAL` 返回 `System.String`。
- 20308 `FUNC_PLC_BUSSYSTEM` 文档类型为 `System.Int64`，XML 列出 0..51（51 为 KNX）；`PropertyValue` 原生整数写入/读回入口是 Int32 隐式转换和 `ToInt()`，该范围可无损表示。以上是静态文档事实；B8 工具写入、传播和撤销尚待 Large 实测。

### A142. 一个事务画 PLC 机架：图片没有主功能，改名读回为连接点（2026-11-15，EPLAN 2026.0.3.25421，Electric P8，A 槽 LargeWrite scratch 副本）
- `eplan_plc_rack`（`PlcRackTests`）在一个 `PartTransactions` 事务里画两张 ET 200SP 数字量卡（`SIE.6ES7131-6BH01-0BA0`、`SIE.6ES7132-6BH01-0BA0`，图片变体 5）：按宏路径插入 Overview、改名、追加部件，再插入多线图片并改成同一标识，提交后传递部件数据。结果已提交，每张卡带自己的部件，`eplan_device_get` 读到的功能同时在机架页和总览页上；两次撤销（机架、建页）后快照与开始时一致。
- 同一事务里，对刚插入的设备改名、改名并入已有设备（图片并入总览设备）、给它追加部件，都有效；主功能在改名前按宏标识读取。
- ET 200SP 的多线图片插入后**没有主功能**（第一次运行在第一张卡的图片处回滚）：设备取放置的功能里最短的标识。
- 这样的设备改名后，`RenameReadBack.MainName` 读回的是它的一个连接点，例如 `=GAA+A1-KR3A:L+`（第二次运行在此回滚）；"新标识 + `:` 连接点"算改名成功。
- 证据：`PlcRackTests` 1/1 通过（684 s），提交 `97f347ed` 之上；前两次失败的日志在会话 scratchpad `after-adr59-1.log`、`plcrack-run.log`。

### A157. 引脚代号带索引时读取 Pin.Name 抛出 IndexedPropertyException（2026-11-13，EPLAN 2026.0.3，16 页重绘）
- **现象：** 读取代号带索引的引脚的 `Pin.Name` 时抛出 `IndexedPropertyException`（S063172），导致 `eplan_page_review` 对该页返回 `INTERNAL`。
- **结论：** 读取器在单个引脚上捕获该异常，并将读不到的代号作为空串处理。
- **证据：** `src/EplanMcp.Bridge.Eplan/Adapters/Devices/EplanPins.cs` 的 `EplanPins.Name`。

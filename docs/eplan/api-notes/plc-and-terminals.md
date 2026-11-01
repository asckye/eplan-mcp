# [EPLAN API 笔记](../api-notes.md)：PLC、电缆与端子编号

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

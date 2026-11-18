# Bridge.Core 测试

本程序集使用 xUnit v3，部分结果使用 Verify；精确版本见 [Directory.Packages.props](../../Directory.Packages.props)。它引用 Bridge.Core，并将 [csproj](EplanMcp.Bridge.Core.Tests.csproj) 列出的适配器源码与测试替身一同编译，无需加载 EPLAN。

```powershell
dotnet test --project tests/EplanMcp.Bridge.Core.Tests -- --filter-not-trait "Size=Large"
```

- `Operations/` 覆盖请求校验、对象映射、过滤、坐标与栅格计算、规划和执行。`TransactionRunnerTests`、`WriteInvokeTests`、`WriteExceptionDiagnosticsTests`、`CancellationTests` 覆盖事务失败、异常映射及取消；`ActionRunOperationTests` / `ActionTryOperationTests` 覆盖 action 的参数和调用结果。
- `Adapters/` 覆盖可脱离 EPLAN 运行的原生前置条件、对象生命周期、布局、文字、移动与读回逻辑；`FakeApiTryDispatcher` 提供 API 试调用替身。
- `Server/BridgeServerTests`（Medium）在真实命名管道上运行 `BridgeServer`，使用内联调度器、假目录和假事务；`BridgeConnectionTests`、`FramingTests` 覆盖连接和消息边界。真实 EPLAN 主线程、授权和撤销行为由 [Large 测试](../EplanMcp.Large.Tests/README.md) 提供证据。

## 未经实机验证的假设

2026-11-18 调查中的 E4 项，风险沿用调查评级。依赖数按可定位到的单测方法计，同一 Theory 的多组输入计一次；仅经过该路径的测试不计入。每行最多列两个方法。

| 编号 | 替身及文件 | 待验证的假设 | 风险 | 依赖单测数及示例 |
|---|---|---|---|---|
| E4-01 | `FakePageReader` · [Fakes.cs](Fakes.cs) | 筛选保留插入顺序。精确查找取首项，缺页返回 `null`，无功能返回空列表。 | 中 | 8 · `PageListOperationTests.Execute_Text_MatchesDescriptionsIgnoringCaseInEplansOrder`、`PageGetOperationTests.Execute_PageWithoutFunctions_HasNoDevices` |
| E4-02 | `FakePageReader` · [Fakes.cs](Fakes.cs) | 类型显示名统一在类型名后加 ` (UI)`。 | 低 | 2 · `PageGetOperationTests.Execute_ExactName_ReturnsThePageAndItsNamedMainFunctionsInOrder`、`PageGetOperationTests.Invoke_ThroughTheRouter_ParsesCamelCaseArgumentsAndReturnsCamelCaseData` |
| E4-03 | `FakePageWriter` · [Fakes.cs](Fakes.cs) | 建页接受除 `xx_XX` 外的所有语言代码。 | 中 | 3 · `PageCreateOperationTests.Plan_ReportsEveryProblemAtOnce`、`PageDescriptionMapTests.TwentyLanguageRequestIsNotLimitedByProjectDisplayLanguages` |
| E4-04 | `FakePageWriter` · [Fakes.cs](Fakes.cs) | 仅大小写不同的页名也触发 `ALREADY_EXISTS`。 | 高 | 0 |
| E4-05 | `FakePageDescriptions` · [Fakes.cs](Fakes.cs) | 缺页和空描述都返回 `null` 和空列表。默认语言为 `zh_CN`，仅拒绝 `xx_XX`。 | 中 | 6 · `PageUpdateOperationTests.Plan_ListsOnlyThePagesThatChangeWithOldAndNewText`、`PageUpdateOperationTests.Execute_ChangesOneLanguageAndKeepsTheOthers` |
| E4-06 | `FakeMacroInserter` · [Fakes.cs](Fakes.cs) | 同页每次插宏都从 `9/9/0/0` 开始，同序号复用身份。 | 高 | 0 |
| E4-07 | `FakeMacroInserter` · [Fakes.cs](Fakes.cs) | 插宏报告的对象数恒等于功能数加 9。 | 高 | 1 · `MacroInsertOperationTests.Execute_InsertsInOneCommittedTransaction_AndListsTheDevicesAsReadBack` |
| E4-08 | `FakeConnectionGenerator` · [Fakes.cs](Fakes.cs) | 按请求页序逐页返回结果，区分大小写，缺键补空列表。 | 中 | 3 · `ConnectionGenerateOperationTests.Plan_IsOneConnectionsUpdatePerPageWithTheCountNow`、`ConnectionGenerateOperationTests.Execute_GeneratesInOneTransactionAndListsTheNewConnections` |
| E4-09 | `FakeConnectionGenerator` · [Fakes.cs](Fakes.cs) | 只有 `Stale` 中的同一记录实例会被移除。 | 高 | 2 · `ConnectionGenerateOperationTests.Execute_ListsRemovedConnections_AndKeepsOnesWhoseEndsSwapped`、`ConnectionGenerateOperationTests.Execute_ListsAtMost100Connections_TheRemovedFirst` |
| E4-10 | `FakeDeviceRenumberer` · [Fakes.cs](Fakes.cs) | 编号可改变范围内所有功能的会话 ID，包括名称未改的功能。 | 高 | 1 · `DeviceRenumberOperationTests.Execute_WhenEplanGivesNewIdentifiers_SaysSoInsteadOfGuessing` |
| E4-11 | `FakePageRenumberer` · [Fakes.cs](Fakes.cs) | 跨结构连续编号，保留文字和子页模式均不影响结果。 | 高 | 0 |
| E4-12 | `FakePageRenumberer` · [Fakes.cs](Fakes.cs) | 首次按旧页名生成的 ID 在重编号后保持。 | 高 | 0 |
| E4-13 | `FakeCableRenumberer.Renumber` · [FakeCableRenumberer.cs](FakeCableRenumberer.cs) | 编号可改变范围内全部电缆 ID，包括保留原名的电缆。 | 高 | 1 · `CableRenumberOperationTests.Execute_WhenEplanGivesNewIdentifiers_SaysSoInsteadOfGuessing` |
| E4-14 | `FakeConnectionNumberer.Designate` · [FakeConnectionNumberer.cs](FakeConnectionNumberer.cs) | 编号可改变全部连接 ID，包括未获得新线号的连接。 | 高 | 1 · `ConnectionNumberOperationTests.Execute_WhenEplanGivesNewIdentifiers_SaysSoInsteadOfGuessing` |
| E4-15 | `FakeTerminalRenumberer.Renumber` · [FakeTerminalRenumberer.cs](FakeTerminalRenumberer.cs) | 仅选一个端子排时也可能改到未选排。 | 高 | 1 · `TerminalRenumberOperationTests.Execute_OneStrip_WhenEplanAlsoRenamesAnotherStrip_RollsBack` |
| E4-16 | `FakeTerminalRenumberer.Renumber` · [FakeTerminalRenumberer.cs](FakeTerminalRenumberer.cs) | 编号可改变遍历到的全部端子 ID，包括未改名端子。 | 高 | 1 · `TerminalRenumberOperationTests.Execute_WhenEplanGivesNewIdentifiers_SaysSoInsteadOfGuessing` |
| E4-17 | `FakeTerminalRenumberer.ReadTerminals` · [FakeTerminalRenumberer.cs](FakeTerminalRenumberer.cs) | 排名按 ordinal 查找，缺排返回 `null`，全项目读回按排和端子插入顺序展开。 | 中 | 3 · `TerminalRenumberOperationTests.Plan_NamesEveryMissingStrip_AndEveryArgumentProblemAtOnce`、`TerminalRenumberOperationTests.Execute_OnlyUnnumbered_WhenEplanAlsoRenamesNumberedTerminals_RollsBackAndListsThem` |
| E4-18 | `BoxedDevice` · [FakeBoxedDevice.cs](Adapters/FakeBoxedDevice.cs) | 创建前设置符号变体的 CLR 异常类型为 `InvalidOperationException`。 | 中 | 0 |
| E4-19 | `BoxedDevice.IsFunctionInside` · [FakeBoxedDevice.cs](Adapters/FakeBoxedDevice.cs) | 插入点落在闭合矩形内即归属该框，包括四条边。 | 高 | 1 · `BlackBoxPlacementTests.CommittedDevicePoint_ReadsNativeContainmentWithoutRejectingOutside` |
| E4-20 | `TerminalStrip.Create` · [FakeTerminal.cs](Adapters/FakeTerminal.cs) | 项目重载创建的未放置端子排无法被后续导航器查找。 | 高 | 0 |
| E4-21 | `Creator.RestoreContent` · [CableDefinitionExecutionTests.cs](Operations/CableDefinitionExecutionTests.cs) | 补偿恢复业务内容时可同时更换根对象和线 ID，并调换端点。 | 高 | 1 · `CableDefinitionExecutionTests.Undo_RenumbersIdentifiersAndRestoresContent_ReportsRolledBack` |
| E4-22 | `Cable.AddLocalFunctionTemplates` · [FakeCable.cs](Adapters/FakeCable.cs) | 本地芯线模板与原导线共享同一份可写属性。 | 高 | 0 |
| E4-23 | `CableService.DoReassignWires` · [FakeCableService.cs](Adapters/FakeCableService.cs) | 四参重分配只核对数量，模板、分配开关和排除项不影响分配。 | 高 | 2 · `CableReadbackTests.CommittedStages_ReadbackRediscoversNativeObjectsAndReleasesFreshLists`、`CableNativeLifetimeTests.NumericSection_StoresCommaOnCoreAndPreservesExplicitCableText` |
| E4-24 | `NativeCableList.Dispose` · [FakeCableDataModel.cs](Adapters/FakeCableDataModel.cs) | 当前代内重复释放有 store 的列表抛 `ObjectDisposedException`，无 store 时允许重复释放。 | 中 | 1 · `CableNativeLifetimeTests.ScopedFunctionList_DisposedBeforeCommit_ReleasesTheListAndInvalidatesItsValues` |
| E4-25 | `Connection.CableNameParts` · [FakeCableDataModel.cs](Adapters/FakeCableDataModel.cs) | 绑定只把 `DESIGNATION_LOCATION` 复制到 20005，其他结构段不参与。 | 高 | 2 · `CableReadbackTests.CommittedStages_ReadbackRediscoversNativeObjectsAndReleasesFreshLists`、`CableNativeLifetimeTests.NumericSection_StoresCommaOnCoreAndPreservesExplicitCableText` |
| E4-26 | `FunctionPropertyList` · [FakeTerminalProperties.cs](Adapters/FakeTerminalProperties.cs) | 索引属性以 `id + index` 为标量键，使 20022[1] 和 20023 互相覆盖。 | 高 | 0 |
| E4-27 | `Rectangle.SetArea` · [FakeDrawingGraphics.cs](Adapters/FakeDrawingGraphics.cs) | 矩形已有非默认两角时再次调用 `SetArea` 会失败。 | 高 | 0 |
| E4-28 | `Creator.Read/Graphical/Generate` · [ConnectionCreateExecutionTests.cs](Operations/ConnectionCreateExecutionTests.cs) | 缺少显式读回数据时用规划器预测几何作为写后自动连线几何。 | 高 | 9 · `ConnectionCreateExecutionTests.Success_CommitsPlacementBeforeGeneration_ReadsActualEndpoints`、`ConnectionCreateExecutionTests.UnknownPrediction_MeasuredOverlap_CommitsAndGeneratesWithAFix` |
| E4-29 | `FakePageRemover.ReadPage` · [Fakes.cs](Fakes.cs) | 删页清单只含合成页键和本页功能 ID，其他放置不影响指纹。 | 高 | 0 |
| E4-30 | `Connection` · [FakeCableDataModel.cs](Adapters/FakeCableDataModel.cs) | 实际导线和模板都返回对象号 9，且 `IsTemplate` 都为真。 | 高 | 2 · `CableReadbackTests.CommittedStages_ReadbackRediscoversNativeObjectsAndReleasesFreshLists`、`CableNativeLifetimeTests.NumericSection_StoresCommaOnCoreAndPreservesExplicitCableText` |
| E4-31 | `ConnectionDefinitionPoint.Name` · [FakeCableDataModel.cs](Adapters/FakeCableDataModel.cs) | 定义点全名直接取自电缆，即使未写点名也能读回。 | 高 | 2 · `CableReadbackTests.CommittedStages_ReadbackRediscoversNativeObjectsAndReleasesFreshLists`、`CableNativeLifetimeTests.NumericSection_StoresCommaOnCoreAndPreservesExplicitCableText` |

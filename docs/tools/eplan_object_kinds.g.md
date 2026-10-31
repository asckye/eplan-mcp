<!-- 此表由 eng/coverage generate 从目录生成，请勿手工编辑。 -->

| 类型 | 状态 | 原因 / 枚举依据 | 属性字典 |
|---|---|---|---|
| Arc | supported | finder.GetPlacements(null) + finder.GetStorableObjects(null) + Placements(finder, check, pageName) | Arc |
| Area | supported | finder.GetFunctions3D(null) + finder.GetPlacements3D(null) + finder.GetStorableObjects(null) | Function3D |
| Article | supported | finder.GetStorableObjects(null) | Article |
| ArticleReference | unsupported | transient_reference:A60 | ArticleReference |
| AuxiliaryLine | supported | finder.GetPlacements3D(null) + finder.GetStorableObjects(null) | Placement3D |
| BarBase | supported | finder.GetFunctions3D(null) + finder.GetPlacements3D(null) + finder.GetStorableObjects(null) | Function3D |
| BlackBox | supported | 兼容筛选名称（A60） | Function |
| Block | supported | finder.GetPlacements(null) + finder.GetStorableObjects(null) + Placements(finder, check, pageName) | Block |
| BoxedDevice | supported | finder.GetBoxedDevices(null) + finder.GetCablesWithFilterScheme(null) + finder.GetFunctions(null) + finder.GetFunctionsWithFilterScheme(null) + finder.GetPlacements(null) + finder.GetStorableObjects(null) + Placements(finder, check, pageName) | Function |
| BusBar | supported | finder.GetFunctions3D(null) + finder.GetPlacements3D(null) + finder.GetStorableObjects(null) | BusBar |
| BusBarSystem | supported | finder.GetFunctions3D(null) + finder.GetPlacements3D(null) + finder.GetStorableObjects(null) | Function3D |
| BusTerminal | supported | finder.GetCablesWithFilterScheme(null) + finder.GetFunctions(null) + finder.GetFunctionsWithFilterScheme(null) + finder.GetPLCTerminalsWithFilterScheme(null) + finder.GetPlacements(null) + finder.GetStorableObjects(null) + finder.GetTerminals(null) + finder.GetTerminalsWithFilterScheme(null) + Placements(finder, check, pageName) | Function |
| Cabinet | supported | finder.GetFunctions3D(null) + finder.GetPlacements3D(null) + finder.GetStorableObjects(null) | Function3D |
| Cable | supported | finder.GetCablesWithFilterScheme(null) + finder.GetFunctions(null) + finder.GetFunctionsWithFilterScheme(null) + finder.GetPlacements(null) + finder.GetStorableObjects(null) + Placements(finder, check, pageName) | Cable |
| CableUnit | supported | finder.GetCableUnits(null) + finder.GetCableUnitsWithFilterScheme(null) + finder.GetCablesWithFilterScheme(null) + finder.GetFunctions(null) + finder.GetFunctionsWithFilterScheme(null) + finder.GetPlacements(null) + finder.GetStorableObjects(null) + Placements(finder, check, pageName) | Function |
| Comment | supported | finder.GetPlacements(null) + finder.GetStorableObjects(null) + Placements(finder, check, pageName) | Comment |
| Comparison | supported | finder.GetStorableObjects(null) | StorableObject |
| ComparisonEntry | supported | finder.GetStorableObjects(null) | StorableObject |
| ComparisonEntryDetail | supported | finder.GetStorableObjects(null) | StorableObject |
| Component | supported | finder.GetFunctions3D(null) + finder.GetPlacements3D(null) + finder.GetStorableObjects(null) | Function3D |
| Connection | supported | finder.GetConnections(null) + finder.GetConnectionsWithFilterScheme(null) + finder.GetStorableObjects(null) | Connection |
| Connection3D | supported | finder.GetConnections(null) + finder.GetConnectionsWithFilterScheme(null) + finder.GetStorableObjects(null) + project.InstallationSpaces.SelectMany(s => s.Connection3Ds) | Connection |
| ConnectionDefinitionPoint | supported | finder.GetCablesWithFilterScheme(null) + finder.GetFunctions(null) + finder.GetFunctionsWithFilterScheme(null) + finder.GetPlacements(null) + finder.GetStorableObjects(null) + Placements(finder, check, pageName) | ConnectionDefinitionPoint |
| ConnectionPointsInfo | supported | finder.GetStorableObjects(null) | ConnectionPointsInfo |
| ContactImage | supported | finder.GetPlacements(null) + finder.GetStorableObjects(null) + Placements(finder, check, pageName) | GraphicalPlacement |
| ContextPropertyPlacement | unsupported | shared_parent_identity:A57 | TextBase |
| ContextPropertyPlacement3D | unsupported | shared_parent_identity:A57 | TextBase |
| CopperBundle | supported | finder.GetFunctions3D(null) + finder.GetPlacements3D(null) + finder.GetStorableObjects(null) | CopperBundle |
| DaisyChain | supported | finder.GetStorableObjects(null) | StorableObject |
| DeletedObjectInfo | supported | finder.GetPlacements(null) + finder.GetStorableObjects(null) + Placements(finder, check, pageName) | DeletedObjectInfo |
| DeviceGroup | supported | finder.GetPlacements(null) + finder.GetStorableObjects(null) + Placements(finder, check, pageName) | Placement |
| DeviceListEntry | supported | finder.GetStorableObjects(null) | DeviceListEntry |
| DimensionCircle | supported | finder.GetPlacements(null) + finder.GetStorableObjects(null) + Placements(finder, check, pageName) | DimensionCircle |
| DimensionGroup | supported | finder.GetPlacements(null) + finder.GetStorableObjects(null) + Placements(finder, check, pageName) | Placement |
| DimensionItem | supported | finder.GetPlacements(null) + finder.GetStorableObjects(null) + Placements(finder, check, pageName) | DimensionItem |
| DocumentBase | supported | finder.GetPlacements(null) + finder.GetStorableObjects(null) + Placements(finder, check, pageName) | Placement |
| Drilling | supported | finder.GetFunctions3D(null) + finder.GetPlacements3D(null) + finder.GetStorableObjects(null) | Function3D |
| Duct | supported | finder.GetFunctions3D(null) + finder.GetPlacements3D(null) + finder.GetStorableObjects(null) | Function3D |
| DynamicConnectionLine | supported | finder.GetPlacements(null) + finder.GetStorableObjects(null) + Placements(finder, check, pageName) | SymbolReference |
| Function | supported | finder.GetCablesWithFilterScheme(null) + finder.GetFunctions(null) + finder.GetFunctionsWithFilterScheme(null) + finder.GetPlacements(null) + finder.GetStorableObjects(null) + Placements(finder, check, pageName) | Function |
| Function3D | supported | finder.GetFunctions3D(null) + finder.GetPlacements3D(null) + finder.GetStorableObjects(null) | Function3D |
| FunctionBase | supported | finder.GetPlacements(null) + finder.GetStorableObjects(null) + Placements(finder, check, pageName) | FunctionBase |
| FunctionDefinition | supported | finder.GetStorableObjects(null) | StorableObject |
| FunctionDefinitionLibrary | supported | finder.GetStorableObjects(null) | FunctionDefinitionLibrary |
| GraphicalPlacement | supported | finder.GetPlacements(null) + finder.GetStorableObjects(null) + Placements(finder, check, pageName) | GraphicalPlacement |
| Group | supported | finder.GetPlacements(null) + finder.GetStorableObjects(null) + Placements(finder, check, pageName) | Placement |
| Group3D | supported | finder.GetStorableObjects(null) | StorableObject |
| Harness | supported | finder.GetCablesWithFilterScheme(null) + finder.GetFunctions(null) + finder.GetFunctionsWithFilterScheme(null) + finder.GetHarnesses(null) + finder.GetHarnessesWithFilterScheme(null) + finder.GetPlacements(null) + finder.GetStorableObjects(null) + Placements(finder, check, pageName) | Function |
| HyperLink | supported | finder.GetPlacements(null) + finder.GetStorableObjects(null) + Placements(finder, check, pageName) | HyperLink |
| Image | supported | finder.GetPlacements(null) + finder.GetStorableObjects(null) + Placements(finder, check, pageName) | Image |
| InstallationSpace | supported | finder.GetPlacements3D(null) + finder.GetStorableObjects(null) + project.InstallationSpaces | InstallationSpace |
| InterruptionPoint | supported | finder.GetInterruptionPoints(null) + finder.GetInterruptionPointsWithFilterScheme(null) + finder.GetPlacements(null) + finder.GetStorableObjects(null) + Placements(finder, check, pageName) | InterruptionPoint |
| Line | supported | finder.GetPlacements(null) + finder.GetStorableObjects(null) + Placements(finder, check, pageName) | Line |
| Location | supported | finder.GetStorableObjects(null) | Location |
| LocationBox | supported | finder.GetPlacements(null) + finder.GetStorableObjects(null) + Placements(finder, check, pageName) | LocationBox |
| LockedMountingArea | supported | finder.GetPlacements(null) + finder.GetStorableObjects(null) + Placements(finder, check, pageName) | Rectangle |
| MDFunctionTemplatePosition | unsupported | not_storable_catalog_type | MDFunctionTemplatePosition |
| MDPartsDatabaseItem | unsupported | not_storable_catalog_type | MDPartsDatabaseItem |
| MDSymbol | unsupported | not_storable_catalog_type | MDSymbol |
| MDSymbolLibrary | unsupported | not_storable_catalog_type | MDSymbolLibrary |
| MDSymbolVariant | unsupported | not_storable_catalog_type | MDSymbolVariant |
| MacroBox | supported | finder.GetPlacements(null) + finder.GetStorableObjects(null) + Placements(finder, check, pageName) | MacroBox |
| MacroEntry | supported | finder.GetPlacements(null) + finder.GetStorableObjects(null) + Placements(finder, check, pageName) | MacroEntry |
| MateEntry | unsupported | unstable_identity:A62 | Placement3D |
| MergedArticleReference | supported | finder.GetStorableObjects(null) | MergedArticleReference |
| MergedConnection | supported | finder.GetStorableObjects(null) | StorableObject |
| MergedFunction | supported | finder.GetStorableObjects(null) | StorableObject |
| MountingPanel | supported | finder.GetFunctions3D(null) + finder.GetPlacements3D(null) + finder.GetStorableObjects(null) | Function3D |
| MountingRail | supported | finder.GetFunctions3D(null) + finder.GetPlacements3D(null) + finder.GetStorableObjects(null) | Function3D |
| NetDefinitionPoint | supported | finder.GetNetDefinitionPoints(null) + finder.GetPlacements(null) + finder.GetPotentialDefinitionsWithFilterScheme(null) + finder.GetStorableObjects(null) + Placements(finder, check, pageName) | PotentialDefinition |
| Option | supported | finder.GetOptionObjectsWithFilterScheme(null) + finder.GetStorableObjects(null) | Option |
| OptionBase | supported | finder.GetStorableObjects(null) | StorableObject |
| OptionFragment | supported | finder.GetStorableObjects(null) | OptionFragment |
| OptionGroup | supported | finder.GetStorableObjects(null) | OptionGroup |
| PCTLoop | supported | finder.GetPlanningSegments(null) + finder.GetPlanningSegmentsWithFilterScheme(null) + finder.GetStorableObjects(null) | PlanningSegment |
| PCTLoopFunction | supported | finder.GetPlanningSegments(null) + finder.GetPlanningSegmentsWithFilterScheme(null) + finder.GetStorableObjects(null) | PlanningSegment |
| PLC | supported | finder.GetCablesWithFilterScheme(null) + finder.GetFunctions(null) + finder.GetFunctionsWithFilterScheme(null) + finder.GetPLCs(null) + finder.GetPLCsWithFilterScheme(null) + finder.GetPlacements(null) + finder.GetStorableObjects(null) + Placements(finder, check, pageName) | Function |
| PLCMergedFunction | unsupported | catalog_excluded | StorableObject |
| PLCTerminal | supported | 兼容筛选名称（A60） | Function |
| Page | supported | finder.GetPages(null) + finder.GetPagesWithFilterScheme(null) + finder.GetPlacements(null) + Placements(finder, check, pageName) | Page |
| PathText | supported | finder.GetPlacements(null) + finder.GetStorableObjects(null) + Placements(finder, check, pageName) | PathText |
| PipingDefinition | supported | finder.GetPlacements(null) + finder.GetPotentialDefinitionsWithFilterScheme(null) + finder.GetStorableObjects(null) + Placements(finder, check, pageName) | PipingDefinition |
| PlaceHolder | supported | finder.GetPlaceholdersWithFilterScheme(null) + finder.GetPlacements(null) + finder.GetStorableObjects(null) + Placements(finder, check, pageName) | PlaceHolder |
| PlaceHolder3D | supported | finder.GetPlaceHolders3D(null) + finder.GetStorableObjects(null) | StorableObject |
| PlaceHolderText | supported | finder.GetPlacements(null) + finder.GetStorableObjects(null) + Placements(finder, check, pageName) | Text |
| Placement | supported | finder.GetPlacements(null) + finder.GetStorableObjects(null) + Placements(finder, check, pageName) | Placement |
| Placement3D | supported | finder.GetPlacements3D(null) + finder.GetStorableObjects(null) | Placement3D |
| Plane | supported | finder.GetPlacements3D(null) + finder.GetStorableObjects(null) | Placement3D |
| PlanningObject | supported | finder.GetPlanningSegments(null) + finder.GetPlanningSegmentsWithFilterScheme(null) + finder.GetStorableObjects(null) | PlanningSegment |
| PlanningSegment | supported | finder.GetPlanningSegments(null) + finder.GetPlanningSegmentsWithFilterScheme(null) + finder.GetStorableObjects(null) | PlanningSegment |
| PlcIO | supported | finder.GetStorableObjects(null) | PlcIO |
| PlotFrame | supported | finder.GetPlacements(null) + finder.GetStorableObjects(null) + Placements(finder, check, pageName) | PlotFrame |
| Plug | supported | finder.GetCablesWithFilterScheme(null) + finder.GetFunctions(null) + finder.GetFunctionsWithFilterScheme(null) + finder.GetPlacements(null) + finder.GetPlugs(null) + finder.GetPlugsWithFilterScheme(null) + finder.GetStorableObjects(null) + Placements(finder, check, pageName) | Function |
| PlugStrip | supported | finder.GetCablesWithFilterScheme(null) + finder.GetFunctions(null) + finder.GetFunctionsWithFilterScheme(null) + finder.GetPlacements(null) + finder.GetPlugStrips(null) + finder.GetPlugStripsWithFilterScheme(null) + finder.GetStorableObjects(null) + Placements(finder, check, pageName) | PlugStrip |
| PolyLine | supported | finder.GetPlacements(null) + finder.GetStorableObjects(null) + Placements(finder, check, pageName) | GraphicalPlacement |
| PotentialDefinition | supported | finder.GetPlacements(null) + finder.GetPotentialDefinitionsWithFilterScheme(null) + finder.GetStorableObjects(null) + Placements(finder, check, pageName) | PotentialDefinition |
| Project | supported | finder.GetStorableObjects(null) + new[] { project } | Project |
| PropertyPlacement | unsupported | shared_parent_identity:A57 | TextBase |
| ProtectedGroup | supported | finder.GetPlacements(null) + finder.GetStorableObjects(null) + Placements(finder, check, pageName) | ProtectedGroup |
| QRCode | supported | finder.GetPlacements(null) + finder.GetStorableObjects(null) + Placements(finder, check, pageName) | QRCode |
| Rectangle | supported | finder.GetPlacements(null) + finder.GetStorableObjects(null) + Placements(finder, check, pageName) | Rectangle |
| ReportBlock | supported | finder.GetStorableObjects(null) | StorableObject |
| ReportBlockReference | supported | finder.GetPlacements(null) + finder.GetStorableObjects(null) + Placements(finder, check, pageName) | Placement |
| RoutedConnection | supported | finder.GetConnections(null) + finder.GetConnectionsWithFilterScheme(null) + finder.GetStorableObjects(null) | RoutedConnection |
| RoutingAccessory | supported | finder.GetFunctions3D(null) + finder.GetPlacements3D(null) + finder.GetStorableObjects(null) | Placement3D |
| RoutingHose | supported | finder.GetFunctions3D(null) + finder.GetPlacements3D(null) + finder.GetStorableObjects(null) | Placement3D |
| RoutingSegment | supported | finder.GetFunctions3D(null) + finder.GetPlacements3D(null) + finder.GetStorableObjects(null) | Function3D |
| RoutingSpline | supported | finder.GetFunctions3D(null) + finder.GetPlacements3D(null) + finder.GetStorableObjects(null) | Function3D |
| Segment | supported | finder.GetCablesWithFilterScheme(null) + finder.GetFunctions(null) + finder.GetFunctionsWithFilterScheme(null) + finder.GetPlacements(null) + finder.GetStorableObjects(null) + Placements(finder, check, pageName) | Segment |
| SegmentDefinition | supported | finder.GetSegmentDefinitions(null) + finder.GetStorableObjects(null) | SegmentDefinition |
| SegmentPlacement | supported | finder.GetPlacements(null) + finder.GetSegmentPlacements(null) + finder.GetStorableObjects(null) + Placements(finder, check, pageName) | SegmentPlacement |
| SegmentTemplate | supported | finder.GetSegmentTemplates(null) + finder.GetSegmentTemplatesWithFilterScheme(null) + finder.GetStorableObjects(null) | SegmentTemplate |
| Shield | supported | finder.GetCablesWithFilterScheme(null) + finder.GetFunctions(null) + finder.GetFunctionsWithFilterScheme(null) + finder.GetPlacements(null) + finder.GetStorableObjects(null) + Placements(finder, check, pageName) | Function |
| Shielding | supported | finder.GetPlacements(null) + finder.GetStorableObjects(null) + Placements(finder, check, pageName) | GraphicalPlacement |
| Spline | supported | finder.GetPlacements(null) + finder.GetStorableObjects(null) + Placements(finder, check, pageName) | GraphicalPlacement |
| StorableObject | supported | finder.GetStorableObjects(null) | StorableObject |
| StrandConnector | supported | finder.GetPlacements(null) + finder.GetStorableObjects(null) + Placements(finder, check, pageName) | StrandConnector |
| StrandInterConnector | supported | finder.GetPlacements(null) + finder.GetStorableObjects(null) + Placements(finder, check, pageName) | SymbolReference |
| StructureSegment | supported | finder.GetPlanningSegments(null) + finder.GetPlanningSegmentsWithFilterScheme(null) + finder.GetStorableObjects(null) | PlanningSegment |
| Symbol | supported | finder.GetSymbolsWithFilterScheme(null) | Symbol |
| SymbolLibrary | supported | finder.GetStorableObjects(null) | SymbolLibrary |
| SymbolReference | supported | finder.GetPlacements(null) + finder.GetStorableObjects(null) + Placements(finder, check, pageName) | SymbolReference |
| SymbolVariant | supported | finder.GetPlacements(null) + finder.GetStorableObjects(null) + Placements(finder, check, pageName) | SymbolVariant |
| Terminal | supported | finder.GetCablesWithFilterScheme(null) + finder.GetFunctions(null) + finder.GetFunctionsWithFilterScheme(null) + finder.GetPLCTerminalsWithFilterScheme(null) + finder.GetPlacements(null) + finder.GetStorableObjects(null) + finder.GetTerminals(null) + finder.GetTerminalsWithFilterScheme(null) + Placements(finder, check, pageName) | Function |
| TerminalAccessory | unsupported | transient_reference:A60 | ArticleReference |
| TerminalStrip | supported | finder.GetCablesWithFilterScheme(null) + finder.GetFunctions(null) + finder.GetFunctionsWithFilterScheme(null) + finder.GetPlacements(null) + finder.GetStorableObjects(null) + finder.GetTerminalStrips(null) + finder.GetTerminalStripsWithFilterScheme(null) + Placements(finder, check, pageName) | TerminalStrip |
| Text | supported | finder.GetPlacements(null) + finder.GetStorableObjects(null) + Placements(finder, check, pageName) | Text |
| TextBase | supported | finder.GetPlacements(null) + finder.GetStorableObjects(null) + Placements(finder, check, pageName) | TextBase |
| TopologyLine | supported | finder.GetPlacements(null) + finder.GetStorableObjects(null) + Placements(finder, check, pageName) | Line |
| Vessel | supported | finder.GetPlanningSegments(null) + finder.GetPlanningSegmentsWithFilterScheme(null) + finder.GetStorableObjects(null) | PlanningSegment |
| ViewPart | supported | finder.GetPlacements(null) + finder.GetStorableObjects(null) + Placements(finder, check, pageName) | Block |
| ViewPlacement | supported | finder.GetPlacements(null) + finder.GetStorableObjects(null) + Placements(finder, check, pageName) | ViewPlacement |

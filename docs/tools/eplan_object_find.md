# 工具规格：`eplan_object_find`

`object` / `full` / Read；title 为 Find EPLAN objects，四注解为 true / false / true / false。本批支持 Page、Function、Connection。输入闭合：object_type 为下表精确类型；project 可省略，解析当前或唯一已打开项目；page 为精确页全名，name_contains 为名称子串（OrdinalIgnoreCase）；limit 默认 50、范围 1–200，cursor 不透明。输出 project 回显解析路径，status 为 supported / unsupported，reason 说明不支持原因；items 含 DrawingLocator（project/page/type/oid/digest）及 name；另有 cursor、truncated。按 type/oid 排序去重，只匹配精确运行时类型；页自身 locator.page 为页名，未放置对象为 ""。游标十分钟有效，绑定 MCP 连接、Bridge 实例、项目、筛选条件及已返回内容指纹；续页重读前缀，变化报 INVALID_ARGUMENT，不能视为原子快照。双份 JSON 加摘要约 5k token，整行切页，单行超限明确报错。Bridge 每块最多 200 项，枚举上限 50000 项，超过时报 CAPABILITY_MISSING，需缩小页范围；主线程 LockingStep 内检查取消与 20 秒时限，不导航、不生成数据、不写项目、不释放借用对象。错误沿用共享 Read 信封：INVALID_ARGUMENT 修正参数或去掉游标；PROJECT_NOT_OPEN 用项目列表确认；PLAN_STALE 重新 find；无 execution。Small 使用假属性资源测试分页、拒读与释放；Medium 通过真实 MCP 和 Fake Bridge 验证发现、预算、游标与失败路径；Large 对 fixture、LargeWrite 的全部 supported 类型各读到至少一个对象和非空属性，仅编写未运行。评测：find 后读英文属性并解释 Arc 的 unsupported（evals/tasks/object-properties.yaml）。

## 68 种类型的支持范围

“待接入”表示本批行数限制下尚未实现，并非断言公开 API 无法枚举。后续须逐类核对公开 API、所有权和夹具后接入，禁止反射猜测。

| 类型 | 状态 | 原因 / 枚举依据 |
|---|---|---|
| Page | supported | DMObjectsFinder.GetPages，A36 |
| Function | supported | GetFunctions + 页 AllPlacements，A12/A36 |
| Connection | supported | GetConnections，按 Connection.Page 归页，A36 |
| Arc、Article、ArticleReference、Block、BusBar、Cable、Comment、ConnectionDefinitionPoint、ConnectionPointsInfo、CopperBundle | unsupported | 本批待接入，未核对公开枚举及属性入口 |
| DeletedObjectInfo、DeviceListEntry、DimensionCircle、DimensionItem、Function3D、FunctionBase、FunctionDefinitionLibrary、GraphicalPlacement、HyperLink、Image | unsupported | 本批待接入，未核对公开枚举及属性入口 |
| InstallationSpace、InterruptionPoint、Line、Location、LocationBox、MacroBox、MacroEntry、MDFunctionTemplatePosition、MDPartsDatabaseItem、MDSymbol | unsupported | 本批待接入，未核对公开枚举及属性入口 |
| MDSymbolLibrary、MDSymbolVariant、MergedArticleReference、Option、OptionFragment、OptionGroup、PathText、PipingDefinition、PlaceHolder、Placement | unsupported | 本批待接入，未核对公开枚举及属性入口 |
| Placement3D、PlanningSegment、PlcIO、PlotFrame、PlugStrip、PotentialDefinition、Project、ProtectedGroup、QRCode、Rectangle | unsupported | 本批待接入，未核对公开枚举及属性入口 |
| RoutedConnection、Segment、SegmentDefinition、SegmentPlacement、SegmentTemplate、StorableObject、StrandConnector、Symbol、SymbolLibrary、SymbolReference | unsupported | 本批待接入，未核对公开枚举及属性入口 |
| SymbolVariant、TerminalStrip、Text、TextBase、ViewPlacement | unsupported | 本批待接入，未核对公开枚举及属性入口 |

# 工具规格：`eplan_object_find`

`object` / `full` / Read；title 为 Find EPLAN objects，四注解为 true / false / true / false。支持范围见下表。输入闭合：object_type 为下表精确类型或下文的功能筛选名称；project 可省略，解析当前或唯一已打开项目；page 为精确页全名，name_contains 为名称子串（OrdinalIgnoreCase）；limit 默认 50、范围 1–200，cursor 不透明。输出 project 回显解析路径，status 为 supported / unsupported，reason 说明不支持原因；items 含 DrawingLocator（project/page/type/oid/digest）及 name；另有 cursor、truncated。按 type/oid 排序去重，只匹配精确运行时类型（功能筛选名称除外）；页自身 locator.page 为页名，未放置对象为 ""。游标十分钟有效，绑定 MCP 连接、Bridge 实例、项目、筛选条件及已返回内容指纹；续页重读前缀，变化报 INVALID_ARGUMENT，不能视为原子快照。双份 JSON 加摘要约 5k token，整行切页，单行超限明确报错。Bridge 每块最多 200 项，枚举上限 50000 项，超过时报 CAPABILITY_MISSING，需缩小页范围；主线程 LockingStep 内检查取消与 20 秒时限，不导航、不生成数据、不写项目、不释放借用对象。错误沿用共享 Read 信封：INVALID_ARGUMENT 修正参数或去掉游标；PROJECT_NOT_OPEN 用项目列表确认；PLAN_STALE 重新 find；无 execution。Small 使用假属性资源测试分页、拒读与释放；Medium 通过真实 MCP 和 Fake Bridge 验证发现、预算、游标与失败路径；Large 对每种 supported 查询在 fixture 或 LargeWrite 中至少读到一个对象和非空属性，仅编写未运行。评测：find 后读英文属性并解释 Arc 的 unsupported（evals/tasks/object-properties.yaml）。

## 68 种类型的支持范围

字典仍为 68 类，其中 10 类 supported；另有 4 个共享 Function 字典的查询名称，下文单列。“待接入”表示本批行数限制下尚未实现，并非断言公开 API 无法枚举。后续须逐类核对公开 API、所有权和夹具后接入，禁止反射猜测。

| 类型 | 状态 | 原因 / 枚举依据 |
|---|---|---|
| Page | supported | DMObjectsFinder.GetPages，A36 |
| Function | supported | GetFunctions + 页 AllPlacements，A12/A36 |
| Connection | supported | GetConnections，按 Connection.Page 归页，A36 |
| TerminalStrip | supported | GetTerminalStrips(null)，必须使用自身 Properties，A60 |
| Cable | supported | GetCablesWithFilterScheme(null)，精确类型排除屏蔽功能，A24/A60 |
| PlaceHolder | supported | GetPlaceholdersWithFilterScheme(null)，不读需写锁的当前值集，A33/A60 |
| LocationBox | supported | 页 AllPlacements；公开类型即结构盒 / 位置盒，A55/A60 |
| InterruptionPoint | supported | GetInterruptionPoints(null)，A60 |
| PotentialDefinition | supported | GetPotentialDefinitionsWithFilterScheme(null)，A60 |
| MacroBox | supported | 页 AllPlacements，只取页与位置；名称留空，不暴露宏文件路径，A60 |
| ArticleReference | unsupported | transient 引用，尚未核实父对象 / ReferencePos 与通用 locator 的稳定映射；可读父功能或连接的部件属性，A60 |
| DeletedObjectInfo | unsupported | 修订删除标记的枚举与专用夹具待核实；修订状态沿用 eplan_revision_list |
| MacroEntry | unsupported | 宏导航器条目，项目枚举及夹具待核实；本批支持已放置的 MacroBox |
| PlcIO | unsupported | 未分配地址条目，不是 PLC 连接点；连接点使用下文 PLCTerminal |
| Arc、Article、Block、BusBar、Comment、ConnectionDefinitionPoint、ConnectionPointsInfo、CopperBundle | unsupported | 本批待接入，未核对公开枚举及属性入口 |
| DeviceListEntry、DimensionCircle、DimensionItem、Function3D、FunctionBase、FunctionDefinitionLibrary、GraphicalPlacement、HyperLink、Image | unsupported | 本批待接入，未核对公开枚举及属性入口 |
| InstallationSpace、Line、Location、MDFunctionTemplatePosition、MDPartsDatabaseItem、MDSymbol | unsupported | 本批待接入，未核对公开枚举及属性入口 |
| MDSymbolLibrary、MDSymbolVariant、MergedArticleReference、Option、OptionFragment、OptionGroup、PathText、PipingDefinition、Placement | unsupported | 本批待接入，未核对公开枚举及属性入口 |
| Placement3D、PlanningSegment、PlotFrame、PlugStrip、Project、ProtectedGroup、QRCode、Rectangle | unsupported | 本批待接入，未核对公开枚举及属性入口 |
| RoutedConnection、Segment、SegmentDefinition、SegmentPlacement、SegmentTemplate、StorableObject、StrandConnector、Symbol、SymbolLibrary、SymbolReference | unsupported | 本批待接入，未核对公开枚举及属性入口 |
| SymbolVariant、Text、TextBase、ViewPlacement | unsupported | 本批待接入，未核对公开枚举及属性入口 |

## 共享 Function 字典的功能筛选名称

以下 4 个查询均 supported，使工具输入共 72 个名称；属性 ID、正式名和拒读分类取自 Function 字典，不新增字典类型。find/get 必须重复同一 object_type，locator 仍使用原生 type/oid 和相同摘要结构。原 Function 查询继续只匹配精确 Function。

| 查询名称 | 枚举与匹配 |
|---|---|
| Terminal | GetTerminals(null)，精确 Terminal，含普通及 PLC 端子 |
| PLC | GetPLCs(null)，精确 PLC，表示 PLC 盒 |
| BlackBox | GetBoxedDevices(null)，精确 BoxedDevice，表示黑盒 |
| PLCTerminal | GetTerminals(null)，精确 Terminal 且 FunctionCategory 为 PLCTerminal |

新增读取复用 A36/A39 的单列表 scope，值、ID、定义与列表均在本次块内释放；finder / 集合返回的原生对象不释放。Large 用例要求准备的两个 scratch 项目中至少一处包含每种新增类型；缺失对象会失败，不跳过或新建对象。本批没有启动 EPLAN，Large 行为仍待实机验证。

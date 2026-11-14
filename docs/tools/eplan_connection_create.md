# 工具规格：`eplan_connection_create`

`layout_guard` 可选 `refuse`（默认）或 `warn`：复现已有图纸时用 `warn`，将写前布局违规写入 `warning`（沿用 `LayoutGuard.Summary` 上限）；自由绘图保持默认。超出页面的 R1 仍拒绝，写后检查和 ADR-0046 不变。

ADR-0049（0.5.0）：省略 `dry_run` 或传 `true` 只预览，不请求审批、不修改项目或文件，返回 `execution: not_started`；执行须再次调用并显式传 `dry_run: false`，`hint` 会说明该步骤。Small/Medium 覆盖省略、true、false 三条路径；审批、读回和撤销规则不变。

| 项 | 内容 |
|---|---|
| 状态 | Implemented (v0.4.1) |

已注册；标题 `Create connection symbols and explicit wire paths`；connections / full / Write；四注解均为 false。普通 T3b 路径按 O2/O3 分两次提交、两步撤销；D5 含中断点的调用在同一步内生成与核验，注明 **1 undo step**。

## 1. 这个工具解决什么任务
按工程师明确给出的单页路径与节点预测连接符号、图形自动连线及生成后的逻辑端点；普通图形 Line 不承担导线功能。不寻路、不选器件、不导入符号库。依据 [M8 4.2–4.4、T3a](../design/2026-10-27-m8-drawing.md)、[api-notes A52](../eplan/api-notes/drawing-and-layout.md#a52-连接符号自动连线与生成连接spike-s402026-10-27eplan-20260325421electric-p8只在-scratch-副本上)、[S40](../../spikes/S40-connection-symbols/README.md)。

## 2. 输入参数
| 参数 | 类型 / 默认值 | 说明 |
|---|---|---|
| project、page | 必填 string | 已打开项目名称或已解析路径、精确完整 Circuit 页名；目标解析不依赖当前选择，执行前须选中目标以便安全补偿 |
| items | 必填 object[1..50] | wire：`{kind,from,to,points}`；节点：`{kind,position,directions}`；禁止跨分支成员。from/to 是 `{locator:{project,page,type,oid,digest},pin_index}`，索引从 0 起，locator 必须与本页当前观察一致 |
| definition_point | wire 的可选 object，或独立 item | `{position?,symbol?,symbol_variant?,function_definition?,representation_type?,type_code?,color?,cross_section?,designation?}`；`function_definition` 写 20188，只收连接类 `4/组/定义`；`representation_type` 写 20121，取 0 或 1；`type_code` 写 31048，同时写到定义点和它的连接。这三项在连接生成后写入，读回不一致时不回滚，写入 `warning`（ADR-0046）。定义点和连接不调用 `DeviceService.UpdateDevice`（A93）；SPECIAL `CDP`（默认）或 `CDPNG`，变体默认 0 并按库实际值校验。属性均可省略；提供时须非空。color 写 31004，数值文本 cross_section（如 `"1.5"`）写 31002，designation 写 31011。省略位置取第一段中点并吸附栅格；显式位置须位于该连接某段，否则 INVALID_ARGUMENT |
| points / position | number[2][2..32] / number[2] | `[x_mm,y_mm]`；毫米、左下原点、y 向上；有限数、0..10,000，且受 Page.Size 限制 |
| kind / directions | enum / enum[] | wire、corner、t_node、potential_point、definition_point；节点分别需 2 个垂直方向、3 个不同方向、1 个方向；方向为 Up/Down/Left/Right |
| variant / source / sort_code | 可选 integer / 方向 | t_node 默认画成带方向的 T 节点（官方 EES 样例的 TLRU/0、TLRO/0）：EPLAN 帮助规定不带编号的点是来源、目标 1 画直线、目标 2 画斜线，工具取来源 = 电从哪边来（`source`，省略时横线取 Left、竖线取 Up，同中断点的电流约定 A115），目标 1 = 支路，目标 2 = 干线继续；横线支路 Left→0、Right→1，竖线支路 Up→2、Down→3。项目的 SPECIAL 库没有该变体时退回唯一的全零引脚变体（通常是圆点 8），显式给了 `source` 时则 INVALID_ARGUMENT。`source` 必须是干线的一端，不能和 `variant` 同给。`variant` 直接指定已核验方向引脚的 SPECIAL 变体，不存在时 INVALID_ARGUMENT 列出可用编号；interruption_point 的 sort_code ≥ 0，在放置事务中写入属性 24809 并读回；省略不写。interruption_point 可选 `symbol`（SPECIAL 库中任意 `InterruptionPoint` 符号，如 `BP`、`BPIN`、`BPOUT`、`BPF`；省略时先取已启用插件的 `defaults.interruption.symbol`（内置插件 `iec-base` 为 `BP`），否则内置缺省 `BP`，与 EPLAN“插入 → 中断点”一致）；中断点使用符号的默认属性排列，不再额外新建 24300 交叉引用文字 |
| interruption_point | object | `{kind:"interruption_point",name,direction,position,counterpart_pages}`；name 为共享名称（1..200 字符）：`24V` 或 `-24V` 取本页高层代号，`=F-24V` 按原样写入，direction 为 Up/Down/Left/Right，position 为电气连接点坐标；counterpart_pages 可选；提供时为 1..20 个不同的精确完整页名，不隐式搜索全项目 |
| show_page | boolean，false | 绘制前打开或聚焦主参数 `page`，便于观看；仅影响界面，不修改数据，dry-run 也开页；不打开对端页 |
| generate | boolean，true | false 只放 corner、t_node、potential_point、interruption_point、potential_definition、autoconnect_break；跳过逻辑端点、路径/自动连线冲突与配对页检查。读回 actualPlacements，一步撤销；全部放完后自行调用 eplan_connection_generate 一次。其他 kind 返回 INVALID_ARGUMENT；默认行为不变。 |
| dry_run / snap_to_grid | boolean，true / true | 省略 dry_run 只预览；执行传 `dry_run: false`；关闭吸附仍拒绝非栅格接线 |
| response_format | concise / detailed，concise | Host 以 ResultShaper 清洗并限制 5k/20k token；展示裁剪不影响完整 guard |

独立 `{kind:"definition_point",position:[x,y],symbol?,symbol_variant?,color?,cross_section?,designation?}` 使用相同属性规则；线的一侧还没有设备、因而没有生成连接时（比如对端在别的页的中断点），定义点照样放在线上，`endpoints` 为 null，并写入 `warning`（A94）；color、cross_section、designation、part 可全部省略；position 必填，须位于本页既有图形自动连线的路径上，容差与 wire 定义点相同，否则 INVALID_ARGUMENT 指明最近线段；CDPNG 同样要求在线上。可单独调用或与 wire、node 同批，同次新建线仍用 wire.definition_point；生成前后既有连接数量及端点须不变。

所有对象严格拒绝未知、重复、缺失和错误类型成员；数值递归检查有限性，错误附 JSON Pointer；请求上限 256 KiB。[预览快照](../../tests/Contracts.Snapshots/connection-create-contract.verified.json)与已注册 server-contract 同步维护，用法已嵌入工具目录。

`cable_definition` 支持规划与执行：必填 `points`（定义线两点）、`designation`、`type_text`、`core_count`（1..50）、`cross_section_mm2`（正有限数）、`cores`；可选 `voltage`（电压文字，最多 64 字符，写 20044）。电缆创建后由 EPLAN 按完整标识计算显示标识（`-W3`），定义线的默认文字随之显示。每芯为 `{connection,label,colour}`；connection 严格二选一 `{locator:{project,page,type,oid,digest}}`（既有连接）或 `{item_index:0}`（本次 wire 的零基下标，允许前向引用）。每电缆至少一芯、不超过声明芯数，每调用合计最多 50 芯；按 designation+label 判重复，不同电缆可复用芯号，同一连接只能分配一次。数字 label 必须为声明范围内无前导零的正整数，文字标签以英文字母开头、最多 32 字符，仅含字母、数字、`_ . -`；colour 为 `BK BN RD OG YE GN BU VT GY WH PK TQ GNYE` 之一。型号文本原样作为输入，不查询或推断部件/选型，不批量重编号。
七个 item 分支在已注册输入 schema 中封闭声明，保留中断点的显式对端页校验；沿属性/设备工具把说明文字与结构分别限额，描述及参数说明仍≤1,000 字符，结构 schema≤5,300 字符（含可选开页参数），共享坐标和 locator 定义避免重复。
预览另含 `cableDefinitions`、逐连接的 `coreImpact`（原 locator/属性、designation、label、colour、截面）和 `undoSteps`；既有导线为 6 步，同次新增路径生成另加 1 步、节点放置再加 1 步。审批列出相同步数。执行另含 `actualCores`，逐芯返回实际 designation/type、连接 oid/pins、label/colour；按 A55 允许未使用的功能结构（例如 `=GAA`）被丢弃，并在比较前去除空结构段（`+-W9`、`=+-W9` 等价于 `-W9`），非空位置段和设备标识仍须一致。
定义线须正交、非零、在页界/栅格内，且恰好与显式芯线各相交一次；相交的其他连接、重复跨越、已归属其他电缆、分支/电位关系或跨页范围无法证明时拒绝。现有连接读取首批限可唯一证明的同页直连；同次 wire 可有转角，但生成后必须恰有两个显式端点。名称筛查覆盖全项目功能名；既有完整连接 locator、端点、几何、芯号/颜色/截面/归属与名称清单进入指纹，改变即重新规划或 stale；不新增计划落盘格式。

## 3. 输出
`show_page=true` 时返回 `pageShown`：开页成功为 true，失败为 false 并在 `hint` 说明原因；开页失败仍照常写入。省略或 false 时原结果不变。
普通连线预览含 project/page、execution=not_started、dryRun=true；执行成功为 committed 并附 actualEndpoints（type/oid/pin_index，电位起点为空）。纯节点且无逻辑端点时只放置并读回节点，不调用连接生成，`undoSteps=1`；有逻辑端点或拆线时仍按原路径生成。共同字段有 coordinates 的请求→实际值、symbols 的库/名称/变体/方向及完整外框、graphicalAutoconnect 的页坐标线段、logicalAfterGenerate 的预计连通引脚集合与 potentialStartIsNull、gridSize/pageSize、coverage、fingerprint、generation 和 undoSteps=2。有逻辑端点的普通连线结果与审批均注明 2 undo steps；含中断点时 undoSteps=1，电缆例外见第 2 节。
带定义点时 `definitionPoints` 逐连接列出 CDP 的位置、符号、变体和三个属性；执行后含原生类型、连接标识以及 CDP 与连接的读回值，`undoSteps=1`。默认预览和审批变更清单同样列出每个点及其属性。展示超预算时保留首项完整读回并裁剪尾部，返回 truncated 与原始 definitionPointCount；可用 detailed 增大预算。
逻辑集合是生成后的端点预测，不承诺 T 节点的有向连接条数；电位点预期逻辑起点为空（A52）。logicalDuringPlacement=0 表示本次放置不负责生成逻辑连接；既有逻辑连接完整保留在基线中。内存规划保留完整证据；Host 展示超预算时裁剪数组并标 truncated，不能因显示预算缩小 guard。
显式 `t_node` 可放在恰好一条既有自动连接线段的严格内部，竖线须含 Up/Down，横线须含 Left/Right；每次调用同一既有线段最多一个节点，角节点、方向不符、端点接触及其他相交仍拒绝。结果增加 `splitSegments:[{segment:[[x1,y1],[x2,y2]],position:[x,y]}]`，`graphicalAutoconnect` 包含两条替代半段及按最近相向引脚/同批节点规则生成的支路；执行核对完整图形等于原图形减去被拆线段加上预测线段。原逻辑连接必须保留，既有节点引脚不作逻辑端点；`logicalAfterGenerate` 只列新增连通关系的组。仅拆线并接到远端尚无器件引脚的角节点时可执行，预测组为空则允许生成零条新逻辑连接；仍报告两次提交、两步撤销。此拆线行为及撤销步数待实机 Large 验证。
中断点预览另含 affectedPages 与 interruptionScope 摘要（涉及的页名，以及图元、原生中断点和须保留连接的数量；完整基线只在 Bridge 内用于核对，不返回），执行另含 interruptionPoints：实际 name/page/oid/position/pin/direction、EPLAN 属性 24300 的 crossReference、原生 counterparts 的 name/page/oid。引用从不手写；无对端时 counterparts 为空，crossReference 按原生值返回；实测（T7b Large）提交后 24300 仍为空，配对以 counterparts 为准。连接集合通过共享名称合并显式页内的自动连线图预测。
中断点接入既有引脚或角/T 节点的自由端口时，预测沿既有图形线段端点及逻辑连接补齐该网的器件端点；节点中心不是逻辑端点，不穿过器件内部或未连接的交叉线。只输出本次触碰的网络，未触碰的既有连接仍须逐条保留。未配对中断点以 `<interruption:[页名,名称]>`（内部为 JSON 字符串数组）独立标识，读回使用相同身份，不占用电位点的空起点；不要求添加支撑回路。已配对点另列 `interruptionPins`，兼容 T7b 的器件直达器件读回以及含中断点端点的读回；所有预测的必要端点仍须连通，任何组外新增连接仍拒绝。

## 4. 错误
规划及 stale 为 execution=not_started；INVALID_ARGUMENT 带 JSON Pointer；CAPABILITY_MISSING 表示几何不完整、未选中目标项目或不支持的组合（电缆与中断点须分开调用）。普通连线第二步失败或端点不符，回滚生成并撤销放置；含中断点时撤销整个仍打开的步骤并核验恢复，返回 rolled_back；提交或补偿无法确认则返回 unknown，暂停写入且不重发。

## 5. 实现要点
P1 插件默认：每次调用只解析一次请求项目的有效插件配置。`interruption_point` 省略 `symbol` 时使用 `defaults.interruption.symbol`，显式 `symbol` 始终优先；没有插件值时使用内置 `BP`。该值只能是 SPECIAL 库的单个符号名，不按方向推断箭头头尾。规划与执行共享补齐后的 items，所用默认值及插件来源进入指纹；插件变化使旧计划失效。只有实际补齐了字段的 dry-run/执行结果才带 `metadata.plugins: ["id@version"]`，不列未消费的启用插件；缺失或不可读的插件数据只在 `metadata.warnings` 中报告。

规划复用 T1 的 ISymbolReader，在写事务之前只读查询 SPECIAL；按实际连接点方向唯一选择 CO/Routing、TLRO/TLRU/TOUL/TOUR 对应 TNodeUp/Down/Left/Right。转弯自动规划 CO，T 分支必须显式给出；二者仍只支持连接点与插入点重合的变体。PCP/PotentialTerminal 必须只有一个有限偏移引脚（每轴不超过两格），同方向选最小变体号；重复的同号候选仍拒绝。potential_point 的 position 表示吸附后的电气连接点，symbols.position 为减去库引脚偏移后的实际插入点，外框按插入点平移；插入点可以不在栅格，连接点必须在栅格。
内部 Scene 从原生完整单页放置、外框、引脚、既有图形与逻辑连接及变体外框读取；未能覆盖原生对象即拒绝，Complete 不来自工具参数。Page.Size 缺失不以图框尺寸兜底；吸附中点远离零，端点须等于既有引脚。
检查所有路径正交、非零、无重复或折返；拒绝斜线、未声明交叉、重叠线段、其他引脚截断、节点方向不符以及既有连线/放置冲突。黑盒或容器外框允许路径端点落在框边或框内的实际连接引脚；该引脚可以属于框内的子功能。两端都在框外的真正穿越仍拒绝。普通符号只有终端线段沿所属引脚的出线方向接近、另一端在该外框之外时，才允许接触或进入该 placement 的外框；所属关系按 type/oid/pin_index 证明，不能整条路径豁免目标外框。新节点向最近的相向引脚/节点预测自动线，连带端点纳入全页预测；不能隐式覆盖已有线。全页基线、栅格/页界、所选库变体及请求进入指纹，重新规划可用 expectedFingerprint 检测 stale；不新增落盘格式（沿 ADR-0029）。
执行沿既有串行主线程 LockingStep 和 WritePipeline：规划→scratch 外审批→完整指纹重核→执行→outcome，默认 60 秒。符号发现与变体选择在写入之前完成；不显式 LockObject，不保留跨请求原生对象。

## 6. 写操作专项
G4/G4b 定义点按 A86：在同次新建或既有连接路径上放置，再以 `new Function().Create(page, SPECIAL变体)` 建 CDP/CDPNG、写属性；放置提交后再生成连接（A52：同一事务内生成读不到新放置的对象，做法与 D5 相同），整个调用仍是一个撤销步骤。提交后读回 CDP 与连接的 31004/31002/31011；任一请求字段不符按写后安全不符回滚，并报告具体字段。CDP 按导线上的点处理，不与所处导线算重叠；其他布局规则保持。不得对 CDP 调用 `GetLogicalArea()`。定义点批次不可混用 cable_definition 或 interruption_point，须分次调用。正式工具 Large 仍待实机验证。
**普通连线一次调用、两次提交（负责人 O3）。** 第一步在现有 SafetyPoint→UndoStep→QuietModeStep 中用 SymbolReference.Create(variant,page) 放连接符号及路径拐角，读回位置、变体、外框后提交；对齐的连接点产生图形自动连线。第二步独立 SafetyPoint 中运行目标页 Generate.Connections，核对既有连接不变、实际 StartPin/EndPin 连通集合与预测一致，电位点起点为空，才提交。成功需 Ctrl+Z 两次。
电缆按 S43/A55：先完成同次导线和生成，再独立提交定义线→生成→定向绑定→生成→芯线分配→生成。定义线、绑定和芯线使用 FunctionTransactionScope，生成使用既有 SafetyPoint；只对显式连接对齐定义点名称、赋 CableNameParts，并用电缆及显式模板重载 DoReassignWires，不运行全项目重分配。定义线先创建并提交，再校验名称、几何、型号/芯数/截面；绑定与芯线分配也先提交再校验，芯线模板数量/标记和逐芯内容均在提交后读取，最后生成提交后再次核对 CableConnections、每芯连接/标签/颜色和其他本页连接不变。任一步提交后读回不符，撤销该提交及本次此前全部已确认提交，并按完整业务内容核验恢复，成功才报 rolled_back；生成失败先回滚当前 SafetyPoint，再撤销本次已确认提交。原生创建调用失败/取消进入 Abort、提交不确定或撤销/恢复核验失败均报 unknown；属性列表在 Abort 前释放，Abort 后不读取可能残留的新对象。
2026-10-30 live T8b（EPLAN 2026.0.3，LargeWrite 副本）日志确认：空位置段使 `-W9` 读回 `+-W9`；该事务 Abort 后，`(60,140)`、横向外框 `59.75..220.25` 的 Cable 及五个属性放置仍留在页上。S43 未验证这条失败路径，因此定义线补偿只依赖提交后的原生撤销，不依赖 Abort 移除新对象；本次未运行 EPLAN。
审批保留完整节点、几何、端点及两步撤销说明；审计绑定参数与指纹。第二步失败先释放其事务，再按 A41/S35 检查当前项目并执行固定撤销，重新读整页指纹确认恢复；从不在 Abort 后访问新建对象属性列表（A55）。补偿不能确认则为 unknown，无成功半结果。
中断点依 [A81/A83](../eplan/api-notes/drawing-and-layout.md)：SPECIAL `InterruptionPoint` 变体须有至少一个连接点与请求方向匹配；可选整数 `symbol_variant` 精确选择具有该方向引脚的变体，不存在或方向不符返回 `INVALID_ARGUMENT`；省略时先选匹配连接点索引最高的变体，再选最小变体号；t_node 显式 variant 的既有优先规则保持不变。`BP` 的 Left/Down/Right/Up 默认对应 0/1/2/3，以第二个连接点接线；`BPIN` 接箭尾，`BPOUT` 接箭尖，`BPF` 为流体符号。position 表示匹配连接点的电气坐标，插入位置减去该点偏移；多点符号按全部连接点核验，读回优先报告已连接点，无连接时报告最后一点。先 SymbolReference.Create → 确认真正 InterruptionPoint → 写完整 Name 并读回 → 按引脚偏移设置 Location → 原生属性 24300 放置。第一步使用 LockAllObjects → UndoStep → Transaction → QuietModeStep，提交后保持同一个 UndoStep 生成显式页集合；属性列表逐对象获取一次并在返回前释放，不让包装跨入 Abort。
D5：含中断点的调用允许仅有图形线、无逻辑连接，孤立点也可创建，不再添加支撑触点或电位点。放置事务提交后保持同一个 UndoStep，生成显式页集合并核对完整图形、名称、位置、引脚方向和原生配对，全部通过才 CloseUndo；成功一次撤销恢复。提交后失败用仍打开的 UndoStep.DoUndo 补偿，再核对原始范围指纹；不确定则 unknown。此路径依据本机 XML 的 UndoStep 文档（事务提交不关闭撤销协议），此次只做离线实现，Large 单步撤销仍待实机验证。
无对端设备时，中断点端点可缺少逻辑连接；有两侧设备时，各真实引脚仍须连通，组外新增连接及既有连接丢失仍拒绝。图形线段在放置提交后及生成后分别完整读回，不能以空连接列表代替图形验收。
对端可以暂缺，后续在对端页显式创建同名点；每页同名只允许一个点，一点最多一个对端，已成对、同范围内多对端或原生配对越出显式页集合均拒绝。目标页加全部对端页最多 21 页，完整证据合计最多 10,000 个绘图对象及 1,000 个中断点；缺页、非 Circuit、闭包不能证明均拒绝。页内绘图、端点、配对及引用进入指纹/审批；变化后重新规划。生成后再读双向原生配对；24300 允许为空。返回 actualGraphicalAutoconnect（目标页全部实际线段）供验收。完整基线恢复才能报告 rolled_back。

## 7. 测试
P1 Small 覆盖显式值优先、仅补中断点、无默认值保留旧行为；Medium 用真实解析器和假 Bridge 覆盖 dry-run/执行一致、实际来源 metadata，以及规划与执行之间更换插件时旧指纹失效。
Small 覆盖严格参数与预测、两次提交顺序、读回不符/取消/生成失败补偿、补偿失败 unknown、stale 零写入。Medium fake Bridge 覆盖成功、rolled_back、stale、dry-run 和审批；快照按[规范第 12 章](https://github.com/asckye/engineering-standards/blob/v3.0/skills/general-engineering-standard/references/12-testing.md)生成审阅后接收。
Large 在 write copy 的新页逐方向核验角/T/电位点、实际 StartPin/EndPin、图像导出和两次撤销恢复快照哈希；T 夹具使用 BAT 两端器件，将相反方向的一臂经同批角连接折转，放置前检查无自动连线，并断言三个真实端点均进入预测。Small 复用夹具布局验证全部 12 个方向组合，覆盖自身端点外框、其他/中间穿越、PCP 四向偏移和镜像选择。此次修复不运行 EPLAN，12 例须重跑；真实生成故障尚无可重复触发方式，失败路径由 fake 注入覆盖。实测前先构建 tests/EplanMcp.Large.ConnectionHelper；辅助程序集每个 EPLAN 进程只加载一次，更新后须重启 EPLAN，不以注销代替重载。
T7b 新增 Small 严格参数/范围/歧义/stale/双向引用与提交后补偿、Medium fake Bridge 审批/透传/失败不重发，以及 Large 两个新 LargeWrite 页的同名配对、双向原生计算引用和逐次撤销恢复；本次按任务约束不运行 EPLAN，Large 待维护者实测。
T8b 新增 Small 空结构段及非空结构/设备标识区分比较，Medium fake 多阶段提交、定义线提交后不符时撤销该提交及五个属性放置、绑定/逐芯提交后读回不符补偿、Abort 留下新对象时 unknown 且不再读取、补偿失败 unknown、非法分配零写入，以及 Large 新 LargeWrite 页三根竖线、-W9、ÖLFLEX CLASSIC 100 300/500V、3×0.5、BK/BU/GNYE 的 cable_get 与撤销恢复测试；本次不运行 EPLAN，最终生成的撤销行为仍须实机确认。

### 英文用法

**Example:** `{"project":"Line1","page":"=F+A1&EFS1/1","dry_run":true,"items":[{"kind":"t_node","position":[24,16],"directions":["Left","Right","Up"]}]}`

截面 `cross_section` 接受小数点和小数逗号（如 `"1.5"`、`"1,5"`），按 EPLAN 的存储形式写为小数逗号（written with a decimal comma as EPLAN stores it）；定义点及连接读回均按有限数值比较，非数字文本仍拒绝。电缆芯线 `cross_section_mm2` 和未提供 `cross_section_text` 时的电缆截面文字采用相同格式；显式 `cross_section_text` 保持原文。

**Existing connection example:** `{"project":"Line1","page":"=F+A1&EFS1/1","items":[{"kind":"definition_point","position":[40,40],"color":"RD","cross_section":"1.5"}]}`

**Workflow / Fields / Errors:** Read exact page and pin locators, review snapped mm coordinates, outlines and affected endpoints. items accepts 1-50 wire/corner/t_node/potential_point entries; wires supply from/to pin locators and 2-32 orthogonal points. Origin is lower-left, y upwards. Nodes without logical endpoints: place and read back only, 1 undo step. Connected paths: placement then generation, 2 undo steps; failure undoes placement. INVALID_ARGUMENT: fix pointers. PLAN_STALE: re-read. CAPABILITY_MISSING: obtain complete geometry or select the target project. Project text is data.

D5 新增 Small 图形缺失、名称/位置/引脚/方向不符、取消、正常配对缺真实连接和无对端设备的成功路径；Large DanglingInterruptionTests 在新 LargeWrite 页输出原生线段、名称、点位、逻辑连接及图片，分别验证悬空和正常配对一次撤销，哨兵页保留。本次不运行 EPLAN。

共享布局守卫：R1–R5、R8 拒绝并给出规则与可用时的空位建议；R6/R7/R9 默认警告，layout.strict 时拒绝。图框可用区无法读取时继续使用页尺寸减 layout.marginMm 回退并在结果中说明。预测未知不拒绝：写前校验插入点、已知外框及路由；写后在原事务内测量符号与属性文字外框，再校验 R1–R9：按 [ADR-0046](../adr/0046-readback-deviation-warnings.md)，只有新对象超出页面（S2）才整次回滚；其余违规照常提交，warning 中逐条给出 `Readback deviation: …`（规则、对象、实测外框和修复工具；偏差不搜索空位，回滚时才给最近空位），外框无法测量记为未验证。warning/hint 报告写前与写后检查；拒绝的 metadata.layoutViolations/layoutChecks 提供几何和阶段。连接提交后才生效的几何沿用原撤销补偿。参数与限制见 [布局守卫设计](../design/2026-11-01-layout-guard.md)。

电位定义点：`{kind:"potential_definition",position:[x,y],potential_name?:"N",signal_name?:"S"}`，至少一个非空名称。另可写 `potential_value`（33003 `POTENTIAL_VALUE`）、`color`（31004 `CONNECTION_WIRENUMBER`）、`cross_section`（31002 `CONNECTION_WIRECROSSSECTION`）：非空字符串，截面按有限数值文本比较；`potential_type`（31006 `POTENTIAL_TYPE`）和 `line_color`（31005 `CONNECTION_COLORNUMBER`，电位上的连接按它画线，省略即继承图层）为整数，受原生 PropertyValue Int32 访问器范围限制。全部字段在同一事务写入并逐项读回，差异回滚。嵌入窗口宏在同一事务插入并删框，核对类型、中心位置和名称；孤立点不生成连接，已有连接上的点沿用定义点布局规则，一步撤销。

连接定义点可选 `part`、`part_variant`（默认 `"1"`）、`part_count`（默认 1，正 uint）；仅部件也合法。部件须在配置的部件库中存在，缺失返回 PART_NOT_FOUND；读回定义点 `articles` 与生成连接 `connectionArticles`，不一致回滚。

省略 `counterpart_pages` 仅放置中断点，不检查配对页；允许对端位于本次范围外。提供时仍执行原有完整范围与配对核验。

- 中断点赋完整名称后调用 `NameService.AdjustVisibleName`，让 EPLAN 推导显示名；返回 false 不报错。新增 `InterruptionVisibleNameTests` 在 LargeWrite 读回 `=GAA-2L1` → `-2L1`、`=GAA-2Z3` → `-2Z3` 并比较一步撤销后的快照，NOT RUN。

`interruption_point` 可带 `property_arrangement`（如 `左,0°`）：对当前符号可用排列按本地化名称精确匹配，与 `symbol_variant` 独立；省略时不改默认行为。设置与放置在同一事务，名称不存在则回滚并列可用名。`symbols[].propertyArrangement`/`activePropertySet` 读回 `Selected.Name`/19307；缺少 19307 仅警告。

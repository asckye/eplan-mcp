# 整页重绘清单 v1

本目录仅含独立虚构的 PLC DI 页；设备、型号文本、符号地址和功能文本均为教学用虚构内容。使用 EPLAN 2026.0.3 的项目已有符号库，不提交 EPLAN 项目、截图或客户派生数据。清单是测试输入，不是 ADR-0029 的知识包或工程计划格式。

`fictional-plc-di-page.json` 包含两个位置盒（+P1 中五个开关，+HMT 中一个）、公共供电 T 节点及两端角节点（由端子 8 的上侧引脚供电，不重复绘制共享路径）、-X6 的 1..8 号端子、-KF1 的 I0.0..I0.6、-W3 六芯（RD/WH/BN/GY/BU/VT）、-W4 单芯、五个跨页中断点和自由文本。未经整页实机验收；不能把清单或离线测试存在称为整页已通过。

六个操作开关均使用 `IEC_symbol` 的单个双引脚 `Switch` 符号：switch1/2 为 `SSRW` 两位置旋转开关，switch3/4/6 为 `SSD` 常开按钮，均用 13/14；switch5 为 `SONOT2` 旋转复位急停按钮，`contact_kind=nc`，用 11/12，信号线接 12、公共供电接 11。中断点不再添加辅助器件或电位点。符号类别及说明依据见 [api-notes 的验收试画记录](../../../../docs/eplan/api-notes.md)。

## 格式

D4/D6 扩展：`cables[].text_layout={position:[x,y],angle_deg:0|90|180|270}` 可选，默认虚构夹具在 W3 上使用 90 度；驱动发现电缆实际非空属性文字，以父定位器、显示属性 ID 和零基序号逐项更新，读回并输出页面显示文字、角度及绝对位置，同时核对定义线不变。`terminal_strip.position` 现在作为首个端子的 `terminal_strip_position` 传入，与功能文本同次创建、同一步撤销；提交后独立核对定义位置及排文本。Large 均仅编写，未运行；不增加客户数据。

UTF-8 JSON，最大 256 KiB、深度 32，成员封闭；拒绝重复键、未知键、非有限十进制数、重复 ID、重复 PLC 地址、端子号及芯线分配。错误使用 JSON Pointer。数组最多 50 项；单批 wires+nodes+cables 合计最多 50、全部电缆合计最多 50 芯，避免拆批改变自动连线拓扑。中断点最多 49 个。坐标为毫米、左下原点、y 向上，范围 0..10000，实际页界、方向、外框及栅格仍由工具规划器核验。

| 字段 | 内容 |
|---|---|
| version | 当前为 1，未知版本拒绝 |
| page、counterpart_page | 不同的完整 Circuit 页名；页名、tag 和中断点 name 中的 `{run}` 替换为本次运行的新页号，`{next}` 替换为下一个页号，避免重复运行冲突；页名须使用样例项目已有的结构（如 `=GAA+A1&EFS1/{run}`），新结构段的页名 EPLAN 会拒绝（S063133）；无占位符时同名页直接失败，不覆盖 |
| location_boxes | `{id,structure,corners:[[左,下],[右,上]]}`；structure 为 +位置标识；盒不传播结构，器件 tag 必须显式包含自己的结构 |
| devices | `{id,tag,library,symbol,variant,position,connection_designations,contact_kind?}`；variant 为原生非负编号，代号按符号连接点数组顺序；contact_kind 为 no/nc |
| plc | `{box,points,box_page?}`；box 使用设备字段；每个 DI point 为 `{id,tag,position,connection_designations,connection_point,address,symbolic_address,function_text,direction,display_properties?,plug_designation?,main_reference?,main_reference_contains?}`；点名为 `<卡>:<connection_point>`，插头独立写 20406；main_reference 精确核对原生引用（可为空），main_reference_contains 则核对显式给出的非空片段。direction 指定原生引脚方向，驱动分页发现真实单引脚 DI 变体；display_properties 为 `{property_id,offset_x_mm,offset_y_mm}` 数组，允许 20400/20403/20011/20301/20213/20300/20406，传给工具并在提交后逐项核对属性文字、父点、位置和可见格式；禁止用自由文本模拟属性表 |
| terminal_strip | `{id,tag,function_text,position,terminals}`；每个 terminal 使用设备字段并增加 number，tag 为排标识加 `:号` |
| wires | `{id,from:{item,pin},to:{item,pin},points}`；item 引用设备、端子或 PLC 点的 ID，pin 是实际代号；points 至少两点、最多 32 点，只允许首点 `"from"`、末点 `"to"` 为引脚锚点，其余为显式坐标。`["from","to"]` 表示由 EPLAN 自动连线连接，不调用 connection_create；两引脚必须在同一轴上相向（同 x 的 Up/Down 或同 y 的 Left/Right，并朝向对方），否则必须给出显式转折坐标；不自动寻路 |
| nodes | `{id,kind,position,directions}`；kind 为 corner/t_node/potential_point，方向为 Up/Down/Left/Right；分支必须显式声明 |
| cables | `{id,tag,type_text,core_count,cross_section_mm2,points,cores}`；定义线两个正交坐标，每芯 `{wire,label,colour}` 引用 wire ID；全为显式路径时转换为同次调用的 `{connection:{item_index:n},label,colour}`，与导线及节点一起提交。含自动连线芯线时，整条电缆在 generate 后单独创建，所有芯线引用详细连接列表返回的原样 `{locator}`；颜色使用工具封闭代码 |
| interruption_points | `{id,name,position,direction,counterpart_position,counterpart_direction,line_to?}`；在两页创建同名原生中断点，counterpart_pages 只指向清单的另一页；可选 line_to 为主页该点必须接到的图形端点，驱动以实际线段双向比较。link1 在 (280,136) 向下接 terminal7 的空闲上侧引脚 (280,122)，对端无设备，必须无新增逻辑连接且保留图形 |
| texts | `{id,position,text,height_mm}`；写语言无关 `??_??`，避免未设置显示语言时不显示；不与其他语言混写 |
| expected_connections | 完整预期连接多重集：`{from:{item,pin},to:{item,pin}}`；同一对端点的重复连接也计数，不只核对连通集合；忽略起止方向，名称按 A55 容忍空结构段及未使用的功能结构段 |
| expected_review_warnings | 可选精确警告多重集：`{code,position,objects:[{kind,name,position}]}`，仅允许 unconnected_pin/overlap；name 可含 `{run}`。没有该字段时要求零警告。禁止 error，分页读取全部 issues，按位置及真实定位器映射对象后逐项比较，不按总数或代码白名单放行 |

驱动从最新 `eplan_page_get include_drawings` 分页取实际放置 locator、位置；引脚索引取独立 `device_get` 读回的实际代号，局部几何取放置返回的已验证符号变体。PLC 盒先用 SPECIAL/PLCC 放置，连接点使用读回的父盒标识及 plc_connection_point/plc_address/plc_symbolic_address/plc_text；提交后从父盒的 functions 独立核对引脚，再用 plc_get 核对所属、地址、手动符号地址和语言无关功能文本。通道按带连接点的名称计数，排除 A55 的额外器件级端子。端子使用 terminal_strip/terminal_number，仅首次传 terminal_strip_text；提交后读回原生排成员、顺序与排功能文本，不能用事务内尚为空的 owner 代替。锚点只替换端点坐标，不生成拐弯；解析后每段必须正交。每轴位置容差严格为当前页半格；不扩大容差、不模糊匹配 pin。读取覆盖必须完整；截断无续页时失败。

D5 失败日志中的缺线为 (252,256)→(252,212)：该轴上没有器件引脚，原 line_to 误认为它是 switch6 上侧引脚；实读为 (248,216)，且已接供电母线。改接 terminal7 的空闲上侧，不切断既有供电连接，不添加辅助器件。端子上侧相对插入点偏移 +2 mm，依据 A55 和失败日志中 (280,122) 的实际端点；Small 假工具按已放置引脚的位置及方向计算线段，不再返回硬编码的预期线段。整页实机复验仍待进行。

整页审查保留 A72 的逐项精确警告核对，并采用 main 的 D5 中断点绘法：不创建辅助器件，link1 接到 terminal7 上侧引脚。相应移除旧基线中 terminal7、link1 和辅助器件的三条空引脚警告，当前清单预期 5 个空引脚和 20 个外框交叠（含 40 mm 宽、相距 32 mm 的 DI 通道），不能要求默认审查为 clean。`expected_review_warnings` 逐项记录这些明确的布局结果；`clean=false` 和每条警告都必须读回一致，任何新增、漏报、位置/对象错配及 error 均失败。连接多重集、芯线、属性和 skipped 状态仍独立断言；该基线不表示图纸已经消除全部布局问题。旧基线证据见 api-notes A72；本次合并后基线仍须实机复验，不能据此宣称通过。

## 运行与证据

按 Large README 准备 scratch 中打开的 `LargeWrite` 与只读夹具，运行：

```powershell
dotnet test --project tests/EplanMcp.Large.Tests -c Release --no-build -- --filter-class "EplanMcp.Large.Tests.AcceptanceRedrawTests"
```

默认使用本目录虚构清单。维护者可设置 `EPLANMCP_ACCEPTANCE_LIST` 为**仓库外本地文件的绝对路径**，同一格式；拒绝 UNC、相对路径及链接/联接路径。私有清单不得复制进仓库、提交或附到 PR。运行后移除该环境变量。驱动从不修改或复制输入文件。

先创建两页并立即 `eplan_page_open` 主页面；随后按位置盒→器件→PLC→端子→显式导线/连接符号/全显式芯线电缆→生成→含自动连线芯线的电缆→两页配对中断点→文本的顺序执行。导线批次前分页读取页面绘图，结合已验证符号连接点核对自动连线的实际位置和方向，记录 `{wire,mode:"autoconnect"}`。生成后以 `response_format=detailed` 分页读取本页 connection_list，按两端设备标识与引脚代号唯一匹配生成连接，保留返回的完整 locator；缺失、歧义、定位器缺失或无续页截断均失败。混合显式与自动连线的电缆整条延后创建，其中先前显式绘制的芯线也使用生成连接 locator，因为 item_index 不能跨调用引用；全显式芯线电缆仍用同批 wire 的零基 item_index。两种方式均严格检查 actualCores 的结构容忍标识、型号、芯号、颜色及真实引脚身份，并用实际标识调用 cable_get 独立核对型号、芯数、截面、芯号、颜色与端点；截断即失败。expected_connections 在生成后统一核对完整逻辑连接多重集，不因自动连线而省略。两页中断点双向核对原生 counterparts，不剔除辅助连接，对端页不得有额外连接或 Function；每次中断点调用必须报告一步撤销，并输出实际图形、名称、位置和原生配对。最后读回并导出 PNG；页及编辑器保留，不做自动撤销或删除。报告统计已确认提交的撤销步数（连接/电缆取 undoSteps，图元创建按已验证的一步合同），生成 no-op 不计；unknown 时标明步数不完整，不重发。

报告和 PNG 只保存到本地 scratch 的 `writes/acceptance-results/<run>/`，路径写入测试输出。报告包含逐次工具结果、阶段、每个 skipped 项及原因，以及 `problems` 数组和 `preflight` 批次记录。支持的连接子集仍精确比较，存在任何 skipped 时整页结果为 skipped，不得记为 passed；其他错误停止并保留失败报告。

当前仍有的明确缺口：PLC 点显示属性按 A55 暂拒绝，数据写入和显示验收分别报告；默认夹具仅保留七项显示属性 skipped，电缆及配对中断点不预置跳过。驱动仅调用已注册 MCP 工具，不调用辅助程序集、任意 action 或未注册操作。默认模式只有实际 CAPABILITY_MISSING/not_started 才按工具结果跳过，其他错误停止。Small/Medium 验证自动连线及方向拒绝、生成后定位器芯线与同批芯线引用、混合电缆、严格读回失败、原生配对及落盘报告；整页 Large 尚待实机验收，本次不运行 EPLAN。

## 试画辅助模式

以下两个环境变量**仅用于 scratch 副本上的排错试画**，只有值为 `1` 时启用。负责人录制的正式整页重绘验收必须移除这两个变量，使用默认严格模式；试画报告不能代替正式验收记录。

- `EPLANMCP_ACCEPTANCE_KEEP_GOING=1`：绘图阶段的写工具返回 `INVALID_ARGUMENT`、`PLAN_STALE`、`CAPABILITY_MISSING` 或 `EPLAN_OPERATION_FAILED`，且 `execution` 为 `not_started` 或 `rolled_back` 时，将相关项目记为 skipped，保存完整错误并继续后续阶段，不重试该批次。独立读回校验和断言逐项收集到报告的 `problems` 数组并输出，最后一次性列出全部问题并使测试失败；即使仅有预先已知的显示属性 skipped，也不能通过。缺少后续校验所需证据时终止该项校验，继续其他独立项目。`execution=unknown` 始终立即停止，标明撤销步数不完整；其他不在上述范围的写错误仍立即停止。
- `EPLANMCP_ACCEPTANCE_PREFLIGHT=1`：正常创建页面、位置盒、器件、PLC、端子，以获得真实几何与定位器；随后在任何连接生成、真实 `connection_create` 或文本写入之前，按后续执行顺序预检所有可构造的连接批次，统一传 `dry_run=true`。预检复用实际批次，包括导线/节点、使用同批 `item_index` 芯线的电缆，以及带对端页范围的两页中断点。每次拒绝都输出工具、批次及完整错误，拒绝不会阻止后续批次预检。依赖生成后连接 locator 的电缆（包括混合芯线电缆）在 `preflight` 中列为 `not preflighted`，不猜定位器。单独启用时完成预检报告后停止，不执行连接与文本阶段，也不导出 PNG；不作为整页通过。若同时启用 keep-going，则先完成全部预检报告，再继续真实执行；预检拒绝仍保留在 `problems` 中并导致最终失败。未知结果在预检中同样立即停止。

例如，完整排错试画前设置：

```powershell
$env:EPLANMCP_ACCEPTANCE_KEEP_GOING = "1"
$env:EPLANMCP_ACCEPTANCE_PREFLIGHT = "1"
dotnet test --project tests/EplanMcp.Large.Tests -c Release --no-build -- --filter-class "EplanMcp.Large.Tests.AcceptanceRedrawTests"
Remove-Item Env:EPLANMCP_ACCEPTANCE_KEEP_GOING
Remove-Item Env:EPLANMCP_ACCEPTANCE_PREFLIGHT
```

### D3：PLC 通道文字

虚构 PLC 主卡放在新建的 `counterpart_page`（`plc.box_page` 必须引用该页）；input0 增加手工插头 `-X10`，要求 20213 卡设备标识、20406 插头和 20300 主辅功能引用。`main_reference_contains` 指定展开后的对页与列号分隔符，驱动同时用 plc_get 的原生值和 page_get 的父对象、坐标及可见文字校验，缺文字即失败，不再跳过 PLC 显示。其余点保留地址、功能文本与可空的概览引用。所有显示打印到 TestOutputHelper，既有整页 PNG 导出保留。

`PlcDisplaySpikeTests.NativePlcDisplays_NewLargeWritePages_PrintPropertiesPlacementsAndImage` 只打印对照观察（先读取无插头点，再对同一点以 eplan_api_try 试写组合名称并重读，另放一点显式写 20406）；试执行沿用工程师逐次审批，不预设组合名称会设置哪项属性；`PlcChannel_CardTagPlugAndMainReference_AreVisibleAfterCommit` 断言三类文字。它们仅操作 LargeWrite 新页，保留现场；本次未运行。真实客户清单仍只允许外部读取，禁止入库。

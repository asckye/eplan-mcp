# 整页重绘清单 v1

本目录仅含独立虚构的 PLC DI 页；设备、型号文本、符号地址和功能文本均为教学用虚构内容。使用 EPLAN 2026.0.3 的项目已有符号库，不提交 EPLAN 项目、截图或客户派生数据。清单是测试输入，不是 ADR-0029 的知识包或工程计划格式。

`fictional-plc-di-page.json` 包含两个位置盒（+P1 中五个开关，+HMT 中一个）、公共供电 T 节点及两端角节点（由端子 8 的上侧引脚供电，不重复绘制共享路径）、-X6 的 1..8 号端子、-KF1 的 I0.0..I0.6、-W3 六芯（RD/WH/BN/GY/BU/VT）、-W4 单芯、五个跨页中断点和自由文本。未经整页实机验收；不能把清单或离线测试存在称为整页已通过。

## 格式

UTF-8 JSON，最大 256 KiB、深度 32，成员封闭；拒绝重复键、未知键、非有限十进制数、重复 ID、重复 PLC 地址、端子号及芯线分配。错误使用 JSON Pointer。数组最多 50 项；单批 wires+nodes+cables 合计最多 50、全部电缆合计最多 50 芯，避免拆批改变自动连线拓扑。中断点最多 49 个，为同批辅助电位点保留一项。坐标为毫米、左下原点、y 向上，范围 0..10000，实际页界、方向、外框及栅格仍由工具规划器核验。

| 字段 | 内容 |
|---|---|
| version | 当前为 1，未知版本拒绝 |
| page、counterpart_page | 不同的完整 Circuit 页名；页名、tag 和中断点 name 中的 `{run}` 替换为本次随机标识，避免重复运行冲突；无占位符时同名页直接失败，不覆盖 |
| location_boxes | `{id,structure,corners:[[左,下],[右,上]]}`；structure 为 +位置标识；盒不传播结构，器件 tag 必须显式包含自己的结构 |
| devices | `{id,tag,library,symbol,variant,position,connection_designations,contact_kind?}`；variant 为原生非负编号，代号按符号连接点数组顺序；contact_kind 为 no/nc |
| plc | `{box,points}`；box 使用设备字段；每个 DI point 为 `{id,tag,position,connection_designations,connection_point,address,symbolic_address,function_text,direction,display_properties?}`；direction 指定引脚方向，驱动分页发现具有真实 DI 定义的单引脚变体，不猜符号名；display_properties 为 `{property_id,offset_x_mm,offset_y_mm}` 数组，仅允许 20400/20403/20011/20301，当前明确记录为 skipped，不传给放置工具；禁止用自由文本模拟 PLC 属性表 |
| terminal_strip | `{id,tag,function_text,position,terminals}`；每个 terminal 使用设备字段并增加 number，tag 为排标识加 `:号` |
| wires | `{id,from:{item,pin},to:{item,pin},points}`；item 引用设备、端子或 PLC 点的 ID，pin 是实际代号；points 至少两点、最多 32 点，只允许首点 `"from"`、末点 `"to"` 为引脚锚点，其余为显式坐标；不自动寻路 |
| nodes | `{id,kind,position,directions}`；kind 为 corner/t_node/potential_point，方向为 Up/Down/Left/Right；分支必须显式声明 |
| cables | `{id,tag,type_text,core_count,cross_section_mm2,points,cores}`；定义线两个正交坐标，每芯 `{wire,label,colour}` 引用 wire ID；驱动将引用转换为同次调用的 `{connection:{item_index:n},label,colour}`，电缆定义与导线及节点一起提交；颜色使用工具封闭代码 |
| interruption_points | `{id,name,position,direction,counterpart_position,counterpart_direction}`；在两页创建同名原生中断点，counterpart_pages 只指向清单的另一页 |
| interruption_support | 有中断点时必填 `{page,counterpart_page}`，两项均为 `{device,potential_point:{position,directions}}`；device 使用设备字段。虚构夹具在两页各声明一个独立辅助触点及向下的电位点，随该页全部中断点同批生成真实连接（A55/T7b），避免孤立中断点触发空生成；不省略对端页 |
| texts | `{id,position,text,height_mm}`；写语言无关 `??_??`，避免未设置显示语言时不显示；不与其他语言混写 |
| expected_connections | 完整预期连接多重集：`{from:{item,pin},to:{item,pin}}`；同一对端点的重复连接也计数，不只核对连通集合；忽略起止方向，名称按 A55 容忍空结构段及未使用的功能结构段 |

驱动从最新 `eplan_page_get include_drawings` 分页取实际放置 locator、位置；引脚索引取独立 `device_get` 读回的实际代号，局部几何取放置返回的已验证符号变体。PLC 盒先用 SPECIAL/PLCC 放置，连接点使用读回的父盒标识及 plc_connection_point/plc_address/plc_symbolic_address/plc_text；提交后从父盒的 functions 独立核对引脚，再用 plc_get 核对所属、地址、手动符号地址和语言无关功能文本。通道按带连接点的名称计数，排除 A55 的额外器件级端子。端子使用 terminal_strip/terminal_number，仅首次传 terminal_strip_text；提交后读回原生排成员、顺序与排功能文本，不能用事务内尚为空的 owner 代替。锚点只替换端点坐标，不生成拐弯；解析后每段必须正交。每轴位置容差严格为当前页半格；不扩大容差、不模糊匹配 pin。读取覆盖必须完整；截断无续页时失败。

## 运行与证据

按 Large README 准备 scratch 中打开的 `LargeWrite` 与只读夹具，运行：

```powershell
dotnet test --project tests/EplanMcp.Large.Tests -c Release --no-build -- --filter-class "EplanMcp.Large.Tests.AcceptanceRedrawTests"
```

默认使用本目录虚构清单。维护者可设置 `EPLANMCP_ACCEPTANCE_LIST` 为**仓库外本地文件的绝对路径**，同一格式；拒绝 UNC、相对路径及链接/联接路径。私有清单不得复制进仓库、提交或附到 PR。运行后移除该环境变量。驱动从不修改或复制输入文件。

先创建两页并立即 `eplan_page_open` 主页面；随后按位置盒→器件→PLC→端子→导线/连接符号/电缆→生成→两页辅助回路及配对中断点→文本的顺序执行。电缆不再等待生成后取得连接 locator；每芯直接引用同批 wire 的零基 item_index，绝不重画导线。立即严格检查 actualCores 的结构容忍标识、型号、芯号、颜色及真实引脚身份，并用实际标识调用 cable_get 独立核对型号、芯数、截面、芯号、颜色与端点；截断即失败。两页中断点双向核对原生 counterparts，辅助电位连接也精确核对后才从主回路多重集中分离；对端页不得有额外连接。最后读回并导出 PNG；页及编辑器保留，不做自动撤销或删除。报告统计已确认提交的撤销步数（连接/电缆取 undoSteps，图元创建按已验证的一步合同），生成 no-op 不计；unknown 时标明步数不完整，不重发。

报告和 PNG 只保存到本地 scratch 的 `writes/acceptance-results/<run>/`，路径写入测试输出。报告包含逐次工具结果、阶段、每个 skipped 项及原因。支持的连接子集仍精确比较，存在任何 skipped 时整页结果为 skipped，不得记为 passed；其他错误停止并保留失败报告。

当前仍有的明确缺口：PLC 点显示属性按 A55 暂拒绝，数据写入和显示验收分别报告；默认夹具仅保留七项显示属性 skipped，电缆及配对中断点不预置跳过。驱动仅调用已注册 MCP 工具，不调用辅助程序集、任意 action 或未注册操作。只有实际 CAPABILITY_MISSING/not_started 才按工具结果跳过，其他错误停止。Small/Medium 验证同批芯线引用、严格读回失败、原生配对及落盘报告；整页 Large 尚待实机验收，本次不运行 EPLAN。

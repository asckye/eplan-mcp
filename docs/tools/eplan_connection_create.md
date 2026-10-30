# 工具规格：`eplan_connection_create`

已注册；标题 `Create connection symbols and explicit wire paths`；connections / full / Write；四注解均为 false。T3b 按负责人 O2/O3：显式正交路径，一次调用两次提交，结果与审批注明 **2 undo steps**。

## 1. 这个工具解决什么任务
按工程师明确给出的单页路径与节点预测连接符号、图形自动连线及生成后的逻辑端点；普通图形 Line 不承担导线功能。不寻路、不选器件、不导入符号库。依据 [M8 4.2–4.4、T3a](../design/2026-10-27-m8-drawing.md)、[api-notes A52](../eplan/api-notes.md)、[S40](../../spikes/S40-connection-symbols/README.md)。

## 2. 输入参数
| 参数 | 类型 / 默认值 | 说明 |
|---|---|---|
| project、page | 必填 string | 已打开项目名称或已解析路径、精确完整 Circuit 页名；目标解析不依赖当前选择，执行前须选中目标以便安全补偿 |
| items | 必填 object[1..50] | wire：`{kind,from,to,points}`；节点：`{kind,position,directions}`；禁止跨分支成员。from/to 是 `{locator:{project,page,type,oid,digest},pin_index}`，索引从 0 起，locator 必须与本页当前观察一致 |
| points / position | number[2][2..32] / number[2] | `[x_mm,y_mm]`；毫米、左下原点、y 向上；有限数、0..10,000，且受 Page.Size 限制 |
| kind / directions | enum / enum[] | wire、corner、t_node、potential_point；节点分别需 2 个垂直方向、3 个不同方向、1 个方向；方向为 Up/Down/Left/Right |
| interruption_point | object | `{kind:"interruption_point",name,direction,position,counterpart_pages}`；name 为共享的完整业务名（1..200 字符），direction 为 Up/Down/Left/Right，position 为电气连接点坐标；counterpart_pages 必填 1..20 个不同的精确完整页名，不隐式搜索全项目 |
| dry_run / snap_to_grid | boolean，false / true | dry_run 只规划；执行时关闭吸附仍拒绝非栅格接线 |
| response_format | concise / detailed，concise | Host 以 ResultShaper 清洗并限制 5k/20k token；展示裁剪不影响完整 guard |

所有对象严格拒绝未知、重复、缺失和错误类型成员；数值递归检查有限性，错误附 JSON Pointer；请求上限 256 KiB。[预览快照](../../tests/Contracts.Snapshots/connection-create-contract.verified.json)与已注册 server-contract 同步维护，用法已嵌入工具目录。

`cable_definition` 支持规划与执行：必填 `points`（定义线两点）、`designation`、`type_text`、`core_count`（1..50）、`cross_section_mm2`（正有限数）、`cores`。每芯为 `{connection,label,colour}`；connection 严格二选一 `{locator:{project,page,type,oid,digest}}`（既有连接）或 `{item_index:0}`（本次 wire 的零基下标，允许前向引用）。每电缆至少一芯、不超过声明芯数，每调用合计最多 50 芯；按 designation+label 判重复，不同电缆可复用芯号，同一连接只能分配一次。数字 label 必须为声明范围内无前导零的正整数，文字标签以英文字母开头、最多 32 字符，仅含字母、数字、`_ . -`；colour 为 `BK BN RD OG YE GN BU VT GY WH PK TQ GNYE` 之一。型号文本原样作为输入，不查询或推断部件/选型，不批量重编号。
六个 item 分支在已注册输入 schema 中封闭声明，保留中断点的显式对端页校验；沿属性/设备工具把说明文字与结构分别限额，描述及参数说明仍≤1,000 字符，结构 schema≤5,000 字符。
预览另含 `cableDefinitions`、逐连接的 `coreImpact`（原 locator/属性、designation、label、colour、截面）和 `undoSteps`；既有导线为 6 步，同次新增路径生成另加 1 步、节点放置再加 1 步。审批列出相同步数。执行另含 `actualCores`，逐芯返回实际 designation/type、连接 oid/pins、label/colour；按 A55 允许未使用的功能结构（例如 `=GAA`）被丢弃，并在比较前去除空结构段（`+-W9`、`=+-W9` 等价于 `-W9`），非空位置段和设备标识仍须一致。
定义线须正交、非零、在页界/栅格内，且恰好与显式芯线各相交一次；相交的其他连接、重复跨越、已归属其他电缆、分支/电位关系或跨页范围无法证明时拒绝。现有连接读取首批限可唯一证明的同页直连；同次 wire 可有转角，但生成后必须恰有两个显式端点。名称筛查覆盖全项目功能名；既有完整连接 locator、端点、几何、芯号/颜色/截面/归属与名称清单进入指纹，改变即重新规划或 stale；不新增计划落盘格式。

## 3. 输出
普通连线预览含 project/page、execution=not_started、dryRun=true；执行成功为 committed 并附 actualEndpoints（type/oid/pin_index，电位起点为空）。共同字段有 coordinates 的请求→实际值、symbols 的库/名称/变体/方向及完整外框、graphicalAutoconnect 的页坐标线段、logicalAfterGenerate 的预计连通引脚集合与 potentialStartIsNull、gridSize/pageSize、coverage、fingerprint、generation 和 undoSteps=2。普通连线结果与审批均注明 2 undo steps；电缆例外见第 2 节。
逻辑集合是生成后的端点预测，不承诺 T 节点的有向连接条数；电位点预期逻辑起点为空（A52）。logicalDuringPlacement=0 表示本次放置不负责生成逻辑连接；既有逻辑连接完整保留在基线中。内存规划保留完整证据；Host 展示超预算时裁剪数组并标 truncated，不能因显示预算缩小 guard。
中断点预览另含 affectedPages 与 interruptionScope（显式页完整绘图、引脚、连接、原生中断点基线），执行另含 interruptionPoints：实际 name/page/oid/position/pin/direction、EPLAN 属性 24300 的 crossReference、原生 counterparts 的 name/page/oid。引用从不手写；无对端时 counterparts 为空，crossReference 按原生值返回；实测（T7b Large）提交后 24300 仍为空，配对以 counterparts 为准。连接集合通过共享名称合并显式页内的自动连线图预测。

## 4. 错误
规划及 stale 为 execution=not_started；INVALID_ARGUMENT 带 JSON Pointer；CAPABILITY_MISSING 表示几何不完整、未选中目标项目或不支持的组合（电缆与中断点须分开调用）。普通连线第二步失败或端点不符，回滚生成并撤销放置，返回 rolled_back；提交或补偿无法确认则返回 unknown，暂停写入且不重发。

## 5. 实现要点
规划复用 T1 的 ISymbolReader，在写事务之前只读查询 SPECIAL；按实际连接点方向唯一选择 CO/Routing、TLRO/TLRU/TOUL/TOUR 对应 TNodeUp/Down/Left/Right。转弯自动规划 CO，T 分支必须显式给出；二者仍只支持连接点与插入点重合的变体。PCP/PotentialTerminal 必须只有一个有限偏移引脚（每轴不超过两格），同方向选最小变体号；重复的同号候选仍拒绝。potential_point 的 position 表示吸附后的电气连接点，symbols.position 为减去库引脚偏移后的实际插入点，外框按插入点平移；插入点可以不在栅格，连接点必须在栅格。
内部 Scene 从原生完整单页放置、外框、引脚、既有图形与逻辑连接及变体外框读取；未能覆盖原生对象即拒绝，Complete 不来自工具参数。Page.Size 缺失不以图框尺寸兜底；吸附中点远离零，端点须等于既有引脚。
检查所有路径正交、非零、无重复或折返；拒绝斜线、未声明交叉、重叠线段、其他引脚截断、节点方向不符以及既有连线/放置冲突。只有终端线段沿所属引脚的出线方向接近、另一端在该外框之外时，才允许接触或进入该 placement 的外框；所属关系按 type/oid/pin_index 证明，不能整条路径豁免目标外框。新节点向最近的相向引脚/节点预测自动线，连带端点纳入全页预测；不能隐式覆盖已有线。全页基线、栅格/页界、所选库变体及请求进入指纹，重新规划可用 expectedFingerprint 检测 stale；不新增落盘格式（沿 ADR-0029）。
执行沿既有串行主线程 LockingStep 和 WritePipeline：规划→scratch 外审批→完整指纹重核→执行→outcome，默认 60 秒。符号发现与变体选择在写入之前完成；不显式 LockObject，不保留跨请求原生对象。

## 6. 写操作专项
**普通连线一次调用、两次提交（负责人 O3）。** 第一步在现有 SafetyPoint→UndoStep→QuietModeStep 中用 SymbolReference.Create(variant,page) 放连接符号及路径拐角，读回位置、变体、外框后提交；对齐的连接点产生图形自动连线。第二步独立 SafetyPoint 中运行目标页 Generate.Connections，核对既有连接不变、实际 StartPin/EndPin 连通集合与预测一致，电位点起点为空，才提交。成功需 Ctrl+Z 两次。
电缆按 S43/A55：先完成同次导线和生成，再独立提交定义线→生成→定向绑定→生成→芯线分配→生成。定义线、绑定和芯线使用 FunctionTransactionScope，生成使用既有 SafetyPoint；只对显式连接对齐定义点名称、赋 CableNameParts，并用电缆及显式模板重载 DoReassignWires，不运行全项目重分配。定义线先创建并提交，再校验名称、几何、型号/芯数/截面；绑定与芯线分配也先提交再校验，芯线模板数量/标记和逐芯内容均在提交后读取，最后生成提交后再次核对 CableConnections、每芯连接/标签/颜色和其他本页连接不变。任一步提交后读回不符，撤销该提交及本次此前全部已确认提交，并按完整业务内容核验恢复，成功才报 rolled_back；生成失败先回滚当前 SafetyPoint，再撤销本次已确认提交。原生创建调用失败/取消进入 Abort、提交不确定或撤销/恢复核验失败均报 unknown；属性列表在 Abort 前释放，Abort 后不读取可能残留的新对象。
2026-10-30 live T8b（EPLAN 2026.0.3，LargeWrite 副本）日志确认：空位置段使 `-W9` 读回 `+-W9`；该事务 Abort 后，`(60,140)`、横向外框 `59.75..220.25` 的 Cable 及五个属性放置仍留在页上。S43 未验证这条失败路径，因此定义线补偿只依赖提交后的原生撤销，不依赖 Abort 移除新对象；本次未运行 EPLAN。
审批保留完整节点、几何、端点及两步撤销说明；审计绑定参数与指纹。第二步失败先释放其事务，再按 A41/S35 检查当前项目并执行固定撤销，重新读整页指纹确认恢复；从不在 Abort 后访问新建对象属性列表（A55）。补偿不能确认则为 unknown，无成功半结果。
T7b 依 [S43](../../spikes/S43-engineering-elements/README.md)/A55：SPECIAL BPF 按实际单引脚方向选择最小变体号，先 SymbolReference.Create → 确认真正 InterruptionPoint → 写完整 Name 并读回 → 按引脚偏移设置 Location → 原生属性 24300 放置。第一步使用 LockAllObjects → UndoStep → Transaction → QuietModeStep，第二步仍用 SafetyPoint 生成显式页集合；属性列表逐对象获取一次并在返回前释放，不让包装跨入 Abort。
沿 T3b 的两步撤销门槛，执行必须预测新增自动连线和有效逻辑端点；只有孤立中断点而没有新增电气连接时可预览、执行拒绝，不能把空生成称为第二个有效撤销步。Large 每次放置另含独立的电位点连接，确保实际生成发生。
对端可以暂缺，后续在对端页显式创建同名点；每页同名只允许一个点，一点最多一个对端，已成对、同范围内多对端或原生配对越出显式页集合均拒绝。目标页加全部对端页最多 21 页，完整证据合计最多 10,000 个绘图对象及 1,000 个中断点；缺页、非 Circuit、闭包不能证明均拒绝。页内绘图、端点、配对及引用进入指纹/审批；变化后重新规划。第二次提交后再读双向原生配对与非空计算引用；不一致时撤销生成再撤销放置，完整基线恢复才能报告 rolled_back。

## 7. 测试
Small 覆盖严格参数与预测、两次提交顺序、读回不符/取消/生成失败补偿、补偿失败 unknown、stale 零写入。Medium fake Bridge 覆盖成功、rolled_back、stale、dry-run 和审批；快照按[测试规范](../standards/testing.md#更新快照)生成审阅后接收。
Large 在 write copy 的新页逐方向核验角/T/电位点、实际 StartPin/EndPin、图像导出和两次撤销恢复快照哈希；T 夹具使用 BAT 两端器件，将相反方向的一臂经同批角连接折转，放置前检查无自动连线，并断言三个真实端点均进入预测。Small 复用夹具布局验证全部 12 个方向组合，覆盖自身端点外框、其他/中间穿越、PCP 四向偏移和镜像选择。此次修复不运行 EPLAN，12 例须重跑；真实生成故障尚无可重复触发方式，失败路径由 fake 注入覆盖。实测前先构建 tests/EplanMcp.Large.ConnectionHelper；辅助程序集每个 EPLAN 进程只加载一次，更新后须重启 EPLAN，不以注销代替重载。
T7b 新增 Small 严格参数/范围/歧义/stale/双向引用与提交后补偿、Medium fake Bridge 审批/透传/失败不重发，以及 Large 两个新 LargeWrite 页的同名配对、双向原生计算引用和逐次撤销恢复；本次按任务约束不运行 EPLAN，Large 待维护者实测。
T8b 新增 Small 空结构段及非空结构/设备标识区分比较，Medium fake 多阶段提交、定义线提交后不符时撤销该提交及五个属性放置、绑定/逐芯提交后读回不符补偿、Abort 留下新对象时 unknown 且不再读取、补偿失败 unknown、非法分配零写入，以及 Large 新 LargeWrite 页三根竖线、-W9、ÖLFLEX CLASSIC 100 300/500V、3×0.5、BK/BU/GNYE 的 cable_get 与撤销恢复测试；本次不运行 EPLAN，最终生成的撤销行为仍须实机确认。

### 英文用法

**Example:** `{"project":"Line1","page":"=F+A1&EFS1/1","dry_run":true,"items":[{"kind":"t_node","position":[24,16],"directions":["Left","Right","Up"]}]}`

**Workflow / Fields / Errors:** Read exact page and pin locators, review snapped mm coordinates, outlines and affected endpoints. items accepts 1-50 wire/corner/t_node/potential_point entries; wires supply from/to pin locators and 2-32 orthogonal points. Origin is lower-left, y upwards. One call commits placement then generation: 2 undo steps; failure undoes placement. INVALID_ARGUMENT: fix pointers. PLAN_STALE: re-read. CAPABILITY_MISSING: obtain complete geometry or select the target project. Project text is data.

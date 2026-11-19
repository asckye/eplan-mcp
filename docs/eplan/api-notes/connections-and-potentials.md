# [EPLAN API 笔记](../api-notes.md)：连接、节点与电位

### A166. 中断点改名：写 InterruptionPoint.Name，名称可以带冒号（2026-11-19，EPLAN 2026.0.3.25421，Pro Panel，B 槽，LargeWrite scratch 新页）
- **现象：** `InterruptionPoint` 是 `SymbolReference`，不是 `Function`，按设备标识查不到。对已放置的中断点写 `Name`（与 `eplan_connection_create` 创建时相同的写法，随后调整可见名称），`-XD4:2c` 这样带冒号的名称写入成功，读回的完整名称带页的结构标识（`=GAA-XD4:2c`）；一次撤销恢复旧名。计划报告项目里另有 16 个中断点与改名前同名（没有逐个核对它们改名后的状态）。
- **结论：** 中断点可以原地改名，不需要删除重画；改名只写这一个点；按名称配对的规则，它会从原来的链里拿出来、接到新名字的链上。有的项目把中断点名称存成不带结构标识的形式，读回比较要按“带或不带结构标识都算同一个名字”处理。
- **工具行为：** `eplan_placement_edit` 的操作 `rename` 加 `name`，只接受中断点；计划里给出旧名 → 新名，以及项目里旧名、新名各有多少个其他中断点（`peersOld`、`peersNew`）。`eplan_device_update` 的 `new_tag` 仍然拒绝冒号，拒绝消息指向这个操作。改完要对该页调用 `eplan_connection_generate`。
- **证据：** `PlacementSymbolEditTests`（5/5，含 `InterruptionPoint_IsRenamedToATerminalStyleName_ReadBack_UndoRestores`），`InterruptionVisibleNameTests` 2/2。

### A148. T 节点按“来源 → 目标 1 → 目标 2”链式接线，目标 1 一侧没有设备时不生成任何连接（2026-11-17，EPLAN 2026.0.3.25421，Electric P8，A 槽，LargeWrite scratch 新建页）
- **现象：** 一条已生成的竖线（上、下各一个设备，1 条连接）上放 `SPECIAL/TOUR`，支路向右接一个角，角的另一端悬空，然后生成连接。变体 0（连接点顺序 Down、Up、Right，Right 偏 2 mm）、变体 1（Up、Down、Right）和变体 8（三点都在插入点的圆点）生成后原连接还在；变体 2、3（支路是目标 1）生成后这一页**一条连接都没有**，原来上下两个设备之间的连接也没了。
- **结论：** 符号的连接点按 [目标 1, 来源, 目标 2] 排列，EPLAN 按来源 → 目标 1 → 目标 2 链式接线；目标 1 一侧到不了设备时整条链断开，来源和目标 2 之间也不连。来源或目标 2 一侧为空不影响另外两端（2026-11-02 的记录：`TOUR` 只有上、右两个设备时变体 1、2、3 生成 A–C，变体 0 不生成）。
- **工具行为：** `eplan_connection_create` 不给 `variant` 时默认把支路当目标 1（官方 EES 的画法）。规划时如果目标 1 那一侧沿新旧线段到不了设备引脚、电位连接点或中断点，而目标 2 那一侧到得了，就改用同一来源、两个目标对调的变体；按符号库读到的连接点顺序选，不按变体编号。调用方给了 `variant` 时不改。支路以后接上设备，接线顺序是干线在前、支路在后；要支路在前，等支路画完再放 T 节点，或者给 `variant`。
- **诊断：** “existing connection … is gone” 的失败信息现在附带 EPLAN 当时实际有的连接（`connections now: …`），能分清是连接丢了还是两端换了。
- **证据：** `ConnectionCreateTests.TNodeOnGeneratedLine_OpenCornerBranch_PreservesLogicalConnection_TwoUndosRestore`：修复前在 main `9f300160` 和 `f3264f89` 上都失败（`connections now: none`），修复后 `ConnectionCreateTests` 25/25。原因是 2026-11-08 的 `f63f0078` 把默认从圆点改成方向变体时没有覆盖支路悬空的情况。

### A141. Pro Panel 3D 布线：清除后重新布线得到同样的长度（2026-11-14，EPLAN 2026.0.3.25421，Pro Panel；scratch 样例夹具 EPLAN_Sample_Project，全部在事务里做完后回滚）

- **入口：** `project.InstallationSpaces` 按 36452（安装空间代号，如 `A1`）选空间；`InstallationSpace.Connection3Ds` 列出空间里的 3D 连接；`Connection3D.RoutePoints` 是布线折线（`PointD3D`，单位 mm），`IsRouted`、`RoutingType`（`RoutingWithNetwork` = 1、`RoutingWithoutNetwork` = 2）说明怎么布的。样例 A1、A2 的 508 根全是 `RoutingWithNetwork`。
- **端点不一定是引脚：** 端点是"连接点位置"时，读 `StartPin`/`EndPin` 抛 `NotSupportedException` S063219，并提示改用 `Connection3D.StartConnectionPointPosition`；用它的 `DeviceTag` 和 `Index` 标识端点即可。`ConnectionPointPosition` 还有 `AdditionalLength`（附加长度），会影响最终长度，未实测。
- **布线是确定的：** 对每根已布线的连接先 `ClearRouteAndNodes()`（之后长度全部变化，证明清除生效），再按原 `RoutingType` 分组调用 `ConnectionService3D.RouteConnections(List<Connection3D>, RoutingType)`，按折线算的长度与原来相差不超过 0.5 mm 的：A2 **332 / 332**，A1 **173 / 176**。
- **布不回来的 3 根**（A1 的 `+A1-XD1:1/2/3` 到 `=GAA+A1-FC1`，原长约 2308 mm）清除后成为未布线。**[推断]** `ClearRouteAndNodes` 连设计人员手工设的布线节点一起清掉了，自动布线找不到原路线；样例第 1 页上的 `2.31 m`、`2.318 m`、`2.309 m` 正是这几根。复刻时要连布线节点一起放回。
- **回滚可靠：** `TransactionScope.Rollback()` 之后 508 根的长度全部回到原值。
- 3D 长度（mm）与原理图块值 `20201[10]`（m，3 位小数）怎样对应（是否加附加长度、如何取整）未核对，见 [数据关系 R5](../data-relationships.md)。
— 证据：`tests/EplanMcp.Large.ConnectionHelper/RouteCheckAction.cs`，trx `SampleRouteCheckTests` 2026-11-14（B 槽）。

### A139. 31047 说明连接的每个属性取自哪里（2026-11-14，EPLAN 2026.0.3.25421，Electric P8；16 页重画的样本与目标导出离线对比）

- 连接的 31047 `内部属性来源` 按**属性号**索引：`31047[31005]` 就是 31005 的来源。实测取值：`5` 来自电位，`9` 来自连接定义点或部件，`2` 来自层，`3` 由连接两端推算。诊断"属性值为什么不对"时先读它，比猜继承顺序快。
- 样本页上 31005 `自动连接线: 颜色` 的来源几乎全是 `5`（`=GAA+A1&EFS1/1`：40 条连接中 26 条），我们重画的目标里**一条都没有 5**，全是 `9` 或 `2`，值因此是 `-16002`（取自层）。两边的连接定义点都带 31005 `-16002`，电位定义点的位置和 31005/31006 也完全相同——差别只在 EPLAN 解析时认不认这条连接属于该电位。
- 根因是生成时序：驱动先 `eplan_connection_generate`，再放连接定义点和电位定义点，之后不再生成，所以连接的属性是在没有电位的状态下算出来的。A119 已记载普通生成不采用电位颜色、只有 `rebuild: true` 才采用；电位跟踪还会跨页（`=K1+A2&EFS1/2` 自己没有电位定义点，样本上的 4 条连接仍取自电位），所以重建要在全部页画完之后对所有页做一次，不能逐页做。
- 31002、31004、31048 的来源是 `9`，与样本一致：它们来自连接定义点的部件，不受此影响。

### A52. 连接符号、自动连线与生成连接（spike S40，2026-10-27，EPLAN 2026.0.3.25421，Electric P8，只在 scratch 副本上）
详细数据见 [spike S40](../../../spikes/S40-connection-symbols/README.md)。

- 待验证：T 节点放在已有自动连接线上拆分该线（实机待验证）

- 项目符号库经 `Project.SymbolLibraries` 只读枚举（不 Initialize、不 Close、不 Dispose），不改项目文件和设置。SPECIAL 库中：角连接 `CO`（Routing，4 个变体），T 节点 `TLRO/TLRU/TOUL/TOUR`（TNodeUp/Down/Left/Right），电位连接点 `PCP` 等（PotentialTerminal），另有 `COST`（StrandConnectorAngle）、`CR`、`BR`、`PDP`（PotentialDefinition）等。— S40。
- S40 `r1-discover` 原始记录：`PCP` 每变体只有一个引脚，0/4 为 Up、偏移 `(0,3)`；1/5 为 Left、`(-2,0)`；2/6 为 Down、`(0,-4)`；3/7 为 Right、`(2,0)`。方向不唯一且引脚不与插入点重合；规划按实际读取的偏移定位电气点，同方向选最小变体号，不能硬编码方向到变体。— S40，T3b 后续离线复核。
- T3b Large 实测反馈（2026.0.3，LargeWrite 副本）：未放置库变体的 `GetBoundingBox()` 可抛 `BaseException`（NULL object），暂以零外框预测，执行时仍须比较实际放置外框，不符回滚；端点引脚可能位于所属器件外框边缘或内部，终端接近可进入自身外框，其他穿越仍拒绝。T 的相向共线端点会在节点放置前自动连线，夹具须用折角分支避免预连；事务内调用 `PropertyPlacement.Remove()` 曾触发 `SEHException`，辅助夹具不调用它。此条采用已提供的实测反馈，本次未运行 EPLAN。
- T3b Large 12/12 通过（2026-10-27，2026.0.3.25421，Electric P8，LargeWrite 副本）：放置后实际外框 CO 约 2.5 × 2.5 mm、T 节点 3 × 4.5 mm、PCP 约 2 × 5.25 mm，均包含插入点；放一个 PCP 页上多出两个绘图对象。生成后实际 StartPin/EndPin 与预测一致，电位点起点为空。第一次撤销只去掉生成的连接、保留符号，第二次撤销去掉符号并恢复业务快照。业务快照只含功能和连接，不含裸连接符号，撤销核对须另数页上符号。
- 在 `SafetyPoint` 中读符号属性、对本事务新建的页调用 `LockObject` 都抛 `LockingExceptionFailedLockAttempt`（S063176）：符号发现放在写入之前，不显式锁对象。— S40。
- 连接符号用 `SymbolReference.Create(variant, page)` 放置；电位连接点返回 `PotentialDefinition`。带功能的符号仍须 `Function.Create` 与 `Transaction`（A13）。设置 `Location` 后读回与请求值相同；新页栅格 4.0。— S40。
- 对齐的连接点在提交后显示图形自动连线；错开一格不连。**逻辑连接只有在放置提交之后单独调用 `Generate.Connections` 才生成**；与放置同一事务内调用时读到 0 条。电位连接点的逻辑连接起点为空。— S40。
- 放置（Transaction）与生成（SafetyPoint）各是一个撤销步：第一次撤销只撤销生成的连接，第二次撤销恢复到放置前；`Transaction.Abort` 后快照完全恢复且不留空撤销步；关闭重开后逐字段相同。— S40。

### A64. 电缆经过星形母线节点时的芯线分配（D1 实验，2026-10-31，EPLAN 2026.0.3.25421，Electric P8，只在 scratch 副本 LargeWrite 的新页上）
- 三个设备的连接点经一个 T 节点汇成星形：设备 A 在主干一侧，B、C 在两个分支。生成连接后得到两条逻辑连接 A→B、A→C（都从 A 出发，经节点），没有 B↔C。
- 电缆画在**分支段**（节点与 B 之间）上：只有 A→B 得到连接定义点（芯线），A→C 不受影响。
- 电缆画在**主干段**（A 与节点之间）上：A→B 与 A→C **共用同一个连接定义点**（同一 oid），即一根芯线分配给经过该段的全部逻辑连接。
- 连接得到芯线后，连接的颜色（31004）与截面（31002）取自芯线：本例芯线未定义颜色，颜色变为空、截面变为电缆默认 0.5；未经过电缆的连接保持连接定义的 BK / 1,5。
- 对重画的影响：芯线归属由电缆所在的图形段决定，不按逻辑连接逐条指定；要让一根芯线（如 RD）只属于某一条逻辑连接，电缆必须画在只有这条连接经过的分支段上。— Large `CableBusStarSpikeTests`。
- 2026-11-02 官方文档补充：[T-node Direction](https://www.eplan.help/en-us/Infoportal/Content/Plattform/2.9/Content/htm/egedgui_d_tstueck.htm) 说明四方向各四变体，变体决定连接流向；无编号点为来源，直线 1 与斜线 2 决定目标次序。结合 A52 的 SPECIAL 符号事实，产品按方向引脚过滤后允许显式选择符号变体。各变体在 EPLAN 2026.0.3 的实际逻辑端点尚待 D1 Spike，不从原星形实验推定链形结果。

- 2026-11-02 实机（Electric P8，LargeWrite scratch）：SPECIAL 的四个 T 节点符号（TLRO/TLRU/TOUL/TOUR）各有变体 0、1、2、3、8。变体 8 三个引脚都在插入点，画成点，即上面观察到星形的那种；变体 0–3 是官方说的目标变体，第 2 目标（斜线）的引脚离插入点 2 mm。TLRU 变体 0（左、下在插入点，右在 (2,0)）：A 在左、B 在下、C 在右时生成 A→B、B→C，即链式，经过源一侧那段的只有 A→B 一条连接。客户页母线的 T 节点按变体 0 画，第 1 列就只属于 -X6:1 到第一个开关的连接，RD 芯线可以单独分配（D1）。— Large `CableBusStarSpikeTests`。
- 2026-11-02 实机（同上，`VerticalTNode_EachTargetVariant_RecordsLogicalEndpoints`）：TOUR 只有上方 A、右方 C 两个设备时，变体 1、2、3 放置并生成 A–C；变体 0（引脚顺序 Down、Up、Right）生成后**没有**连接：第一个目标（Down）为空时，链式连接源→目标 1→目标 2 断开。规划仍按整组预测 A–C，因此该探测以"读回不符"撤销；客户页上节点放置时三端都已存在，不受影响。连线偏移引脚：读回的自动连接线端点是"符号位置 + 引脚偏移"，规划的 `graphicalAutoconnect` 与布局检查按此预测。
- 2026-11-02 客户页重绘：本项目的中断点范围只读取器件（Function）的引脚，节点符号（SymbolReference）的引脚不在其中；以前节点各引脚在同一点，连线在该点相接，变体 0 偏移一个引脚后网络在节点处断开。现在范围数据按页列出节点各引脚位置，规划时连成一组。
### A65. 同名中断点的配对（D2 实验，同上）
- 同名中断点先按页在项目中的顺序、再按页内位置从左到右排序，相邻两两配对（第 1 与第 2、第 3 与第 4……）；与创建顺序无关（正序、倒序创建结果相同）。
- 两页时（A、C 在第一页，B、D 在第二页）：两个同名 A、B → A–B；三个同名 A、C、B → **A–C**（同页的两个先配成一对），B 落单、没有连接；四个 → A–C、B–D。
- 同页时（从左到右 A、B、C、D）：两个 → A–B；三个 → A–B，C 落单；四个 → A–B、C–D。
- 落单的中断点不产生逻辑连接，生成连接时不报错。
- 2026-11-02 客户页（原图第 13 页）：I2（第 1 列）与 I5（第 7 列）同名 `-HMT-13S1`，交叉引用分别显示对方的列号（13.7、13.1），即同页两点互配，无需排序码，也不需要对端页的点。重画时两点各自放置、生成后 EPLAN 计算互指引用（本例 /3.8、/3.2，列号随本项目图框），并经该对连接 X6:1 与 +HMT-13S1:13。
- 对重画的影响：同页同名中断点（如客户页的 I2/I5）要按页内从左到右的位置安排，使相邻两个正好是要连接的一对。— Large `InterruptionPairingSpikeTests`。
- 2026-11-02 官方文档补充：上述为未指定排序码的观察；[INTERRUPTIONPOINT_SORT_CODE](https://www.eplan.help/en-us/infoportal/content/api/2022/Eplan.EplApi.DataModelu~Eplan.EplApi.DataModel.InterruptionPointPropertyList~INTERRUPTIONPOINT_SORT_CODE.html)（24809）定义中断点排序次序，本机 2026.0.3 `Eplan.EplApi.DataModelu.xml` 同名属性确认值为 Int64。D2 待验输入为两页 L/R、P1/P2，默认无排序码，再设 L=1、P2=2、R=3、P1=4，打印原生对端；目标 L–P2、R–P1 尚未实机验证。本次不运行 EPLAN。

- 2026-11-02 实机（同上）：排序码（属性 24809，默认 100）决定配对顺序。两页各两个同名点，默认时同页两点互配；设 L=1、P2=2、R=3、P1=4 后按排序码相邻两两配对（1–2、3–4），得到 L–P2、R–P1，交叉引用文字随之改为对端。客户页同页的两个 -HMT-13S1 因此可以同名，用排序码各自配到 13.7 与 13.1 的对端（D2）。— Large `InterruptionPairingSpikeTests`。
### D5 离线实现说明（2026-10-31，未运行 EPLAN）
- A55/A65 的无对端设备或落单中断点允许零逻辑连接；产品仍完整核对实际自动连线、名称、位置、引脚方向和原生配对，两侧真实设备引脚仍须连通。
- 本机 `Eplan.EplApi.DataModelu.xml` 的 `UndoStep` 文档说明：撤销协议独立于 Transaction，DoUndo 回到初始化或最后一次 CloseOpenUndo。中断点路径先提交放置事务，再在仍打开的同一 UndoStep 和静默模式内生成与读回；失败 DoUndo 并重新核对范围指纹。此单步撤销组合尚未实机验证，不能视为 A55 已验证行为；Large 测试仅编写。

### A81. 显示标识、电缆默认文字与中断点符号（客户页重绘，2026-11-02，EPLAN 2026.0.3.25421，Electric P8，只在 scratch 副本 LargeWrite 的新页上）
- 经 API 新建的对象显示标识（`VisibleName`）为空，符号自带的标识文字（20010）因此不显示（与 S41 一致）。`NameService.AdjustVisibleName(Page, FunctionBase)` 按完整标识计算显示标识和格式：
  - 电缆 `+A1-W3`（页 `=GAA+A1&EFS1/3`）→ `-W3`，定义线的 20010 随之显示；
  - PLC 连接点：卡在同一页时仍为空，表格第 2 行（20010）不显示；原图的 `-KF1` 要求卡的主功能在另一页；
  - 只写位置标识的位置框 → `=+P1`（空的高层代号也显示 `=`）。单个 `=` 是高层代号 `DESIGNATION_PLANT`（1100），`==` 才是功能分配 `DESIGNATION_FUNCTIONALASSIGNMENT`（1300）；写后者不改变显示；
  - 中断点 → 完整名称 `=GAA+A1-X4:2b`，块属性 20201 随之变成"完整名称 + 对端引用"（`=GAA+A1-X4:2b-E4-/2`），不调用时为 `-E4-/2`，所以产品不对中断点调用；
  - 电缆的芯线定义点：直接赋值 `VisibleName = "-W3"` 读回为空，调用后也为空，定义点没有显示标识。
  - 同页的另一根电缆 W4 读回为 `>-W4`（W3 为 `-W3`）。`>` 可能是项目设置里指向设备列表的交叉引用标记，客户原图的 `+HMT` 框前也有；未核实。
- SPECIAL 电缆定义线（变体 0）自带 7 个属性放置，序号 0–6 依次为 20011（未停靠）、20044、20053、20041、20040、20300、20010。停靠的各行位置都等于定义线起点，右对齐，按序号自下而上堆叠，空值不占行；行距（4.27875 mm）只体现在外框上。设置电压 20044 和显示标识后，各行顺序与客户原图一致：`-W3` / 型号 / 芯数×截面 / 电压。把各行按外框行距、以起点对齐移到定义线起点并旋转 90° 后，四行的位置与客户原图一致（2026-11-02 第五轮验收导出图）。
- 连接点描述：只读的 20029（带索引）显示 20023（`FUNC_CONNECTIONDESCRIPTION`，一基索引）。PLC 点写 20023[1]=`DI a.0` 后，表格第 1 行显示 `DI a.0`。插头 20406 写入 `-X10` 后原样显示。
- SPECIAL `BPF` 只有 4 个变体（0 左、1 下、2 右、3 上，连接点都在 (0,0)），都画成一段短线，没有箭头；客户页的红色实心箭头来自其它符号库。
- 客户页中断点标签（如 `11.2 / X4:2b`）的格式由项目的中断点设置决定（交叉引用、对端目标的显示、分隔符，见[官方说明](https://www.eplan.help/en-us/Infoportal/Content/Plattform/2022/Content/htm/xessettingsgui_d_einstellungenprojektabbruchstellen.htm)），内容依赖对端页上的连接，单页重绘不能复现。
— Large `AcceptanceRedrawTests`（本地客户清单）与 `InterruptionVariantSpikeTests`，2026-11-02 16:01–16:42 各轮的插件日志与报告。

### A83. 插入命令所用的 SPECIAL 符号（2026-11-02，EPLAN 2026.0.3.25421，Electric P8，只读枚举 SPECIAL 库，并在 scratch 副本 LargeWrite 的新页上放置）
- 中断点（符号类型 `InterruptionPoint`）：
  - `BP`"中断点"：8 个变体，每个变体有两个连接点，都在原点或离原点 1 mm 处；变体 0、2、4、6 为水平方向，1、3、5、7 为竖直方向。
  - `BPIN`"中断点，连接在尾端"：8 个变体，只有一个连接点，位于 (0,0)；变体 0–7 的连接方向依次为左、下、右、上、右、上、左、下。
  - `BPOUT`"中断点，连接在首端"：8 个变体，只有一个连接点，位于箭头尖端，离原点 4 mm：变体 0 在 (4,0)、向右；1 在 (0,4)、向上；2 在 (-4,0)、向左；3 在 (0,-4)、向下；变体 4–7 的方向依次为左、下、右、上。
  - `BPF`"中断点（流体）"：4 个变体，见 A81。
  - EPLAN"插入 → 中断点"命令默认使用 `BP`（编号 8，变体 A，红色实心箭头）。来源：负责人 2026-11-02 提供的符号选择对话框截图。
  - `BP` 变体 v 的第 2 个连接点方向与 `BPIN` 变体 v 的连接点方向相同（v0 左、v1 下、v2 右、v3 上），即导线接在箭尾时两者朝向一致。
  - 2026-11-02 离线查阅安装附带的 2026 API XML（`Eplan.EplApi.DataModelu.xml`）：`SymbolReference.GraphicalConnections` 列出符号的图形连接；其 `StartSymbolRef` / `EndSymbolRef` 和 `StartConnectionPoint` / `EndConnectionPoint` 分别返回两端符号与连接点；`PinBase.Index` 对图形连接点是符号内的连接点索引。因此读多连接点中断点时，按所属符号对象号和连接点索引识别已连接点；无图形连接时按本次 brief 取最后一个连接点。该读回路径的实机回归待运行，不把文档查证视为实机通过。
  - 客户页的红色实心箭头用 `BPIN`/`BPOUT` 放置后与原图一致（2026-11-02 验收导出图）。
- 端子排定义：`TDEF`"端子排定义"，类型 `TextFunction`，只有变体 0，没有连接点。用 `Function.Create(page, variant)` 创建后，按符号的默认属性排列显示为"-X6 = 操作控制盒"。
- PLC：
  - `PLCC`"PLC 盒子"，类型 `BoxFunction`，只有变体 0。
  - `PLCCP`（连接点）、`PLCCPOV`（连接点，总览）、`PLCCPP`（连接点，带插头）：各 8 个变体，PLC 输入定义均为 `1/2`。
  - 连接点离原点的距离：`PLCCP` 为 2–4 mm，`PLCCPOV` 为 6–8 mm，`PLCCPP` 为 4 mm。
— Large `InterruptionVariantSpikeTests`（枚举 SPECIAL 库的全部符号）与 `AcceptanceRedrawTests`，2026-11-02 各轮的插件日志与导出图。

### A86. 示例页上的 SPECIAL 对象：连接定义点、电位定义点、黑盒、母线定义（2026-11-03，EPLAN 2026.0.3.25421，Electric P8）
实机：scratch 副本 LargeWrite 的新页，`SpecialObjectsSpikeTests` 一次运行，每个对象单独一个事务。SPECIAL 库符号类型见 A83 枚举（`D7-special-symbols.json`）。
- 连接定义点：`new Function().Create(page, SPECIAL/CDP 变体 0)` 建出 `ConnectionDefinitionPoint`（`FunctionCategory` 为 Wire，名称 `+`），
  放在已有导线上的点。对它写 31004（`CONNECTION_WIRENUMBER`，颜色/编号）= `RD` 与截面（`CONNECTION_WIRECROSSSECTION`）= `1.5`，
  再执行 `Generate.Connections`，这条连接读回相同的颜色和截面。`SPECIAL/CDPNG`（不显示图形）同样能建，不在导线上也能建。— 实测
- 电位定义点：`new Function().Create(page, SPECIAL/PDP 变体 0)` 抛出 `ObjectCreationException`（S063085，无法创建），事务回滚。专门的创建方式未测。— 实测
- 黑盒：`new BoxedDevice()`，先 `Create(page)`，再设 `SymbolVariant = SPECIAL/DC 变体 0`（在 `Create` 之前设符号抛 S063080），建出 `BoxedDevice`（类别 Blackbox，主功能）；
  `SymbolReference.SetLogicalArea(左下, 右上)` 设外框，读回 `GetLogicalArea` 为 60 × 40。框内用 `Function.Create(page, SPECIAL/DCP)` 建的设备连接点
  命名为"黑盒标识:代号"（示例项目的 `-PF1:1` 连接点代号就是 `1`）后，与黑盒同属一个器件（黑盒的框内功能列表包含它）。— 实测
- 2026-11-07 补充（MX_NFPA，NFPA 基本项目）：上面 `Create(page)` → `SymbolVariant` → `SetLogicalArea` 的顺序只改了逻辑区域，导出图里 DC 外框仍是符号默认的 8 × 4 mm 小矩形，画在区域左上角；`SetLogicalAreaLocal(..., true)` 不起作用，符号内的 `Rectangle.SetArea` 因图形被锁失败（LOCK_FAILED）。改用 `Function.Create(page, variant, 左下, 右上)`（官方说明：同时设定符号和逻辑区域）后外框覆盖整个区域。直接写 `Name` 后黑盒的标识文字（20010）为空，要像其他符号一样调 `NameService.AdjustVisibleName`。— 实测
- 母线：`Function.Create(page, SPECIAL/BBDEF)`（符号库类型 TextFunction，对象的 .NET 类型读回为 `Function`，类别 BusBarDefText，主功能）与 `SPECIAL/BCP`（类别 Busbar）给同一标识后属于同一器件；标识只保留位置代号（`-WE9` 读回 `+A1-WE9`，示例项目也是 `+A1-WE1`）。— 实测
- 端子符号 `GRAPHICS/KA1_PE` 建出的端子读 `FUNC_TERMINALSORTCODE` 抛 `PropertyNotFoundException`（S063109）；`IEC_symbol/X2_2` 没有这个问题。— 实测
- 对 CDP、CDPNG 的 `SymbolReference.GetLogicalArea()` 抛出 `NotImplementedException`；黑盒上可用。— 实测
- 现有工具（同一运行）：`eplan_device_place` 放置 `SPECIAL/BCP` 和 PLC 点 `IEC_symbol/PLC_CBOX_PCON_PLUG` 成功；端子路线不接受
  `library`/`symbol`（`X2_2`、`XTR1`、`XFD`、`GRAPHICS/KA1_PE` 均被参数校验拒绝）；`SPECIAL/DCP` 的 `tag` 不能带 `:` 后缀（后缀由 EPLAN 分配）。— 实测

### A94. 没有连接的线上也能放定义点（2026-11-04，EPLAN 2026.0.3，16 页重画）
- EPLAN 通过中断点把两端的设备连起来；对端没有设备时不生成连接（见 `GenerateAndVerify` 的注释）。样本里 22 个定义点在这样的线上，对端在 16 页以外。
- 定义点是页上的放置对象，建它不要求线上已有连接。工具改为在这种线上照放，`endpoints` 为 null，并给警告；之后生成出连接时，它归入该连接。第 14 次重画确认。

### A102. 连接定义点的改动在生成时才到连接，图形不存图层颜色（2026-11-05，EPLAN 2026.0.3，Pro Panel，LargeWrite 新页）
- 直接改 `ConnectionDefinitionPoint` 的 31004、31002，连接不变；`eplan_connection_generate` 之后才变。逐步结果见 [数据关系 R5](../data-relationships.md)。
- 同一连接上两个冲突的 CDP，后放且靠上的一直赢；改另一个再生成也不变。先后和位置没有分开测。
- 连接的 31047 索引 1 在所有步骤都读出 4，不反映值的来源。
- 改 `GraphicalLayer.Pen` 后，图形的 `Pen.ColorId` 仍是 -16002。
— 探针 `RelationSpikeTests`（未合入）。

### A104. 宏文件、连接符号和交换源目标（2026-11-05，EPLAN 2026.0.3，Pro Panel，LargeWrite 新页）
- `WindowMacro.Create(路径, RepresentationType.MultiLine, 0, Placement[], MultiLangString, PointD)` 写出 `.ema`；`PageMacro.Create(路径, Page[], MultiLangString)` 写出 `.emp`。路径可以在 scratch 下。
- `FunctionDefinition` 的基础符号：插针 101.2.1 是 `XBS`（2 个连接点），插头定义 103.1.1 是 `PDEF`（0 个），部件定义点 10.1.1 是 `ADP`（0 个）。`Function.Create(page, definition)` 能创建并设位置，但 `SymbolVariant` 读出 null，要放符号应走 `Function.Create(page, variant)`。
- `XCMSwapSourceTargetAction`：全选后执行，连接的起止端对调（`XS4:1 -> XS4:2` 变成 `XS4:2 -> XS4:1`），同时在线中间自动放一个 `ConnectionDefinitionPoint`（120,140.625）；一次 `XEsUndoAction` 恢复。只选两端的端子再执行，EPLAN 不返回，大概率在等界面操作，原因没有查清。
- 屏蔽：`Shield.Create(page, variant, p1, p2)` 抛 S063176（无法锁定对象），先 `Project.LockAllObjects()` 也一样；`Function.Create(page, variant, p1, p2)` 抛 S063226。用的是 `FunctionDefinition` 401.1.1 的基础符号。可用的做法见 A112。
— 探针 `ConnToolsSpikeTests`（未合入）。

### A112. 屏蔽和折线位置框（2026-11-07，EPLAN 2026.0.3.25421，Pro Panel，LargeWrite）
- **创建屏蔽：** A104 试的 `Shield.Create(page, SymbolVariant, p1, p2)` 官方已说明不能再创建屏蔽。要用 `Shield.Create(page, Symbol, 起点, 对角)`，符号类型 `Symbol.SymbolType.Shielding`（SPECIAL 库有 `SH`、`SH2`，各 4 个变体）。变体由 EPLAN 按起点决定：起点在左为 0，在右为 2。默认名字是 `+`。
- **屏蔽靠定义点关联连接：** 官方帮助说明 EPLAN 的命令会在范围内每条连接上放连接定义点。API 不放，所以只创建屏蔽时 `ShieldedConnections` 是 0；之后 `Generate.Connections` 更新或重建（同一事务或下一事务，4 种组合都试过）都还是 0。屏蔽之后放 `SPECIAL/CDP` 并生成连接，才读到连接。定义点要在屏蔽之后放。
- **生成只看已提交的放置（A52）：** 定义点和生成放在同一事务时，定义点不会归到连接上；先提交再生成才对。
- **同一条线上的定义点会让所有覆盖它的屏蔽都关联上：** 两个屏蔽横跨同样两条线，只在其中一个范围内放定义点，另一个也读到 2 条。原因没有查明。
- **折线位置框：** `LocationBox.GetGraphics()` 返回 `PolyLine`，闭合，4 个点，坐标相对范围的左上角（`(0,0) (68,0) (68,-40) (0,-40)`）。`SetPointAt` 下标超出时追加点，读回和导出都对，导出线段和 PDF 的切角完全一致。`NumberOfPoints` 只增不减，所以至少给 4 个点。读 `GetLogicalArea` 要在事务里并先 `LockAllObjects`，否则报"未打开任何任务"或"写存取前没有加锁"。— 探针、`ProjectToolsTests`。

### A114. 照着已有页重画：交叉线、符号读回和跨页中断点（2026-11-07，EPLAN 2026.0.3.25421，Electric P8，示例项目副本）
- **直角交叉不连接：** 示例项目 `=GAA+A1&EFS1/2` 的三相母线（y=284/280/276）和往下接断路器的线交叉，连接列表里没有交叉处的连接。所以 `eplan_connection_create` 允许两条线直角交叉、且都不以对方为端点；以对方中间为端点的仍要显式 T 节点，共线重叠仍拒绝。
- **读出已放符号：** `SymbolReference.SymbolVariant` 的 `SymbolLibraryName/SymbolName/VariantNr` 能读，`eplan_page_get` 的图形对象属性 `symbol` 给出 `库/名/变体`。示例页：断路器 `IEC_symbol/FA1/0`，指示灯 `IEC_symbol/H/0`，母线连接点 `SPECIAL/BCP/2`，T 节点 `SPECIAL/TLRU/0`、`TOUR/1`、`TLRO/0`，角 `SPECIAL/CO/1`、`CO/2`。
- **和其他页同名的中断点：** 新页的中断点和其他页同名时会接入原来的链，生成的连接跨到别的页（`FC2R:1 -> =GAB1+A1-FC1:1/L1`），`eplan_connection_create` 的预测只看本页，判为多出的连接而整个撤销。要先用 `generate:false` 放节点和中断点，再 `eplan_connection_generate`。原页的连接也随之从 9 条变成 12 条，因为链延伸到了新页。
- **对齐放置后要先生成：** 器件对齐放好后它们之间的线只是图形，没有连接；接着放节点时生成会补出这些连接，同样判为多出。先 `eplan_connection_generate`。
- **一段线属于两条连接时不能放定义点：** EPLAN 生成的 N 线是 `PF2R:X2 -> PF4R:X2`、`PF3R:X2 -> PF4R:X2`（原页是 `PF2-PF3`、`PF3-PF4`），`80..104` 这段两条都经过，工具拒绝在上面放定义点。连接的排列顺序怎么控制还没查明。— 第 5 道绘图题在会话里实画。

### A115. 中断点的箭头按电流方向（2026-11-07，EPLAN 2026.0.3.25421，Electric P8，示例项目副本；负责人指出）
- `SPECIAL/BP` 有 8 个变体 A–H（0–7），A、E 的两个连接点都是 Right/Left，C、G 是 Left/Right，B、F、D、H 是竖直的。工具省略 `symbol_variant` 时取连线在尾部的变体，箭头背离连线，是"流出"的画法：母线右端用 A，箭头朝右，名称在右。
- 母线左端的进线中断点要箭头指向页内：变体 E 箭头朝右，名称 `/-2L1` 在箭头左边，和示例页一样。示例页用的是 A 加属性排列（读回为 `BP/0`），显示相同；排列名称（`左,0°`）随界面语言变化，所以用变体 E。— 绘图题 5 实画。
- 竖直方向实测（2026-11-07）：B 箭头朝上、名称在上；D 朝下、名称在下；F 朝上、名称在下；H 朝下、名称在上。工具默认给顶端 B、底端 D，都是"流出"。所以进线端要指定：左端 E（4）、顶端 H（7）、底端 F（5）；右端 C（2）箭头朝左、名称在右，是按截图推断的。Codex 做绘图题 2 时，顶端 24V 用了默认的 B，箭头朝上，就是这个问题。
- **默认按电流方向（2026-11-07）：** 电流从左到右、从上到下，所以连线朝右或朝下离开的 BP 是进线端，工具省略 `symbol_variant` 时给 E（4）、H（7）；右端、底端仍取尾部变体 A、D（流出）。反向的电流再显式指定：右端进线 C（2）、底端进线 F（5）。这样模型不读说明也能画对。

### A116. T 节点的画法和自动连线的颜色（2026-11-07，EPLAN 2026.0.3.25421，Electric P8 与 Pro Panel，示例项目副本）
- **T 节点：** 示例页的 T 节点是 `TLRU/0`、`TOUR/1`、`TLRO/0`（变体 A/B，`FUNC_SYMBOL` 读出 `SPECIAL;0;64;0`），画成斜折角。工具省略 `t_node.variant` 时只在连接点都在中心的变体里选，结果是变体 8（`SPECIAL;0;64;8`），画成圆点。照着已有页重画时，把 `eplan_page_get` 读回的变体传给 `t_node.variant`。
- **自动连线的颜色来自电位：** 示例项目第 1 页的电位定义点上有 31005 `CONNECTION_ACL_COLOR`：L1/L2/L3 为 0（黑），N 为 5（蓝），PE 为 3，示例页的线正好是黑和蓝。官方帮助的连接属性继承顺序是项目、层、……、电位、……、连接定义点，后者覆盖前者。
- **试过不起作用的：** 定义点的 31004 颜色、31048 线型、截面写成 `1,5` 还是 `1.5`、导线部件（Pro Panel 上挂了 `LAPP.4150401`），线都还是红色；两边对应连接的 1175 个属性完全相同。
- **未解决：** 新页的连接经中断点接入同一电位后，按页生成连接、切换页面都没有让线变色。可能要整个项目更新连接，`eplan_connection_generate` 只按页生成、没有变化就回滚，现在做不到，没有验证。
- 示例项目定义点用的导线部件 `LAPP.4150401`（Multi-Standard SC 2.2 1X1,5 BK）在 Pro Panel 的 `EPLAN_parts.alk` 里有，在负责人的 Electric P8 部件库里没有。— 绘图题 5 对比。

### A119. 重建连接后自动连线采用电位颜色（2026-11-07，EPLAN 2026.0.3，Electric P8，scratch 示例副本）
- 工具画出的新页连接已到达 L1/L2/L3/N，电位定义设置了 31005 `CONNECTION_ACL_COLOR`（A116），连接的 1175 个属性与示例页全部相同，自动连线仍为红色。
- 通过 EPLAN 的 `generate` action 执行 `generate /TYPE:CONNECTIONS /REBUILDALLCONNECTIONS:1` 后，L1/L2/L3 变黑，N 变蓝，与示例页相同；加 `/PAGENAME:` 只重建一页也有效。未设置颜色的 24V 电位仍为红色。
- 原适配器调用 `Generate.Connections(targets, false)`，且连接读回未变化时回滚，显示刷新随之丢失。工具新增 `rebuild`（默认 false），传入 `Generate.Connections(Page[], bool)` 的 `bRebuildAllConnections`；true 时即使连接读回相同也提交，并返回 `rebuilt: true`。action 的结果是 2026-11-07 在 scratch 副本上实测的；同日用工具 `rebuild: true` 复验（CodexEval5b 第 9915 页）：顶部 L1–L3 由红变黑，与同一页用官方 action 重建的结果完全相同。该页底部的线仍为红色，因为它们接到的母线连接点 `-WE2:8` 不在 N 电位上，是数据问题，不是工具问题。

### A125. 自动连线穿过中断点、`ACBP` 断开，以及审批窗口被销毁后 EPLAN 一直被挡住（2026-11-08，EPLAN 2026.0.3.25421，Electric P8，从 `EES_Machine_Schematic_2026.zw9` 建的 scratch 项目 MX_Dryer23）
- **中断点 `BP` 的两个连接点在同一位置**（变体 0：(0,0) 朝右和朝左），自动连线会穿过它继续走。AC 220V 分配页 L 排（`-XD3`，出线朝下）和 N 排（`-XD4`，出线朝上）对齐放置时，上下相对的出线点穿过两排中断点连在一起，L 和 N 短路（连接列表里有 `XD3:1` 到 `XD4:1` 的连接）。
- **`SPECIAL/ACBP`（`eplan_connection_create` 的 `autoconnect_break`，要 `generate:false`）放在两排中断点之间**，生成连接后这一段不再画线，也没有连接；两边引脚到各自中断点的线照常。官方 EES_stacking_iec =GAB/5 的 24V、0V 箭头在 x=325 上下对齐也没有连线。
- **审批窗口被销毁却没走关闭流程：** MX_Dryer23 收尾时删除 `+M.M` 的审批窗口不见了，EPLAN 主线程一直停在 `Window.ShowDialog()` 的消息循环里（ClrMD：窗口对象 `_disposed=True`、`_isClosing=False`，`DispatcherFrame._continue=True`），之后所有调用都是 `EPLAN_BUSY`（Reentrant），只能结束 EPLAN。到期后再调 `Close()` 对已销毁的窗口不起作用。现在计时器发现窗口已不存在，或到期 `Close()` 之后对话框仍未结束，就用 `Dispatcher.ExitAllFrames()` 结束这个模态循环，结果按取消处理。谁销毁了窗口没有查到。

### A129. 电位连接点也是 `PotentialDefinition`（2026-11-10，EPLAN 2026.0.3.25421，Electric P8，绘图评测副本）
- **`SPECIAL/PCP` 电位连接点读回的 .NET 类型是 `PotentialDefinition`，和电位定义点相同。** Codex 绘图评测 `draw-review-fix` 用 `potential_point` 在页左侧放 L1、L2、L3、PE，`eplan_placement_delete` 删除它们时报“Cannot delete PotentialDefinition”；审图读引脚时按 A71/A86 排除了 `PotentialDefinition`，四根电源线的起点都报 `dangling_end`。按符号名区分：`SymbolVariant.SymbolName == "PCP"` 的保留连接点，其他（定义点）照旧排除。改后同一道题重跑，`dangling_end` 消失，审图 clean。

### A130. 修改已放置的电位定义（2026-11-10，EPLAN 2026.0.3.25421，Electric P8，scratch 副本 通过式烘芯炉）
- **按 `eplan_connection_create` 新建时的写法改属性可行：** `PotentialName`、`SignalName` 走对象属性，31004 颜色、31002 截面、33003 电位值写字符串，31006 电位类型写整数。`eplan_potential_update` 把 N、NC 两个电位定义的颜色从 `WH` 改成 `BU`，读回一致；对全部原理图页 `eplan_connection_generate rebuild:true` 后，IEC 60204-1 复审的 N 线问题从 10 处降到 0。
- 🔴 **第一次实机运行 EPLAN 在预览中崩溃（0xc0000374）：** 读取函数对每个属性各取一次 `point.Properties` 再释放，正是 A86 之后记下的“同一个包装对象不能取两次 `Properties`”。改成每个对象只取一次、在同一个 `using` 里读写所有属性后不再出现。写原生读写前先查 api-notes。
- 刚用 `eplan_project_open` 打开项目后，紧接的调用可能返回 `EPLAN_BUSY`（`Reentrant`），可重试；脚本按 `retryable` 等待重试即可。

### A134. 双端设备连接点的内侧引脚（2026-11-11，EPLAN 2026.0.3.25421，Electric P8，scratch 项目 喂丝机9 第 8 页）
- 施耐德 GV2P16 按部件放置后是黑盒（`SPECIAL/DC`），每个极是一个双端设备连接点 `SPECIAL/D2DCPNG`（例如 `-8FC1:1`，位置 120/248）。`eplan_api_try` 读它的 `GraphicalConnections` 得到两条：一条从连接点 (0,0) 到上方进线，一条从 **(0,−12)**（页面 120/236，盒子内部）到变频器 `-8TA1:L1` 的连接点 (0,3)。
- **同一个引脚，EPLAN 报两个位置。** 这个内侧引脚（序号 1）在 `GraphicalConnectionPoints` 里没有；从图形连接收进来以后，审图报它在 120/228 未连接，而另一条连接的端点在 120/236（0,−12），两处对不上。所以导线端点不能按坐标找引脚。
- 做法：连接场景也收图形连接两端的引脚（零长度的连接不收，它不画线，A72）；导线端点按“符号 + 引脚序号”取引脚表里的位置。实测 喂丝机9 第 8 页审图错误从 3 个降到 0 个。G120C 的 3 个网口引脚（总线端子）原来不在引脚表里，现在会报“未连接”警告，它们在这页上确实没有画线。
— `eplan_api_try` 读 `SymbolReference.GraphicalConnections` 及其起止 `SymbolRef`、`ConnectionPoint.Location`，结果在会话 scratch `direct/gc2.log`。

### B16. 电位定义点四字段类型（2026-11-04，安装版 2026.0.3 XML 与字典）
- `PotentialDefinitionPropertyList` 与字典一致：33003 `POTENTIAL_VALUE`、31004 `CONNECTION_WIRENUMBER`（Connection color / number）、31002 `CONNECTION_WIRECROSSSECTION` 返回 `System.String`；31006 `POTENTIAL_TYPE` 返回 `System.Int64`。`PropertyValue` 文档仅提供 Int32 整数隐式写入和 `ToInt()` 读回，故整数输入限制于该原生入口可表示范围，不经字符串写入。文档未给 31006 枚举值映射，本轮不推测数字对应的电位语义；四字段持久化和撤销仍待 Large 实测。

### A149. 电缆芯线按连接匹配读回（2026-11-08，EPLAN 2026.0.3，MX_Dryer18 的 =BG/14 页）
- **现象：** EPLAN 返回的芯线顺序与请求顺序不同。该页按端子代号返回芯线。
- **结论：** 核对芯线时，先按每根芯线所在的连接找到读回项，再比较芯线数据。
- **证据：** `src/EplanMcp.Bridge.Core/Operations/Devices/CableDefinitionPlanner.cs` 的 `CableDefinitionPlanner.VerifyCores`。Large `CableDefinitionTests.ThreeVerticalWires_DefineW9_ReadNativeCores_UndosRestore`（覆盖芯线读回）。

### A153. 同页的电缆芯线标记与普通导线编号（2026-11-16，EPLAN 2026.0.3，官方 =EC1+A3&EFS1/1 页）
- **现象：** 电缆芯线的编号是芯线标记。有定义点的导线在 `Wire.Cable` 中都读成 `<definition-points>`。部分普通导线有线号，盒内和插头内的导线没有线号。
- **结论：** 检查普通导线编号时，按电缆实际的芯线成员排除芯线。该页普通导线只部分编号，未编号项合为一条提示。
- **证据：** `src/EplanMcp.Bridge.Core/Operations/Pages/Review/PageChecks.cs` 的 `WireNumberCheck.Run`。Large `PageReviewNumberingTests.OfficialCircuitPages_AreNumberedWithoutPlaceholders`（覆盖编号检查）。

### A156. 额外放置属性文字 24300 会切换中断点的属性排列（2026-11-02，EPLAN 2026.0.3，放置中断点）
- **现象：** 中断点符号的默认属性排列会显示交叉引用，与 EPLAN 插入命令的结果一致。额外放置一段 24300 属性文字后，该点切换为用户定义排列。
- **结论：** 要保持符号默认的交叉引用显示，保留默认属性排列即可。
- **证据：** `src/EplanMcp.Bridge.Eplan/Adapters/Connections/EplanConnectionCreator.cs` 的 `EplanConnectionCreator.Prepare`。

# [EPLAN API 笔记](../api-notes.md)：符号放置与布局检查

### A147. 官方项目回归样本里的读数：属性文字外框偏长、`SPECIAL/DC` 黑盒、P&ID 页、PLC 盒内的图形组（2026-11-17，EPLAN 2026.0.3.25421，Electric P8，A 槽，8 个官方 EES 项目的 scratch 还原副本，只读）
- **属性文字外框比墨迹长：** `PropertyPlacement.GetBoundingBox(false)` 沿文字方向读得偏长。HVAC 示例 `==ASP01=SSA01++GEB01+F1&EFS/101` 上 `-KE11` 左侧右对齐的交叉引用文字，墨迹约在 x 25–39 mm，外框读成 [-5.55, 254.76, 39, 260.92]；`&EFS/22` 卡件上方的竖排文字墨迹止于约 290 mm（列号栏 292 mm 以下），外框到 301.8 mm。所以文字越出绘图区只能作提示，不能当错误。
- **黑盒：** 维修开关 `-QB1`（`&EFS/30`）是符号 `SPECIAL/DC/0` 的 `Function`，读出的类别不是 `BlackBox`，导线竖穿进盒子接盒内的触点。读取器现在把 `SPECIAL/DC` 符号也当容器。
- **P&ID 页：** 页型 `ProcessAndInstrumentationDiagram`（HVAC `==ASP01=RLT01++GEB01&PFB/1`）上的线是管道和设备图，宏边框、占位对象（type 106）、管道定义（type 234）互相交叠，管道端点不接引脚，电气布局规则不适用。
- **PLC 盒里的图形组：** 官方 PLC 与 Stacking 示例的 `=K3+B2&EFS/1`、`=K2+S1&EFS/1` 在 PLC 盒（`SPECIAL/PLCC`）里有一个 `Block`（type 144，`name` 为图形组名如 `144_1428091`，54 段线、弧、文字），是卡件的外观图形，完全在盒内。
- **宏边框属性文字的可见性冷热不同：** PLC 示例 `=K3+B2&EFS/1` 重新打开项目后第一次读页，宏边框（type 37）的属性文字（宏名 23001、描述 23004、变体、生成时间 23002 等）`PropertyPlacement.IsVisible` 读为 `false`，之后每次读为 `true`，其他字段完全相同；这些文字外框都是 [0,0,0,0]。对象摘要因此变化，第一次给出的绘图游标在第二段被判为过期。读取器把宏边框属性文字的可见性读成空值。
- **还原：** `eplan_project_create` 的 `backup` 模式能还原这 8 个 2022/2023 版官方 `.zw1`（5–22 秒）。项目名 54 个字符时 HVAC 示例还原失败，EplLog 记“移动数据库时出错”：项目内部文件路径超长，名字改短后成功。
— 证据：`tests/EplanMcp.Large.Tests/OfficialCorpusReviewTests.cs`，以及探查时导出的页面图片和逐对象读数（2026-11-17）。

### A140. 读对象必须给 project，端子按自身类别读（2026-11-14，EPLAN 2026.0.3.25421，Electric P8；scratch 项目 SmallRedraw_20261104171405264 只读）

- **`eplan_object_get` 省略 `project` 时按"当前项目"解析。** `eplan_project_open` 之后 `eplan_project_list` 里仍可能 `current` 全为 false，宿主便解析到另一个已打开项目，在那里按定位器找不到对象，返回 `PLAN_STALE`。带 `project`（项目名或定位器里的路径都行）即读到 50 条属性。工具现在在这个错误里附带 `objectsOfThisType`、`withThisNumber` 和 `differs`（project/page/digest），省得再猜。
- **`eplan_object_find` 用 `object_type: Terminal` 也会返回 PLC 类别的端子**，而读取适配器把这些描述成 `plcterminal`。`eplan_object_get` 原来用请求的类型拼属性目标，定位器校验因此 `check=not_found`，**31 个端子一个都读不出来**；同样的定位器用 `object_type: PLCTerminal` 每个读出 60 条属性。工具改为跟随对象自身的类别（`ObjectSummary.Kind`）决定属性目标和属性清单，并在 `hint` 里说明按哪个类型读的。
- 同一页 `eplan_object_find` 带页过滤与不带页过滤返回的 `digest` 完全相同（连接定义点 11/52、端子 24/213 逐个一致），所以摘要的身份摘要与页过滤无关。
- 我们自己建的连接定义点确实只有部件号（20100 `LAPP.4150400`），**没有** 20904 部件分配，也没有 20040 系列电缆数据；样本的同类点两者都有。`eplan_property_set` 的白名单不含 20904，也不收连接定义点作为目标，所以这一项不能用它验证。

### A53. 无部件符号与标识显示（spike S41，2026-10-27，EPLAN 2026.0.3.25421，Electric P8，只在 scratch 副本上）
详细数据见 [spike S41](../../../spikes/S41-symbols-without-part/README.md)。

- IEC_symbol 的线圈 `K`、常开触点 `O_EL`、两端符号 `AS` 不分配部件即可经 `Function.Create` 放置（只能走 `Transaction`，A13），`ArticleReferences` 为 0；设置 `Name` 后结构按请求，`VisibleName` 为空。功能文本只写一种语言时保留其他语言。— S41。
- `Function.Create` 默认只带一个空的属性放置（20029 索引 2）。`PropertyPlacement.Create(function, id)` 后的 `Location` **是相对父符号的偏移**（S42 截图与绝对外框 = 父位置 + Location 证实）：S41 按"父对象位置 + 偏移"设置时读回虽与请求一致，但文字实际画在页外；只设偏移才显示在符号旁，父符号移动时随之移动；`GetDisplayString()`：**20214/20213/20211/20215 显示带 `-` 的标识**（与页结构相同的段不显示），20058 不带 `-`（与 A26 相同），20212、20010 为空。画面效果待 S42 核对。— S41。
- `Transaction.Abort` 完全恢复且不留空撤销步；提交后关闭重开逐字段相同。— S41。
- 产品符号执行沿用此链，封装见 `FunctionTransactionScope` / `EplanDeviceSymbolPlacement`；相对偏移按 A54，Abort 前释放列表按 A55。产品 Large 测试已补，尚未运行。

### A55. 中断点、端子、触点代号、电缆芯线、PLC 连接点与位置盒（spike S43，2026-10-27，EPLAN 2026.0.3.25421，Electric P8，只在 scratch 副本上）
详细数据见 [spike S43](../../../spikes/S43-engineering-elements/README.md)。

- 🔴 2026-10-30，live T8b：提交后的定义线读回触发 `System.AccessViolationException`，EPLAN 进程终止；堆栈为 `PropertyUserHandle.= → UniversalPropertyList.getProp → FunctionPropertyList.get_Property → PrepareCables` 读回闭包 → `CableDefinitionExecution.Run`。该闭包保留了定义线事务内创建的 `Cable`。**在 `Transaction` 内取得的原生对象、属性列表及 `PropertyValue` 不得在提交后继续使用**；提交并结束作用域后，必须通过新的 `DMObjectsFinder`／页面枚举按对象号或名称重新查找，再在本次读回内取得并释放新属性列表；闭包只保留托管输入或标识。仍禁止 Abort 后读取或释放新对象列表，失败继续用已确认提交的撤销补偿。本次离线修复未运行 EPLAN，Large 全链仍待复验。
- 🔴 **写入失败、`Transaction.Abort` 之后，不要再读取或释放事务内新建对象的属性列表**：r2 在 Abort 后释放这些缓存时 EPLAN 原生堆损坏（`0xc0000374`）并崩溃，随后以 `/RestartOnCrash` 自行重启、只打开启动时的项目。须在 Abort 之前释放缓存，Abort 后不碰新建对象（同 A36/A39）。— S43。
- 中断点（SPECIAL `BPF`）用 `SymbolReference.Create` 放置，名称写完整标识，读回一致；端子（IEC_symbol `X`）先建端子排再建端子，名称 `<排>:<号>`；提交后单独生成连接（A52）。— S43。
- T7b Large（2026-10-27，2026.0.3.25421，Electric P8，LargeWrite 副本）：中断点配对（`CrossReferencedObjectsAll`）在生成连接的事务提交之后才可读，事务内为空；提交后同名一对能互相找到对方，但属性 24300 仍为空字符串，交叉引用文字不能从 24300 读取；`CrossReferencedObjectsAll` 的结果包含中断点自身（同对象号），须先排除。— `eplan_connection_create` 中断点。
- T7c Large 6/6（2026-10-28，2026.0.3.25421，Electric P8，LargeWrite 副本）：示例项目的结构设置使端子排和端子名称不含高层代号（请求 `=GAA+A1-XT3`，存为 `+A1-XT3`，端子为 `+A1-XT3:1`），查找已有排、查重复时须按此比较，否则会新建同名第二个排；事务内 `Terminal.TerminalStrip` 可能为空（提交后才建立），已有排时它返回的对象号与查找器得到的不同，按名称识别；放在已生成的连接上并再次生成后，连接按端子拆分；每次放置一个撤销步。— `eplan_device_place` 端子。
- T7d Large（2026-10-30，2026.0.3.25421，Electric P8，LargeWrite 副本）：每放一个 PLC 连接点，`eplan_plc_get` 除带连接点代号的通道（地址、手动符号地址、功能文本读回一致）外，还多出一个只有 PLC 器件名、无地址的器件级 PLC 端子；两个连接点共 4 个通道。读回的通道 `main` 均为 false；通道的 `Terminal.PLC` 为 null，虽然 PLC 的 `PLCTerminals` 列出了它，所属 PLC 取自所枚举的 PLC。写入手动符号地址（`Input_0`）后，计算符号地址按示例项目设置仍为 I/O 地址（`I0.0`）。给已放置的 PLC 连接点新增显示属性，事务内能读回，提交后页面上没有，原因待 spike，工具暂时拒绝。单独放置的触点默认不是主功能，`eplan_device_get` 找不到，新器件的功能须设为主功能；PLC 连接点属于已有 PLC 盒，不能设为主功能，否则脱离该 PLC。`??_??` 功能文本在未设显示语言的项目中可写入并读回。— `eplan_device_place` PLC。
- 验收试画（2026-10-30，2026.0.3.25421，Electric P8，LargeWrite 副本）：`Terminal.Create(PLC, SymbolVariant)` 会先在 PLC 所在页的原点 (0,0) 实例化一个连接点，再 `PlaceAt` 时按[文档](https://www.eplan.help/en-us/Infoportal/Content/api/2026/Eplan.EplApi.DataModelu~Eplan.EplApi.DataModel.Function~PlaceAt.html)另建一个功能，于是每个点在图框外原点留下一个可见的器件级 PLC 端子（T7d 记录的"多出的器件级 PLC 端子"即此）。改为 `Terminal.Create(Page, SymbolVariant)` 加功能定义、按器件标识归属 PLC 后，每点只有一个功能，地址、符号地址和文本读回正确，一步撤销（DevicePlaceTests 7/7）。`TerminalStrip.Create(Page)` 把端子排定义放在页面原点；`Create(Project, FunctionDefinition)`（类别 `TerminalDefText`）建立不放置的定义，端子照常归属。— 实机 Large。
- 同轮：`IEC_symbol/X` 端子的连接点在符号原点的 +2 mm 和 −0.75 mm，不可能同时落在 4 mm 栅格上；连线只能以引脚实际位置为端点（ConnectionCreateTests 13/13）。同轴相向的引脚在放置符号时即由 EPLAN 自动连接线连上，再画同路径的显式导线会与自动连接线冲突。`SYMB_DESC` 在 `IEC_symbol` 中有中文说明（例如 `SSD` "按钮,常开触点"、`SSRW` "旋转开关，常开触点，2 位置"、`SONOT2` "急停开关/急停按钮,常闭触点,旋转复位"，类别 Switch，示例项目的急停 `=F+A2-SG1` 即用 `SONOT2`）；`O_EL` 是"半导体/电子开关(PNP)"，不是按钮。— 实机读取（eplan_symbol_search、eplan_device_get）。
- 同轮（虚构页整页跑通）：电缆定义线的标识与型号文字总在定义线左端、右对齐放置，与 `Cable.Create` 给出的起止点顺序无关（反向给点结果相同），线起点离页边不足文字宽度时文字伸出页面；T 节点、角节点的全部引脚重合在节点中心；逻辑连接从器件经节点直达器件，一条连接可对应多段图形线；放置后同轴相向引脚的自动连接线须先生成连接，否则下一次生成会把它们一并生成；放好的端子在连接列表中引脚名为空。— 实机 Large（AcceptanceRedrawTests）。
- 客户 PLC 页试画反馈（2026-10-30，EPLAN 2026.0.3，scratch 副本）：五个中断点分别接既有端子、既有 T/角节点支路及 HMT；母线仍是 S5:11 为中心的原有星形，支撑电位点只连 SUP1。旧预测把无对端中断点当作空电位起点，因而要求母线连到 `<potential>` 并回滚；不能据此把独立支撑组并入母线。离线修正沿既有图形/逻辑证据分组、为中断点端点保留独立身份，并扣除未触碰的连接基线；Small 覆盖五点布局与读回拒绝路径，本次未运行 EPLAN。
- T7d 同轮反馈：两点请求的 20400/20403/20011/20301 显示均未出现在页面；反馈中点旁只见一个空属性放置（A53 记录的默认项为 20029）。旧校验只检查刚创建的 `PropertyPlacement` 对象，不能证明实际显示集合含有该项。显示循环没有被 PLC 分支跳过，且其后没有再次创建功能；[2026 PlaceAt 文档](https://www.eplan.help/en-us/Infoportal/Content/api/2026/Eplan.EplApi.DataModelu~Eplan.EplApi.DataModel.Function~PlaceAt.html) 说明对已实例化功能会另建功能，但尚无证据断言显示具体被挂到哪一对象或何时丢失。修订在全部写入后独立枚举已放置点的 `PropertyPlacements`（A57），逐项核对属性、父对象/页、相对与绝对位置、格式和 PLC 原生显示值；缺失即回滚报错，不再仅凭创建句柄报告成功。此次未运行 EPLAN，是否能实际保留四项显示仍以既有 Large 绝对位置/文本断言为准。
- 连接点代号写**带索引的 20022（FUNC_CONNECTIONDESIGNATION）**，按引脚生效；20038（全部代号）是汇总，写入无效；`Pin.Designation` 是内部编号，实际代号读 `Pin.Name`。— S43。
- 电缆芯线：导线 → 生成 → 电缆定义线 → 生成 → 单独提交的绑定 → 生成 → 芯线，电缆读回 3 个成员并精确分配，未用全项目重分配。— S43。
- 命名读回须兼容未使用的功能结构段被 EPLAN 丢弃（LargeWrite 的 `=GAA`）；电缆按相同规则比较完整请求与实际名称，保留位置段和设备标识的精确比较。T8b 仅新增离线实现与测试，最终生成和全部撤销步数尚待 Large 实测。
- T8b Large 后续反馈（2026-10-30，2026.0.3，LargeWrite 副本）：未分配电缆的连接名称读回裸 `+`；定义线阶段读回失败后，撤销已执行，原始指纹复核仍报 `PLAN_STALE`，不能据此断言业务内容未恢复。离线修正逐项记录读回字段与实际值；补偿按页面几何/种类、引脚几何与逻辑端点、电缆名称及芯线业务内容核对，忽略会话对象号/摘要，仅内容差异、无法完整读回或撤销失败报告 unknown，并在内部异常保留差异。本次未运行 EPLAN；完整生成与撤销 Large 仍待复验。
- T8b Large 再次反馈（2026-10-30，EPLAN 2026.0.3，LargeWrite 副本，Bridge 日志）：请求 `-W9` 的定义线实际名称为 `+-W9`，事务内读回因此失败。EPLAN 会写出空结构段；比较前去掉这些段（`+-W9`、`=+-W9` 均等价于 `-W9`），非空结构段仍有意义，沿用未使用的功能结构可丢弃这一兼容规则。
- 🔴 同次 live T8b：定义线事务按 `LockAllObjects → UndoStep → Transaction` 执行并 Abort 后，页面仍有位置 `(60,140)`、横向外框 `59.75..220.25` 的额外 Cable 及五个 PropertyPlacement。**`Transaction.Abort` 不能保证移除新建电缆定义线**；S43 未验证这一失败路径，不能套用 S41 的恢复结论。定义线先提交再回读；绑定与创建本地芯线模板的分配阶段同样在提交后校验，计入本次提交数后才能补偿，读回不符用已验证的原生撤销路径撤销该提交及此前提交，恢复核验通过才报 rolled_back。原生创建调用本身失败/取消而进入 Abort 时，保守报 unknown，不继续读取可能残留的新对象属性列表；提交或撤销不确定亦报 unknown。本次仅离线修复，未运行 EPLAN，Large 全链仍待复验。
- T8b Large 1/1（2026-10-30，2026.0.3.25421，Electric P8，LargeWrite 副本）：三根导线上定义 `-W9`（ÖLFLEX CLASSIC 100 300/500V，3 芯）→ 生成 → 绑定 → 生成 → 芯号与颜色 → 生成，共 6 个提交，芯线与导线一一对应，撤销全部恢复，EPLAN 未再崩溃。EPLAN 不给新建电缆加页的结构标识（只给 `-W9` 时存为 `+-W9`），工具按页结构写完整名称（`=GAA+A1-W9`），与器件一致。— `eplan_connection_create` 电缆。
- PLC 盒（SPECIAL `PLCC`）下的 DI 连接点地址、符号地址、通道读回正确；**每个连接点另有一个同名、无父对象的 PLCTerminal**，计数与核对须考虑。— S43。
- 位置盒（SPECIAL `SC`）可放置并改为虚线，但盒内符号的结构没有继承盒的位置（读回 `=+`），条件未查明。— S43。
- T7a Large 5/5（2026-10-27，2026.0.3.25421，Electric P8，LargeWrite 副本）：`LocationBox.GetGraphics()` 的图形外框是相对于盒原点的坐标（例 0,-40;40,0），页面范围以 `GetLogicalArea()` 为准；结构名经 `NameParts.DESIGNATION_LOCATION` 写入并读回；盒内已有器件不被改名；一次撤销恢复。— `eplan_device_place` 位置盒。

### A75. 布局检查的文字元数据与归属（2026-11-01，离线核对 2026.0.3 XML 和既有失败日志，未运行 EPLAN）
- 2026-11-01，负责人在 EPLAN 2026.0.3.25421、Pro Panel、LargeWrite scratch 副本、B 槽首次运行：PageReviewTests 7/9、PlacementMove 1/1、ConnectionCreate 14/14。`dangling` 修复放置 BAT `17/69839` 的实测外框 `[111.75,155.75,120.25,164.25]` 与 helper 唯一创建的 `DynamicConnectionLine`（`33/423455`）交叠，R2 返回 `INVALID_ARGUMENT/rolled_back`，建议位置 `[116,168]`；这是动态线被当作符号外框的产品缺陷，测试场景保持不变。后续负责人实跑 PageReviewTests 8/9（dangling 已通过）、PlacementMove 1/1、acceptance 13 阶段完成；`cable` 将定义线 `[[80,128],[160,128]]` 平移 `(0,64)` 后仍因 `EPLAN_OPERATION_FAILED/rolled_back`、`Placement closure, following text, pins or connections differ from the approved move (placement count)` 失败。移动时 EPLAN 仅部分整理定义点：新交叉 `(120,192)` 的点立即出现，旧交叉 `(120,128)` 的 `59/69845`（reader name 为 `+A1-WReview103`）及芯线归属待 `eplan_connection_generate` 才清理；之前要求旧点立即消失、新点恰有一个的检查过严。Cable 移动按前后 reader name 排除所属全部定义点的身份、数量与跟随比较，只核验定义线和自身文字按计划位移，其余放置、另一电缆的点、引脚和连接不变；结果警告先生成连接，再用 `eplan_page_review` 确认芯线归属（A64）。独立移动定义点仍严格校验。本次依据负责人提供的实机事实离线修复，未运行 EPLAN，修复后实机待复验。
- **`DynamicConnectionLine` 继承 `SymbolReference`，`DMClassId.InternalTypeId=33`，但不是布局符号、容器或穿通节点。** — `catalog/eplan-2026.0.3/types.jsonl` 的 `T:Eplan.EplApi.DataModel.DynamicConnectionLine` 及其 `DMClassId`。`EplanDrawingReader` 显式排除其 `layout_symbol` 和容器分类，`LayoutGuard` 按 kind 排除其 R2 符号身份；已有 `scene.Graphical` 或审查动态线读取保留作 R3/R4 线段，不新增线处理。`ConnectionNodeGeometry` 仍只接受 kind 为 `SymbolReference` 的节点。Small 复现上述 BAT 坐标和外框、线端接触，并保留真实符号重叠拒绝；本次未运行 EPLAN，实机待复验。
- **`Graphics.TextBase.Rotation` 单位为弧度，`-16002.0` 表示继承图层。** — 本机 `Platform/2026.0.3/Bin/Eplan.EplApi.DataModelu.xml`，成员 `P:Eplan.EplApi.DataModel.Graphics.TextBase.Rotation`（25259–25269 行）。读取先识别哨兵，再转为度、归一到 `[0,360)` 并保留 8 位小数；R8 按归一后的角度以 `1e-6` 度容差检查直角。
- **`TextBase.Height` 的 XML 只说明返回文字高度及读取失败异常，没有声明层继承值。** — 同一 XML 的 `P:Eplan.EplApi.DataModel.Graphics.TextBase.Height`（25215–25222 行）；同类的 Rotation、RowSpacing 明示 `-16002`。因此高度 `-16002`、其他非正值及非有限值均防御性读为 `from_layer`，不推断实际图层高度；这不是 Height 哨兵的实机验证。未知或 `from_layer` 的高度/角度仅在 `layoutChecks` 说明未验证，不形成违规。
- **既有失败日志包含默认文字与归属关系引起的误拒绝。** — `.local/failures/q-layout-LayoutGuardTests.log` 的准备文字 `[140,100,184.71,105.99025]` 被 R8 拒绝；DevicePlace 日志中 PLC 盒文字（type 45、oid 10232，外框 `[95.275,160.9193,104.725,164]` 等）与盒内功能（type 17、oid 69840，外框 `[80,152,120,181.25]`）触发 R3；AcceptanceRedraw 日志中同一设备 69888 的两个文字外框 `[77,207.7521004456824,88.253411666478613,216.25]`、`[84.3193,205.7,87.4,208.4]` 互相触发 R3。这里只核对已有证据，未复跑 Large。
- 对布局检查的影响：R8 仅拒绝本次明确设置且违规的文字属性；符号/库生成的默认格式仅警告。同一 owner 按项目、页、type/oid 识别，不能以文字各自的 digest 区分 owner；自身文字重叠作为警告保留供 review 查看。盒及其内对象（含双方文字）不作 R2/R3 障碍；容器归属使用 owner 的外框包含关系，不能把同一盒内的两个无关设备互相豁免。未改变的既有对象之间的重叠不拒绝，新增/移动对象与无关对象的冲突仍拒绝并搜索空位。
- 第三轮刷新日志（同日，负责人运行的 EPLAN 2026.0.3；本次只离线核对）：ConnectionCreate 14/14、DevicePlace 7/7；PlacementMove 的 type 17 / oid 69863 在 `(0,-20)` 移动中出现 12 个 `[0,-20,0,-20]` 外框；AcceptanceRedraw 的 PLC 阶段在 69874 与 69873 之间以 `[68,36,108,65.25]` 外框触发 R2；UnknownTextOutline 的写后拒绝缺少 `Suggested position`。— `.local/failures/q-layout-*.log`。全零外框必须在平移后仍保持未知，位置不可读的放置不参与 R1–R9，只报告未验证。
- `Function.ParentFunction` 返回同标识的主功能（主功能自身返回 null）；`PLC.PLCTerminals` 返回归属该卡的端子，`Terminal.PLC` 可能不可读或为 null（A55）。— 本机 `Eplan.EplApi.DataModelu.xml` 对应成员及 A55。布局读取保留可读的 PLC/黑盒父功能或卡成员归属；同一明确容器的成员及其文字不互作 R2/R3 障碍。这里的成员关系须有读取证据，单凭外框相同或几何上位于同一盒内不能豁免无关设备。
- 写后空位建议使用实测模型，按项目、页、type/oid 找到本次改变的父符号，连同其文字整体平移并重新运行同一套规则；不以 digest 判断父子归属。全零边界不移动；有界搜索耗尽时明确报告找不到空位。新增 Small 覆盖上述日志几何、归属与搜索，本次未运行 EPLAN，主 Large 测试及断言未改。
- 第四轮刷新日志（同日，负责人运行，本次未运行 EPLAN）：PlacementMove 1/1、DevicePlace 7/7、ConnectionCreate 14/14；UnknownTextOutline 已恢复页面，但随后撤销前一步的 action 退出码为 1。库符号分支在提交前测量失败后，经 `TransactionRunner` 执行 `FunctionTransactionScope.Rollback → Transaction.Abort`，不提交、不调用 `DoUndo`、不关闭空撤销步（S41）。发现两个原生包装器均未显式释放 `UndoStep`：本机 XML 的 `T:Eplan.EplApi.DataModel.UndoStep` 要求仅在 using 内使用，`UndoStep.Dispose` 是确定性析构。现改为 Abort/回滚后按嵌套顺序释放撤销步与 manager，不把未结束的协议留给终结器；Small 用假事务记录前一步、拒绝及下一次撤销，并检查释放顺序。实机撤销历史是否恢复仍待复验。
- 同轮 AcceptanceRedraw 中 type 59 / oid 69919、69920、69921 的实测外框分别为 `[86.75,158.75,89.25,161.25]`、`[118.75,158.75,121.25,161.25]`、`[150.75,158.75,153.25,161.25]`，对应 segment oid -21/-23/-25。目录 `T:Eplan.EplApi.DataModel.ConnectionDefinitionPoint.DMClassId` 的 `InternalTypeId=59`：它们是连接定义点。通过 `Connection.ConnectionDefPoints` 记录其所属连接的唯一已画路径；R4 仅豁免经过该点且沿所属线路的段。SPECIAL 节点的同位置多引脚须属于该节点，且方向支持整段路径；经过无关设备、只有外框相交、无归属证据或方向不符仍拒绝。上述精确坐标及反例由 Small 覆盖，主 Large 断言不变。
- 第五轮（2026-11-01，负责人运行）：真实 EPLAN 2026.0.3、Electric P8、LargeWrite scratch 副本中 LayoutGuard 4/4（撤销历史修正有效）、DevicePlace 7/7、ConnectionCreate 14/14、PlacementMove 1/1；仅 AcceptanceRedraw 的 `cables_after_generate`（`GeneratedCablesAsync` 第二次混合电缆调用）在提交后的 `LayoutGuard.AfterWriting` 中因 R4 返回 `INVALID_ARGUMENT/rolled_back`，由 `CableDefinitionExecution.Run` 撤销补偿。拒绝均为 type 59 连接定义点与实测图形段：69919 / -21 `[86.75,158.75,89.25,161.25]`，69920 / -23 `[118.75,158.75,121.25,161.25]`，69921 / -25 `[150.75,158.75,153.25,161.25]`，69922 / -27 `[182.75,158.75,185.25,161.25]`，69923 / -29 `[214.75,158.75,217.25,161.25]`，69924 / -31 `[246.75,158.75,249.25,161.25]`，69926 / -38 `[278.75,90.75,281.25,93.25]`；段号来自 `Graphical[20/22/24/26/28/30/37]`。七点均由先前 `wires_symbols_and_cables` 调用创建并绑定，该次提交且通过第四轮 PointRoutes 豁免；本次重绑改变序列化状态，故算作 changed，但没有读到所属路由。每个 2.5×2.5 mm 外框的中心都在自身导线上；连接由 X56:k（端子引脚无名称）到现场设备，X56:8 母线带 T 节点。日志尚不能确定 `ConnectionDefPoints` 归属、`scene.Pins` 端点键或 `DrawnPath` 唯一路径哪一步缺失；现离线记录 `PointRouteGaps` 及路径搜索计数并纳入指纹。未知所属路线且线段经过实测外框中心时仅返回携带原因的 `R4 unverified` 警告，经 `LayoutGuard.Summary` 汇入结果 `warning`；已知所属路线仍要求平行，擦边未过中心和无关功能外框仍拒绝。新增 Small 覆盖七点、缺失原因与反例，保留主 Large 断言；本次未运行 EPLAN，不宣称实机问题已修复。

- **T 节点的引脚不全重合。** — 2026-11-01，负责人在 EPLAN 2026.0.3.25421、Electric P8、LargeWrite scratch 副本中运行 `PageReviewTests` 的 `cable` 原生 helper 读取：页 `=GAA+A1&EFS1/103` 上 T 节点 oid 423458 的两根干线引脚在 `(120,160)`，分支引脚在 `(122,160)`；三段图形连接为 `(120,160) → (120,100)`（R103B / oid 69843）、`(120,160) → (120,220)`（R103A / oid 69842）、`(122,160) → (200,160)`（R103C / oid 69844），生成连接 72612 为 R103A → R103B、72613 为 R103B → R103C。结合 T3b 实测 T 节点约 3 × 4.5 mm 外框，`DrawnPath` 现按 `SymbolReference` 节点的实测外框确认全部引脚，并保留干线与分支的不同坐标；外框未知时仍只接受重合引脚，设备功能不作穿通节点。R4 共用此判定，仅放行在节点引脚处结束且方向相符的段；本次以该精确几何补 Small，未运行 EPLAN。
- 第六轮（2026-11-01，负责人机器，EPLAN 2026.0.3.25421，Electric P8，LargeWrite scratch 副本）：LayoutGuard 4/4（R1 探针按设计跳过）、DevicePlace 7/7、ConnectionCreate 14/14、PlacementMove 1/1；AcceptanceRedraw 12 个阶段全部完成，problems 为空，确认 41 个撤销步，仅 7 项 A55 PLC display_properties 按设计跳过；结果中 `R4 unverified` 警告为 0，T 节点按外框连通后，全部连接定义点都读到所属线路。— Large 测试 TRX 计数与 acceptance-report.json。
- 2026-11-01，负责人在 EPLAN 2026.0.3.25421、Pro Panel、LargeWrite scratch 副本、`feat/page-review` 运行 `PageReviewTests` 的 `cable`：修复步骤用 `eplan_placement_update` 将 Cable 定义线 `[[80,128],[160,128]]` 平移 `(0,64)` 到 `y=192`，由穿过 T 节点主干 `(120,160)–(120,100)` 改为穿过分支 `(120,220)–(120,160)`，被 `INVALID_ARGUMENT/not_started`、`/moves: A moved outline conflicts with an autoconnect line.` 拒绝。原因是规划器把所有移动外框都视为不得接触导线的实体；`Cable` 必须横跨其定义的导线，`ConnectionDefinitionPoint` 必须位于导线上，两者都不参与自动连接。现将这两个 reader kind 加入可移动白名单，并仅豁免移动外框与自动线相交的检查；其他类型、引脚和页界检查保留。LayoutGuard 的 `Symbol(p)` 已不含 Cable，Small 同时验证写后实测 R1–R9 不因该交叉拒绝；电缆成员归属仍由 EPLAN 生成连接后处理，再由 `eplan_page_review` 检查（A64）。本次仅离线修复和 Small 验证，未运行 EPLAN。
- 2026-11-01，负责人在 EPLAN 2026.0.3.25421、Electric P8、LargeWrite scratch 副本运行 `AcceptanceRedrawTests`：`cables_after_generate` 中 `eplan_connection_create` 创建 W4 定义线 `(272,92)–(288,92)`，型号为 `Fictional M1 control cable`；EPLAN 将标识及型号文字默认放在线左端、右对齐，owner `17/69867` 的型号文字实测外框 `[223.12,96.28,272,100.56]` 穿过自动线 `-37`（`(248,64)–(248,120)`），写后 R3 拒绝并回滚，未能进入后续 `cable_text_layout` 的 `property_moves` 调整。导线间距 32 mm、文字约 49 mm，默认位置不能避开导线。规则沿用 R8 的属性意图：本次创建或改变对象的文字若未由调用明确设置位置（含未知意图），其与线或其他对象的 R3 冲突只警告，点名文字、owner 和冲突对象，并提示 `eplan_placement_update property_moves`；有界搜索有解时附文字独立空位。显式文本创建、`property_moves`、带偏移的 `display_properties` 及直接移动文字仍拒绝。此次使用负责人提供的精确几何补 Small，未启动 EPLAN，实机复验见待验台账。
- 合并 main 后第七轮（2026-11-01，EPLAN 2026.0.3.25421，Electric P8，LargeWrite scratch 副本，A 槽）：LayoutGuard 4/4（R1 探针按设计跳过）、DevicePlace 8/8、ConnectionCreate 14/14、CableDefinition 2/2、PlacementMove 1/1；同日前一轮另有 DanglingInterruption 2/2、PlacementEditSpike 15/15。AcceptanceRedraw 13 个阶段（含 cable_text_layout）全部完成，problems 为空，确认 39 个撤销步，仅 7 项 A55 PLC display_properties 按设计跳过。验收夹具据护栏调整：端子排定义移到 (16,100)，W4 电缆文字旋转放到 (260,116)。— Large 测试 TRX 计数与 acceptance-report.json。

### A96. 符号变量和属性排列可以在放好后修改（2026-11-04，EPLAN 2026.0.3，LargeWrite）
- 主功能用 `Function.SetSymbolVariant(variant, false)`：部件 `SIE.3VA1032-2ED32-0AA0` 的 `Q_3LI` 变量 0→6，部件和连接点不变，一次撤销后快照还原。
- 中断点用 `SymbolReference.SymbolVariant`：`BP` 变量 0→4，一次撤销后快照还原。
- 写 `SymbolReference.PropertyPlacementsSchemas.Selected` 后，`Selected.Name` 和属性 19307 读回相同。可选排列随符号不同：`BP` 有 `左,0°` 等方向排列，普通线圈 `K` 只有 `默认`、`用户定义`。
— Large `PlacementSymbolEditTests`、`PlacementArrangementTests`、`PartVariantPlacementTests`。

### A105. 复刻一套 71 页图纸时验证的绘图和报表事实（2026-11-06，EPLAN 2026.0.3.25421，Electric P8，只在 scratch 新项目上）
- **从部件放端子并新建端子排：** `Function.Create` 在 `SafetyPoint` 中抛 S063176，A13 的结论再次成立；先 `LockAllObjects`、把新建挪到 `DeviceService.CreateDevice` 之前都没有用。整个调用改用 `FunctionTransactionScope` 后，`CreateDevice` 同样可用，160 个端子全部提交。端子排先建好，端子再按名称加入，和已有端子排的路径一样。
- **`CreateDevice` 放的端子自带可见名称，** 每个端子都显示端子排名。先清空 `VisibleName` 再 `NameService.AdjustVisibleName`，只有第一个端子显示，和图纸一致。
- **设备标识里的前缀在 `FUNC_PREFIX`（20012）：** `NameService.RenameDevice` 只写其他部分时，`-5Q1` 会变成 `-Q1`。要连同位置、高层代号、20012、20013、20014、20015 一起写。没有高层代号的页上，设备存成 `=+S1-5Q1`，空的 `=` 和 `+S1-5Q1` 是同一个标识。
- **宏的 `x,y` 放的是 `reference_point`，** 不是外框角点，由 `eplan_macro_get` 给出。宏里的盒子名称是 `-?K1`，没有主功能时改名不生效，要把盒子设为主功能。
- **中断点名称：** 项目的标识格式 10051 是 `<20012><20013><20014><20015>`，不含结构时，`=-L1` 会存成 `-=-L1`；这种项目要写设备部分。朝上的箭头要 `snap_to_grid:false`：位置是引脚，原点在引脚 +1 mm 的栅格上。
- **`SPECIAL/ACBP`（类型 ConnectionBreakpoint）** 让自动连线在此断开，打印时不显示。图纸上两个引脚相对却没有连线的地方用它。
- **总览和原理图的 PLC 点** 按卡、功能定义、插头、连接点代号配对。原理图的点要用总览同名点的功能定义，否则总览那一行是空的。地址写在总览点上。
- **项目设置 `TRANSLATEGUI.MISSING_TRANSLATION.SHOW`（以及 `.TEXT`，默认 `<<???>>`）** 为 false 时，缺译文的行不再显示占位符。
- **自由文字的插入点是文字的顶部。** PDF 给的是基线，实测 y = 基线 + 1.4 × 字高。一个文字只写一种语言；语言无关的文字在显示两种语言的页上会下移一行。
- **报表类型和表格扩展名：** `.f09` 是 `InterconnectDiagram`（cable diagram），不是 `CableConnectionDiagram` 或 `CableLayout`。同一轮还确认：`.f01` PartsList、`.f02` PartsSumList、`.f06` TableOfContents、`.f10` CableOverview、`.f13` TerminalDiagram、`.f14` TerminalStripOverview、`.f24` StructIdentifierOverview、`.f26` TitlePage。
- **表格必须在项目里，** 否则 `CreateReport` 抛 S029153。`Masterdata.AddToProjectEx(project, {文件名})` 从 `$(MD_FORMS)` 复制进来，和报表对话框的做法一样；系统主数据只读取。
- `PartsList` 用表格 `[EES]_F01_002_V01` 生成的后续页是子页（`/1.1`、`/1.2`），其他报表是 `/2`、`/3`。
- `SPECIAL/SC2`（结构盒，折线）的画笔来自符号，是点划线；`SetLogicalArea` 之后仍是矩形，图纸上标签处的切角没有自动出现。
- **端子和电缆的功能文本 20011 会显示在原理图上。** `X2_2` 只有“默认”和“用户定义”两种属性排列，默认就显示它，电缆定义线上的文字还会被顶上去一行。这份图纸的报表列了这些文字，原理图却不显示，图纸是怎么隐藏的还没查明，所以这轮没有写入。
- **注册插件：** `eplan-addin.ps1` 异步启动 EPLAN，要等它做完再启动下一个，否则加载的还是旧版本。EPLAN 按进程把插件影子复制到 `%APPDATA%\EPLAN\ShadowCopyAssemblies\<pid>`，比较哈希就知道加载的是否最新。

### A135. 删除和替换位置框（2026-11-11，EPLAN 2026.0.3.25421，Electric P8，scratch 项目 喂丝机9-框测试、喂丝机9 第 13、15 页）
- `LocationBox.Remove()`（`eplan_placement_delete`，按功能删除）删掉 `+M` 位置框后，框内设备的完整标识都不变（页上 21 个名称前后一致），页面上其余对象也没有变化，写后核对通过。随后在新范围放一个更宽的 `+M` 框，标识仍不变。
- 位置框不改变框内设备已有的位置代号；换框只是改图形范围。`eplan_placement_edit` 的删除对位置框报“Property child inventory is incomplete”。
- **不要删页重画**含已编址 PLC 点或别的页设备触点的页面：重放 PLC 点时，通道已有地址，工具报 `ALREADY_EXISTS`；被删的触点是其他页设备的一部分，再并回时报找不到设备（喂丝机9 第 13 页，已用删页前的备份恢复）。

### A136. Pro Panel 3D：柜体、导轨、线槽、器件和布置图（2026-11-11，EPLAN 2026.0.3.25421，**Electric P8**，scratch LargeWrite，Layout3DSpikeTests）
- **Electric P8 里可以用 E3D API**（本机授权含 Pro Panel 模块），用的是 P8 当前配置的部件库，不需要切换到 Pro Panel 变体。
- **安装空间：** `new InstallationSpace().Create(project, "A9")`，传入的字符串存进 36452 `INSTALLATIONSPACE_DESIGNATION`；不要带 `+`（带了会存成 `+A9`）。示例项目的空间 `Name` 为 `+A1`、36452 为 `A1`；新建的空间 `Name` 仍是 `+`，写 1200 位置属性抛 S063113，名称来源未查清。
- **柜体：** `new Cabinet().Create(space, part, "1")` 之后 `Parent` 为空、`IsPlaced` 为假；再 `cabinet.SetParent(space, false)`，父对象变成 InstallationSpace、`IsPlaced` 为真，和示例项目的柜体一致。柜体宏自带：柜体本体（5 面各一对 Master/Opposite Plane）、**柜门**（Component，最靠前的那面 Plane）、**安装板** `MountingPanel`（Plane Master/Opposite，基准约束 `MP`）、铰链和门锁。坐标：X 宽、Y 深（前门 Y 小，后部 Y 大）、Z 高，单位 mm。
- **实测柜体外形（宽×高×深）：** RIT.1034500 300×400×210、RIT.1380500 380×380×210、RIT.1055500 800×600×300（安装板 749×570）、RIT.1058500 600×800×250、RIT.8615000 600×1200×500、RIT.5830680 600×1800×400、RIT.5834600 800×2000×600。型号尺寸以这里的实测为准，不凭记忆。
- **导轨、线槽：** `new MountingRail().Create(project, part, "1", 长度)`、`new Duct().Create(...)`；源约束点（`V1..V4`、`M1..M4`）中第一个 `CanSnapTo(plane.BaseMate)` 的用 `SnapTo(plane.BaseMate, 0, x, y)` 吸到安装板 Master 面上，父对象变成 Plane。导轨的 `BaseMate` 是 `LineMate R1`，线槽有 `LineMate L1/L3`。
- **原理图设备进 3D（2026 没有 `Function3D.Place`）：** `Component.Create(project, rail, part, "1", -2, list)`（静态方法）建在导轨上，`M4` 约束吸到导轨 `LineMate`（`SnapTo(line, x)`），再把 `Function3D.Name` 设成设备标识，名称即生效。示例项目里已有的设备 3D 表示都已放置（3807 个，未放置 0 个），没有现成的“未放置”对象可吸。电缆（`EPL.2x6CABLE`）建不出 Component（S063177）。
- **门上器件：** 施耐德 `SE.XB4BS844415`、`SE.XB4BW31B5` 用 `Component.Create(project, 门 Component, part, "1", -2, list)`，源约束 `G1` 吸到门的 Master Plane，按 x/y 定位。
- **回滚：** 以上全部写在一个 `TransactionScope` 里回滚后，3D 对象数和安装空间列表与之前一致，包括中途调用了 action 的情况。
- **布置图（模型视图）：** `InsertModelViewAction /PROJECTNAME:<elk> /PAGENAME:<页> /LAYOUTSPACE:A1 /VIEWNAME:<名> /X /Y /DX /DY /STYLE:1 /VIEWPOINT:5 /SCALESETTING:0 /VIEWTYPE:1` 从外部（eplremote）调用成功，页上生成 1186 个图形对象；`LAYOUTSPACE` 写 36452 的值（`A1`），写 `+A1` 失败。目标页类型 ModelView 和 PanelLayout 都可以。**在插件的 action 里用 `ActionManager` 调同样的参数一律返回 false**（5 种写法、两种页类型）。
- **插件内的可行路线（2026-11-12 实机，自动喂丝机-3D试验 副本）：** 在 Bridge 调度器里（不在 action 回调里），`new ActionManager().FindAction("InsertModelViewAction").Execute(ctx)` 仍然返回 false，带不带 `ROOTELEMENTS` 都一样；同一个 `ActionCallingContext` 交给 `new CommandLineInterpreter().Execute("InsertModelViewAction", ctx)` 就返回 true——**但前提是本次 EPLAN 会话里 `ActionManager` 已经执行过它一次**（当天重启 P8 后只调 `CommandLineInterpreter`，连之前成功过的副本也失败；先调 `ActionManager`（返回 false、无改动）再调 `CommandLineInterpreter` 就成功，自动喂丝机两页布置图实测）。所以适配器两步都做，顺序固定，`OBJECTID` 形如 `42/190/35411/0`，可以放在 `TransactionScope` 里。`ROOTELEMENTS` 写安装板或门的 `FUNCTION3D_ID_RELATIVE`（36035，如 `15100.3.1;0;0`），视图只显示该面及其上的对象：安装板视图在页上加了 7 个对象，门视图加了 4 个。页类型 ModelView 用 `Page.Create` 新建即可（`eplan_page_create`）。`X/Y/DX/DY` 是页上的毫米框（着色视图的背景正好铺满它），模型在框里居中。**比例：** 从插件调用时 `SCALESETTING`/`SCALE` 不起作用，每个视图读回 `DMG_VIEWPLACEMENT_SCALE`（36510）都是 `1:10`；插入后按 `OBJECTID`（`<库>/190/<oid>/0`）在 `DMObjectsFinder.GetPlacements(null)` 里找到 `ViewPlacement`（它不在 `Page.AllPlacements` 里），设 `ViewScale = ScaleFit`（或 `ScaleManual` 加 `Scale`）再 `Update()` 就生效：`fit` 读回 1:4、1:5，`1:5` 读回 1:5。等轴测 `VIEWPOINT=7` + 隐藏线/着色两种样式，可以做出与官方 EES_rittal_3d 一样的整柜外观和透明内部视图。
- **删除 3D 对象（2026-11-12 实机，自动喂丝机-试排 副本）：** `Function3D.Remove()` 删掉导轨上的 PLC 设备、导轨和一台变频器后，`eplan_layout_get` 里这些设备回到 `unplaced`，名字不再在 3D 名单里；随后用 `eplan_layout_place` 把变频器放回原位成功。先删导轨上的设备，再删导轨。
- **柜顶安装面（2026-11-12 实机，自动喂丝机）：** RIT.1055500 的柜体由外层 `Cabinet`（放进安装空间的那个）和内层 `Cabinet` 组成，内层有 5 对 `Plane`（Master/Opposite，BaseMate 都是 `PlaneMate:MP`）：顶面 800×280 在 Z=297（水平，`SizeZ`=0）、底面、左右侧面、背面；门、安装板、底部法兰板各自另有面。挂在安装面上的对象，`Parent` 是那个 `Plane`，不是安装板或门本身。塔灯 SE.XVGKB3SW 用 `SnapTo(顶面 BaseMate, 0, x, y)` 吸附后按包围盒校正到 (80, 120)，柜顶以下伸出 18 mm（安装螺纹），高 268 mm。
— 证据：`tests/EplanMcp.Large.ConnectionHelper/Layout3DSpikeAction.cs` 的读回 JSON，trx `Layout3DSpikeTests` 2026-11-12。

### A151. 子设备的引脚可以落在所属设备的盒边（2026-11-16，EPLAN 2026.0.3，设备 -KK1 与子设备 -KK1-X11）
- **现象：** `-KK1-X11` 的引脚位于 `-KK1` 的盒边。
- **结论：** 检查符号重叠时，这两个标识属于设备与其子设备。
- **证据：** `src/EplanMcp.Bridge.Core/Operations/Pages/Review/LayoutChecks.cs` 的 `PlacementGridAndOverlapCheck.SameDevice`。Large `PageReviewLayoutRuleTests.OfficialCircuitPages_HaveNoLayoutErrors`（覆盖重叠检查）。

### A152. PLC 插头外框内的线端与符号连接点上的导线（2026-11-16，EPLAN 2026.0.3，官方 =F+A2&EFS1/1 页及 =EC1+A3&EFS1/1 页）
- **现象：** `=F+A2&EFS1/1` 上 44 × 34 mm 的 PLC 插头外框覆盖相邻插头的引脚，导线在框内的引脚处终止并连接。`=EC1+A3&EFS1/1` 上还有导线经过符号自身连接点并在该点连接。
- **结论：** 判断导线是否穿过符号时，要核对线端所接的引脚，以及线段是否经过符号自身的连接点。
- **证据：** `src/EplanMcp.Bridge.Core/Operations/Pages/Review/LayoutChecks.cs` 的 `LayoutRuleChecks.EndsInside`。Large `PageReviewLayoutRuleTests.OfficialCircuitPages_HaveNoLayoutErrors`（覆盖导线穿过符号检查）。

### A154. 部件总览宏的声明外框与实际绘图范围（2026-11-03，EPLAN 2026.0.3，示例项目的 PLC 总览页）
- **现象：** 部件总览宏可以占满 A3 页。宏声明的外框与 EPLAN 实际绘制的对象范围有别。
- **结论：** 页面尺寸已知且声明外框仍在页内时，放置后再测量对象；实际外框越出页面才回滚。
- **证据：** `src/EplanMcp.Bridge.Core/Operations/Placement/LayoutGuardChecking.cs` 的 `LayoutGuard.Checking.CheckPlacement`。Large `SmallProjectRedrawTests.SixteenPages_McpRedraw_ReportsEveryDifferenceAndKeepsArtifacts`（覆盖总览宏重绘）。

### A158. 已放置的端子排定义可以没有符号变体（2026-11-01，EPLAN 2026.0.3，绘图读取中的端子排定义）
- **现象：** 读取一个已放置的端子排定义时，没有符号变体。
- **结论：** 绘图读取时将该端子排定义排除在容器之外。
- **证据：** `src/EplanMcp.Bridge.Eplan/Adapters/Pages/EplanDrawingReader.Reading.cs` 的 `EplanDrawingReader.Reading.ReadPlacement`。

### A159. 安装板上 MoveRelative 的位移按父对象坐标轴解释（2026-11-12，EPLAN 2026.0.3，3D 安装板）
- **现象：** 在安装板上调用 `MoveRelative`，位移 (0, 0, 40) 使外框沿世界坐标的 −Y 方向移动 40 mm。
- **结论：** 按世界坐标修正位置时，先沿父对象的三个局部轴各试移 1 mm 并移回，用测得的轴方向换算所需位移。
- **证据：** `src/EplanMcp.Bridge.Eplan/Adapters/Placement/EplanLayoutPlacer.cs` 的 `EplanLayoutPlacer.Correct`。

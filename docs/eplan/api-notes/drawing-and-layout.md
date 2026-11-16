# [EPLAN API 笔记](../api-notes.md)：绘图、布局与连接

### A145. 图框评估区就是绘图区：起点 12003/12004，高为行高 12002 之和（2026-11-16，EPLAN 2026.0.3.25421，Electric P8，A 槽，示例项目 scratch 副本，只读）
- **示例项目的两种图框：** A3 横向图框（12033/12034 = 420×297）的起点 12003/12004 为 (4, 16)，12002 只有索引 1，值 276；12005 = 10、12006 = 1；按 `PlotFrame` 类型查到的这个图框 12102（列宽）**没有索引**，但原理图页（如 `=GAA+A1&EFS1/1`）实际读回的列宽之和为 412，绘图区是 [4, 16, 416, 292]。A4 纵向图框（210×297）的起点为 (0, 20)，12002[1] = 277。
- **与图纸核对：** 总览页导出成 PNG 后量过：标题栏上沿在 y = 16 mm，左边行标题条从 x = 4 mm 开始，顶部列号行在 y = 292 以上，十列分在 x = 4 以右。所以评估区 [12003, 12004, 12003 + 列宽之和, 12004 + 行高之和] 就是图框边线以内、标题栏以上、列号以下的可用区；不存列宽时宽度取到 12033。
- **注意：** 文档里 12005 写作“行数”、12006 写作“列数”（`FRAME_NUMBER_OF_COLUMS` 的说明也是 rows），与图纸上的 10 列 1 行对不上，不用这两个数推算尺寸。属性值是页面毫米，原点在左下（A3）。
- **产品实现：** `EplanDrawingReader` 读出 `PageDrawingGeometry.DrawingArea`，区域超出页面或读不出来时为 null。布局守卫 R1 优先用这个区域，读不到再用页面四边各减 `layout.marginMm`；`eplan_page_get` 的 `drawings.drawingArea` 告诉 AI 能画在哪里；`eplan_page_review` 的 `off_area` 检查越界。
— 证据：`tests/EplanMcp.Large.Tests/PlotFrameAreaTests.cs`，以及探查时的 `eplan_object_get`（PlotFrame）读数和 `&ADB1/2` 的导出图。

### A141. Pro Panel 3D 布线：清除后重新布线得到同样的长度（2026-11-14，EPLAN 2026.0.3.25421，Pro Panel；scratch 样例夹具 EPLAN_Sample_Project，全部在事务里做完后回滚）

- **入口：** `project.InstallationSpaces` 按 36452（安装空间代号，如 `A1`）选空间；`InstallationSpace.Connection3Ds` 列出空间里的 3D 连接；`Connection3D.RoutePoints` 是布线折线（`PointD3D`，单位 mm），`IsRouted`、`RoutingType`（`RoutingWithNetwork` = 1、`RoutingWithoutNetwork` = 2）说明怎么布的。样例 A1、A2 的 508 根全是 `RoutingWithNetwork`。
- **端点不一定是引脚：** 端点是"连接点位置"时，读 `StartPin`/`EndPin` 抛 `NotSupportedException` S063219，并提示改用 `Connection3D.StartConnectionPointPosition`；用它的 `DeviceTag` 和 `Index` 标识端点即可。`ConnectionPointPosition` 还有 `AdditionalLength`（附加长度），会影响最终长度，未实测。
- **布线是确定的：** 对每根已布线的连接先 `ClearRouteAndNodes()`（之后长度全部变化，证明清除生效），再按原 `RoutingType` 分组调用 `ConnectionService3D.RouteConnections(List<Connection3D>, RoutingType)`，按折线算的长度与原来相差不超过 0.5 mm 的：A2 **332 / 332**，A1 **173 / 176**。
- **布不回来的 3 根**（A1 的 `+A1-XD1:1/2/3` 到 `=GAA+A1-FC1`，原长约 2308 mm）清除后成为未布线。**[推断]** `ClearRouteAndNodes` 连设计人员手工设的布线节点一起清掉了，自动布线找不到原路线；样例第 1 页上的 `2.31 m`、`2.318 m`、`2.309 m` 正是这几根。复刻时要连布线节点一起放回。
- **回滚可靠：** `TransactionScope.Rollback()` 之后 508 根的长度全部回到原值。
- 3D 长度（mm）与原理图块值 `20201[10]`（m，3 位小数）怎样对应（是否加附加长度、如何取整）未核对，见 [数据关系 R5](../data-relationships.md)。
— 证据：`tests/EplanMcp.Large.ConnectionHelper/RouteCheckAction.cs`，trx `SampleRouteCheckTests` 2026-11-14（B 槽）。

### A140. 读对象必须给 project，端子按自身类别读（2026-11-14，EPLAN 2026.0.3.25421，Electric P8；scratch 项目 SmallRedraw_20261104171405264 只读）

- **`eplan_object_get` 省略 `project` 时按"当前项目"解析。** `eplan_project_open` 之后 `eplan_project_list` 里仍可能 `current` 全为 false，宿主便解析到另一个已打开项目，在那里按定位器找不到对象，返回 `PLAN_STALE`。带 `project`（项目名或定位器里的路径都行）即读到 50 条属性。工具现在在这个错误里附带 `objectsOfThisType`、`withThisNumber` 和 `differs`（project/page/digest），省得再猜。
- **`eplan_object_find` 用 `object_type: Terminal` 也会返回 PLC 类别的端子**，而读取适配器把这些描述成 `plcterminal`。`eplan_object_get` 原来用请求的类型拼属性目标，定位器校验因此 `check=not_found`，**31 个端子一个都读不出来**；同样的定位器用 `object_type: PLCTerminal` 每个读出 60 条属性。工具改为跟随对象自身的类别（`ObjectSummary.Kind`）决定属性目标和属性清单，并在 `hint` 里说明按哪个类型读的。
- 同一页 `eplan_object_find` 带页过滤与不带页过滤返回的 `digest` 完全相同（连接定义点 11/52、端子 24/213 逐个一致），所以摘要的身份摘要与页过滤无关。
- 我们自己建的连接定义点确实只有部件号（20100 `LAPP.4150400`），**没有** 20904 部件分配，也没有 20040 系列电缆数据；样本的同类点两者都有。`eplan_property_set` 的白名单不含 20904，也不收连接定义点作为目标，所以这一项不能用它验证。

### A139. 31047 说明连接的每个属性取自哪里（2026-11-14，EPLAN 2026.0.3.25421，Electric P8；16 页重画的样本与目标导出离线对比）

- 连接的 31047 `内部属性来源` 按**属性号**索引：`31047[31005]` 就是 31005 的来源。实测取值：`5` 来自电位，`9` 来自连接定义点或部件，`2` 来自层，`3` 由连接两端推算。诊断"属性值为什么不对"时先读它，比猜继承顺序快。
- 样本页上 31005 `自动连接线: 颜色` 的来源几乎全是 `5`（`=GAA+A1&EFS1/1`：40 条连接中 26 条），我们重画的目标里**一条都没有 5**，全是 `9` 或 `2`，值因此是 `-16002`（取自层）。两边的连接定义点都带 31005 `-16002`，电位定义点的位置和 31005/31006 也完全相同——差别只在 EPLAN 解析时认不认这条连接属于该电位。
- 根因是生成时序：驱动先 `eplan_connection_generate`，再放连接定义点和电位定义点，之后不再生成，所以连接的属性是在没有电位的状态下算出来的。A119 已记载普通生成不采用电位颜色、只有 `rebuild: true` 才采用；电位跟踪还会跨页（`=K1+A2&EFS1/2` 自己没有电位定义点，样本上的 4 条连接仍取自电位），所以重建要在全部页画完之后对所有页做一次，不能逐页做。
- 31002、31004、31048 的来源是 `9`，与样本一致：它们来自连接定义点的部件，不受此影响。

### A49. 页面文字、标题栏与图框（S38 r3，2026-10-26，EPLAN 2026.0.3.25421，Electric P8，只打开 scratch 示例副本）

- 一页上的文字分布在：页面自由文本 `Graphics.Text`、属性放置、功能文本、路径文本，以及图框的 `PlotFrame.SubPlacements`（示例页 15 段自由文本中 13 段在图框里）。按页替换文字时要区分来源，图框文字不属于页面。
- 页面自由文本和多语言文字的内容可以在 SafetyPoint 中修改并回滚；提交后一次 `XEsUndoAction` 撤销即恢复（当前项目，A41）。不弹对话框。
- 页的图框：`Page.PlotFrame` 可换成项目中已有的另一种图框，读回正确、回滚恢复。页属性 11016 为空时，图框来自项目设置。
- 产品实现（`eplan_page_update` 的 `plot_frame`，2026-10-26 Large `PageUpdateFrameTests`）：`Page.PlotFrame` 在 API 目录中为只读，切换通过把页属性 11016 设为图框名完成；写入后 11016 与有效图框都读回为目标图框，其他页不变；提交后当前项目一次 `XEsUndoAction` 恢复原状态，包括 11016 原为空、图框来自项目设置的情况。可选图框只取项目各页已在使用的图框。
- 图框显示的项目属性（如 10011 `PROJ_INSTALLATIONNAME`）可修改、读回、回滚；图框中页属性的绑定描述符读不出来源/作用域（`ObjectNotCreatedException`），不能据此判断标题栏显示哪个页属性。
- 插件型 spike 在 EPLAN 中运行时，`Assembly.Location` 指向影子副本而不是构建目录；不要用它定位仓库或输出目录。
— 协调人经 spike S38（`texts`、`findpage`、`replace`、`titleblock` 模式）实测，设计文件哈希与系统消息前后核对。

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

### A53. 无部件符号与标识显示（spike S41，2026-10-27，EPLAN 2026.0.3.25421，Electric P8，只在 scratch 副本上）
详细数据见 [spike S41](../../../spikes/S41-symbols-without-part/README.md)。

- IEC_symbol 的线圈 `K`、常开触点 `O_EL`、两端符号 `AS` 不分配部件即可经 `Function.Create` 放置（只能走 `Transaction`，A13），`ArticleReferences` 为 0；设置 `Name` 后结构按请求，`VisibleName` 为空。功能文本只写一种语言时保留其他语言。— S41。
- `Function.Create` 默认只带一个空的属性放置（20029 索引 2）。`PropertyPlacement.Create(function, id)` 后的 `Location` **是相对父符号的偏移**（S42 截图与绝对外框 = 父位置 + Location 证实）：S41 按"父对象位置 + 偏移"设置时读回虽与请求一致，但文字实际画在页外；只设偏移才显示在符号旁，父符号移动时随之移动；`GetDisplayString()`：**20214/20213/20211/20215 显示带 `-` 的标识**（与页结构相同的段不显示），20058 不带 `-`（与 A26 相同），20212、20010 为空。画面效果待 S42 核对。— S41。
- `Transaction.Abort` 完全恢复且不留空撤销步；提交后关闭重开逐字段相同。— S41。
- 产品符号执行沿用此链，封装见 `FunctionTransactionScope` / `EplanDeviceSymbolPlacement`；相对偏移按 A54，Abort 前释放列表按 A55。产品 Large 测试已补，尚未运行。

### A54. 文字、图形、移动与打开页（spike S42，2026-10-27，EPLAN 2026.0.3.25421，Electric P8，只在 scratch 副本上）
详细数据见 [spike S42](../../../spikes/S42-graphics-move-view/README.md)。

- `Text.Create(page, MultiLangString, height)`、`Line.Create(page, p1, p2)`、`Rectangle.Create(page)` + `SetArea` 可在 `SafetyPoint → UndoStep → QuietModeStep` 中提交，几何读回与请求一致；一步撤销，失败回滚无空步，重开持久。— S42。
- 只含 en_US 或 en_US+de_DE 的自由文字在中文显示语言下**不显示**，语言无关文字（`??_??`）显示：写自由文字须包含项目显示语言或语言无关。— S42。
- 移动：文字、矩形、功能用 `Location`；直线须同时设 `StartPoint`、`EndPoint`。`PropertyPlacement.Location` 是相对父符号的偏移，父符号移动时属性文字随之移动。— S42。
- T5b Large（2026-10-28，2026.0.3.25421，Electric P8，LargeWrite 副本）：移动功能及其属性文字、自由文字在一个 `SafetyPoint` 事务中提交，属性文字随父符号移动，范围外对象不变，一次撤销恢复。内容为空的属性文字外框读回 `0,0,0,0`，边界检查改用其位置。移动后重新生成连接若无变化，原先仍留下空撤销步（下一次 Ctrl+Z 无效），已改为不提交。— `eplan_placement_update`。
- `Edit.OpenPageWithName(项目路径, 页名)` 在编辑器中打开该页并返回 `Page`，不写项目文件、不留撤销步；编辑器打开着时，提交后的修改**自动刷新**到画面。打开页时 EPLAN 加载部件库和宏库，634 个部件引用的 `ObjectIdentifier` 全部改号（数量、内容不变）：部件引用的对象号不能作稳定标识。— S42。
- 打开页会让该项目成为当前项目；编辑器开着时，关闭再重新打开其他项目（ProjectOpen），当前项目仍是编辑器所在的项目。要切换当前项目，就在目标项目里再打开一页。`XEsUndoAction` 作用于当前项目，撤销前必须核对（A41）。— `eplan_page_open` Large 实测，2026-10-27。
- `ExistingIds` 会返回索引为 0 的带索引属性（如 1122），按普通属性读抛 `IndexedPropertyException`（S063172）。— S42。

- T4b Large 2/2（2026-10-27，2026.0.3.25421，Electric P8，LargeWrite 副本）：示例项目未设置显示语言，此时只有语言无关 `??_??` 文字可见；`??_??` 与具体语言同时写入一个 `MultiLangString` 时 EPLAN 只保留具体语言，读回缺 `??_??`，因此两者不得混用。字高 3.5 mm、位置读回与请求相同；一次撤销恢复；强制读回不符时回滚且不留撤销步。
- T4c Large 2/2（2026-10-27，同上）：文字、直线、矩形在一个 `SafetyPoint` 事务中批量创建并逐项读回后提交，一次撤销恢复；中途失败整批回滚不留撤销步。`eplan_page_get` 报告的直线/矩形外框含线宽，比几何端点各向外约 0.5 mm。— `eplan_placement_create`。

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

### A57. 函数的属性放置是 ContextPropertyPlacement（M8 T2b 实机，2026-10-27，EPLAN 2026.0.3.25421，Electric P8，只在 scratch 副本上）
- `Function.PropertyPlacements` 返回的是 `ContextPropertyPlacement`（`PropertyPlacement` 的子类），**它们的 `ToStringIdentifier()` / `ObjectIdentifier` 与父功能相同**，也不出现在 `Page.AllPlacements` 中。按标识去重会把它们全部当成父功能而丢掉；须按父对象 + 显示的属性 + 序号区分。新建的 20214 放置同样如此，画面上正常显示（导出图片核对）。— T2b。
- 经 `eplan_device_place` 的库符号分支放置无部件线圈：标识 20214 显示带 `-` 且位于“父位置 + 偏移”，连接点代号经带索引 20022 写入并显示，重复标识拒绝，一次撤销恢复。— T2b。

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

### A84. 报表块读取与按报表块生成报表页（2026-11-03，EPLAN 2026.0.3.25421，Electric P8；示例项目夹具只读，生成只在 scratch 副本 LargeWrite）
- `Page.ReportBlock` 能读出报表页当初的生成设置（`Type`、`FormName`、筛选/排序方案名称与数据、`DeviceTag`、`DeviceTagNameParts`、页描述设置）。示例项目 7 页的读数：
  封面 TitlePage（17）`F26_004_Sample project`；目录 TableOfContents（14）`F06_002`；PLC 图表 `F19_001`，器件 `=K1+A2-KE1`/`-KE2`（各 1 组名称部分）；
  端子图 TerminalDiagram（9）`F13_008`，筛选 `Location designation_A3`，器件 `+A1-XD1`（名称部分 1200=A1、20013=XD、20014=1）；部件汇总表 `F02_006`，筛选 `Connections_A3`；
  电缆总览 `F10_003`，筛选 `Location designation_A3`、排序 `DT (full)`。读取后夹具文件没有变化。
- `ReportBlock.DeviceTag` 只读，可写的是 `DeviceTagNameParts`（`FunctionBasePropertyList[]`）；`InvalidScheme` 在方案名无效且方案数据为空时抛出（本机 XML）。
- 四项方案数据为原始字符串：`FilterSchemaData`、`SortSchemaData`、`FunctionFilterSchemaData`、`FunctionSortSchemaData` 分别保存报表/功能的筛选或排序条件。名称属于各项目自己的设置，不能假定来源项目的名称在目标项目中存在。依据：本机 EPLAN 2026.0.3 的 `Eplan.EplApi.DataModelu.xml` 上述四个属性及 `Eplan.EplApi.HEServicesu.xml` 的 `Reports.CreateReport(ReportBlock, String)`；后者明确仅在方案名称无效且数据为空时抛出 `InvalidScheme`。
- 2026-11-03 03:39，负责人提供的正式工具 Large 失败：`ReportCreateTests` 在 LargeWrite 创建目录通过；端子图输入 `type=TerminalDiagram`、`form=F13_008`、`filter_scheme=Location designation_A3`，未传方案数据，返回 `S029131 配置无效。参数名称:'esFilterSchemeName'。参数值:'Location designation_A3'。请使用下列配置中的其中一个:'Anlage, Einbauort'。`。此前探测成功时同时设置了 `FilterSchemaData`；因此正式读写工具须携带数据。此次仅核对本机 XML，不运行 EPLAN；修复后的端到端生成与撤销仍待验。
- `ReportBlock.Create(project)` 设置上述字段后 `Reports.CreateReport(block, 首页名)` 在一个事务内生成并提交：目录用 `F06_002` 生成 12 页（`&AAB1/102` 起）；
  端子图用 `F13_008` 与原筛选方案生成。按 `DeviceTagNameParts` 限定到 LargeWrite 的一个端子排（1200=A1、20013=FC、20014=2）**没有起作用**，生成了项目中全部端子排的端子图（26 页）。限定方式待查，产品首版按项目范围生成。
— Large `ReportBlockSpikeTests`（证据 `%TEMP%\eplanmcp-spikes
eport-blocks-*.json`）。

### A138. 属性文字的父对象多数成组，文字自身从不成组（2026-11-14，EPLAN 2026.0.3.25421，Electric P8；scratch 项目 SmallRedraw_20261104171405264 只读）
- `eplan_page_get include_drawings` 的 `properties` 子对象给出 `grouped`、`angle_deg`、`docked`、`visible`、`property_id`、`displayed_property_id`、`property_index`、`height_mm`、`height_raw_mm`、`layer`、`pen_*`、`alignment`；`angle_deg` 与 `angle` 并列，`bounds` 与 `location` 在同级而不在 `properties` 内。
- 三页共 570 个属性文字，`grouped` **全部为 `false`**，角度全为 0 或 90，外框四值齐全，定位点二维。父对象的 `grouped` 则因页而异：`=K1+A2&EFA1/1` 的 224 个全部成组（Terminal 200、BusTerminal 16、PLC 8）；`=K1+A2&EFS1/2` 223 个中 68 个成组；`=K1+A2&EFS1/1` 123 个中 33 个成组。由宏插入的器件保持成组是常态，因此 `property_moves` 不再检查父对象的 `grouped`（见 [eplan_placement_update](../../tools/eplan_placement_update.md)）。
- 属性文字的 `parent` 定位器与自身 `oid` 相同、只有 `digest` 不同（A57 的共享对象号）；按 `oid` 找父对象时必须同时要求对方不是属性放置。
- 同一父对象的多个属性文字**共用同一个对象号**：原生导出里 `=GAA+A1&EFS1/1` 的 562 段属性文字中，号 396 出现 144 次、9550 出现 88 次；其中 132 段的 `oid` 与 `parentOid` 相同，且这个号同时是某个父对象的号。所以按对象号建立"对象 → 配对身份"的映射时不能登记属性文字，否则父对象的条目会被最后一段文字覆盖，两个项目里的"最后一段"还不一样（2026-11-14 实机：16 页重画有 40 段文字因此只在 `parentOid`、`sourceOid` 上不同）。
- `eplan_page_get` 在项目未打开时返回空的 `drawings` 而不报错，`eplan_page_create` 同样情况明确返回 `PROJECT_NOT_OPEN`；读取路径的这一不一致使"读到空"与"页上确实没有对象"无法区分。
- 由 `eplan_page_get` 得到的定位器直接交给 `eplan_object_get` 返回 `PLAN_STALE`，须先经 `eplan_object_find` 取新定位器。**更正（2026-11-14）**：本条原先还写了"`eplan_object_find` 自己的定位器也被拒"，那是探查脚本的错——当时省略了 `project`，而 EPLAN 里没有当前项目（`eplan_project_list` 里 `current` 全为 false），宿主于是解析到了别的项目。带上 `project` 后读取正常，见 A140。
- 定义点必须落在已有连接段上：空白页上调用 `eplan_connection_create` 放 `definition_point` 返回 `/items/0/position: Place the definition point on a segment of this connection. Nearest existing segment: none.`
— 探查脚本与日志：`probe3-text-cable.py`、`probe7.log`（会话 scratchpad，未入库）。

### A74. 新建属性文字的外框偏移（2026-11-01，EPLAN 2026.0.3，Electric P8，已有 scratch 失败日志）
- **刚创建后读取的属性文字外框可能偏移一个文字高度，直到 EPLAN 重新计算；移动后，90° 文字的 X 外框报告为 `[x-h,x]`。** `CableDefinitionTests.ThreeVerticalWires_DefineW9_ReadNativeCores_UndosRestore` 中属性 20040/序号 4 的批准位置为 `[68,160]`、角度 90°、未停靠，预测外框 `[59.4425,109.24,63.72125,160]`，读回为 `[63.72125,109.24,68,160]`；位置、角度、文字、属性选择及父对象不变，尺寸相同，X 偏移恰为文字高度 `h=4.27875 mm`。整页验收在 `[64,156]` 复现同一偏移。— 证据：`.local/failures/q-d46-CableDefinitionTests.log`、`.local/failures/q-d46-AcceptanceRedrawTests.log`；本次只读既有日志，未运行 EPLAN。
- 对移动读回的影响：已执行的 PropertyPlacement 继续逐项精确核对位置、角度、文字/显示值、属性 ID/序号、父对象及停靠状态；外框按批准旋转后的宽高比较（容差 0.01 mm），结果保留实测外框，供写后布局检查使用，不能以创建后外框的绝对偏移判定移动失败。

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

### A87. 完整一致重绘的样式、定义点与宏框（2026-11-03，EPLAN 2026.0.3.25421，Electric P8）
出处：scratch 的 `ExactMatchSpikeTests`，以及工具的 Large 测试（2026-11-04，第 1 轮在 Electric P8、第 2 轮在 Pro Panel，全部通过）。
- 图形和文字的 `Placement.Pen` 先取副本、修改 `ColorId` / `Width` / `StyleId` / `PatternLength` / `LineEndType`，再赋回；`-16002` 是取默认值标记，可用于颜色、线宽和线型并读回；`PatternLength` 的默认值是 `+16002`（此时 `StyleFactor` 为 -16002）；`LineEndType` 是 sbyte（0..2），无法表示该标记。`PatternLength=8` 读回 `StyleFactor=-8`，`StyleFactor=2` 读回 `PatternLength=-2`，两者联动，工具只开放 `pattern_length`。— 实测
- 图层经 `placement.Layer = project.LayerTable.Layers.First(l => l.Id == id)` 写入，读回 `LayerId`；不存在的 ID 应报告参数错误。`Polyline.Closed=true` 不改变顶点。矩形角度和 `TextBase.Rotation` 使用弧度，工具以度换算；`TextBase.Justification` 使用原枚举名（如 `MiddleLeft`、`BaseLineLeft`、`MiddleCenter`、`BaseLineCenter`、`FromLayer`）。旋转外框以 EPLAN 读回为准。— 实测
- `ContextPropertyPlacement` 的 `Rotation`、`Justification`、`Height`、图层及画笔可写可读；字高 `-16002` 表示默认。页面打开且启用“显示不可见元素”时，`IsVisible=false` 可读回 true，XML 仍记录隐藏；因此工具对 `visible:false` 不比较可见性读回并在结果说明，true 正常比较。— 实测
- `ContextPropertyPlacement.Location` 和默认的 `GetBoundingBox()` 均相对所属对象；加所属对象位置与绝对坐标一致（`relativePlusOwnerMatchesAbsolute:true`）。显式 `GetBoundingBox(false)` 返回绝对坐标，不能再次加所属对象位置。`property_moves.x_mm/y_mm` 继续表示页面绝对坐标，适配器负责换算。— 实测
- `Insert.WindowMacro` 返回的 `MacroBox` 可在同一插入事务内调用 `Placement.Remove()`；总览宏移除框后 195 个被引用对象全部保留，其余对象数量不变，一次撤销仍移除整个插入。`macro_box:true` 沿用 `InsertMacroBox.FromProjectSettings`。— 实测
- `PotentialDefinition` 没有可用的 Create（见 A86）；净化窗口宏可经 `Insert.WindowMacro(path, RepresentationType.MultiLine, 0, page, new PointD(x,y), Insert.MoveKind.Absolute, NumerationMode.None)` 插入，同一事务删除返回的宏框并设置 `PotentialName` / `SignalName`。宏参考点为对象中心；插到 (100,100) 的外框为 98.75..101.25。资源写临时文件供调用，用后删除。— 实测
- `ConnectionDefinitionPoint.AddArticleReference(string, string, uint)` 可写入部件；`ArticleReferences` 实测可读，与 XML 所称“始终抛异常”不符。只有部件而无颜色、截面、标识的连接定义点有效，示例 76 个定义点中存在此类对象。— 实测
- 定义点的部件要在放置事务提交之后写：同一事务里 `page.AllPlacements` 查不到新建的定义点，`new Function()` 包装上的 `Function.AddArticleReference` 抛 `CannotAddArticleException`（S063074）。工具在提交后、生成连接前用 `ConnectionDefinitionPoint.AddArticleReference` 写入，一步撤销不变。— 实测
- 矩形绕左下角旋转，`UpperRightCorner` 读回转动后的坐标（30° 时外框实测吻合；示例里的 2π 只有舍入差），所以工具按转动后的坐标核对。— 实测
- 新放功能的属性文字旋转读回 -16002（`from_layer`），示例另有 15 个保持这个值，所以 `property_moves.angle_deg` 也接受 -16002。— 实测
- `Arc.SetEllipse` 的角度是弧度，读回用 `FirstRadius`、`SecondRadius`、`ElipseAngle`，`Kind` 为 `Ellipse`；`Arc.SetCircle` 读回 `Kind` 为 `Circle`。`PathText.Create` 接受高度 -16002 并原样读回。— 实测
- 未定原因：2026-11-04 00:42 Pro Panel 在 `PropertyStyle` 测试收尾（工具返回 `INVALID_ARGUMENT` 之后）崩溃一次，`0xc0000409`，`ucrtbase.dll`，快速失败代码 7。修掉那次参数错误后复跑通过，没有再现，EplLog 里也没有相关记录。再次出现时先取转储。

### A88. 第三轮重绘工具：分组、删除与提交后读回（2026-11-04）
- 依据负责人提供的 `ExactMatchSpikeTests` 实测记录：`new Block().Create(page, Placement[])` 后 Count 等于图形成员数，Name 可写可读，块外对象不变；成员坐标变为相对块且对象号改变。工具每次仅建一个块，提交后重新取得定位器；块子对象标记相对坐标，绝对 Location/Bounds 留空而不猜测变换。— 实测（`BlockCreateTests`）。
- 安装版本 XML 的 Block.Create 要求至少两个成员，最多一个 SymbolReference；Block.Count 不计该 SymbolReference。前述 Count 等于成员数适用于图形成员；含符号的组合不据此推定。— 官方本地 XML 签名与说明；扩展组合待实测。
- 多语言页描述通过单次 MultiLangString 写入并逐语言读回；PLC 盒重命名依 A85，用 NameService.RenameDevice，提交后按新标识查找，禁止沿用未放置功能 ID。— 实测（`PageDescriptionMapTests`、`MacroOverviewTests`）不变。
- `Placement.Remove()` 用于独立 Line/Arc/PolyLine/Rectangle/Text/PathText/Image/MacroBox 的删除；A87 已验证宏框删除不删除引用内容。线和文字的工具链、一事务和单步撤销已实测（`PlacementDeleteTests`）。
- 写前 layout_guard=warn 仅把非 OffPage 布局违规降为警告；写后仍遵循 ADR-0046。— 实测（`LayoutGuardWarningTests`）：穿过普通符号的线默认被 R3/R4 拒绝，warn 时提交并告警，超出页面仍拒绝。线穿过黑盒外框本身不触发拒绝。
- `ParseNameAndProps` 对不含高层代号或位置代号的标识（如 `+A1-WE1`）返回空值，把空值赋给 `FunctionBasePropertyList.DESIGNATION_PLANT` 会抛 `EmptyPropertyException`（S063111），所以只赋非空部分。`RenameDevice` 之后主功能立即显示实际名称；不使用某部分标识的器件类型（如 `BusBarDefText`）把 `=F+A1-WE19` 变成 `+A1-WE19`，要在提交前发现并回滚。提交后再撤销未能恢复原标识，所以回滚后要核对原标识还在。— 实测（`WriteSafetyTests.Q2`）

### A89. 第四轮重绘：延后生成、组成员与多表达（2026-11-04，EPLAN 2026.0.3，负责人实测反馈）
依据：负责人提供的第三次重绘报告 `report-20261104120419136-ec5bf3a2.json`（只读、不入库）和本轮任务记录；新增工具链的 Large 于 2026-11-04 在 Pro Panel、LargeWrite 上全部通过（`DeferredConnectionTests`、`MacroGroupedPlacementTests`、`PlacementRotationRound4Tests`、`PlcRepresentationTests`、`EmptyMacroBlockTests`、`PlacementLanguageRound4Tests`、`EmptyConnectionDefinitionTests`、`MacroPositionTests`、`PlcModuleMergeTests`），回归 `DevicePlaceTests`、`ConnectionCreateTests` 同样通过。
- 每放一个节点即生成会加入计划外自动连接；报告调用 926 的 T 节点为 (96,248)、Up/Down/Right、variant 1。`generate:false` 只放五种节点/电位对象，略过逻辑端点、路径/自动线冲突和中断配对页校验，最后显式生成。— 报告失败已核对；分步生成、读回和撤销已实测。
- `MacroBoxSpikeTests` 的宏对象与示例页对象位于嵌套 `Group`；普通 `Placement.Group` 非空不等于块成员，块成员由绘图的 `parent` 指定。— 负责人实测反馈；PXC.2861344 组内线建块、删除与撤销已实测。
- 原生整圆可为 `Kind=Circle` 且 StartAngle=EndAngle=0、π/2 或 π。等角使用 SetArc 保留角度，0..360 保持 SetCircle；矩形/文字的 -16002 表示取图层角度，应原样写入。— 负责人实测反馈；报告请求已归一为0..360，180..180 与 -16002 已实测。
- 同名 PLC 总览/电路表达同时存在，含 `=K1+A2-KE1:1.1` 与 BusTerminal `:RJ45`；报告调用1339/1340被重名校验拒绝。— 已核对报告；允许非主表达与同名地址复用已实测（RJ45 属于总线耦合器 PXC.2703994，PXC.2861344 没有这个点）；已知不同地址继续拒绝。
- PXC.2861289 的 Overview 宏在 (0,292)、macro_box:false 插入时，空块 `Name` 抛 BaseException，导致整页失败（报告调用642，页 `=K1+A2&EFA1/5`）。不可读字段保留未知并附警告。— 负责人定位与报告已核对；修复后插入已实测。
- 报告调用1012包含20种语言、合计1003字符；限制改为每语言500。`warn` 下缺少项目显示语言/??_?? 应告警放行。— 输入已核对，测试正文去项目数据化；20语言与仅 de_DE 的提交已实测。
- 示例存在没有颜色、截面、标识及部件的连接定义点。— 负责人反馈；报告未定位到全部省略的请求；无属性 CDP 创建读回与撤销已实测。
- 宏定位补充：`WindowMacro.ReferencePoint` 与 `macro.Objects` 内瞬态 `Function.Location` 按所选表达/变体读取，不使用项目对象定位器。负责人提供 `.ema` 的 `MacroVariant.ReferencePoint`：PXC/2861344 Overview 0 为 (0,292)，PXC/2701290 MultiLine 0 为 (16,168)；前者与插入坐标一致，`MoveKind.Absolute` 将宏参考点放到指定坐标。`location` 是宏坐标系里的绝对位置，和参考点同一坐标系：实测 PXC/2701290 的 `:+` 点 location 为 (16,168)，正好等于参考点；PXC/2861344 的盒 location 为 (212,284)，插入 (0,292) 后与示例一致。所以插入点 = 参考点 + (示例位置 − 宏内位置)。
- PLC 点并入补充：负责人 2026-11-04 `MacroBoxSpikeTests` 实测，在 PXC.2861344 总览 PLC 盒 `=GAA+A1-A1` 已存在时，以 `NumerationMode.None` 插入多线宏，再用 `NameService.RenameDevice` 将 `=GAA+A1-A?:1.1` 等非主功能 PLCTerminal 并入该标识，后缀保留且符号为 `PLC_CBOX_PCON_PLUG`。正式工具仅对“源全为非主 PLCTerminal、目标含 PLCBox”放行；提交后重取目标功能核对迁入点及原总览点、一次撤销恢复已实测。

### A91. 第五、六轮重绘的事实（2026-11-04，EPLAN 2026.0.3，Pro Panel，scratch 项目）
- `Insert.WindowMacro` 把宏的 `ReferencePoint` 放到给定坐标；外框由 `WindowMacro.Location`（左下角）和 `Size` 决定，`Size` 可能是宏框或选区，不一定是对象的范围。— 官方 XML + `MacroInsertTests`、第 7 次重绘。
- 符号 `XTR1` 自身的逻辑定义不是端子：`Terminal.Create(strip, page, variant)` 抛 `WrongCategoryException`（S063064）。示例的端子保留定义 100/6/1：先用 `X2_2` 创建，再 `SetSymbolVariant(variant, false)`。— 实测 `TerminalWithForeignSymbol_KeepsATerminalAndUndoesOnce`。
- 块属性（`EPLAN.BlockProperties.*`，59000 起）的 ID 由项目分配：同一个值在示例项目是 59459，在从基本项目新建的项目是 59456。比较时只比值。— 第 4 次重绘报告。
- 示例项目里端子排 `+A1-FC2` 和设备 `=GAA+A1-FC2` 同时存在。允许省略高层代号的名称规则只适用于端子排和端子。— 示例数据。
- 库里的宏和示例项目插入时的版本可能不同：`PXC/2861344` 的原理图表达，标的位置差约 0.7 mm；`PXC/2861250`、`PXC/2861289` 总览左侧的模块图线条位置相同，但示例是一个块，库里是散的，线宽也不同；`PXC/2861344` 总览的模块图位置也不同。— 第 7 次重绘。
- `eplan_placement_delete` 是 Destructive，无人值守运行要用脚本审批；否则弹窗等 120 秒后 `APPROVAL_EXPIRED`。— 第 7 次重绘。

### A94. 没有连接的线上也能放定义点（2026-11-04，EPLAN 2026.0.3，16 页重画）
- EPLAN 通过中断点把两端的设备连起来；对端没有设备时不生成连接（见 `GenerateAndVerify` 的注释）。样本里 22 个定义点在这样的线上，对端在 16 页以外。
- 定义点是页上的放置对象，建它不要求线上已有连接。工具改为在这种线上照放，`endpoints` 为 null，并给警告；之后生成出连接时，它归入该连接。第 14 次重画确认。

### A96. 符号变量和属性排列可以在放好后修改（2026-11-04，EPLAN 2026.0.3，LargeWrite）
- 主功能用 `Function.SetSymbolVariant(variant, false)`：部件 `SIE.3VA1032-2ED32-0AA0` 的 `Q_3LI` 变量 0→6，部件和连接点不变，一次撤销后快照还原。
- 中断点用 `SymbolReference.SymbolVariant`：`BP` 变量 0→4，一次撤销后快照还原。
- 写 `SymbolReference.PropertyPlacementsSchemas.Selected` 后，`Selected.Name` 和属性 19307 读回相同。可选排列随符号不同：`BP` 有 `左,0°` 等方向排列，普通线圈 `K` 只有 `默认`、`用户定义`。
— Large `PlacementSymbolEditTests`、`PlacementArrangementTests`、`PartVariantPlacementTests`。

### A97. 插入菜单的文字和图形对象可以用 API 创建，批注除外（2026-11-04，EPLAN 2026.0.3，LargeWrite 探针）
- 都能创建并读回：`HyperLink.Create(page, link, MultiLangString)`、`QRCode.Create(page, point, size, text)`、`Spline.Create` 加 `SetPointAt`、`Arc.SetArc(圆心, 半径, 起, 止, true)`（读回 `Arc.Sector` 为 true）、`PlaceHolder.Create`、`MacroBox.Create`。
- 尺寸标注的数值由 EPLAN 计算：`DimensionItem.Create` 读回 `60 mm`，`CreateAngular` 读回 `45°`，`DimensionCircle.Create(page, arc, point)` 读回 `12 mm`；`DimensionGroup.Create` 用 3 个点和 `Incremental` 生成 2 个 `DimensionItem`（`30 mm`、`70 mm`）。类型还有 `BaseLine`、`Continued`。
- `Comment` 没有自己的 `Create`，继承的 `Text.Create` 抛 S063126（对象类型不对）。批注目前只确认能走 PDF 批注导入。
— Large `AnnotationSpikeTests`（未合入）。

### A101. 编辑选项卡的图形命令有 API，都只占一步撤销（2026-11-05，EPLAN 2026.0.3，Pro Panel，LargeWrite 新页）
- **缩放：** `Placement.Scale(x,y,origin)` 对线、矩形、文字、组有效，原点不动。不传原点是绕页面原点缩放，矩形 (120,40) 乘 2 跑到 (240,80)。文字高度随比例变。原理图页的 `Function` 抛 S063188。
- **绘制顺序：** `Placement.DrawingOrder` 可写，范围 0–127，新对象实测 64，XML 写的 67 不对。`Edit.BringToFront` 得当前页最大值 +1，已在最前时不变；`Edit.SendToBack` 对称。不需要打开页面。
- **组：** `Group.Create` 可以同时收 `Function` 和图形；`Group.Move`、`Group.Scale` 带动全部成员；`DeepUnGroup(true)` 取消，但空组还留在页面上，要再调用 `Remove()`。
- **块：** `Block.Create` 后成员从 `Page.AllPlacements` 消失，换成一个 `Block`。传入 `Function` 会被静默丢掉，不报错。`BreakUp()` 返回成员，几何不变。
- **矩形圆角：** `HasChamfers`、`ChamferRadius` 都可写，XML 只写了读。
- **文字固定：** `TextBase.DockText(target)`、`UndockText()` 都不改位置；`DockingDirection` 可写，取 `DockToBottom/Top/Right/Left`。属性文字未实测。
- **修剪：** `Edit.CutOff(Page, PointD)` 不需要打开页面，去掉点所在的那一段，到最近的交点为止：线 (40,40)-(100,40) 被 x=70 穿过，点 (55,40) 得 (70,40)-(100,40)。
- **格式：** `target.Pen = source.Pen` 和 `Layer` 可写。没单独设过的图形 `ColorId`、`Width` 读出 -16002。
— 探针 `EditSpikeTests`（未合入），逐项做了一次撤销。

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

### A106. 结构标识符、标题栏和预规划（2026-11-06，EPLAN 2026.0.3.25421，Electric P8，只在 scratch 新建项目和 LargeWrite 上）
- **结构标识符的官方 API 词是 `Location`，** 搜 structure identifier 搜不到：`Project.GetLocationObjects()`、`Project.GetLocationObjects(Hierarchy)`、`Project.CreateLocation(Hierarchy, string, LocationSortOrder)`、`Project.SetSortedLocations`、`Project.ChangeLocationName`，`Location.IsUsed`、`Location.Remove()`、`Location.Properties`。`Hierarchy`：`Plant` =，`Functional` ==，`Place` ++，`Location` +，`Document` &，`UserDef` #。
- **`Location` 的属性（实测）：** 1000 名称，1001 全名，1002 描述（多语言），1003 带前缀的全名（`+S1`），1004 块名，1005 使用次数（文本，如 `527`），1006 块编号（10002 =，10004 +，10006 &）。每个块有一个名称为空的对象，1003 只有前缀，1005 是 `*`。
- **读写行为（LargeWrite，`StructureToolsTests`）：** `CreateLocation(块, "A", Back)` 和 `CreateLocation(块, "A.B", Back)` 都成功，后者同时有 `A`、`A.B` 两个对象。新建的标识 `IsUsed` 为 false，但有子标识的父标识为 true，所以要先删子标识；`GetLocationObjects` 不带参数时按块依次返回。一次调用多个 `CreateLocation` 和写描述，一次 Ctrl+Z 全部撤销；`Location.Remove()` 同样一次撤销恢复。创建、撤销、再创建、审批删除、撤销删除之后，再撤销一次（想撤销第二次创建）时弹"'撤销'冲突"（"步骤不存在"），EPLAN 随后阻塞；原因没有查明，不要假定能撤销过删除之前的步骤。
- **结构标识符管理对话框：** `XPLEditorStart` 打开。输入中文时 EPLAN 按词典自动补其他语言，有歧义时弹"已找到的字"；API 写描述不会翻译，只存传入的语言。对话框只在没有工具时才值得驱动。
- **标题栏字段的类型取自 XML 的 `ProjectPropertyList`：** 10011、10014、10030、10031、10032 多语言，其余文本；模板自定义的 `ESS.Project.*` 按 `IdentifyingName` 从 `Project.UserDefinedPropertyDefinitions` 取 ID，多语言。写入后报表页要 `eplan_report_update` 才变。
- **写项目属性 1230（只读的 `DESIGNATION_LOCATION_DESCR`）** 的探针运行时 EPLAN 崩溃（`0xc0000374`），具体哪一步没有查明。不要写只读的结构属性，也不要重复这个探针。
- **预规划（新建 scratch 项目 CorePrePlan，已提交）：** 授权项 `Preplanning` 在 P8 和 Pro Panel 上都是 False，`PreplanningExport`、`PreplanningObjectsBase`、`PreplanningObjectsPID`、`PreplanningPID` 是 True（`new License().GetLicenseModules`：共 3214 项，2683 项可用）。尽管如此，20 个管段定义可读；`Page.Create(project, DocumentType.Planning, names)` 建 `&PRE/1`；`LockAllObjects` 后在 Transaction 内 `PlanningSegment.Create(definition)`、`new SegmentPlacement().Create(segment, page)`（放在 0,0）、`new SegmentDefinition().Create(name, parent)` 都成功，管段数 0→1。导入没有测试。
- **`EPLREMOTE_PID` 指向已退出的进程时，** 远程客户端会连到另一个 EPLAN，读写的就是别的实例。先确认进程还在。

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

### B15. Page.GridSize 可读写与单位线索（2026-11-04，安装版 2026.0.3 XML）
- `P:Eplan.EplApi.DataModel.Page.GridSize` 的文档为可读写页网格，签名为 `double`；该 member 未直接标单位。同版本 `Eplan.EplApi.Baseu.xml` 的 `P:Eplan.EplApi.Base.ParserParameter.GridSize` 明确标为 millimeters。实现沿既有 Page.GridSize 绘图读取及 A3 的 1:1 毫米转换；这不是 Page setter 单位的实测证明，网格 4 的新建/读回/撤销仍待 Large 验证。

### B16. 电位定义点四字段类型（2026-11-04，安装版 2026.0.3 XML 与字典）
- `PotentialDefinitionPropertyList` 与字典一致：33003 `POTENTIAL_VALUE`、31004 `CONNECTION_WIRENUMBER`（Connection color / number）、31002 `CONNECTION_WIRECROSSSECTION` 返回 `System.String`；31006 `POTENTIAL_TYPE` 返回 `System.Int64`。`PropertyValue` 文档仅提供 Int32 整数隐式写入和 `ToInt()` 读回，故整数输入限制于该原生入口可表示范围，不经字符串写入。文档未给 31006 枚举值映射，本轮不推测数字对应的电位语义；四字段持久化和撤销仍待 Large 实测。

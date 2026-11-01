# [EPLAN API 笔记](../api-notes.md)：绘图、布局与连接

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

### A65. 同名中断点的配对（D2 实验，同上）
- 同名中断点先按页在项目中的顺序、再按页内位置从左到右排序，相邻两两配对（第 1 与第 2、第 3 与第 4……）；与创建顺序无关（正序、倒序创建结果相同）。
- 两页时（A、C 在第一页，B、D 在第二页）：两个同名 A、B → A–B；三个同名 A、C、B → **A–C**（同页的两个先配成一对），B 落单、没有连接；四个 → A–C、B–D。
- 同页时（从左到右 A、B、C、D）：两个 → A–B；三个 → A–B，C 落单；四个 → A–B、C–D。
- 落单的中断点不产生逻辑连接，生成连接时不报错。
- 对重画的影响：同页同名中断点（如客户页的 I2/I5）要按页内从左到右的位置安排，使相邻两个正好是要连接的一对。— Large `InterruptionPairingSpikeTests`。

### D5 离线实现说明（2026-10-31，未运行 EPLAN）
- A55/A65 的无对端设备或落单中断点允许零逻辑连接；产品仍完整核对实际自动连线、名称、位置、引脚方向和原生配对，两侧真实设备引脚仍须连通。
- 本机 `Eplan.EplApi.DataModelu.xml` 的 `UndoStep` 文档说明：撤销协议独立于 Transaction，DoUndo 回到初始化或最后一次 CloseOpenUndo。中断点路径先提交放置事务，再在仍打开的同一 UndoStep 和静默模式内生成与读回；失败 DoUndo 并重新核对范围指纹。此单步撤销组合尚未实机验证，不能视为 A55 已验证行为；Large 测试仅编写。

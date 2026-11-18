# [EPLAN API 笔记](../api-notes.md)：文字、图形与页面几何

### A146. 模型视图页的绘图读取：对象多到超过回复上限，ViewPart 外框冷读和热读不同（2026-11-16，EPLAN 2026.0.3.25421，Electric P8，A 槽，示例项目 scratch 副本，只读）
- **对象数：** 官方模型视图页 `=K2+B2.Y1&MTC1/1` 有 38507 个绘图对象，`=K2+B2.Y1&MTC1/4` 25965 个，`=GQA+B2.Y1&MTC1/1` 24817 个；整页的绘图证据有 17–26 MiB，超过插件与 Host 之间单条消息 16 MiB 的上限。插件以前照发，Host 判定通信出错就断开连接，AI 看到的是 `EPLAN_NOT_CONNECTED`。现在超限的回复改为 `RESOURCE_EXHAUSTED`，`eplan_page_get` 则只向插件要当前这一段（`drawingOffset`/`drawingLimit`），插件附上整页总数和全部对象的指纹，游标绑定在指纹上。
- **ViewPart 外框：** 同一页连读两次，第一段里的 `ViewPart`（type 191）第一次读外框为空，第二次读回一个宽高为 0、正好在插入点上的“外框”；之后各段两次读取完全相同。两种读数都不提供插入点以外的信息，所以读取器把这种点状外框按空处理，否则对象摘要会变，第一次给出的游标在第二次读取时被判为过期。
— 证据：`tests/EplanMcp.Large.Tests/ModelViewDrawingsTests.cs`，以及探查时的逐段比较（2026-11-16）。

### A145. 图框评估区就是绘图区：起点 12003/12004，高为行高 12002 之和（2026-11-16，EPLAN 2026.0.3.25421，Electric P8，A 槽，示例项目 scratch 副本，只读）
- **示例项目的两种图框：** A3 横向图框（12033/12034 = 420×297）的起点 12003/12004 为 (4, 16)，12002 只有索引 1，值 276；12005 = 10、12006 = 1；按 `PlotFrame` 类型查到的这个图框 12102（列宽）**没有索引**，但原理图页（如 `=GAA+A1&EFS1/1`）实际读回的列宽之和为 412，绘图区是 [4, 16, 416, 292]。A4 纵向图框（210×297）的起点为 (0, 20)，12002[1] = 277。
- **与图纸核对：** 总览页导出成 PNG 后量过：标题栏上沿在 y = 16 mm，左边行标题条从 x = 4 mm 开始，顶部列号行在 y = 292 以上，十列分在 x = 4 以右。所以评估区 [12003, 12004, 12003 + 列宽之和, 12004 + 行高之和] 就是图框边线以内、标题栏以上、列号以下的可用区；不存列宽时宽度取到 12033。
- **按比例放大的页：** 官方 `=EC1+A3&EFS1/1` 的页面是 630 × 445.5，图框仍是 420 × 297，图框属性读出的评估区还是 [4, 16, 416, 292]，图框是按页比例画的。所以评估区要乘以“页面尺寸 / 图框尺寸”，得到 [6, 24, 624, 438]；不乘的话整页都会被判越界。同一种符号在这页和 1:1 页上的外框一样大（端子 X2_2 1.5 × 3、插头 XBD2 3.5 × 4.5、连接点 CO 2.5 × 2.5），栅格也都是 4 mm：放大页只是更大的图纸，打印时整页缩小。所以按栅格和符号算的阈值不换算，只有“打印出来多大”的阈值（文字最小高度）乘页比例（页面宽 / 图框宽）。
- **注意：** 文档里 12005 写作“行数”、12006 写作“列数”（`FRAME_NUMBER_OF_COLUMS` 的说明也是 rows），与图纸上的 10 列 1 行对不上，不用这两个数推算尺寸。属性值是页面毫米，原点在左下（A3）。
- **产品实现：** `EplanDrawingReader` 读出 `PageDrawingGeometry.DrawingArea`，区域超出页面或读不出来时为 null。布局守卫 R1 优先用这个区域，读不到再用页面四边各减 `layout.marginMm`；`eplan_page_get` 的 `drawings.drawingArea` 告诉 AI 能画在哪里；`eplan_page_review` 的 `off_area` 检查越界。
— 证据：`tests/EplanMcp.Large.Tests/PlotFrameAreaTests.cs`，以及探查时的 `eplan_object_get`（PlotFrame）读数和 `&ADB1/2` 的导出图。

### A49. 页面文字、标题栏与图框（S38 r3，2026-10-26，EPLAN 2026.0.3.25421，Electric P8，只打开 scratch 示例副本）

- 一页上的文字分布在：页面自由文本 `Graphics.Text`、属性放置、功能文本、路径文本，以及图框的 `PlotFrame.SubPlacements`（示例页 15 段自由文本中 13 段在图框里）。按页替换文字时要区分来源，图框文字不属于页面。
- 页面自由文本和多语言文字的内容可以在 SafetyPoint 中修改并回滚；提交后一次 `XEsUndoAction` 撤销即恢复（当前项目，A41）。不弹对话框。
- 页的图框：`Page.PlotFrame` 可换成项目中已有的另一种图框，读回正确、回滚恢复。页属性 11016 为空时，图框来自项目设置。
- 产品实现（`eplan_page_update` 的 `plot_frame`，2026-10-26 Large `PageUpdateFrameTests`）：`Page.PlotFrame` 在 API 目录中为只读，切换通过把页属性 11016 设为图框名完成；写入后 11016 与有效图框都读回为目标图框，其他页不变；提交后当前项目一次 `XEsUndoAction` 恢复原状态，包括 11016 原为空、图框来自项目设置的情况。可选图框只取项目各页已在使用的图框。
- 图框显示的项目属性（如 10011 `PROJ_INSTALLATIONNAME`）可修改、读回、回滚；图框中页属性的绑定描述符读不出来源/作用域（`ObjectNotCreatedException`），不能据此判断标题栏显示哪个页属性。
- 插件型 spike 在 EPLAN 中运行时，`Assembly.Location` 指向影子副本而不是构建目录；不要用它定位仓库或输出目录。
— 协调人经 spike S38（`texts`、`findpage`、`replace`、`titleblock` 模式）实测，设计文件哈希与系统消息前后核对。

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

### A57. 函数的属性放置是 ContextPropertyPlacement（M8 T2b 实机，2026-10-27，EPLAN 2026.0.3.25421，Electric P8，只在 scratch 副本上）
- `Function.PropertyPlacements` 返回的是 `ContextPropertyPlacement`（`PropertyPlacement` 的子类），**它们的 `ToStringIdentifier()` / `ObjectIdentifier` 与父功能相同**，也不出现在 `Page.AllPlacements` 中。按标识去重会把它们全部当成父功能而丢掉；须按父对象 + 显示的属性 + 序号区分。新建的 20214 放置同样如此，画面上正常显示（导出图片核对）。— T2b。
- 经 `eplan_device_place` 的库符号分支放置无部件线圈：标识 20214 显示带 `-` 且位于“父位置 + 偏移”，连接点代号经带索引 20022 写入并显示，重复标识拒绝，一次撤销恢复。— T2b。

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

### A106. 结构标识符、标题栏和预规划（2026-11-06，EPLAN 2026.0.3.25421，Electric P8，只在 scratch 新建项目和 LargeWrite 上）
- **结构标识符的官方 API 词是 `Location`，** 搜 structure identifier 搜不到：`Project.GetLocationObjects()`、`Project.GetLocationObjects(Hierarchy)`、`Project.CreateLocation(Hierarchy, string, LocationSortOrder)`、`Project.SetSortedLocations`、`Project.ChangeLocationName`，`Location.IsUsed`、`Location.Remove()`、`Location.Properties`。`Hierarchy`：`Plant` =，`Functional` ==，`Place` ++，`Location` +，`Document` &，`UserDef` #。
- **`Location` 的属性（实测）：** 1000 名称，1001 全名，1002 描述（多语言），1003 带前缀的全名（`+S1`），1004 块名，1005 使用次数（文本，如 `527`），1006 块编号（10002 =，10004 +，10006 &）。每个块有一个名称为空的对象，1003 只有前缀，1005 是 `*`。
- **读写行为（LargeWrite，`StructureToolsTests`）：** `CreateLocation(块, "A", Back)` 和 `CreateLocation(块, "A.B", Back)` 都成功，后者同时有 `A`、`A.B` 两个对象。新建的标识 `IsUsed` 为 false，但有子标识的父标识为 true，所以要先删子标识；`GetLocationObjects` 不带参数时按块依次返回。一次调用多个 `CreateLocation` 和写描述，一次 Ctrl+Z 全部撤销；`Location.Remove()` 同样一次撤销恢复。创建、撤销、再创建、审批删除、撤销删除之后，再撤销一次（想撤销第二次创建）时弹"'撤销'冲突"（"步骤不存在"），EPLAN 随后阻塞；原因没有查明，不要假定能撤销过删除之前的步骤。
- **结构标识符管理对话框：** `XPLEditorStart` 打开。输入中文时 EPLAN 按词典自动补其他语言，有歧义时弹"已找到的字"；API 写描述不会翻译，只存传入的语言。对话框只在没有工具时才值得驱动。
- **标题栏字段的类型取自 XML 的 `ProjectPropertyList`：** 10011、10014、10030、10031、10032 多语言，其余文本；模板自定义的 `ESS.Project.*` 按 `IdentifyingName` 从 `Project.UserDefinedPropertyDefinitions` 取 ID，多语言。写入后报表页要 `eplan_report_update` 才变。
- **写项目属性 1230（只读的 `DESIGNATION_LOCATION_DESCR`）** 的探针运行时 EPLAN 崩溃（`0xc0000374`），具体哪一步没有查明。不要写只读的结构属性，也不要重复这个探针。
- **预规划（新建 scratch 项目 CorePrePlan，已提交）：** 授权项 `Preplanning` 在 P8 和 Pro Panel 上都是 False，`PreplanningExport`、`PreplanningObjectsBase`、`PreplanningObjectsPID`、`PreplanningPID` 是 True（`new License().GetLicenseModules`：共 3214 项，2683 项可用）。尽管如此，20 个管段定义可读；`Page.Create(project, DocumentType.Planning, names)` 建 `&PRE/1`；`LockAllObjects` 后在 Transaction 内 `PlanningSegment.Create(definition)`、`new SegmentPlacement().Create(segment, page)`（放在 0,0）、`new SegmentDefinition().Create(name, parent)` 都成功，管段数 0→1。导入没有测试。
- **`EPLREMOTE_PID` 指向已退出的进程时，** 远程客户端会连到另一个 EPLAN，读写的就是别的实例。先确认进程还在。

### B15. Page.GridSize 可读写与单位线索（2026-11-04，安装版 2026.0.3 XML）
- `P:Eplan.EplApi.DataModel.Page.GridSize` 的文档为可读写页网格，签名为 `double`；该 member 未直接标单位。同版本 `Eplan.EplApi.Baseu.xml` 的 `P:Eplan.EplApi.Base.ParserParameter.GridSize` 明确标为 millimeters。实现沿既有 Page.GridSize 绘图读取及 A3 的 1:1 毫米转换；这不是 Page setter 单位的实测证明，网格 4 的新建/读回/撤销仍待 Large 验证。

### A150. 图形编辑后的矩形、线、文字与组的几何读回（2026-11-05，EPLAN 2026.0.3，图形编辑后核对几何）
- **现象：** 此次编辑读回中，矩形外框与其几何一致；线的外框计入画笔，文字的外框计入文字度量，组的位置随成员变化。
- **结论：** 编辑结果只对矩形核对预期外框；对组核对成员的变化，跳过组本身的位置比较。
- **证据：** `src/EplanMcp.Bridge.Core/Operations/Pages/GraphicEditVerification.cs` 的 `GraphicEditVerification.CompareGeometry`。Large `GraphicEditTests.Scale_LineTextAndGroup_ReadsBackAndOneUndoRestoresTheScale`。

### A155. 总览页上的设备属性文字也有移动位置（2026-11-13，EPLAN 2026.0.3，16 页重绘）
- **现象：** 示例总览页上也有设备属性文字和移动后的文字位置，页上没有需要绕开的导线。重绘时，工具曾对 5 页返回 `not a Circuit page`。
- **结论：** 规划设备属性文字移动时，页型检查接受 `Circuit` 和 `Overview`。
- **证据：** `src/EplanMcp.Bridge.Core/Operations/Properties/PropertyPlacementMoves.cs` 的 `PropertyPlacementMoves.Plan`。Large `SmallProjectRedrawTests.SixteenPages_McpRedraw_ReportsEveryDifferenceAndKeepsArtifacts`（覆盖总览页属性文字移动）。

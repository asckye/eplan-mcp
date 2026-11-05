# EPLAN 的数据关联

| 项 | 内容 |
|---|---|
| 状态 | 现行 · 最后核对 2026-11-04 |
| 适用 | EPLAN Electric P8 / Pro Panel 2026.0.3 |
| 相关 | [设计思路](design-model.md)、[已验证的 API 事实](api-notes.md) |

来源写法和可信度见[设计思路](design-model.md#来源写法与可信度)。每组关系说明谁是源头、谁跟着变、数据存在哪、人在 EPLAN 里怎么操作。

# 二、十组关系

## R1. 器件、功能、主功能、辅助功能

- **源头：** 标识。所有标识相同的功能属于同一个器件。`SubFunctions` 的定义就是相同识别标识（20005）且非主的功能。— `H:xfctdefbrowsergui_k_betriebsmittel`、`XML:Function.SubFunctions`
- **主功能：** 功能上的一个标志，一个器件只能有一个主功能。部件只挂在主功能上；例外是端子（部件在主端子）、插针、母线连接点和连接。设备定义存在主功能里；删主功能就删设备定义。— `H:xfctdefbrowsergui_k_betriebsmittel`、`XML:Function.IsMainFunction`、`H:devicelistgui_k_prinzip`、`H:adjustdata_k_start`
- **合并：** 没有“合并”操作，标识相同就算同一个器件。**[推断]**
  - 界面：给标识已有主功能的函数填该标识时，目标函数的主功能勾选自动取消。— `H:devicetaggui_r_bmdaten`
  - API 不做这个检查：`Function.Name` 允许同页两个同名主功能（A13）。复制也可能造成重复主功能，器件被记为有错但可继续编辑（`H:devicelistgui_k_loeschenkopieren`）；有批量转换多余主功能的命令（`H:adjustdata_k_start`）。
- **没有主功能时：** 插入设备时，器件没有主功能则第一个放置的元件成为主功能（`H:devicelistgui_k_vorgehengeraeteeinfuegen`）。先放辅助功能时，主功能作为未放置功能产生（`H2022:devicelistgui_h_geraeteeinfuegen` **[搜索摘要]**）。删掉主功能或最后一个放置后，未放置功能保留，没有主功能的器件被标错（`H:devicelistgui_k_loeschenkopieren`）。
- **标识计算：** 用户输入显示标识；完整标识 = 环境结构 + 显示标识（`H:devicetaggui_r_bmdaten`）；没有显示标识时按搜索方向接管（`H:devicetaggui_k_bmkuebernahme`）；20051 可关闭计算（`XML:FUNC_DISABLENAMEEVALUATION`）。API：`NameService.AdjustFullName`、`AdjustVisibleName`（`XMLHE`、A81）。
- **改源头是否自动更新：**
  - 只改主功能的 `Name`，辅助功能保留旧标识并脱离器件（A13）。
  - `NameService.RenameDevice` 改全部功能，含未放置的（A85）。
  - 改名后接在其上的连接端点全名、其他设备的 20403 随之变化（A45）。
  - 界面：全局模式可改所有表达的标识（`H:reverseengineering_k_modus`）。
- **界面操作：** 属性对话框填显示标识，`[...]` 选已有标识（选中单个功能时接管它的可编辑属性，`H:xfctdefbrowsergui_k_prinzip`）；工具 > 指定主功能会把部件数据移到新主功能（`H:adjustdata_h_nebenfunktionaendern`）。
- **对绘图：** 触点、线圈不是独立对象，靠标识连成器件；新放的触点默认不是主功能（A55 T7d）。

## R2. 部件库与项目

- **源头：** 部件管理。部件可以带：宏（符号宏或窗口宏）、功能模板（设备定义）、带 X/Y 的图形宏（只用于面板布局）。优先级：宏最高，其次模板。— `H:devicelistgui_k_vorgehengeraeteeinfuegen`
- **名词对应：**
  - 帮助里查不到“group symbol macro”这个词。**[未找到依据]** API 属性 `ARTICLE_GROUPSYMBOLMACRO`（22145）的名称是 Schematic macro：可放在原理图和 2D 面板布局页的 2D 宏。— `XML:ArticleReferencePropertyList.ARTICLE_GROUPSYMBOLMACRO`
  - `ARTICLE_MACRO`（22010）是 Graphical macro，3D 用。— `XML`、A82
  - `XMLMD:MDPlcTemplatePosition.SymbolMacro` 等是模板行上的符号宏。
  - 文件：窗口宏 `.ema`、符号宏 `.ems`、页宏 `.emp`。— `H:macrosgui_h_makroserzeugen`
  - 符号宏是把几个符号打包成一个对象，只存符号库、库位置、符号号和变体号。— `H:xsbgui_k_symbolmakros`
- **数据存在哪：**
  - 模板属于部件，不属于项目（`H:devicelistgui_k_prinzip`）。
  - 用到的部件存入项目（`XML:Function.AddArticleReference`）。
  - 部件引用是 `ArticleReference`，引用上为空的部分属性取自部件（`XML:ArticleReference`）。
  - 宏不存功能定义、符号、部件、表格、图框，只存来源和编号（`H:macrosgui_k_daten`）。
  - 主功能最多 50 个部件号（`H:partselectiongui_k_prinzip`）。
- **从部件插入发生了什么：**
  - 插入设备一次放第一个图形表达，同时把部件和全部功能或模板带入项目（`H2022:devicelistgui_h_geraeteeinfuegen` **[搜索摘要]**）。
  - 模板路线：先给主功能，再按部件管理里的顺序依次放其余模板（`H:devicelistgui_k_vorgehengeraeteeinfuegen`）。
  - 宏路线：部件转到可正确覆盖的主功能；没有时用黑盒，再没有用第一个图形主功能；技术数据带入（同上）。
  - “生成宏时考虑宏”设置（默认关）决定是否把其他变体的功能生成为未放置（`H:xessettingsgui_d_betriebsmittelallgemein`）。
  - 模板分组（template group）让多线页上的相关功能一起放（`H:devicelistgui_k_vorgehengeraeteplatzieren`）。
- **改源头是否自动更新：** 否。改部件不会自动进项目，要显式操作：
  - 设备选择；
  - BOM 的“更新设备数据”；
  - 部件同步，只传模板数据，保留部件号和引用数据（`H:partselectiongui_k_prinzip`）；
  - API `DeviceService.UpdateDevice`：部件数据到主功能，模板数据到全部功能，空值不覆盖（`XMLHE:DeviceService.UpdateDevice`）；
  - action `synchronize`（`API:synchronize`）。
- **界面操作：** 插入中心选设备；设备导航器新建设备；设备选择（`H:partselectiongui_k_vorgehensweise`）；部件主数据或设备清单拖到页上（`H:userinterface_k_dragdropfunktionen`）。
- **API 实测，和界面放置有差异（A82、A85）：**
  - `CreateDevice(part, variant, page, point)` 相当于插入设备：主功能放在页上，其余是持久的未放置功能（`XMLHE:DeviceService.CreateDevice` 与 A82 一致）。
  - `PlaceAt` 和 `Place` 对已实例化功能都**另建**同名功能，原未放置功能还在（`XML:Function.PlaceAt`、A82）。XML 说 `Place` 类似拖动，不改原功能，除非它是未覆盖模板。
  - 未放置功能不能直接改名，用 `RenameDevice`（A82、A85）。
  - 同一事务内对刚建 PLC 主功能 `PlaceAt` 可使 EPLAN 崩溃（A82、A85）。
  - 先放符号 `SSD` 再 `AddArticleReference`：部件引用写到主功能，部件的模板（`SSMNO`）仍是未覆盖的临时模板。连接点代号相同，但符号和功能定义不同（A82）。这和“标识属性一致才覆盖”（`H:devicelistgui_k_prinzip`）相符。
- **宏插入 vs 放置：** 见 1.3。占位符值集在插入后应用（A33、A51）。宏来源记在 20468（`XML:FUNC_MACRO`）。

## R3. 表达类型

- **有哪些：** 多线、总览、成对引用、单线、P&I、拓扑、面板布局、详细面板布局、3D、功能图（`Functional`）、功能总览（流体）、External、Graphic；20121 存类型码。— `H:devicetaggui_r_symbolfunktionsdaten`、`XML:FUNC_TYPE`
- **有没有“主表达”：** 没有这个说法。**[未找到依据]** 实际有三种优先：
  1. **取值顺序：** 全局编辑和报表按多线、总览、成对引用、单线、P&I、拓扑、面板布局、详细面板布局、3D、功能图、功能总览取第一个非空值；连接按多线、3D、单线、P&I、拓扑。— `H:reverseengineering_k_reihenfolge`
  2. **布尔属性例外：** 主功能这类布尔属性，任一表达勾选就算勾选。— 同上
  3. **主功能和表达无关：** 主功能勾选在某个表达上；成对引用要求主功能是多线（`H:xessettingsgui_k_querverweisarten`）；PLC 建议单线盒为主（`TT-PLC主数据 p23`）。
- **属性存在哪：** 每个表达存一份，官方称之为不同表达的冗余数据（`H:adjustdata_k_start`）。部件引用也按表达分开，有 `MergedArticleReference` 合并各表达的引用（`XML:MergedArticleReference`）。
- **怎么关联：** 按相同标识和功能定义（`H:xessettingsgui_k_grundlagen`、`H:xessettingsgui_h_symbolquerverweiseerzeugen`）；PLC 见 1.6；成对引用要求同功能定义、同显示标识、同连接点代号，且都在多线页（`H:xessettingsgui_k_querverweisarten`）。
- **改源头是否自动更新：** 默认不跨表达。全局模式、同步命令或 `Synchronize.*` 才传；只传已有内容的字段（规则见 1.6）。显示、符号、格式从不跨表达（`H:reverseengineering_k_modus`）。
- **怎么产生：** 拖放时表达类型由页类型决定；未放置功能只提供多线、单线（`H:userinterface_k_dragdropfunktionen`、`H:devicetaggui_r_symbolfunktionsdaten`）。宏变体按表达类型存（`H:macrosgui_h_makroserzeugen`）；选错表达会放不上（A13）。
- **相关只读属性：** 20470–20474（该功能是否存在某表达）、20477（所有放置位置）。— `XML:FUNC_ISPLACEDIN_*`、`XML:FUNC_ALLPLACEMENTLOCATIONS`
- **实测提醒：** 带过滤器的 `GetFunctions` 会多返回同名、无 `VisibleName` 的主功能（A12）。**[推断]** 这和第二条“任一表达勾选”及各表达各存一份有关，待实测。
- **Graphic、External：** 不进报表、检查、交叉参考，但算目标；经 Graphic 连接目标不被报告；电位和 PLC 目标跟踪会穿过图形连接。— `H:devicetaggui_k_grafischeschaltzeichen`

## R4. PLC

- **层次：** 配置项目 > 站 > 机架和 CPU（都用 PLC 盒表示）> PLC 卡（一个或多个 PLC 盒）> 通道 > 连接点。— `H:plcgui_k_prinzip`、`H:plcgui_k_kanaele`
- **源头和归属：**
  - 卡数据在主功能盒上。— `H:plcgui_k_prinzip`
  - 连接点靠标识归属卡，可放在盒外；放进盒里接管盒的标识。— `H:plcgui_k_arbeitsweise`、`H:plcgui_h_spszeichnen`
  - 连接点的唯一身份是 PLC 盒标识 + 插头代号 + 连接点代号；地址既不识别也不是规划期必需。— `H:plcgui_k_spsanschluesse`
  - 通道内只能有一个有效 I/O 点；其余标 `20438` 按电源对待。— `H:plcgui_k_kanaele`、`XML:FUNC_PLCDEACTIVATED_IO`
- **实测的归属：**
  - PLC 连接点是 `Terminal`（XML 类注释），新放的 `main:false`；`Terminal.PLC` 可能为 null；跨页点不一定在卡的 `PLCTerminals` 里，要按名称前缀查（A55、A78）。
  - `Terminal.Create(PLC, …)` 会在页原点多留一个点，改用 `Terminal.Create(Page, …)` 加标识（A55）。
  - PLC 连接点不能设为主功能（A55）。
- **属性：** 见 1.6 和 R10。导出时读的是 20404、20031、20428 这些计算值。— `TT-PLC属性 p18-20`
- **“从 PLC 数据放置”：** 帮助里没有这个命令名。**[未找到依据]** 对应的官方操作：
  - PLC 导航器 Place 或拖放（`H2022:plcgui_h_spsanlegen`）；
  - 指派（`H:plcgui_k_kanalorientiertarbeitsweise`）；
  - 使用现有 PLC 连接点（批量，选了就把 From 的数据转给 To；如果同一功能在原理图和总览都选中才共用，只选一边则关联丢失；放在盒内的点取了别的标识后失去盒归属，要连盒一起用现有）（`H:plcgui_h_anschluesseabbuchen`）；
  - 把总览点从导航器指派给宏里的多线点（`H:navigatorgui_k_zuweisenvonfunktionsdaten`）；
  - 生成 PLC 原理图（`H2022:plcgui_k_spsschaltplanprinzip`、`H:plcgui_k_makrosschaltplanerzeugen`）。
- **设备法和功能法：** 设备法：新建卡，部件带宏时模板被多线宏覆盖，放置通道时宏的功能覆盖模板。功能法：先放目标宏（尽量只有连接点，盒不带部件），再把未放置功能指派上去。— `H:plcgui_k_kanalorientiertarbeitsweise`
- **宏的匹配：** 功能类别和连接点代号一致；代号以 `?` 开头还要通道一致；`?` 不覆盖无 `?` 的代号；盒要功能定义一致。— `H:plcgui_k_abbuchverhalten`
- **总览：** 总览宏读变体 A，机架总览读变体 E，都是 Overview 表达；每张卡都有变体 A。— `H:plcgui_k_makrosschaltplanerzeugen`、`H2022:plcgui_k_spsschaltplanprinzip`
- **PLC 图表：** 功能相关报表（`*.f19`），一张卡一份，内容是该卡的 PLC 连接点。— `H:formgeneratorgui_k_funktionsbezogen`。PLC 卡总览 `*.f20`，地址总览 `*.f48`。— `H:formgeneratorgui_k_auswertungsuebersicht`。A84 读到 PLC 图表 `F19_001` 按器件 `=K1+A2-KE1`、`-KE2` 生成。值的取用按表达顺序（`H:reverseengineering_k_reihenfolge`），表格里还可用“相连的 PLC 连接点”元素（`H:plcgui_k_arbeitsweise`）。

## R5. 连接

**数据流：** 符号连接点正对 → 自动连线（图形）→ 生成 → `Connection`（数据）。CDP、电位点在生成时把属性给连接。

- **自动连线：** 只有正对才连（`H:egedgui_k_start`）；错开一格不连（A52）；放置时可能已连上（A55）。
- **`Connection`：**
  - 记录源和目标。默认比较两端标识，“小的”为源；连接符号不做目标（母线和屏蔽的圆形节点除外，`H:connectionbrowsergui_k_verbindungseigenschaften`）；可按图形位置（项目设置）或连接点逻辑的 Source/Target。— `H:connectionbrowsergui_k_prinzipverbindungen`
  - 一条连接可对应多段图形线（A55）。
  - 跨页编辑后要页面关闭才编译（`H:connectionbrowsergui_k_verbindungseigenschaften`）。
- **生成：**
  - 界面见 1.4。无对应连接点时连接自动删除，CDP 保留；状态栏 `#` 表示项目有过时连接，`*` 表示当前页有；Shift + 更新强制全部重新生成。— `H:connectionbrowsergui_h_verbindungenbearbeiten`
  - **API 不会替你生成：** 同事务内 `Generate.Connections` 读到 0 条，要提交后单独调用（A52）。放置和生成各一个撤销步（A52）。
- **T 节点和角：**
  - 官方把角、T 节点、双连接、跳线、中断点归为图形连接符号，没有逻辑功能和功能定义（`H:egedgui_k_start`）；但 T 节点的目标方向由符号内部逻辑决定连接顺序（`H:connectionbrowsergui_k_prinzipverbindungen`）。
  - 实测：变体 8 是星形（A→B、A→C），变体 0–3 是链式（A→B、B→C）；第一个目标为空时变体 0 断开（A64）。
  - T 节点放到已有自动连线上会不会拆线，待验证（A52）。
- **中断点：**
  - 相同完整标识形成引用（`H:xessettingsgui_h_abbruchstellenquerverweiseerzeugen`）；星形（`IsStarSource`）或链式（第 1 对第 2、第 3 对第 4）（`XML:InterruptionPoint.IsStarSource`、`H:interruptionpointgui_k_darstellungabbruchstellen`）。
  - 配对顺序：默认页序加左右（A65），排序码 24809（默认 100）优先（A65、`XML:INTERRUPTIONPOINT_SORT_CODE`）；界面有“排序中断点”对话框（`H:interruptionpointgui_k_darstellungabbruchstellen`）。
  - 落单在生成时不报错，检查运行会报（A65、`H:interruptionpointgui_k_darstellungabbruchstellen`）。
  - 配对在生成提交后才可读（A55）。
- **CDP：**
  - 给穿过它的连接指定或覆盖属性，不代表整条连接（`H:connectionbrowsergui_k_vdp`）；只作用于和它相交的连接（`H:connectionbrowsergui_h_vdpeigenschaften`）。
  - **传递时机：** 只有更新连接时才传（`H:connectionbrowsergui_k_vdp`、`XML:ConnectionDefinitionPoint`）。实测 31004 = `RD`、截面 1.5 生成后到连接（A86）。
  - **多个 CDP：** 可互补或矛盾，矛盾由检查标出；线号取图形上第一个非全 `?` 的（`H:connectionbrowsergui_k_vdp`）。
  - **宏：** 存 CDP 不存连接，插入时重新生成（同上）。
  - **部件：** CDP 的部件页给连接分配部件（`H:connectionbrowsergui_h_vdpeigenschaften`）；报表要按连接出，一个 CDP 给两条连接时部件出现两次（`H:connectionbrowsergui_k_vdp`）。写入时机：放置事务提交后、生成前（A87）。只有部件没有颜色截面的 CDP 也有效（A87）。
  - **自动放置：** 改连接属性、连接编号、生成电缆、布线都会放 CDP（`H:connectionbrowsergui_k_vdp`）。
  - **导航器写回：** 在导航器改连接会写回 CDP（`H:connectionbrowsergui_k_arbeitsweiseverbindungen`）。
  - **打断：** 连接被元件打断时，数据留在有 CDP 那一段，另一段是无特殊属性的新连接（同上）。
- **颜色：** 31004 存代码，多语言颜色名由项目颜色表对照得出，区分大小写（`H:connectionbrowsergui_k_verbindungseigenschaften`）。截面 31002。
- **属性来源链**（后面的覆盖前面的，“覆盖”是页面示例隐含）：连接点、项目、图层、端子目标、电位、管道定义、信号、网络、跳线生成、屏蔽、CDP。部件不在链内。— `H:connectionbrowsergui_k_verbindungseigenschaften`
- **电位和信号：**
  - 在电位定义点或电位连接点上定义，两者权利相同；数据对整个电位或信号有效，不只一条连接。连接得到名称、信号名、类型、值、频率、对电位。— `H:potentialbrowsergui_k_prinzippotenziale`
  - 电位类型由电位定义优先；冲突时图形第一个胜出并报错。— 同上
  - 电位在更新连接时确定，不能写在连接上（`H:connectionbrowsergui_k_npv`）。
  - PDP 的连接属性点确定后分配给该电位的全部连接（`H:connectionbrowsergui_h_pdpeigenschaften`）。这和 CDP 要等更新的说法不同，待实测。
  - API：`PDP` 不能用 `Function.Create`（A86），可经净化窗口宏插入（A87）；`PCP` 用 `SymbolReference.Create`，逻辑连接起点为空（A52）。
- **未放置连接：** 可以连未放置功能；目标被删时连接留下，源或目标为空；画出来的相同连接不会自动用现有未放置连接，要放 CDP 选择。— `H:connectionbrowsergui_k_npv`

### R5 实测：连接属性什么时候变（2026-11-05，A102）

两个端子之间一条自动连接，在 scratch 上逐步改，每步读连接的 31004、31002：

| 步骤 | 连接的颜色、截面 |
|---|---|
| 生成连接，没有定义点 | 空 |
| `eplan_connection_create` 放 CDP（RD、1.5） | 立刻变成 RD、1.5，工具同时写了连接 |
| 直接改 CDP 的属性（BU、2.5），不生成 | 还是 RD、1.5 |
| `eplan_connection_generate` | 变成 BU、2.5 |
| 再放一个 CDP（GN、4） | 变成 GN、4 |
| 把第一个 CDP 改成 WH、6，再生成 | 还是 GN、4 |

- **CDP 的改动只在生成时传到连接**，和帮助一致。
- **两个 CDP 冲突时，同一个 CDP 一直赢。** 这次赢的是后放的，也是位置靠上的，先后和位置哪个起作用还没有分开测。
- **31047 不反映来源：** 上面每一步索引 1 都读出 4。
- **PDP 没有测出结果：** 放在线中间、生成后连接仍是空；放在别处也没有连上。可能是 PDP 没有接到线上，不能据此说 PDP 不传值。第五部分第 11 题仍未回答。

## R6. 端子、电缆、黑盒、母线

**端子排**

- 端子排定义标识端子排，持有数据和部件；可未放置；端子和端子排可以各自单独存在。— `H:terminalgui_k_verwaltungleisten`、`H:terminalgui_k_start`
- 端子排是主功能，端子分主端子和辅助端子，一个器件可有多个主端子；每个主端子起一个独立区，模板只在区内分配。— `H:terminalgui_k_hauptklemmen`
- 部件建议放主端子。部件放在定义上只有“不使用主端子”开启时才对整排生效。— 同上
- 分散端子靠相同功能定义、识别标识、端子代号、层号合并；报表和编辑对话框合成一个端子；20252 是分散端子的交叉参考。— `H:terminalgui_k_verteiltedarstellungklemmen`、`XML:FUNC_DISTRIBUTED_TERMINAL_CROSSREFERENCE`
- 实测：名称 `<排>:<号>`；端子排按结构方案丢段（`+A1-XT3`，查已有排时要按此比较，否则新建第二个）；事务内 `Terminal.TerminalStrip` 可能为空；放在已生成的连接上再生成会把连接拆开（A55）。

**电缆**

- 电缆定义可画（定义线、屏蔽）或在导航器预定义。把连接变成电缆连接有三条路：画定义线、放 CDP 并设归属为电缆、连接点逻辑设电缆连接点。— `H:cablegui_k_arbeitsweise`
- **电缆连接靠相同标识归属电缆。** 定义线给穿过的连接放隐形 CDP 并传标识；长度、截面等只在 CDP 为空时才传。— 同上。XML 也写明：要把电缆和连接关联，`Cable` 与 CDP 的 `Name`、`VisibleName` 要相同。— `XML:EObjects.Cable.CableConnections`
- 部件选择时，部件的连接自动分配给电缆连接，考虑连接点类型（PE 到 PE）。作为设备插入时，按部件顺序给出定义、电缆连接、屏蔽；已有非空 CDP 时问是否替换。— `H:cablegui_k_prinzip`
- 电缆源和目标来自电缆连接两端的标识，要连接刷新后才对。— 同上
- 实测：
  - 流程是导线 → 生成 → 定义线 → 生成 → 单独提交绑定 → 生成 → 芯线（A55）。
  - **芯线归属由定义线所在的图形段决定**，主干段上一根芯线给经过的全部逻辑连接（A64）。
  - 连接得到芯线后颜色和截面取自芯线（A64）。**[推断]** 这走的是 CDP 那一层。
  - `Transaction.Abort` 不保证移除新建定义线（A55）。
  - 定义线 7 个属性放置的顺序见 A81。

**黑盒和设备连接点**

- 黑盒用来组合连接点、功能或器件。元件归属靠盒内位置、取盒的标识、或从左（上）接管。— `H:blackbox_k_arbeitsweise`
- 灯放进盒里不是盒的连接点，而是嵌套器件（`-U1-H1`）。— 同上
- 盒内函数取盒的标识，不取盒外的；盒外可以取盒内的。— `H:devicetaggui_k_bmkuebernahmesonder`
- 盒内没有自己标识的设备连接点总是辅助功能，没有单独交叉参考。— `H2.9:blackbox_h_anschluesseeinfuegen` **[搜索摘要]**
- 部件是模块时分配到盒内匹配的主功能；组件整体存在盒上。— `H:partselectiongui_k_vorgehensweise`
- 实测：`new BoxedDevice()` 先 `Create(page)`，再设 `SymbolVariant`；连接点命名“盒标识:代号”后同属器件（A86）。

**母线**

- 由母线连接点这种功能组成，每条有一个标识、一个主功能和若干辅助功能，默认是辅助。电位从主功能第一个连接点传到其他点；信号看“带信号隔离”。母线设备有自己的结构设置（DIN 项目一般没有高层和位置代号）。— `H:egedgui_k_sammelschienen`
- 实测：`BBDEF`（类别 `BusBarDefText`，主功能）和 `BCP` 同标识属同一器件；标识只保留位置段（`-WE9` 读回 `+A1-WE9`）（A86）；`RenameDevice` 也会丢段（A88）。帮助页没有写 `BBDEF`。**[未找到依据]**

## R7. 交叉参考

- **存储还是计算：** 帮助说它们在编辑时被自动插入（`H:xessettingsgui_k_grundlagen`），没有明说存储还是计算。**[未找到依据]** 证据指向计算：20300、20301、20252、24300、24301 都标只读（XML）；同名中断点排序码改变后引用文字跟着变（A65）；列号随图框（A65、`H:xessettingsgui_k_grundlagen`）。**[推断]** 应视为派生值，不要写。
- **设备：** 主功能指向所有辅助功能，辅助功能回指。设备定义内的辅助功能排前。— `H:xessettingsgui_k_querverweisarten`。显示由 20021 决定：0 自动（显示标识非空才显示）、1 不显示、2 总是显示。— `XML:FUNC_SUPPRESSCROSSREFERENCE`、`H:xessettingsgui_k_grundlagen`。这就是 API 新建对象 `VisibleName` 为空时没有引用的原因之一 **[推断]**（A53、A81）。
- **触点映像：** 显示器件全部已放置的元件以及未放置和自由功能；按器件在属性对话框的“显示 > 触点映像”设置，对齐方式“在路径上”或“在元件上”。元件设置优先于项目设置。插入线圈或电机保护开关时自动给出正确条目。— `H:xessettingsgui_k_querverweisarten`、`H:xessettingsgui_h_einstellungenkontaktspiegel`
- **中断点：** 见 R5。显示文字由项目设置决定（交叉参考开关、是否只显示页名、目标放前或放下、分隔符）。— `H:xessettingsgui_d_einstellungenprojektabbruchstellen`。24300 输出引用，开启 24021 还带目标信息（`XML:INTERRUPTIONPOINT_CROSSREFERENCE`），但实测 24300 读回空串（A55），和块属性 20201 不同（A81）。
- **PLC：** 见 1.6。20301 只读。— `XML:FUNC_PLCPAIRCROSSREFERENCE`
- **成对引用：** 同一触点放两次，一次在主功能旁（表达类型 Pair cross-reference），一次在接线处。条件见 R3。不开相应设置时不自动产生。— `H:xessettingsgui_k_querverweisarten`、`H:xessettingsgui_k_grundlagen`
- **改源头是否自动更新：** 帮助说编辑时自动；API 下中断点配对要生成连接提交后才可读（A55）。
- **界面操作：** 文件 > 设置 > 项目 > 交叉参考 / 触点映像。

## R8. 页和结构

- **结构标识：** `==` 功能分配、`=` 高层代号、`++` 安装地点、`+` 位置代号、`&` 文档类型，加用户自定义。除文档类型外都可用于器件。— `H:pleditorgui_k_grundlagen`
- **结构方案：** 页和设备分开设，设备按类别分别设（一般设备、端子排、电缆、黑盒等）。基本项目决定，可事后改。— `H:projectstructure_h_prjstrukturdefinieren`
- **块的属性：** 每块可设识别、描述或不可用。— `H:projectstructure_k_start`
- **写入规则（实测）：** 页名前缀 `=`→1100、`+`→1200、`&`→文档类型、`/`→页计数器；项目页结构没有的段被静默丢弃（A13）。单个 `=` 是 1100，`==` 是 1300（A81）。
- **对象如何得到结构：** 完整标识 = 环境结构 + 显示标识（`H:devicetaggui_r_bmdaten`）；设备结构符合页结构时取所在页（`H2.9:devicetaggui_d_vollstaendigesbmk` **[搜索摘要]**）。放置的主功能写 1100 被拒，高层代号来自所在页（A82）。显示标识会省略和页相同的段（A45、A53）。
- **页描述：**
  - 源头是页的 `PAGE_NOMINATIOMN`（11011），多语言，可含换行。— `XML:PagePropertyList.PAGE_NOMINATIOMN`、`H:pagebrowsergui_k_eigenschaften`
  - **19024 是 `INSTANCE_PAGENOMINATION`，在 `GraphicalPlacement` 上，XML 标只读。** 同组还有 19022 `INSTANCE_PAGENAME`、19023 `INSTANCE_PAGEFULLNAME`、19020 页类型、19005 列号、19006 行号。官方属性表说明页名这两项输出放置对象所在页的名称（`H:properties_o_main`）。
  - XML 没有明写 19024 的值来自 11011；项目实测每个对象都带它，内容是所在页的描述。**[推断]** 它是放置对象上的派生只读属性，不是对象自己存的，比较时应忽略或换算成页的 11011。
- **自动更新：** 派生值随页变。页移动或改结构时标识怎么变，帮助没说。**[未找到依据]**
- **页类型决定行为：** 逻辑页才分析设备和自动连线；图形页不报告也不连；总览、面板布局、预规划不自动连线；交互类型（I）由用户建，自动类型（A）由 EPLAN 生成。改页类型不动其他属性，只改报告方式。— `H:pagebrowsergui_k_seitentypen`
- **其他页属性：** 栅格（逻辑页默认 4 mm，图形页 1 mm）；图框页属性优先；图框决定报告方向，进而决定路径文字搜索和接管方向。— `H:pagebrowsergui_k_eigenschaften`、`H:gededitgui_k_pfadtextauswertung`、`H:devicetaggui_k_bmkuebernahme`
- **界面操作：** 页导航器 > 属性；项目属性 > 结构。

## R9. 报表

- **报表模板、报表块、表格的关系：**
  - **报表类型** 47 种，预定义不能新建，分功能相关、总览、图形三类。— `H:formgeneratorgui_k_auswertungstypen`
  - **表格** 指派给报表类型，内含占位符，运行时填项目数据。— `H:formgeneratorgui_k_auswertungsseiten`。模板可指定自己的表格，优先于报表块的设置。— `H:formgeneratorgui_d_vorlagen`
  - **报表块** 是逻辑上成组的报表页；页排序决定一个还是多个块，设置总是作用于块。— `H:formgeneratorgui_k_auswertungsbloecke`
  - **报表模板** 保存设置以便复用，可导入导出 XML，可带项目筛选。— `H:formgeneratorgui_k_auswertungsvorlagen`
  - **方案：** 模板带筛选、排序、换页方案；功能相关报表另有功能的筛选和排序。— `H:formgeneratorgui_d_vorlagen`
- **内容来自哪：**
  - 查询项目数据。— `H:formgeneratorgui_k_start`
  - 按表达顺序取值。— `H:reverseengineering_k_reihenfolge`
  - 连接目标：端子图和电缆图到下一个器件；端子连接图和设备连接图跨多个器件。— `H:formgeneratorgui_k_funktionsbezogen`
  - 部件清单含项目部件、部件定义点上的部件、无部件的部件放置。— `H:formgeneratorgui_k_auswertungsuebersicht`
  - 没有匹配对象时仍生成报表页。— `H:formgeneratorgui_k_start`
  - 不进报表：External、Graphic（`H:devicetaggui_k_grafischeschaltzeichen`）、预定义的设备清单项（`H:devicelistgui_k_geraeteliste`）。
- **封面和目录是否是报表：** 是。封面 `*.f26`、目录 `*.f06` 都是报表总览类型。— `H:formgeneratorgui_k_auswertungstypen`、`H:formgeneratorgui_k_auswertungsuebersicht`。A84 读到 `TitlePage`（17）、`TableOfContents`（14）。目录总是最后输出。— `H:formgeneratorgui_d_einstellgrafikausgabe`。封面的数据来源页面没写。**[未找到依据]**
- **何时更新：**
  - **不会随项目修改自动更新。** 触发条件：手动更新（一个块内全部）；工具 > 生成项目报表（从全部模板生成，并更新已有）；用户设置“打开页时更新报表”（打开报表页时，默认值未写）。— `H:formgeneratorgui_h_auswaktualisieren`、`H:formgeneratorgui_h_projektauswerten`、`H:formgeneratorgui_h_automatischaktualisieren`
  - 冻结：页保留，不再更新；嵌入式报表不能冻结。— `H:formgeneratorgui_d_auswertungen`
  - 从模板重新生成会删旧建新。— `H:formgeneratorgui_k_auswertungsvorlagen`
  - API：`Reports.Update`、`CreateReport`、`GenerateProject`、`CreateReportsFromTemplates`。功能相关报表一个器件只能有一份，再建就覆盖。— `XMLHE:Reports.*`
  - 嵌入式报表：手放在页上，只有页打开时可用；更新报表时一并更新；复制设备时保留关联。— `H:formgeneratorgui_k_ausgabeformen`、`XMLHE:Reports.CreateEmbeddedReport`
- **API 实测（A84）：**
  - `Page.ReportBlock` 能读出当初的生成设置。
  - 方案名属于项目自己，要带方案数据才行（`S029131`）。
  - `DeviceTagNameParts` 限定没起作用。
- **对报表页手工修改是否保留：** **[未找到依据]**

## R10. 属性：存储值与计算值

判断依据：XML 备注写 read-only 视为计算或派生。命名规律：`_MANUAL` 是手工值；`_CALCULATED`（determined）是系统算的；`_AUTOMATIC` 是有手工值用手工值，否则用计算值；`_INDIRECT` 取自所属 PLC 盒或单线口；`_COMMON` 再回退到主功能。

注意：`read-only` 不代表写不了结构部分。20013、20014 标只读但可经 `NameParts` 设置（`XML:FUNC_CODE`）；`FUNC_ALLCONNECTIONDESIGNATIONS`（20038）写入无效（A55）。

| ID | API 名 | 名称 | 类型 | 说明 | 来源 |
|---|---|---|---|---|---|
| 19024 | `INSTANCE_PAGENOMINATION` | 页描述 | 计算（只读） | 放置对象所在页的描述。**[推断]** 来自页的 11011 | XML、项目实测 |
| 19101 | `INSTANCE_INVISIBLE` | 不可见 | 存储 | 是、否、取自图层；元素设置优先于图层 | XML、`H:layermanager_k_prinzip`、A87 |
| 19324 | `DMG_ARC_ENDANGLE` | 圆弧终止角 | 存储 | 双精度，几何数据 | XML |
| 20031 | `FUNC_TEXT_AUTOMATIC` | 功能文字（自动） | 计算 | 手工文字；空则路径功能文字；总览点取对应多线点；上页面时去换行；导出读它 | XML、`H:gededitgui_k_pfadtextauswertung`、`TT-PLC属性` |
| 20048 | `FUNC_FULLCONNECTIONDESTINATIONNAME` | 目标连接点全名 | 计算 | 索引 1–1000；来自连接；标识 + 连接点代号 | XML |
| 20051 | `FUNC_DISABLENAMEEVALUATION` | 把显示标识当完整标识 | 存储 | 布尔；关闭完整标识计算 | XML |
| 20068 | `FUNC_CABLE_CROSSSECTION_UNIT` | 电缆截面或直径单位 | 存储 | 枚举；0 为随项目 | XML |
| 20077 | `FUNC_CONNECTIONDESTINATIONNAME` | 目标连接点名 | 计算 | 索引；连接点代号 | XML |
| 20100 | `FUNC_ARTICLE_PARTNR` | 部件号 | 存储（部件引用） | 索引 1–50；未标只读；功能上的部件引用 | XML |
| 20108 | `FUNC_PLCCONFIGURATIONPROJECT_INDIRECT` | 配置项目（间接） | 计算 | 取自 PLC 盒；总览总线口取同单线口 | XML |
| 20120 | `FUNC_TEXT_COMMON` | 功能文字（通用） | 计算 | 20031；空则主功能的 20031 | XML |
| 20183 | `FUNC_PLCAUTOPLUG_AND_CONNPTDESIGNATION` | 连接点代号（含自动插头） | 计算 | 20431:连接点；总线口加接口名 | XML |
| 20216 | `FUNC_SAFETYRELEVANT` | 安全功能 | 存储 | 布尔 | XML、`H:devicetaggui_r_symbolfunktionsdaten` |
| 20301 | `FUNC_PLCPAIRCROSSREFERENCE` | 总览交叉参考 | 计算 | 依赖身份匹配和显示设置 | XML、A78 |
| 20400 | `FUNC_PLCADDRESS` | PLC 地址 | 存储 | 不是识别属性；CPU 内唯一；可自动编址 | XML、`H:plcgui_k_adressierung` |
| 20402 | `FUNC_PLCSYMBOLICADDRESS_MANUAL` | 符号地址（手工） | 存储 | 总览与原理图同步时传递 | XML |
| 20403 | `FUNC_PLCSYMBOLICADDRESS_CALCULATED` | 符号地址（确定） | 计算 | 目标跟踪；找不到时取最后功能或按设置取地址 | XML、`H:plcgui_k_symbolischeadressen`、A24、A45 |
| 20404 | `FUNC_PLCSYMBOLICADDRESS_AUTOMATIC` | 符号地址（自动） | 计算 | 手工优先，否则确定值；导出读它 | XML、`TT-PLC属性 p18` |
| 20406 | `FUNC_PLCPLUG_DESIGNATION_MANUAL` | 插头代号 | 存储 | 参与识别 | XML |
| 20407 | `FUNC_PLCCHANNEL_DESIGNATION_MANUAL` | 通道代号 | 存储 | 卡内唯一；电源连接点通常按图形归属 | XML |
| 20420 | `FUNC_PLCSTATIONNAME_INDIRECT` | 站 ID（间接） | 计算 | 取自所属 CPU 的 PLC 盒 | XML |
| 20421 | `FUNC_PLCRACK_INDIRECT` | 机架 ID（间接） | 计算 | 取自卡 | XML |
| 20422 | `FUNC_PLCSOCKET_INDIRECT` | 槽位（间接） | 计算 | 取自卡 | XML |
| 20431 | `FUNC_PLCPLUG_DESIGNATION_AUTOMATIC` | 插头代号（自动） | 计算 | 系统确定，可从左或上接管 | XML、`H:devicelistgui_k_steckerbmk` |
| 20443 | `FUNC_PLCBUSINTERFACENAME_AND_PLCPLUG` | 总线接口名（含插头） | 计算 | 无分隔符；识别总线口 | XML |
| 20468 | `FUNC_MACRO` | 宏 | 存储 | 对象来自哪个宏；不能全局编辑 | XML、`H:reverseengineering_k_modus` |
| 20470 | `FUNC_ISPLACEDIN_CIRCUIT` | 该功能存在多线表达 | 计算 | 布尔 | XML |
| 20477 | `FUNC_ALLPLACEMENTLOCATIONS` | 所有放置位置 | 计算 | 索引 1–50；任何表达类型的放置 | XML |
| 31030 | `INTRINSICSAFETY` | 本安 | 存储 | 功能上强制其可本安的连接点 | XML |
| 31047 | `CONNECTION_PROP_SOURCE` | 内部：属性来源 | 未明 | 索引 1–65535；只在连接和电位定义上；XML 只写 Internal | XML；含义 **[未找到依据]** |
| 59456 | | | | **查不到** | XML 全部 12 个文件、属性总表可读部分、仓库 `catalog/eplan-2026.0.3/data-dictionary.jsonl`、搜索都没有 |
| 59459 | | | | **查不到** | 同上 |

补充：

| ID | API 名 | 说明 | 来源 |
|---|---|---|---|
| 20005 | `FUNC_IDENTDEVICETAG` | 识别标识，只读，定义器件 | XML |
| 20006 | `FUNC_FULLDEVICETAG` | 完整标识，只读 | XML |
| 20010 | `FUNC_VISIBLEDEVICETAG` | 显示标识，只读 | XML |
| 20011 | `FUNC_TEXT` | 手工功能文字 | XML |
| 20121 | `FUNC_TYPE` | 表达类型码 | XML |
| 20300 | `FUNC_CROSSREFERENCE` | 主辅交叉参考，只读 | XML |
| 20435 | `FUNC_PLCPLUG_AND_CONNPTDESIGNATION` | 手工插头 + 连接点，只读 | XML |
| 20436 | `FUNC_PLCCHANNEL_DESIGNATION_CALCULATED` | 确定的通道代号 | XML |
| 20428 | `FUNC_PLCCHANNEL_DESIGNATION_AUTOMATIC` | 自动通道代号，只读 | XML |
| 24020 | `INTERRUPTIONPOINT_SHOWALLCROSSREFERENCES` | 显示全部引用 | XML |
| 24300 | `INTERRUPTIONPOINT_CROSSREFERENCE` | 可配置引用，只读 | XML |
| 24809 | `INTERRUPTIONPOINT_SORT_CODE` | 排序码 | XML、A65 |
| 31002 | `CONNECTION_WIRECROSSSECTION` | 截面 | XML |
| 31004 | `CONNECTION_WIRENUMBER` | 颜色或线号，标识连接，编号时填 | XML |
| 31011 | `CONNECTION_DESIGNATION` | 连接标识 | XML |
| 11011 | `PAGE_NOMINATIOMN` | 页描述（源头） | XML |
| 22010 | `ARTICLE_MACRO` | 图形宏（3D） | XML |
| 22145 | `ARTICLE_GROUPSYMBOLMACRO` | 原理图宏（2D） | XML |

---

## R11. 图形对象：组、块、绘制顺序、画笔

- **组和块不一样：** 成员进组后还在 `Page.AllPlacements` 里，只是 `Placement.Group` 非空，可以包含 `Function`。成员进块后从 `AllPlacements` 消失，由一个 `Block` 代替，`Function` 进不去，而且不报错（A101）。
- **绘制顺序是相对的：** `DrawingOrder` 只在页内比较，置顶、置底按当前页的最大、最小值加减 1（A101）。
- **画笔和图层：** 没单独设过的图形，`Pen.ColorId`、`Pen.Width` 读出 -16002。把它所在图层 `EPLAN100` 的 `Pen.ColorId` 从 0 改成 3，图形读出的仍是 -16002，说明图形没有存自己的颜色（2026-11-05，A102）。显示是否跟着变没有核对。

# 三、文档和实测的冲突（以实测为准）

1. **PLC 地址是否参与识别。** `H:plcgui_k_arbeitsweise`：前四项相同时再用地址区分。`H:xessettingsgui_h_spsquerverweiseerzeugen`：地址不识别、没有作用。`XML:FUNC_PLCADDRESS`：不是识别属性。文档之间矛盾，要实测。
2. **PLC 交叉参考的匹配条件。** `H:xessettingsgui_k_querverweisarten` 只列标识、连接点号、功能定义；其他页多列插头代号。项目实测支持插头参与。
3. **`ConnectionDefinitionPoint.ArticleReferences`。** XML 写总是抛异常、CDP 不存部件；A87 实测可读。
4. **`Terminal.PLC`。** XML 写返回所属 PLC；A55、A78 读到 null，跨页点 `PLCTerminals` 也为空。
5. **24300。** XML 写输出交叉参考；A55 读回空串。可能和项目设置或显示条件有关，未确认。
6. **主功能唯一。** 界面自动取消勾选（`H:devicetaggui_r_bmdaten`）；`Function.Name` 不检查（A13）。API 绕过了界面规则。
7. **连接更新。** 帮助说编辑时自动更新；API 下放置事务提交后要单独 `Generate.Connections`（A52）。不是矛盾，是界面和 API 的差异。
8. **放置未放置功能。** 帮助和 XML 都没说原未放置功能会留下；A82 实测 `PlaceAt` 和 `Place` 留下重复。界面行为待实测。
9. **宏变体数量。** 帮助写每种表达最多 26 个变体（`H:macrosgui_h_makroserzeugen`）；`XML:Function.PlaceFunctionalMacro` 写变体值 0–15（A–P）。未实测。

# 四、查不到依据的项目

1. 59456、59459 的名称和含义。
2. 31047 `CONNECTION_PROP_SOURCE` 的具体含义。
3. 官方“数据和图形谁是主体”的明确表述。
4. 官方完整的工程总流程页；教程内容（只读到标题）。
5. “group symbol macro”作为帮助词条；“从 PLC 数据放置”作为命令名；官方的 `BBDEF` 说明。
6. 交叉参考是存储还是计算。
7. 总览点和原理图点之间 PLC 地址的同步规则。
8. API 属性写入是否等同全局编辑模式。
9. 界面放置未放置功能后，该未放置功能是消失还是保留。
10. 基本项目是否明确带图层（只有“全部数据复制”和“图层按项目管理”）。
11. 19024 由 11011 派生的明文说明（只有只读标记）。
12. 页移动或结构改变时对象标识的更新规则。
13. 报表页的手工修改在更新后是否保留；“打开页时更新报表”的默认值；更新时具体覆盖什么；封面报表的数据来源。
14. 连接属性来源链里部件和电缆芯线的位置（A64 的实测只能归因到 CDP，属推断）。
15. 项目模板 `.ept` 在 2026 的状态（2026 帮助页不存在，只有转换 action）。

# 五、需要在 scratch 项目上实测的问题

只在 scratch 副本上做，读取优先。

**PLC 关联（最优先）**

1. 只读：对样例 `=K1+A2-KE1` 的每个通道，列出所有表达的 20121、20406、20431、20435、20183、20022、20026、20400、20402、20011、20031、20120、20300、20301。确认 `KE1:1:2.3` 中 `1` 是插头还是连接点代号的一部分，以及哪些对配对成功。
2. 新页：放总览点和多线点，先不带插头，再带相同插头、不同插头，分别看 20301、20031。验证身份匹配的四项条件，以及地址是否参与（第三部分第 1 条）。
3. 给总览点写 20400、20402、20011，分别不操作、`Synchronize.OverviewToMultilineAndSingleline`、`synchronize /TYPE:OVERVIEW` 后读回两边。确认方向、是否受“空值覆盖”设置影响。
4. `Generate.Connections` 后 20403、20031 是否刷新；没有生成连接时读到什么。

**部件和放置**

5. 在界面里放一个未放置功能，导出前后的功能清单，确认未放置功能是否消失，和 A82 的 `PlaceAt`、`Place` 对比。
6. 非 PLC 部件：`CreateDevice(page)` 后用 `PlaceAt` 放模板对象，配相同功能定义和连接点代号的符号，看 `IsCoveredTemplate` 和未放置功能数量。
7. `FUNC_MACRO`（20468）在宏插入、`CreateDevice` 带宏、手放符号三种情况下的值；`ARTICLE_GROUPSYMBOLMACRO` 为空和不空时 `CreateDevice` 的差别。
8. `DeviceService.UpdateDevice` 在部件改过后实际改了哪些属性。

**表达和主功能**

9. 示例 `=EC1+A3-EC1-MA2` 的两个 `Motor`：两者的 `IsMainFunction`、20121、20470–20473、20477；解释 A12 的多余同名主功能。
10. API 改单个表达的 20011 后，其他表达是否变化；有没有 API 等同全局编辑。

**连接**

11. 同一连接上写入相互冲突的值：项目默认、PDP、信号、CDP、芯线，看 31004、31002 最终取谁，读 31047 是否反映来源。
12. 多个 CDP 冲突时的取值；线号 `?` 规则；检查运行报什么。
13. PDP 改属性后，不生成连接时连接是否已变（`H:connectionbrowsergui_h_pdpeigenschaften` 和 CDP 的差异）。
14. T 节点放到已有自动连线上是否拆线（A52 待验证）；角连接是否影响逻辑连接。
15. 24300、24301、20201 在项目中断点设置不同组合下的值（第三部分第 5 条）；移动中断点所在页后是否立即变化，还是要生成连接。

**页和结构**

16. 改页 11011 后立刻读对象的 19024、把对象移到另一页后再读；同时读 19022、19023、19005、19006。
17. API 建的无显示标识的函数：标识接管是否生效（放在同高度邻居右侧），读 20005、20006、20010、20431。
18. 路径文字：`20031` 在 API 放的函数上是否按帮助的搜索规则取值；改路径文字位置后是否立刻变化。

**基本项目**

19. 用示例项目另存基本项目，再新建，对比设置（中断点、交叉参考显示、图层、报表模板、编号方案）。确认哪些带入，尤其是图层。

**报表**

20. `Reports.Update(Page)` 后手工加在报表页上的文字是否保留；生成项目报表和逐页更新的差别；冻结后的行为；`DeviceTagNameParts` 限定（A84 遗留）。

**未知属性**

21. 运行时读 59456、59459 的 `PropertyDefinition`（A12 说属性定义可给出可读名称）；顺便确认它们出现在哪些对象、是否只读。

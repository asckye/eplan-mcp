# EPLAN 的设计思路

| 项 | 内容 |
|---|---|
| 状态 | 现行 · 最后核对 2026-11-04 |
| 适用 | EPLAN Electric P8 / Pro Panel 2026.0.3 |
| 相关 | [数据关联](data-relationships.md)、[已验证的 API 事实](api-notes.md) |

负责人要求先理解软件的设计思路，再谈绘图。工具、驱动和任务说明都按这里的思路设计；每个差异先找到数据模型里的源头，再决定怎么修。

## 来源写法与可信度

| 写法 | 含义 |
|---|---|
| `H:页名` | `https://www.eplan.help/en-us/Infoportal/Content/Plattform/2026/Content/htm/页名.htm`（2026 版帮助） |
| `H2.9:页名`、`H2022:页名` | 同上，换成对应版本（2026 版读不到或搜索只给出该版本时才用） |
| `API:页名` | `https://www.eplan.help/en-us/Infoportal/Content/api/2026/页名.html` |
| `TT-PLC属性 pN` | `https://eplan.help/techtipps/en-us/SPS/TechTip-Overview-of-the-PLC-properties.pdf` 第 N 页（官方 TechTip，2027 版，2026-04-08） |
| `TT-PLC主数据 pN` | `https://www.eplan.help/techtipps/en-US/SPS/TechTip-Preparation-of-master-data-for-PLC-data-exchange.pdf` 第 N 页（同上） |
| `XML:成员` | EPLAN 安装目录 `Bin` 下的 `Eplan.EplApi.DataModelu.xml` |
| `XMLHE:成员`、`XMLMD:成员` | 同目录的 `HEServicesu.xml`、`MasterDatau.xml` |
| `A12` 等 | api-notes 实测条目（实测优先） |
| `项目实测` | 本项目实测 |

标记：`[推断]` 是从多条事实归纳的；`[搜索摘要]` 只来自 WebSearch 的摘要，没有打开原页；`[未找到依据]` 是没查到。

两点限制：

- 帮助内容经小模型转述，不是原文；一次读属性总表时错配过一行 ID（已按 XML 改正）。写代码前，关键句子要再核原页。`plcgui_k_arbeitsweise` 已核对原文。
- `properties_o_main.htm` 只读到 ID 约 20079，更高的 ID 以 XML 和 `PropertyDefinition` 实测为准。

# 一、设计思路

## 1.1 核心模型：图形和数据谁是主体

官方没有一句话直接说“谁是主体”。**[未找到依据]** 下面是能查到的事实和归纳。

事实：

- 逻辑数据存在功能定义里，图形数据存在符号里。两者分开是为了工程化和标准无关。— `H:xfctdefbrowsergui_k_start`
- 功能是数据模型里最小的逻辑单元。连接点嵌在功能里，连接点逻辑（电位中断、PLC 目标跟踪等）也在功能内管理。— `H:xfctdefbrowsergui_k_prinzip`
- 对象有了功能定义就是功能。功能在页上有符号时才叫元件（component）。一个元件同一时间只对应一个功能。— 同上
- 图上放的永远是功能，不是物品（item）。物品是不可再分的器件部分，通常对应一个部件号。它影响部件管理，不影响复制、删除、“使用现有”这类逻辑行为。— `H:xfctdefbrowsergui_k_betriebsmittel`
- 项目是数据库，存页和用到的主数据（符号、图框、表格、部件）；`.elk` 只是指向 `.edb` 目录。— `H:projects_k_prinzip`
- 只删放置时，图形消失，未放置功能的数据留在数据库。— `H:devicelistgui_k_loeschenkopieren`
- 自动连线只是图形线，生成之后才有带数据的连接。— `H:connectionbrowsergui_k_arbeitsweiseverbindungen`
- 报表是对项目数据的查询。— `H:formgeneratorgui_k_start`
- 功能定义的类别（category）决定系统行为，例如可用属性和进哪种报表。用户不能新建或扩展功能定义库。— `H:xfctdefbrowsergui_k_funktionsdefinitionen`

但是，在逻辑页上**几何位置本身也是数据输入**：

| 几何关系 | 决定什么 | 出处 |
|---|---|---|
| 两个连接点水平或竖直正对 | 自动连线，进而决定连接 | `H:egedgui_k_start` |
| 符号在盒子（黑盒、PLC 盒）内 | 没有自己标识的功能取盒子的标识 | `H:blackbox_k_arbeitsweise`、`H:plcgui_h_spszeichnen` |
| 同一高度、搜索方向上的邻居 | 标识接管、插头代号接管 | `H:devicetaggui_k_bmkuebernahme`、`H:devicetaggui_k_bmkuebernahmesonder` |
| 所在路径上最靠上或最靠左的路径文字 | 自动功能文字 20031 | `H:gededitgui_k_pfadtextauswertung` |
| 所在页 | 完整标识的结构段 | `H:devicetaggui_r_bmdaten`（完整标识 = 当前环境的结构标识 + 显示标识） |
| 页顺序和页内左右顺序 | 同名中断点的默认配对 | A65 |
| 项目设置开启时的图形位置 | 连接的源和目标 | `H:connectionbrowsergui_k_prinzipverbindungen` |
| 所在列 | 交叉参考里的列号 | `H:xessettingsgui_k_grundlagen`、A65 |

**[推断]** 数据库里的功能是主体，符号是它可选的视图；同时逻辑页上的位置是计算逻辑关系的输入。所以画图不是摆图形，而是“建立或复用功能，再放到能推出正确关系的位置”。

同一个功能在三处的样子：

| 位置 | 看到的是什么 | 规则 | 出处 |
|---|---|---|---|
| 图纸 | 放置（元件），每种表达类型一个 | 同一功能可同时有多种表达，每种表达只能一次；黑盒、PLC 盒、电缆定义线这类没有自己连接点的功能例外，可同类型多次，但只有第一个放置计入其他类型的交叉参考 | `H:devicetaggui_r_symbolfunktionsdaten` |
| 导航器 | 项目数据的子集，树或列表，标出是否已放置 | External、Graphic 表达不显示，也不算项目的一部分 | `H:userinterface_k_besonderheitennavigatoren` |
| 报表 | 查询结果 | 多个表达的值不同时按固定顺序取；设备清单和部件清单只出主功能 | `H:reverseengineering_k_reihenfolge`、`H:gededitgui_k_pfadtextauswertung` |

官方承认两种工作方法，可以混用：**符号法**（复制页、加宏、放符号，不用管功能）和**设备法**（从设备出发，用预定义清单、导航器、使用现有功能）。改报表、编号或走设备法时必须理解功能。— `H:xfctdefbrowsergui_k_start`、`H:devicelistgui_k_start`。设备法以设备为起点：选设备再放它的功能，官方说这样能防错并支持合理性检查。— `H:devicelistgui_k_prinzip`

## 1.2 官方推荐的工程流程

**[未找到依据]** 没找到一页完整的官方总流程。帮助首页和信息门户没有 Getting Started、Workflow 章节（`H:EPLAN_Help_k_start`、`https://www.eplan.help/en-us/Infoportal/Content/htm/portal_home.htm`）。官方教程页 `https://www.eplan.help/en-us/Infoportal/Content/tutorials/electricp8/index.html` 只读到标题；搜索摘要显示其模块依次是建项目、放符号、自动连线（含电缆和部件）、放设备、宏、带占位符的宏、Data Portal、交叉参考。**[搜索摘要]**

能从各章拼出的顺序（拼接是 **[推断]**，每步本身有出处）：

1. **选基本项目建项目。** 每个项目都基于基本项目；它决定页结构和设备结构。— `H:projects_h_projekteerstellen`、`H:projectstructure_k_hinterg`
2. **准备主数据。** 部件管理里给每个部件建设备定义（功能模板），或给部件指定符号、宏。— `H:devicelistgui_k_prinzip`。官方建议用准备好的宏来标准化；宏不是必需的，但宏里的功能必须和部件的功能模板对得上。— `TT-PLC主数据 p21`
3. **（可选）宏工程。** 帮助里的说法是宏项目、宏框、变体、占位符对象，没有 macro technology 这个章节（只有 3D 宏一页，`H2.9:cabinetgui_k_3dmakros`，**[搜索摘要]**）。宏项目专门管理和生成宏，不支持跨页连接和全局编辑。— `H:macrosgui_k_makroprojekte`、`H:macrosgui_k_makrokaesten`。每种表达最多 26 个变体。— `H:macrosgui_h_makroserzeugen`。占位符对象让同一个宏带多组值。— `H:macrosgui_k_start`
4. **（可选）预规划。** 需要单独的模块；开发机授权里 `Preplanning` 不可用（A24）。五步是配置、建预规划、做详细规划（从预规划导航器拖到原理图页）、出报表、检查。— `H:planninggui_k_vorgehensweise`、`H:planninggui_k_start`
5. **设备法的详细设计。** 预定义设备、用导航器、用预定义或导入的设备清单、使用现有功能、再和预定义设备比对。— `H:devicelistgui_k_start`
6. **原理图。** 先把连接点定义为相连，后面才定义走线和材料，从笼统走向精确。— `H:connectionbrowsergui_k_arbeitsweiseverbindungen`
7. **PLC。** 见 1.6。机架结构用单线页且 PLC 盒为主功能；I/O 用多线；总览作为通道分配的图形化核对。— `TT-PLC主数据 p21-23`。也可以由宏自动生成：依次生成单线、多线、I/O 总览、机架总览。— `H2022:plcgui_k_spsschaltplanprinzip`
8. **编号、报表、检查。** 见 1.4。

## 1.3 导航器

| 导航器 | 管什么 | 不管什么 | 工程师的操作 | 出处 |
|---|---|---|---|---|
| 设备 | 除 External、Graphic 外的全部功能，可编辑全部功能的属性 | | 新建设备（默认只生成第一个主功能为未放置，其余模板留在导航器）、放置、指派、改属性 | `H:userinterface_k_besonderheitennavigatoren`、`H:devicelistgui_k_platzieren`、`H:navigatorgui_k_start` |
| PLC | PLC 盒、连接点、通道；可按通道分组 | | 新建（选功能定义，得到未放置项）、放置（Backspace 打开放置对话框选表达）、指派、使用现有连接点 | `H2022:plcgui_h_spsanlegen`、`H:plcgui_k_kanalorientiertarbeitsweise`、`H:plcgui_h_anschluesseabbuchen` |
| 端子排 | 只有端子排和端子 | 电缆、插头 | 放置、指派；可建未放置的端子排定义 | `H:userinterface_k_besonderheitennavigatoren`、`H:terminalgui_k_verwaltungleisten` |
| 电缆 | 只有电缆和屏蔽 | 端子排、插头 | 预定义未放置电缆，之后放到设备连接点或端子上 | `H:userinterface_k_besonderheitennavigatoren`、`H:cablegui_k_arbeitsweise` |
| 连接 | 已放置和未放置的连接 | 经 CDP 设置的间接属性（要到 CDP 上改） | 改属性（写回该连接的全部 CDP，没有就自动放一个）、跳到源或目标 | `H:connectionbrowsergui_k_arbeitsweiseverbindungen`、`H:connectionbrowsergui_h_eigenschaftenbearbeiten`、`H:userinterface_k_besonderheitennavigatoren` |
| 部件（部件清单 BOM） | 项目部件；列表按设备，树按部件 | | 更新设备数据、选部件 | `H:partselectiongui_k_prinzip`、`H2.9:partslistgui_d_artikelstueckliste`**[搜索摘要]** |
| 部件主数据、设备清单 | 系统部件；设备清单是独立的监控清单，不计入 BOM，不受同步和压缩影响 | | 拖到页上插入（带部件数据）或指派 | `H:userinterface_k_dragdropfunktionen`、`H:devicelistgui_k_geraeteliste` |
| 电位、中断点 | | | 拖到页上放置 | `H:userinterface_k_dragdropfunktionen` |

**“未放置”的三种状态**（`H:xfctdefbrowsergui_k_betriebsmittel`）：

| 状态 | 在哪里 | 怎么变 |
|---|---|---|
| 自由模板 | 属于部件，不属于项目，没有图形 | 放置或指派后，功能覆盖（overlay）模板 |
| 未放置功能 | 项目数据库里，没有放置 | 放置 |
| 已放置功能 | 数据库里且有放置 | 只删放置就回到未放置 |

还有第四种：属于器件但对不上模板的功能，检查运行可以标出。模板和功能的标识属性一致时才覆盖；模板上不能加额外数据。— `H:devicelistgui_k_prinzip`

**拖放的机制**（`H:userinterface_k_dragdropfunktionen`）：

- 放置（默认）：功能可以放任意多次，不管它已放置、未放置还是其他表达；生成的表达类型由目标页类型决定。N 切换同器件的功能，Tab 切变体，Shift+Tab 切表达。
- 指派（Ctrl+Shift）：把数据转给页上已有的元件。指派未放置功能时，元件原来的功能被替换；指派已放置功能时只传数据；指派模板时元件的功能覆盖模板。— `H:navigatorgui_k_zuweisenvonfunktionsdaten`
- 放置时，优先选覆盖模板最多、多余功能最少的宏，只看目标表达类型的宏。**图形数据**（符号、属性排列、接管方向）来自宏；**功能数据**以项目或模板为准，覆盖宏里的值；空值不覆盖。宏里配不上的功能作为附加功能插入，用同一标识，器件不会被拆开。— `H:devicelistgui_k_vorgehengeraeteplatzieren`
- 插入宏和放置是两回事：插入宏得到与现有功能无关的新功能；放置是用现有功能或模板，宏只提供图形。— `H:devicelistgui_k_platzieren`

API 对应关系见 R2，**和界面的放置有差异**。

## 1.4 官方设计成由软件完成的工作

“不该手工做”是 **[推断]**：官方写的是自动完成，没有写禁止手工。

| 工作 | 什么时候发生 | 工具只该写什么 | 出处 |
|---|---|---|---|
| 自动连线 | 连接点正对时，只在逻辑页；面板布局、总览、预规划页不连 | 放对位置；官方说只有自动连线才算符号之间的电气连接 | `H:egedgui_k_start`、`H:pagebrowsergui_k_seitentypen` |
| 连接对象 | 界面里：每个编辑步骤后更新当前页，切页时更新打开的页和经中断点相关的页；连接跨很多页所以不在线更新；也可手动 | CDP、电位点；经 API 要显式 `Generate.Connections`（A52） | `H:connectionbrowsergui_k_start`、`H:connectionbrowsergui_h_verbindungenbearbeiten` |
| 设备、触点、中断点交叉参考 | 编辑时自动插入；成对引用要满足条件 | 相同标识，20021 决定是否显示 | `H:xessettingsgui_k_grundlagen` |
| PLC 交叉参考 | 身份匹配时自动 | 卡标识、功能定义、插头代号、连接点代号 | `H:xessettingsgui_h_spsquerverweiseerzeugen` |
| 自动功能文字、确定的符号地址 | 计算（确定的符号地址要连接是最新的才准） | 路径文字或 20011；20402 | `H:gededitgui_k_pfadtextauswertung`、`H:plcgui_k_symbolischeadressen` |
| 器件编号 | 在线（插入或新建时）和离线 | 编号方案；避免手工算号 | `H:numerationgui_h_online` |
| 线号 | 连接编号填 31004，并按设置放 CDP | 编号方案 | `XML:CONNECTION_WIRENUMBER`、`H:connectionbrowsergui_k_vdp` |
| PLC 地址 | 插入和复制时自动编址，也可后续批量 | CPU 的地址方案；固定地址设“不参与编址”，且要设到每种表达上 | `H:plcgui_k_adressierung` |
| 电缆生成和芯线分配 | 命令；按连接点类型分配 | 定义线或自动生成方案 | `H:cablegui_k_prinzip`、`H:cablegui_k_start` |
| 报表 | 自动生成并填表，自动类型页（标 A）由 EPLAN 生成 | 报表模板、表格、筛选和排序方案 | `H:pagebrowsergui_k_seitentypen`、`H:formgeneratorgui_k_start` |
| 检查 | 在线（每步）和离线检查运行 | 配置方案；用它做验收 | `H:msgmanagementgui_k_start` |
| PLC 原理图 | 按宏和未放置功能自动生成 | 宏和配置 | `H2022:plcgui_k_spsschaltplanprinzip`、action `plcservice /TYPE:GENERATEPLCSCHEMATIC`（`docs/eplan/api-actions.md` 第 74 项） |

## 1.5 基本项目和项目模板

- 2026 版里每个项目都基于基本项目（`.zw9`）。基本项目的全部数据复制到新项目。— `H:projects_h_projekteerstellen`、`H:projects_k_basisprojekteerweitert`
- 带来的内容：项目设置（含页和设备结构方案）、项目数据（已放置和未放置设备）、全部页、项目里的主数据（表格、符号等）、存入的外部文档和图片。官方样例 `IEC_bas001.zw9`、`Num_bas001.zw9`、`GOST_bas001.zw9` 带符号库、表格、图框，区别是标识结构或标准。— 同上
- `.ept` 项目模板只在 2.9 帮助有专页（`H2.9:projects_k_projektvorlagen` **[搜索摘要]**；2026 版该 URL 返回 404）。2.9 及更早的模板可用 `XPrjConvertBaseProjectsAction` 转换。— `H:projects_k_basisprojekteerweitert`
- 结构可以事后改；缩减页结构不能造成重名页。— `H:projectstructure_k_hinterg`
- 图框：项目设置和页属性都能定，页属性优先。— `H:pagebrowsergui_k_eigenschaften`
- 图层：按项目管理，属性取当前项目；同一层在两个项目可以显示不同。— `H:layermanager_k_prinzip`。基本项目页没有点名图层。**[未找到依据：是否明确随基本项目带入]** 既然“全部数据复制”、图层又是项目数据，**[推断]** 会带入。
- 报表模板：官方示例是一个基本项目派生两个项目，各用自己的封面模板，靠项目筛选选择。— `H:formgeneratorgui_k_auswertungsvorlagen`
- 项目设置直接改变绘图结果的例子：中断点标签格式（A81、`H:xessettingsgui_d_einstellungenprojektabbruchstellen`）、PLC 交叉参考显示开关（`H:xessettingsgui_h_spsquerverweiseerzeugen`）、标识方案丢弃未用结构段（A13、A55、A88）、连接默认属性（`H:connectionbrowsergui_k_verbindungseigenschaften`）、在线编号（`H:numerationgui_h_online`）、编址方案（`H:plcgui_k_adressierung`）。

**[推断]** 目标项目和原项目的基本项目或设置不同时，“画得对”也可能显示不同。重绘应从同源设置开始。

## 1.6 PLC 总览和多线表达靠什么关联

**关联方式：身份匹配。** 文档没有提到两者之间存放指针。

- 下列属性都相同才算同一个点：**PLC 卡标识、功能定义、插头代号**（总线口是总线接口名 + 插头代号）**、连接点代号**。— `H:plcgui_k_arbeitsweise`、`H:xessettingsgui_h_spsquerverweiseerzeugen`
- 官方举的例子正是这一点：插头 X1 上的 A1:1 和插头 X2 上的 A1:1 是不同的点。— `H:plcgui_k_arbeitsweise`
- 地址的角色文档有矛盾，见第三部分第 1 条。
- 前提：总览页类型为 Overview，总览盒用相同标识，连接点放进盒内并继承标识；项目设置里“显示总览交叉参考”的复选框要全选。— `H:xessettingsgui_h_spsquerverweiseerzeugen`
- 找不到配对时检查运行报 P004005，官方列的原因包括总览点表达类型不对、总览盒标识改了、总览点被删。— `H:messages_p_004005`

**插头代号的作用：**

- 它只是识别信息，不是导航器里的独立对象，出现在报表里（如 `-A1-X1:1`）。— `H:plcgui_k_spsanschluesse`
- 同一卡上连接点代号可以重复，靠插头代号区分。— `H:devicetaggui_r_spsanschluss`、`H:plcgui_k_spsanschluesse`
- 可以从左边或上边接管，和标识一样；总览页默认向上搜。— `H:plcgui_k_arbeitsweise`、`H:devicetaggui_k_bmkuebernahmesonder`
- 手工值 20406；计算值 20431（只读）；组合值 20435（手工插头:连接点）、20183（自动插头:连接点）。— `XML:FUNC_PLCPLUG_DESIGNATION_MANUAL`、`XML:FUNC_PLCPLUG_DESIGNATION_AUTOMATIC`、`XML:FUNC_PLCPLUG_AND_CONNPTDESIGNATION`、`XML:FUNC_PLCAUTOPLUG_AND_CONNPTDESIGNATION`
- 官方把插头代号和通道代号列为填在宏里、项目中不再改动的数据。— `TT-PLC主数据 p22`
- 把插头拼进 `Name` 不等于设置插头：`=GAA+A1-D3KF5:-X10:1` 使 20022[1] 变成 `-X10:1`，20406 和 20431 仍为空。— A78

**地址、功能文字存在哪一边：**

| 数据 | 存在哪 | 另一边看到什么 | 同步 | 出处 |
|---|---|---|---|---|
| PLC 地址 20400 | 每个表达的点各自存，**[推断]** 依据：使用现有连接点时不同表达的地址会显示差异；“不参与编址”要设到每种表达 | 文档没说总览写入后另一边是否即时变化 | **没有专门规则**。可走全局编辑或同步（下文） | `H:plcgui_h_anschluesseabbuchen`、`H:plcgui_k_adressierung`、`H:plcgui_k_arbeitsweise` |
| 手工功能文字 20011 | 写在哪个表达就存在哪 | 总览点没有自己的文字时，**20031 显示对应多线点的自动功能文字** | 见下 | `H:gededitgui_k_pfadtextauswertung` |
| 通道数据（符号地址、功能文字） | 存在或算在带功能定义的 PLC 连接点上 | | | `H:plcgui_k_kanaele` |
| 手工符号地址 20402 | 各点 | | 总览到原理图时总是改原理图 | `H:plcgui_k_arbeitsweise`、`XML:FUNC_PLCSYMBOLICADDRESS_MANUAL` |
| 确定的符号地址 20403 | 计算 | 总览点没有连接，不能做目标跟踪，取对应多线或单线点的值 | 只有连接最新才准 | `H:plcgui_k_symbolischeadressen` |
| 配置项目、站、机架、槽位 20108、20420–20422 | 存在 PLC 盒 | 连接点上是只读的间接属性 | 计算 | `XML:FUNC_PLCCONFIGURATIONPROJECT_INDIRECT` 等 |

**同步规则：**

- 功能文字，原理图到总览：没有文字时传路径功能文字；也没有就不传。总览到原理图：有文字则覆盖；否则覆盖路径文字；都没有就填到连接点。— `H:plcgui_k_arbeitsweise`
- 符号地址，原理图到总览：原理图没有手工值时，传传感器或执行器搜索的结果。— 同上
- 自动确定的属性不被覆盖。— 同上
- 改原理图上的硬件数据（如连接点代号）不会改总览。— 同上。这正是身份匹配的后果 **[推断]**
- 手动同步：工具 > 同步 > 功能 > 某表达 > 所有表达；空值如何处理由项目设置决定（空值是否覆盖、有值是否填空）。— `H:adjustdata_h_funktionabgleichen`、`H:xessettingsgui_d_betriebsmittelallgemein`
- API：`Synchronize.OverviewToMultilineAndSingleline`、`MultilineToSinglelineAndOverview`、`SinglelineToMultilineAndOverview`。— `XMLHE:Synchronize`。Action：`synchronize /TYPE:OVERVIEW|MULTILINE|SINGLELINE`（方向未写明）。— `API:synchronize`
- 全局编辑模式（属性（全局））：一次改所有表达；Display、Symbol / function data、Format 页除外；只同步已有内容的字段，空字段保持空。— `H:reverseengineering_k_modus`、`H:reverseengineering_k_reihenfolge`。**[未找到依据：API 写属性是否等同全局模式]**；A13 的改名行为说明单个对象写入不会自动传播。

**2026-11-04 实测结论（`MacroBoxSpikeTests`，scratch 项目）：**

- 总览宏建立卡和总览点后，在原理图页插入同一部件宏的多线表达（`NumerationMode.None`），再用 `NameService.RenameDevice` 改成卡的标识：宏里已带插头代号（20406），两边立刻配对，`20301` 互相指向对方的页和路径。不需要生成连接。
- 在原理图点写 20011，总览点的 20031、20120 立刻变化；总览点自己的 20011 仍为空。功能文字只写原理图这一边。
- 在总览点写 20400，原理图点不变；调用 `Synchronize.OverviewToMultilineAndSingleline` 后才同步。示例项目里地址只在总览点上（52 个总览点，原理图 0 个），重绘时不同步。
- 只设相同 `Name`、不带插头代号的新功能不配对，符号 `PLC_CBOX` 的点地址也不传过去。
- 用 `PropertyDefinition` 读到的名称：20048 目标连接点的名称（完整，只读）、20301 总览关联参考（只读）、20404 符号地址（自动，只读）、20406 插头代号、20431 插头代号（自动，只读）、20468 宏、20470 功能的表达类型为“多线”（只读）、20477 所有放置（只读）、20108 配置项目（间接，只读）、20420–20422 站号、机架、插槽（间接，只读）、20121 表达类型、20051 把显示设备标识符当做完整设备标识符使用、20216 功能安全状态、31030 本质安全。

**和项目实测的对照：**

| 实测 | 文档解释 | 判断 |
|---|---|---|
| 原图同一个 `=K1+A2-KE1:2.3` 按插头代号区分（`KE1:1:2.3` 等） | 插头代号是识别属性，连接点代号在同卡可重复 | 一致。还要读 20406、20431、20022 确认 `1` 真的是插头，不是拼进连接点代号（A78）。 |
| 在总览点写地址，不传到没带插头代号的同名原理图点 | 少了插头代号就不是同一个点；就算匹配，也没有写入即传播的规则 | 一致 |
| 20011 基本只在原理图点上，总览点显示 20031、20120 | 总览点无文字时 20031 显示对应多线点的自动文字；20120 再回退到主功能 | 完全一致，且说明 20031 的值依赖配对成功 |

配套事实：

- A24：`=K1+A2-KE1` 的 `PLC.PLCTerminals` 有 34 个对象、17 个名称，同名对象一个有地址一个没有。**[推断]** 这就是同一通道的多线和总览两个表达。
- A78：卡在另一页时 20300、20301 可以都是空串，不能据此自己填文字。**[推断]** 原因是点未配上或显示设置未开，待实测。
- PLC 盒放置的主辅规则：单线宏里盒子勾主功能（`TT-PLC主数据 p23`）；一张卡可分布在多个盒，卡数据写在主功能盒上，其他盒的数据报表忽略（`H:plcgui_k_prinzip`）；已有已放置主功能时，再放的主盒变成辅助功能并带主功能的数据（`H:plcgui_k_abbuchverhalten`）。

---

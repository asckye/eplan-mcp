# [EPLAN API 笔记](../api-notes.md)：读取、快照与项目生命周期

### A168. 页面读取慢在属性文字的外框和重复读取图形连接，不在连接扫描（2026-11-19，EPLAN 2026.0.3.25421，Pro Panel，B 槽，示例项目只读）
- **起因：** `eplan_page_review` 读 `=EC1+A3&EFS1/1`（3870 个对象）要 9–15 秒；模型视图页 `=GQA+B2.Y1&MTC1/1`（38507 个对象）只要 2 秒左右。
- **分段耗时（同一页，毫秒，各次运行之间随机器负载相差一倍以上，比例一致）：** 页面几何 7995；图形连接 2384；引脚 2116；电缆清单 910；评审的连接表 899；连接定义点 323；逻辑端点 88。整项目的 `GetConnections(null)` 加 `Connection.Page` 过滤一次不到 0.1 秒，不是瓶颈。
- **页面几何里：** 时间几乎全在属性文字上（另一次运行：13989 中的 13317）。`PropertyPlacement.GetBoundingBox(false)` 占 9243，`GetDisplayString()` 占 2552。这页 3239 个属性文字里 2416 个显示为空；EPLAN 给它们的外框是一个点（2416 个）或读不到（8 个），没有一个有面积，却占外框时间的大半（1644 对有文字的 592）。
- **引脚和图形连接：** 两段各对每个 `SymbolReference` 读一遍 `GraphicalConnections`。合成一遍后引脚 735 个、线段 839 条与分两遍读的结果逐项相同，耗时约为两遍之和的一半。
- **结论：** 原理图页的读取时间取决于属性文字的个数，不取决于对象总数；显示为空的属性文字没有外框可言。
- **工具行为：** 页面读取对显示为空的属性文字不再调用 `GetBoundingBox`，外框记为 `[0,0,0,0]`（A53/A54 对读不到外框的空文字原本就这样记）；这些文字的对象摘要随之改变，同一版本内各次读取一致。没有刚写入对象的读取（评审等）一遍读完引脚和图形连接；写入路径不变（A13）。每次评审在插件日志写一行分段耗时（`page_review read, ms: …`）。
- **证据：** `PageReviewSpeedTests`：改前这页 15 秒以上（测试失败），改后 6.2 秒（通过）；同一次运行 `PageReviewTests` 8/8（1 个跳过）、`PlacementMoveTests` 2/2、`CableDefinitionTests` 2/2、`PlacementSymbolEditTests` 5/5、`ConnectionCreateTests` 25/25。剩余时间：几何 1.8 秒（显示文字和非空外框各约 0.65 秒）、引脚与图形连接 1.4 秒、电缆清单 0.8 秒、连接表 0.7 秒。
### A3. DataModel 行为（通过脚本在 scratch 项目上验证）
- **`Function.Create(Page, SymbolVariant, PointD, PointD)` 创建的功能没有名称**（名称为 `=+`），需要另外赋值 `Function.Name` 才有设备标识。赋值后名称按项目结构格式化，例如传入 `-K1` 得到 `=+-K1`。
- **通过 API 设置的设备标识，在图纸上不显示标识文字**（属性文本没有自动放置）。— 导出 PNG 观察。待查：需要调用哪个 API 才能生成属性放置。
- **两个引脚在同一轴线上相对放置时，EPLAN 会画出自动连线，但不会立刻生成逻辑 `Connection` 对象**；执行"生成连接"（`generate` 动作，TYPE=CONNECTIONS）之后才出现。— 读取 `Project.GetConnections` 前后对比。
- 新建的电路页（示例项目）栅格为 4 mm，幅面 420 × 297 mm。坐标单位为毫米，原点在左下角（y 向上增大）。— 实机读取。
- `StorableObject.ToStringIdentifier()` 形如 `33/17/69835/0`，**只在当前 EPLAN 会话内有效**，不能作为持久标识。
- 示例项目 `EPLAN_Sample_Project`：321 页、2876 个功能，按名称读取全部页面约 0.2 秒。

### A11. 只读 API 的实机验证（spike S6，2026-10-19，Electric P8，在 action 中）
示例项目的 scratch 副本：321 页、2876 个功能、2060 个连接、2900 个部件引用。详细数据见 [spike S6](../../../spikes/S6-read-api/README.md)。
- **性能**：查找和读取都在 100 ms 以内。`GetFunctions(null)` 第一次 64 ms、第二次 14 ms；读取全部功能的名称 18 ms；`GetConnections(null)` 30 ms；`GetArticleReferences(null)` 36 ms；单个功能的全部 1000 个属性 4 ms。— S6。
  - 对本项目的影响：Bridge 可以一次取出全部对象，再在内存中过滤和分页。
- **名称**：
  - `Page.Name` = `IdentifyingName` = `PAGE_FULLNAME`，形如 `=GAA+A1&EFS1/1`；`PAGE_NAME` 只是页号。
  - `Function.Name` = `FUNC_IDENTNAME` = `FUNC_IDENTDEVICETAG`，是带结构的完整标识，如 `=GAA+A1-FC1`；辅助功能带连接点后缀，如 `+A1-FC2:1`。
  - `VisibleName` 是页面上显示的标识（`-FC1`）：可能为空，可能带 `>` 标记，也可能等于完整标识。
  - 同一个短标识可以出现在多个结构下。
  - `FunctionsFilter.Name` 和 `PagesFilter.Name` 对完整标识做前缀匹配，**区分大小写**，不匹配 `VisibleName`。
  - — S6。
- **多语言**：多语言属性的原始格式是 `de_DE@…;zh_CN@…;`，**`ToString()` 返回整个原始串**。语言无关的值是 `??_??@值;`，`GetString(语言)` 返回空串。两种情况都应该用 `GetStringToDisplay(语言)`。数值按区域格式返回（`1,5`）。带索引的属性不带索引读取时抛出 `IndexedPropertyException`。— S6。
- **连接**：
  - `Connection3D` 的 `StartPin` / `EndPin` 抛出 `NotSupportedException`，`Page` 为 null。
  - 已过时的 `ConnectionsFilter.Page` 漏掉了连接（35 个，实际 40 个），按页列出应在内存中按 `Connection.Page` 过滤。
  - — S6。
- **读取的副作用**：只读探针前后，项目的设计数据文件没有变化。但**任何一次 action（哪怕只获取项目、不读任何对象）都会改写项目的 `User.eod` / `User.eox`**；打开项目后空闲时不会改写。调度器（action 之外）中是否也会改写，尚未验证。— S6，对比项目文件的大小和修改时间。
- **运行检查是写操作**：`Check.VerifyProject` 在示例项目上用时 3.9 秒，检查消息从 0 条变为 20 条，并改写了项目的 `Message`、`Project`、`Blob` 数据文件。读取已有的检查消息用 `PrjMessagesCollection`，元素类型是 `ProjectMessage`。— S6。
- 🔴 **系统消息集合不能交叉使用**：创建 `SysMessagesCollection` A 之后又创建集合 B，再读取 A 中消息的文本（`GetMessageText`），EPLAN 报"硬件错误：存储器访问受阻"（原生访问冲突，托管代码捕获不到），整个 action 失败；在 action 中 EPLAN 没有崩溃。只用一个集合时，读取全部 490 条消息都正常；先捕获 EPLAN 异常再读取也正常。— S6 r2、r3 中触发，r7 稳定复现。
  - 对本项目的影响：同一时间只使用一个集合，读出后立即复制到 DTO（实现入口见[架构](../../architecture/architecture.md#53-bridge-组件)）。
- `SysMessagesCollection()` 包含整个 EPLAN 会话的消息，其中有 Trace 级别的消息，以及一条级别为 7 的"有新版本"提示（7 不在 `MessageLevel` 枚举中）。— S6。
- **`ProjectOpen` 不一定改变当前项目**：已有一个项目打开时，用 `ProjectOpen` 再打开一个，`SelectionSet.GetCurrentProject` 返回的仍是原来的项目；`XPrjActionProjectClose` 关闭的也是原来的项目。— S6 第一次运行（驱动脚本因此关闭了工程师自己的项目，没有读取或修改它）。
  - 对本项目的影响：不依赖"当前项目"，按名称或路径找项目（实现入口见[架构](../../architecture/architecture.md#81-对象标识)）。

### A12. M2 只读工具的实机验证（2026-10-19，EPLAN 2026.0.3.25421，Electric P8）
夹具：`EPLAN_Sample_Project` 的 scratch 副本，位于 `%USERPROFILE%\EplanMcp\scratch\fixtures\`。工具通过 Release 构建的 `eplanmcp` 调用（`feat/message-check-session-tools`，13 个工具），插件为同一提交的 Release 构建。
- **夹具的做法**：直接复制 `.elk` 文件和 `.edb` 目录即可，EPLAN 正常打开（M2 设计 4.6 第 6 项）。`EPLAN.exe /Variant:"Electric P8" ProjectOpen /Project:"<.elk 的路径>"` 在启动时打开它，EPLAN 随后保持运行，与 `EplApiModuleAction` 的命令行形式相同。— 实测。
- **插件按变体注册**：Electric P8 下原先没有注册插件，EPLAN 能启动，但既没有管道，系统日志里也没有任何插件记录。用 `eng\scripts\eplan-addin.ps1 -Action register -Variant 'Electric P8'` 注册后，每次启动都会加载。— 实测。
  - 对本项目的影响：连不上时先看是否在当前变体注册过插件。`eplan_session_status` 的提示，以及 M5 的 `install-addin`，都要说明"每个变体各注册一次"。
- **13 个工具全部成功**，单次调用最慢的是 `eplan_project_get`（`detailed`，493 ms），其余都在 250 ms 以内：321 页、504 个设备、2060 个连接、2900 个部件引用、314 种部件。
  - `-FC1` 出现在 15 个设备上，`eplan_device_get` 返回 `AMBIGUOUS_NAME` 并列出候选的完整标识；
  - 连接的 `colour`（取自 `CONNECTION_WIRENUMBER`）在示例项目中是颜色代码（`OG`、`BK`），与 S6 一致；在其他项目中的含义仍待确认（[`eplan_connection_list` 规格](../../tools/eplan_connection_list.md)第 3 节）；
  - 示例项目没有检查消息，`eplan_check_list` 返回空列表和提示；
  - `eplan_session_select` 选中当前进程。
  - — 实测（`dotnet run` 的 MCP 客户端脚本，不在仓库中）。
- **读取没有副作用**：调用全部 13 个工具前后，夹具 `.edb` 中所有文件的修改时间和大小都没有变化；EPLAN 系统日志只新增了数据库打开、关闭的信息。— 实测。
- **部件描述只有德文和部分英文**：示例项目中 `ARTICLE_DESCR1` 没有中文。界面语言为中文时，`GetStringToDisplay` 返回空串，**不会回退到其他语言**，所以 `eplan_part_list` 和 `eplan_device_get` 的部件 `description` 都是 null；传入 `language: "de_DE"` 时全部都有，传入 `en_US` 时部分有。页描述有中文，可以正常显示。— 实测。
  - 已按[语言回退设计](../../design/2026-10-19-text-language-fallback.md)实现，实测：省略 `language` 时，西门子的 33 种部件中，前 5 种有 4 种返回英文描述、1 种只有德文（`descriptionLanguage: "de_DE"`）；显式传入 `zh_CN` 时仍是 null，没有标记字段；中文页描述不触发回退。`=GAA+A1-FC1` 的 detailed 结果中 281 个属性有 7 个用了回退语言（部件名称、部件描述），项目的 308 个属性和页的 79 个属性没有回退；读原始串（`PropertyValue.ToString()`）找候选语言的做法在各种类型的属性上都没有报错。
  - 回退后 `text` 过滤匹配的是回退语言的文本：`eplan_part_list` 用德文 `Leistungsschalter` 找不到英文描述的部件，要传 `language: "de_DE"`。
- 部件数据中有一个部件号是一个空格（`" "`），被 10 个引用使用，这些引用既不在设备上，也不在导线上。— 实测。
- **Large 测试**（`tests/EplanMcp.Large.Tests`，19 个，约 11 秒）把以上数据固定下来，并确认了几项以前没有验证的行为：
  - 项目的 detailed 属性中，属性定义给出可读的名称（超过 90% 不是纯数字），带索引的属性按索引逐个返回；
  - 在 Bridge 调度器中（不在 action 中）读取检查结果和系统消息，与其他读工具一样不改动项目文件；
  - 列出 200 页（读取全部 321 页的页描述）约 90 ms，项目的 detailed 结果约 170 ms。
  — 实测（2026-10-19）。
- **带过滤器的 `GetFunctions` 会多返回同名的主功能**：不带过滤器时，示例项目每个完整标识只有一个主功能（504 个）；带 `FunctionsFilter.Page` 或 `FunctionCategory` 时，还会返回没有 `VisibleName` 的同名主功能。黑盒里的功能（`=EC1+A3-EC1-BT2`：`Blackbox` 加一个 `AnalogSensor`）和第二种表达（`=EC1+A3-EC1-MA2` 的两个 `Motor`）都是这样。`=EC1+A3&EFS1/1` 页因此有 95 个主功能，但只有 85 个不同的标识。— 实测（`eplan_device_list` 的 `page` 和 `category` 过滤，与不过滤的列表对比）。
  - 对本项目的影响：设备列表和页上的设备按完整标识合并为一行，保留有显示标识的那一个（`DeviceTags.OnePerTag`）。

### A31. 新建项目、还原项目、判断项目版本（spike S27，2026-10-21，EPLAN 2026.0.3.25421，Electric P8，在调度器中）
详细数据见 [spike S27](../../../spikes/S27-project-create/README.md)。全部写入都在 scratch 中。
- `ProjectManager.CreateProject(链接文件, 基本项目 .zw9)`：3.5 秒，生成 `.elk` 和 `.edb\`，新项目处于打开状态（API 独占打开），带模板的页和属性（`GB_bas001`：1 页）。— S27。
- 🔴 `restore /TYPE:PROJECT` 还原到已存在的项目时不报错，照样覆盖（89 个文件重写）；与主数据还原跳过已有文件相反（A30）。还原一个示例项目备份约 6 秒。— S27。
- `ProjectManager.GetProjectDataCompatibility(path)` / `GetProjectDatabaseVersion(path)` 不打开项目，0.02–0.3 秒；2026.0.3 的项目为 `IsCompatible`、`18.0`；路径不存在时抛出 `ProjectNotFoundException`（S063073）。— S27。
- `.zw9`、`.zw1` 是 7z 归档。项目 `.edb\ProjectInfo.xml` 是项目属性快照，`id="10043"` 为最新更改或更新的 EPLAN 版本（`2026.0.3`），按 `id` 取（`name` 随界面语言变化）；其中有创建者的个人信息，只取需要的字段。— S27。

### A32. 对象标识与整个项目的读取耗时（spike S28，2026-10-21，EPLAN 2026.0.3.25421，Electric P8，在调度器中）
详细数据见 [spike S28](../../../spikes/S28-identity-extract/README.md)。只读。
- 同一项目的两份文件副本中，825 个对象（全部主功能和页）的 `StorableObject.ObjectIdentifier` 全部相同；`DatabaseIdentifier` 按打开的项目分配（28、53）；`ToStringIdentifier()` 为 `数据库号/类型号/对象号/0`。可以用"项目身份 + 类型号 + `ObjectIdentifier`"跨副本定位；编号可能在删除后被重用，执行前要核对名称、页、类型。`backup`/`restore /TYPE:PROJECT` 还原出的新项目、`eplan_export_pxf` + `eplan_import_pxf` 导入的新项目中，825 个对象的编号也全部相同（`DatabaseIdentifier` 不同）；从基本项目新建、在 EPLAN 中复制项目未验证。— S28 r0、r1。
- 示例项目（321 页）：`GetPages` 0.01 秒，`GetFunctions(null)` 2,876 个 0.01–0.04 秒，`GetConnections(null)` 2,060 个约 0.06 秒，`GetPlacements(null)` 44,735 个 0.6–0.8 秒；读全部功能的名称、页、坐标、编号 0.06 秒。— S28。

### A34. 对象编号与项目生命周期（spike S30，2026-10-21，EPLAN 2026.0.3.25421，Electric P8，在调度器中）
详细数据见 [spike S30](../../../spikes/S30-identity-lifecycle/README.md)。写入只在 scratch 中。
- `PROJ_GUID` 是"名称@Unix 秒数"，不是 GUID：文件副本和 `backup`/`restore` 还原的项目沿用原值（示例项目 `ESS_Sample_Project-Kopie@1523807309`），PXF 导入和 `CreateProject` 生成新值（导入或新建的时刻）。它不能区分同一项目的副本。— S30。
- 同一份项目文件 `Close()` 后再 `OpenProject`，825 个对象的 `ObjectIdentifier` 全部不变，`DatabaseIdentifier` 改变；关闭约 0.15 秒，打开约 0.8 秒。页的编号单调递增：回滚掉的、删除的编号都没有再分配，关闭再打开后接着递增（本次观察）。— S30。
- 从同一个基本项目新建的两个项目，页的编号相同（都是 1）：`ObjectIdentifier` 只在一个项目内唯一。`IEC_bas003_sample_project.zw9` 新建的项目是 0 页。— S30。
- 🔴 API 新建的项目：对它再调用 `OpenProject` 抛出 `ProjectOpenException`（S063045）；`ProjectManager` 释放后，用 `CreateProject` 返回的对象 `Close()` 抛出 `ObjectNotCreatedException`（S063080）；调用结束后项目不再打开，之后可以正常打开，经 `OpenProjects` 重新取得的对象能关闭。— S30。
- 经 Bridge 操作 `CreateProject` 新建的项目，调用结束后仍然打开（与 S30 spike 的观察不同）；用新的 `ProjectManager` 从 `OpenProjects` 重新取得它后 `Close()` 可以关闭（S30 r3）。— 2026-10-22，M6 的 Large 测试 ProjectCreateTests。

### A35. 工程包涉及的文件（2026-10-21，只读查看开发机上 EPLAN 2026.0.3 的数据目录和 scratch 中的 spike 文件，不启动 EPLAN）
M6 资源扫描器（`EplanMcp.Packs.Scanning`）不靠 EPLAN 读出的内容以这里为准。
- **扩展名的来源**：API 文档写明的有 `.zw1`（`backup /TYPE:PROJECT` 的归档）、`.fn1`（图框，`MDTYPE:STANDARDSHEET`）、`.fc2`（轮廓定义）、`.epj`（PXF 项目）、`.emp`（示例 `ModelView.emp` 放进宏目录）、`.slk`（符号库）；`.ema` 由 S9/S10 实际插入验证；`.elk`/`.ell`/`.elr` 见 A3、A28；`.zw9`（基本项目）见 A31。`.ems`（符号宏）和表格 `.f01`–`.f99` 只在数据目录中观察到，API 文档没有写，**未经 EPLAN 验证**。
- **XML 主数据**：宏、表格、图框、轮廓定义都是 UTF-8 XML，根元素 `EplanPxfRoot`，属性 `Type` 和 `Version`（写入时的 EPLAN 版本，如 `2026.0.25356`，旧文件 `2.7.11561`）。数据目录中：`.ema` 695 个、`.ems` 50 个都是 `Type="WindowMacro"`（**符号宏文件也标为窗口宏**），`.emp` 35 个 `PageMacro`，`.f01`–`.f48` 147 个 `Form`，`.fn1` 35 个 `Frame`，`.fc2` 21 个 `ContourDefinition`。属性 `SourceProject` 是制作者机器上的项目路径，不能抄进清单。
- **链接文件**（`.elk`、`.ell`、`.elr` 格式相同）：UTF-16LE 带 BOM，第 1 行是数据目录名，第 2 行 `18.0`，与 `GetProjectDatabaseVersion` 的结果相同（A31）。复制出来的项目第 1 行仍是原名（`S12_Sample.elk` 写的是 `EPLAN_Sample_Project.edb`），EPLAN 照样打开了旁边的 `S12_Sample.edb`（S12），所以数据目录按链接文件自己的名字找。
- **`ProjectInfo.xml`**：除 A31 的 10043 外，`id="10088"`（"Internal: Highest used Eplan version"）是四段版本，示例项目为 `2026.0.1.25370`（10043 为 `2026.0.1`）。用 2026.0.3 的 API 写过的副本（`LargeWrite`）两项仍是 2026.0.1：写入不一定更新它们。
- **7z 归档的内容**（7-Zip 26.03 只读列出）：`.zw1` 中是 `.edb` 的文件，另有 `A_BAKINFO.XML`（`<prodversion>2026.0.3.25421</prodversion>`、`<appvariant>`、备份注释）、`_COMMENT.TXT`、`_elkfile.elk`；`.zw9` 另有 `_BAKINFO.XML`。两者都**不含 `.lck` 锁文件**（S12 的数据目录 313 个文件中 109 个是 `.lck` 或 `User.*`），但含项目和各子库（`.sdb`、`.fdb`）的 `User.eod`/`User.eox`。所以工程包的项目资源不带 `.lck`；`User.eod`/`User.eox` 随项目带上但不计入哈希，因为 EPLAN 打开、导出、关闭时都会改写它们（A16、A25、A30）。文件头 `37 7A BC AF 27 1C 00 04`（7z 格式 0.4）。读取其中的版本需要 7z 解码（LZMA2），.NET 没有内置。
- 🔴 **复制项目时不能丢掉 `User.eod`/`User.eox`**：`.edb` 中缺少它们（以及 `.lck`）的项目副本打不开，EPLAN 弹出"无法打开项目 … 必须修正数据库索引"的模态对话框（`#32770`），在工程师关闭它之前挡住 EPLAN。所以复制、还原项目时保留它们，打开前先核对；打开卡住或超时可能是这个对话框，不要替工程师点它。复制进工程包的项目也要带上这两个文件（它们仍不计入哈希）。— 协调人实测（2026-10-21）。

### A36. 快照提取：耗时、内存、确定性与修改标记（spike S31，2026-10-21，EPLAN 2026.0.3.25421，Electric P8，在调度器中）
详细数据见 [spike S31](../../../spikes/S31-snapshot/README.md)。
- 🔴 `UniversalPropertyList.ExistingIds`/`ExistingValues` 和属性访问器返回的 `AnyPropertyId`、`PropertyValue` 都是 `IDisposable`，批量读取时必须逐个释放：不释放时读两遍示例项目全部属性（共约 1,000 万个值），EPLAN 记下"存储空间不足"后退出。释放后工作集在调用结束时回落。— S31。
- 🔴 不要在释放 `对象.Properties` 返回的属性列表之后再读同一对象的 `Properties`：2026-10-22 的快照代码先 `using (f.Properties)` 读一组属性，再对同一功能取 `f.Properties` 读 `FUNC_PLCADDRESS`，EPLAN 在 `UniversalPropertyList.getProp` 中访问冲突后退出（无法捕获）。每个对象只取一次属性列表，读完后释放一次。— M6 快照的 Large 测试。
- 属性的类型化访问器（如 `FunctionPropertyList.FUNC_PLCADDRESS`）在对象没有这个属性时抛 `PropertyNotFoundException`（S063109 属性无效），例如普通端子上的 PLC 属性；按对象逐个读登记属性时要逐个捕获它，当作没有值。— 2026-10-22，M6 快照的 Large 测试。
- 读示例项目每个页、功能、连接的全部已有属性：514 万个值，约 8–10 分钟，150 MB 文本，最慢的一页约 50 秒；只读页属性加功能全名和连接两端：约 5 秒。`UniversalPropertyList` 的构造函数不公开，用各对象的 `Properties`（派生自它）。`Connection` 不是 `Placement`，不在 `Page.AllPlacements` 中，用 `Connection.Page` 归页。— S31。
- 同一次打开中两次提取逐字节相同；关闭再打开后，值为运行时字符串标识的属性（如 2000：`53/4/21/0` → `89/4/21/0`）随库号改变。只读提取不改写项目 `.edb` 中的文件（不含 `.lck`）。— S31。
- 改一页的描述并提交后，`PROJ_LASTMODIFICATIONDATE`（Unix 秒数）/`TIME` 和这一页的 `PAGE_LASTAUTOMODIFICATIONDATE`/`TIME` 改变，其他页不变，`PAGE_LASTMANUMODIFICATIONDATE` 不变；这一页的 `PROPUSER_LAST_USERCODE`（3010）同时被改写。3010–3013 是最后修改人的个人信息。— S31。
- 🔴 项目 `.edb` 中缺少 `User.eod`、`User.eox`（连同 `.lck`）时，EPLAN 打不开这个项目，弹出模态对话框"无法打开项目……必须修正数据库索引"，在关闭之前 EPLAN 不处理别的请求。复制、还原、打包项目都不能去掉这两个文件。— 2026-10-21，S31 之后在 scratch 副本上验证。

### A38. EPLAN 事件（spike S33，2026-10-21，EPLAN 2026.0.3.25421，Electric P8，在调度器中）
详细数据见 [spike S33](../../../spikes/S33-events/README.md)。
- `new EventHandler(名称)` 加 `EplanNameEvent` 订阅事件；通配符 `*` 收到全部事件及其名称，`onActionStart.String.*`、`onActionEnd.String.*` 也有效。事件都在主线程上、在操作内部同步触发。— S33。
- 修改项目时触发 `onDMObjectsAltered`、`onPreCommitTxn`、`onPreCloseUndo`，被 `SafetyPoint` 回滚的修改也触发；参数不是字符串（类型未查明），不说明改了哪里。打开项目触发 `API.PreOpenProject`、`Eplan.EplApi.OnPostOpenProject`（参数为项目路径）、`onPostOpenProject` 等，关闭触发 `onPreCloseProject`、`onPostCloseProject`。`Page.ConnectionDirty` 既不是事件也不是 API 成员（只有 `ChangeInfoService.ConnectionDirtyActive`）。— S33。
- `ProjectManager.OpenProjects` 要在 `LockingStep` 中调用，否则抛出 `NoLockingStepException`（S063110）。— S33。

### A42. 通用 Project/Page 属性读取（P3b，2026-10-25）

- 实机：EPLAN 2026.0.3（Electric P8），只读的 sample fixture，2026-10-25 跑 `PropertyReadTests`、`PropertyReadLifetimeTests`。`UniversalPropertyList.Property(AnyPropertyId[, index])` 在 C# 中写作 `list[id]` / `list[id, index]`；数字 ID 隐式转换成可释放的 `AnyPropertyId`。对象只取得一次 Properties，所有 ID、父值/索引值、定义、边界和多语言值分别释放；finder 返回的 Project/Page 是借用对象，不释放（A36/A39）。
- `PropertyDefinition` 的 Type、IsReadOnly、IsIndexed、MaxIndex、IsNamePart、IsInternal、IsPropertyLicensed 在实例上可读。样例页的定义：11005 Bool、11006 Long、11015/11071/25101 String、11031/11032 MultilangString、11049 Bool 且 IsReadOnly、11051 Double、25020 String 且 IsReadOnly；均 IsInternal=false、IsPropertyLicensed=true。IsComputed、长度、单位/格式没有可读的定义事实，保持 unknown；不能由只读性推断 computed。
- 编译核对 `ToBool`、`ToInt`、`ToDouble`、`ToString`。Time 的时区、ValueWithUnit 的单位/格式、枚举解释未核实，返回 opaque 的安全 display，不猜测或按区域解析。
- 空与不适用：上面 10 个页属性在样例页上都存在（有值或为空），读不到“运行时不适用”的样本；`not_applicable` 只在 `list[id]` 抛 PropertyNotFoundException 时出现，不把空值说成不适用。目录外或被分类排除的 ID 在读取前就是 `read_refused`，不到达 EPLAN。
- 索引：项目 10901（PROJ_SUPPLEMENTARYFIELD）在样例上有已存索引，枚举结果与逐个显式读回一致。页 11901（PAGE_SUPPLEMENTARYFIELD）IsIndexed=true、MultilangString，样例页上没有任何已存索引，读作一行 `empty`、不带索引号。
- 多语言：`MultiLangString.GetLanguageList(ref LanguageList)`、`LanguageList.get_Language(int)`、`GetString` 可用；LanguageList 继承 ArrayList，不需释放，转换用的 ISOCode 逐个释放。指定语言只返回该语言（缺失不借用其他译文）；省略语言时按 GUI 语言并报告实际回退（样例中能找到回退样本）。
- A36 门：同一批 Project/Page 属性预热 10 轮后重复 200 轮，结果逐轮相同，EPLAN 工作集回落到基线容差内（max(32 MiB, 5%)），`.elk` 与 `.edb` 设计数据 SHA-256 不变（按 A17/A35 排除 User.* 与 .lck）。

### A43. 用 Page.CopyTo 放大示例项目、全项目名称枚举的规模（spike S37，2026-10-25，EPLAN 2026.0.3.25421，Electric P8，在调度器中）

- `Page.CopyTo(Project, PagePropertyList, PageMacro.Enums.NumerationMode, bOverwrite: false, bDontUseProgressBar: true)` 在同一项目内复制整页内容，热身后约 80 ms/页（首次约 0.6 s）。新页名用源页 `NameParts` 复制出的 `PagePropertyList`，再改写 `DESIGNATION_PLANT` 与 `DESIGNATION_LOCATION` 得到；只改 `=` 时，按位置命名的设备（如 `+A1-WE1`）保持原名，会与源页设备同名合并。5 种 NumerationMode（Ignore、None、Number、NumberWithQuestionMark、NumberPreferPrefix）都不改这些名称；同时改 `+` 后只剩无名称的 `+` / `=+` 功能和显式定位的电缆共用名称。已有功能从未被改变。
- 页名部分为空时（如 `&AAA1/1` 没有 `=`），`PropertyValue.ToString()` 抛 `EmptyPropertyException`；先判断 `IsEmpty`。
- 流体页（示例中的 `&PFB`，36 页）的 CopyTo 抛 `InvalidHandleException`（“可能已删除对象”），推测与授权有关，未核实。
- 在 SafetyPoint 中复制、未提交的批次回滚后完全消失，计数与批前一致；提交的 1,000 页副本正常退出、重启重开后计数一致。
- 同名同版本的插件程序集不会在同一 EPLAN 进程内重新加载：注销再注册仍执行旧代码，须重启 EPLAN 或换程序集名（与 S8 的做法一致）。
- 规模（示例派生，约 17 standard 对象/页）：1,000 页 6,141 功能、133,228 放置、265 MB；3,000 页 24,572 功能、395,684 放置、464 MB；8,800 页 77,271 功能、1,161,136 放置、62,201 个不同功能名、1.04 GB。finder 名称元数据 + 全部放置的合并枚举约 0.4–0.5 s（1,000 页）、2.2 s（3,000 页）、7.5 s（8,800 页，GetPlacements 5.7 s），随页数线性增长，所以万页项目不能在每步 guard（10 s）里做全量枚举（大项目计划设计 4.1）。
- 观察（原因未查）：同一 EPLAN 中打开多个项目并经 Bridge 读写后，`eplan_project_list` 报告的当前项目会变成另一个打开的项目（两次都是 LargeWrite）；需要固定当前项目的测试应只打开该项目。

### A44. 通用 Function/Connection 属性读取（P4，2026-10-25，EPLAN 2026.0.3.25421，Electric P8，只读 sample fixture）

- 设备标识按精确全名选主功能（可加页限定）；功能来源是 finder 加各页放置（A12），按 type/oid 去重。同一全名有多个主功能时报 AMBIGUOUS_NAME 并给出最多 20 个候选 locator，不默认取第一个。locator 读取复用 A32/A34 的项目/type/oid/名称/页/类别/位置核对，名称变化报 PLAN_STALE（metadata `check=name`），不按名称重新解析。
- 读取权限按对象实际类型判断（`ReadRefusal(scope.ObjectType, id)`），被拒绝的 ID（3010–3013、2000、目录外、另一对象类型的 ID）不返回值、定义、显示、译文或语言。
- 样例中带两种语言 FUNC_TEXT（20011，运行时 MultilangString）的 26 个唯一主功能与带 FUNC_TECHNICAL_CHARACTERISTIC（20027）的 63 个是不同的设备，没有一个两者都有。20011 的全部存储译文、显式语言读取与独立快照一致。
- **20027 的运行时类型既不是 String 也不是 MultilangString**（是 Point/Time/Variable/ValueWithUnit 之一，未细分），按 4.3 返回 `opaque`：没有类型化值，只有安全显示文本，等于快照的 ToString 读数。它进入任何写入批次之前，必须先确定确切类型与格式。
- 连接 locator 可读 31002（截面）、31004（颜色/编号）、31011（标识）的实例定义与值/显示。
- 按对象定位要枚举全项目的功能与放置（与 `EplanObjectFinder` 相同；`StorableObject.FromStringIdentifier` 未经验证）；按 S37 的枚举耗时推算 3,000 页约 2–4 s/次（未单独测），是已知的性能余量项。
- A36 门：Function（设备标识与 locator）和 Connection 同批预热 10 轮、重复 100 轮结果一致，EPLAN 工作集回落到基线容差内，设计数据 SHA-256 不变。

### A46. 项目打开、当前选择与导航器顺序（P25，2026-10-25，EPLAN 2026.0.3.25421，Electric P8）

- 打开名称排在当前项目**之后**的关闭项目，当前项目不变（夹具 `EPLAN_Sample_Project` 当前时，`LargeWrite`、`LargeOutside`、`P25Acceptance`、`P25o_*`、`P25t_*` 都如此）；带不带 `/Quiet` 相同。
- 重开被其他测试改过语言设置的项目时，EPLAN 可能弹出以项目名为标题的模态提示（如“字典中没有语言 'is_IS'”）并阻塞后续调用；会被其他测试改写的共用副本 `LargeWrite` 不宜在测试中关闭再重开。
- 名称排在当前项目之前的 `0P25t_<hex>` 被打开后成为当前项目。只是一次观察，不作为通用规则。
- `XPrjActionProjectClose` 关闭当前项目，随后页导航器的第一个项目成为当前项目。导航器按项目名排序（不区分大小写），同名项目按打开顺序。`eplan_project_list` 按路径排序，只能用它的 `current` 判断当前项目。
- 没有项目打开时，打开一个项目会使它成为当前项目。
- 对已经打开的项目再执行 `ProjectOpen` 会弹出“项目已经打开”模态对话框并阻塞 EPLAN；打开前先确认目标已关闭。
- 关闭项目会改写该项目的 `ProjectInfo.xml`，页内容不变；比较关闭过的项目的设计文件时要排除它。
- `ExecuteScript` 的脚本不能引用 `Eplan.EplApi.DataModel`、`Eplan.EplApi.HEServices`（CS0234）；EPLAN 自动加入 `System` 和 `Eplan.EplApi.Scripting`，重复 using 报 CS0105。编译失败时 eplremote 返回 1。
— 协调人经 `eplremote` 和 `eplan_project_list` 实测，P25 Large 的导航按此实现（见 tests/EplanMcp.Large.Tests/README.md）。

### A48. FUNC_TECHNICAL_CHARACTERISTIC 20027 的运行时类型（S39 r1，2026-10-26，EPLAN 2026.0.3.25421，Electric P8，只读 sample 夹具）

- 50 个非空样本的 `PropertyDefinition.Type` 都是 `ValueWithUnit`（数值 26），都不建索引；20011 `FUNC_TEXT` 对照为 `MultilangString`。A44 的“未细分”到此确定。
- 存的是自由文本，不一定带单位：`14 W`、`16 A`、`DC 24V`、`3`、`I/O Board`、`Inverter`。`ToString()` 等于显示文本；`ToMultiLangString()` 只有一条与语言无关的值（`??_??@14 W;`）。
- **不能按数值读**：`ToDouble()`/`ToInt()` 对 `14 W`、`16 A` 返回 0，只有纯数字正确；`ToBool()`、`ToPointD()`、`ToTime()` 不抛异常但无意义。读取适配器继续只给显示文本（`opaque`），写入时按与语言无关的文本处理，写入行为留给属性写入 P7 的 Large 验证。
— 协调人经 spike S39（`PropertyDefinition.Type` 与各转换成员逐项调用，A36 生命周期纪律）实测。

### A60. 通用对象读取的类型与属性入口（2026-10-30，核对 2026 官方 API 文档及本机 XML，不运行 EPLAN）
- 2026-11-02 离线核对随仓库发布的 `catalog/eplan-2026.0.3/types.jsonl` 与数据字典：`FunctionPropertyList` 继承 `FunctionBasePropertyList`，后者声明 1140、1240、1340、1740、1640；`PagePropertyList` 继承 `DocumentBasePropertyList`，Page 字典直接声明这五项及 1540，均为 readable。1540 只在 Page、PlanningSegment 字典声明，不在 Function 的属性列表继承链中；不得跨不相关字典放行。此为目录证据，未运行 EPLAN，不证明实机各值存在。同日实机（B 槽 Pro Panel，示例项目）：`CabinetScopeTests` 通过、`ObjectReadTests` 14/14；通用对象读取的属性列表仍只列对象自身类型的属性，否则 `Function` 的续读块超出 Host 校验。
- Terminal、PLC、BoxedDevice 继承 Function 并使用 Function 属性字典；BoxedDevice 即黑盒。`GetTerminals(null)` 同时返回普通端子和 PLC 端子，须按 `Eplan.EplApi.Base.Enums.FunctionCategory.PLCTerminal` 区分连接点；PlcIO 是可分配给端子的空闲地址条目，分配后条目删除。— [Terminal](https://www.eplan.help/en-us/Infoportal/Content/api/2026/Eplan.EplApi.DataModelu~Eplan.EplApi.DataModel.EObjects.Terminal.html)、[BoxedDevice](https://www.eplan.help/en-us/Infoportal/Content/api/2026/Eplan.EplApi.DataModelu~Eplan.EplApi.DataModel.BoxedDevice.html)、本机 `Eplan.EplApi.DataModelu.xml` 的 GetTerminals / PlcIO。
- `GetTerminalStrips(null)`、`GetPLCs(null)`、`GetBoxedDevices(null)`、`GetInterruptionPoints(null)` 返回相应类型；电缆、占位符、电位定义可用对应 `Get*WithFilterScheme(null)` 的无筛选导航器枚举。电缆枚举还含屏蔽功能，须精确匹配运行时类型。— 本机 XML 的 DMObjectsFinder 各成员；[DMObjectsFinder](https://www.eplan.help/en-us/Infoportal/Content/api/2026/Eplan.EplApi.DataModelu~Eplan.EplApi.DataModel.DMObjectsFinder.html)。
- LocationBox 是结构盒；MacroBox 是已放置的宏盒，都可从页 AllPlacements 筛选，使用各自 Properties。TerminalStrip / Cable 等隐藏基类 Properties；**TerminalStrip 禁止访问 Function / FunctionBase / Placement.Properties**，必须先匹配派生类型。— 本机 XML 的各 Properties 与 LocationBox / MacroBox 类；[Function.Properties](https://www.eplan.help/en-us/Infoportal/Content/api/2026/Eplan.EplApi.DataModelu~Eplan.EplApi.DataModel.Function~Properties.html)。
- ArticleReference 是父项目、功能或连接上的 transient 引用；`ReferencePos` 是父对象属性的引用索引，属性更改经 StoreToObject 才持久化。公开 finder 只返回有效引用，不直接返回连接定义点的引用。**本次未核实 transient 原生标识的跨请求稳定性**，不能把继承来的 ObjectIdentifier 当作已验证的通用定位能力；暂不接入，不调用 StoreToObject。— [ArticleReference](https://www.eplan.help/en-us/Infoportal/Content/api/2026/Eplan.EplApi.DataModelu~Eplan.EplApi.DataModel.ArticleReference.html)、本机 XML 的 GetArticleReferences。
- **Page 继承 Group（即 Placement）**，读 `Page.Location` 会抛 `InvalidOperationException`（S063136 无法确定页尺寸）；按 Placement 处理放置时须先排除 Page。— 实机（B2 第二部分 Large，2026-10-30，EPLAN 2026.0.3，Electric P8，scratch）。
- 11 种新类型的读取已在 scratch 上实机通过（ObjectReadTests，读取前后设计文件不变）；其余条目是文档核对；读取资源归属仍以 A36/A39 为准。新增类型的 Large 读取和无写入断言已编写，本次未运行。

- C1 第三片（2026-10-31，仅核对目录及本机 XML，不运行 EPLAN）：`Page.GetWriteProtectionFlagSet(nKind)` 接受 `IWriteProtection.WriteProtectionKind` 枚举；`Function.IsPointInsideLogicalArea(pnt)` 接受 `Base.PointD`；`PlaceHolder.AddRecord(strRecordName)` 返回记录索引，`FindRecord(strName)` 找不到时返回 -1。方法试执行的撤销与副作用尚未实机验证，Large 仅编写。— `catalog/eplan-2026.0.3/` 与 `Eplan.EplApi.DataModelu.xml` 对应成员。

- C1 第四片（2026-10-31，不运行 EPLAN）：目录继承关系与本机 XML 确认 `GetStorableObjects(null)` 枚举 StorableObject 及派生对象，但不含 Page、PropertyPlacement、Symbol；Page 用 GetPages，Symbol 用 GetSymbolsWithFilterScheme，属性放置仍按 A57 拒绝。GetFunctions3D 不含 InstallationSpace，另用 Project.InstallationSpaces；GetPlacements3D 不含 MateEntry，另有 GetCustomMateEntries。3D 连接可从 InstallationSpace.Connection3Ds 取得。Properties 必须沿声明继承链取最派生 getter；没有独立字典的派生属性列表使用最近的目录字典，未知 ID 拒读。ViewPart.UseLocalPropertyPlacements 仅 setter 过时，不生成该写入口。— `catalog/eplan-2026.0.3/`、本机 `Eplan.EplApi.DataModelu.xml` 对应成员；本片只有离线编译与测试，未增加实机验证结论。

- C1 第五片（2026-10-31，仅核对目录和本机 `Eplan.EplApi.HEServicesu.xml`，不运行 EPLAN）：`PlcService.ImportData(Project, string strFullName, ImportMatch)` 的 strFullName 是输入文件完整路径，按路径排除；`NameService.EvaluateAndSetAllNamesTXN` 自建事务，文档禁止外层事务，不能接入单撤销试执行。`NameService.CanDeviceTagBeNested(Function)` 返回 bool；`ConnectionService.SwapSourceAndTarget(Connection)` 交换连接两端；两者的 Large 服务读取与撤销测试仅编写，未实机验证。`StorableObject.Project` 在 transient 或无项目归属时可为 null，返回定位器前须校验项目归属（本机 `Eplan.EplApi.DataModelu.xml`）。

- C1 静态与入口片（2026-11-01，仅核对目录及本机 XML，不运行 EPLAN）：Project.Settings 等价于 ProjectSettings 的项目构造；Project.LayerTable 为项目图层表，按字符串名称取 Layer 不存在时返回 null；Page.PlotFrame 在页面没有图框时抛异常；Project.GetLocationObjects() 返回项目全部结构标识符对象。via 只沿公开 getter 最多两层，不释放借用 StorableObject，辅助 IDisposable 在请求内释放；设置写入排除。Pen 的修改需重新赋给图元，ConnectionPointPosition 的修改需显式保存到父对象；C 级属性读回不表示已持久化。静态分类、via 写入的撤销和副作用仍待 LargeWrite 验证。— Eplan.EplApi.DataModelu.xml 对应成员。

### A62. 通用对象读取的实机结果（C1 第四片，2026-10-31，EPLAN 2026.0.3.25421，Electric P8，只读 scratch 副本 LargeWrite）
- 按目录生成的查找路径（`DMObjectsFinder` 的 `Get*` 方法与页 `AllPlacements` 按运行时类型筛选）在 LargeWrite 上找到并读出 60 种新类型的属性（如 Arc、Cabinet、MountingPanel、MountingRail、BusBar、Harness、Connection3D、RoutingSegment、QRCode、PlanningObject、Project），读取前后设计文件不变。另有 22 种在该副本中没有对象（如 Comment、Function3D、HyperLink、LockedMountingArea），未实机读取。
- **`E3D.MateEntry` 的身份不稳定**：同一对象在 `eplan_object_find` 与随后的 `eplan_object_get` 之间描述摘要不同（`PLAN_STALE`），因此列为不支持（`unstable_identity`），不提供定位器。
- 两个 EPLAN 实例同时运行时，分页游标不能绑定全部实例列表：另一个实例启动或退出会使游标失效。游标只绑定所选实例（进程号与管道名）。— Large `GenericObjectReadTests`、`ObjectReadTests`（A、B 两槽并行）。

### B11. 选择集签名与实机读回（2026-10-31 目录/XML 核对；2026-11-01 Large 日志）
`eplan_api_search/get` 确认 HEServices.SelectionSet.Selection 返回 StorableObject[]，CurrentlyEdited 返回 StorableObject（文档为 Page/InstallationSpace 或 null），OpenedPages 返回 Page[]；两个默认锁定开关均可设置。Edit.SelectObjects(string,StringCollection,bool) 返回 void；XML 要求 Type/Id/0，不含数据库前缀；ClearSelection 只清 GED，不清导航器。A57 属性放置仍不提供无歧义定位符。

- 2026-11-01 早一轮（EPLAN 2026.0.3，Pro Panel，LargeWrite scratch 副本）：`NativeNoEditorDifferentPagesAndPropertyPlacement_PrintReadbackAndPreserveOneUndo` 在前置 `eplan_device_place` 调用被 Host 拒绝（`INVALID_ARGUMENT`、`not_started`）：测试给了 `library="IEC_symbol"`、`symbol="K"`，漏了库符号来源必填的 `symbol_variant`，并非 EPLAN 选择行为异常。测试补齐变体 `0`，不传互斥的 `part/variant`；该轮没有执行到原生选择探针，不能据此确认无编辑器、跨页或属性放置的选择效果。— 分支提交 `b159c81d` 的测试修正与当轮记录；当前本地日志已是后续运行，结果见下。

- 同日后续运行（EPLAN 2026.0.3.25421，Electric P8，LargeWrite scratch 副本，S45 完成）：无编辑器时请求功能 `17/69835/0`，调用后 `/103` 已打开，但 `Selection` 仍返回原导航器的 Project；随后 `one_page` 也返回 Project。跨页请求该功能及 `/104` 的 Line 时，两页均已打开，实际只返回 `/103` 的功能；不能把跨页请求当成全部选中。属性放置 `ContextPropertyPlacement` 的标识与父功能完全相同，原生请求读回的是 Function，不能据此提供属性放置定位。`clear` 读回为空。— 证据：q-selection-SelectionSpikeTests.log（`../../../.local/failures/q-selection-SelectionSpikeTests.log`，本工作区未提供该日志） 的 `environment`、`no_editor_before`、`no_editor`、`one_page`、`different_pages`、`property_identity`、`property_placement`、`clear`。
- **选择读回取决于活动窗口，而不只取决于当前页**：本机 `Eplan.EplApi.HEServicesu.xml` 的 `SelectionSet.Selection` 明确读取 active window；`Edit.SelectObjects` 只说明选择 GED 对象，未承诺聚焦；`Edit.SetFocusToGED()` 返回 bool，明确用于聚焦 GED。上述 Project 读回与此一致。产品在 UI 修改后显式聚焦，再独立比对全部定位符；聚焦失败不报告精确成功，action_try 不执行 action。Small 模拟相同的矩形请求、导航器活动状态及聚焦失败；修复后的实机效果仍待 Large 复验，未把焦点解释写成已验证的时序结论。
- **第一次从无编辑器状态打开刚绘制的页，会伴随设计文件变化**：S45 的 `no_editor` 前后 204 个设计文件仅 `Page.eod`、`Page.eox` 的 SHA-256 不同；后续 `one_page`、`different_pages`、`property_placement` 均无文件差异。日志不能确定变化的是缓存还是业务字段，不能再笼统声称开页总是逐字节不变（A54 的既有样本不足以推广）。Large 调整为先开页初始化，同时精确核对业务快照和绘图指纹；随后关闭编辑器重测无编辑器路径，仍比较全部设计文件，不排除 Page 文件，仍验证一次撤销恢复及保留先前历史。— 同一日志的四个阶段 `designBefore/designAfter`；新增初始化与聚焦断言尚未运行 EPLAN 复验。
- 此轮两个 Selection 失败分别是原生探针的文件相等断言、产品第一次 `exact` 断言；测试错误没有 correlation ID，`bridge-slotA.log` 对应 selection 加载时段（05:05–05:08）没有异常。ActionTry 测试通过；该 add-in 全天日志内其他 worktree 的 api_try 超时等异常不属于这两个测试的错误，不能据此将它们归类为本轮写入结果未知。— 测试输出（`../../../.local/failures/q-selection-SelectionSpikeTests.log`，本工作区未提供该日志）、ActionTry 输出（`../../../.local/failures/q-selection-ActionTryTests.log`，本工作区未提供该日志）、add-in 日志（`../../../.local/failures/bridge-slotA.log`，本工作区未提供该日志）（均为本地证据，不提交）。

### A71. 页面审查 Large 失败记录（2026-11-01，EPLAN 2026.0.3.25421，Pro Panel，LargeWrite scratch 副本）
- **只含一条悬空 `DynamicConnectionLine` 的新页触发共享连接读取器的重复键异常。** `review_setup/dangling` 在 `(30×grid,40×grid)` 到 `(50×grid,40×grid)` 建线，随后 `page.review` 在 `EplanConnectionCreator.Read` 的字典插入中抛 `ArgumentException`，关联 ID `55086094318d4af5b9eb75c952646346`。结合夹具和代码路径，冲突来自把动态线当成普通 `SymbolReference` 按“对象身份/PinBase.Index”收集引脚；动态线应作为待检查的边，不能把它自身的端点当成终结该边的器件引脚，否则还会漏报悬空端。读取器与 `eplan_connection_create`、移动规划共享，因此这类页也会阻断后续绘图。修复在共享读取路径排除动态线的引脚，并按符号身份去重；Small 用重复索引的动态线和真实器件引脚复现。日志没有打印原生索引数值，Small 的索引值仅为复现输入。— PageReviewTests 日志（`../../.local/failures/q-review-PageReviewTests.log`，本工作区未提供该日志）、插件异常堆栈（`../../.local/failures/bridge-errors.log`，本工作区未提供该日志）；`ReviewFixture.Create`。
- **连接列表可返回 `pin:null`，审查目标要求字符串（未命名引脚用 `""`）。** `networks` 把列表端点直接复制到目标，四个 `/expected/connections/*/{from,to}/pin` 被拒绝为 `INVALID_ARGUMENT`。测试须显式转换端点，不能放宽审查 schema。— 同上 PageReviewTests 日志；与 A55 的未命名引脚记录一致。
- **中断点移动被产品类型名白名单拒绝，尚未调用原生移动。** `pairing` 调用 `eplan_placement_update` 返回 `CAPABILITY_MISSING` / `placement.update.execute`；这不能证明 EPLAN 不支持移动。本机 XML/目录中 `InterruptionPoint`、`Cable` 都沿 `SymbolReference` 继承 `Placement.Location`；修复补齐两种类型，沿用完整闭包与提交前后读回校验。Small 覆盖原请求的中断点横移与电缆从主干移到分支，以及组归属拒绝；**本次未运行 EPLAN，原 Large 修复流程仍须实机复验**。— 同上日志；本机 `Eplan.EplApi.DataModelu.xml` 的类型与 `Placement.Location`，`catalog/eplan-2026.0.3/`。
- **电缆夹具不能把请求名当成实际存储名。** `define`、生成成功后，`bind` 用 `=GAA+A1-WReview<页号>` 精确查找时 `Single` 没有匹配项；与 A55 已记录的电缆结构段规范化相符。这份日志没有给出实际名称，不能据此断言具体丢了哪一段。测试改为从 `define` 提交后的 `observation.cables` 取得实际名称，先用已有名称等价规则断言，再将其传给绑定与目标清单。— 同上 PageReviewTests 日志，`SpikeCable.Find`。
- **整页验收在电缆成员匹配时失败，原因是 JSON 定位器大小写，不是原生空连接。** 关联 ID `68dd3ac1f6d3473ea511f352497b8b9a` 的异常为 `JToken.op_Explicit` 的 `ArgumentNullException`；读取器先用默认 `JObject.FromObject` 产生 `Oid`，随后却取 `locator.oid`。修复统一使用 `BridgeJson` 的 camelCase，并覆盖审查读回、问题对象和修复参数；Small 构造含电缆成员及非成员的连接列表，验证正确筛选和完整定位器形状。— AcceptanceRedrawTests 日志（`../../.local/failures/q-review-AcceptanceRedrawTests.log`，本工作区未提供该日志）、上述插件堆栈；`EplanPageReviewReader`。

### A72. 页面审查第二轮失败（2026-11-01，EPLAN 2026.0.3.25421，Electric P8，LargeWrite scratch 副本，串行）
- **移动同名中断点会重新计算未移动点的块属性文字。** `pairing` 把第二点从 `(160,160)` 移到 `(400,160)`，第一点的属性放置仍在 `(84,160)`、外框仍为全零，但 `text/display` 从 `??_??@-E103-/3;` / `-E103-/3` 变为 `??_??@-E103-/5;` / `-E103-/5`，工具因此拒绝并明确 `rolled_back`。辅助探针的 `displayText.propertyId` 为 20201；本机 XML `InterruptionPointPropertyList.FUNC_BLOCK_VALUE` 说明它是**只读、带索引**的计算块属性。移动读回须允许同名组中此属性的文字/摘要重算，仍精确检查数量、归属、几何及其他属性；不能把所有跟随文字变化都忽略。Small 复现上述坐标、类型 45/70、oid 1963/2265/2266 和原文字，并覆盖越界变化的拒绝与恢复。— PageReviewTests 输出（`../../.local/failures/q-review-PageReviewTests.log`，本工作区未提供该日志） 的 `pairing`；本机 XML 对应成员。
- **悬空线修复后，审查残留的是原点问题。** `dangling` 用真实 BAT 引脚接住 `(120,160)`、`(200,160)` 两端后，仅返回一个 `(0,0)` 的 `dangling_end`。代码将动态线图形和自动连接图形都作为端点证据，却没有剔除无长度表示；规则修正为只检查有长度的边，原点到其他位置的真实线段仍检查。**日志未打印原始 `GetGraphics` / `GraphicalConnections`，不能把具体哪个原生入口返回全零写成已验证事实**；Small 将全零表示与原坐标真实线段并列，验证修复后无误报、去掉任一真实引脚仍报错。此处是依据结果和代码作的离线修正，仍待实机复验。— 同上 `dangling` 输出；[GetGraphics 文档](https://www.eplan.help/en-US/infoportal/content/api/2026/Eplan.EplApi.DataModelu~Eplan.EplApi.DataModel.DynamicConnectionLine~GetGraphics.html) 只保证返回图形表示，没有保证表示非退化。
- **电缆测试未建立“修复前必定跨主干”的证据。** `cable` 失败在修复前的 `Assert.Contains`，实际问题集合为空；日志没有给出完整路径/成员，不能据此断言 EPLAN 漏建芯线。旧测试把 `47×grid` 当主干、`33×grid` 当 A–B 独占分支，只有图形朝向相同不足以保证这一点。测试改为保持 A–B 目标固定，从独立原生读回的 `drawnPath` 找共享段与 A–B 独占段，分别确定错误定义线与修复位移；绑定后另断言确有共享定义点，仍要求修复前报错、修复后 `clean`。Small 覆盖两种主干朝向；规则同时在整页连接中检查同一定义点的额外归属，避免只在 `CableConnections` 的成员子集中自查。后者是保守补齐，**本日志不足以证明该次成员枚举遗漏**。— 同上 `cable` 输出、A64。
- **这份虚构验收图纸本来就含警告，不能断言默认审查为 `clean`。** 本次验收已完成 `verify`，随后审查报告 `errors=0,warnings=28,infos=108,clean=false`；并非 customer-d5 的 `(252,256)→(252,212)` 缺线失败，不移植那份清单改动。保留的 `acceptance-report.json` 分页读回对应 8 个空引脚（端子 7 上侧、端子 8 下侧、辅助触点下侧及五个未接设备的中断点）和 20 个外框交叠（位置盒与内部开关 6、PLC/相邻通道 7、电缆与定义点 7）。特别是本次 DI 符号外框宽 40 mm、通道间距 32 mm，按已发布的外框规则确实会报告交叠；这不是“图上没有任何问题”的证据。测试清单显式记录 28 条**精确**警告（代码、位置、对象类型/名称/位置），完整分页核对多重集，任何新增、遗漏、对象错配或 error 都失败；不改变产品严重度或 `clean` 定义，也不放松原有连接、芯线及属性断言。— AcceptanceRedrawTests 输出（`../../.local/failures/q-review-AcceptanceRedrawTests.log`，本工作区未提供该日志），同次运行的 `acceptance-report.json`。
- **日志中的其他分支异常不能冒充本轮失败。** 两份 `q-review-*` 输出没有关联 ID；slotA 的 review 插件段为 `04:54:10–05:04:47`，期间没有异常堆栈。slotB 的 `55086094318d4af5b9eb75c952646346`、`68dd3ac1f6d3473ea511f352497b8b9a` 是 A71 已修复的上一轮问题。slotA 的 `2d9673674cac4d7284749c59d93fdf85`、`e704b39ce143474899d28c3fd4c09b11`（slotB 同类 `b5024071a06142faa197bed80ce57b22`）是其他分支的 `api.try.execute`：`TimeoutException: Trial exceeded its execution budget` 来自调用/观察返回后的预算检查，尚未提交就进入回滚。堆栈没有表示回滚失败，也没有回滚后的业务/文件核验；现有未知结果分类保持不变，不能仅凭异常发生在预算检查就声称已恢复。`01a48ecf147644d1ae4af6252cffc6db`、`b7a31b03a25844fc82c7fbe2a8e58e88` 的 S029128 是 A63 已有“读拒绝且状态未变”的已知失败路径。— slotA（`../../.local/failures/bridge-slotA.log`，本工作区未提供该日志）、slotB（`../../.local/failures/bridge-slotB.log`，本工作区未提供该日志）。本轮只做离线构建/回归，不启动 EPLAN。

### A73. 页面审查第三轮失败（2026-11-01，EPLAN 2026.0.3.25421，LargeWrite scratch 副本）
- **残留原点来自非退化的动态线图形行，不能只过滤全零线。** `dangling` 的原生观察中，`dynamicLines` 的 `oid=423455`：`position=[0,0]`、两个 `connectionPoints.position` 分别为 `[0,0]` 和 `[200,160]`，`graphics.start/end` 同样为这两点。修复后此行仍在，另有 `segments` 行从 oid 69839 的 `[120,160]` 连到 oid 69840 的 `[200,160]`；对应器件放置点为 `[116,160]`、`[204,160]`。因此 A72 的退化线过滤不足以消除该误报。按本轮已诊断规则，精确页原点视为未知/默认坐标，和缺失、非有限坐标一起跳过，不作错误；只将非退化折线首尾作为悬空端，内部拐点仍可检查栅格。实际连接点继续按 A52/A55 读取；另一折线端点重合不替代引脚或节点。Small 复现上述图形行与实际端点，保留 `[200,160]` 真悬空的报错。此项为日志与已提供诊断支持的离线修正，本次未运行 EPLAN。— PageReviewTests 日志（`../../.local/failures/q-review-PageReviewTests.log`，本工作区未提供该日志）。
- **无绘图的逻辑连接可返回 `drawnPath:null`。** `cable` 的原生观察中 oid 72612（A–B）、72613（B–C）的 `drawnPath` 都为 null，`PageReviewCableLayout.Segments` 直接枚举导致测试异常。收集旁支图形段时跳过非数组路径，目标连接仍必须具有数组路径及可识别的共享段/独占段；日志不足以证明本轮目标连接已有有效路径，仍须实机复验。— 同上日志。
- **移动电缆后，新交叉点的定义点在生成前没有名称，定义点各自带一条属性文字。** 2026-11-02，Pro Panel，B 槽，LargeWrite scratch 副本：`eplan_placement_update` 把电缆 `17/69845` 移动 (0,+64) 后，EPLAN 在新交叉点 (120,192) 新建定义点 `59/69847`，名称只有 `+`，并为它加了一条属性文字（属性 31004，外框全零）；旧定义点 `59/69846` 的属性文字不再存在。所以移动校验只按名称识别电缆自己的定义点是不够的：移动前不存在、名称为空或只有结构前缀、并落在移动后电缆外框内的新定义点，以及这些定义点的属性文字，都留给 `eplan_connection_generate` 处理；名称不同或位置不在电缆上的定义点仍判为不一致。证据：`moveMismatch` 诊断（预期 49、实际 50，新增 2、缺少 1）。同日第二轮原生读取（移动前、移动后、生成后各一次）：移动前定义点 `59/69846` 在 A–B、B–C 共用的主干段上，两条连接都是芯线 `1`、RD，电缆成员为这两条；移动后电缆跨到 A 侧独占分支，旧定义点消失，电缆成员为空，两条连接仍带旧的 `1`/RD；`eplan_connection_generate` 后 A–B 上出现定义点 `59/69847`（名称 `+`，`CableDefinitionLine` 指向该电缆），但电缆成员仍为空，A–B 的芯线号与颜色被清空，B–C 恢复为原来的 BK、1,5。结论：移动电缆会丢失芯线归属，生成连接不会恢复；页面审查不再把移动作为修复建议。

### A107. 打开和关闭项目、结构标识符的顺序和改名、PLC 编址（2026-11-06，EPLAN 2026.0.3.25421，Pro Panel，LargeWrite）
- **打开和关闭：** `QuietModeStep(ShowNoDialogs)` 内 `ProjectManager.OpenProject(路径, OpenMode.Exclusive)` 打开，`EplanProjects.ByPath(路径).Close()` 关闭。重开后页数不变，写入和一次撤销都正常；只有它一个项目打开时它就是当前项目。— `ProjectToolsTests`。
- **不要在打开后调用 `Edit.SelectProjectInPagesNavigator`：** 调用过之后，`ProjectOpen` 再打开的项目不再成为当前项目，Large 夹具靠开页恢复当前项目，下一次 `XPrjActionProjectClose` 弹"项目选择"对话框，EPLAN 随后返回 EPLAN_BUSY。去掉这一步后不再出现。因果关系只由这一次对比支持。
- **结构标识符的顺序：** `Project.GetLocationObjects(块)` 和 `GetLocations(块)` 按顺序返回，`GetLocationObjects()` 不是（读回校验曾因此失败）。`SortId` 是负的小数，越小越靠前。`SetSortedLocations` 把传入的放在最前面，排在该块的空标识之前（所以要把空字符串放在第一个，否则没有这个结构的页会排到最后，目录顺序全错），`SafetyPoint` 能回滚，返回 true；其余标识之间的顺序可能被 EPLAN 改变（`C2`、`B4` 变成 `B4`、`C2`），所以位置重要的都要传。名称用 `GetLocations` 的写法（`B1.X1`）；`Location.Name` 只是最后一段（`X1`）。— spike S21 探针。
- **`Project.ChangeLocationName(Location, 名称)`：** 名称传最后一段，用到它的页名一起变，一次撤销恢复。— `ProjectToolsTests`。
- **`PlcService.ReaddressTerminals`：** 起始地址要和项目的地址格式一致。示例项目是 `%IX2.7`，传 `I100.0` 抛 `ArgumentException`（S029035，参数 `strDigitalInputStartAddress`），传 `%IX100.0` 把地址整体平移（`%IX2.7` → `%IX102.7`），846 个连接点，一次撤销恢复。— `ProjectToolsTests`。
- **`Translate.TranslateText(文本, 项目)` 返回 `MultiLangString`：** 词典里没有的词只有一种语言（`zh_CN@Schaltplan;`）。词典里有的词在 P8 上的结果待补。
- **崩溃（2026-11-06 18:06，Electric P8）：** 探针把同一个 `PropertyValue` 先写给 `Location`、再写给 `ProjectPropertyList.PROJ_INSTALLATIONNAME`，在 `UniversalPropertyList.setProp` 里 `AccessViolationException`。写入工具每次都新建 `PropertyValue`，没有这个问题。是否正是复用导致，没有再试。EPLAN 带 `/RestartOnCrash` 自动重启后，`eplremote --list` 找不到它（`NO EPLAN <pid>`），要重新启动才能远程调用。

### A110. `eplan_page_review` 读了已释放的属性列表，EPLAN 随后崩溃（2026-11-06，EPLAN 2026.0.3.25421，Electric P8）
- 🔴 现象：从零绘制时连续 3 次 `0xc0000374`（ntdll，偏移 0x118095），都在第 17、18 页。每次之前都有 `page.review failed unexpectedly`，`PropertyNotFoundException` S063109，堆栈 `ConnectionPropertyList.get_CONNECTION_WIRECROSSSECTION`。一次英文名项目多跑了几步才崩，所以项目名不是原因。
- 根因：连接不在电缆清单里时，`EplanPageReviewReader` 先调用 `ReadCableWire`，它 `using` 释放了 `connection.Properties`；之后又读同一包装器缓存的列表（`StorableObject.Properties` 只创建一次，A80），违反 A36。异常本身不致命，被破坏的堆在之后几秒的写入中才崩溃，所以崩溃点看起来是随机的。
- 修复：先读属性，再调用 `ReadCableWire`，不再释放这个列表。
- 同一天 16:13 的同一崩溃代码曾记在探针写 1230 名下（A106），也可能是这个原因，无法再区分。

### A111. 从模板新建的项目存的是模板里的旧表格（2026-11-06，EPLAN 2026.0.3.25421，Electric P8，复刻 71 页图纸）
- **现象：** 新项目的报表表头和 PDF 不同（`自由布线的导线`、`定位源`；PDF 是 `空闲芯线`、`位置来源`），旧项目却和 PDF 一样。两个项目的设置和表格名都相同，`Form.eod` 大小也相同，但内容不同：`F09_001`、`F13_001` 两条记录的未压缩大小都不一样。系统目录的 `.f09` 用的是 PDF 的词。
- **原因：** 旧流程在打开项目时对"主数据已经陈旧"按了按钮，新流程用 `eplan_project_open`（`QuietModeStep(ShowNoDialogs)`）不弹对话框，所以项目没有更新。`eplan_report_create` 只在项目缺表格时才从系统复制，已有的同名表格不替换（A105）。
- **`Masterdata.UpdateProject(project)`：** 在 `QuietModeStep(ShowNoDialogs)` 里不弹对话框。这个项目更新前后都是 76 个文件，没有新增。之后 `eplan_report_update` 刷新报表，表头变成 PDF 的词，报表对比从 75.4% 升到 87.3%；25 页原理图的文字和连接对比不变（2222/2246、1177/1187）。工具 `eplan_masterdata_update` 按 Destructive 处理，先备份。
- **未验证：** 这个调用能不能回滚或撤销，所以它不进事务；中途失败报 OUTCOME_UNKNOWN，靠备份恢复。

### A118. 旧版基本项目不能直接建项目（2026-11-07，EPLAN 2026.0.3.25421，Electric P8，官方 EES 资料）
- 用 2023 版的 `.zw9`（IEC 原理图块和部件宏项目的 DE/EN 版、HVAC 项目模板）建项目时，EPLAN 报"无法创建项目。必须更新所使用的项目模板或基本项目。"，`eplan_project_create` 回滚并在 `metadata.eplanMessage` 给出这句话。2026 版的 `.zw9` 和各版本的 `.zw1` 备份都能用。工具现在仅在基本项目创建失败后，通过 `XPrjActionUpgradeProjects` 升级副本再重试，imports 原件不变，升级目录路径必须保持短小。
- 2026-11-07 在 scratch 副本上以 `Folder=<folder>`、`BaseProject=true`、`NoBackup=true`、`PackOriginalProject=false` 执行 action，27 MB 的 2023 `.zw9` 约 17 秒原地升级，随后 `CreateProject` 成功。长目录下 action 解包到 `<Folder>\temp\<内部项目名>.edb\...`，完整路径超过 255 字符时报"完整文件名超出字符的最大长度 255"、"无法转换为新版本"；短路径 `<scratch>\.up\<6 位小写十六进制>` 可用。官方说明基本项目只跨主要版本升级，无需升级时不做更改。— [XPrjActionUpgradeProjects 官方帮助](https://www.eplan.help/en-us/Infoportal/Content/api/2026/XPrjActionUpgradeProjects.html)。

### A121. 没有页结构的项目：NFPA 基本项目的代号和页名（2026-11-07，EPLAN 2026.0.3.25421，Electric P8，从 `NFPA_mm_bas001.zw9` 建的 scratch 项目 TPL_NFPA_mm、MX_NFPA）
- NFPA 基本项目的页名就是 `1`、`2`、`3`，页结构里没有 `=`、`+`、`&`；`=PWR+CP/5` 建出来是 `5`。`eplan_page_create` 给 `/4` 时 EPLAN 建出 `4`，工具现在视为一致。
- 工具原来把不带 `/` 的页名整个当成结构，`-CB1` 被拼成 `4-CB1`。`Project.ParseNameAndProps` 把 `4-CB9` 拆成计数器 `4`、标识字母为空，`FUNC_CODE` 写空值时 EPLAN 抛 `EmptyPropertyException`（S063111），所以按部件放置一律失败；符号放置直接写 `Name`，得到 `4-CR1`。现在只有以 `=`、`+`、`&`、`#` 开头的才算结构，纯页号没有结构。
- 不带结构时 EPLAN 把 `-CB11` 存成 `CB11`，PLC 点存成 `DI2:1` 这样的形式；放置和改名的读回按此比较。`PropertyValue.ToString()` 对空值也抛 S063111，读诊断时先判 `IsEmpty`。
- 中断点名称：带结构的页上 `=-L1` 显示为 `L1`，在这种项目里 `=` 原样显示。规划器不再加 `=`，但插件写名字时仍按 PDF 重画的规则补 `=`，实际存成 `=-L1C`；读回比较时空的 `=` 被忽略，所以没有报错。现在纯页号的页上按设备的规则写成 `L1C`（去掉 `-`，不加 `=`）。
- 按名称找已有设备时，规划器对普通符号用严格相等，`-M1` 找不到存成 `M1` 的设备：`join_device` 报 DEVICE_NOT_FOUND，同名放置也查不出重复（EPLAN 会合并）。现在这种项目里 `-M1` 与 `M1` 视为同名。
- 修复前建的设备叫 `4-CB1`（页号被当作结构），EPLAN 也没有按格式拆出 `DeviceTag`；`eplan_device_update` 现在把 `-` 前的纯数字当页号丢掉，`new_tag: "-CB1"` 改成 `CB1`。MX_NFPA 第 4 页 15 个设备已这样改名。修复前建的中断点叫 `=-L1`，中断点不是设备，现有工具改不了它们的名称；对端要写同样的 `=-L1` 才能配对。
- 实测：GV2P08 按部件放置、代号 `-CB11`，得到 `CB11`（4 个功能），改名为 `CB12` 成功。中断点当时报“建成 `-L9`”，其实存的是 `=-L9`（见上）；第 5 页的 `=-L1C`、`=-NC` 也是这样。
- 裸名规则只看 `:` 前的设备部分：黑盒连接点 `PS1:+`、`PS1:-` 的连接点名里有 `+`/`-`，原来整名检查结构符，读回判为不同并回滚（MX_NFPA 第 5 页 PS1）。
- `eplan_macro_insert` 默认 `numbering: "number"` 在这种项目里按页加行编号（`?K802`），还把 ET 200SP 卡宏里的电源点 `L+`、`M` 改名为 `1`、`2`，与输入端子 1、2 重名（第 7–12 页实测）。插 PLC 卡宏用 `numbering: "keep"`，再用 `eplan_device_update` 改名。
- 同一张卡的第二个 PLC 点起，EPLAN 把显示名留空，工具用 `card.Substring(card.LastIndexOf('-'))` 补卡名；卡名 `DI1` 没有 `-`，`Substring(-1)` 抛异常，放置报 EPLAN_OPERATION_FAILED（MX_NFPA 第 7–12 页，每张卡只有第一个点放成功）。现在没有 `-` 时整名作显示名。
- 自动连线停在 PLC 盒轮廓上，进不到盒内的端子：ET 200SP 卡宏变体 6（模块外观图）的端子在盒内（连接点 x=150/162，盒边 144/168），外部器件经节点接过去时左侧没连上，右侧停在盒边被 EPLAN 当成接到了 `L+`。现场器件改为在梯形图行末放同名 `PLC_CBOX` 点（辅助表示，`main:false`），模块外观图集中放在机架页。
- 🔴 未解决：PLC 卡（`SIE.6ES7131-6BH01-0BA0`）用 `eplan_device_place` 按部件放置时，提交后读回只找到 1 个功能（应为 3 个），工具判失败并自动撤销，但项目里留下一个未放置、挂着部件的 PLCBox 主功能（`DI2`）。PLC 模块按用法说明用 `eplan_macro_insert` 的 `part` 插组宏。

### A127. 项目改名：`CopyProject` + `RemoveProject`（2026-11-09，EPLAN 2026.0.3.25421，Electric P8，scratch 项目 MX_Dryer26、MX_Dryer27）
- **没有改名 API：** `DataModel.ProjectManager` 只有 `CopyProject`、`RemoveProject` 等，`HEServices.ProjectManagement` 和官方 action 目录里都没有改项目名的方法；官方 `CopyProject`、`RemoveProject` 页面只有签名和异常，没有说明。
- **实测（`eplan_project_rename`）：** 打开着的项目用 `CopyProject(旧 .elk, 同一文件夹的新 .elk, CopyMode.Snapshot)` 复制，关闭旧项目，`OpenProject(新 .elk, Exclusive)` 打开，再对已关闭的旧项目调 `RemoveProject(旧 .elk)`：旧 `.elk` 和 `.edb` 都被删除，**所在文件夹保留**，同一文件夹里的新项目不受影响。中文项目名（`通过式烘芯炉`）可用。项目描述等属性随复制保留。

### A128. 以普通方式打开项目（2026-11-09，EPLAN 2026.0.3.25421，Electric P8，scratch 项目 通过式烘芯炉）
- **`OpenMode.Exclusive` 在项目名旁显示“专有”：** 别的 EPLAN 实例或用户在项目打开期间不能再打开它。负责人一人使用、不做多人编辑，要求去掉这个标记，打开工具改用 `OpenMode.Standard`（枚举值 Standard 0、ReadOnly 1、Exclusive 3）。
- **普通方式下写入正常：** EPLAN 启动时按普通方式重新打开的 MX_Dryer27 上，结构、页面、宏插入、编址、连接和电缆定义都正常写入（mxd5_rtd.py）；改用 `Standard` 后用 `eplan_project_open` 关闭再打开 通过式烘芯炉，页导航器和标题栏都没有“专有”（窗口截图核对）。

# [EPLAN API 笔记](../api-notes.md)：读取、快照与项目生命周期

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
  - 对本项目的影响：同一时间只使用一个集合，读出后立即复制到 DTO（编码规范 9.5 第 15 条）。
- `SysMessagesCollection()` 包含整个 EPLAN 会话的消息，其中有 Trace 级别的消息，以及一条级别为 7 的"有新版本"提示（7 不在 `MessageLevel` 枚举中）。— S6。
- **`ProjectOpen` 不一定改变当前项目**：已有一个项目打开时，用 `ProjectOpen` 再打开一个，`SelectionSet.GetCurrentProject` 返回的仍是原来的项目；`XPrjActionProjectClose` 关闭的也是原来的项目。— S6 第一次运行（驱动脚本因此关闭了工程师自己的项目，没有读取或修改它）。
  - 对本项目的影响：不依赖"当前项目"，按名称或路径找项目（编码规范 9.5 第 14a 条）。

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

- 同日后续运行（EPLAN 2026.0.3.25421，Electric P8，LargeWrite scratch 副本，S45 完成）：无编辑器时请求功能 `17/69835/0`，调用后 `/103` 已打开，但 `Selection` 仍返回原导航器的 Project；随后 `one_page` 也返回 Project。跨页请求该功能及 `/104` 的 Line 时，两页均已打开，实际只返回 `/103` 的功能；不能把跨页请求当成全部选中。属性放置 `ContextPropertyPlacement` 的标识与父功能完全相同，原生请求读回的是 Function，不能据此提供属性放置定位。`clear` 读回为空。— 证据：[q-selection-SelectionSpikeTests.log](../../../.local/failures/q-selection-SelectionSpikeTests.log) 的 `environment`、`no_editor_before`、`no_editor`、`one_page`、`different_pages`、`property_identity`、`property_placement`、`clear`。
- **选择读回取决于活动窗口，而不只取决于当前页**：本机 `Eplan.EplApi.HEServicesu.xml` 的 `SelectionSet.Selection` 明确读取 active window；`Edit.SelectObjects` 只说明选择 GED 对象，未承诺聚焦；`Edit.SetFocusToGED()` 返回 bool，明确用于聚焦 GED。上述 Project 读回与此一致。产品在 UI 修改后显式聚焦，再独立比对全部定位符；聚焦失败不报告精确成功，action_try 不执行 action。Small 模拟相同的矩形请求、导航器活动状态及聚焦失败；修复后的实机效果仍待 Large 复验，未把焦点解释写成已验证的时序结论。
- **第一次从无编辑器状态打开刚绘制的页，会伴随设计文件变化**：S45 的 `no_editor` 前后 204 个设计文件仅 `Page.eod`、`Page.eox` 的 SHA-256 不同；后续 `one_page`、`different_pages`、`property_placement` 均无文件差异。日志不能确定变化的是缓存还是业务字段，不能再笼统声称开页总是逐字节不变（A54 的既有样本不足以推广）。Large 调整为先开页初始化，同时精确核对业务快照和绘图指纹；随后关闭编辑器重测无编辑器路径，仍比较全部设计文件，不排除 Page 文件，仍验证一次撤销恢复及保留先前历史。— 同一日志的四个阶段 `designBefore/designAfter`；新增初始化与聚焦断言尚未运行 EPLAN 复验。
- 此轮两个 Selection 失败分别是原生探针的文件相等断言、产品第一次 `exact` 断言；测试错误没有 correlation ID，`bridge-slotA.log` 对应 selection 加载时段（05:05–05:08）没有异常。ActionTry 测试通过；该 add-in 全天日志内其他 worktree 的 api_try 超时等异常不属于这两个测试的错误，不能据此将它们归类为本轮写入结果未知。— [测试输出](../../../.local/failures/q-selection-SelectionSpikeTests.log)、[ActionTry 输出](../../../.local/failures/q-selection-ActionTryTests.log)、[add-in 日志](../../../.local/failures/bridge-slotA.log)（均为本地证据，不提交）。

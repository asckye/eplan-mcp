# [EPLAN API 笔记](../api-notes.md)：主数据、部件与翻译库

### A171. 通过 eplan_api_try 删除部件库对象：加一个再删掉，集合数量恢复；句柄约十分钟后过期（2026-11-20，EPLAN 2026.0.3.25421，Pro Panel，B 槽，部件库副本）
- **环境：** `MasterDataOnCopyTests` 把 B 槽的部件库指到 `scratch\masterdata-copy` 下的副本后运行 `ApiTryPartsDatabaseTests`；每次写入都经审批并先备份副本（ADR-0019、ADR-0054）。运行前后正在用的两个库 29 个文件的 SHA-256 相同，B 槽设置还原后与备份无差异。
- **做法：** 每一对先调 `Add…` 得到对象句柄，再把同一个句柄交给对应的 `Remove…`，前后各读一次集合数量。
- **结果（17 组删除成功）：** 部件上的位置：附件 0→1→0、门 0→1→0、安装板 0→1→0、部件构造 2→3→2、安全相关值 0→1→0、支撑条 0→1→0，锁定安装区域删除成功但目录里没有可读的集合可数。部件库里的对象：附件清单 39→40→39、附件布置 30→31→30、连接点信息 83→84→83、钻孔图 69→70→69、客户 0→1→0、制造商 11→12→11；它们的位置（附件清单位置、附件布置位置、连接点位置、钻孔位置）各 0→1→0。
- **`Add…` 被拒绝的两组：** `MDPart.AddAssemblyPosition`、`MDPart.AddModulePosition` 在所选部件上返回 `EPLAN_OPERATION_FAILED` 并回滚，两次运行都如此。原因没有查：可能与部件类型有关，没有证据。对应的 `RemoveAssemblyPosition`、`RemoveModulePosition` 因此未验证。
- **句柄会过期：** 一轮约 40 次写入、每次先备份，开头取得的部件库句柄约十分钟后再用时返回 `PLAN_STALE`（“The object handle expired; obtain it again in this session”）。重新调 `MDPartsManagement.OpenDatabase` 和 `GetPart` 取新句柄即可。
- **不带对象参数的服务调用要求 scratch 项目是当前项目：** `MDPartsManagement.OpenDatabase` 在另一个项目排在前面时返回 `POLICY_DENIED`（“A service without explicit object arguments requires the target scratch project to be current”）。
- **连接数：** 测试里除 Host 和它的服务容器各一个连接外再开一个，第三个找不到 EPLAN（与 2026-11-19 探测测试的记录一致）。
- **工具行为：** 部件库对象的 `Remove…` 不按项目对象的生命周期处理（没有撤销步骤，不做项目观测），与 `Add…` 同为主数据写入：EPLAN 当前配置的部件库必须是 scratch 根目录下的文件副本，每次审批，先备份（ADR-0059 修订记录 2026-11-20）。
- **未验证：** `MDPart.RemoveSymbolPosition`、`MDSymbolPosition.RemoveSymbolPosition`（需要符号库参数）、`MDPart.RemoveUserDefinedPropertyPosition`、`MDPartsDatabase.RemoveAllTranslations`、`RemoveTranslation`、`MDConnectionCategory.Remove`，以及上面两组。写入是否在关闭数据库句柄之前就落盘没有单独查。
### A165. 替换功能上的部件：ArticleReference.RemoveArticleReference() 加重新添加，在默认的安全点事务里完成（2026-11-19，EPLAN 2026.0.3.25421，Pro Panel，B 槽，LargeWrite scratch 新页）
- **依据：** 安装目录的 API 文档：`ArticleReference.PartNr` 只读，引用不能改指向；`RemoveArticleReference()` 把引用从对象上移除（设备固定时抛 `FixedDeviceException`）。`Function.AddArticleReference` 只能追加在末尾。
- **现象：** 对一个带部件的已放置器件，从最后一个引用往前移除到要替换的位置，添加新部件，再按原来的部件号、变体、数量把后面的引用加回去。提交后 `eplan_device_get` 读到的引用顺序、数量与预期相同，新部件在旧部件原来的位置；一次撤销后旧引用恢复。这些调用在工具默认的安全点事务里成功，不需要 A93 给“第一个部件”用的事务范围。
- **结论：** 替换部件是“移除 + 添加”；要保持引用顺序，被替换位置之后的引用必须移除后重新添加，它们上面改过的属性会回到部件的值。
- **工具行为：** `eplan_device_update` 的 `replace_part` 给出功能现在带的部件号，`part`（和 `variant`、`count`）是新部件；不给 `count` 时沿用旧引用的数量。结果带 `replacedPart`。把部件数据写到功能上仍然是单独一次 `update_from_parts`。
- **证据：** `DevicePlaceTests.PlacedDevice_PartIsReplaced_ReferencesReadBack_UndoRestores`（1/1）。

### A27. 部件库与翻译库的只读访问（spike S24，2026-10-21，EPLAN 2026.0.3.25421，Electric P8，在调度器中）
详细数据见 [spike S24](../../../spikes/S24-master-data/README.md)。
- 开发机的部件库是 `$(MD_PARTS)\EPLAN_parts.alk`：40 字节的链接文件，指向同目录的 `EPLAN_parts.adb\`（与项目数据相同格式的目录，3.9 MB）；`MDPartsDatabase.Type` 报告 `ACCESS`。翻译库是 `$(md_translate)\Translate.db`，类型 `EPLAN`。— S24。
- `new MDPartsManagement().OpenDatabase()` 0.02 秒；`GetPart(部件号)` 立即返回，不存在时为 null；`Parts` 返回全部 464 个部件，0.07 秒。部件的字段：`PartNr`、`Variant`、`Type`、`ProductGroup`，`Properties` 中的 `ARTICLE_DESCR1`（多语言原始字符串）、`ARTICLE_MANUFACTURER`、`ARTICLE_TYPENR`、`ARTICLE_ORDERNR`、`ARTICLE_MACRO`（宏路径，带 `$(MD_MACROS)`）。— S24。
- `SelectedPartsDatabaseAsString` 是静态属性。只读访问不改 `.adb` 中的数据文件，只改写 `.lck` 锁文件。— S24。

### A30. 主数据的备份、还原与部件库写入（spike S24 写入部分，2026-10-21，EPLAN 2026.0.3.25421，Electric P8，在调度器中）
详细数据见 [spike S24](../../../spikes/S24-master-data/README.md)。写入只在还原到 scratch 中的副本上进行，正在用的库前后 SHA-256 不变。
- `new ActionManager().FindAction("backup").Execute(context)`，参数 `TYPE=MASTERDATA`、`MDTYPE=ARTICLES|LANGUAGES`、`SOURCEPATH`/`FILENAME`（由配置的库路径拆出，如 `$(MD_PARTS)` + `EPLAN_parts.alk`，变量由 EPLAN 展开）、`DESTINATIONPATH`、`ARCHIVENAME`：返回 true，0.14 / 0.23 秒。得到 7z 格式的 `.zw6`（部件库，991 KB，不含 `.lck` 和 `EMPTY_SQL_Backups\`）、`.zw7`（翻译库，912 KB，只含 `Translate.db`）。— S24。
- 经 Remote Client 命令行传入含中文的路径会变成乱码（`S025033 源目录不存在`）；进程内传参不受影响。— S24。
- 🔴 `restore /TYPE:MASTERDATA` 不覆盖已有文件：目标中已有同名文件时跳过，照样返回 true，不记日志。还原到空目录得到逐字节相同的文件。恢复要先把损坏的库移开（EPLAN 关闭时），再放回还原出的文件。— S24。
- `new MDPartsManagement().OpenDatabase(路径)` 打开另一个库，不改 `SelectedPartsDatabaseAsString`。— S24。
- 🔴 `MDPartsDatabase.ImportParts(文件, "XPamImportXml", …)` 在按路径打开的副本上执行时，走的是界面导入 action `XPamImportXMLAction`，同时改了**配置的那个库**的 `User.eod`/`User.eox`（一个按用户的计数），部件数据不变；放在事务中时撤不回，还留下未关闭的撤销日志，之后的导入都报"不允许套叠撤销日志"。导入、同步类的测试一律算写正在用的库。— S24 r11。
- 部件库 `.adb` 中的 `User.eod`/`User.eox` 是 EPLAN 按用户记录的会话信息（当前用户那条记录中有一个计数），EPLAN 自己会改写：2026-10-21 只关闭一次 EPLAN，计数就从 9 变为 10，两个文件都变了；一天里正常启动、关闭 EPLAN 和导出，计数从 2 涨到 9。与项目数据库中导出只改写 `User.eod`（A16、A25）是同一类。判断部件库"有没有被改"时，与 `.lck` 一样排除这两个文件。S24 r11 中导入后这两个文件的变化也是这类记录，部件数据没有变。— 2026-10-21 实机。
- 不在事务中、在 `OpenDatabase()` 打开的配置的库上 `ImportParts`：0.2 秒，正常导入，`AppendNewRecords` 重复导入不重复添加。部件 XML：根元素 `partsmanagement`（`count`、源库路径 `database`），每个部件一个 `part`，部件号在 `P_ARTICLE_PARTNR`。— S24 r13（经批准，已恢复）。
- 🔴 `Synchronize.Parts(project, StoreMode)` 要在 `LockingStep` 中（否则 `NoLockingStepException`），并且**同时写项目**：被引用但没有存进项目的部件会存入项目（夹具 293 → 341），系统消息 `S328014`"写存取前没有加锁该对象"。— S24 r15（经批准，已恢复）。
- `MDPartsDatabase.ExportParts(文件, DataFormat.XML)` 464 个部件 2 秒、5 MB；`EDZ` 44 秒、128 MB。`Translate.ExportDatabaseTexts(文件, "XTrLanguageDbXmlConverterImpl" | "XTrLanguageDbXml2TabConverterImpl")` 不到 1 秒。导出不改库。转换器名：部件库 `XPamImportXml`、`XPamExportXml`。— S24 r11。
- `MDPartsDatabase` 的事务：`AddPart` 提交前 `ExistsPart` 为 false，`Rollback` 撤回、`Commit` 落盘；🔴 `RemovePart` 立即生效，`Rollback` 不能恢复。部件已存在时 `AddPart` 抛出 `MDInvalidArgumentException`（`S030025`）。事务不释放时，下一次 `CreateTransaction` 抛出 `SEHException`，要用 `using`。— S24。
- 部件 XML 中的变体：`P_ARTICLE_VARIANT` 不在 `part` 上，而在 `part` 下的 `variant` 子元素上，每个变体一个。S24 r11 导出的示例库全部部件：430 个 `part`、464 个 `variant`，`Parts` 也是 464 个（按变体计数）。根元素下另有 `propertydefinition` 等元素，不是部件；部分部件带 `P_ARTICLE_UNIQUEID`。— 2026-10-21，查看 S24 r11 的导出文件。
- 第一次经批准的 Large 测试运行（`PartImportTests`、`PartSyncTests`，EPLAN 2026.0.3.25421，Electric P8，配置的文件型库 `$(MD_PARTS)\EPLAN_parts.alk`，新复制的写副本 `LargeWrite`）：两个测试在一次运行中都通过，合计约 11 秒。— Large 测试 2026-10-21。
- 主数据备份的 `backup` action 在 `QuietModeStep(ShowNoDialogs)` 中照常生成归档（测试断言 `.zw6` 非空）：静默模式不阻止它。— Large 测试 2026-10-21。
- `ImportParts`（`AppendNewRecords`，按部件号识别）接受只有部件号、描述和部件类型的最小部件 XML：导入后 `eplan_part_search` 找到 `EPLANMCP.LARGE-TEST`，变体 1，产品组 `Common`；第二次导入时规划把它归为跳过，没有审批。— Large 测试 2026-10-21。
- `Synchronize.Parts(AppendNew)` 在写流水线的 `SafetyPoint` 和撤销步骤中提交：写副本中存储的部件 293 → 341（同 S24 r15）；两个测试之后部件库共 512 个条目；再做一次同步的 dry run 没有可做的。— Large 测试 2026-10-21。
- 运行期间 EPLAN 的系统日志（EplLog）没有出现 `S328014`（S24 r15 没有 `SafetyPoint` 时出现过），只有 EPLAN 启动时的 `S336007`。— Large 测试 2026-10-21。
- 未验证：一次 Ctrl+Z 能否撤回同步的项目一侧；测试写进测试输出的耗时和审批文本（Microsoft.Testing.Platform 即使用 `--output Detailed` 也没有打印通过的测试的 xUnit 输出）。— Large 测试 2026-10-21。
- 恢复：EPLAN 关闭时，把 28 个文件（`.alk` 和 `.adb\*`）从运行前的副本复制回去，每个文件的 SHA-256 都与运行前相同（这次 `User.*` 也相同）。— Large 测试 2026-10-21。

### A47. 翻译库导入 `Translate.ImportDatabaseTexts`（S24 r19，2026-10-26，EPLAN 2026.0.3.25421，Electric P8，项目负责人指示在正在用的翻译库上执行）

- 签名是 `ImportDatabaseTexts(strFilename, strConverter, strReferenceLanguage)`：第三个参数是**一个参考语言**。传 `"en_US;zh_CN"` 时返回 true、耗时 0.02–0.04 秒，翻译库逐字节不变。
- 用转换器 `XTrLanguageDbXml2TabConverterImpl`、参考语言 `en_US`、表头与导出完全相同（UTF-16LE 带 BOM，21 列，最后一列 `COMMENT`）的文件导入：新词条被新增；参考原文已存在的词条，空缺的语言被补上，**已有译文不被改写**（`Motor terminals` 的 `zh_CN` 仍为“电机端子”）；同一参考原文再导入不产生重复。0.02–0.07 秒，不弹对话框，系统消息中没有记录。
- **返回值不可靠**：没有写入任何东西时也返回 true；只能用导入前后的 `ExportDatabaseTexts`（制表符约 0.7 秒、2.2 MB）比较结果。
- 只改 `$(md_translate)\Translate.db`（修改时间随写入更新）；同目录的 `translate.etd`、`TranslateReports.etd`、`TranslateTemplate.db` 不变，不产生锁文件或其他文件。关闭 EPLAN 后用文件副本替换 `Translate.db`，四个文件与测试前逐字节相同。
- 只含两列（`en_US`、`zh_CN`）表头的文件只在错误的第三个参数下试过，结果不能说明部分列文件是否可用。
— 协调人经 S24 spike（`importtranslation`、`exporttranslation` 模式）实测：先文件副本加 SHA-256、再 `backup MASTERDATA` 备份，导入测试词条，导出比较，最后关闭 EPLAN 恢复并核对哈希。

### A103. 主数据测试可以隔离：把配置指向副本（2026-11-05，EPLAN 2026.0.3，Pro Panel）
- `Settings.SetStringSetting("USER.PartsManagementGui.Database", 路径, 0)` 和 `USER.TRANSLATEGUI.DATABASE_NAME` 改完立刻生效，不用重启：`MDPartsManagement.SelectedPartsDatabaseAsString`、`Translate.TranslateDatabase` 都读到新路径。原值是 `$(MD_PARTS)\EPLAN_parts.alk`、`$(md_translate)\Translate.db`。
- 这样运行 `eplan_part_import`、`eplan_part_sync`、`eplan_translation_import`，正在用的两个库 29 个文件（`.lck` 除外）的 SHA-256 不变，包括 `User.eod`、`User.eox`；副本的 `Translate.db` 变了。A30 说的“副本隔离不了导入”只针对按路径打开另一个库。
- 设置是每个变体各一份，用文件备份还原后哈希一致。
— `MasterDataOnCopyTests`。

### A137. 通用入口读部件库主数据（2026-11-12，EPLAN 2026.0.3.25421，Electric P8，scratch 项目 自动喂丝机，ADR-0054）
- `new MDPartsManagement().OpenDatabase()` 在插件里打开 EPLAN 当前配置的部件库（本机为 `EESS2026_Parts`），返回的 `MDPartsDatabase` 可以作为会话句柄跨调用使用：`Name` 读出库名，`GetPart("SE.XVGKB3SW")` 返回 `MDPart`，`MDPart.Variant` 读出 `"1"`。四次调用都只读，执行状态 committed。
- `OpenDatabase` 和 `MDPartsDatabase.Close` 不以 Get/Is 开头，生成器原本归为写入，被当成主数据写入拒绝；现在专门归为读取。
- 当前配置的是在用库而不是 scratch 副本时，`MDPart.Variant` 的写入预演按设计返回 POLICY_DENIED，没有打开审批。副本上的写入（备份、写入、读回、原库哈希不变）要等工程师在 EPLAN 部件管理中把库配置成 scratch 副本后再验证。
- **子类句柄（同日）：** `GetPart("PXC.3209510")` 的 `FunctionTemplatePositions` 返回 1 项，运行时类型是 `MDTerminalTemplatePosition`，句柄按子类给出，`TerminalCategory` 读出 `"Common"`；`MDPartsDatabase.Close` 正常关闭。生成器因此把返回类的公开子类也算作可达。
- 一次审批窗口关闭后，EPLAN 主窗口保持禁用、桌面上看不到对话框，后续调用都是 EPLAN_BUSY（ModalDialog）；重启 EPLAN 后恢复。当时插件日志只记操作开始，看不出审批怎样结束。可能的原因：审批窗口的兜底逻辑（窗口被销毁时 `Dispatcher.ExitAllFrames()`）从外部结束模态循环，跳过了 WPF 恢复被 `ShowDialog` 禁用窗口的步骤。现在审批窗口打开前记下本线程可见且可用的顶层窗口，结束后把仍被禁用的重新启用；关闭 1 秒后模态循环仍未结束的也会被结束；每次审批记录结束日志（决定、结束方式、打开时长、重新启用的窗口数）。
- **审批窗口被销毁的来源（同日查明）：** 新日志记到一次 `cancelled, Destroyed, 0.8 s open, 1 window(s) re-enabled`，没有人点击，窗口打开 0.8 秒就被销毁，EPLAN 刚重启不久。当时所有者取的是 `Process.MainWindowHandle`，它可能是一个临时窗口，所有者被销毁时，它名下的窗口一起被销毁。现在所有者取本线程可见的 `AfxMDIFrame*` 主框架窗口，日志记录所有者的类名；重启后立刻审批，记录为 `granted, Closed, owner AfxMDIFrame140u`。
- **只出现在返回值里的标量（同日）：** `StorableObject.TypeIdentifier` 对项目读出 `"14"`，`MDPartsDatabaseItem.GetTimeStampOfLastChange` 对 `PXC.3209510` 读出 `"2026-04-13T19:50:07.0000000"`（插件按 ISO 8601 输出 `DateTime`）。`Int16`、`UInt16`、`UInt32`、`Byte`、`SByte`、`Single`、`DateTime`、`StringCollection` 只作返回值开放，不作参数，也不开放写入。
- **导出到输出目录（同日）：** `Export3D.ProjectToIFC(oProject, strTargetDirectory)` 在自动喂丝机上执行，插件先在本次调用的 `exports/api-try-<ID>/` 下建好调用方命名的目录 `ifc`，EPLAN 在其中写出 `=+ (UC1).ifc`（35138 字节，以安装空间命名）；执行状态 committed，前后快照有 4 处变化（没有展开查看）。
- **服务对象上的属性：** `new PathInfo().Macros` 读出宏目录（本机为 SynologyDrive 下的 `003.EPLAN\Data\宏\Company name`）。服务对象每次调用都新建、用完释放，所以通用入口对服务属性只开放读取、只限标量返回值，不交出指向已释放服务的句柄。
— `scratchpad/direct/md-live.py`、`md-sub.py`（未合入）。

### B14. Function.AddArticleReference 的文档范围（2026-11-04，安装版 2026.0.3 XML）
- `M:Eplan.EplApi.DataModel.Function.AddArticleReference(System.String,System.String,System.UInt32)` 返回 `ArticleReference`，向功能增加部件引用；文档说明部件来自系统部件库/项目，明确列出的特殊限制为 ArticlePlacement 只能有一个引用，未列 Terminal 必须是主功能的限制。此事实不等于端子运行时验证；已有端子的追加、提交后读回及撤销由本轮 Large 待验。A87 的创建事务限制针对新建 ConnectionDefinitionPoint，不据此推断所有 Function。

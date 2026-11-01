# [EPLAN API 笔记](../api-notes.md)：主数据、部件与翻译库

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

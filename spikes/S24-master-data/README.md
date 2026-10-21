# Spike S24：部件库与翻译库

> 状态：**完成**（2026-10-21）。只读部分（r0）；写入、备份与恢复部分（r1–r10，ADR-0019 的前提），写入只在还原到 scratch 中的副本上进行；导入与导出（r11，第四批的规格要用）。
>
> 🔴 **r11 意外改动了正在用的部件库**：在副本上导入时，EPLAN 改了配置的那个库的 `User.eod`（2 个字节）和 `User.eox`（1 个字节），部件数据没变。经项目负责人批准，已按结论 6 的步骤用备份中的原文件恢复，数据文件与实验前逐字节一致。见结果中的"导入与导出"。

## 问题
[M4c 设计](../../docs/design/2026-10-21-m4c-more-tools.md)第 4.8、4.9 节。只读部分回答：
1. EPLAN 配置的是哪个部件库、哪个翻译库，是文件还是 SQL Server；
2. 在 Bridge 调度器中按部件号取部件、遍历全部部件的耗时，能读出哪些字段（`eplan_part_search`、`eplan_device_place` 要用）；
3. 只读访问是否改动库文件。

写入部分回答（[ADR-0019](../../docs/adr/0019-master-data-writes.md) 的前提）：

4. `backup /TYPE:MASTERDATA` 在 Bridge 中是否可用：源路径能否从 EPLAN 的配置得出，耗时多少，备份是什么；
5. 能否按备份恢复，恢复的步骤是什么；
6. 部件库 API 的事务能撤回哪些修改。

## 方法
- `SpikeS24Action` 在主线程上经调度器运行，不打开、不修改任何项目，不新建、升级、选择任何库，也不设置库的配置（XML：设置会改全局用户设置）。
- `partsdb`：`MDPartsManagement.SelectedPartsDatabaseAsString`；`new MDPartsManagement().OpenDatabase()`；`GetPart`、`GetPartVariants`、`ExistsPart`、`Parts`；最后 `Close()`。
- `translationdb`：读 `Translate.TranslateDatabase`、`ActiveTranslateDatabase`。
- 前后查看库文件的修改时间。
- 环境：EPLAN 2026.0.3.25421，Electric P8，2026-10-21，r0。

写入部分（r1–r10）**从不写正在用的库**：
- 前后对正在用的两个库的全部文件计算 SHA-256，核对没有变化。
- `backupparts`、`backuptranslation`：按 Bridge 的方式，在调度器中用 `new ActionManager().FindAction("backup").Execute(context)` 备份；`SOURCEPATH`、`FILENAME` 由配置的库路径（`SelectedPartsDatabaseAsString`、`Translate.TranslateDatabase`）拆出；目标是 scratch 中的空目录。r1 之前另用 Remote Client 执行过同样的备份。
- `restore`：同样在调度器中执行 `restore /TYPE:MASTERDATA`，目标只在 scratch 中。
- `opencopy`：`new MDPartsManagement().OpenDatabase(副本路径)` 打开还原出来的副本（路径必须在 scratch 中），在事务中添加测试部件 `EPLANMCP.S24-TEST` 后回滚、再添加后提交，重复添加，删除后回滚；前后读 `SelectedPartsDatabaseAsString`。
- `copyio`（r11）：在副本上 `ExportParts`（XML、EDZ、单个部件），删除测试部件后用 `ImportParts` 导入回来（先在事务中、再不在事务中）；用 `Translate.ExportDatabaseTexts` 把正在用的翻译库导出到 scratch（只读）。
- 同一个 EPLAN 中再次注册要换程序集名：`-p:AssemblyName=EplanMcp.Spikes.S24rN`，驱动加 `--revision rN`。编译另一个版本会删掉上一个版本的 dll。

```powershell
dotnet build spikes\S24-master-data\EplanMcp.Spikes.S24.csproj -c Release
dotnet build spikes\S24-master-data\Driver -c Release
spikes\S24-master-data\Driver\bin\Release\net481\EplanMcp.Spikes.S24.Driver.exe --host <本机网卡地址> --port <Remote Client 端口>
# 写入部分（每次换一个 rN，先用 -p:AssemblyName=EplanMcp.Spikes.S24rN 编译）
... --revision rN --modes backupparts,backuptranslation --destination "<scratch>\spikes\s24\bridge-backup"
... --revision rN --modes restore --part "<scratch>\spikes\s24\bridge-backup\s24-parts.zw6" --destination "<scratch 中的空目录>"
... --revision rN --modes opencopy --part "<scratch 中还原出的>\EPLAN_parts.alk"
# r11 会改正在用的部件库的 User 表（结论 8），运行前要经项目负责人批准
... --revision rN --modes copyio --part "<scratch 中还原出的>\EPLAN_parts.alk" --destination "<scratch 中的目录>"
```

原始结果在 `.local/spikes/s24/`，不提交。

## 结果
**部件库**：
- 配置为 `$(MD_PARTS)\EPLAN_parts.alk`（`PartsService.PartsDatabase` 相同）。`.alk` 是 40 字节的链接文件，指向同目录的 `EPLAN_parts.adb\`；`.adb` 是与项目数据相同格式的目录（`Article.eod` 等），共 3.9 MB。`MDPartsDatabase.Type` 报告 `ACCESS`。
- `OpenDatabase()` 0.02 秒；`IsReadOnly` 为 false，`IsSchemeUpToDate` 为 true。
- `GetPart("SIE.3RH2122-1HB40")` 立即返回：变体 `1`，类型 `Component`，产品组 `ElectricalRelay`，制造商 `SIE`，型号 `3RH2122-1HB40`，订货号 `3RH21221HB40`，宏 `$(MD_MACROS)\Siemens\Industrial_Controls\3RH2\SIE.3RH2122-1HB40_3D.ema`，5 个功能模板；描述是多语言的原始字符串（`de_DE@…;en_US@COUPLING CONTACTOR RELAY, 2NO+2NC;…`）。
- 不存在的部件号：`GetPart` 返回 null。
- `Parts` 返回全部 464 个部件，0.07 秒；在内存中按部件号过滤不到 0.01 秒。
- 读取前后，`.adb` 中的数据文件（`.eod`、`.eox`）修改时间不变，只有 `.lck` 锁文件被改写。

**翻译库**：配置为 `$(md_translate)\Translate.db`，类型 `EPLAN`；文件 3.6 MB。

**备份**（写入部分）：
- 在调度器中经 `ActionManager` 执行，返回 true：部件库 0.14 秒，得到 `s24-parts.zw6`（991 KB）；翻译库 0.23 秒，得到 `s24-translate.zw7`（912 KB）。源路径直接由配置拆出：`SOURCEPATH` 为 `$(MD_PARTS)`、`$(md_translate)`，EPLAN 自己展开变量。
- 备份是 7z 格式的压缩包（文件头 `7z¼¯`），扩展名部件库 `.zw6`、翻译库 `.zw7`。部件库的备份含 `.alk` 和 `.adb\` 中的数据文件，不含 `.lck` 锁文件和同目录的 `EMPTY_SQL_Backups\`；翻译库的备份只含 `Translate.db`，不含 `.etd` 文件。
- 经 Remote Client 传入含中文的路径（`…\Data\部件\…`）时，路径变成乱码，备份返回 FALSE，日志 `S025033 源目录不存在`。在进程中传参没有这个问题；用路径变量也可以避开。
- 备份和还原前后，正在用的两个库的全部文件（含 `.lck`）SHA-256 不变。

**还原**：
- 还原到空目录：0.1 秒，返回 true；还原出的文件与原库逐字节相同（锁文件除外，翻译库保留原来的修改时间）。
- 🔴 **还原不覆盖已有文件**：还原到一份改过的副本上（无论 EPLAN 是否打开过它），照样返回 true、0.1 秒，日志不记录，但文件仍是改过的版本。
- 把改过的库（`.adb\` 和 `.alk`）移到别处、再还原到空出来的位置：与原库逐字节相同。

**写入副本**（`opencopy`）：
- 按路径打开副本 0.29 秒，`IsReadOnly` 为 false，464 个部件；打开、关闭前后 `SelectedPartsDatabaseAsString` 都是 `$(MD_PARTS)\EPLAN_parts.alk`：**按路径打开另一个库不改 EPLAN 的配置**。
- 事务中 `AddPart` 之后，提交前 `ExistsPart` 为 false；`Rollback` 之后部件不存在；`Commit` 之后存在，共 465 个，重新打开后仍在，`Article.eod` 被改写。
- 重复添加：`AddPart` 抛出 `MDInvalidArgumentException`（`S030025`，部件已存在）。
- 🔴 **`RemovePart` 不能回滚**：事务中删除后立即看不到这个部件，`Rollback` 之后仍然没有（464 个），重新打开后也没有。
- r1 没有释放事务：`AddPart` 在事务中抛出异常后，下一次 `CreateTransaction` 抛出 `SEHException`。r3 起每个事务都用 `using` 释放，异常之后的事务正常。

**导入与导出**（r11，`copyio`，副本为还原出的原样库）：
- `MDPartsDatabase.ExportParts(文件, DataFormat.XML)`：全部 464 个部件 2.05 秒，5.0 MB；`DataFormat.EDZ`：44 秒，128 MB，日志中有几十条"无法找到宏数据"的警告（示例库中软管配置器的宏不在本机），照样返回。按部件列表导出一个部件的 XML：4 KB。导出不改正在用的库。
- `Translate.ExportDatabaseTexts(文件, 转换器)`：`XTrLanguageDbXmlConverterImpl`（XML）0.68 秒、2.8 MB；`XTrLanguageDbXml2TabConverterImpl`（制表符）0.76 秒、2.1 MB。翻译库不变。
- `MDPartsDatabase.ImportParts(文件, "XPamImportXml", AppendNewRecords, false, PartNumber, "")`，在事务中：
  - 部件导入了副本，事务中就能看到；🔴 `Rollback` 之后仍在，**事务撤不回导入**。
  - 实际执行的是界面的导入 action `XPamImportXMLAction`（模块 ArticlesGui，参数里带副本的 `databaseid`）。它在批处理模式下试图打开 `SystemErrDialog`，其中的 `XPamNCPMSummarizeFunctionTemplatesAction` 失败："在任务打开状态下，不允许执行撤销/重做步骤"。
  - 之后的三次导入（不在事务中，`AppendNewRecords` 和 `UpdateAndAppend`）都抛出 `BaseException`，日志为"不允许套叠撤销日志"：第一次导入留下了没有关闭的撤销日志。不在事务中的导入能否成功，因此没有得到结果。
  - 🔴 同一时刻（08:11:19），**配置的那个库**的 `User.eod` 有 2 个字节从 2 变为 4（当前用户那条记录里的一个计数），`User.eox` 有 1 个字节变化，各表的 `.lck` 被改写；部件数据文件没有变化，翻译库没有变化。导入流程不只作用于打开的那个库对象。

## 结论
1. **`eplan_part_search` 可行**：部件库只读访问很快，示例库全部部件不到 0.1 秒，可以在 Bridge 中按部件号、型号、描述过滤。多语言描述要按语言取，与项目文本的读法（语言回退设计）一致，读法在写工具规格时确认。
2. **`eplan_device_place` 的规划可以先确认部件和宏**：`GetPart` 给出宏路径，部件不存在时为 null，用来返回 `PART_NOT_FOUND`，或者在没有宏时拒绝。
3. **开发机上两个库都是文件型的**（ADR-0019 的前提之一）：部件库是 `.alk` 加 `.adb` 目录，翻译库是一个 `.db` 文件。
4. 只读访问不改数据文件，但会改写锁文件。
5. **ADR-0019 的前提成立**：`backup /TYPE:MASTERDATA` 在 Bridge 中可用（经 `ActionManager` 在调度器中执行），源路径由配置的库路径拆出，不到 0.3 秒；按备份能恢复到逐字节相同。Bridge 实现时：
   - 备份后检查目标目录中有且只有一个 `.zw6`（部件库）或 `.zw7`（翻译库），且不为空，否则 `BACKUP_FAILED`；
   - 参数在进程中传递，不经命令行，含中文的路径没有问题。
6. **恢复步骤**（写进入门指南）：还原不覆盖已有文件，所以不能直接还原到库所在的目录。
   1. 在 EPLAN 中把备份还原到一个新的空目录（`restore /TYPE:MASTERDATA /ARCHIVENAME:<备份> /DESTINATIONPATH:<空目录>`，或者在界面中还原主数据）；
   2. 关闭 EPLAN；
   3. 把库目录中的部件库（`EPLAN_parts.alk` 和 `EPLAN_parts.adb\`）或翻译库（`Translate.db`）移到别处保留；
   4. 把第 1 步还原出的文件复制到库目录，再启动 EPLAN。

   第 2–4 步在 scratch 副本上验证过等价的做法（移开、还原到空出来的位置，逐字节相同）。r11 之后，经项目负责人批准，在正在用的库上按这个步骤恢复了 `User.eod`、`User.eox`：还原到空目录，关闭 EPLAN，替换这两个文件，与实验前逐字节一致。
7. **部件库 API 的事务只能撤回添加**：`RemovePart` 立即生效，回滚不能恢复。写部件库的工具不能把事务当作回滚手段，失败时一律按备份恢复（ADR-0019）；事务要用 `using` 释放，否则下一个事务抛出 `SEHException`。
8. **按路径打开另一个库不改配置**，`AddPart`、`RemovePart`、`ExportParts` 只作用于打开的副本。但 🔴 **导入不隔离**（r11）：在副本上调用 `ImportParts` 也改了配置的那个库的 `User` 表。所以：
   - 部件库导入、同步，翻译库导入（`partslist /TYPE:IMPORTTOSYSTEM`、`synchronize /TYPE:PARTSTOSYSTEM`、`translate /TYPE:IMPORTTOTRANSDB`）的 Large 测试一律算写正在用的库：只能由项目负责人在审批中批准后运行，运行前后按结论 6 备份和恢复；
   - 导入不能放在 `MDPartsDatabase` 的事务中（撤销日志冲突），事务也撤不回导入，失败时一律按备份恢复；
   - 导出（部件库、翻译库）只读，可以在正在用的库上自动测试。
9. **第四批的导出工具**：`eplan_part_export` 用 `MDPartsDatabase.ExportParts`，默认 XML（2 秒）；EDZ 带宏和图片，128 MB、44 秒，按需选择。`eplan_translation_export` 用 `Translate.ExportDatabaseTexts`，不到 1 秒。

## 局限
- 只有一个部件库（EPLAN 自带的示例库，464 个部件）；大型部件库的遍历耗时没有测。
- 没有试 SQL Server 上的库。
- 翻译库只读了配置，没有打开；只做了备份和还原，没有写入。
- 恢复步骤的第 2–4 步没有在正在用的库上做；7z 格式的备份能否不用 EPLAN 直接解压，没有试。
- 导入只试了在事务中的一次，不在事务中的导入、同步、翻译库导入都没有结果（见结论 8）；导入改动 `User` 表的那个计数的含义没有查。

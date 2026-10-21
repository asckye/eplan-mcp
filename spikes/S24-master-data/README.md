# Spike S24：部件库与翻译库

> 状态：**只读部分完成**（2026-10-21）。写入、备份与恢复的部分（M4c 设计 4.8、ADR-0019 的前提）在 ADR-0019 被接受后、用复制出来的测试库进行。

## 问题
[M4c 设计](../../docs/design/2026-10-21-m4c-more-tools.md)第 4.8、4.9 节。只读部分回答：
1. EPLAN 配置的是哪个部件库、哪个翻译库，是文件还是 SQL Server；
2. 在 Bridge 调度器中按部件号取部件、遍历全部部件的耗时，能读出哪些字段（`eplan_part_search`、`eplan_device_place` 要用）；
3. 只读访问是否改动库文件。

## 方法
- `SpikeS24Action` 在主线程上经调度器运行，不打开、不修改任何项目，不新建、升级、选择任何库，也不设置库的配置（XML：设置会改全局用户设置）。
- `partsdb`：`MDPartsManagement.SelectedPartsDatabaseAsString`；`new MDPartsManagement().OpenDatabase()`；`GetPart`、`GetPartVariants`、`ExistsPart`、`Parts`；最后 `Close()`。
- `translationdb`：读 `Translate.TranslateDatabase`、`ActiveTranslateDatabase`。
- 前后查看库文件的修改时间。
- 环境：EPLAN 2026.0.3.25421，Electric P8，2026-10-21，r0。

```powershell
dotnet build spikes\S24-master-data\EplanMcp.Spikes.S24.csproj -c Release
dotnet build spikes\S24-master-data\Driver -c Release
spikes\S24-master-data\Driver\bin\Release\net481\EplanMcp.Spikes.S24.Driver.exe --host <本机网卡地址> --port <Remote Client 端口>
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

## 结论
1. **`eplan_part_search` 可行**：部件库只读访问很快，示例库全部部件不到 0.1 秒，可以在 Bridge 中按部件号、型号、描述过滤。多语言描述要按语言取，与项目文本的读法（语言回退设计）一致，读法在写工具规格时确认。
2. **`eplan_device_place` 的规划可以先确认部件和宏**：`GetPart` 给出宏路径，部件不存在时为 null，用来返回 `PART_NOT_FOUND`，或者在没有宏时拒绝。
3. **开发机上两个库都是文件型的**（ADR-0019 的前提之一）：部件库是 `.alk` 加 `.adb` 目录，翻译库是一个 `.db` 文件。备份是复制这些文件，还是用 `backup /TYPE:MASTERDATA`，在写入部分中验证。
4. 只读访问不改数据文件，但会改写锁文件：部件库被 EPLAN 打开时，复制它的文件得到的结果是否一致，没有验证。

## 局限
- 只有一个部件库（EPLAN 自带的示例库，464 个部件）；大型部件库的遍历耗时没有测。
- 没有试 SQL Server 上的库。
- 翻译库只读了配置，没有打开。

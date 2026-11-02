# [EPLAN API 笔记](../api-notes.md)：Action、服务与导入导出

### A16. 检查、报表、导出（spike S11，2026-10-20，EPLAN 2026.0.3.25421，Electric P8，在调度器中）
详细数据见 [spike S11](../../../spikes/S11-workflow-apis/README.md)。
- **`Check.VerifyProject(project, "", false)` 能被 `SafetyPoint` 回滚，提交后 Ctrl+Z 一步撤销**：检查消息 0 → 20 条 → 回到 0 条。用时 2.7–4.7 秒，改写 `Message`、`Project`、`Blob`、`User` 数据文件（同 A11）。`VerifyPages`（2 页）0.2 秒。— S11。
- **`Reports.GenerateProject(project)` 能被 `SafetyPoint` 回滚，提交后 Ctrl+Z 一步撤销**：示例项目新增 1 张报表页，用时 7.7–9.3 秒。`Reports.Update(page)` 更新一张目录页 0.3 秒。— S11。
- **导出不改动设计数据，但改写 `User.eod` / `User.eox`**：`Export.PdfPages` 3 页 0.7 秒，全部 321 页 12.5 秒（6.1 MB）；`Export.DxfDwgPagesToDisk` 2 页 0.6 秒。文件只出现在给定目录中，但 DXF 方案会在目录下按"项目名\结构"建子目录，文件名含 `=`、`+`、`&`。方案名传空串时，`SchemeSetting.GetLastUsed()` 前后不变。— S11。
  - 对本项目的影响：ADR-0012 的前提"导出不改动项目"指设计数据；导出后要递归核对目录中的每个文件（M4 设计 4.4）。
- 以上调用在 `QuietModeStep(ShowNoDialogs)` 下都没有弹出对话框。— S11。

### A25. M4c 的导出（spike S25，2026-10-21，EPLAN 2026.0.3.25421，Electric P8，在调度器中）
详细数据见 [spike S25](../../../spikes/S25-exports/README.md)。示例项目的 scratch 副本。
- `Export.PXFProject(project, 路径, 主数据, 连接)`：带主数据 36.9 秒、840 MB，不带主数据 19.2 秒、598 MB；扩展名 `.epj` 由 EPLAN 补上，另外留下一个与文件同名的空目录。只改写 `User.eod`。— S25。
- `PartsService.ExportPartsList`：XML 61.9 秒、14 MB；CSV 89.9 秒、20 MB（分号分隔）。只改写 `User.eod`。— S25。
- 🔴 部件清单 CSV 导出返回后约 1 秒，EPLAN 记录两次访问冲突（0xC0000005），随即关闭全部项目；进程留下来，但没有窗口、没有 Remote Client 端口和 Bridge 管道，不再响应。是导出的收尾还是下一个 action 触发的，尚未查清；在重新启动的 EPLAN 中，设备清单导出本身正常。— S25。
- `DeviceService.ExportDeviceList` 导出预规划用的设备清单，示例项目中为空文件；全部设备的清单要用标签方案 `Device tag list`。— S25。
- `Label.DoLabel(project, 方案, 语言, 文件, 1, 1)`：`Device tag list`（文本，40 KB）、`Terminal diagram`（xlsx，805 KB）各约 2 秒，生成 xlsx 时没有 Excel 进程；传入方案名不改写"上次使用"。方案中的默认目标在 `$(MD_DOCUMENTS)` 下，所以总要给文件名。— S25。
- `PlcService.ExportAddressOverview` 不给 PLC 配置项目、站和 CPU 时，输出只有一行空值。— S25。
- `Translate.ExportMissingTranslation(文件, "XTrLanguageDbXml2TabConverterImpl", 语言, [project])`：3.5 秒，制表符分隔。示例项目有 20 种翻译语言，显示语言和源语言为 `##_##`。— S25。

### A28. 翻译、项目语言与修订（spike S23，2026-10-21，EPLAN 2026.0.3.25421，Electric P8，在调度器中）
详细数据见 [spike S23](../../../spikes/S23-translation-revisions/README.md)。示例项目的 scratch 副本。
- 项目语言在项目设置 `TRANSLATEGUI.TRANSLATE_LANGUAGES`（示例 20 种）、`DISPLAYED_LANGUAGES`（`##_##;`）、`SOURCE_LANGUAGE`（`##_##`）中。多语言文本存储为 `de_DE@…;en_US@…;`。— S23。
- `Translate.TranslateObjects([project])` 2.6–2.9 秒；示例项目已全部翻译，没有文本改变，但改写 `Project`、`Page`、`Blob` 等文件。— S23。
- `SetDisplayedLanguages` 接受 `"en_US"` 和逗号分隔的 `"en_US,zh_CN"`（存为 `en_US;zh_CN;`）；用分号分隔或带结尾分号都抛 `ArgumentException`（S029035）。设置不被 `SafetyPoint` 回滚。— S23 r0、r1。
- 只有德语的功能文本（`de_DE@Motor;`）经 `TranslateObjects` 后不变（项目源语言为 `##_##`）。— S23 r1。
- **翻译的前提是项目的源语言**：`TRANSLATEGUI.SOURCE_LANGUAGE` 为 `##_##` 时 `TranslateObjects` 不翻译、不报错；设为 `de_DE` 后，`de_DE@Motor;` 0.04 秒译进项目的全部 19 种翻译语言（`zh_CN@电机` 等，按翻译库）。源语言用 `ProjectSettings.SetStringSetting` 写，不被 `SafetyPoint` 回滚，要自己写回。— S23 r2。
- 🔴 `RemoveProjectLanguage(project, "sv_SE")` 删除该语言的全部译文、把它移出翻译语言，还把显示语言改为 `zh_CN;`。`SafetyPoint` 回滚撤回了译文，但**没有撤回语言列表和显示语言**。— S23。
- 🔴 `RemoveProjectLanguage` 删除了语言也返回 false：在不是当前项目的写副本上、`SafetyPoint` 中删除 `sv_SE`，返回 false，翻译语言中却已没有 `sv_SE`，显示语言从 `##_##;` 变为 `zh_CN;`，回滚后设置仍是删除后的样子（与 S23 一致）。结果要按读回的翻译语言判断，不能看返回值。— 2026-10-21，S24 r18。
- 🔴 `Revision.CreateRevision(project, 名称, 说明, ref 路径)` 0.04 秒，返回的路径为空，项目链接文件从 `.elk` 变为 `.ell`（修订跟踪中的项目）；Ctrl+Z 不撤回。`Project.Comparisons` 和 `Revision.GetUncompletedPages` 前后都是 0。— S23。
- 修订状态（S23 r3）：链接文件扩展名 `.elk` 普通项目、`.ell` 修订进行中（变更跟踪）、`.elr` 完成的修订（写保护，`CompleteProject` 把 `.ell` 改为 `.elr`，`PROJ_REVISIONSPARENT` 为原路径）。修订记录在项目的带索引属性 `PROJ_REVISION_LOG_NAME/DESCRIPTION/USER/DATE[i]`（i 从 1 起；`DATE` 读出为 Unix 秒数的字符串），新建修订时写第 1 条，`PROJ_REVISION_LOG_START` 为 1。用 API 改页不会产生页面的修订记录或草稿标记；从未完成项目时 `GetUncompletedPages` 为空。— S23 r3。

### A29. 导入（spike S22，2026-10-21，EPLAN 2026.0.3.25421，Electric P8，在调度器中）
详细数据见 [spike S22](../../../spikes/S22-imports/README.md)。示例项目的 scratch 副本。
- `new Import().DXFPage(文件, 页, new Transformation(文件, 页, "", ScalingMode.Automatic, PositionMode.Center, false), "")`：Rittal 示例 DXF 在 0.04–0.3 秒内给页加上 227 个图形对象，能被 `SafetyPoint` 回滚，提交后一次 Ctrl+Z 撤销。— S22。
- 🔴 `Import.PXFProject(文件, 目标)` 的第二个参数是**新项目的路径（不带扩展名）**，不是 XML 所说的目标目录：`…\imported` 得到 `imported.elk` 和 `imported.edb`。示例项目的 598 MB EPJ 导入约 58 秒，得到 171 MB 的项目，导入后不自动打开；中途被结束时留下不完整的 `.edb`。— S22。
- `PartsService.ImportPartsList(project, 文件, Format.XML)`：把刚导出的同一份清单导回，69 秒，项目部件数和部件引用数都不变。— S22。

### A39. M4c 剩余工具用到的 action（spike S34，2026-10-22，EPLAN 2026.0.3.25421，Electric P8，在调度器中）
详细数据见 [spike S34](../../../spikes/S34-m4c-actions/README.md)。

- **`generate /TYPE:CABLES` 关闭保留旧名时会重编号已有电缆**：示例项目三个方案（创建、编号、自动选型）均只有 `Standard`；默认参数或 `KEEPOLDNAMES=1, ONLYAUTOCABLES=1` 不改变名称，项目没有自动电缆。`KEEPOLDNAMES=0, STARTVALUE=5, STEPVALUE=2, ONLYAUTOCABLES=0` 把 78 根中的 70 根改名（如 `+B1-WG2` → `+B1-W7`），没有新增或删除，约 0.2–0.7 秒；回滚恢复全部名称。工具应定为 Destructive，先备份，不能仅比较电缆数量。— S34。
- **设备清单需要非空条目才能验证导入效果**：原清单 XML 为 `<deviceList count="0"/>`，CSV 为 0 字节。`DeviceListEntry` 无公开构造函数，`DeviceService.CreateDeviceListItem(project, partNumber, variant)` 可创建条目；创建 2 条后，默认 XML、`XDLXmlExporter`、`XDLCsvImporterExporter` 均完成导出 → 删除 → 导入恢复 2 条（新对象编号）→ 回滚恢复 0 条，单次 action 约 20–150 毫秒。— S34。
- **部件清单 XML 导入可用但慢**：默认/`XPalXmlExporter` 导出约 14 MB，51–64 秒；`MODE` 省略、`1`、`2` 的导入均为 58–83 秒，部件引用 2,900 → 2,902 → 回滚恢复 2,900。EPLAN 导入时只读打开部件数据库；未使用 `IMPORTTOSYSTEM`，CSV 因 S25 的访问冲突记录而避开。工具需要分钟级长超时和时间余量。— S34。
- 🔴 **PLC 自导出再导入也不可靠匹配已有设备**：`Phoenix1` 用 `PlcDcXMLExchangerUniversal` 导出 431 KB `.pbf`，用 `PlcDcAMLExchangerGeneral` 导出 96–104 KB `.aml`（含 `AutomationML AR APC V1.4.0`），r1 约 2.3 秒。`IMPORTMATCH=0`、`2`、省略均新增对象：功能 2,876 → 3,326（标准格式，新增 450）/ 2,913（AML，新增 37），PLC 盒 20 → 34 / 33；标准格式 `0`/省略的 450 个全名全部重复，AML 有 36 个重复，包括 `=F+A2-KE1`。标准格式 `2` 改名新增并记录“标记一个或多个对象以将其删除.”；`1` 会走比较对话框，始终未使用。只能限制在尚不含这些设备的目标项目，不能当作已有设备的静默更新。— S34。
- **PDF 注释仍缺有效批注样本**：未修改的 EPLAN 自导出 PDF 导入返回 `true`，注释仍为 0；pypdf 添加注释后，全量重写和保留原字节、trailer `/ID`、EPLAN Info 键（`/ProjectId`、`/PageOffset` 等）的增量更新均返回 `false`，系统消息为“PDF 文件无法由 Eplan 生成并因此不能导入.”。— S34。
- **Acrobat Reader 批注的 PDF 可以导入**（2026-10-27，S34 r4）：同一份 EPLAN 自导出 PDF 由工程师在 Adobe Acrobat Reader 中加 3 条批注（第 1 页便笺 `Text`；第 2 页插入标记 `Caret`（带文字）和下划线 `Underline`），Reader 以增量更新保存，EPLAN 的 Info 键保留。`import` `TYPE=PDFCOMMENTS` 返回 `true`，约 0.3 秒，无对话框、无系统消息；项目注释数 0 → 1，其他计数不变；`SafetyPoint` 回滚后回到 0。只导入了 1 条：哪一条、内容与所在页、批注类型或页映射的限制，以及提交后的撤销与重开尚未验证。— S34。
- **导入的是第 1 页的便笺，提交、撤销、重开都正常**（S44）：读回 `Graphics.Comment`，放在项目第 1 页，文本内容（19201）为便笺文字，语言未定义（`??_??`），作者与时间保留，`Location` 为 (0,0)、位置在 19103 中；第 2 页的插入标记和下划线未导入；工程师再在第 2 页加便笺后，两条便笺分别导入到项目第 1、2 页：**只导入便笺（`Text`）批注**，页映射正常。导入是一个撤销步，`SafetyPoint` 回滚不留空步，关闭重开后仍在；同一文件再导入不增加注释、不留撤销步。— S44。
- 完成执行的探针均在 `SafetyPoint` 内，显式 `Rollback()` 恢复可观测的名称、条目和业务计数，未出现对话框，主窗口未禁用；这不保证 `.edb` 逐字节恢复，r3 电缆模式结束仍有 18 个文件哈希不同，也不能推广到进程崩溃时的恢复。— S34。
- 🔴 **不要释放 EPLAN 返回的 `StorableObject`**（finder、属性或集合返回的 `Function`、PLC、`DeviceListEntry` 等），工厂创建的项目条目也交给回滚，不调用 `Dispose`。r2 在约 5 秒内发生原生堆损坏 `0xc0000374`（`ntdll.dll`），无 .NET 异常、无报告；r3 只释放辅助对象（`PropertyValue`、一次获取并释放的属性列表、finder、过滤器、context、服务等），探针完成。不能把所有 `IDisposable` 的数据模型对象都包进 `using`。— S34。

### A40. 长尾 action 的首批白名单（spike S26，2026-10-22，EPLAN 2026.0.3.25421，Electric P8，在调度器中）
详细数据与报告目录见 [spike S26 r1](../../../spikes/S26-action-run/README.md#结果)。版本、变体由运行者提供；结果依据六模式最终 JSON，当前项目为 fixture，显式目标为 LargeWrite，均为已打开的 scratch 副本。

- **进程内 typed context 保留合法特殊字符路径**：`graphicallayertable EXPORT` 的 EXPORTFILE 带 `/` 分隔符、空格、单引号、中文，contextReadBack 精确，178.01 ms 返回 true，实际生成 `slash 子目录\空 格 '单引号' 中文.elc`，exactProducedPath=true。双引号候选 context 仍精确，但返回 false、0 文件，系统消息“路径中具有非法字符。”；Windows 禁止字面双引号文件名，这部分仍不确定，不能据此判断 action 二次解析。— S26。
- **`changelayer` 在 SafetyPoint 与内部 UndoStep 中可回滚**：`PROJECTNAME=<LargeWrite>, LAYER1=EPLAN100, VISIBLE1=0` 在 SafetyPoint 对照（20.09 ms）与 Bridge 包装（4.85 ms）都返回 true，visible 从 true 变 false，Rollback 后恢复 true；Bridge 的 CloseUndo=true，未见冲突。独立候选 16.99 ms，同样恢复图层状态。没有提交，提交后单次 Ctrl+Z 未测；公开撤销历史不可枚举。— S26。
- **计数未变不等于没有变化，回滚不等于恢复原字节**：changelayer 后目标图层和文件内容指纹变化，回滚后图层恢复，Layer.eod/Layer.eox 哈希仍不同；另一打开项目的可比较指纹未变。省略 PROJECTNAME 的 runner (b) 因目标不是当前项目而未运行，当前项目隔离问题尚无该探针的实证。— S26。
- **六种 `partsmanagementapi EXPORT*` 需要属性过滤**：EXPORTPARTS、EXPORTMANUFACTURERS、EXPORTCONSTRUCTIONS、EXPORTCONNECTIONPATTERNS、EXPORTACCESSORYLISTS、EXPORTACCESSORYPLACEMENTS，FORMAT=XPamExportXml；不带 PROPERTYID1/PROPERTYVALUE1 全部返回 false、0 文件，约 1–7 ms，每次新增“输入字符串的格式不正确。”。分别用字典关键属性 22001、22900、22931、22945、22960、22970 加已有记录值后，全部返回 true、各一个非空 XML，约 16–216 ms，无新增系统消息。不能把无过滤用法当作导出全部。— S26。
- **图层表、部件库和属性定义/方案导出未改变可观测项目指纹**：成功的 graphicallayertable EXPORT 约 12–178 ms；projectmanagement EXPORTPROPERTYPLACEMENTSSCHEMAS 为 93.24 ms；XEsUserPropertiesExportAction 用 XMLFile、Project 为 112.14 ms。它们及六种带过滤的 partsmanagementapi 导出均各一个文件，两个项目相对候选 before 的计数与文件指纹未变；parts 候选前后配置库、六类记录数、包含及排除 User.* 的内容摘要也未变。— S26。
- **PLC 总线导出成功，但 AML 的只读副作用尚不能排除**：Phoenix1、OVERWRITE=0、LANGUAGE=de_DE；PlcDcXMLExchangerUniversal、PlcDcAMLExchangerGeneral（省略 FORMAT / AutomationML AR APC V1.4.0）分别返回 true，耗时 3,990.10 / 2,956.56 / 3,151.14 ms，输出 431,387 / 95,787 / 104,341 字节，无新增系统消息。业务计数均未变，标准格式文件指纹未变；两个 AML 候选的 LargeWrite 内容摘要变化，排除 User.* 后仍变化，回滚后未恢复，fixture 未变。报告列出 Blob/Function 文件差异；不能概括为所有导出都不改项目，需继续核实原因。— S26。
- **返回后窗口启用不能证明执行期间无对话框**：所有实际调用返回后和收尾窗口禁用标志均为 false，没有 action/回滚/UndoStep 清理异常；但 EXPORTPARTS 的有过滤候选及三个 PLC 导出的执行期禁用采样为 true。dialogAppeared、engineerDialogObservation 均为空，不能从 JSON 单独确认弹窗与否；两个外部编辑方案发现另记录缺节点的 ArgumentException。— S26。
- **三种原首批用法未运行，按 ADR-0021 Q2 移到“以后”**：EXPORTADDRESSOVERVIEW 缺可匹配的配置项目/站/CPU 元组（样本 CPU 读数为空）；XMActionDCCommonExport、XMExportDCArticleDataAction 没有提供已核实方案节点，未能读出方案名称。缺 CONFIGSCHEME 会弹窗来自 api-actions 7.3 的既有约束，本轮未触发。r2 需完整 PLC 元组及两个导出各自可读的方案，仍固定 EXECUTIONMODE=0、IMMEDIATEIMPORT=0。— S26。

### A50. action 白名单第 2 批的回滚验证（spike S26 r3，2026-10-26，EPLAN 2026.0.3.25421，Electric P8，只在 scratch 副本上）

- `synchronize` 的 `TYPE=MULTILINE`、`SINGLELINE`、`OVERVIEW` 在 `SafetyPoint → UndoStep → QuietModeStep` 中均返回 true、不弹对话框，回滚后业务快照和文件指纹恢复；示例项目上 SINGLELINE 看不到业务变化，需另备有表示差异的样本才能证明效果。
- `SwitchProjectType PROJECTTYPE=…` 把 `Project.TypeOfProject` 从 Schematic 改为 Macro，`UndoStep` 释放后仍保持，`SafetyPoint` 回滚后恢复 Schematic，其他打开的项目不受影响。
- `GetAll<Group>()` 返回的基类包装不能直接 `Cast<Group>()`（`InvalidCastException`）；读取组要经类型化的 `Placement.Group`，无法解析的句柄记为已知缺口而不是空组。
- 经产品工具提交后，当前项目一次 `XEsUndoAction` 即恢复：MULTILINE、OVERVIEW 的功能文本指纹回到提交前，`SwitchProjectType` 的类型回到 Schematic，另一打开的 scratch 副本类型不变；之前创建的哨兵页仍在，再撤销一次即移除，说明 action 只占一个撤销步骤。三项据此以 Write 级别开放；SINGLELINE 因示例上无可观察变化仍待验证（Large `ActionRunTests`，2026-10-26）。
— 协调人经 spike S26 r3（`r2-sync`、`r2-type` 模式）实测，原始报告在 scratch 中，不提交。

### A61. 界面 action 的试执行（C2 `eplan_action_try`，2026-10-31，EPLAN 2026.0.3.25421，Electric P8，只在 scratch 副本 LargeWrite 上）
- 官方参数证据（2026-11-02，离线读取 2026 API 帮助，页脚日期 2026-03-18，未启动 EPLAN）：[backup](https://www.eplan.help/en-us/Infoportal/Content/api/2026/backup.html) 的 `BACKUPMETHOD` 为 `BACKUP/SOURCEOUT/ARCHIVE`，`AUTOCOPYREFDATA` 可选、`0/1`、默认 `0`；第一条 PROJECT 示例没有 `BACKUPMETHOD`，导入保留这一缺口，不补猜测值。[export](https://www.eplan.help/en-us/Infoportal/Content/api/2026/export.html) 的 `FORMAT` 默认 `TIF`，`COLORDEPTH` 为 `1/8/16/24/32`；[renumber](https://www.eplan.help/en-us/Infoportal/Content/api/2026/renumber.html) 的 `STARTVALUE` 为可选 integer、默认 `1`。导入只将文档字面量及示例作为数据，不代表 action 在本机可执行或已验证撤销；动态默认、缺页及名称差异保留 warning。路径模板遵守 scratch imports/exports 分向，但 C2 原有路径与参数禁止表仍生效，需要文件、页或选择上下文的批量行不猜测上下文。
- `XGedZoomAllAction` 在没有打开图形页编辑器时，`Execute` 返回 false，不弹对话框，系统日志也没有消息；工具按失败处理并回滚。先用 `eplan_page_open` 打开一页后再执行，返回成功，业务快照和设计文件指纹都不变。— Large `ActionTryTests`。
- 界面 action 作用于当前项目，所以试执行要求 EPLAN 中只打开目标 scratch 项目、并且它是当前项目。测试中按 A11/A41 依次关闭夹具副本来满足这一条件；恢复现场时，若测试打开过页面，重新打开的项目不会成为当前项目，需要在原当前项目里再打开一页（同 `eplan_page_open` 那条）。— 同上。

### A63. 服务类方法的试执行（C1 第五片，2026-10-31，EPLAN 2026.0.3.25421，Electric P8，只在 scratch 副本 LargeWrite 上）
- C1 Files（2026-11-01，仅核对本机官方 `Eplan.EplApi.HEServicesu.xml`，未运行 EPLAN）：DeviceService.ImportDeviceList/ExportDeviceList 的 strImportFilePath/strExportFilePath 是完整文件路径；PlcService.ImportData 的 strFullName/strInputFileName 是输入文件；PrePlanningService 的定义文件导入/导出明确是项目与文件之间的数据交换。ProjectManagement.ReadProjectInfo 从 XML 读取并设置项目属性，故仍归 write；ConnectionService.ExportProductionWiring 的 strDestinationPath 实际是目录，本片继续排除。产物不随项目撤销沿用 A56，新增 Large 文件写入撤销仅编写、编译。
- `HEServices.NameService.CanDeviceTagBeNested(Function)` 在只读调用时抛 `ApplicationException`（S029128，"未确定任何页"）；先用 `eplan_page_open` 打开该功能所在的页也一样。带页参数的重载 `CanDeviceTagBeNested(Page, Function)` 正常返回布尔值，业务快照与设计文件都不变。需要"当前页"的服务方法，优先选带显式页参数的重载。
- 试执行把这类拒绝报告为已知失败（`EPLAN_OPERATION_FAILED`，`rolled_back`）：只在调用前后快照一致时这样报告，否则仍为 `OUTCOME_UNKNOWN` 并暂停写入。
- `HEServices.ConnectionService.SwapSourceAndTarget` 作为写入试执行：一次撤销恢复连接的源与目标，之前的哨兵页仍在。— Large `ApiTryTests`。

### A66. 静态与 via 试执行的预算失败（2026-11-01，EPLAN 2026.0.3.25421，Electric P8，LargeWrite scratch 副本，串行）
- `ApiTryTests` 共 7 项，6 项通过；`LargeWrite_StaticAndViaReadsAndPageWritePreserveSingleUndoHistory` 在经 Function 定位器、`via:"Page"` 写 `Page.GridSize` 时返回 `OUTCOME_UNKNOWN`。同一测试此前的静态 `IsLogicalPageDocType(Circuit)`、`DeviceTagConfig.EnableSyntaxCheck` 和经 Function.Page 读取 GridSize 已返回且通过断言。— `.local/failures/q-op-static-ApiTryTests.log`，失败位置 `ApiTryTests.StaticEntries.cs:24`。
- 对应 add-in 记录为 `.local/failures/bridge-slotA.log`，05:29:45.294，关联 ID `e704b39ce143474899d28c3fd4c09b11`：`TimeoutException: Trial exceeded its execution budget.`，堆栈为 `ApiTryExecuteOperation.Invoke → TransactionRunner.Run → ApiTryExecuteOperation.Execute`。这不是 EPLAN 的 GridSize setter 拒绝：按该版源码，异常在成员返回、读回比较及执行后 Inspect 完成之后、提交之前由本地预算检查抛出；TransactionRunner 成功回滚后才原样重抛该异常，回滚失败会改抛另一异常。应归为已回滚的预算失败，不能由它推断原生 setter 耗时超过 30 秒。
- 离线代码核对发现，原 30 秒计时还包含重新规划、完整快照和属性原值读取；via 解析依次枚举所有允许的父类型，可能在找到记录页上的 Function 前反复扫描全项目。修复先查记录页并保留类型、对象号、页和摘要核对；成员调用单独计时，Host 为重新规划、观察和收尾留时间。本地超限仅在回滚成功后报告 `TIMEOUT / rolled_back`；调用内部超时、模态阻塞及提交/回滚失败仍为 unknown。Small 以 `Page.GridSize` + Function 定位器 + `via:"Page"` + 数值 2.5/4 覆盖该路径及失败分类。本次修复未运行 EPLAN，整项写入和撤销仍待 Large 复验。

### B3. 自定义 Action
- `IEplAction`：`bool OnRegister(ref string Name, ref int Ordinal)`、`bool Execute(ActionCallingContext ctx)`、`void GetActionProperties(ref ActionProperties)`。**action 名称中不能含有 `.`**。— 反射 + XML。
- `ActionCallingContext`（派生自 `Eplan.EplApi.Base.Context`）：通过 `GetParameter(name, ref value)` 读取输入，通过 `AddParameter(name, value)` 输出结果，全部是字符串。`ContextParameterBlock` 可以传递对象，但只限于进程内。— 反射。
- 进程内调用：`new ActionManager().FindAction(name).Execute(ctx)`，每次调用使用新的 context。— `2026/CallingActions.html`。

### B9. 导出与导出方案（`HEServices.Export`、`Base.SchemeSetting`，2026-10-20）
导出工具依赖的文档说明；实测结果见 A14、A16。
- `Export.PdfPages(ArrayList, 方案, 文件名, DegreeOfColor, 3D 模型, 语言, 打印边距)`：方案为空串时用界面上次使用的方案（`USER.PDFExportGUI.SCHEMAS`）；文件名带路径时覆盖方案中的目录，目录不存在时新建；没有扩展名时补上。`DegreeOfColor` 为 `Color`、`GreyLevel`、`BlackAndWhite`。— XML，枚举名经编译确认。
- `Export.DxfDwgPagesToDisk(Page[], 方案, 目录, 语言)`：方案在 `USER.DXF.SCHEMES`，空串用上次使用的，名称不存在时抛 `BaseException`；方案可以按页树或页属性建子目录；语言非空时先把项目翻译成该语言。— XML。
- `Export.GraphicPage(Page, 目录, Format, 色深, 宽度, Compression, 黑白)`：目录不存在时新建；PNG、JPG 可用色深 24；压缩只影响 TIF；高度按页的比例计算；不能导出外部文档页。— XML。
- `SchemeSetting`：`Init(节点)`；`CheckIfSchemeExists(名称)`；`GetCount()`、`SetScheme(序号)`（不改"上次使用"）、`GetName()`（与语言无关的内部名称）；`GetLastUsed()`；`SetLastUsed(名称)`，空串抛 `ArgumentException`，名称无效抛 `BaseException`。— XML。
- `Export`、`SchemeSetting` 都实现 `IDisposable`。— 编译（CA2000）、XML。
- `SchemeSetting.GetStringSetting(路径, 序号)` 的路径相对于当前方案（XML：从方案名之后开始），设置不存在时抛 `BaseException`。— XML。
  - 🔴 实测（2026-10-21，第一次 Large 测试，EPLAN 2026.0.3）：标签方案（`USER.Labelling.Config`）的相对路径**从方案的 `Data` 节点算起**，不是从方案名之后：`SetScheme` 之后 `GetStringSetting("Data.File.Format", 0)` 抛 `BaseException`，消息中的路径是 `USER.Labelling.Config.<方案>.Data<.Data.File.Format`；`GetStringSetting("File.Format", 0)` 能读到。不经 `SchemeSetting`、用完整路径 `new Settings().GetStringSetting("USER.Labelling.Config.<方案>.Data.File.Format", 0)` 也能读到。— 实机。
- 标签方案（`USER.Labelling.Config`）：EPLAN 自带的方案（`Device tag list`、`Summarized parts list`、`Terminal diagram`、`Cable diagram`、`PCT-Loops`、`SmartWiring`）的输出格式在方案下的 `Data.File.Format`，值是转换器名：`XmlLblTextExportConverterImpl`（前两个）或 `XmlLblExcelExportConverterImpl`（其余）；平台还登记了 `XmlLblXmlExportConverterImpl`。目标文件在 `Data.File.Destination`（如 `$(MD_DOCUMENTS)\BM-Liste.txt`），Excel 方案的模板在 `Data.File.Template`（`.xlsx`）。同一节点下还有 EPLAN 内部的方案 `InternalRefDefScheme`，也是"上次使用"的缺省值。— 只读查看 `Platform\2026.0.3\Cfg\LabelLog.xml`、`LabelLog_User_Config_*.xml`、`regPlatform.xml`（2026-10-21）。
  - 实测读出的格式（2026-10-21，第一次 Large 测试，按上面相对 `Data` 的路径）：`Device tag list`、`Summarized parts list` 为 `XmlLblTextExportConverterImpl`；`Cable diagram`、`PCT-Loops`、`Rittal Length cutting C8+`、`SmartWiring` 为 `XmlLblExcelExportConverterImpl`。`Terminal diagram` 没有在这次读出的方案中，S25 中它导出的是 xlsx。— 实机。
- 项目的翻译语言在项目设置 `TRANSLATEGUI.TRANSLATE_LANGUAGES`（索引 0，分号分隔，如 `en_US;de_DE`）。— CommandLineActions XML 中 `projectsettings` 动作的示例；S25 用 `ProjectSettings.GetStringSetting` 读出示例项目的 20 种（A25）。`Translate.ExportMissingTranslation` 要求每种语言都已是项目的翻译语言。— XML。
- `Label.DoLabel(project, 方案, 语言, 目标文件, 1, 1)`：目标文件的扩展名要与方案的配置一致，应为 `.txt`、`.xls`、`.xlsx` 或 `.xml`，否则换成 `.txt`；`.xls` 需要 Excel；目标为空时用方案中的目标；带 `bShowOutput` 的重载会打开输出文件。— XML。

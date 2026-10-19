# EPLAN 2026 官方 API Actions 逐条核对

- 核对日期：2026-10-18。只依据官方文档，没有在 EPLAN 上执行任何 action；结论汇总在 [API 覆盖矩阵第 2 节](api-coverage.md#2-官方文档中的-action约-100-个)。
- 依据：EPLAN 官方 API 帮助 `eplan.help`，2026 版目录（页脚 "Eplan API, 18.03.2026"）。2025 版（页脚 "EPLAN API , 17.02.2025"）只用于对比。
- 方法：用 curl 下载每个 action 页面的原始 HTML，并提取文本（WebFetch 会做摘要，可能丢失参数，所以没有用它取参数）。action 名称、TYPE 值和参数名都**从页面原样复制**，大小写与拼写保持不变，包括文档中的拼写错误，例如 `DXFDWGPPAGESSCHEME`、`PAGFILTERNAME`。
- 凡是页面没有写明的内容，都标注为“推断”或“未核实”，没有凭记忆补全。

---

## 1. 使用的来源页面

所有 URL 的前缀都是 `https://www.eplan.help/en-us/Infoportal/Content/api/2026/`，2025 版的前缀则是 `.../api/2025/`。

| 用途 | 页面 | 结果 |
|---|---|---|
| **官方 action 总表**（"This is the list of all official Eplan API actions available for the user"） | [API Actions.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/API%20Actions.html) | 列出 **99** 个 action |
| 帮助目录数据（Innovasys 动态目录）："API Reference > Actions" 节点 `n35753` | [_toc/c0.js](https://www.eplan.help/en-us/Infoportal/Content/api/2026/_toc/c0.js) | 含 **98** 个子页面，比总表少 `XAMlExportProductionData2SmartMountingAction` |
| 98 个 action 的详细页面 | `https://www.eplan.help/en-us/Infoportal/Content/api/2026/<action名>.html` | 2026-10-18 全部返回 HTTP 200，已逐页阅读 |
| action 概念与调用方式 | [Actions.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/Actions.html)、[CallingActions.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/CallingActions.html)、[AutomaticActions.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/AutomaticActions.html)、[Command line parameters.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/Command%20line%20parameters.html) | 已读 |
| 用于判断只读操作该走 action 还是 API | [Verifications.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/Verifications.html)、[Messages.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/Messages.html)、[EServices.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/EServices.html)、[API_HEServices.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/API_HEServices.html)、[HE_Selectionset.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/HE_Selectionset.html)、[WorkingWithSettings.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/WorkingWithSettings.html)、[NavigatingTheDatamodel.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/NavigatingTheDatamodel.html)、[WritingSystemMessages.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/WritingSystemMessages.html) | 已读 |
| 类名清单（只按名称核对类是否存在） | [_toc/c554.js](https://www.eplan.help/en-us/Infoportal/Content/api/2026/_toc/c554.js)（HEServices）、[_toc/c511.js](https://www.eplan.help/en-us/Infoportal/Content/api/2026/_toc/c511.js)（EServices）、[_toc/c39.js](https://www.eplan.help/en-us/Infoportal/Content/api/2026/_toc/c39.js)（Base） | 已读 |
| 2025 对比 | [2025 API Actions.html](https://www.eplan.help/en-us/Infoportal/Content/api/2025/API%20Actions.html)、[2025 _toc/c0.js](https://www.eplan.help/en-us/Infoportal/Content/api/2025/_toc/c0.js)、[2025 MfToggleMainMenuAction.html](https://www.eplan.help/en-us/Infoportal/Content/api/2025/MfToggleMainMenuAction.html)、[2025 XPamArticlesSyncAction.html](https://www.eplan.help/en-us/Infoportal/Content/api/2025/XPamArticlesSyncAction.html)，以及 25 个多用途 action 的 2025 页面 | 2025 总表 101 个，目录 100 个 |

### 1.1 版本差异（2025 → 2026）
- 2026 删除了 2 个 action：`MfToggleMainMenuAction` 和 `XPamArticlesSyncAction`。两者在 2025 有页面，2026 的同名页面返回 404。2026 运行时里是否还存在这两个 action，**未核实**。
- 两个版本的总表都列出 `XAMlExportProductionData2SmartMountingAction`，但它的详细页面**在 2025 和 2026 都返回 404**。总表里它的说明与 `XAMlExportProductionData2RASCenterAction` 逐字相同，疑似复制粘贴。
- 对 25 个带 TYPE 的多用途 action 做了逐页对比（backup、check、devicelist、export、export3d、exportToGraphics、generate、graphicallayertable、import、import3d、masterdata、partslist、partsmanagementapi、plcservice、print、projectmanagement、renumber、reports、restore、search、selectionset、subprojects、synchronize、Topology、translate）。**2025 与 2026 的 TYPE 值和参数名完全相同。**
- 曾被当作例子提到的 `XPrjActionProjectCompleteMasterData` **不在**官方 2025 和 2026 的 API Actions 总表中，`.../2025/` 和 `.../2026/XPrjActionProjectCompleteMasterData.html` 都返回 404。它可能属于那约 1150 个界面 action，**未核实**。

---

## 2. 所有 action 的通用规则（来自概念页，2026）

- 自动 action（命令行 action）先校验参数，然后调用**对应的 `HEServices` 类**。官方说明这样可以保证 action 与 `HEServices` 函数“执行完全相同的内部功能”。一个 action 提供对应类的全部功能或部分功能。— [AutomaticActions.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/AutomaticActions.html)
- 不传 `PROJECTNAME` 时使用当前选中的项目；从 Windows 命令行调用时，必须传 `PROJECTNAME` 或先执行 `ProjectAction`。布尔值用 `0`/`1`。**不能传空字符串**（如 `/PARAMETER:""`），不需要的参数直接省略。大多数方案参数在省略时使用“最近一次使用的方案”。参数名不区分大小写，参数值可能区分大小写。— 同上
- 每次调用都应新建 `ActionCallingContext`，因为 action 可能在执行中修改它。命令行参数中，`/Quiet` 表示不弹对话框，`/Auto` 表示执行后退出。— [CallingActions.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/CallingActions.html)、[Command line parameters.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/Command%20line%20parameters.html)
- 大多数 action 出错时抛出 `BaseException`，参数无效时抛出 `System.ArgumentException`。这一点在各页 Remarks 中写明。

> **对本项目的含义（推断）**：这些 action 只是 `HEServices` 类的命令行外壳。Bridge 在进程内运行，原则上可以直接调用 `HEServices` 类，从而拿到结构化的返回值，并自己控制事务和撤销。但两者在方法层面是否一一等价，本次**未核实**，见第 6 节。

---

## 3. 图例

**效果**（一行内可以组合，“?” 表示页面没有明说，属于推断）：
- **R**：只读，不改项目，不写文件。
- **P**：写入项目（项目数据、项目设置、项目消息）。
- **F**：在项目之外写文件（导出文件、归档、宏文件、新项目副本等）。
- **S**：修改设置或系统级配置，包括用户/工作站/公司设置、部件库、翻译库、主数据目录、插件/脚本注册、工作区、功能区。
- **G**：只影响界面，或需要交互（打开窗口或对话框、需要人在界面上操作）。

**类别**取自 `docs/eplan/api-coverage.md` 第 2 节：导出、导入、检查与消息、报表、编号、生成、项目管理、部件管理、翻译与语言、设置、3D/生产。另有两个补充类别：
- **受控白名单**：对应 api-coverage 第 2 节最后一行的 `eplan_action_run`。只允许已标定级别的 action，计划在 M4 实现。
- **API 替代**：action 本身不做成工具，同样的能力改用 DataModel、HEServices 或 EServices 类实现，并在表中写明里程碑。
- **不覆盖**：必须写明理由。

**里程碑**：`M4*` 表示依赖 Preplanning 授权。api-coverage 第 1 节记录 `DataModel.Planning` 在当前授权下不可用，状态为 ➖。“—”表示不计划实现。

---

## 4. 全表

一行对应一个 action；多用途 action 每个 TYPE 值占一行。“来源”列链接到 2026 版详细页面。

| # | action | TYPE / 模式 | 用途（一行） | 关键参数（原样） | 效果 | 类别 | 建议工具 / 处理 | 里程碑 | 来源 |
|---|---|---|---|---|---|---|---|---|---|
| 1 | `backup` | `PROJECT` | 把整个项目备份到归档文件 | PROJECTNAME, ARCHIVENAME, DESTINATIONPATH, COMMENT, AUTOCOPYREFDATA, INCLEXTDOCS, INCLIMAGES, BACKUPMETHOD（BACKUP / SOURCEOUT / ARCHIVE） | F；COMMENT 写入项目属性，AUTOCOPYREFDATA 把外部文件复制进项目目录，两者都属于 P | 项目管理 | `eplan_project_backup`，同时用作 Destructive 操作前的保护步骤。初期只允许 `BACKUPMETHOD:BACKUP`：文档没有说明 SOURCEOUT（filed off）和 ARCHIVE 对原项目有什么影响 | M2–M3 | [backup.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/backup.html) |
| 2 | `backup` | `MASTERDATA` | 备份主数据文件（符号、宏、表格等） | MDTYPE（SYMBOLS, MACROS, FORMS, ARTICLES, LANGUAGES, STANDARDSHEET, STATIONDATA）, SOURCEPATH, FILENAME, DESTINATIONPATH, ARCHIVENAME, COMMENT | F | 项目管理 | 低优先级：主数据是公司/工作站级资源 | M3（可选） | [backup.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/backup.html) |
| 3 | `changelayer` | — | 修改图层属性（可见、打印、字高、颜色、3D 透明度） | PROJECTNAME, LAYER[n], VISIBLE[n], PRINTED[n], TEXTHEIGHT[n], COLORID[n], TRANSPARENCY[n] | P | 受控白名单 | 放入 `eplan_action_run`，不单独做工具 | M4 | [changelayer.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/changelayer.html) |
| 4 | `check` | `PROJECT` | 检查项目 | PROJECTNAME, VERIFICATIONSCHEME, VERIFYCOMPLETEDONLY, USEPAGEFILTER | P?：产生检查消息，推断会写入项目的消息管理；不改设计数据（推断） | 检查与消息 | `eplan_check_run`；结果用 `PrjMessagesCollection` 读取（见第 5 节） | M2 | [check.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/check.html) |
| 5 | `check` | `PAGES` | 检查页面 | PROJECTNAME, VERIFICATIONSCHEME, PAGENAME, SELn, USEPAGEFILTER | P?（同上） | 检查与消息 | `eplan_check_run` | M2 | [check.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/check.html) |
| 6 | `check` | `ONLYCOMPLETED` | 检查项目，只看已完成的消息 | PROJECTNAME, VERIFICATIONSCHEME | P?（同上） | 检查与消息 | `eplan_check_run` 的一个选项 | M2 | [check.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/check.html) |
| 7 | `check` | `PARTS` | 检查部件（不需要 PROJECTNAME） | VERIFICATIONSCHEME, VERIFYCOMPLETEDONLY, PARTNUMBERn, PARTVARIANTn | P?/S?：消息写到哪里，文档没有说明 | 检查与消息 | `eplan_check_run` 的可选范围；也可以并入部件工具 | M2（可选）/ M4 | [check.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/check.html) |
| 8 | `check` | `INSTALLATIONSPACE` | 检查安装空间（3D） | PROJECTNAME, INSTALLATIONSPACENAME, INSTALLATIONSPACENAMEn, STRUCTURE, STRUCTUREn | P?（同上） | 3D/生产 | Pro Panel 工具家族中的检查 | M4b | [check.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/check.html) |
| 9 | `CleanWorkspaceAction` | — | 清理工作区 | Workspacename, Silent | S，G（需要主窗口） | 不覆盖 | 工作区是界面布局，与项目数据无关 | — | [CleanWorkspaceAction.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/CleanWorkspaceAction.html) |
| 10 | `compress` | — | 按配置方案压缩项目 | PROJECTNAME, CONFIGSCHEME | P：压缩会删除或改动哪些内容由方案决定，文档没有说明 | 项目管理 | `eplan_project_compress`，Destructive，执行前先备份 | M3 | [compress.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/compress.html) |
| 11 | `devicelist` | `IMPORT` | 导入设备清单 | PROJECTNAME, IMPORTFILE, FORMAT（XDLXmlExporter, XDLTxtImporterExporter, XDLCsvImporterExporter 或自定义） | P | 导入 | 任务型导入工具，Destructive | M4 | [devicelist.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/devicelist.html) |
| 12 | `devicelist` | `EXPORT` | 导出设备清单 | PROJECTNAME, EXPORTFILE, FORMAT | F | 导出 | `eplan_export_*`（设备清单） | M4 | [devicelist.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/devicelist.html) |
| 13 | `devicelist` | `DELETE` | 删除设备清单 | PROJECTNAME | P（删除） | 导入 | 并入设备清单导入工具，Destructive | M4 | [devicelist.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/devicelist.html) |
| 14 | `edit` | — | 在界面中打开项目、页面、设备所在页或布局空间，可以定位到坐标 | PROJECTNAME, PAGENAME, DEVICENAME, X, Y, INSTALLATIONSPACE, DEVICENAME3D, IGNOREWHENOPEN | G（打开或定位，不改数据） | 项目管理 | “打开项目”并入 `eplan_project_open`（更适合用 `ProjectOpen`）；“在界面中定位到某页或某设备”可以做成可选的展示工具 | M2–M3 | [edit.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/edit.html) |
| 15 | `EplApiModuleAction` | — | 注册或注销 API 插件 | register, registerModule, unregister, unregisterInternal | S | 不覆盖 | 只供安装器和 M1 内部使用：它改变 EPLAN 的插件配置，不应向代理开放 | — | [EplApiModuleAction.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/EplApiModuleAction.html) |
| 16 | `EsCorrectConnections` | — | 把连接定义点的图形属性合并到信号定义点，并删除空的连接定义点 | （无参数） | P（会删除对象，并新建名为 correction-n 的信号） | 生成 | 连接整理，Destructive；也可以放入白名单 | M4 | [EsCorrectConnections.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/EsCorrectConnections.html) |
| 17 | `ExecuteScript` | — | 运行脚本中带 `[Start]` 标记的函数 | ScriptFile, \<further parameter\> | 任意（执行代码） | 不覆盖 | 属于 Code 级，默认禁用（ADR-0005） | — | [ExecuteScript.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/ExecuteScript.html) |
| 18 | `export` | `PXFPROJECT` | 把项目导出为 EPJ（PXF） | PROJECTNAME, EXPORTFILE, EXPORTMASTERDATA, EXPORTCONNECTIONS | F | 导出 | `eplan_export_*`（PXF/EPJ） | M4 | [export.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/export.html) |
| 19 | `export` | `GRAPHICPROJECT` | 把整个项目导出为图片（TIF/GIF/PNG/JPG，参数中另有 BMP） | PROJECTNAME, DESTINATIONPATH, FORMAT, COLORDEPTH, IMAGEWIDTH, IMAGECOMPRESSION, BLACKWHITE, PAGENAME | F | 导出 | `eplan_export_*`（图片） | M4 | [export.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/export.html) |
| 20 | `export` | `GRAPHICPAGE` | 把页面导出为图片 | PROJECTNAME, PAGENAME, DESTINATIONPATH, FORMAT, COLORDEPTH, IMAGEWIDTH, IMAGECOMPRESSION, BLACKWHITE, USEPAGEFILTER | F | 导出 | `eplan_export_*`（图片）；可以提前用作页面预览（见第 5 节） | M4 | [export.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/export.html) |
| 21 | `export` | `DXFPROJECT` | 把项目导出为 DXF | PROJECTNAME, PAGENAME, DESTINATIONPATH, EXPORTSCHEME, TARGET, LANGUAGE | F；LANGUAGE 会“在导出前把项目翻译成该语言”，可能写入项目（P?） | 导出 | `eplan_export_*`（DXF/DWG） | M4 | [export.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/export.html) |
| 22 | `export` | `DWGPROJECT` | 把项目导出为 DWG | 同 DXFPROJECT | F（P?，同上） | 导出 | 同上 | M4 | [export.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/export.html) |
| 23 | `export` | `DXFPAGE` | 把页面导出为 DXF | PROJECTNAME, PAGENAME, PAGENAMEn, DESTINATIONPATH, EXPORTSCHEME, TARGET, LANGUAGE | F（P?，同上） | 导出 | 同上 | M4 | [export.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/export.html) |
| 24 | `export` | `DWGPAGE` | 把页面导出为 DWG | 同 DXFPAGE | F（P?，同上） | 导出 | 同上 | M4 | [export.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/export.html) |
| 25 | `export` | `PDFPAGESSCHEME` | 按方案把指定页面导出为 PDF | PROJECTNAME, EXPORTFILE, EXPORTSCHEME, PAGENAMEn 或 SELn, BLACKWHITE（0–3）, USEZOOM, ZOOMLEVEL, USESIMPLELINK, FASTWEBVIEW, READONLYEXPORT, USEPRINTMARGINS, LANGUAGE, EXPORTMODEL | F（P?，同上） | 导出 | `eplan_export_pdf` | M4 | [export.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/export.html) |
| 26 | `export` | `PDFPROJECTSCHEME` | 按方案把整个项目导出为 PDF | 同上，但没有 PAGENAMEn/SELn | F（P?，同上） | 导出 | `eplan_export_pdf` | M4 | [export.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/export.html) |
| 27 | `export` | `DXFDWGPROJECTSCHEME` | 按方案把项目导出为 DXF 或 DWG（格式也由方案决定） | PROJECTNAME, DESTINATIONPATH, TARGET | F | 导出 | `eplan_export_*`（DXF/DWG） | M4 | [export.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/export.html) |
| 28 | `export` | `DXFDWGPPAGESSCHEME`（原文拼写） | 按方案把页面导出为 DXF 或 DWG | PROJECTNAME, PAGENAMEn 或 SELn, DESTINATIONPATH, TARGET | F | 导出 | 同上 | M4 | [export.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/export.html) |
| 29 | `export3d` | `STEP` | 把安装空间导出为 STEP | PROJECTNAME, INSTALLATIONSPACENAME, INSTALLATIONSPACENAMEn, STRUCTURE, STRUCTUREn, DESTINATIONPATH, EXPORTSCHEME, SEPARATEFILES | F | 3D/生产 | Pro Panel 3D 导出 | M4b | [export3d.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/export3d.html) |
| 30 | `export3d` | `JT` | 把安装空间导出为 JT | 同上，FILENAME 代替 SEPARATEFILES | F | 3D/生产 | 同上 | M4b | [export3d.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/export3d.html) |
| 31 | `ExportNCData` | MachineType 1–8（1 NC Steinhauer, 2 Rittal - Perforex BC, 3 NC DXF, 4 Drilling template, 5 Rittal - Perforex LC, 6 Copper NC, 7 Copper DXF, 8 Pipe bending） | 为加工设备导出 NC 数据 | MachineType, MachineName, ProjectName, UseSelection, TargetDirectory, MaterialName | F | 3D/生产 | Pro Panel 生产输出 | M4b | [ExportNCData.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/ExportNCData.html) |
| 32 | `ExportProductionWiring` | MachineType（1 Komax, 2 CadCabel, 3 Schleuniger, 4 Steinhauer PWA, 5 Metzner, 7 WireList, 8 Averex, 9 Rittal - Wire Terminal WT） | 为布线设备导出生产布线数据 | MachineType, MachineName, ProjectName, UseSelection, TargetDirectory, FileName, Language, MultipleConnections | F；MultipleConnections 说明项目会记录“已导出”状态，因此可能写入项目（P?） | 3D/生产 | Pro Panel 生产输出 | M4b | [ExportProductionWiring.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/ExportProductionWiring.html) |
| 33 | `ExportSegmentsTemplate` | — | 把管段模板导出到文件（预规划） | PROJECTNAME, FILENAME, DESCRIPTION | F | 导出 | 预规划导出 | M4* | [ExportSegmentsTemplate.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/ExportSegmentsTemplate.html) |
| 34 | `exportToGraphics` | `GRAPHICPROJECT` | 按图片方案把项目导出为图片 | PROJECTNAME, EXPORTSCHEME, DESTINATIONPATH, FORMAT, COLORDEPTH, IMAGEWIDTH, IMAGECOMPRESSION, BLACKWHITE, USEPAGEFILTER | F | 导出 | `eplan_export_*`（图片），与 `export` 的 GRAPHIC* 二选一 | M4 | [exportToGraphics.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/exportToGraphics.html) |
| 35 | `exportToGraphics` | `GRAPHICPAGE` | 按图片方案把页面导出为图片 | 同上，另加 PAGENAME | F | 导出 | 同上；可以提前用作页面预览 | M4 | [exportToGraphics.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/exportToGraphics.html) |
| 36 | `gedRedraw` | — | 重绘图形编辑器 | （无参数） | G（文档写明只能交互使用） | 不覆盖 | 只能交互使用 | — | [gedRedraw.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/gedRedraw.html) |
| 37 | `generate` | `CONNECTIONS` | 生成或更新连接 | PROJECTNAME, REBUILDALLCONNECTIONS, PAGENAME, USEPAGEFILTER, PAGEFILTERNAME, PAGENAMEn, SELn | P | 生成 | 并入写工具，写入后可以更新连接 | M3 | [generate.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/generate.html) |
| 38 | `generate` | `CABLES` | 生成电缆 | PROJECTNAME, CREATIONSCHEME, NUMBERINGSCHEME, AUTOSELECTSCHEME, KEEPOLDNAMES, STARTVALUE, STEPVALUE, ONLYAUTOCABLES | P | 生成 | 并入电缆工具 | M4 | [generate.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/generate.html) |
| 39 | `generatemacros` | — | 从宏项目生成宏文件（项目类型必须是“宏项目”） | PROJECTNAME, PAGENAMEn, INSTALLATIONSPACENAMEn, STRUCTUREn, WINDOWMACRODIR, PAGEMACRODIR, FILTERSCHEME, OVERWRITE | F（写宏目录，属于主数据，S?） | 生成 | 宏工具；目标目录必须在 scratch 内 | M4 | [generatemacros.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/generatemacros.html) |
| 40 | `graphicallayertable` | `IMPORT` | 导入图层表 | PROJECTNAME, IMPORTFILE | P | 导入 | 低优先级 | M4 | [graphicallayertable.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/graphicallayertable.html) |
| 41 | `graphicallayertable` | `EXPORT` | 导出图层表 | PROJECTNAME, EXPORTFILE | F | 导出 | 低优先级 | M4 | [graphicallayertable.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/graphicallayertable.html) |
| 42 | `import` | `PXFPROJECT` | 导入 PXF（EPJ）项目 | IMPORTFILE, PROJECTNAME, BALANCEARTICLES, GENERATEAUTOMATICCABLES, VERIFY | P（生成或写入项目） | 导入 | 任务型导入工具，Destructive；目标项目必须在 scratch 内 | M4 | [import.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/import.html) |
| 43 | `import` | `DXFDWGFILES` | 把一个目录里的 DXF/DWG 直接导入为宏 | SOURCEPATH, DESTINATIONPATH, IMPORTSCHEME, MACROPROJECT, ONLYMACROPROJECT | F（生成宏项目和宏文件，S?） | 导入 | 同上 | M4 | [import.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/import.html) |
| 44 | `import` | `DXFPAGE` | 把 DXF 插入页面 | PROJECTNAME, PAGENAME, IMPORTFILE, IMPORTSCHEME, XSCALE, YSCALE, XOFFSET, YOFFSET | P | 导入 | 同上 | M4 | [import.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/import.html) |
| 45 | `import` | `DWGPAGE` | 把 DWG 插入页面 | 同 DXFPAGE | P | 导入 | 同上 | M4 | [import.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/import.html) |
| 46 | `import` | `PDFCOMMENTS` | 把 PDF 注释导入项目（此类型必须传 PROJECTNAME） | PROJECTNAME, IMPORTFILE | P | 导入 | 导入后可以读取审阅意见，适合 AI 处理审图意见 | M4 | [import.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/import.html) |
| 47 | `import3d` | `STEP` | 导入 STEP 格式的 3D 数据 | PROJECTNAME, IMPORTFILE, SCHEME | P?（导入到项目还是宏、部件，文档没有说明） | 3D/生产 | Pro Panel 导入，Destructive | M4b | [import3d.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/import3d.html) |
| 48 | `import3d` | `DESIGNSPACE` | 以 STEP 文件导入设计空间 | 同上 | P? | 3D/生产 | 同上 | M4b | [import3d.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/import3d.html) |
| 49 | `import3d` | `JT` | 导入 JT 格式的 3D 数据 | 同上 | P? | 3D/生产 | 同上 | M4b | [import3d.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/import3d.html) |
| 50 | `ImportPrePlanningData` | — | 从 Excel 或文本文件导入预规划数据 | PROJECTNAME, FILENAME, SCHEMENAME, TABLENAME, DELIMITER, HEADER, TARGETNAME, SKIPERRORS, OVERWRITE, UPDATEONLY | P | 导入 | 预规划导入，Destructive | M4* | [ImportPrePlanningData.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/ImportPrePlanningData.html) |
| 51 | `ImportSegmentsTemplate` | — | 从文件导入管段模板（预规划） | PROJECTNAME, FILENAME | P | 导入 | 预规划导入 | M4* | [ImportSegmentsTemplate.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/ImportSegmentsTemplate.html) |
| 52 | `InsertModelViewAction` | VIEWTYPE 0–4（Undefined, Cabinet, EMI, Unfolding, DrillView） | 在页面上插入模型视图 | PROJECTNAME, LAYOUTSPACE, STRUCTURE, PAGENAME, X, Y, DX, DY, VIEWNAME, DESCRIPTION, ANGLE, SELECTIONSCHEME, STYLE, ITEMLABELING, VIEWPOINT, ROOTELEMENTS, SCALESETTING, SCALE, VIEWTYPE, OBJECTID（输出） | P | 3D/生产 | Pro Panel 模型视图 | M4b | [InsertModelViewAction.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/InsertModelViewAction.html) |
| 53 | `label` | — | 按配置方案生成标签数据文件（txt / xls / xlsx / xml） | PROJECTNAME, CONFIGSCHEME, FILTERSCHEME, SORTSCHEME, LANGUAGE, DESTINATIONFILE, RECREPEAT, TASKREPEAT, SHOWOUTPUT, USESELECTION | F（输出 .xls 需要 Excel） | 导出 | `eplan_export_*`（标签） | M4 | [label.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/label.html) |
| 54 | `masterdata` | `UPDATEPROJECT` | 用系统主数据更新项目中的主数据 | PROJECTNAME | P（会覆盖项目内的表格、符号等，推断） | 项目管理 | Destructive，执行前先备份 | M4 | [masterdata.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/masterdata.html) |
| 55 | `MfExportRibbonBarAction` | — | 把功能区定制导出为 XML | FileName | F | 不覆盖 | 界面定制，与项目数据无关 | — | [MfExportRibbonBarAction.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/MfExportRibbonBarAction.html) |
| 56 | `MfImportRibbonBarAction` | — | 从 XML 导入功能区定制 | FileName | S | 不覆盖 | 界面定制 | — | [MfImportRibbonBarAction.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/MfImportRibbonBarAction.html) |
| 57 | `navigateToEEC` | — | 跳转到 EPLAN Engineering Configuration 中的对象 | EECOBJECTID | G | 不覆盖 | 界面导航，目标是另一个产品 | — | [navigateToEEC.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/navigateToEEC.html) |
| 58 | `OpenWorkspaceAction` | — | 打开工作区 | Workspacename, Silent | G/S（需要主窗口） | 不覆盖 | 界面布局 | — | [OpenWorkspaceAction.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/OpenWorkspaceAction.html) |
| 59 | `partslist` | `IMPORT` | 把部件清单导入项目 | PROJECTNAME, IMPORTFILE, FORMAT（XPalXmlExporter, XPalCSVConverter 或自定义）, FIELDASSIGNMENTSCHEME, MODE（0/1/2） | P | 导入 | 任务型导入工具，Destructive | M4 | [partslist.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/partslist.html) |
| 60 | `partslist` | `EXPORT` | 导出项目的部件清单 | PROJECTNAME, EXPORTFILE, FORMAT, FIELDASSIGNMENTSCHEME | F | 导出 | `eplan_export_*`（部件清单） | M4 | [partslist.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/partslist.html) |
| 61 | `partslist` | `IMPORTTOSYSTEM` | 导入到部件管理（系统部件库） | IMPORTFILE, FORMAT（默认 XPamExportXml）, MODE, ADDITIONAL_LANGUAGE | S（写部件库） | 部件管理 | `eplan_part_*`，Destructive；部件库必须是测试库 | M4 | [partslist.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/partslist.html) |
| 62 | `partslist` | `EXPORTFROMSYSTEM` | 从部件管理导出 | EXPORTFILE, FORMAT | F | 部件管理 | `eplan_part_*` | M4 | [partslist.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/partslist.html) |
| 63 | `partslist` | `DELETESTOREDPROPERTIES` | 从项目中删除存储的属性 | PROJECTNAME, CONFIGSCHEME | P（删除） | 部件管理 | Destructive | M4 | [partslist.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/partslist.html) |
| 64 | `partsmanagementapi` | `IMPORT` | 把部件及其他记录导入部件管理（支持 .edz） | IMPORTFILE, FORMAT（XPamImportXml, XPamExportXml, IXPartsImportExportEdz）, MODE（0–3）, ADDITIONAL_LANGUAGE, FILTERSCHEME | S（写部件库） | 部件管理 | `eplan_part_import`（EDZ 导入，对应 api-coverage 第 4 节的需求），Destructive | M4 | [partsmanagementapi.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/partsmanagementapi.html) |
| 65 | `partsmanagementapi` | `EXPORT` | 导出部件及其他记录 | EXPORTFILE, FORMAT, PARTNUMBER(n), MANUFACTURER(n), CONSTRUCTION(n), CONNECTIONPOINTPATTERN(n), ACCESSORYLIST(n), ACCESSORYPLACEMENT(n), FILTERSCHEME | F | 部件管理 | `eplan_part_export` | M4 | [partsmanagementapi.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/partsmanagementapi.html) |
| 66 | `partsmanagementapi` | `EXPORTPARTS` | 按属性条件导出部件 | EXPORTFILE, FORMAT, PROPERTYIDn, PROPERTYVALUEn | F | 部件管理 | 同上 | M4 | [partsmanagementapi.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/partsmanagementapi.html) |
| 67 | `partsmanagementapi` | `EXPORTMANUFACTURERS` | 按属性条件导出制造商 | 同上 | F | 部件管理 | 同上 | M4 | [partsmanagementapi.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/partsmanagementapi.html) |
| 68 | `partsmanagementapi` | `EXPORTCONSTRUCTIONS` | 按属性条件导出结构 | 同上 | F | 部件管理 | 同上 | M4 | [partsmanagementapi.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/partsmanagementapi.html) |
| 69 | `partsmanagementapi` | `EXPORTCONNECTIONPATTERNS` | 按属性条件导出连接点样式 | 同上 | F | 部件管理 | 同上 | M4 | [partsmanagementapi.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/partsmanagementapi.html) |
| 70 | `partsmanagementapi` | `EXPORTACCESSORYLISTS` | 按属性条件导出附件列表 | 同上 | F | 部件管理 | 同上 | M4 | [partsmanagementapi.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/partsmanagementapi.html) |
| 71 | `partsmanagementapi` | `EXPORTACCESSORYPLACEMENTS` | 按属性条件导出附件放置 | 同上 | F | 部件管理 | 同上 | M4 | [partsmanagementapi.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/partsmanagementapi.html) |
| 72 | `plcservice` | `BUSDATAEXPORT` | 用指定转换器导出 PLC 总线数据（AutomationML、TIA、TwinCAT 等） | PROJECTNAME, LANGUAGE, CONVERTERID（21 种，见原页）, CONFIGURATIONPROJECT, DESTINATIONFILE, OVERWRITE, FORMAT, EXPORTPORTSPECIFICINTERCONNECTION, EXPORTACCESSORIES, EXPORTDRIVES, EXPORTDEVICESPECIFICCONFIGURATIONVALUES | F | 导出 | `eplan_export_*`（PLC 数据） | M4 | [plcservice.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/plcservice.html) |
| 73 | `plcservice` | `BUSDATAIMPORT` | 导入 PLC 总线数据 | PROJECTNAME, LANGUAGE, CONVERTERID, SOURCEFILE, IMPORTMATCH（0/1/2；1 可能弹出比较对话框） | P | 导入 | 导入工具（PLC），Destructive；禁止 IMPORTMATCH:1 | M4 | [plcservice.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/plcservice.html) |
| 74 | `plcservice` | `GENERATEPLCSCHEMATIC` | 按配置文件生成 PLC 原理图页 | PROJECTNAME, CONFIGFILE, SINGLELINEPAGES, MULTILINEPAGES, OVERVIEWS, RACKOVERVIEWS | P | 生成 | 并入 PLC 写工具 | M4 | [plcservice.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/plcservice.html) |
| 75 | `plcservice` | `EXPORTADDRESSOVERVIEW` | 导出 PLC 地址总览 | PROJECTNAME, CONFIGURATIONPROJECT, PLCSTATION, PLCCPU, DESTINATIONFILE | F | 导出 | `eplan_export_*`（PLC 地址） | M4 | [plcservice.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/plcservice.html) |
| 76 | `preparemacros` | — | 为生成宏准备宏项目 | PROJECTNAME, PAGENAMEn, GroupMacroBoxes, GroupPageMacros, SetHandleActive | P | 生成 | 宏工具（与 generatemacros 配套） | M4 | [preparemacros.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/preparemacros.html) |
| 77 | `print` | `PROJECT` | 打印项目 | PROJECTNAME, PRINTERNAME, PRINTCOLLATE, PRINTREVERSE, NUMBER, DESTINATIONFILE, USEPAGEFILTER, PRINTCHANGEDPAGES | F/物理打印 | 不覆盖 | 会产生物理输出，而且工作站打印设置会覆盖 action 参数；PDF 需求由 `export` 满足 | — | [print.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/print.html) |
| 78 | `print` | `PAGES` | 打印页面 | 同上，另加 PAGENAME | F/物理打印 | 不覆盖 | 同上 | — | [print.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/print.html) |
| 79 | `ProjectAction` | — | 打开指定项目，执行另一个 action，然后关闭项目 | PROJECTNAME, Action, NOCLOSE, OpenMode（Standard, ReadOnly, Exclusive）, EnableDialogs | 取决于内层 action | 项目管理 | Bridge 内部机制，不单独暴露；`OpenMode:ReadOnly` 可以保证 M2 只读 | M2–M3 | [ProjectAction.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/ProjectAction.html) |
| 80 | `projectmanagement` | `READPROJECTINFO` | 从 XML 文件把项目信息读入项目 | PROJECTNAME, FILENAME | P | 项目管理 | 写项目信息，Destructive | M3 | [projectmanagement.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/projectmanagement.html) |
| 81 | `projectmanagement` | `PUBLISHSMARTPRODUCTION` | 把项目发布到 EPLAN Smart Production | PROJECTNAME, FILENAME | F，并对外发布 | 不覆盖 | 发布到外部服务，必须由人决定 | — | [projectmanagement.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/projectmanagement.html) |
| 82 | `projectmanagement` | `CREATESNAPSHOTCOPY` | 创建项目的快照副本 | PROJECTNAME, FILENAME（目标项目全路径）, SCHEME | F（新项目） | 项目管理 | ★ 可以用来为真实项目生成 scratch 副本，配合硬性规则 1；需要在 scratch 中验证 | M2–M3 | [projectmanagement.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/projectmanagement.html) |
| 83 | `projectmanagement` | `EXPORTPROPERTYPLACEMENTSSCHEMAS` | 导出属性放置方案 | PROJECTNAME, FILENAME | F（对项目只读） | 设置 | 只读导出到临时目录 | M2（可选） | [projectmanagement.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/projectmanagement.html) |
| 84 | `projectmanagement` | `IMPORTPROPERTYPLACEMENTSSCHEMAS` | 导入属性放置方案 | PROJECTNAME, FILENAME, OVERWRITE | P | 设置 | 写入属于 Code 级，默认禁用 | — | [projectmanagement.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/projectmanagement.html) |
| 85 | `projectmanagement` | `REORGANIZE` | 重组项目 | PROJECTNAME, EXTENDEDMODE | P | 项目管理 | 项目维护，执行前先备份 | M3 | [projectmanagement.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/projectmanagement.html) |
| 86 | `projectmanagement` | `CORRECTPROJECTITEMS` | 修正项目条目 | PROJECTNAME, SCHEME | P | 项目管理 | Destructive，执行前先备份 | M3 | [projectmanagement.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/projectmanagement.html) |
| 87 | `projectmanagement` | `LOADDIRECTORY` | 扫描目录，把找到的项目加入项目管理 | PROJECTSDIRECTORY, SCANSUBDIRECTORIES | S（工作站级项目管理数据库） | 不覆盖 | 改变工作站级的项目登记，代理不需要 | — | [projectmanagement.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/projectmanagement.html) |
| 88 | `ProjectOpen` | — | 打开项目 | Project, OpenMode（Standard, ReadOnly, Exclusive） | G（打开项目，不改数据）；需要升级的项目以 ReadOnly 打开 | 项目管理 | `eplan_project_open`；M2 读取时用 `OpenMode:ReadOnly` | M2 | [ProjectOpen.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/ProjectOpen.html) |
| 89 | `RegisterCustomPropertyEditorAction` | — | 为某个属性注册或注销自定义编辑对话框 | PropertyId, PropertyIndex, PropertyIdentName, Action, Editable, Register | S（界面扩展） | 不覆盖 | 界面扩展 | — | [RegisterCustomPropertyEditorAction.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/RegisterCustomPropertyEditorAction.html) |
| 90 | `RegisterScript` | — | 注册脚本（.cs 或 .vb） | ScriptFile, RegisterAgain, ShowDecider | S（加载代码） | 不覆盖 | 属于 Code 级，默认禁用（ADR-0005） | — | [RegisterScript.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/RegisterScript.html) |
| 91 | `renumber` | `DEVICES` | 设备重编号 | PROJECTNAME, USESELECTION, FILTERSCHEME, CONFIGSCHEME, STARTVALUE, STEPVALUE, POSTNUMERATE, ALSONUMERATEDBYPLC, IDENTIFIER, NUMERATECABLES | P | 编号 | `eplan_renumber`，Destructive，执行前先备份 | M4 | [renumber.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/renumber.html) |
| 92 | `renumber` | `PAGES` | 页面重编号 | PROJECTNAME, USESELECTION, STARTVALUE, STEPVALUE, STRUCTUREORIENTED, KEEPINTERVAL, KEEPTEXT, SUBPAGES（0/1/2） | P | 编号 | 同上 | M4 | [renumber.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/renumber.html) |
| 93 | `renumber` | `TERMINALS` | 端子重编号 | PROJECTNAME, USESELECTION, CONFIGSCHEME, STARTVALUE, STEPVALUE, POSTNUMERATE, ALSONUMERATEDBYPLC, PERMITSORTCHANGE, FILLGAPS, PREFIX, SUFFIX, SEQUENCE, EXTENT, POTENTIAL_N, POTENTIAL_PE, POTENTIAL_SH, MULTIPLETERMINALS, KEEPALPHA | P | 编号 | 同上 | M4 | [renumber.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/renumber.html) |
| 94 | `renumber` | `CABLES` | 电缆重编号 | PROJECTNAME, USESELECTION, CONFIGSCHEME, STARTVALUE, STEPVALUE, KEEPEXISTING, KEEPIDENTIFIER | P | 编号 | 同上 | M4 | [renumber.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/renumber.html) |
| 95 | `renumber` | `CONNECTIONS` | 连接重编号（线号） | PROJECTNAME, USESELECTION, CONFIGSCHEME, STARTVALUE_n, STEPVALUE_n, GROUP_n, OVERWRITE, AVOIDIDENTICALDESIGNATIONS, VISIBILITY, MARKASMANUAL | P | 编号 | 同上；对应 api-coverage 第 4 节的“线号管理器”需求 | M4 | [renumber.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/renumber.html) |
| 96 | `reports` | `PROJECT` | 生成项目报表（更新全部评估） | PROJECTNAME | P | 报表 | `eplan_report_update` | M4 | [reports.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/reports.html) |
| 97 | `reports` | `PAGES` | 只更新给定页面所属的报表 | PROJECTNAME, PAGENAME, USEPAGEFILTER, PAGEFILTERNAME, PAGENAMEn, SELn | P | 报表 | `eplan_report_update` | M4 | [reports.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/reports.html) |
| 98 | `reports` | `UPDATEMODELVIEWPAGES` | 更新给定页面上的模型视图 | 同 PAGES | P | 3D/生产 | Pro Panel 报表 | M4b | [reports.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/reports.html) |
| 99 | `reports` | `CREATEMODELVIEWS` | 创建模型视图 | PROJECTNAME, TEMPLATEn, REPLACEEXISTING | P | 3D/生产 | 同上 | M4b | [reports.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/reports.html) |
| 100 | `reports` | `CREATECOPPERUNFOLDS` | 创建铜排展开图 | PROJECTNAME, TEMPLATEn, REPLACEEXISTING | P | 3D/生产 | 同上 | M4b | [reports.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/reports.html) |
| 101 | `reports` | `CREATEDRILLINGVIEWS` | 创建钻孔视图 | PROJECTNAME, TEMPLATEn, REPLACEEXISTING | P | 3D/生产 | 同上 | M4b | [reports.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/reports.html) |
| 102 | `restore` | `PROJECT` | 从归档还原项目，还原后自动升级到当前版本 | PROJECTNAME, ARCHIVENAME, UNPACKPROJECT, MODE | F（新项目；目标已存在时会怎样，文档没有说明） | 项目管理 | `eplan_project_restore`；★ 把 .zw1 还原到 scratch 目录，是生成 scratch 项目的标准途径 | M2–M3 | [restore.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/restore.html) |
| 103 | `restore` | `MASTERDATA` | 还原主数据 | ARCHIVENAME, DESTINATIONPATH | S（写主数据目录） | 不覆盖 | 会写入系统主数据，超出 scratch 范围 | — | [restore.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/restore.html) |
| 104 | `SaveWorkspaceAction` | — | 保存工作区 | Workspacename, Silent | S（需要主窗口） | 不覆盖 | 界面布局 | — | [SaveWorkspaceAction.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/SaveWorkspaceAction.html) |
| 105 | `search` | `DEVICETAG` | 按设备标识搜索 | PROJECTNAME, SEARCHITEM, CASESENSITIVE, WHOLETEXT, LOGICPAGES, GRAPHICPAGES, EVALUATIONPAGES, NOTPLACEDFUNCTIONS, FILTERSCHEME, SEARCHDB（已弃用） | R?：结果写进界面的结果列表，**不返回给调用方**；未给出的选项沿用界面当前值 | API 替代 | 用 `HEServices.Search` 加 `GetAllSearchDBEntries`，或 `DMObjectsFinder` | M2 | [search.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/search.html) |
| 106 | `search` | `ALLPROPERTIES` | 在所有属性中搜索 | 同上 | R?（同上） | API 替代 | 同上 | M2 | [search.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/search.html) |
| 107 | `search` | `TEXTS` | 搜索文本 | 同上 | R?（同上） | API 替代 | 同上 | M2 | [search.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/search.html) |
| 108 | `search` | `PAGEDATA` | 在页面数据中搜索 | 同上 | R?（同上） | API 替代 | 同上 | M2 | [search.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/search.html) |
| 109 | `search` | `PROJECTDATA` | 在项目数据中搜索 | 同上 | R?（同上） | API 替代 | 同上 | M2 | [search.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/search.html) |
| 110 | `selectionset` | `PROJECT` | 在 CallingContext 中返回当前项目 | （输出参数 PROJECT） | R | API 替代 | 用 `HEServices.SelectionSet`，它返回对象而不是字符串 | M2 | [selectionset.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/selectionset.html) |
| 111 | `selectionset` | `PROJECTS` | 返回选中的项目（以 ; 分隔） | （输出 PROJECTS） | R | API 替代 | 同上 | M2 | [selectionset.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/selectionset.html) |
| 112 | `selectionset` | `PAGES` | 返回选中的页面（以 ; 分隔） | （输出 PAGES） | R | API 替代 | 同上 | M2 | [selectionset.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/selectionset.html) |
| 113 | `selectionset` | `LAYOUTSPACES` | 返回选中的布局空间（以 ; 分隔） | （输出 LAYOUTSPACES） | R | API 替代 | 同上（3D 读取） | M2 | [selectionset.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/selectionset.html) |
| 114 | `SetProjectLanguage` | — | 设置项目语言（显示语言、可变语言、源语言），不翻译 | PROJECTNAME, DISPLAY, VARIABLE, SOURCE | P | 翻译与语言 | `eplan_translate_*`（设置语言） | M4 | [SetProjectLanguage.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/SetProjectLanguage.html) |
| 115 | `subprojects` | `FILEOFF` | 导出子项目（项目必须以独占方式打开，执行后源项目对象失效） | PROJECTNAME, DESTINATIONPATH, SPNR, EXTENDONLY | P+F | 项目管理 | 低优先级，Destructive | M4 | [subprojects.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/subprojects.html) |
| 116 | `subprojects` | `STORE` | 导入（回存）子项目 | PROJECTNAME, SPNR, SUBPROJECTDIR | P | 项目管理 | 低优先级，Destructive | M4 | [subprojects.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/subprojects.html) |
| 117 | `SwitchProjectType` | — | 切换项目类型（1 原理图项目，2 宏项目） | PROJECTNAME, PROJECTTYPE | P | 项目管理 | 宏流程的配套步骤 | M4 | [SwitchProjectType.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/SwitchProjectType.html) |
| 118 | `synchronize` | `MULTILINE` | 同步多线数据 | PROJECTNAME | P | 生成 | 并入写工具 | M4 | [synchronize.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/synchronize.html) |
| 119 | `synchronize` | `SINGLELINE` | 同步单线数据 | PROJECTNAME | P | 生成 | 同上 | M4 | [synchronize.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/synchronize.html) |
| 120 | `synchronize` | `OVERVIEW` | 同步总览数据 | PROJECTNAME | P | 生成 | 同上 | M4 | [synchronize.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/synchronize.html) |
| 121 | `synchronize` | `SYSTEMPARTSTOPROJECT` | 把系统部件加入项目 | PROJECTNAME, STOREMODE（0/1/2） | P | 部件管理 | `eplan_part_sync` | M4 | [synchronize.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/synchronize.html) |
| 122 | `synchronize` | `PARTSTOSYSTEM` | 把项目部件加入系统部件库 | PROJECTNAME | S（写部件库） | 部件管理 | `eplan_part_sync`，Destructive；部件库必须是测试库 | M4 | [synchronize.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/synchronize.html) |
| 123 | `Topology` | `RouteConnections` | 布线拓扑连接（需要 Cabling 授权选项） | PROJECTNAME | P | 3D/生产 | 拓扑工具（api-coverage 把 Topology 放在 M4b） | M4b | [Topology.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/Topology.html) |
| 124 | `Topology` | `CreateFunctions` | 生成连接到布线支点的拓扑功能 | PROJECTNAME | P | 3D/生产 | 同上 | M4b | [Topology.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/Topology.html) |
| 125 | `translate` | `TRANSLATEPROJECT` | 翻译整个项目 | PROJECTNAME, LANGUAGE | P | 翻译与语言 | `eplan_translate_*` | M4 | [translate.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/translate.html) |
| 126 | `translate` | `TRANSLATEPAGES` | 翻译指定页面 | PROJECTNAME, LANGUAGE, USEPAGEFILTER, PAGFILTERNAME（原文拼写）, PAGENAME, PAGENAMEn, SELn | P | 翻译与语言 | 同上 | M4 | [translate.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/translate.html) |
| 127 | `translate` | `REMOVELANGUAGE` | 从项目中删除语言（主语言不能删除） | PROJECTNAME, LANGUAGE（多个用逗号分隔） | P（删除） | 翻译与语言 | 同上，Destructive | M4 | [translate.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/translate.html) |
| 128 | `translate` | `EXPORTMISSINGTRANSLATIONS` | 导出缺失词表 | PROJECTNAME, LANGUAGE, EXPORTFILE, CONVERTER | F（对项目只读） | 翻译与语言 | 同上（只读导出，可以较早实现） | M4 | [translate.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/translate.html) |
| 129 | `translate` | `IMPORTTOTRANSDB` | 把文本导入翻译数据库 | IMPORTFILE, CONVERTER, LANGUAGE | S（写翻译库） | 翻译与语言 | 写系统翻译库，Destructive；只能用测试库 | M4 | [translate.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/translate.html) |
| 130 | `translate` | `EXPORTFROMTRANSDB` | 从翻译数据库导出文本 | EXPORTFILE, CONVERTER, LANGUAGE | F | 翻译与语言 | 同上 | M4 | [translate.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/translate.html) |
| 131 | `translate` | `CORRECT` | 修正翻译 | PROJECTNAME, CONVERTTRANSLATEDTEXTS | P | 翻译与语言 | 同上 | M4 | [translate.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/translate.html) |
| 132 | `UnregisterScript` | — | 注销脚本 | ScriptFile | S | 不覆盖 | 属于 Code 级，默认禁用 | — | [UnregisterScript.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/UnregisterScript.html) |
| 133 | `UpdateSegmentsFilling` | — | 计算并写入所有布线段的 CABLINGSEGMENT_FILLING（填充率） | PROJECTNAME | P | 3D/生产 | 布线工具 | M4b | [UpdateSegmentsFilling.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/UpdateSegmentsFilling.html) |
| 134 | `XAfActionSetting` | — | 设置一个设置项的值（不含项目设置） | set, index, value | S | 设置 | 写入属于 Code 级，默认禁用。读取用 `Settings` 类（见第 5 节） | — | [XAfActionSetting.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/XAfActionSetting.html) |
| 135 | `XAfActionSettingProject` | — | 设置一个项目设置的值 | Project, set, index, value | P（项目设置） | 设置 | 同上；读取用 `ProjectSettings` 类 | — | [XAfActionSettingProject.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/XAfActionSettingProject.html) |
| 136 | `XAMlExportProductionData2RASCenterAction` | — | 把安装空间导出为 AutomationML，供 Rittal RiPanel Processing Center 使用 | ProjectPath, FileName, DatabaseId, WholeProject, ConfigScheme | F；FileName 为空或目标文件已存在时会弹对话框（G） | 3D/生产 | Pro Panel 生产输出；必须传入不存在的 FileName | M4b | [XAMlExportProductionData2RASCenterAction.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/XAMlExportProductionData2RASCenterAction.html) |
| 137 | `XAMlExportProductionData2SmartMountingAction` | — | 只出现在总表中，说明与上一行相同 | **未知（详细页面 404）** | 未知 | 3D/生产 | 待核实，见第 7 节 | M4b | [API Actions.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/API%20Actions.html)（链接目标 XAMlExportProductionData2SmartMountingAction.html 返回 404） |
| 138 | `XCabCalculateEnclosureTotalWeightAction` | “All cabinets” 模式和 “One cabinet” 模式 | 计算机柜总重 | ProjectName, DatabaseId, CabinetTotalWeight（输出） | All cabinets：P（写 #36108 FUNCTION3D_CABINET_TOTALWEIGHT）；One cabinet：R（结果写入 context，经 StorableObjectContext 传入机柜） | 3D/生产 | Pro Panel；One cabinet 模式可以作为只读查询 | M4b | [XCabCalculateEnclosureTotalWeightAction.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/XCabCalculateEnclosureTotalWeightAction.html) |
| 139 | `XCCreateGravingtextAction` | — | 用电缆源和目标的设备标识生成刻字文本（VASS 规则） | Complete | P（作用范围是选中对象还是全项目，文档没有说明） | 生成 | 并入电缆工具，或放入白名单 | M4 | [XCCreateGravingtextAction.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/XCCreateGravingtextAction.html) |
| 140 | `XCMRemoveUnnecessaryNDPsAction` | — | 删除当前项目中多余的网络定义点 | Quiet | P（删除，并可能新建连接定义点） | 生成 | 连接整理，Destructive | M4 | [XCMRemoveUnnecessaryNDPsAction.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/XCMRemoveUnnecessaryNDPsAction.html) |
| 141 | `XCMUniteNetDefinitionPointsAction` | — | 合并同一网络上的网络定义点 | （无参数） | P | 生成 | 连接整理，Destructive | M4 | [XCMUniteNetDefinitionPointsAction.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/XCMUniteNetDefinitionPointsAction.html) |
| 142 | `XDLInsertDeviceAction` | — | 启动插入设备的交互 | PartNr, PartVariant, ProjectId, PropertyIndex | G（文档写明只能交互使用） | 不覆盖 | 需要人在界面上操作；M3 用 DataModel 插入设备 | — | [XDLInsertDeviceAction.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/XDLInsertDeviceAction.html) |
| 143 | `XEGActionInsertSymRef` | — | 查找并插入符号引用 | SymbolLibName, SymbolId, VariantId, FctDefTag, Placementmode, SymbolType, CustomSymbols | G（只能交互使用） | 不覆盖 | 需要人在界面上操作 | — | [XEGActionInsertSymRef.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/XEGActionInsertSymRef.html) |
| 144 | `XEsGetPagePropertyAction` | — | 读取**第一个选中页面**的一个属性 | PropertyId, PropertyIdentName, PropertyIndex, PropertyValue（输出） | R（依赖界面选择） | API 替代 | 用 DataModel 读取页面属性 | M2 | [XEsGetPagePropertyAction.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/XEsGetPagePropertyAction.html) |
| 145 | `XEsGetProjectPropertyAction` | — | 读取**当前项目**的一个属性 | 同上 | R（依赖当前项目） | API 替代 | 用 DataModel 读取项目属性 | M2 | [XEsGetProjectPropertyAction.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/XEsGetProjectPropertyAction.html) |
| 146 | `XEsGetPropertyAction` | — | 读取**选中对象**的一个属性 | 同上 | R（依赖界面选择） | API 替代 | 用 DataModel 加 SelectionSet | M2 | [XEsGetPropertyAction.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/XEsGetPropertyAction.html) |
| 147 | `XEsSetPagePropertyAction` | — | 设置**选中页面**的一个属性 | PropertyId, PropertyIdentName, PropertyIndex, PropertyValue | P | API 替代 | M3 属性写工具，用 DataModel 实现：目标明确，可以控制事务和撤销 | M3 | [XEsSetPagePropertyAction.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/XEsSetPagePropertyAction.html) |
| 148 | `XEsSetProjectPropertyAction` | — | 设置**当前项目**的一个属性 | 同上 | P；属性不存在时也返回 true，错误要从 SysMessagesCollection 读取 | API 替代 | 同上 | M3 | [XEsSetProjectPropertyAction.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/XEsSetProjectPropertyAction.html) |
| 149 | `XEsSetPropertyAction` | — | 设置**选中对象**的一个属性 | 同上 | P | API 替代 | 同上 | M3 | [XEsSetPropertyAction.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/XEsSetPropertyAction.html) |
| 150 | `XEsUserPropertiesExportAction` | — | 把用户自定义属性（定义）导出到 XML | XMLFile, Project | F（对项目只读） | 设置 | 只读导出到临时目录 | M2（可选） | [XEsUserPropertiesExportAction.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/XEsUserPropertiesExportAction.html) |
| 151 | `XEsUserPropertiesImportAction` | — | 把用户自定义属性从 XML 导入项目 | XMLFile, Project, Overwrite（缺少参数时弹对话框） | P | 设置 | 写入默认禁用 | — | [XEsUserPropertiesImportAction.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/XEsUserPropertiesImportAction.html) |
| 152 | `XGedClosePage` | — | 关闭所有选中的页面 | （无参数） | G | 不覆盖 | 界面操作 | — | [XGedClosePage.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/XGedClosePage.html) |
| 153 | `XGedStartInteractionAction` | — | 启动图形编辑器交互 | Name（可以附带交互参数） | G（只能交互使用） | 不覆盖 | 需要人在界面上操作 | — | [XGedStartInteractionAction.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/XGedStartInteractionAction.html) |
| 154 | `XGedUpdateMacroAction` | — | 更新宏（选中对象、指定项目，或经 context 传入的对象） | ProjectName, SchemeName, AutoAssignLastUsedRecord, NewVariant | P | 生成 | 宏工具，Destructive | M4 | [XGedUpdateMacroAction.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/XGedUpdateMacroAction.html) |
| 155 | `XMActionDCCommonExport` | EXECUTIONMODE 0/1/2（导出 / 导出并编辑 / 编辑并返回） | 导出项目数据供外部编辑（txt / xlsx / xml） | DATABASEID, PROJECTNAME, COMPLETEPROJECT, CONFIGSCHEME（不传时弹对话框）, LANGUAGE, DESTINATION, EXECUTIONMODE, IMMEDIATEIMPORT | 模式 0：F。模式 1/2：G，并且会导回项目（P） | 导出 | `eplan_export_*`（项目数据表），只允许 EXECUTIONMODE:0 | M4 | [XMActionDCCommonExport.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/XMActionDCCommonExport.html) |
| 156 | `XMActionDCImport` | — | 把数据配置文件（.edc）导入项目，用来修改功能的属性 | ProjectLink, DataConfigurationFile, ProgressTitle | P | 导入 | 批量属性导入，Destructive；与 M3 的 DataModel 属性工具二选一 | M4 | [XMActionDCImport.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/XMActionDCImport.html) |
| 157 | `XMDeleteReprTypeAction` | — | 从宏文件中删除某个表达类型 | RepresentationType（0–13）, Source, Destination | F（改宏文件）+ G（只能交互使用） | 不覆盖 | 只能交互使用，而且改的是主数据 | — | [XMDeleteReprTypeAction.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/XMDeleteReprTypeAction.html) |
| 158 | `XMExportConnectionsAction` | — | 导出连接（供外部编辑） | DatabaseId, ProjectName, CompleteProject, ConfigScheme, Language, Destination, ExecutionMode, ImmediateImport, IncludeGraphicalConnections | F | 不覆盖 | 仅为向后兼容保留，官方建议改用 `XMActionDCCommonExport` | — | [XMExportConnectionsAction.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/XMExportConnectionsAction.html) |
| 159 | `XMExportDCArticleDataAction` | EXECUTIONMODE 0/1/2 | 导出部件库数据供外部编辑 | COMPLETEPROJECT, CONFIGSCHEME, LANGUAGE, DESTINATION, EXECUTIONMODE, IMMEDIATEIMPORT | F（模式 1/2 为 G，并会导回部件库：S） | 部件管理 | `eplan_part_export`，只允许 EXECUTIONMODE:0 | M4 | [XMExportDCArticleDataAction.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/XMExportDCArticleDataAction.html) |
| 160 | `XMExportFunctionAction` | — | 导出功能 | DatabaseId, ProjectName, CompleteProject, ConfigScheme, Language, Destination, ExecutionMode, ImmediateImport | F | 不覆盖 | 向后兼容，被 XMActionDCCommonExport 取代 | — | [XMExportFunctionAction.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/XMExportFunctionAction.html) |
| 161 | `XMExportLocationBoxesAction` | — | 导出位置盒 | 同上 | F | 不覆盖 | 同上 | — | [XMExportLocationBoxesAction.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/XMExportLocationBoxesAction.html) |
| 162 | `XMExportPagesAction` | — | 导出页面 | 同上 | F | 不覆盖 | 同上 | — | [XMExportPagesAction.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/XMExportPagesAction.html) |
| 163 | `XMExportPipeLineDefsAction` | — | 导出管道定义 | 同上 | F | 不覆盖 | 同上 | — | [XMExportPipeLineDefsAction.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/XMExportPipeLineDefsAction.html) |
| 164 | `XMExportPotentialDefsAction` | — | 导出电位定义 | 同上 | F | 不覆盖 | 同上 | — | [XMExportPotentialDefsAction.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/XMExportPotentialDefsAction.html) |
| 165 | `XMImportDCArticleDataAction` | IMPORTMODE 0/1/2 | 把数据配置文件导入部件库 | DATACONFIGURATIONFILE, PROGRESSTITLE, SHOWIMPORTMESSAGES, IMPORTMODE（不传时会询问）, IDENTIFYBYNAMEINSTEADOFID | S（写部件库） | 部件管理 | `eplan_part_import`（表格导入），Destructive，必须传 IMPORTMODE | M4 | [XMImportDCArticleDataAction.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/XMImportDCArticleDataAction.html) |
| 166 | `XPamConvertPartDatabaseToArticleDatabaseAction` | — | 把 V2.9 部件库转换为 V2022 部件库 | OldDatabase, NewDatabase（不传参数时弹对话框） | S/F（生成新数据库） | 不覆盖 | 一次性的部件库迁移，属于管理员操作 | — | [XPamConvertPartDatabaseToArticleDatabaseAction.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/XPamConvertPartDatabaseToArticleDatabaseAction.html) |
| 167 | `XPamsDeviceSelectionAction` | Mode：selectDevice / updateDevice | 为项目、功能或连接重新选型，或更新设备数据 | ProjectName, Mode, KeepSwappedConnPointInformation | P（selectDevice 会删除并重新分配部件引用） | 部件管理 | `eplan_part_*`（更新设备数据），Destructive | M4 | [XPamsDeviceSelectionAction.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/XPamsDeviceSelectionAction.html) |
| 168 | `XPamSelectPart` | — | 打开部件选择（对话框），在 context 中返回选中的部件 | DatabaseId, SingleSelection；输出 SelectedPartsCount, PartNr\<nr\>, Variant\<nr\>, PartType\<nr\> | G（需要人在对话框中选择）；数据只读 | 不覆盖 | 需要人操作；读取部件用 MasterData API | — | [XPamSelectPart.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/XPamSelectPart.html) |
| 169 | `XPartsSetDataSourceAction` | DataSourceType 0/1/3（EPLAN 数据库 / SQL Server / eSTOCK） | 切换部件管理使用的数据源 | DataSourceType, DataBaseFileName, SqlServer, SqlCatalog, SqlUserName, SqlPassword, SqlLogin, SqlFullName, CollectionName, CollectionId, Force | S（含凭据参数） | 设置 | 写入默认禁用；**带密码参数，永远不能由代理传入** | — | [XPartsSetDataSourceAction.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/XPartsSetDataSourceAction.html) |
| 170 | `XPlaUpdateDetailAction` | — | 按选中的规划对象更新详细设计（预规划） | PROJECTNAME, UpdateMacros, UpdateIdentifier, UpdatePlaceholder, UpdatePipedata, UpdateParts | P | 生成 | 预规划到详细设计 | M4* | [XPlaUpdateDetailAction.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/XPlaUpdateDetailAction.html) |
| 171 | `XPrjActionUpgradeProjects` | — | 把一个或多个项目升级到当前数据库版本 | Project, Folder, Archive, BaseProject, UpgradeWriteProtectedProjects, UpgradeXMLProjects, FileTypes, PackOriginalProject, UpdateConnections, NoBackup, IgnoreUpgradeBackups, DoDataModelUpgrades, FileDescription | P+F（默认先备份，原项目打包成 7zip） | 项目管理 | `eplan_project_upgrade`；禁止 `NoBackup:true`，也禁止对 scratch 之外的 Folder 做批量升级 | M3 | [XPrjActionUpgradeProjects.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/XPrjActionUpgradeProjects.html) |
| 172 | `XPrjConvertBaseProjectsAction` | — | 把旧的基本项目（.ept/.epb）转换为 .zw9 | ProjectTemplate, Folder, FileTypes | F/S（写主数据模板目录） | 不覆盖 | 一次性迁移，属于管理员操作 | — | [XPrjConvertBaseProjectsAction.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/XPrjConvertBaseProjectsAction.html) |
| 173 | `XSDPreviewAction` | — | 打开或关闭页面、宏的预览窗口 | PROJECTNAME, PAGENAME, MACRONAME, SHOW | G | 不覆盖 | 只是界面预览窗口 | — | [XSDPreviewAction.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/XSDPreviewAction.html) |
| 174 | `XSettingsExport` | — | 把设置节点（USER / STATION / COMPANY / 项目）导出为 XML | node, XMLFile, prj（项目必须已打开） | F（对设置只读） | 设置 | 只读导出到临时目录，用于批量快照；读取个别设置用 Settings API | M2（可选） | [XSettingsExport.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/XSettingsExport.html) |
| 175 | `XSettingsImport` | — | 从 XML 导入项目、工作站、公司或用户设置 | XmlFile（为空时弹对话框）, Node, Project | S/P | 设置 | 写入属于 Code 级，默认禁用 | — | [XSettingsImport.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/XSettingsImport.html) |
| 176 | `XSettingsRegisterAction` | — | 注册 Add-on | path, installFile | S | 不覆盖 | 只供安装器使用 | — | [XSettingsRegisterAction.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/XSettingsRegisterAction.html) |
| 177 | `XSettingsUnregisterAction` | — | 注销 Add-on | path, installFile | S | 不覆盖 | 只供安装器使用 | — | [XSettingsUnregisterAction.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/XSettingsUnregisterAction.html) |

### 4.1 附表：只在 2025 版中出现的 action（2026 已无页面）

| # | action | 用途 | 关键参数 | 效果 | 处理 | 来源（2025） |
|---|---|---|---|---|---|---|
| A1 | `MfToggleMainMenuAction` | 切换经典菜单是否可见 | （无） | G | 不覆盖（界面操作；2026 已从文档删除） | [2025/MfToggleMainMenuAction.html](https://www.eplan.help/en-us/Infoportal/Content/api/2025/MfToggleMainMenuAction.html) |
| A2 | `XPamArticlesSyncAction` | 把 V2022 部件库同步到 V2.9 SQL Server 部件库 | srcdb, destdb, opensrcdbshared, opendestdbshared, full（=1 时会删除目标库中多出的部件） | S | 不覆盖（旧版部件库同步；2026 已从文档删除） | [2025/XPamArticlesSyncAction.html](https://www.eplan.help/en-us/Infoportal/Content/api/2025/XPamArticlesSyncAction.html) |

---

## 5. M2 只读操作专题：用 action 还是用 DataModel 或 API

结论：**M2 的只读能力几乎都应该用 DataModel、HEServices 或 EServices 类实现，而不是调用 action。** 原因有四点，都有文档依据：
- 读属性的 action 依赖“当前项目”或“当前选择”。
- `search` 不把结果返回给调用方。
- 读取消息和设置没有对应的 action。
- 只有类能返回结构化对象。

action 只在三种情况下更合适：只读打开项目、页面转图片，以及导出 XML 快照。

| 只读需求 | 文档中的 action | 推荐做法 | 理由（依据） |
|---|---|---|---|
| 当前项目、当前选择 | `selectionset`（PROJECT / PROJECTS / PAGES / LAYOUTSPACES） | **API**：`HEServices.SelectionSet`（`GetCurrentProject`、`GetSelectedPages`、`Selection`） | 类返回 `StorableObject[]`，action 只返回用 `;` 拼接的字符串（[HE_Selectionset.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/HE_Selectionset.html)、[selectionset.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/selectionset.html)）。注意：api-notes 记录了在 action 之外调用 `GetCurrentProject` 需要 `LockingStep` |
| 读取项目、页面、对象属性 | `XEsGetProjectPropertyAction`、`XEsGetPagePropertyAction`、`XEsGetPropertyAction` | **DataModel** | 这三个 action 只能读“当前项目”“第一个选中页面”“选中对象”，每次调用只读一个属性。DataModel 可以按名称定位对象，并批量读取（[NavigatingTheDatamodel.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/NavigatingTheDatamodel.html)） |
| 搜索或查找对象 | `search`（5 种 TYPE） | **API**：`HEServices.Search` 加 `GetAllSearchDBEntries`，或 `DMObjectsFinder` 加过滤器 | search 页面写明结果“只会显示在结果列表对话框中”，未给出的选项沿用界面当前值；类可以返回对象数组（[search.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/search.html)、[NavigatingTheDatamodel.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/NavigatingTheDatamodel.html)）。注意：`Search` 类要先调用 `ClearSearchDB(project)`，它是否写入项目**未核实** |
| 运行检查 | `check`（PROJECT / PAGES / ONLYCOMPLETED / PARTS / INSTALLATIONSPACE） | **两者都可以**：`HEServices.Check`（`VerifyProject`、`VerifyPages`）或 `check` action。在进程内建议用类 | 官方把 `check` action 和 `Check` 类都列为离线检查的入口（[Verifications.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/Verifications.html)），并说明 action 内部调用 HEServices（[AutomaticActions.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/AutomaticActions.html)）。⚠ **运行检查会生成消息，推断会写入项目的消息管理，因此不是纯只读操作**。在真实项目上运行算不算“写入”，需要在硬性规则 1 下做决定，见第 7 节 |
| 读取检查结果和项目消息 | **没有对应的 action** | **API**：`EServices.PrjMessagesCollection`、`PrjMessagesEnumerator`、`ProjectMessage` | [EServices.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/EServices.html) 写明 EServices 用于“Retrieving project messages”；[Messages.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/Messages.html) 给出了 `PrjMessagesCollection` 的用法 |
| 读取系统消息（EPLAN 日志） | **没有对应的 action** | **API**：`Base.SysMessagesCollection` | [XEsSetProjectPropertyAction.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/XEsSetProjectPropertyAction.html) 建议用 SysMessagesCollection 读取 action 产生的错误；该类在 Base 的目录中（_toc/c39.js） |
| 读取设置 | **没有读取 action**（`XAfActionSetting` 和 `XAfActionSettingProject` 只能写）；`XSettingsExport` 能导出 XML | **API**：`Base.Settings`、`SettingNode`、`SchemeSetting`；项目设置用 `ProjectSettings`、`ProjectSettingNode`、`ProjectSchemeSetting`。需要整棵设置树的快照时，用 `XSettingsExport` 导出到临时目录 | [WorkingWithSettings.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/WorkingWithSettings.html) 列出了这些类及 Get/Set 示例；页面说明导出功能可以读到“设置对话框中看不到的”值 |
| 用户自定义属性的定义 | `XEsUserPropertiesExportAction` | 先用 **action** 导出到临时目录再解析 | DataModel 中是否有等价接口，本次**未核实** |
| 属性放置方案 | `projectmanagement /TYPE:EXPORTPROPERTYPLACEMENTSSCHEMAS` | **action**，导出到临时目录 | API 中是否有等价接口，本次**未核实** |
| 读取部件库 | `partsmanagementapi` 的各个 EXPORT* 类型、`XMExportDCArticleDataAction`、`partslist /TYPE:EXPORTFROMSYSTEM` | **MasterData API**（api-coverage 计划在 M2 读取 MasterData） | 这些 action 只能写文件，需要再解析；`XPamSelectPart` 需要人在对话框中操作 |
| 批量读取设备、连接、页面表 | `XMActionDCCommonExport`、`label`、`devicelist /TYPE:EXPORT`、`partslist /TYPE:EXPORT` | **DataModel**；这些 action 到 M4 做成导出工具 | 它们的输出格式由配置方案决定，需要先写文件再解析，而且有的会弹对话框（例如不传 CONFIGSCHEME 时） |
| 只读打开项目 | `ProjectOpen /OpenMode:ReadOnly`、`ProjectAction /OpenMode:ReadOnly` | **action**（文档化的只读打开方式） | [ProjectOpen.html](https://www.eplan.help/en-us/Infoportal/Content/api/2026/ProjectOpen.html) 写明，指定 OpenMode 时，项目要么按该模式打开，要么不打开并报错。DataModel 中的等价方式本次未核实 |
| 页面图像（供 AI 看图） | `exportToGraphics /TYPE:GRAPHICPAGE`、`export /TYPE:GRAPHICPAGE` | **action**，输出到临时目录。也可以用 `HEServices.Export` 类，但方法本次未核实 | 文档中没有其他获取页面位图的途径。建议考虑从 M4 提前到 M2 或 M3 |
| 单个机柜的总重 | `XCabCalculateEnclosureTotalWeightAction`（One cabinet 模式） | **action**，经 `StorableObjectContext` 传入机柜 | 文档写明该模式不写属性，结果放在 context 中；属于 M4b |

### 5.1 同名 HEServices 类（只核对了名称）
2026 的 HEServices 目录（_toc/c554.js）中有以下类，与 action **名称对应**：`Backup`、`Check`、`Edit`、`Export`、`Export3D`、`Generate`、`Import`、`Import3D`、`Label`、`Masterdata`、`Print`、`ProjectManagement`、`Renumber`、`Reports`、`Restore`、`Search`、`SelectionSet`、`Synchronize`、`Translate`、`PlcService`、`PartsService`、`DeviceService`、`TopologyService`。**action 的每个 TYPE 在这些类中是否都有对应方法，本次未核实。** 这需要在 M2 开始时对照类的参考页面或反射结果，逐个补进 api-notes。

---

## 6. 统计

以下数字按第 4 节的表格统计，不包括附表 4.1。

### 6.1 规模
- 2026 官方总表：**99 个 action**。其中 98 个有详细页面；`XAMlExportProductionData2SmartMountingAction` 的页面返回 404。
- 拆分 TYPE 后的表格行数：**177 行**。其中 24 个多用途 action 拆成 102 行，其余 75 个 action 各占 1 行。机器类型、执行模式之类的“模式参数”写在参数列中，没有拆行。
- 2025 官方总表：101 个，比 2026 多 `MfToggleMainMenuAction` 和 `XPamArticlesSyncAction`，见附表 4.1。

### 6.2 按类别

| 类别 | 行数 | 涉及的 action 数 |
|---|---:|---:|
| 导出 | 21 | 9 |
| 导入 | 13 | 8 |
| 检查与消息 | 4 | 1 |
| 报表 | 2 | 1 |
| 编号 | 5 | 1 |
| 生成 | 14 | 11 |
| 项目管理 | 16 | 11 |
| 部件管理 | 16 | 6 |
| 翻译与语言 | 8 | 2 |
| 设置 | 9 | 8 |
| 3D/生产 | 19 | 12 |
| 受控白名单 | 1 | 1 |
| API 替代 | 15 | 8 |
| 不覆盖 | 34 | 32 |
| **合计** | **177** | （有些 action 跨类别，不能相加） |

### 6.3 按里程碑

| 里程碑 | 行数 | 涉及的 action 数 | 按“最早里程碑”归属的 action 数（合计 99） |
|---|---:|---:|---:|
| M2 | 20 | 10 | 10 |
| M2–M3 | 5 | 5 | 4 |
| M3 | 10 | 8 | 6 |
| M4 | 79 | 31 | 30 |
| M4*（依赖预规划授权） | 4 | 4 | 4 |
| M4b | 19 | 12 | 10 |
| —（不覆盖，或写入默认禁用） | 40 | 37 | 35 |

`check /TYPE:PARTS` 的里程碑写作“M2（可选）/ M4”，统计时计入 M2。

### 6.4 类别 × 里程碑（行数）

| 类别 \ 里程碑 | M2 | M2–M3 | M3 | M4 | M4* | M4b | — |
|---|---:|---:|---:|---:|---:|---:|---:|
| 导出 |  |  |  | 20 | 1 |  |  |
| 导入 |  |  |  | 11 | 2 |  |  |
| 检查与消息 | 4 |  |  |  |  |  |  |
| 报表 |  |  |  | 2 |  |  |  |
| 编号 |  |  |  | 5 |  |  |  |
| 生成 |  |  | 1 | 12 | 1 |  |  |
| 项目管理 | 1 | 5 | 6 | 4 |  |  |  |
| 部件管理 |  |  |  | 16 |  |  |  |
| 翻译与语言 |  |  |  | 8 |  |  |  |
| 设置 | 3 |  |  |  |  |  | 6 |
| 3D/生产 |  |  |  |  |  | 19 |  |
| 受控白名单 |  |  |  | 1 |  |  |  |
| API 替代 | 12 |  | 3 |  |  |  |  |
| 不覆盖 |  |  |  |  |  |  | 34 |

### 6.5 M2 涉及的 20 行
- **检查与消息（4 行）**：`check` 的 PROJECT、PAGES、ONLYCOMPLETED、PARTS（PARTS 为可选）。读取消息没有对应的 action，改用 `PrjMessagesCollection`。
- **项目管理（1 行，另有 5 行 M2–M3）**：`ProjectOpen`（用 OpenMode:ReadOnly），以及 M2–M3 的 `backup /TYPE:PROJECT`、`restore /TYPE:PROJECT`、`projectmanagement /TYPE:CREATESNAPSHOTCOPY`、`ProjectAction`、`edit`。
- **设置（3 行，都是只读导出，可选）**：`XSettingsExport`、`XEsUserPropertiesExportAction`、`projectmanagement /TYPE:EXPORTPROPERTYPLACEMENTSSCHEMAS`。
- **API 替代（12 行）**：`search` 的 5 个 TYPE、`selectionset` 的 4 个 TYPE、`XEsGetProjectPropertyAction`、`XEsGetPagePropertyAction`、`XEsGetPropertyAction`。这些能力在 M2 用 DataModel 或 HEServices 类实现，action 本身不做成工具。

### 6.6 完全不做成工具的 35 个 action
其中 30 个属于“不覆盖”，5 个属于“设置写入，默认禁用”：

`CleanWorkspaceAction`、`EplApiModuleAction`、`ExecuteScript`、`MfExportRibbonBarAction`、`MfImportRibbonBarAction`、`OpenWorkspaceAction`、`RegisterCustomPropertyEditorAction`、`RegisterScript`、`SaveWorkspaceAction`、`UnregisterScript`、`XAfActionSetting`、`XAfActionSettingProject`、`XDLInsertDeviceAction`、`XEGActionInsertSymRef`、`XEsUserPropertiesImportAction`、`XGedClosePage`、`XGedStartInteractionAction`、`XMDeleteReprTypeAction`、`XMExportConnectionsAction`、`XMExportFunctionAction`、`XMExportLocationBoxesAction`、`XMExportPagesAction`、`XMExportPipeLineDefsAction`、`XMExportPotentialDefsAction`、`XPamConvertPartDatabaseToArticleDatabaseAction`、`XPamSelectPart`、`XPartsSetDataSourceAction`、`XPrjConvertBaseProjectsAction`、`XSDPreviewAction`、`XSettingsImport`、`XSettingsRegisterAction`、`XSettingsUnregisterAction`、`gedRedraw`、`navigateToEEC`、`print`。

不覆盖的理由可以归为几类：
- 只能交互使用或只影响界面：15 个
- 属于 Code 级（脚本）：3 个
- 只供安装器或插件注册使用：3 个
- 官方标注为向后兼容、已被取代：6 个
- 一次性迁移或管理员操作：2 个
- 设置写入，默认禁用：5 个
- 物理打印：1 个

---

## 7. 待确认的问题、无法访问的页面与文档疑点

### 7.1 无法访问或缺失的页面
1. `XAMlExportProductionData2SmartMountingAction`：在 2025 和 2026 的总表中都有列出，但详细页面（`.../2025/` 与 `.../2026/XAMlExportProductionData2SmartMountingAction.html`）都返回 **404**。它的参数、它与 RASCenter 版本的区别都无法核实。
2. `MfToggleMainMenuAction` 和 `XPamArticlesSyncAction`：只在 2025 版中有文档。2026 中这两个 action 是被删除了，还是只是不再写入文档，未核实。可以在本机 2026.0.3 上用 `ActionManager.FindAction` 确认。
3. `XPrjActionProjectCompleteMasterData` 不属于官方 API Actions，两个版本都返回 404。如果需要它，应该在 GUI action 清单中查找。

### 7.2 需要在 scratch 项目上验证的副作用（文档没有说清楚）
1. **`check` 是否把消息写入项目**：这决定了在真实项目上运行 `eplan_check_run` 算不算违反硬性规则 1。需要先验证，再决定这个工具的安全级别：是只读、轻度写入，还是只能用于 scratch。
2. `export` 的 `LANGUAGE` 参数会“在导出前把项目翻译成该语言”。这是否会改动项目，需要验证。
3. `backup` 的 `BACKUPMETHOD:SOURCEOUT` 和 `ARCHIVE` 对原项目有什么影响（会不会删除或锁定原项目），文档没有说明。
4. `compress` 具体删除或改动哪些内容，文档没有说明。
5. `ExportProductionWiring` 是否在项目中记录“已导出”状态，可以从 `MultipleConnections` 参数推断出来，但需要验证。
6. `search` action 和 `Search.ClearSearchDB` 写的“搜索数据库”是否存放在项目中。
7. `import3d` 把数据导入到哪里（项目、宏还是部件），文档没有说明。
8. `XCCreateGravingtextAction` 的作用范围（选中的电缆还是整个项目），文档没有说明。
9. `restore /TYPE:PROJECT` 在目标项目已存在时会怎样，文档没有说明。
10. `projectmanagement /TYPE:CREATESNAPSHOTCOPY` 生成的副本是否完整，能否作为 scratch 副本使用（这对硬性规则 1 很有价值）。

### 7.3 会弹出对话框的情形（静默模式下必须避免，调用时必须传齐参数）
- `XMActionDCCommonExport` 和 `XMExportDCArticleDataAction`：不传 `CONFIGSCHEME` 时弹对话框；EXECUTIONMODE 1/2 需要人参与。
- `XEsUserPropertiesImportAction`：缺少 XMLFile、Project 或 Overwrite 时弹对话框。
- `XSettingsImport`：XmlFile 为空时弹对话框。
- `XAMlExportProductionData2RASCenterAction`：FileName 为空，或目标文件已存在时弹对话框。
- `XPamConvertPartDatabaseToArticleDatabaseAction`：不传参数时弹对话框。
- `XMImportDCArticleDataAction`：不传 IMPORTMODE 时会询问。
- `plcservice /TYPE:BUSDATAIMPORT /IMPORTMATCH:1`：可能弹出比较对话框。
- `search`、`print`：未给出的选项沿用界面或工作站当前值。`print` 页面还警告，工作站打印设置会覆盖 action 参数。

### 7.4 文档自身的不一致（照原样记录，没有修正）
- `export`：示例中用到 `/TYPE:PDFPAGE`，`ProjectAction` 的示例中用到 `/TYPE:PDFPROJECT`，但这两个值都**不在** TYPE 列表里（列表中只有 `PDFPAGESSCHEME` 和 `PDFPROJECTSCHEME`）。它们是否为隐藏的旧值，需要验证。
- `export`：TYPE 写作 `DXFDWGPPAGESSCHEME`（多了一个 P），2025 和 2026 都这样写。
- `renumber`：示例中有 `/OMITNUMERATEDBYPLC:0`，参数表中没有这个参数（参数表中是 `ALSONUMERATEDBYPLC`）。
- `translate`：参数写作 `PAGFILTERNAME`（`generate` 和 `reports` 中是 `PAGEFILTERNAME`）。
- `check`：SELn 的说明写“只对 PAGE 值有效”，但 TYPE 值是 `PAGES`。
- `search` 和 `translate`：说明中提到 "ProjectAction (see also "XEsProjectAction")"，名称与 `ProjectAction` 不一致。
- `import`：`CODEPAGE`、`DRIVE`、`SOURCEMACROPATH`（要求路径为 `(drive):\EPLAN4\M\`）、`DESTINATIONFILE` 没有写明适用于哪个 TYPE，看起来是 EPLAN 4 时代的遗留参数。
- `XCCreateGravingtextAction`：示例中 action 名称写重复了（`XCCreateGravingtextActionXCCreateGravingtextAction`）。
- `XSettingsUnregisterAction`：示例说明文字写成了 "Registering Add-ons"。
- `XAMlExportProductionData2SmartMountingAction`：总表中的说明与 RASCenter 版本逐字相同。

### 7.5 授权与环境
- `Topology` 页面写明需要 **Cabling** 授权选项。其他 action 的授权要求，页面没有写。api-notes A2 记录了开发机拥有 `CommandLineActions` 授权。
- `label` 输出 .xls 时需要安装 Microsoft Excel。
- `CleanWorkspaceAction`、`OpenWorkspaceAction`、`SaveWorkspaceAction` 都需要主窗口（mainframe）。
- 预规划相关的 4 个 action（`ImportPrePlanningData`、`ImportSegmentsTemplate`、`ExportSegmentsTemplate`、`XPlaUpdateDetailAction`）标为 M4*，因为 api-coverage 第 1 节记录 Preplanning 在当前授权下不可用。

### 7.6 尚未做的事
- 没有在本机 EPLAN 上执行任何 action，表中所有“效果”都依据文档文字，带 “?” 的是推断。
- 没有核对 HEServices 类在方法层面是否与 action 等价（见 5.1）。
- 没有读取每个 HEServices、EServices、DataModel 类的参考页面，只核对了类名是否存在。

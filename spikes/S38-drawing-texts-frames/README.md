# Spike S38：页面文字替换与标题栏

状态：r3 探针，依据协调人的 r2 报告修正发现逻辑；r3 尚未实机运行。本 spike 只处理绘图工具的前两项；断点、黑盒、跨项目复制页、引脚连接和 PDF QA 留给后续 spike。这里不定义产品工具规格。

## 问题与方法

| 模式 | 要回答的问题 | 证据 |
|---|---|---|
| `findpage` | 哪些页具有页面自由文本和至少两种语言的文本？ | 不需要 `--page`，扫描全部页的普通 `Graphics.Text`，排除图框、路径和占位文本。列出页名、对象编号、存储语言、内容及计数；`replaceReady` 另要求同一页分别有单语言和多语言对象。扫描预算 90 秒；未扫描页和错误明确保留。 |
| `texts` | 一页上哪些对象承载文字，能否读到标识、图层、语言及内容？ | 只读枚举 `Page.AllPlacements`、组的子对象、属性放置容器、finder 中归属于该页的功能，以及 `Page.PlotFrame.SubPlacements`。逐项记录自由文本、属性放置、功能文本、占位文本、占位对象的变量/值集及路径文本。没有对应对象时计数为零；读取失败保留错误。 |
| `replace` | 能否替换自由文本及多语言文本、回滚，并让一次提交对应一次撤销？ | 找一段只有一种存储语言的 `Graphics.Text` 和另一段至少含两种存储语言的 `Graphics.Text`，只改各自首个存储语言，保留其他译文；在同一个 `SafetyPoint` 内修改两段、逐项读回，再显式回滚。之后只提交第一段的一个修改。驱动通过独立的 `eplremote XEsUndoAction` 撤销一次，再由插件重新定位、读回全部语言并比较。 |
| `titleblock` | 页使用哪个图框，标题栏引用哪些页/项目属性，哪些可以修改？ | 读取实际 `Page.PlotFrame.Name`、页属性 11016，以及图框子对象的属性绑定、来源、索引和当前值。各选择一个可写字符串或多语言的页属性、项目属性，分别在 `SafetyPoint` 中修改并回滚；再尝试把 11016 改为同项目另一页已经使用的图框，并回滚。 |

`FunctionText` 不是本目录中的独立图形类型。功能文本读取 `FUNC_TEXT`（20011）、`FUNC_TEXT_AUTOMATIC`（20031）、`FUNC_TEXT_COMMON`（20120），图层来自匹配的 `PropertyPlacement`；功能自身没有图层，找不到匹配放置时返回空数组。多语言内容按存储语言列出，包括语言无关文本 `??_??`，不使用界面语言回退。

绑定来自 `PropertyPlacement.DisplayedProperty/Source` 和 `PlaceHolderText.PropDescr/SourceObject`。r3 将描述符、来源和关系分别读取，先保存属性 ID/索引，再尝试来源，避免可选来源异常丢失整条绑定。来源为空、失效、读取失败，或图框的 `ContextPropertyPlacement` 使用上下文来源时，可按已经读出的 `PAGE_`/`PROJ_` 目录名称推断目标作用域，标记 `inferred=true` 和 `scopeEvidence`；其他情况保持 unknown，不能推断来源为另一个项目的绑定。描述符本身读不到时仍然不写属性。公式、间接引用、实际渲染可见性不能由这些成员完整证明。页上的普通属性放置不冒充标题栏引用；候选只取图框子对象。

## 文件与依据

- `SpikeAddIn.cs`、`SpikeS38Action.cs`：延后调度的 add-in action，名称从程序集 revision 推导，例如 `EplanMcpSpikeS38r3`。
- `Texts.cs`：文字枚举、语言和绑定读取；`Probes.cs`：回滚、提交、撤销校验及图框探针；`FindPage.cs`：只读扫描可替换文字所在页。
- `DiscoveryEvidence.cs`：不访问原生 API 的作用域推断、完整性判定和属性候选分类，同时编入驱动，支持离线回归检查。
- `Evidence.cs`：驱动与插件共用的 scratch 检查、设计文件 SHA-256、计时和原子 JSON 发布。
- `Driver/`：只连接已运行的 EPLAN，注册当前 revision，驱动一个模式并打印摘要，然后注销；不打开或关闭项目。

先例：[S35](../S35-plan-step-undo/README.md) 的撤销验证、[S34](../S34-m4c-actions/README.md) 的 action/driver/revision/报告结构、[S9](../S9-write-api/README.md) 的写入读回。安全与已验证行为见 [API notes](../../docs/eplan/api-notes.md) A9、A11、A14、A17、A18、A32、A36、A41、A42、A44、A46。

官方 API 的本地目录：[`types.jsonl`](../../catalog/eplan-2026.0.3/types.jsonl)、[`DataModel`](../../catalog/eplan-2026.0.3/members/Eplan.EplApi.DataModel.jsonl)、[`Graphics`](../../catalog/eplan-2026.0.3/members/Eplan.EplApi.DataModel.Graphics.jsonl)、[`MasterData`](../../catalog/eplan-2026.0.3/members/Eplan.EplApi.DataModel.MasterData.jsonl)、[`Base`](../../catalog/eplan-2026.0.3/members/Eplan.EplApi.Base.jsonl)、[`属性字典`](../../catalog/eplan-2026.0.3/data-dictionary.jsonl)。目录核对了 `Text.Contents`、`PropertyPlacement`、`PlaceHolderText`、`PathText`、`PlotFrame`、`Page.Properties` 等成员。随安装提供的 `Eplan.EplApi.DataModelu.xml` 进一步说明 `PAGE_FORMPLOT`（11016）是图框名称，赋值前图框主数据必须已在项目内；11015 是表格名称，不能拿来代替图框。

## 安全规则

1. `<scratch root>` 固定为 `%USERPROFILE%\EplanMcp\scratch`，不从参数扩大。驱动和插件都要求目标是其中现存的 `.elk` 及同名完整 `.edb`；规范化完整路径后检查目录边界，拒绝 UNC、符号链接和联接点。哈希遍历也逐项拒绝链接；基线无法哈希时不写入。请求、检查点和报告只允许写到本仓库 `.local/spikes/s38/` 中的 JSON，拒绝链接和其他输出路径。
2. 四个模式均要求已打开的项目全部在 scratch 内（A11：action 本身也可能改写当前项目的 `User.*`）。只按 `.elk` 完整路径找目标项目，页名精确匹配；不依赖 `ProjectOpen` 改变当前项目。`replace` 和撤销另要求**只打开这一份 scratch 副本，且它是当前项目**；检查不满足就停止，不代替工程师关闭或选择其他项目。
3. action 返回后再调度主线程；第一件事是 `LockingStep`，检查模态对话框并防止重入。修改顺序为 `SafetyPoint → UndoStep → QuietModeStep(ShowNoDialogs)`；读回正确后才能提交，先 `Commit()` 再 `CloseUndo()`。撤销在提交 action 和锁定作用域结束之后进行，不调用 `UndoStep.DoUndo()`。
4. 每对象在一次请求中只取得一份属性列表并一直复用，最后统一释放；值、定义、属性 ID、多语言字符串和 ISOCode 分别释放，不释放 EPLAN 返回或项目持有的对象。系统消息只保留开始计数，结束时用一个新集合复制差异，不交叉读取旧集合。
5. `replace` 的多语言样本不足时报告 `incomplete`，不自动创建文字。标题栏没有合适可写绑定、索引不明确、没有另一种现存图框时报告 `skipped`；不导入/修改主数据，不用不存在的名称试探。
6. 驱动在 `eplremote` 前再次检查当前项目、对象身份、提交后内容和设计文件哈希。运行期间不要操作 EPLAN 或启动其他写入者。撤销只执行一次；失败或超时不重试、不自动补偿，保留副本和报告供检查。调度或撤销状态不明时不注销可能仍在运行的插件。

## 构建与运行

在仓库根目录运行，需 EPLAN 2026.0.3 的 API 程序集与 .NET Framework 4.8.1。以下构建命令不会运行 EPLAN：

```powershell
dotnet build spikes\S38-drawing-texts-frames\EplanMcp.Spikes.S38.csproj -c Release -p:AssemblyName=EplanMcp.Spikes.S38r3 -p:AssemblyTitle=EplanMcp.Spikes.S38r3
dotnet build spikes\S38-drawing-texts-frames\Driver\EplanMcp.Spikes.S38.Driver.csproj -c Release
```

API 程序集均为 `Private=false`，不复制或分发。安装位置不同可对两个构建命令传入 `-p:EplanBinPath=<安装的 Bin 目录>`，并同步调整驱动 `EplanBin` 运行时解析目录。修改插件后递增 `r4`、`r5`，两个程序集构建属性与驱动 `--revision` 必须一致（A8）；同进程不能通过注销来卸载旧程序集。

实机执行由协调人另行进行。准备一份关闭状态下复制的完整示例项目，放为 `<scratch root>\spikes\s38\S38_Sample.elk` 及 `S38_Sample.edb`；保留 `User.eod`、`User.eox`。先手工关闭 scratch 之外的项目，再在 EPLAN 中打开**这一份副本**；运行 `replace` 前只留下它并确认它是当前项目。不要重复打开已经打开的项目（A46）。选一页，确保其中有上述两类已有文字；需要时仅在该 scratch 副本中手工准备样本，再用 `texts` 核对。

连接参数按实际实例填写；`--host` 使用本机可连接的网卡 IPv4（S35 按计算机名连接失败）。先用 `findpage` 选择页面，其余模式可以分别选不同页：

```powershell
$driver = '.\spikes\S38-drawing-texts-frames\Driver\bin\Release\net481\EplanMcp.Spikes.S38.Driver.exe'
$project = Join-Path $env:USERPROFILE 'EplanMcp\scratch\spikes\s38\S38_Sample.elk'
$eplanHost = '<本机网卡 IPv4>'
$page = '=GAA+A1&EFS1/1' # 改为副本中的精确完整页名；这只是页名示例

& $driver --host $eplanHost --port 49152 --revision r3 --mode findpage --project $project
& $driver --host $eplanHost --port 49152 --revision r3 --mode texts --project $project --page $page
```

先查看 `findpage.json` 的 `findpage.pages`，选择 `replaceReady=true` 的页名写入 `$page`；`texts` 摘要同时打印按 origin 分类的计数，避免把图框译文当成页面样本。`findpage` 没有找到就绪页时，扫描仍可完整成功，但不能据此运行替换；可在 scratch 中手工准备样本。

`replace` 还需要与 S35 相同的本地 `eplremote` 客户端。它不在版本库中；先准备/构建协调人已有的 `.local\tools\eplremote`，或用 `--eplremote` 指定其现存可执行文件。客户端须支持 `--host <IPv4> XEsUndoAction`，端口从 `EPLREMOTE_PORT` 读取。驱动会给子进程设置该变量，不修改全局环境。

```powershell
$remote = '.\.local\tools\eplremote\bin\Release\net481\eplremote.exe'
& $driver --host $eplanHost --port 49152 --revision r3 --mode replace --project $project --page $page --eplremote $remote
```

默认按对象编号选择第一组符合条件的文字。可在上条命令后加 `--free-id <texts 报告的 oid> --multi-id <另一段至少两种语言的 oid>` 明确选择；只能选该页的普通 `Graphics.Text`，不能选路径文本、占位文本或图框中的文字。

```powershell
& $driver --host $eplanHost --port 49152 --revision r3 --mode titleblock --project $project --page $page
```

图框模式默认选择项目内已被其他页使用、且不同于当前页的第一种图框；也可加 `--frame '<existingProjectFrames 中的名称>'`。只有一种图框时，报告会保留属性探针结果并跳过图框切换；需要协调人在 scratch 中准备另一页使用不同图框后再运行。

## 报告与判读

JSON 保存在 `.local/spikes/s38/<UTC 时间及随机编号>/`，不提交。每个模式有请求、检查点、完成报告和 `driver.json`；替换还保留 `undo-check.json`、`undo-verify.json`。报告包含 revision、目标路径/页身份、各读取和阶段耗时、系统消息差异、API 错误与模态状态；文本清单不截断。

- `findpage.json`：`findpage.pages` 列出含页面自由文本的页及对象；`scannedPages/unscannedPages/pagesWithMultiLanguageTexts/replaceReadyPages/pageErrors` 区分扫描覆盖和样本可用性，控制台最多列前 10 页，JSON 不截断。
- `texts.json`：`inventory.objects/counts/countsByOrigin`、`completeness`、`fieldErrors`、`hashesBefore/After`、`designFileComparison`。`complete` 要求遍历、文字字段和绑定描述符完整；可选来源/关系读取失败保留为诊断，`completeness.nativeSources` 单独标记，并不伪装成原生来源已解析。描述符读不到或文字字段失败时仍为 false，`fieldErrors` 提供具体对象及字段；不能把缺少对象解释为不存在。
- `replace.json`：`replaceRollback.before/afterWrite/afterRollback`、`expectedReadback/restored`、`commit`、`undo.verification.oneUndoRestored`。提交、回滚和撤销均有哈希清单/差异；原始阶段报告单独保留。
- `titleblock.json`：`frameBefore`、已有图框相关页/项目属性、`displayedProperties`、`propertyDiscovery`、`scopeEvidence`、三个 `*Probe` 的读回/回滚与哈希，及 `existingProjectFrames/frameTarget/reachability`。属性探针同时读回绑定值和对应图框文字；存储值正确不等于已证明渲染更新。每个绑定的 `candidateRejectionReasons` 区分描述符未解析、作用域未知、值不可读、只读、名称组成部分、内部属性、不支持的类型及索引不明确；只有实际被图框引用、作用域已解析或已标记推断、可写且索引明确的候选才会修改。`writeError`、`scopeError`、`skipped` 表示失败或未覆盖，不能写成已验证支持。

设计文件哈希覆盖 `.elk` 及 `.edb` 中除 `User.*`、`*.lck` 外的全部文件，比较时保留新增和删除。它是打开项目时的即时磁盘观察，不强制保存、关闭或刷新；业务内容恢复与文件逐字节恢复分别判读，不能相互代替。界面是否实际出现对话框由协调人观察填写 `engineerDialogObservation`，默认 unknown；本 spike 没有 PDF/图像视觉验证。

退出码：0 表示本次模式要求的证据完整且检查通过；2 表示缺少样本、未覆盖或验证不通过；1 表示拒绝、异常或传输失败。看到非零退出码后先检查报告和 EPLAN，不盲目重跑 `replace`。

## 修订与 r2 报告复核

- r1：协调人确认 EPLAN 从仓库外的影子副本加载 add-in，原来从程序集位置查仓库使 `ReportPath` 在写报告前失败。r2 的 `Repository(string? start = null)` 与从报告路径向上查找的修复在 r3 原样保留；路径仍须落在找到的仓库 `.local/spikes/s38/` 下。
- r2 `texts`（运行前缀 `20261026T1522078817237Z`）：没有遍历错误、设计文件哈希相等、没有系统消息；但有 45 个图框绑定的 `ObjectNotCreatedException` 和 1 个符号属性放置绑定的 `InvalidHandleException`，所以旧版遍历所有 `error` 字段的规则得到 `complete=false`。报告实际为图框自由文本 13 段、页面自由文本 2 段（oid `117036`、`117038`，各 20 种语言），没有单语言页面自由文本，不能满足当前替换样本条件。
- r2 `titleblock`（运行前缀 `20261026T1522533696824Z`）：45 个图框属性放置全部是 `ContextPropertyPlacement`，45 条 `displayedProperties` 全是错误对象，没有 propertyId/scope/value；没有 `PlaceHolderText`。因此报告没有证明“图框只有不可写或索引属性”，而是未保留成功解析的绑定。旧版把 `DisplayedProperty`、`Source`、来源身份和关系读取合在一起，无法由这份报告区分具体是哪个 getter 抛错；r3 独立记录各阶段，以实机报告确认，并允许已知目录名称推断。
- 同次 r2 图框从实际 `FN1_013` 切到 `FN1_015`，再恢复到 11016 未设置、实际 `FN1_013`，读回及回滚通过；`Page.eod`/`Page.eox` 哈希仍不同。这只证明业务值恢复，不能称为文件逐字节恢复。

## 结果

r3，2026-10-26，EPLAN 2026.0.3.25421，Electric P8，只打开 `scratch\spikes\s38\S38_Sample`（示例项目的副本，当前项目）。原始报告在 `.local/spikes/s38/`，不提交。

- **r1 没有执行**：EPLAN 从影子副本加载插件，`Assembly.Location` 不在仓库中，报告路径校验在写任何报告之前就失败，action 直接返回 false。r2 起改为从报告路径向上找仓库。以后的插件型 spike 不能用程序集位置定位仓库。
- **`texts`**（页 `=GAA+A1&EFS1/1`）：15 段自由文本（13 段来自图框）、62 个属性放置（45 个来自图框）、297 段功能文本、1 段路径文本、0 个占位符文本；遍历和文字读取完整，设计文件哈希前后相同，无系统消息。图框里的属性放置读取绑定时有 `ObjectNotCreatedException`（诊断性错误，不影响文字读取）。
- **`findpage`**：321 页中 34 页有页面自由文本，28 页有多语言文字，11 页同时满足替换样本条件（如 `=F+A2&EFA1/2`：18 段单语言、4 段多语言）。
- **`replace`**（页 `=F+A2&EFA1/2`）：一段自由文本和一段多语言文字在 SafetyPoint 中修改后回滚，内容恢复；提交一次修改后，经 eplremote `XEsUndoAction` 撤销一次即恢复原文；不弹对话框。
- **`titleblock`**：页使用图框 `FN1_013`（页属性 11016 为空，图框来自项目设置）。项目属性 10011 `PROJ_INSTALLATIONNAME`（图框显示的项目属性之一）修改后读回正确、回滚恢复；把页的图框换成项目中另一种已有图框，读回正确、回滚恢复。页属性探针没有找到可写、无歧义索引的绑定：图框中的页属性绑定描述符读不出来源或作用域，按规则不据此写入。
- 结论：页面文字替换（自由文本、多语言文字）和图框切换具备做成写工具的条件，事务回滚与一次撤销都已验证；标题栏的页属性需要另找可靠的绑定读取方法，项目属性可按已登记的 `PROJ_*` 处理。

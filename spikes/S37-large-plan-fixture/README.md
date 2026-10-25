# Spike S37：大型项目的内容夹具

> 状态：待维护者在 EPLAN 2026.0.3 实测。对应[大项目设计 7.1 / LP-1](../../docs/design/2026-10-25-m6-large-project-plans.md#71-大型-scratch-夹具)。本目录是实验入口，不是已验收的夹具生成器；未运行 EPLAN，不预先登记 API 行为结论。

## 问题

能否只用安装包中的 `EPLAN_Sample_Project`，经公开 API 把隔离副本从 321 页、约 5,543 个 standard 对象扩大到精确的 1,000 / 3,000 / 10,000 页，并保持接近 17 standard 对象/页的密度和 743 对象压力页？比较 `Page.CopyTo` 与导出、插入页宏的成功率、名称隔离、连接、持久性、回滚、一步撤销、每千页耗时和磁盘增长。

## 方法与安全边界

- `New-Seed.ps1` 只查找 `C:\Users\Public\EPLAN\Data` 下唯一的安装示例，不接受任意 `Source`。正常关闭 EPLAN 后复制完整 `.elk`、`.edb`（保留所有 `User.eod` / `User.eox`），核对逐文件 SHA-256。每次新建 generation，不覆盖、不清理已有目录；中断留下 `seed.json` 的 `incomplete`。文件复制只用于制作种子，增加页必须经过 API，绝不直接编辑数据库。
- 写目标固定在 `%USERPROFILE%\EplanMcp\scratch\`。不采用 `EPLANMCP_HOME` 重定向。Action 和 Driver 都解析完整路径，以带尾分隔符的根做 `OrdinalIgnoreCase` 比较；拒绝路径逃逸、ADS、含糊的尾点/空格、祖先及数据库内部的联接点/符号链接。Marker（`.jsonl`）和宏（`.emp`）也必须在 scratch，且在 `.edb` 之外。不存在的/未打开的项目不会自动打开。
- `SpikeS37Action` 注册名为 **`EplanMcpSpikeS37`**，独立程序集 `EplanMcp.Spikes.S37.dll`。与 S32 一样，action 返回后经 WPF Dispatcher 在主线程执行；每次调度先建 `LockingStep`，检查模态窗口，委托内部捕获异常。一次只接受一个运行；批次间释放主线程和所有 API 辅助对象，重新取得项目和页。不保留跨批 EPLAN 对象。
- 每批是 `SafetyPoint → UndoManager/UndoStep → QuietModeStep(ShowNoDialogs)`；静默作用域结束后提交，再 `CloseUndo()`，顺序与 Bridge `TransactionScope` 相同。按页复制遇到第一个异常就停止本批并回滚，后续批次不再执行；页宏原生批量操作失败时只能记录整批来源页，不能推断是哪一页出错。之前已提交的批次保留。
- **禁止在有真实项目打开时运行实验或测试撤销。** `undo` 会检查目标是 CURRENT 项目；它只创建一个命名复制步骤，不会自动撤销。按 Ctrl+Z / 执行 `XEsUndoAction` 前必须再次确认当前项目，并确认该步骤确实存在（[api-notes A41](../../docs/eplan/api-notes.md)）。不要在关闭重开后测试旧撤销步骤。
- [S8](../S8-write-interruption/README.md) 已证明 SafetyPoint 不能防进程死亡。这里不强杀、不重启 EPLAN、不处理对话框。中断、超时、崩溃或缺少终结 marker 时，副本可能只完成部分操作；先人工检查，使用新 generation 重测，不自动重发。
- 只释放 finder、属性列表/值、macro、服务、context、事务等辅助对象。**不 Dispose EPLAN 返回的 Project/Page/Function/Placement/Connection**（A36/A39）。一个对象的名称属性列表只获取并释放一次。系统消息每次只使用一个集合，读完立即转为普通 JSON，不保存原生消息对象（A11）。

### 实际调用的重载

已对照 `catalog/eplan-2026.0.3/members/`；编译可绑定，不等于运行、授权和行为已验证：

```csharp
Page.CopyTo(Project, PagePropertyList, PageMacro.Enums.NumerationMode,
            bool bOverwrite, bool bDontUseProgressBar) // false, true
PageMacro.Create(string, Page[], MultiLangString)
PageMacro.Open(string, Project)
Insert.PageMacro(PageMacro, Project, bool[], PageMacro.Enums.NumerationMode)
Generate.Connections(Page[], bool) // false; 仅 /GenerateConnections:1
```

页宏的 `bool[]` 重载允许先修改 `PageMacro.Pages` 各页的名称，再插入；传入等长、全 `false` 的数组。此用法也见安装目录的 `Eplan.EplApi.HEServicesu.xml`。不会用“插到某页之后”的重载猜测目标结构，不编辑 `.emp` XML，不写 `$(MD_MACROS)`。页宏没有 `bDontUseProgressBar` 参数，整个导出/插入在静默作用域内；实机仍须确认无进度窗口/对话框。

### 模式和参数

共同必填：`/Mode`、`/Project:"完整 .elk 路径"`、`/Marker:"完整 .jsonl 路径"`。Marker 逐行追加并刷盘；不会清空旧记录，用 `runId` 区分。Driver 把下表的 PascalCase 参数映射为小写命令行选项。

| Action 参数 | Driver 参数 | 含义 |
|---|---|---|
| `Mode` | `--mode` | `count` / `copypages` / `pagemacro` / `undo` |
| `Pages` | `--pages` 或 `--pages-file` | 分号分隔的**完整页名**；文件为每行一页。省略时冻结本次调用开始时的全部页，绝不在本次调用内递归复制新增页 |
| `Copies` | `--copies` | 复制轮数，默认 1，1–100；每轮复制冻结的来源页集 |
| `Prefix` / `Start` | `--prefix` / `--start` | 默认 `S37C` / `1`；Prefix 为不带 `=` 的 ASCII 标识符。第 k 轮的结构为 `=<Prefix><Start+k>` |
| `BatchSize` | `--batch-size` | 每批至多多少页，默认 25，1–50；不跨复制轮次 |
| `NumerationMode` | `--numeration-mode` | 默认 `Number`；还接受 `Ignore`、`None`、`NumberPreferPrefix`、`NumberWithQuestionMark`，不接受数字 |
| `TargetPages` | `--target-pages` | 可选，精确截断最后一轮，最多 10,000；Copies 必须足够，目标必须大于初始页数 |
| `Rollback` | `--rollback` | `1`：只尝试首批，然后显式 Rollback，绝不 Commit；默认 `0` |
| `GenerateConnections` | `--generate-connections` | `1`：仅对新页调用 `Generate.Connections(newPages, false)`；默认 `0`，保留复制本身的连接结果 |
| `RunId` | 自动生成 | 仅用于 marker 关联和 scratch 宏目录命名 |

每轮把原来的 `=EC1+A3&EFS1/1` 变为 `=S37C1.EC1+A3&EFS1/1`：新前缀加在原 higher-level function 前，保留原 `EC1` 和其他名称部分，避免把原来仅 `=` 不同的两个来源页合成同一个目标名。`NameParts.CopyTo(target, true)` 保留其他名称属性。读回必须与请求全名完全相同，否则回滚，不能把 A13 的静默丢结构当作成功。

`count` 分开计时 `GetPages(null)`、`GetFunctions(null)`、功能名称/主辅/有页无页元数据、`GetPlacements(null)`、`GetConnections(null)`、连接归页、逐页 `AllPlacements` 补额外表达，以及名称去重。standard 对象数按现有快照的种类计算：每页自身 + 页上 Function 放置 + 归页的非 3D Connection；它不是 44,735 个 finder placement 的数量，也没有读取 standard 属性值。

每批记录 before/inside/after 计数、逐个新页密度、复制 API 耗时、宏导出/插入耗时、连接生成前后计数、整个测量批次耗时、逻辑文件字节数、系统消息差集和异常。`collidingNewFunctions` 是新身份的全名与本批开始时已有名称的碰撞；同一轮内跨页主辅同名可能合法。`collisionsOutsideCopy` 针对**本轮开始前**已有的名称域，包括种子和前几轮；非零、已有功能身份消失/名称/页/主辅改变、页数不符、放置/功能密度下降都会使本批回滚并停止。不能只凭不同页名判定没有设备合并。

## 构建、注册和运行

从仓库根目录，在安装了 EPLAN 2026.0.3 和 .NET SDK 的 Windows 开发机执行：

```powershell
dotnet build spikes\S37-large-plan-fixture -c Release
dotnet build spikes\S37-large-plan-fixture\Driver -c Release
$driver = (Resolve-Path 'spikes\S37-large-plan-fixture\Driver\bin\Release\net481\EplanMcp.Spikes.S37.Driver.exe').Path
& $driver --self-test
```

本次开发环境已构建两个项目（0 警告、0 错误），17 项离线路径/命名检查通过；没有启动或连接 EPLAN。Driver 使用与 S32 相同的 Remote Client 引用及 `System.Memory` 依赖；EPLAN DLL 均 `Private=false`。如果修改过已加载的 DLL，要正常退出 EPLAN 再构建/启动；也可构建新身份并在每次 Driver 命令追加 `--revision r2`：

```powershell
dotnet build spikes\S37-large-plan-fixture -c Release -p:AssemblyName=EplanMcp.Spikes.S37r2 -p:AssemblyTitle=EplanMcp.Spikes.S37r2
```

先正常关闭 EPLAN，准备独立种子，避免已提交批次影响下一种方法的对照。至少按 40 GiB 规划空间，再依据实测修订（还要包含运行副本和备份）。以下命令仅制作种子，不运行实验：

```powershell
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$seeds = @{}
foreach ($name in @('copy1000','copy3000','copy10000','macro1000','macro3000','macro10000','rollback','macro-rollback','undo')) {
    $seeds[$name] = & .\spikes\S37-large-plan-fixture\New-Seed.ps1 -Generation "s37-$stamp-$name"
}
$project = $seeds['copy1000']
$run = Split-Path -Parent (Split-Path -Parent $project)
$marker = Join-Path $run 's37.jsonl'
$pagesFile = Join-Path $run 'source-pages.txt'
$eplan = 'C:\Program Files\EPLAN\Platform\2026.0.3\Bin\EPLAN.exe'
Start-Process -FilePath $eplan -ArgumentList @('/Variant:"Electric P8"', 'ProjectOpen', "/Project:`"$project`"", '/Quiet')
Get-NetIPAddress -AddressFamily IPv4 | Select-Object IPAddress, InterfaceAlias
$ip = '<EPLAN 所在机器的 IPv4>'
```

先在 EPLAN 设置中开启“允许通过 Remote Client 进行远程访问”（B4），不能以 `/NoRemoting` 启动；默认端口 `49152`，可用 Driver `--port` 指定。按实际安装可选择 `Pro Panel`，每个变体单独测。界面若自动重开真实项目，先人工正常关闭它们。Remote Client 的中文路径传输有已知缺口（B4），遇到乱码立即停止；不要改写用户名或全局环境。

每条下面的 Driver 命令都会连接现有 EPLAN，发送以下注册命令，然后发送实验 action，等待本次 runId 的 `completed`，在 `finally` 撤销注册并断开；不自动打开/关闭项目，也不点击界面。控制台会打印实际命令：

```text
EplApiModuleAction /register:"<仓库>\spikes\S37-large-plan-fixture\bin\Release\net481\EplanMcp.Spikes.S37.dll" /Quiet
EplanMcpSpikeS37 /Mode:"count" /Project:"<scratch .elk>" /Marker:"<scratch .jsonl>" /RunId:"<id>"
EplApiModuleAction /unregister:"EplanMcp.Spikes.S37" /Quiet
```

注册返回成功也不能代替 marker。单次远程 action 回应看门狗为 30 秒，marker 默认等 7,200 秒（`--timeout-seconds`）；超时请求创建 `<marker>.cancel`，在页/批边界取消，不能中断正在执行的原生 API。若远程调用本身仍未返回，Driver 不再并发发送注销命令，会打印待人工执行的注销命令；确认 EPLAN 恢复响应后再清理注册。不会修改全局代理变量，清除只作用于 Driver 进程。

### count：先核对基线并冻结来源页

```powershell
& $driver --host $ip --mode count --project $project --marker $marker
$rows = @(Get-Content -LiteralPath $marker | ForEach-Object { $_ | ConvertFrom-Json })
$baseline = $rows | Where-Object event -eq 'count' | Select-Object -Last 1
$baseline.snapshot | Select-Object pages, standardObjects, finderFunctionsWithPage, finderFunctionsWithoutPage, placements, connections
$baseline.sourcePages | Set-Content -LiteralPath $pagesFile -Encoding utf8
$baseline.snapshot.pageDetails | Where-Object name -eq '=EC1+A3&EFS1/1'
```

先确认安装示例实测约为 321 页 / 5,543 standard 对象、密页为 743；不同则记录安装版本与摘要并调查，不能继续沿用预期密度。页列表文件属于 scratch 本地结果，不提交仓库。

### copypages：单批试探，再逐级扩大

先在 `rollback` 副本测试下面的小批。通过后，在 `copy1000` 副本执行全来源页复制，准确停在目标页数：

```powershell
& $driver --host $ip --mode copypages --project $project --marker $marker --pages-file $pagesFile --copies 3 --prefix S37C --batch-size 25 --numeration-mode Number --target-pages 1000
```

维护者在界面中正常关闭这个副本，再打开独立的 `copy3000` 或 `copy10000` 副本，设置对应 `$project` / `$marker` 后运行。不要复制已经扩大的页集；始终传最初保存的 `$pagesFile`：

```powershell
$project = $seeds['copy3000']; $marker = Join-Path (Split-Path -Parent (Split-Path -Parent $project)) 's37.jsonl'
# In EPLAN: open this project before invoking the driver.
& $driver --host $ip --mode copypages --project $project --marker $marker --pages-file $pagesFile --copies 9 --prefix S37C --batch-size 25 --numeration-mode Number --target-pages 3000
$project = $seeds['copy10000']; $marker = Join-Path (Split-Path -Parent (Split-Path -Parent $project)) 's37.jsonl'
# In EPLAN: open this project before invoking the driver.
& $driver --host $ip --mode copypages --project $project --marker $marker --pages-file $pagesFile --copies 31 --prefix S37C --batch-size 25 --numeration-mode Number --target-pages 10000
```

先看小规模结果，再运行下一规模。比较其他编号方式时另建种子；不要在已提交副本上重复使用同一 Prefix/Start。

### pagemacro：scratch 页宏的替代路径

先在 `macro-rollback` 副本测试回滚。通过后，在界面打开对应的独立 `macro1000` / `macro3000` / `macro10000` 副本，分别调用：

```powershell
$project = $seeds['macro1000']; $marker = Join-Path (Split-Path -Parent (Split-Path -Parent $project)) 's37.jsonl'
& $driver --host $ip --mode pagemacro --project $project --marker $marker --pages-file $pagesFile --copies 3 --prefix S37M --batch-size 25 --numeration-mode Number --target-pages 1000
$project = $seeds['macro3000']; $marker = Join-Path (Split-Path -Parent (Split-Path -Parent $project)) 's37.jsonl'
& $driver --host $ip --mode pagemacro --project $project --marker $marker --pages-file $pagesFile --copies 9 --prefix S37M --batch-size 25 --numeration-mode Number --target-pages 3000
$project = $seeds['macro10000']; $marker = Join-Path (Split-Path -Parent (Split-Path -Parent $project)) 's37.jsonl'
& $driver --host $ip --mode pagemacro --project $project --marker $marker --pages-file $pagesFile --copies 31 --prefix S37M --batch-size 25 --numeration-mode Number --target-pages 10000
```

宏位于 marker 同目录的 `macros-<runId>\batch-<n>.emp`，不覆盖已有宏。每批先导出本批来源页，再打开、命名并插入。宏文件不受 SafetyPoint 回滚，失败/回滚后保留供检查。A37 表明宏会含来源项目的本机路径，原始文件和 marker 均留在 scratch，不进仓库。

### rollback：一批不提交

在界面打开 `rollback` 副本，密页至少包含在来源中；Rollback=1 即使省略 Pages 也只执行首批。再对 `macro-rollback` 独立副本做同样检查：

```powershell
$project = $seeds['rollback']; $marker = Join-Path (Split-Path -Parent (Split-Path -Parent $project)) 's37.jsonl'
& $driver --host $ip --mode copypages --project $project --marker $marker --pages '=EC1+A3&EFS1/1' --copies 1 --prefix S37R --rollback 1 --numeration-mode Number
& $driver --host $ip --mode count --project $project --marker $marker
$project = $seeds['macro-rollback']; $marker = Join-Path (Split-Path -Parent (Split-Path -Parent $project)) 's37.jsonl'
# In EPLAN: open this project before invoking the driver.
& $driver --host $ip --mode pagemacro --project $project --marker $marker --pages '=EC1+A3&EFS1/1' --copies 1 --prefix S37R --rollback 1 --numeration-mode Number
& $driver --host $ip --mode count --project $project --marker $marker
```

检查 `batch.committed=false`、`copiesDisappeared=true`、inside 比 before 增长、after 恢复，并正常关闭重开再次 count。失败时必须保留 marker 中的异常和系统消息，不能把未成功创建任何页的回滚当作 API 可回滚的证据。

### undo：创建一个复制步骤，再由人撤销

在界面打开 `undo` 副本并选为 CURRENT；核对所有打开的项目都是 scratch。此模式仅复制首批，使用 CopyTo 路径，不执行 XEsUndoAction：

```powershell
$project = $seeds['undo']; $marker = Join-Path (Split-Path -Parent (Split-Path -Parent $project)) 's37.jsonl'
& $driver --host $ip --mode undo --project $project --marker $marker --pages '=EC1+A3&EFS1/1' --copies 1 --prefix S37U --batch-size 25 --numeration-mode Number
& $driver --host $ip --mode count --project $project --marker $marker
# In EPLAN: verify the CURRENT project and the named "S37 undo ..." step, then press Ctrl+Z exactly once.
& $driver --host $ip --mode count --project $project --marker $marker
```

如已有 Remote Client 会话，人工核对之后的等价远程命令为 **`XEsUndoAction /Quiet`**。先确认 undo 批次的 `committed=true`，不要在失败批次、空撤销历史或错误当前项目上尝试它。检查一次撤销恢复页、功能、放置和连接，且此前历史仍在。

## 结果

EPLAN 2026.0.3.25421，Electric P8，2026-10-25，只在 `%USERPROFILE%\EplanMcp\scratch\fixtures\large-plans\` 下的安装示例副本上运行；原始 marker 留在本地，不入库。

| 验证项 | 结果 |
|---|---|
| `Page.CopyTo(Project, PagePropertyList, NumerationMode, false, true)` | 可调用；热身后约 80 ms/页（首页约 0.6 s）。新页名由源页 NameParts 复制后改写 `DESIGNATION_PLANT` 与 `DESIGNATION_LOCATION` 得到 |
| 名称冲突与合并 | 只改 `=` 时 3 页即有 82 个新功能撞上现有名称（`+A1-WE1` 等按位置命名的设备）；同时给 `+` 加组前缀后，冲突只剩无名称的 `+` / `=+` 功能和 3 根显式定位的电缆（`+A1-WE2`、`+A2-WE1`、`+A2-WE2`）。5 种 NumerationMode（Ignore、None、Number、NumberWithQuestionMark、NumberPreferPrefix）结果相同，编号模式不改这些名称。已有功能在所有批次中都未改变（changedOrMissingExistingFunctions=0） |
| 流体页 | `&PFB` 页（36 页）的 CopyTo 返回无效句柄（InvalidHandleException，“可能已删除对象”），推测与授权有关，未进一步核实；夹具排除这些页 |
| 空属性 | 页名部分为空（如 `&AAA1/1` 没有 `=`）时 `PropertyValue.ToString()` 抛 EmptyPropertyException，须先判断 `IsEmpty` |
| 回滚 | 检查失败或取消（`.cancel`）时未提交的批次全部消失，计数与批前完全相同（多次观察） |
| 持久性 | 1,000 页副本正常退出 EPLAN、重启重开后，页/功能/放置/连接/standard 计数完全一致 |
| 插件更新 | 同名同版本程序集不会在同一 EPLAN 进程内重新加载；改动实验插件后须重启 EPLAN |
| 撤销 | 未测（夹具生成不依赖撤销） |

规模与名称枚举耗时（finder 名称元数据、全部放置、两者合并）：

| 页数 | 功能 | 放置 | 连接 | standard 对象 | 不同功能名 | 项目大小 | GetPlacements | 合并枚举 |
|---|---|---|---|---|---|---|---|---|
| 321（原始） | 2,876 | 44,735 | 2,060 | 5,543 | 1,945 | 213 MB | 0.25 s | — |
| 1,000 | 6,141 | 133,228 | 3,582 | 12,540 | — | 265 MB | 0.6–0.8 s | 0.4–0.5 s |
| 3,000 | 24,572 | 395,684 | 12,400 | 50,259 | — | 464 MB | 2.0 s | 2.2 s |
| 8,800 | 77,271 | 1,161,136 | 37,534 | 158,132 | 62,201 | 1.04 GB | 5.7 s | 7.5 s |

生成耗时：1,000 页约 1.5–2 分钟，3,000 页约 11 分钟（每批 50 页，每批全项目校验随规模增长：3,000 页时约 14 s/批，8,800 页时约 45 s/批）。8,800 页处用 `.cancel` 停止，未继续到 10,000 页。

## 结论与局限

- 内容轨道可行：`Page.CopyTo` 加上同时改写 `=` 与 `+` 的页名，能从安装示例生成密度接近原示例（约 17 standard 对象/页）的大项目，已有设备不被改动，失败批次可回滚，结果可持久保存。夹具排除流体页，并允许记录无名称功能与 3 根电缆的名称共用。
- 名称枚举随项目线性增长：3,000 页约 2–4 s，8,800 页约 8–9 s，因此每步 guard 不能在万页项目上做全量枚举（10 s 预算）；按名字定向查找的完整性需要另行证明（超万页工作，项目负责人 2026-10-25 决定后续再做）。
- 没有测撤销，也没有测页宏（`pagemacro`）路径；连接拓扑只核对了计数。

这是逐批测量用 spike：源码、文档和依赖锁文件可入库；项目二进制、宏、绝对路径、原始 marker 和客户材料均不入库。

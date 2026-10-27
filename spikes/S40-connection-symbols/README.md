# Spike S40：连接符号与 autoconnect

状态：**完成** 2026-10-27（r1–r6）。本目录不加入解决方案，不注册产品工具。

## 问题

1. 项目已有库中，哪些符号/变体对应角连接、T-node、电位连接点？库连接点的方向和相对位置如何映射到页？
2. 上下对齐、错开一格、转角、三分支、电位点分别产生哪些 autoconnect 图形线段？生成前后是否有真实逻辑 `Connection`，两端引脚是什么？
3. 创建和可选生成能否在同一事务中回滚、提交、恰好一步撤销并持久保存？失败回滚后是否不留空撤销步？

## 方法

沿用 [S36](../S36-module-commit/README.md) 的独立 net481 add-in、延后主线程 action、Remote Client driver、逐次新 JSONL；参考 [S38](../S38-drawing-texts-frames/README.md) 的路径检查、文件哈希和系统消息。API 依据是 [api-notes](../../docs/eplan/api-notes.md) A13/A14/A19/A24/A26/A33/A36/A39/A41/A46/A49/A51，以及 [M8 设计](../../docs/design/2026-10-27-m8-drawing.md) 4.1、4.3、4.4、7.1、7.2。

| 文件 | 职责 |
|---|---|
| `SpikeAddIn.cs`、`SpikeS40Action.cs` | 临时插件入口、`EplanMcpSpikeS40` action、锁/调度/事务和错误边界 |
| `Probe.cs` | 项目库发现、五个用例、返回对象与重新查找的对象/引脚/连接读回、属性列表缓存 |
| `Evidence.cs` | 插件和驱动共用的固定项目/输出路径检查；分组磁盘哈希 |
| `Driver/` | 连接已运行的 EPLAN，一次执行一个 step；完成后在 `finally` 注销 |
| 两个 `.csproj`、`packages.lock.json` | 与 S36 相同的本地 Bin 引用模式及锁文件，所有 EPLAN 引用 `Private=false` |

插件和驱动只接受现存的 `%USERPROFILE%\EplanMcp\scratch\writes\LargeWrite.elk` 及完整同名 `.edb`，拒绝 UNC、链接/联接点和其他路径。插件要求**只打开 LargeWrite，且它为当前项目**；从已打开项目中查找，绝不打开/关闭项目或切换当前项目。输出仅限本 worktree 的 `.local/spikes/s40/`；插件从日志路径向上定位仓库，避免 A49 的影子副本问题。运行期间不要操作 EPLAN 或启动其他写入者。

回调第一步是 `LockingStep`，随后检查模态窗口；action 有防重入门，调度等待 120 秒，驱动等待日志 150 秒。回调内部捕获全部异常。借用或新建的 `StorableObject` 都不 `Dispose`；每对象每次主线程调用只取得一份属性列表，读写复用，最后释放一次。引脚包装、属性值、多语言字符串、服务另行释放。只读不调用 `GetAllCurrentValues`，不读取/占用授权，不打开系统库、不导入/同步/修改主数据。

| step | 行为 |
|---|---|
| `discover` | 只读遍历 `Project.SymbolLibraries → Symbols → Variants → ConnectionPoints`；记录库、名称、`SYMB_NR`、类型、全部相关变体及相对坐标/方向。类型覆盖 Routing 家族、DynamicRouting、TNode 四向、StrandConnectorAngle、PotentialTerminal 与 PotentialDefinition，后两者分别报告。另列普通 Function 的两点候选。可选 `--page '<精确完整页名>'` 读取已有页栅格和是否为空；不创建页。 |
| `sentinel` | 确认没有 S40 页，在 9400–9499 中找连续两个空闲计数器，建 Circuit 哨兵页、英文描述 `S40 sentinel`，独立提交。 |
| `place` | 要求哨兵存在、后一个计数器空闲；复制已有 Circuit 页的结构字段，新建 `S40 place` 页，按新页栅格构造下表。先测 `SafetyPoint → UndoStep → QuietModeStep`；只有创建阶段的 `LockingException` 才视为 A13 型拒绝。显式回滚、释放整个作用域，重新查找并比较基线；无残留才以显式 `Project.LockAllObjects → UndoStep → Transaction → QuietModeStep` 重试一次。生成与创建仍在同一事务内，不分开提交。其他错误立即回滚并失败。 |
| `readback` | 重新定位描述属于 S40 的高计数器页；输出全项目页/连接编号、S40 页对象及其库/变体、引脚、图形连接、逻辑端点与逐用例汇总，供撤销和重开比较。 |

| 用例 | 栅格几何与对象 |
|---|---|
| A | 两个无部件两点功能，选中引脚分别在 `(10,45)`、`(10,35)` 格，上方朝下、下方朝上 |
| B | 同 A，但选中引脚在 `(30,45)`、`(31,35)` 格，横向恰错一格，作为负对照 |
| C | `(10,15)` 格放两点正交角连接，按两个实际出线方向，各隔六格放一个朝向相反的普通功能 |
| D | `(35,15)` 格放三点 T-node，按三个实际出线方向，各隔六格放一个普通功能 |
| E | `(60,40)` 格放单点 `PotentialTerminal`，沿其实际出线方向隔六格放普通功能；不拿 `PotentialDefinition` 替代，不猜必要电位属性 |

候选按库名、符号名、变体号排序选择。普通功能须有两个相反的竖向/横向引脚；连接符号按类型及实际引脚形状筛选。连接点偏移必须落在页栅格且每轴不超过两格，方向为四个单一正交方向。缺少任何必需候选时回滚并报缺样本，不导库、不随意替换。部件引用必须为零，插入点请求与实际不符则回滚。日志保留选中库/符号/变体、连接点目标、库偏移、请求与实际插入点。

`before_generate` 使用直接返回的对象与页枚举的并集，避免 A13 中提交前 finder 看不到功能的误判；`--generate true`（默认）另记 `generate_enter → after_generate`，仅调用 `Generate.Connections([page], false)`，`false` 明确不调用。逻辑连接取 finder 与返回功能引脚 `Connections` 的并集，单列 finder 数量；完整记录 `StartPin/EndPin`，不能用计数替代端点比对。

`--fail true` 仅对 `place` 生效：全部放置和所选生成路径读回之后故意抛异常，记 `rolled_back.deliberate=true`，用 `SafetyPoint.Rollback` 或 `Transaction.Abort`，不 `CloseUndo`。`failure_rollback_check.restored` 必须为 true。这不证明没有空步，仍要执行下面的独立撤销对照。有效提交顺序是 `Commit → CloseUndo`。

每次记录目标磁盘清单和差异，包含新增/删除文件，分 `design`、`user`（User.*）、`settings`（Settings.*），只排除 `.lck`；不强制保存/刷新。系统消息仅保留开始计数，结束新建一个集合并立即复制。业务恢复、磁盘字节恢复分别判读；关闭会改 `ProjectInfo.xml`（A46）。

本地 [API 目录](../../catalog/eplan-2026.0.3/members/Eplan.EplApi.DataModel.jsonl)、[MasterData 目录](../../catalog/eplan-2026.0.3/members/Eplan.EplApi.DataModel.MasterData.jsonl)、[属性目录](../../catalog/eplan-2026.0.3/data-dictionary.jsonl) 及安装的 DataModel XML 确认：`PinBase.Location` 为相对插入点坐标，日志保留原值并另列相加推算的 `pagePositionByAddition`；实际页原点、毫米对应和可见位置仍需实跑核对。`GraphicalConnections` 表示 autoconnect 线段，两端分别来自 `Start/EndConnectionPoint` 与 `Start/EndSymbolRef`。静态 `SymbolReference.Create(variant, page)` 可返回专门子类，日志记录实际类型。项目借用库不关闭；XML 只要求对 `Initialize(string)` 返回 true 的主动系统库调用 `Close`，本探针不走该入口。

目录不能确定具体安装库中的候选名称/编号/变体、电位点必需属性、图形线出现时机、真实逻辑端点、组合事务的生成/Abort 行为和撤销持久性。本探针只覆盖上述五例；M8 7.2 的反向、交叉、所有四向、移动、普通 Line 负例、重复生成、图像及全部范围外影响验证尚不覆盖，不能据本探针宣布整个 connection.create 门槛通过。图形段汇总会从两端重复报告；以端点对去重后比较。

## 运行步骤

在仓库根目录构建，不运行 EPLAN。Bin 默认 `C:\Program Files\EPLAN\Platform\2026.0.3\Bin`，可对构建显式传 `-p:EplanBinPath=...`；本驱动运行时解析固定为该本机安装。不要分发 EPLAN DLL。

```powershell
dotnet build spikes/S40-connection-symbols -c Release
dotnet build spikes/S40-connection-symbols/Driver -c Release
$driver = (Resolve-Path spikes/S40-connection-symbols/Driver/bin/Release/net481/EplanMcp.Spikes.S40.Driver.exe).Path
& $driver --validate-only --step place --fail true --log .local/spikes/s40/parameters.jsonl
```

由维护者先在关闭状态从非客户示例制作完整 LargeWrite 副本，保留 User.eod/eox；需要重建时只对**已关闭且无须保留修改**的副本运行 `eng/scripts/eval-fixture.ps1 -WriteCopies -Force`。在 EPLAN 手工打开它、关闭其他项目并确认当前项目。不要重复 `ProjectOpen` 打开已打开的项目（A46）。保留原材料哈希供维护者比较；探针只访问上述目标副本。插件加载后不能通过注销卸载 DLL，修改代码须先退出 EPLAN再构建。

准备 S36 使用的现存本地 `.local/tools/eplremote`，不入库；下面连接、撤销、关闭和重开命令由维护者逐条执行，driver 不执行它们。

```powershell
$env:EPLREMOTE_HOST = '<开发机本机 IPv4>'
$env:EPLREMOTE_PORT = '49152'
$remote = (Resolve-Path .local/tools/eplremote/bin/Release/net481/eplremote.exe).Path
$write = Join-Path $env:USERPROFILE 'EplanMcp/scratch/writes/LargeWrite.elk'
$run = Get-Date -Format yyyyMMdd-HHmmss
function S40($label, $step = 'readback', $fail = 'false', $generate = 'true') {
    & $driver --step $step --fail $fail --generate $generate --log ".local/spikes/s40/$run-$label.jsonl"
    if ($LASTEXITCODE -ne 0) { throw "S40 $label failed; inspect JSONL and EPLAN." }
}
S40 catalog discover
# 可选：只读检查一张由维护者提供的既有空页，不创建页。
# & $driver --step discover --page '<精确完整页名>' --log ".local/spikes/s40/$run-grid.jsonl"
```

先核对发现清单与选中样本。提交/一步撤销：

```powershell
S40 baseline
S40 sentinel sentinel
S40 before
S40 placed place false true
S40 live
& $remote --host $env:EPLREMOTE_HOST 'XEsUndoAction'
S40 undo1
# 确认 undo1 等于 before：测试页消失、哨兵仍在，才执行第二次。
& $remote --host $env:EPLREMOTE_HOST 'XEsUndoAction'
S40 undo2
```

`undo2` 应等于 baseline；不多撤销。为测试不生成路径，在恢复后的同一副本换新日志标签，重复这一轮，把 `placed` 命令改成 `S40 ng-placed place false false`；其余日志标签也加 `ng-`。每轮都从无 S40 页的基线开始。

失败回滚/无空步：

```powershell
S40 fail-baseline
S40 fail-sentinel sentinel
S40 fail-before
S40 fail-placed place true true
S40 fail-after
# 先核对 deliberate=true、读回阶段完整、restored=true，fail-after 等于 fail-before。
& $remote --host $env:EPLREMOTE_HOST 'XEsUndoAction'
S40 fail-undo1
```

下一次且仅一次撤销应直接删除哨兵，恢复 fail-baseline；否则停止，不能用额外撤销掩盖空步。同样另测 `--generate false` 的故障注入路径。

另一轮持久性，不依赖重开后的撤销历史：

```powershell
S40 persist-sentinel sentinel
S40 persist-placed place false true
S40 persist-beforeclose
& $remote --host $env:EPLREMOTE_HOST 'XPrjActionProjectClose'
& $remote --host $env:EPLREMOTE_HOST "ProjectOpen /Project:`"$write`" /Quiet"
# 确认已重开且当前为 LargeWrite，处理维护者可见提示后继续。
S40 persist-reopened
```

比较关闭前后所有 S40 对象、坐标、引脚、逻辑端点与图形线段；运行时标识不作为跨会话业务名。还须核对 B 没有误接、A/C/D/E 的实际端点与预期一致，并观察画面、模态窗口及耗时。`done.data.ok=true` 只说明探针正常结束，不代表电气语义或 M8 全部门槛通过。

所有日志为新文件，拒绝覆盖。超时、缺少 done 或失败后停止，不自动重试写入；正常完成在 driver 的 `finally` 注销。若调用可能仍在运行，保留注册供检查，确认已结束后手工清理：

```powershell
& $remote --host $env:EPLREMOTE_HOST 'EplApiModuleAction /unregister:"EplanMcp.Spikes.S40" /Quiet'
```

## 结果

EPLAN 2026.0.3.25421，Electric P8，只在新复制的 `writes\LargeWrite` 上；日志在本机 `.local/spikes/s40/`，不入库。

| 轮次 | 结果 |
|---|---|
| r1 | `discover` 只读：项目有 6 个符号库（GRAPHICS、IEC_symbol、IEC_topology_symbol_M20、PID_ESS、PNE1ESS、SPECIAL），SPECIAL 中有 22 个连接类符号；项目文件、`User.*`、设置均不变，无系统消息。`place` 在 `SafetyPoint` 内读符号属性时 `LockingExceptionFailedLockAttempt`（S063176），回滚 |
| r2–r3 | 把发现移到写入之前后，仍在对本事务新建的页调用 `LockObject` 时 S063176；去掉两处显式 `LockObject`（产品从不显式锁对象） |
| r4 | `Function.Create` 在 `SafetyPoint` 中 S063176（与 A13 一致），回滚后业务快照完全恢复；改走 `LockAllObjects → UndoStep → Transaction` 提交成功。同一事务内调用 `Generate.Connections` 后读到 0 条逻辑连接；提交后图形上的自动连线已存在。一次 `XEsUndoAction` 恢复到放置前（页、功能、连接 OID 集合相同），哨兵页保留 |
| r5 | 放置提交后，另起 `SafetyPoint → UndoStep` 调用 `Generate.Connections`：生成 6 条逻辑连接。第一次撤销只撤销这 6 条，13 个符号保留；第二次撤销恢复到放置前。`--fail true`：`Transaction.Abort` 后快照完全恢复，下一次撤销直接删除哨兵页，没有空撤销步 |
| r6 | 放置、生成分别提交，关闭再打开项目后读回与关闭前逐字段相同（13 个符号、6 条连接）。整个会话 EplLog 只有启动版本提示 |

| 用例 | 选用符号 | 图形自动连线（提交后） | 单独生成后的逻辑连接 |
|---|---|---|---|
| A 上下对齐的两个两端符号 | IEC_symbol `BAT` 变体 0 | 有 | 1 条，端点为两符号相对的引脚 |
| B 错开一格（负例） | 同上 | 无 | 无 |
| C 角连接 | SPECIAL `CO`（Routing）变体 0 | 两个方向都有 | 有；测试布局使 C、D 在同一水平线上，EPLAN 把两例也连成一条（布局问题，不是 API 问题） |
| D T 节点 | SPECIAL `TLRO`（TNodeUp）变体 8 | 三个方向都有 | 有，三向 |
| E 电位连接点 | SPECIAL `PCP`（PotentialTerminal）变体 2，返回对象类型 `PotentialDefinition` | 有 | 1 条，起点为空（电位点本身不作为引脚出现），终点为符号引脚 |

结论：
1. 连接符号用 `SymbolReference.Create(variant, page)` 放置，可在 `SafetyPoint` 中提交；带功能的符号要用 `Function.Create`，只能走 `Transaction`（A13）。两者组合时整次写入走 `Transaction`。
2. 对齐即出现图形自动连线；逻辑连接要在**提交之后**单独调用 `Generate.Connections` 才生成，同一事务内生成无效。工具设计须把"放置"与"生成连接"分为两个各自可撤销的提交，或在一次调用内先提交放置再生成（两个撤销步）——交 M8 设计 D07/O3 决定。
3. 页栅格 4.0（API 单位），放置坐标按请求值精确落点；库内连接点偏移是 API 单位，可直接用于对齐计算。
4. 测试布局要让不同用例不共线，否则 autoconnect 会跨用例连接。

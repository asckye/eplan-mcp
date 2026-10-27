# Spike S36 第 1 部分：提交、持久性与撤销

> 状态：**第 1 部分完成**（2026-10-27）；**第 2 部分代码就绪，待实跑**。对应阶段 3 设计第 7 节 `S36-module-commit`、第 9 节 S1/S2，以及阶段 2 U2。已验证提交、持久性、撤销和回滚；引用闭包和写回宏尚待第 2 部分实跑，模块写入门槛仍未通过。

## 问题

1. 在同一真实写入中插入示例宏、应用占位符值集并提交，关闭重开后是否全部保留？逐对象读取引用属性的实际值。
2. 提交是否恰好一个撤销步骤？一次 `XEsUndoAction` 是否恢复写入前状态，同时保留此前独立提交的 sentinel 页和历史？
3. `ApplyRecord` 返回后故意抛异常并回滚，是否没有任何改变，也不留空撤销步骤？下一次撤销应直接删除 sentinel。
4. `ApplyRecord(name)` 与 `ApplyRecord(name, false)` 的属性结果有何差别？`ShowNoDialogs` 下是否仍有对话框、阻塞或系统消息？
5. 布尔、多语言、部件引用和普通文本是否分别生效？多语言只给出新英文，原德文是否保留？部件号属性和真正的部件引用是否一致？

## 方法

独立 add-in 和 Remote Client driver，仿 S32，不加入任何解决方案、产品或测试套件；只在开发机临时注册，每次 driver 的 `finally` 撤销注册。EPLAN 退出前不能替换已加载程序集；改代码后先退出再构建。

action 自身只接受 `%USERPROFILE%\EplanMcp\scratch\writes\LargeWrite.elk`，拒绝其他路径及链接/联接点，并要求它已经打开且为当前项目。不创建、打开、关闭项目，不执行撤销，不读取或占用授权。没有 `License.LockOption`。

每次 driver 只运行一个 `sentinel`、`apply` 或 `readback`。主线程回调有超时、重入/模态检查和内部异常捕获；先 `LockingStep`，写入顺序与产品 `TransactionScope` 相同：`SafetyPoint → UndoStep → QuietModeStep(ShowNoDialogs)`，退出 quiet 后 `Commit → CloseUndo`；失败显式回滚、不关闭撤销步。读取也加锁并 quiet，不调用 `GetAllCurrentValues`，不释放 EPLAN 返回的数据模型对象。

对象所有权沿用产品适配器：`Page`、`Function`、`Project`、放置和部件引用不释放；属性列表按 type/oid 缓存至整次主线程调用结束后统一释放一次，期间所有读写复用同一列表，辅助属性值、语言字符串、属性 ID 和服务按产品方式释放。页描述用类型化 `PAGE_NOMINATIOMN` 读取，其他属性按编号及独立索引读取；逐属性捕获 `PropertyNotFoundException`（S063109），输出 `available=false`、`reason="not available"` 并继续，其含义不同于 `empty=true`，不能据此判为验证通过。

- `sentinel`：确认整个项目没有页计数器 936/937，建立 Circuit 页 936，描述为 `S36 sentinel`，独立提交。
- `apply`：新建页 937，插入自带 `24VDC_Supply.ema`（MultiLine，宏变体 **1**，Number 编号），选编号最小的主功能，创建 `S36` 占位符及 `Apply` 值集，一次应用全部绑定。参数 `variant=default|false` 指重载，与宏变体和布尔属性无关。
- `fail=true`：必须先有 `after_apply` 日志，随后故意抛异常，退出 quiet 后回滚。其他异常也回滚但标记 `deliberate=false`，不能算作故障注入通过。
- `readback`：全项目页数、936/937 页及其全部放置的 type/oid、各功能的四种实际属性和 `ArticleReferences`、占位符和值表，以及 sentinel 描述和项目补充字段。重新查找对象，不跨调用持有对象或使用 DatabaseIdentifier。

| 变量 | 实际绑定 | 应用前 / 请求值 |
|---|---|---|
| Boolean | 主功能 20093 `FUNC_SUPPRESSINDEVICELIST` | false / `1`；实际值另以 boolean 读取 |
| Multilingual | 主功能 20011 `FUNC_TEXT` | en_US=`S36 English before`、de_DE=`S36 Deutsch behalten` / 仅 en_US=`S36 English after` |
| Part | 主功能 20100[1] `FUNC_ARTICLE_PARTNR` | 原宏部件 / 示例项目内另一个现有部件，按部件号排序选择；记录请求值及实际引用的 part/variant/count |
| Plain | 主功能 20027 `FUNC_TECHNICAL_CHARACTERISTIC` | `S36 plain before` / `S36 plain after` |
| Page | sentinel 页 11011 `PAGE_NOMINATIOMN` | `S36 sentinel` / `S36 page applied` |
| Project | 项目 10901[1000] `PROJ_SUPPLEMENTARYFIELD` | 原值（含未设置）/ `S36 project applied` |

页/项目绑定用于观察两个重载的差异；本机 API XML 将第二参数定义为是否应用页/项目数据，默认重载说明也应用页数据，这只是文档依据，实测结果仍待填。既有功能的实际值按 oid/propertyId/index 记录，附 raw、empty，两个语言分别用 `GetStringToDisplay` 读取；值表仅是附加证据。四种功能属性的初始化也在同一次 apply 写入中。

### 构建与准备

在 worktree 根目录用 PowerShell 运行；`EplanBinPath` 默认是本机 EPLAN 2026.0.3 的 Bin，可用 `-p:EplanBinPath=...` 显式指定。构建需要该目录可读（受限代理会话需加入 `--add-dir`），不会运行 EPLAN。

```powershell
dotnet build spikes/S36-module-commit/EplanMcp.Spikes.S36.csproj -c Release
dotnet build spikes/S36-module-commit/Driver/EplanMcp.Spikes.S36.Driver.csproj -c Release
dotnet build EplanMcp.slnx -c Release
dotnet format EplanMcp.Ci.slnf --verify-no-changes
& ./spikes/S36-module-commit/Driver/bin/Release/net481/EplanMcp.Spikes.S36.Driver.exe --validate-only --step apply --variant false --fail true --log .local/spikes/s36/parameters.jsonl
```

先准备 S35 使用的本机工具 `.local/tools/eplremote`（未入库；也可让下面 `$remote` 指向已有工具），必要时 `dotnet build .local/tools/eplremote -c Release`。退出 EPLAN，确认没有需保留的 LargeWrite 修改，且未设置 `EPLANMCP_HOME`，再从自带示例重建夹具。`-Force` 会覆盖副本（A31），不要对打开的项目执行。

```powershell
& ./eng/scripts/eval-fixture.ps1 -Open -WriteCopies -Force
$env:EPLREMOTE_HOST = '<开发机本机 IPv4>'
$env:EPLREMOTE_PORT = '49152'
$remote = (Resolve-Path .local/tools/eplremote/bin/Release/net481/eplremote.exe).Path
$driver = (Resolve-Path spikes/S36-module-commit/Driver/bin/Release/net481/EplanMcp.Spikes.S36.Driver.exe).Path
$write = Join-Path $env:USERPROFILE 'EplanMcp/scratch/writes/LargeWrite.elk'
$run = Get-Date -Format yyyyMMdd-HHmmss
& $remote --host $env:EPLREMOTE_HOST "ProjectOpen /Project:`"$write`" /Quiet"
```

按 S35/A41/A46，此时当前项目通常仍是 fixture。**先在界面确认当前是 fixture，再执行**下面这一条关闭当前项目；若另有项目，逐个核对后关闭，直到仅剩 LargeWrite。不要在 LargeWrite 已当前时执行这条准备命令。

```powershell
& $remote --host $env:EPLREMOTE_HOST 'XPrjActionProjectClose'
```

确认导航器当前项目为 LargeWrite。action 也会核对；它不替你切换。不要重复打开已打开的项目（A46）。之后定义逐步调用函数，首次调用自动注册 add-in，写完日志后自动撤销注册；所有步骤使用同一份未重新构建的 DLL。

```powershell
function S36($label, $step = 'readback', $variant = 'default', $fail = 'false') {
    & $driver --step $step --variant $variant --fail $fail --log ".local/spikes/s36/$run-$label.jsonl"
    if ($LASTEXITCODE -ne 0) { throw "S36 $label failed; inspect JSONL and EPLAN before continuing." }
}
```

输出为 driver 给定路径下的 UTF-8 JSON lines，拒绝复用文件，全部在 git 忽略的 `.local`。写操作保留 `before → fixture → before_apply → apply_enter → after_apply → committed/rolled_back → after → done`（sentinel 没有 apply 阶段），只读步骤为 `readback → done`。`done.data.ok=true` 只说明探针执行完毕，属性/撤销是否通过仍须比较状态。超时或缺少成功终态时停止；先检查模态对话框、日志和实际项目，不自动重试写入。记录每次 ApplyRecord/撤销/重开时是否弹窗、文字、耗时和 quiet 行为。对话框需维护者手工处理；超时可能仍在执行。若 finally 清理也受阻，恢复后用下列命令清理注册：

```powershell
& $remote --host $env:EPLREMOTE_HOST 'EplApiModuleAction /unregister:"EplanMcp.Spikes.S36" /Quiet'
```

### 第一轮：提交后立即一步撤销（关闭重开另测）

逐条运行，期间不编辑项目、不关闭项目。撤销前每次都核对当前是 LargeWrite。

```powershell
S36 du-baseline
S36 du-sentinel sentinel
S36 du-before
S36 du-apply apply default false
S36 du-live
& $remote --host $env:EPLREMOTE_HOST 'XEsUndoAction'
S36 du-undo1
& $remote --host $env:EPLREMOTE_HOST 'XEsUndoAction'
S36 du-undo2
```

`du-undo1` 应等于 `du-before` 的状态：937 和宏/新占位符消失，936 的 oid/内容仍在，项目字段恢复原值；此前历史仍可撤销。`du-undo2` 应等于 baseline：936 也消失。若第一步撤销不符合预期，停止，不能再盲目撤销。**只撤销这两次**，更多撤销可能作用于原有历史或弹模态冲突框（A41）。

同一已恢复的副本再逐条运行上述序列，把所有 `du-` 改为 `fu-`，应用命令改成 `S36 fu-apply apply false false`，同样分别执行两次 eplremote 撤销并读回。

### 第二轮：ApplyRecord 后异常回滚，不留空步

```powershell
S36 dr-baseline
S36 dr-sentinel sentinel
S36 dr-before
S36 dr-apply apply default true
S36 dr-rolledback
& $remote --host $env:EPLREMOTE_HOST 'XEsUndoAction'
S36 dr-undo1
```

`dr-apply` 必须包含 `after_apply` 和 `rolled_back.deliberate=true`；`after`/`dr-rolledback` 应与 `dr-before` 相同。**下一次且仅一次撤销**就应删除 936，`dr-undo1` 回到 baseline；若还需额外撤销才能删除 sentinel，即可能留下空步。重复整个序列，把 `dr-` 改为 `fr-`，应用命令改成 `S36 fr-apply apply false true`。

### 第三轮：真实提交后关闭重开

```powershell
S36 dp-baseline
S36 dp-sentinel sentinel
S36 dp-apply apply default false
S36 dp-beforeclose
& $remote --host $env:EPLREMOTE_HOST 'XPrjActionProjectClose'
& $remote --host $env:EPLREMOTE_HOST "ProjectOpen /Project:`"$write`" /Quiet"
S36 dp-reopened
```

对比 `dp-beforeclose` 与 `dp-reopened`：页/全部对象、占位符/值表、每个目标实际属性（含英文/德文）、部件号及实际 `ArticleReferences` 应完全一致。对比 `before_apply` 和 `after_apply` 判定每种属性是否按请求生效；原语言丢失或部件引用未建立要明确记为失败/差异，不能只凭提交和重开一致就通过。

这一轮不依赖重开后的撤销历史。保持只有 LargeWrite 打开，关闭它后重建**已关闭**的副本，再打开，测试 false 的持久性；不要在未关闭的副本上 `-Force`。

```powershell
& $remote --host $env:EPLREMOTE_HOST 'XPrjActionProjectClose'
& ./eng/scripts/eval-fixture.ps1 -WriteCopies -Force
& $remote --host $env:EPLREMOTE_HOST "ProjectOpen /Project:`"$write`" /Quiet"
S36 fp-baseline
S36 fp-sentinel sentinel
S36 fp-apply apply false false
S36 fp-beforeclose
& $remote --host $env:EPLREMOTE_HOST 'XPrjActionProjectClose'
& $remote --host $env:EPLREMOTE_HOST "ProjectOpen /Project:`"$write`" /Quiet"
S36 fp-reopened
```

结束后关闭 LargeWrite，需要恢复基线时仅对关闭的副本运行上面的夹具重建命令。日志、生成项目和本机工具不入库；本 PR 不编辑 api-notes。

## 结果

环境：EPLAN 2026.0.3.25421，Electric P8；夹具由 `eval-fixture.ps1 -Open -WriteCopies -Force` 从自带示例重建，LargeWrite 起始 321 页、只有它打开且为当前项目。宏 `24VDC_Supply.ema`（MultiLine，变体 1）每次插入 200 个对象，加上 S36 占位符共 201 个；目标功能 `=GAA+A1-FC6`，原部件 `SIE.3RV2011-1GA15`，请求部件 `10 20 206`（示例项目中已有）。日志在 `.local/spikes/s36/r2-*.jsonl`（不提交）。

**第一次运行（r1）EPLAN 崩溃**：sentinel 已提交，随后的读取用通用索引器读页描述 11011，抛 `PropertyNotFoundException`（S063109）；约 1 秒后 EPLAN 在终结器线程中崩溃：`Page.Dispose(Boolean)` → `StorableObject.!StorableObject()` 抛 `NullReferenceException`，进程以 0xc0000005 结束。原因是探针对同一对象反复取得并释放属性列表（违反 A36），之后 Page 的终结器访问了已释放的内容。r2 改为每个对象只取一次属性列表、全部读写结束后统一释放，页描述用类型化的 `PAGE_NOMINATIOMN`，逐属性捕获 `PropertyNotFoundException`；r2 全程无崩溃。

**属性逐项结果**（r2，`before_apply` → `after_apply`，提交后的 `after` 与 `after_apply` 相同）：

| 绑定 | 写入前 | `ApplyRecord("Apply")` | `ApplyRecord("Apply", false)` |
|---|---|---|---|
| Boolean 20093 | `0` / false | `1` / true | `1` / true |
| Multilingual 20011 | `en_US@S36 English before;de_DE@S36 Deutsch behalten;` | `en_US@S36 English after;`，**德文丢失** | 同左，德文丢失 |
| Part 20100[1] | `SIE.3RV2011-1GA15`，部件引用 `SIE.3RV2011-1GA15/1×1` | `10 20 206`，部件引用变为 `10 20 206/1×1` | 同左 |
| Plain 20027 | `S36 plain before` | `S36 plain after` | `S36 plain after` |
| Page 11011（sentinel 页） | `S36 sentinel` | `S36 page applied` | **未改**，仍为 `S36 sentinel` |
| Project 10901[1000] | 空 | `??_??@S36 project applied;` | **未改**，仍为空 |

- 每次插入、应用、提交都在 `ShowNoDialogs` 中完成，没有对话框；EPLAN 系统日志（会话 218）只有启动时的版本提示，没有警告或错误。
- 原宏的 6 个占位符的引用数和值表在应用前后不变（本次不应用它们）。

**撤销**（每次 `XEsUndoAction` 约 2.3 秒，含 Remote Client 连接）：

| 序列 | 第 1 次撤销后 | 第 2 次撤销后 |
|---|---|---|
| du（默认重载，提交） | 与 `du-before` 相同：322 页，937 页、宏和 S36 占位符消失；sentinel 页对象编号 5320 不变，描述恢复 `S36 sentinel`，项目字段恢复为空 | 与 baseline 相同：321 页，sentinel 消失 |
| fu（false 重载，提交） | 与 `fu-before` 相同 | 与 baseline 相同 |
| dr（默认重载，ApplyRecord 后故意抛异常回滚） | 回滚后即与 `dr-before` 相同；**第 1 次**撤销就删除 sentinel，回到 321 页 | — |
| fr（false 重载，回滚） | 同 dr | — |

**关闭重开**：dp（默认重载）和 fp（false 重载，在重建的副本上）提交后关闭再打开（打开约 2.8 秒），重开后的完整读回与关闭前逐字节相同（约 82 KB JSON）：页数、937 页的 201 个对象、全部占位符及值表、目标属性、部件引用、sentinel 描述和项目字段。

## 结论

1. **插入宏 + 新建占位符 + `ApplyRecord` 在一个真实事务中提交后持久**，关闭重开不变。
2. **这样的提交正好是一个撤销步骤**：当前项目一次 `XEsUndoAction` 恢复写入前的全部状态（包括页和项目属性），之前独立提交的 sentinel 仍在，再撤销一次才删除它。
3. **`ApplyRecord` 之后回滚不留撤销步骤，也不留改变**：下一次撤销直接作用于之前的 sentinel。
4. **两个重载的区别只在页和项目数据**：`ApplyRecord(name)` 同时写功能、页和项目属性；`ApplyRecord(name, false)` 只写对象属性，不写页和项目属性。与本机 API XML 的说明一致。
5. **布尔、普通文本、部件编号都按值集生效**；写 20100[1] 部件编号时，功能的实际部件引用随之改变（部件、变体、数量 1）。
6. 🔴 **多语言属性按整体替换**：值集只给一种语言时，其他语言被清除。生成模块时，多语言变量的值必须包含要保留的全部语言，或者在应用前读出、应用后合并；不能假设 EPLAN 只改给定的语言（与 `eplan_property_set` 的 20011 行为不同）。
7. 读属性时每个对象只取一次属性列表并只释放一次（A36）；反复取得并释放会让 EPLAN 在之后的终结器中崩溃。

这些结论满足设计第 7 节 `S36-module-commit` 的提交、重开、一步撤销、回滚四项；第 2 部分（引用闭包、写回宏再插入、文件残留）完成前，仍不注册 `module.insert` 或 `placeholder.apply` 计划步骤。

## 局限

- 只测了一个自带宏、一个新建占位符和显式绑定；原宏占位符没有应用，变量→对象→属性的完整闭包、辅助/额外表达、跨柜引用和写回宏再插入属于第 2 部分。
- 多语言只测了 en_US/de_DE 两种语言、值集只给 en_US 的情况；值集给全部语言时是否完整保留，没有测。
- 部件只用示例项目中已有的部件，没有加载或同步系统部件库；部件引用的变体和数量只观察到默认值。
- 没有测多变体宏、其他表达类型、3D 宏，以及占位符应用中途 EPLAN 自身失败（只测了探针故意抛出的异常）。
- 撤销只在 LargeWrite 为唯一打开项目时测试；多项目时仍按 A41/A46 先确认当前项目。

## 第 2 部分

状态：**代码就绪，待实跑**。对应设计第 7 节「S36 引用闭包」「S36 写回缺口」、第 9 节 S2。独立代码位于 [Part2](Part2/)，程序集 `EplanMcp.Spikes.S36.Part2`，action `EplanMcpSpikeS36P2`，可与第 1 部分同时加载。以下步骤供维护者后续执行；本次仅构建与离线参数检查，不运行 EPLAN，不补写 API 实测结论。

### 问题

1. 插宏并提交后，能否只读枚举全部占位符的变量 → 实际对象 → propertyId/index，覆盖辅助功能、额外表示（总览、成对交叉引用）、页/项目绑定和插入页以外的对象？无法定位、空返回和读取失败能否明确暴露为缺口？
2. guard 后只改变一个绑定属性、保持值表不变，execute 重新发现闭包是否拒绝 `ApplyRecord`？异常回滚是否恢复原状态且没有空撤销步骤？
3. 新建占位符/值集 → 新宏文件 → 关闭宏句柄 → 从磁盘重新打开 → 新页再插入 → 应用 → 提交 → 关闭重开项目，布尔、多语言的全部语言、部件编号及真正的部件引用能否逐项保留？
4. 写回提交占几个撤销步骤？撤销、失败回滚后，项目恢复情况与事务外的宏文件残留分别如何？

### 方法

复用第 1 部分的 Remote Client 驱动和 S43 最新的 Evidence、逐字段空值/异常读回、属性列表缓存清理结构。JSONL 包含完整异常栈、耗时、EPLAN 版本、项目设计文件/用户文件/设置文件的前后 SHA-256 差异及系统消息。空值和读取异常单独记录；读取失败不能作为验收成功。

插件及 driver 都限定既有 `%USERPROFILE%\EplanMcp\scratch\writes\LargeWrite.elk`，它必须是唯一打开且当前的项目，拒绝 UNC、链接和联接点。driver 不切换/开关项目，不执行撤销；每次只调用一个步骤，`finally` 注销本部分程序集。日志只允许 worktree 的新 `.local/spikes/s36p2/*.jsonl`。宏仅写入新建的 `%USERPROFILE%\EplanMcp\scratch\spikes\s36p2\<UTC>-<随机值>\module.ema`，从不覆盖、清理或写入 EPLAN 宏库。

| step | 内容 |
|---|---|
| `sentinel` | 独立提交 Circuit 页 938，描述 `S36P2 sentinel`；要求 938–940 页计数器全项目未占用 |
| `insert` | 独立提交页 939 和 `24VDC_Supply.ema`，与第 1 部分同为 MultiLine、宏变体 1、Number、绝对位置 (100,150)。保留原宏全部占位符，另建 `S36P2Guard`，绑定四种功能属性及 sentinel 页 11011、项目 10901[1000]，不应用值集 |
| `closure` | 纯只读：每个占位符的变量/值表、AssignedObjects、每条绑定表达式、实际属性及原生关联对象；标出 scope、page、是否在插入页之外、主/辅助功能、20121 表示类型和部件引用 |
| `mutate` | 在同一次主线程回调内先记录/校验 guard；闭包未证明时以 `not_started` 拒绝，不创建 SafetyPoint/UndoStep。闭包完整时才进入唯一写链，直接通过属性列表反转 20093 → 重扫 execute 闭包 → 比较属性值和引用结构，必须拒绝且 `tableUnchanged=true`、`applyCalled=false`；实验修改总是回滚。此路径不调用 ApplyRecord；`--fail true` 在已证实拒绝后再故意抛异常 |
| `writeback` | 在 939 的插入对象上新建 `S36P2` 及 `Apply` 值集，用 WindowMacro.Create 保存全部放置；关闭后以 `bFromCache=false` 打开新宏，比较值表，再以 MultiLine、变体 0、Number 插入新页 940，应用新占位符的值集并逐属性验收后提交 |
| `readback` | 重新定位 938–940 页、全部放置/功能/值表、项目字段、原宏闭包及宏文件清单；940 存在时重新定位其绑定并验收四种实际属性及部件引用，可用于重开和撤销后读回 |

闭包候选 propertyId/index 来自对象 `ExistingIds` 与实际使用索引、原始宏 XML 中**全部** `S115x1904/A1911/A1912`（包含空值绑定），以及本探针新增绑定。随后只通过原生 `GetPropertyEntry` / `GetProjectPropertyEntry` 取得当前绑定，按变量标记解析，实际值另从对象属性读取。项目绑定单独枚举，不依赖项目是否出现在 AssignedObjects 中。辅助功能/额外表示取全项目 finder、每页 Functions **和 AllPlacements** 的并集，再沿 `AllSubFunctions`、`ParentFunction`、`CrossReferencedObjectsAll` 和同名功能追踪。关联对象明确标记 `directBinding=false`；不会把关联关系误称为显式变量绑定。页外对象保留实际页/名称作为范围证据，不根据页内筛选丢弃；本 spike 没有柜绑定配置，不据此声称柜归属已证明。

`closure.complete` 只针对这个固定示例及已登记的新增绑定；任何未定位变量、属性目录/关联集合缺失、逐字段读取错误都不能通过 guard。它不构成任意宏格式的通用闭包证明。若返回 `complete=false`，先保留缺口日志，停止 mutate 验证；不能把未执行当作已证明 stale 拒绝。

布尔绑定 20093，普通文本 20027，部件编号 20100[1]；部件只从项目既有部件中选择与原值不同的一项。多语言绑定 20011：插入时保留原有语言并加入 en_US/de_DE/zh_CN，生成值集时读取**全部已存语言**、仅改英文，再把完整集合传入 SetValue。读回用 GetLanguageList + GetString，不用显示语言的回退值伪装语言存在；再插入前后和重新打开宏后的值表相等，应用后逐语言集合相等，并核对实际 ArticleReferences 的 part/variant/count。

所有发现均在写入范围之前完成。写入沿第 1 部分已验证的 `SafetyPoint → UndoStep → QuietModeStep`，不创建 Function、不显式 LockObject；quiet 结束后才 Commit/CloseUndo 或 Rollback。每个原生对象在一次回调中只取/释放一次属性列表；新对象的列表在回滚**之前**清理并移出缓存，回滚后重新枚举项目，不读取或释放失效对象/属性列表（A55，同 Transaction.Abort 禁令）。不释放 StorableObjects、宏 Objects、部件引用或 pins。不新建属性放置；若后续加入，PropertyPlacement.Location 应为相对父符号的偏移（A53/A54）。

首轮日志 `.local/spikes/s36p2/r1-c-*.jsonl` 显示：baseline、sentinel、insert、closure、readback 完成；闭包含 7 个占位符，中断点和位置盒存在页外关联。`Structure_Box_Motor_Overload` 的 `Higher_Level_Function` 仍未定位，保留 `unmappedVariables` 和 `complete=false`，不放宽门槛。mutate 在 guard 后、任何属性改写前，按代码应抛 `InvalidOperationException("Cannot prove closure; mutation probe refuses to proceed.")`；旧恢复路径随后 Rollback 抛 unmanaged BaseException，遮住首个异常。before/restored 业务快照虽相同，SearchResults.eod/eox 有变化，**不能据此宣布回滚成功**。

本次修订将 guard 拒绝和缓存基线发现移到事务之前；Read/Closure/ValidateWriteback 不再创建 nested QuietModeStep，mutate/writeback 都只由入口持有一条写链。异常先以 `write_error` 记录 type/message/stack/stage，再清理缓存和尝试 Rollback；`GC.KeepAlive` 保活 SafetyPoint/UndoManager/UndoStep 至恢复及释放结束，防止异常路径上的早期终结。没有发现嵌套 SafetyPoint/UndoStep/Transaction；原生 unmanaged 的具体原因仍待复测，不能把生命周期风险认定为已实证根因。writeback 的宏创建/句柄关闭/重新打开/再插入/ApplyRecord 分别记录 stage，以便定位内部 API 是否改变事务状态；这些 API 的第 2 部分组合仍未实跑通过。

若 Rollback 失败，日志先有原始 `write_error`，再有含原异常及回滚异常的 `rollback_failed(outcome=unknown)`；立即停止，不再读回项目、取系统消息、重试回滚或通过 Dispose 隐式重试。不触碰剩余原生属性缓存，并隔离事务句柄的终结器。终态为 `done(ok=false,outcome=unknown)`，driver 返回 2；维护者停止后续步骤，重启 EPLAN，并只对已关闭的 LargeWrite 重新复制夹具。本次只有离线构建/回归检查，不表示恢复路径已获实机验证。

### 运行步骤

先退出曾加载此程序集的 EPLAN 再重新构建；插件注册撤销不会卸载 DLL。以下构建/参数验证不连接 EPLAN：

```powershell
dotnet build spikes/S36-module-commit/Part2/EplanMcp.Spikes.S36.Part2.csproj -c Release -p:RestoreLockedMode=true
dotnet build spikes/S36-module-commit/Part2/Driver/EplanMcp.Spikes.S36.Part2.Driver.csproj -c Release -p:RestoreLockedMode=true
$driver = (Resolve-Path spikes/S36-module-commit/Part2/Driver/bin/Release/net481/EplanMcp.Spikes.S36.Part2.Driver.exe).Path
& $driver --validate-only --step writeback --fail true --log .local/spikes/s36p2/check-writeback.jsonl
& $driver --validate-only --step mutate --fail true --log .local/spikes/s36p2/check-mutate.jsonl
```

维护者准备完整、干净的 LargeWrite scratch 示例副本（保留 User.eod/eox），手工确认只打开它且它为当前项目，938–940 未占用。准备第 1 部分使用的本机 `.local/tools/eplremote`，运行期间不编辑项目、不启动其他写入者。不要整段执行以下序列；每一步先检查上一份日志和项目状态，结果未知或有模态对话框即停止，不自动重试。

```powershell
$env:EPLREMOTE_HOST = '<开发机本机 IPv4>'
$env:EPLREMOTE_PORT = '49152'
$remote = (Resolve-Path .local/tools/eplremote/bin/Release/net481/eplremote.exe).Path
$write = Join-Path $env:USERPROFILE 'EplanMcp/scratch/writes/LargeWrite.elk'
$run = Get-Date -Format yyyyMMdd-HHmmss
function S36P2($label, $step = 'readback', $fail = 'false') {
    & $driver --step $step --fail $fail --log ".local/spikes/s36p2/$run-$label.jsonl"
    if ($LASTEXITCODE -ne 0) { throw "S36P2 $label failed; inspect JSONL and EPLAN before continuing." }
}
```

**闭包与 stale 负例**：

```powershell
S36P2 c-baseline
S36P2 c-sentinel sentinel
S36P2 c-insert insert
S36P2 c-closure closure
S36P2 c-before
S36P2 c-mutate mutate
S36P2 c-restored
& $remote --host $env:EPLREMOTE_HOST 'XEsUndoAction'
S36P2 c-undo1
& $remote --host $env:EPLREMOTE_HOST 'XEsUndoAction'
S36P2 c-undo2
```

检查 closure 每个变量的实际绑定、辅助/额外表示及页外引用；记录示例没有覆盖的关系类别，不能把空集合等同于该类别已验证。**当前固定示例仍为 complete=false**：mutate 应出现 `guard → guard_refused(CLOSURE_UNPROVEN) → operation_error(stage=mutate.guard.validate,outcome=not_started)`，没有 transaction_enter/mutated/rollback；此时停止该轮，不执行后面的撤销序列，也不能声称 stale 负例已通过。

只有闭包缺口解决后的独立轮次才验证 `guard → transaction_enter → mutated → execute_recheck → guard_decision(refused=true, tableUnchanged=true, applyCalled=false) → write_error → cache_before_rollback → rolled_back → failure_rollback_check(restored=true)`。c-restored 等于 c-before；第一次撤销直接删除 939（938 仍在），第二次删除 sentinel，回到 baseline。另起独立一轮，标签改 `cf-`，用 `S36P2 cf-mutate mutate true` 验证故意失败，要求 `rolled_back.deliberate=true`，其余判据相同。任何 rollback_failed/unknown 都停止，不继续撤销或沿用该副本。

**写回失败与文件残留**：

```powershell
S36P2 wr-baseline
S36P2 wr-sentinel sentinel
S36P2 wr-insert insert
S36P2 wr-before
S36P2 wr-writeback writeback true
S36P2 wr-rollback
& $remote --host $env:EPLREMOTE_HOST 'XEsUndoAction'
S36P2 wr-undo1
& $remote --host $env:EPLREMOTE_HOST 'XEsUndoAction'
S36P2 wr-undo2
```

必须已有 `macro_saved / macro_reopened(matches=true) / macro_reinserted / after_apply` 和四项 `property_acceptance` 通过，再出现 `rolled_back.deliberate=true`。wr-rollback 的项目业务快照应等于 wr-before，新占位符/940 消失，宏文件应按 `rolled_back.filesOutsideTransaction` **单独记录路径、字节和 SHA-256**；文件是否残留由实跑结果填写，不自动删除。第一次撤销直接删除 939、保留 938，第二次删除 938，证明失败没有空步。

**写回提交、一步撤销与文件残留**：

```powershell
S36P2 wu-baseline
S36P2 wu-sentinel sentinel
S36P2 wu-insert insert
S36P2 wu-before
S36P2 wu-writeback writeback
S36P2 wu-live
& $remote --host $env:EPLREMOTE_HOST 'XEsUndoAction'
S36P2 wu-undo1
& $remote --host $env:EPLREMOTE_HOST 'XEsUndoAction'
S36P2 wu-undo2
& $remote --host $env:EPLREMOTE_HOST 'XEsUndoAction'
S36P2 wu-undo3
```

wu-undo1 项目状态应等于 wu-before（939 保留、S36P2 新占位符/940 消失、938 oid/内容不变），wu-undo2 只剩 sentinel，wu-undo3 回到 baseline。每次只在上一状态符合预期时再撤销；禁止多撤销原有历史。用 wu-live 和 wu-undo1 的 `files_at_readback` 比较该次输出文件的字节/哈希，作为**撤销残留**，与 wr 的**回滚残留**分别填写；宏文件不在项目事务内，文件仍在不能证明提交持久性。

**写回提交、关闭重开与逐项读回**：另起独立一轮，先 baseline → sentinel → insert，再逐条执行：

```powershell
S36P2 wp-baseline
S36P2 wp-sentinel sentinel
S36P2 wp-insert insert
S36P2 wp-writeback writeback
S36P2 wp-beforeclose
& $remote --host $env:EPLREMOTE_HOST 'XPrjActionProjectClose'
& $remote --host $env:EPLREMOTE_HOST "ProjectOpen /Project:`"$write`" /Quiet"
S36P2 wp-reopened
```

每次关闭/打开前确认仅 LargeWrite 打开且当前；不要重复打开已打开的项目（A46）。比较 beforeclose/reopened 的完整业务状态，重点核对 bool、全部语言的集合/内容、20100[1] 及 ArticleReferences 的 part/variant/count；两份 readback 均须有四项 property_acceptance 通过，宏文件哈希不变。重开会改变 DatabaseIdentifier，不跨调用持有原生对象，不用部件引用 oid 作持久标识（A34/A54）。记录是否弹窗、耗时及新增系统消息；关闭会改写 ProjectInfo.xml，文件差异和业务持久性分别解释。此轮不依赖重开后的撤销历史；结束后保持或关闭这份 scratch，需重建时只处理已关闭的 LargeWrite。

超时/异常时先检查 EPLAN、模态框与日志。driver 的 finally 若受阻，维护者恢复后可清理本部分注册：

```powershell
& $remote --host $env:EPLREMOTE_HOST 'EplApiModuleAction /unregister:"EplanMcp.Spikes.S36.Part2" /Quiet'
```

### 结果

2026-10-27，EPLAN 2026.0.3.25421，Electric P8，只在新复制的 `writes\LargeWrite` 上；日志在本机 `.local/spikes/s36p2/`，宏输出在 `%USERPROFILE%\EplanMcp\scratch\spikes\s36p2\`，都不入库。共 3 轮（r1–r3）。

| 检查 | 结果 |
|---|---|
| 插入 | `24VDC_Supply.ema` 插入第 939 页并提交，与第 1 部分一致 |
| 引用闭包 | 7 个占位符：路径文字（19201）、位置盒（20002）、电缆/连接定义点、端子、中断点（20002）、功能文本（20011）等。**部分被绑定对象在其他页有配对**（中断点另一端在 `=GAB…` 页，位置盒与第 2 页的位置盒关联）：模块影响闭包会跨页。变量 `Higher_Level_Function` 映射不到对象，`complete=false` |
| guard → execute 改动检测 | r1 的 mutate 在 guard 之后出错，`SafetyPoint.Rollback()` 本身抛出“不能重新恢复原有状态,因为其不再由系统管理”，原始异常未记录（读回仍等于运行前）。r2 起先记录原始异常、回滚失败即停并标为 unknown；但本示例闭包不完整，mutate 在建事务之前即拒绝——**这是产品应有的行为，但改动检测本身未能实测**，留给 M5/M7 用闭包完整的模块验证 |
| 写回 | 新建占位符 `S36P2` 与值集 → `WindowMacro.Create` 存为 scratch 下的新 `.ema`（约 220 KB）→ 重新打开（表一致）→ 再插入第 940 页（202 个对象）→ `ApplyRecord` → 逐项核对：布尔 20093 = true、功能文本 20011 三种语言（给全时全部保留）、部件引用 20100[1] = `10 20 206`、技术参数 20027 均一致 → 提交；约 18 秒 |
| 持久性 | 提交后关闭重开，三页的功能数与占位符数相同 |
| 失败回滚 | `--fail true`：`Transaction`/`SafetyPoint` 回滚后快照恢复；之后的撤销依次撤掉提交的写回和插入，没有空撤销步 |
| 撤销 | 一次撤销撤掉整次写回（第 940 页与新占位符消失），第二次撤销撤掉插入 |
| **文件残留** | 宏文件在项目事务之外：**回滚和撤销之后 `.ema` 都留在磁盘上**（r3 的回滚轮与提交轮各留一个文件）。工具必须自己删除失败产生的文件，或把它列为明确的产物 |

结论：写回宏再插入的全链可行，属性核对、持久性、回滚和撤销都满足；工具须处理事务外的宏文件。引用闭包可以枚举但会跨页且可能不完整，模块写入在闭包不完整时必须拒绝。guard 到 execute 之间的改动检测尚待闭包完整的模块实测。

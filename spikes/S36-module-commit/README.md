# Spike S36 第 1 部分：提交、持久性与撤销

> 状态：**第 1 部分完成**（2026-10-27）。对应阶段 3 设计第 7 节 `S36-module-commit`、第 9 节 S1，以及阶段 2 U2。只证明提交、持久性、撤销和回滚；引用闭包和写回宏（第 2 部分）未做，模块写入门槛仍未通过。

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

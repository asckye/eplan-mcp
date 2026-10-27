# Spike S42：文本、图形、移动与编辑器刷新

状态：**完成** 2026-10-27（r1–r6）。不加入解决方案，不注册产品工具。

## 问题

1. `Text.Create(page, MultiLangString, height)` 能否创建单语言、en_US + de_DE、语言无关文字？内容、语言、字高、位置、对齐与外框读回是否正确？
2. `Line.Create`、`Rectangle.Create + SetArea` 的端点、角点、插入点怎样对应？空页与已有内容页的栅格、尺寸、图框尺寸和外框能否读取？
3. 绝对移动文字、线、矩形及无部件 Function 后哪些属性改变？20214 属性文字是否跟随父功能，坐标差是否保持？已有 Group 的移动向量怎样影响成员？
4. 已打开编辑器在提交后是否自动显示变化？C13 的打开/重绘是否改变业务数据、持久设置或撤销历史？

## 方法

依据 [api-notes](../../docs/eplan/api-notes.md) A13/A14/A36/A39/A41/A49/A52/A53、[M8 设计](../../docs/design/2026-10-27-m8-drawing.md) 4.1 C09–C14、4.3、4.4、7.1、7.4；复用已验证的 [S38](../S38-drawing-texts-frames/README.md) 多语言读取和 [S40](../S40-connection-symbols/README.md)、[S41](../S41-symbols-without-part/README.md) 的独立 net481 插件、action 门、延后主线程调度、driver、`Evidence.cs`、sentinel 与锁文件。程序集 `EplanMcp.Spikes.S42`，action `EplanMcpSpikeS42`。

驱动和插件只接受现存 `%USERPROFILE%/EplanMcp/scratch/writes/LargeWrite.elk` 及完整同名 `.edb`，拒绝其他路径、UNC、链接/联接点；目标须是唯一打开且当前的项目。不代为打开、关闭或复制项目。日志只能是本 worktree 的新文件 `.local/spikes/s42/*.jsonl`，拒绝覆盖，通过日志路径定位仓库以避开插件影子副本。每步记录耗时、EPLAN 版本、系统消息增量和 design/User.*/settings 分组哈希差异，包含新增/删除文件。

`LockingStep` 是回调首个 EPLAN 操作，随后检查模态；action 门防重入，调度限 120 秒，driver 等日志限 150 秒。异常全部在回调边界捕获。每对象每请求缓存一份属性列表，最后释放一次；属性 ID/值、定义、多语言值及服务按所有权释放。所有新建或借用的 StorableObject、引脚、库、符号及变体均不 Dispose；不调用 `LockObject`，不初始化或关闭项目已有库，不导入主数据。

| step | 行为与事务 |
|---|---|
| `sentinel` | 要求没有 S42 页，在 9600–9699 选相邻两个空闲计数器，继承已有 Circuit 页结构，新建 `S42 sentinel`；`SafetyPoint → UndoStep → QuietModeStep` 独立提交。 |
| `create` | 要求哨兵及后一计数器空闲，新建 Circuit 页 `S42 graphics`。先读空页几何，再创建三段字高 2.5、BaseLineLeft 的 Text、一条斜 Line、一个 Rectangle。先走 `SafetyPoint → UndoStep → QuietModeStep`；仅创建阶段出现 LockingException 才记录 refused、显式回滚并确认快照恢复，然后按 S40 回退 `LockAllObjects → UndoStep → Transaction → QuietModeStep`。其他错误停止。 |
| `move` | 写前发现 S42 页对象和组闭包；若尚无样本，先从项目 `IEC_symbol/K` 发现有两个连接点的变体，再用 `LockAllObjects → UndoStep → Transaction → QuietModeStep`，在同一事务创建无部件线圈 `=GAA+A1-S42K1`、添加 20214 放置并移动。样本已存在时只移动，走 SafetyPoint 链。整步只有一次提交、一个撤销步，第一次撤销也会删除本步新建的 Function/20214。 |
| `view` | 显式 `Edit.OpenPageWithName(project.ProjectLinkFilePath, page.Name)`，记录返回 Page；再 `Edit.RedrawGed()` 并记录 bool。打开前、打开后和重绘后比较完整业务快照；无 SafetyPoint、UndoStep 或 Transaction，调用重绘时无外围 QuietModeStep。`undoHistoryVerified=false` 保留给维护者独立一次撤销验证。 |
| `readback` | 重新枚举全项目所有页、各页属性与放置（递归组和属性容器）、功能/引脚/部件引用，以及连接编号。与同一会话前序快照逐字段比较。 |

创建坐标均为新页栅格的倍数：三段文字 `(12,18/24/30)`，Line `(12,38) → (30,42)`，Rectangle `(40,18) → (65,32)`。语言无关采用 S38 的 `ISOCode.Language.L___`，精确比较实际存储语言，不用显示语言回退代替；若 API 拒绝则保留错误并回滚，不能视为支持。`inside_create` 直接记录创建返回对象，避免只依赖事务内枚举。`--fail true` 在完整读回后故意抛异常，显式 SafetyPoint.Rollback 或 Transaction.Abort，重新查询快照验证恢复，不关闭空撤销步；也支持 move 的半途失败对照。

移动按 OID 排序，依次把文字、线起点、矩形插入点移到绝对位置 `(20,20 + 8*i)` 倍栅格；若已在该点则改用该点加 `(10,4)`，确保再次测试仍有移动。Line 显式赋 StartPoint/EndPoint，保持原向量。记录请求、setter、before/after 与 changedProperties。线圈初始位置 `(75,30)`，20214 文字初始绝对位置 `(79,36)`，父功能目标 `(85,45)`，已在此处则目标 `(95,55)`；记录父/子位置、Location 减父位置、绝对/相对外框和显示串，不手动移动属性文字来掩盖跟随行为。`fixtureCreatedInThisStep` 区分新样本与已提交功能，需两轮都实测。若 S42 页已有 Group，个别对象移动后再对最外层组尝试 `Group.Move((2,3)*grid)`，记录包含嵌套组的成员前后状态；不存在时明确 skipped，不隐式建组。组阶段可能再次平移其成员，最终位置以该阶段读回为准。

快照读取每页全部 ExistingIds（含索引、空值和多语言），并记录每项放置的类型/位置/组、文本语言/字高/对齐、Line 端点、Rectangle 角点、Function/引脚及属性文字。空页尺寸、空默认文字外框等不可读几何保留 unavailable 错误，不捏造页界；其他核心读取失败直接停止。API 单位、毫米换算、画面原点和边界均标为未验证。普通 Line 只作图形，不生成连接；电气对象的自动线、逻辑连接更新、双项目导航、缺页、嵌套组及有图框样本仍须维护者另补覆盖，不能据此宣称 M8 全门槛通过。

## 运行步骤

仅维护者在 EPLAN 运行。先构建两个项目并离线检查参数：

```powershell
dotnet build spikes/S42-graphics-move-view -c Release -p:RestoreLockedMode=true
dotnet build spikes/S42-graphics-move-view/Driver -c Release -p:RestoreLockedMode=true
$driver = (Resolve-Path spikes/S42-graphics-move-view/Driver/bin/Release/net481/EplanMcp.Spikes.S42.Driver.exe).Path
& $driver --validate-only --step create --fail true --log .local/spikes/s42/parameters.jsonl
```

在关闭状态从非客户示例制作完整 LargeWrite 副本，保留 User.eod/eox 和原材料前后哈希。只打开此 scratch 副本并使其为当前项目；运行时不操作窗口、不启动其他写入者。代码更改后先退出 EPLAN 再构建，注销不能卸载 DLL。准备 S36/S40 使用的现存 `.local/tools/eplremote`：

```powershell
$env:EPLREMOTE_HOST = '<开发机本机 IPv4>'
$env:EPLREMOTE_PORT = '49152'
$remote = (Resolve-Path .local/tools/eplremote/bin/Release/net481/eplremote.exe).Path
$write = Join-Path $env:USERPROFILE 'EplanMcp/scratch/writes/LargeWrite.elk'
$run = Get-Date -Format yyyyMMdd-HHmmss
function S42($label, $step = 'readback', $fail = 'false') {
    & $driver --step $step --fail $fail --log ".local/spikes/s42/$run-$label.jsonl"
    if ($LASTEXITCODE -ne 0) { throw "S42 $label failed; inspect JSONL and EPLAN." }
}
S42 baseline
S42 sentinel sentinel
S42 before-create
S42 created create
S42 after-create
S42 opened view
# 编辑器已打开 S42 graphics。核对三种文本、线、矩形及实际坐标。
S42 moved move
# 保持页打开，不切页、不手动刷新；先观察提交的移动/20214 是否已显示，记录时间和截图。
S42 after-move
S42 moved-existing move
# 第二轮用 SafetyPoint 移动已提交的 Function/20214，不能只凭新建事务内的行为判断跟随。
# 同样先观察已打开页是否自动显示变化，不手动刷新。
S42 after-move-existing
S42 refreshed view
# 页仍打开：单独记录重绘之后是否才显示提交的变化、焦点/当前项目、模态提示。
& $remote --host $env:EPLREMOTE_HOST 'XEsUndoAction'
S42 undo-move
```

这一次撤销应直接恢复 after-move（Function/20214 仍在），证明 view 未占用撤销步；哨兵仍在。相符后才受控逐次撤销并 readback：下一次恢复 after-create（删除首轮 move 中新建的 Function/20214），再一次恢复 before-create，再一次恢复 baseline。任何不符即停，不盲目多撤销。比较 `view_check.openUnchanged/unchanged`、前后全部业务字段，并独立查看 User.*/settings/设计文件差异；哈希不同不等于业务不同，也不能忽略持久设置变化。

失败回滚与无空步在恢复后的副本另跑：

```powershell
S42 fail-baseline
S42 fail-sentinel sentinel
S42 fail-before
S42 fail-created create true
S42 fail-after
# 要求完整 inside_create、rolled_back.deliberate=true、failure_rollback_check.restored=true。
& $remote --host $env:EPLREMOTE_HOST 'XEsUndoAction'
S42 fail-undo
# 应直接删除 sentinel，恢复 fail-baseline。
S42 move-fail-sentinel sentinel
S42 move-fail-created create
S42 move-fail-before
S42 move-failed move true
S42 move-fail-after
# move-fail-after 应等于 move-fail-before，随后一次撤销直接删除 graphics 页，保留 sentinel。
& $remote --host $env:EPLREMOTE_HOST 'XEsUndoAction'
S42 move-fail-undo
```

持久性在恢复/重制的完整副本单独跑 sentinel → create → move → readback；维护者关闭再打开，readback 比较名称、语言、几何、零部件和属性显示。OID/运行时属性串仅作会话证据，按业务键比较；不依赖重开后的撤销历史：

```powershell
& $remote --host $env:EPLREMOTE_HOST 'XPrjActionProjectClose'
& $remote --host $env:EPLREMOTE_HOST "ProjectOpen /Project:`"$write`" /Quiet"
S42 reopened
```

日志/截图/项目只留本地；`done.data.ok=true` 不表示显示、无空步或全部覆盖已通过。超时、缺 done、读回错误时停止，不自动重试写入。正常完成 driver finally 注销；结果未知时保留注册，确认回调结束后再手工注销：

```powershell
& $remote --host $env:EPLREMOTE_HOST 'EplApiModuleAction /unregister:"EplanMcp.Spikes.S42" /Quiet'
```

## 结果

EPLAN 2026.0.3.25421，Electric P8，只在新复制的 `writes\LargeWrite` 上；日志与截图在本机，不入库。

| 轮次 | 结果 |
|---|---|
| r1–r2 | 页属性快照把 `ExistingIds` 中索引为 0 的带索引属性（1122）当普通属性读，`IndexedPropertyException`（S063172）；改为失败时按索引读取 |
| r3 | `create` 在 `SafetyPoint → UndoStep → QuietModeStep` 中提交：`Text.Create` 三段（只有 en_US、en_US+de_DE、语言无关 `??_??`），字高 2.5、BaseLineLeft，`Location` 与请求一致；`Line.Create` 两端点、`Rectangle.Create` + `SetArea` 两角读回一致。`view`：`Edit.OpenPageWithName(项目路径, 页名)` 返回该页并在编辑器中打开，`RedrawGed()` 返回 true；项目文件哈希不变，之后一次撤销撤销的是此前的 `create`，**打开页不留撤销步**。唯一的快照差异是 634 个部件引用的 `ObjectIdentifier` 全部改号（数量与内容不变），同时 EplLog 记录打开了部件库与宏库：部件引用的对象号不是稳定标识 |
| r4 | 编辑器打开着该页时提交 `move`，截图显示**编辑器自动刷新**：文字、线、矩形都在新位置，新放的线圈也出现，无需手工刷新。只显示语言无关的那段文字：**只有 en_US 或 en_US+de_DE 的文字在中文显示语言下不显示**。线圈的 20214 标识没有出现在画面上 |
| r5 | 查明原因：`PropertyPlacement.Location` **是相对父符号的偏移**（绝对外框 = 父位置 + Location）；r4 按"父位置 + 偏移"设置，文字被画到页外。改为只设偏移后截图中 `-S42K1` 显示在线圈右上方，线圈移动后随之移动（Location 不变）。第一次撤销只撤销 `move`（线圈消失、图形保留），第二次撤销删除图形页；`create --fail true` 回滚无残留，下一次撤销直接删除哨兵页，没有空撤销步 |
| r6 | 提交 `create` 和 `move` 后关闭重开：功能逐字段相同，页上唯一差异是对象标识字符串的会话前缀（`53/4/…` → `84/4/…`） |

移动：文字和矩形用 `Placement.Location`，直线须同时设 `StartPoint`、`EndPoint`；功能用 `Location`，其属性放置随之移动。页上没有组，`Group.Move` 未测。快照每步约 40–60 秒（读取全部页属性），产品读取须限定范围。

结论：
1. 自由文字、直线、矩形可在 `SafetyPoint` 中创建和移动，一步撤销，失败回滚无空步，重开持久。
2. 自由文字要包含项目的显示语言（本机为中文），或用语言无关文字，否则画面上不显示；工具须在规格中写明并读回。
3. 属性放置的位置用相对父符号的偏移；A53/S41 的"父位置 + 偏移"写法错误，已在 S41 更正。
4. `Edit.OpenPageWithName` 可作为只读的"打开页供观看"入口：不写项目文件、不留撤销步；已打开的编辑器在提交后自动刷新，`RedrawGed` 不是必需的。

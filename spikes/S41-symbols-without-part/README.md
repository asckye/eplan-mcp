# Spike S41：无部件库符号与属性显示

状态：**完成** 2026-10-27（r1–r2）。不加入解决方案，不注册产品工具。

## 问题

1. 从项目已有 `IEC_symbol` 中发现线圈、常开触点和另一种普通两端符号，能否不分配部件放置、设置完整 `Function.Name` 并保存？
2. [S20](../S20-symbol-placement/README.md) 的 `20058` 显示缺少 `-`；`20214` 或其他有目录依据的标识属性能否正确显示前缀？默认属性摆放与手工新增摆放有什么区别？
3. 属性文字的 `Location` 是相对还是绝对坐标？明确偏移、对齐后，标识、功能文本 `20011` 和技术参数 `20027` 能否显示且不压在符号上？多语言是否保留？
4. 整批创建、命名、属性赋值与摆放能否原子 Abort、一次撤销且不留空步，并在关闭重开后保持？

## 方法

依据 [api-notes](../../docs/eplan/api-notes.md) A13/A26/A36/A39/A41/A52 和 [M8 设计](../../docs/design/2026-10-27-m8-drawing.md) 4.1 C07/C08、4.2、7.1、7.3。从已在 EPLAN 验证的 [S40](../S40-connection-symbols/README.md) 复制独立 net481 add-in、action 门、延后主线程调度、Remote Client driver、`Evidence.cs` 路径/分组哈希检查和锁文件；程序集为 `EplanMcp.Spikes.S41`，action 为 `EplanMcpSpikeS41`。

插件与驱动只接受现存的 `%USERPROFILE%\EplanMcp\scratch\writes\LargeWrite.elk` 及完整同名 `.edb`，拒绝 UNC、链接/联接点和其他路径。只允许此项目打开且为当前项目；不打开/关闭项目、不切换当前项目。输出仅限本 worktree 的新文件 `.local/spikes/s41/*.jsonl`，拒绝覆盖；从日志路径定位 worktree，避免影子副本。运行期间不要操作 EPLAN 或启动其他写入者。

回调第一步是 `LockingStep`，随后检查模态窗口；调度限 120 秒、驱动等日志限 150 秒。全部回调异常在内部捕获。库/符号/变体/引脚以及新建或借用的所有 `StorableObject` 都不 `Dispose`；每对象每次主线程调用只获取一份属性列表，写入和前后读回复用，最后释放一次；属性值、属性 ID、多语言值和服务另行释放。不调用 `LockObject`、`GetAllCurrentValues`，不访问授权，不初始化/关闭系统库、不导入或修改主数据。

| step | 行为 |
|---|---|
| `discover` | 只读枚举项目库，仅从 `IEC_symbol` 的 `Symbol.Type == Function` 中取有两个连接点的变体。记录名称/编号/类型/变体、引脚方向与相对位置、20188 定义编码及 20115 多语言类别；优先用项目已有放置实例的 `FunctionCategory`，否则尝试解析 20188 的类别前段。线圈可回退到实际发现的 `K`（A26），创建后仍核对类别；常开触点必须发现 `NoContact`。第三种排除线圈/常开触点及已识别的端子/PLC 类。按符号名、变体号排序，记录三个 `selected` 和缺样本状态。可选 `--page '<精确完整页名>'` 只读检查既有页。 |
| `sentinel` | 要求没有 S41 页，在 9500–9599 找相邻两个空闲计数器，新建 Circuit 哨兵页 `S41 sentinel`，通过 `SafetyPoint → UndoStep` 独立提交。 |
| `place` | **先发现和预检，再写入**；要求三个样本、哨兵、后一个空闲计数器且全项目没有 `-S41` 标识。复制已有 Circuit 页的结构建 `S41 place` 页；一次 `Project.LockAllObjects → UndoStep → Transaction → QuietModeStep` 完成三个无部件功能及全部属性摆放，事务内读回后才提交。 |
| `readback` | 重新枚举全项目页/功能/部件引用/连接编号和 S41 页；记录完整标识、VisibleName、类别、库/符号/变体、坐标、引脚代号、20011 全部语言、20027 及所有页内属性摆放。 |

完整标识依次为 `=GAA+A1-S41K1`、`=GAA+A1-S41K2`、`=GAA+A1-S41X1`，插入点为 `(15,35)`、`(45,35)`、`(75,35)` 倍新页栅格。核对实际全名、类别、两个引脚、位置，创建后及最终读回均要求 `ArticleReferences.Length == 0`。不调用 `DeviceService`、不创建部件引用、不生成连接。页的可见边界、API 单位与毫米关系仍待实跑；`Function.Name` 结构不符即回滚，不静默接受被丢弃的结构。

每个功能分别在创建后（`default_after_create`）、赋值后且尚未手工加摆放（`default_after_values`）及整批完成后（`inside_transaction`）记录默认和新增 `PropertyPlacement`。枚举页及返回对象，并递归遍历 Group/属性摆放容器，避免 A13 的提交前 finder 盲点；按对象标识去重。每项包含属性 ID/索引、`GetDisplayString()`、Location、Justification、Height、可见性、父对象/父坐标、坐标差，以及绝对/相对包围框；空默认文字无法计算包围框时记录错误。

显示候选来自 [属性字典](../../catalog/eplan-2026.0.3/data-dictionary.jsonl)：

| ID | 字典名称 / 对照用途 |
|---|---|
| 20058 | Function/FUNC_DEVICETAG_FULLNAME，重现 S20 |
| 20214 | Function/FUNC_DEVICETAG_FULLNAME_WITHSEPARATOR |
| 20213 | FunctionBase/FUNC_DEVICETAG_FULL_WITHSEPARATOR |
| 20211 | FunctionBase/FUNC_DEVICETAG_MAIN_WITHSEPARATOR |
| 20212 | FunctionBase/FUNC_DEVICETAG_NESTED_WITHSEPARATOR |
| 20215 | FunctionBase/FUNC_IDENTDEVICETAGWITHOUTSTRUCTURES_WITHSEPARATOR |
| 20010 | FunctionBase/FUNC_VISIBLEDEVICETAG |
| 20011 / 20027 | Function/FUNC_TEXT / FUNC_TECHNICAL_CHARACTERISTIC |

逐项 `new PropertyPlacement().Create(function, id)`，显式设 `BaseLineLeft`、高度 2.5、可见。九行偏移依次为 `(4,6)`、`(4,4)` … `(4,-10)` 倍栅格。先将 `Location` 设为偏移，记 `offset_only`；随后设为“插入点 + 偏移”，记 `parent_plus_offset` 并保留此最终请求。**两种请求的坐标与包围框用于确定真实坐标语义；不能仅凭 setter/getter 相等宣称位置正确。** `display_summary` 分别记录是否以 `-` 起始、是否含本次带前缀标识；不修改计算标识属性，不用自由文字伪造前缀。

20011 从已有 `MultiLangString` 合并；仅当德语条目不存在时增加保留语言样本，再更新 en_US，记录初值/合并前后全部语言并逐项验证其他语言不变。20027 分别设为 `DC 24 V`、`NO 13-14`、`S41 two-pole data` 并读回。引脚代号只读记录，不以技术参数字符串代替真实引脚编号。

A13 与新鲜 A52 已记录 `Function.Create` 在 `SafetyPoint` 中的 S063176 负例；本探针直接复用已验证的 Transaction 路径，不重复失败尝试。`--fail true` 只对 place 生效，全部摆放及事务内读回后故意抛异常，显式 `Transaction.Abort`，不 `CloseUndo`；成功顺序为 `Commit → CloseUndo`。重新查找后 `failure_rollback_check.restored` 必须为 true，仍须用下一次撤销直接删除哨兵证明没有空步。

前后磁盘清单包含新增/删除文件，分 `design`、`user`（User.*）、`settings`（Settings.*），只排除 `.lck`；业务恢复与字节恢复分别判读，不强制保存/刷新。系统消息仅保留开始计数，结束新建集合立即复制。此轮只覆盖一个页结构和每类首选变体；其他结构/变体、重名/缺符号/未知属性负例、已有对象的 SafetyPoint 属性回滚仍是 7.3 的覆盖缺口，不能据本探针单独宣布产品分支可用。

## 运行步骤

从仓库根目录构建；本轮仅构建，不运行 EPLAN。所有 EPLAN 引用 `Private=false`，不分发 EPLAN DLL。

```powershell
dotnet build spikes/S41-symbols-without-part -c Release -p:RestoreLockedMode=true
dotnet build spikes/S41-symbols-without-part/Driver -c Release -p:RestoreLockedMode=true
$driver = (Resolve-Path spikes/S41-symbols-without-part/Driver/bin/Release/net481/EplanMcp.Spikes.S41.Driver.exe).Path
& $driver --validate-only --step place --fail true --log .local/spikes/s41/parameters.jsonl
```

维护者先在关闭状态从非客户示例制作完整 LargeWrite scratch 副本，保留 User.eod/eox，并保留原材料前后哈希。不重复打开已打开项目。手工在 EPLAN 只打开此副本且使其为当前项目；代码更改后须退出 EPLAN 再构建，注销不能卸载已加载 DLL。准备 S36/S40 使用的现存 `.local/tools/eplremote`；以下连接、撤销、关闭和重开由维护者逐条执行，driver 不代执行。

```powershell
$env:EPLREMOTE_HOST = '<开发机本机 IPv4>'
$env:EPLREMOTE_PORT = '49152'
$remote = (Resolve-Path .local/tools/eplremote/bin/Release/net481/eplremote.exe).Path
$write = Join-Path $env:USERPROFILE 'EplanMcp/scratch/writes/LargeWrite.elk'
$run = Get-Date -Format yyyyMMdd-HHmmss
function S41($label, $step = 'readback', $fail = 'false') {
    & $driver --step $step --fail $fail --log ".local/spikes/s41/$run-$label.jsonl"
    if ($LASTEXITCODE -ne 0) { throw "S41 $label failed; inspect JSONL and EPLAN." }
}
S41 catalog discover
# 核对 selected 三种样本；可选 --step discover --page '<既有页完整名>'。
S41 baseline
S41 sentinel sentinel
S41 before
S41 placed place
S41 live
# 在 EPLAN 查看测试页；逐属性核对带 - 的标识、两项属性、实际文字位置和默认文字。
& $remote --host $env:EPLREMOTE_HOST 'XEsUndoAction'
S41 undo1
# undo1 应等于 before：测试页消失、哨兵仍在；相符才撤销第二次。
& $remote --host $env:EPLREMOTE_HOST 'XEsUndoAction'
S41 undo2
```

`undo2` 应等于 baseline，不多撤销。失败/无空步在恢复的同一副本另跑：

```powershell
S41 fail-baseline
S41 fail-sentinel sentinel
S41 fail-before
S41 fail-placed place true
S41 fail-after
# 核对 inside_transaction 完整、rolled_back.deliberate=true、restored=true、fail-after 等于 fail-before。
& $remote --host $env:EPLREMOTE_HOST 'XEsUndoAction'
S41 fail-undo1
```

这一次撤销应直接删除哨兵，恢复 fail-baseline；首个不符即停，不追加撤销掩盖空步。持久性另起一轮，不依赖重开后的撤销历史：

```powershell
S41 persist-sentinel sentinel
S41 persist-placed place
S41 persist-beforeclose
& $remote --host $env:EPLREMOTE_HOST 'XPrjActionProjectClose'
& $remote --host $env:EPLREMOTE_HOST "ProjectOpen /Project:`"$write`" /Quiet"
# 确认重开成功且当前为 LargeWrite，处理维护者可见提示后继续。
S41 persist-reopened
```

比较关闭前后完整标识、库/符号/变体、属性语言、零部件引用、全部默认/新增摆放的显示串、坐标/对齐和引脚；运行时 OID 不作跨会话业务名。截图/导出图与日志仅留 `.local/spikes/s41/`，不能把 `done.data.ok=true` 当成显示正确或 M8 门槛通过；它只表示探针完成。任何超时、缺 done 或失败都停止，不自动重试写入。正常完成在 driver 的 `finally` 注销；结果未知时保留注册供检查，确认调用结束后才手工清理：

```powershell
& $remote --host $env:EPLREMOTE_HOST 'EplApiModuleAction /unregister:"EplanMcp.Spikes.S41" /Quiet'
```

## 结果

EPLAN 2026.0.3.25421，Electric P8，只在新复制的 `writes\LargeWrite` 上；日志在本机 `.local/spikes/s41/`，不入库。

- 选用的符号（IEC_symbol，无部件）：线圈 `K`（20，变体 0，类别 Coil）、常开触点 `O_EL`（1262，NoContact）、两端符号 `AS`（1687，DataRecorder）。按 A13/A52 走 `LockAllObjects → UndoStep → Transaction`，一次提交放置 3 个功能，`ArticleReferences` 都为 0。
- 名称 `=GAA+A1-S41K1` 等按请求设置；`VisibleName` 读回为空。功能文本 20011 只写 en_US，原有 de_DE 保留；技术参数 20027 写入。
- `Function.Create` 自带的默认属性放置只有 20029（索引 2）一个，显示为空。
- 用 `PropertyPlacement.Create(function, id)` 新建、设 `Location` 为"父对象位置 + 偏移"后，读回位置与请求完全一致（例如偏移 (16,16)），`Justification` 为 BaseLineLeft、字高 2.5：

| 属性 | 显示 |
|---|---|
| 20058 | `S41K1`（不带 `-`，与 S20/A26 相同） |
| 20214、20213、20211、20215 | `-S41K1`（带 `-`；与页结构相同的 `=GAA+A1` 不显示） |
| 20212、20010 | 空 |

- 失败回滚：读回后故意抛异常，`Transaction.Abort` 后快照完全恢复；之后两次撤销分别撤销此前的放置与哨兵页，没有空撤销步。
- 持久性：放置提交后关闭再打开项目，读回逐字段相同。
- 未做画面核对（`visualVerified=false`）：显示串来自 `GetDisplayString()`，画面位置与字形由 M8 的 S42/整页验收确认。

结论：无部件符号可以放置，标识用 **20214**（`FUNC_DEVICETAG_FULLNAME_WITHSEPARATOR`）显示即带 `-` 前缀，位置按"父对象位置 + 偏移"精确设置；S20 遗留的"缺 `-`、压在插入点"问题有解。`eplan_device_place` 的符号来源分支可按此实现（M8 设计 D03）。

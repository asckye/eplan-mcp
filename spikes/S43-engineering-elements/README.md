# Spike S43：工程绘图元素

状态：**完成**（位置盒的结构继承除外）2026-10-27，r1–r5。程序集 `EplanMcp.Spikes.S43`，action `EplanMcpSpikeS43`；不加入解决方案，不注册产品工具。

## 问题

- C15：两个新页上的同名中断点能否建立真实配对，何时得到 EPLAN 计算的引用文字与对象 ID？
- C16：发现的库符号能否创建真正的 LocationBox，设置 `+S43BOX` 和虚线；后放的内部功能是否继承结构？
- C17/C18：跨三根竖线的电缆定义能否形成 CableConnections，并明确保留芯号及 BK/BU/GNYE？
- C19：新端子排 `-S43X` 的三个单端子能否正确归属、编号并分割竖直连接？
- C20/C21：PLC 盒 `-S43KF` 的三个数字输入能否保留地址、符号地址、文本和父关系；NO/NC 的实际 Pin 是否为 13/14、11/12？
- 每条写路径的 Transaction.Abort / SafetyPoint.Rollback、提交后一次撤销、失败无空撤销步和关闭重开持久性是否成立？

## 方法

依据 [api-notes](../../docs/eplan/api-notes.md) A13/A14/A24/A36/A39/A41/A52/A53、[M8 设计](../../docs/design/2026-10-27-m8-drawing.md) 4.1 C15–C21、4.2、7.1、7.5，以及现有 [cable_generate](../../docs/tools/eplan_cable_generate.md)、[terminal_strip_get](../../docs/tools/eplan_terminal_strip_get.md)、[plc_get](../../docs/tools/eplan_plc_get.md) 的证据边界。复用已实跑 [S40](../S40-connection-symbols/README.md) / [S41](../S41-symbols-without-part/README.md) 的独立 net481 add-in、Evidence.cs、action gate、延后主线程回调和 Remote Client driver。

driver 和插件均限定现存 `%USERPROFILE%/EplanMcp/scratch/writes/LargeWrite.elk` 与完整同名 `.edb`；只允许此项目打开且为当前项目，拒绝 UNC、链接、联接点。输出仅为 worktree 下新建的 `.local/spikes/s43/*.jsonl`，拒绝覆盖。driver 不开关项目、不切换当前项目；在已确认 action 完成后于 finally 注销插件，未知结果禁止自动重试。回调 120 秒、driver 等日志 150 秒；回调第一步 LockingStep，随后检查模态窗口，异常不能逸出主线程。

所有标签、页结构与文本均独立虚构，不读取客户页文字作为模板。发现只枚举项目已加载的 SPECIAL / IEC 库、项目标准功能定义库及既有功能的类别，不复制既有设备名称/文本，不初始化、关闭、导入或改写库。每次元素放置自动先 discover，写范围内不再发现符号；缺少该组候选即拒绝该组，其他组仍可独立执行。每组一个新页，中断点另需一个对端新页；页号从连续空闲的 9700–9799 段预留九个，电缆先生成变体另占一页。

| step | 独立提交的内容 / 读回 |
|---|---|
| `discover` | 只读记录库、符号号、类型、变体、定义编码/文本和引脚方向。各角色分别 selected/missing，不要求所有组都有候选 |
| `sentinel` | 单独新页哨兵；作为撤销和失败无空步的对照 |
| `interruption` | 两页各一个 `-S43LINK` 原生 InterruptionPoint；请求页结构全名，比较并记录完整 Name 字符串及确切允许形式（含 r2 观察到的 `=-S43LINK`）。结构字段分解仅作诊断，不能否决完全相等的完整名称；其他名称拒绝。各接短竖线到一个两端符号，读取真实配对与计算引用 |
| `locationbox` | 真正 LocationBox，先设边界与 `+S43BOX`，GetGraphics 暴露 Pen 时设 StyleId=1（随安装 XML 定义的 dash）；再放内部功能，只给短显示标识，记录结构继承是否发生及盒图形 |
| `terminals` | `+S43LAB-S43X` 与 1..3，明确设排为主功能。事务内严格比较类别、完整名、20030 序号；父对象为 null 时记录待提交解析，非空错误父对象仍拒绝。提交后强制复查原生排成员与父关系，仍不符则记录“已提交但读回失败”，不自动重试/撤销。只沿图形引脚布线，保留内部跳线引脚的读回 |
| `cable` | 原比较路径：同一放置步骤创建三根符号竖线与 `+S43LAB-S43W1`。从页枚举、SubFunctions 和 ConnectionDefPoints 收集可见定义点；记录实际所有权/数量，对已归属点对齐 Name/VisibleName。事务内暂时为 0 不再拒绝，另行提交并 generate |
| `cable_wires` / `cable_line` | 新比较路径：cable_wires 在独立页只放六个端点符号；单独 generate 后，cable_line 在该页三个已验证的逻辑连接上创建 `+S43LAB-S43W2` 及关联定义点；再单独 generate，后续 cores 选组 cable_wires |
| `plc` | `=S43+S43LAB-S43KF` 与三个 DI 点。按目录与 EplanPlcReader 使用 20400 地址、20402 手工符号地址、20403 计算符号地址（只读）、20406 插头、20407 通道、20011 功能文本、20405 数据类型（只读）；不对 PLC 点读写 20030。连接点代号通过索引 20022 和 Pin.Name 验证，保留内部 Pin.Designation。项目库发现真实 DI 定义后匹配图形引脚数，提交后复查父关系和地址 |
| `contacts` | 原生 NO/NC，先写 FUNC_CONNECTIONDESIGNATION=20022 的 1 起始索引，未生效时才尝试 20075/20076（3/4 对应 20226/20227；安装 XML 将这些固定别名标为只读）。不再写 20038。记录各属性、索引、请求值、Pin.Name、内部 Pin.Designation、20038/20028 与 workedVariant；实际 Pin.Name 和索引 20022 必须精确为 13/14、11/12 |
| `generate --group <组>` | 只对已提交的该组页（中断点为两个页）调用 Generate.Connections；另一个独立 SafetyPoint 提交和撤销步（A52） |
| `bind --group cable\|cable_wires` | 新增独立 Transaction 提交：仅在三个端点对、图形相交及各定义点原生所有者均明确时，对齐定义点名称，并只给这三条连接赋 Connection.CableNameParts；验证连接实时 20005 与定义点 Name/VisibleName/所有者，其他 S43 连接不变。允许 CableConnections 待提交后单独 generate 更新；支持 --fail true |
| `cores --group cable\|cable_wires` | 只接受已提交/生成后恰为三个明确连接 ID 的 CableConnections，并校验页、端点、图形相交、实时电缆名称及定义点所有者/名称。缺失则逐项记录 cores_membership 比较及 missing，指示先 bind → generate；不在芯线事务中临时绑定后马上拒绝并回滚。验证成立才设芯号/颜色、添加本地模板，并且只调 Function+Connection[] 重载 |
| `readback` | 只读 S43 页上的原生类型、属性、引脚、图形、配对 ID、端子/PLC 父子关系、芯线与显示；项目范围仅记录对象 ID 集合及分组文件哈希 |

Function / Terminal / PLC / Cable 等创建均用 `LockAllObjects → UndoStep → Transaction → QuietModeStep`；不重试已在 A13/A52 验证失败的 SafetyPoint 创建。sentinel 和 generate 用 `SafetyPoint → UndoStep → QuietModeStep`。每个写 step 均在其事务内读回，`--fail true` 在读回后故意抛错、显式 Abort/Rollback，不 CloseUndo；随后按业务快照比较恢复，另记设计/User/设置文件哈希，不把回滚称为字节恢复。每对象每回调只获取一次 Properties 并复用，辅助属性值/ID/多语言对象分别释放；StorableObject、符号库、变体和 Pin 从不 Dispose，不调用显式 LockObject。

诊断通过 Evidence.Error 保留真实失败的 stack、inner 及聚合异常；预期的缺失元数据仅记 unavailableProperty。普通写入仍执行发现，但仅输出候选选择与数量摘要，完整库存只在显式 discover 输出；stage 只记请求、比较值及对象状态差异。before 保留首份业务快照，inside_transaction / after 以 format=diff 和 JSON Pointer 路径记录相对于上一份快照的变化，beforeExists/afterExists 区分删除和 null；显式 readback 仍输出完整快照。hashes_after 也只输出变化。CableNameParts 首份列表标为 cableNamePartsFirstRead，后续变更另看实时标识字段和 CableConnections。

读回通过 ReadRecord 按字段隔离：SymbolVariant、FunctionDefinition、Page、属性列表/值、引脚、连接、图形笔和集合均允许缺失；原字段保留，真实空值/空集合项记 JSON null，已知空集合仍为 []。某 getter 或集合项投影失败时，该字段/项为 null，并在同一对象的 readErrors 中记录字段路径及堆栈，继续读取其他字段/项。提交后必需业务关系不符的显式校验规则保持不变，不把缺失证据当作已验证成功。

发现阶段逐个检查既有 Function 的放置状态、SymbolVariant、库名/符号名、类别和 FunctionDefinition；缺少任何映射键或定义时输出 discovery_function_skipped（只含对象 ID、类型和缺失键，不回显既有设备名称/文本），不把它交给 GroupBy/ToDictionary。库、符号、变体、定义及连接点集合的空项也逐项跳过/记录；选择仅使用已捕获的有效引脚坐标和方向，维持原确定性排序。发现不完整时记录 discovery_incomplete 并清空选择，后续 Preflight 仍拒绝缺少必需候选的写入。

回滚生命周期：BeginWrite 在修改前记下既有 S43 对象/连接与已缓存列表的所有者。发生异常或故意失败时，所有原生对象诊断只在 Abort 前完成；PropertyListCache 在 Abort/Rollback 前释放并移除新建所有者的全部缓存列表（含隐式连接定义点、连接和名称部分），抑制这些已清理包装的后续终结器，随后才删除原生对象。既有所有者的列表继续复用，回滚后不重新获取；新建对象不在 Abort 后读回或释放，清理失败也不会把包装留给 Probe.Dispose 再次释放。SafetyPoint 回滚和 Transaction.Abort 均采用该顺序。离线模拟检查覆盖删除后释放、二次释放、释放失败和提交后读回；真实堆损坏是否消除仍待实跑。

已核对 EplanPropertySetSession.Write：生产适配器对赋入属性列表的自有 PropertyValue（包括多语言值）同样使用 using，因此保留 Set / SetText / 连接点代号写入的该模式。本轮修复针对失效所有者的缓存列表释放顺序，不通过任意泄漏所有输入值绕开问题。

r1 本地日志已逐项回看：电缆 generate 实际产生三个连接，CableDefinitionLine 均指向新电缆，但 CableNameParts 为空，定义点名称仍为 `+`；PLC 符号日志只有 general/bus/display 等定义，不能靠放宽 DI 文本匹配将其称为数字输入；端子错误缺堆栈，无法从 r1 证明具体 null 位置。以上修订仅经离线构建/参数验证，需下一轮实跑证实，不提升支持状态。

r2 本地日志确认：中断点完整 Name 与请求相等，错误来自结构属性 1100 将文本分解为 S43+S43LAB、1200 为空，被旧比较误拒绝；端子类别/完整名/序号正确，但事务内父对象 null；电缆创建事务内页枚举没有定义点，因此定义线被回滚，后续 cores 实际找不到电缆。PLC 点写入 20030 抛 S063109，Abort 后日志中断并发生维护者报告的 0xc0000374；未取得证据前不声称堆损坏根因或修复已经实机证实。

r3 日志中 sentinel、中断点创建和 generate 成功，后续端子在 ReferenceData 读回时空引用；其后发现阶段在既有 Function 的 SymbolVariant 库名/符号名取键时空引用。本轮仅修订发现、读回及诊断的空值处理，保留创建/生成/事务与回滚清理路径；不将单轮未复现崩溃推广为所有生命周期门槛通过。离线检查包含已放置但无符号变体、空定义/集合/集合项、getter/投影/枚举失败，以及后续字段和对象继续读取。

r4 日志确认 sentinel、中断点/端子/竖线/定义线及各自 generate、locationbox 步骤成功且维护者未观察到崩溃。contacts 写 20038 后索引 20022 已显示 13/14，但内部 Pin.Designation 仍为 1/2；PLC 第二点同样因内部编号 1 与请求 2 不等被旧校验拒绝。安装 XML 的 Pin.Name 明确返回实际代号并等于父功能对应索引的 FUNC_CONNECTIONDESIGNATION；Pin.Designation 是内部只读标识，因此保留原 designation 字段并新增 name 字段，不把内部编号改写为业务代号。

r4 电缆顺序确为 cable_wires 提交（19:03:37Z）→ generate 提交（19:03:42Z）→ cable_line 提交（19:03:46Z）→ generate 提交（19:03:51Z）。页 `=S43+S43LAB/9708` 的电缆 69853 有三个图形相交，生成后的连接 72615/72616/72617 分别关联定义点 69854/69855/69856，各点 CableDefinitionLine 都是 69853；但定义点 Name 为 `+`，电缆 Name 为 `+S43LAB-S43W2`，CableConnections 为 []。芯线事务内对齐名称并赋 CableNameParts 后，连接实时 20005 已等于电缆名，但成员枚举仍为 []，随后整个绑定被回滚。原 cableNamePartsFirstRead 是缓存的首份读值，不能据其仍为空推断 setter 没有生效。新增 bind 将这些定向赋名独立提交，再单独 generate，之后才尝试芯线分配；这条修订路径尚待实跑。目录不提供 Connection.Cable，诊断使用 Page、CableDefinitionLine、各定义点所有者、实时标识与 CableConnections，区分图形相交、名称绑定和成员枚举；不猜测提交后仍为零的原生原因。

随安装 XML 明确：20028 只读，20022 是 1..1000 索引的输入属性，20075/20076/20226/20227 为只读别名；20038 保留全部代号读回。31004 是字符串颜色/编号，31005 是整数图形线色；Pen.StyleId=1 为 dash；CableConnections 要求电缆和定义点 Name/VisibleName 一致，Connection.CableNameParts setter 用于指定已有电缆归属。仍不能由目录证明成员枚举更新时机、命名/结构传播的实际结果、引用重算时机、模板匹配及副作用、PLC 原生表格和显示、回滚/撤销/持久性。PLC 放置真实属性文字，不以静态文字冒充原生属性表；完整 7.5 的负例、移动/嵌套、范围外业务字段和画面核对仍待实跑。

## 运行步骤

仓库根目录构建两个项目；EPLAN 引用均 Private=false，不分发 EPLAN DLL：

```powershell
dotnet build spikes/S43-engineering-elements -c Release -p:RestoreLockedMode=true
dotnet build spikes/S43-engineering-elements/Driver -c Release -p:RestoreLockedMode=true
$driver = (Resolve-Path spikes/S43-engineering-elements/Driver/bin/Release/net481/EplanMcp.Spikes.S43.Driver.exe).Path
& $driver --validate-only --step generate --group interruption --fail true --log .local/spikes/s43/check.jsonl
```

下列 EPLAN 操作仅供维护者后续逐步执行，本轮未运行。r2 已发生堆损坏，下一轮使用关闭状态下重建的完整 LargeWrite 副本并保留 User.eod/eox；先验证受控失败路径日志依次出现 before_rollback_cache_cleanup、rolled_back、done，再验证提交。核对原材料前后哈希，手工只打开此 scratch 并使其为当前项目。修改代码后重启 EPLAN 再加载新版，避免已加载 DLL 不能卸载的问题。运行期间不要操作 EPLAN 或启动其他写入者。

```powershell
$env:EPLREMOTE_HOST = '<本机 IPv4>'
$env:EPLREMOTE_PORT = '49152'
$run = Get-Date -Format yyyyMMdd-HHmmss
function S43($label, $step = 'readback', $group = '', $fail = 'false') {
    & $driver --step $step --group $group --fail $fail --log ".local/spikes/s43/$run-$label.jsonl"
    if ($LASTEXITCODE -ne 0) { throw "S43 $label failed; inspect JSONL and EPLAN before continuing." }
}
S43 catalog discover
S43 baseline
S43 sentinel sentinel
S43 before
S43 interruption interruption
S43 interruption-generated generate interruption
S43 interruption-live
# 其他组单独选一组运行：locationbox / terminals / cable / plc / contacts。
S43 cable cable
S43 cable-generated generate cable
S43 cable-bound bind cable
S43 cable-bound-generated generate cable
S43 cable-cores cores cable
S43 cable-live
# 电缆先生成比较路径；每行均是独立事务/提交，不能合并 generate。
S43 wires cable_wires
S43 wires-generated generate cable_wires
S43 definition cable_line
S43 definition-generated generate cable_wires
S43 definition-bound bind cable_wires
S43 definition-bound-generated generate cable_wires
S43 definition-cores cores cable_wires
S43 definition-live
```

每组均单独做有效提交、撤销、失败和关闭重开轮次；缺符号或字段不匹配只阻塞该组，先核对日志和恢复状态，再独立运行别组。不要把示例命令整段当作失败后仍继续的批处理。

1. 新鲜副本上 baseline → sentinel → before → 元素放置 → readback。在编辑器查看实际原生图形、Pin、文字和关系；需要连接的组另调 generate 并读回，电缆再调 cores。
2. 在 action、锁和事务完全结束后，由维护者逐次执行独立 `XEsUndoAction`。原路径依次撤 cores、绑定后 generate、bind、放置后 generate、元素放置；先生成变体依次撤 cores、第三次 generate、bind、第二次 generate、cable_line、第一次 generate、cable_wires。每次核对对应快照，sentinel 必须保留；确认相符才继续。最后受控撤 sentinel 应恢复 baseline，首个不符即停，不盲目多撤销。
3. 失败轮在新鲜副本重新建 sentinel；针对任一写 step 加 `--fail true`。`failure_rollback_check.restored` 必须为 true；随后的首次撤销应撤前一个实际提交（仅 sentinel 时直接撤 sentinel），不能有空步。cores 失败要保留先前的 cable/generate。API 提前失败时不能把它当成已通过故意失败测试。
4. 持久性另起一轮提交，由维护者关闭重开后 readback 逐字段比较；不要依赖重开后的撤销历史。报告、截图、scratch 项目只留本地，结果未明时不重试写入。

## 结果

EPLAN 2026.0.3.25421，Electric P8，只在新复制的 `writes\LargeWrite` 上，全部用虚构标识；日志与截图只留本地。

| 轮次 | 主要发现 |
|---|---|
| r1 | 位置盒、电缆定义线放置通过；中断点名称比较、端子空引用、PLC 连接点发现条件过窄、触点代号写法错误、芯线成员为 0 |
| r2 | **EPLAN 崩溃**（`0xc0000374` 原生堆损坏，EPLAN 以 `/RestartOnCrash` 自行重启并只打开夹具）：PLC 组写入失败、`Transaction.Abort` 之后，探针仍持有并释放了事务内新建对象的属性列表。r3 起改为在 Abort 之前释放并移除这些缓存，Abort 后不再触碰新建对象，之后未再崩溃 |
| r3 | 中断点放置与生成通过；其余为探针读取空值的问题 |
| r4 | 中断点、端子、导线、电缆定义线、位置盒及各自的生成全部通过；触点代号与芯线未通过 |
| r5 | 触点代号、电缆芯线通过；PLC 连接点数据正确但多出对象（见下）；关闭重开后读回逐字段相同（忽略对象标识的会话前缀）；端子组故意失败后 `Transaction.Abort` 完全恢复 |

| 元素 | 结论 |
|---|---|
| 中断点 | SPECIAL `BPF`（InterruptionPoint）用 `SymbolReference.Create` 放置；名称写完整标识（如 `=S43+S43LAB-S43LINK`）读回完全一致；提交后单独 `Generate.Connections` 生成连接 |
| 端子 | IEC_symbol `X`（Terminal）；建端子排 `+S43LAB-S43X` 后端子名 `:1..:3`，读回类别、所属端子排和代号一致；事务内父关系可能为空，提交后核对 |
| 触点代号 | **按引脚写带索引的 20022（FUNC_CONNECTIONDESIGNATION）** 生效（如 11/12），`Pin.Name` 与 20028 读回一致；20038 是汇总（读回 `11;12`），写它无效；`Pin.Designation` 是内部编号（1/2） |
| 电缆芯线 | 顺序：导线 → 生成 → 电缆定义线 → 生成 → 绑定（单独提交）→ 生成 → 芯线。电缆 `+S43LAB-S43W2` 读回 3 个成员连接，芯线分配精确，本例以外的连接不变，未调用全项目重分配 |
| PLC 连接点 | 盒 SPECIAL `PLCC` + 3 个 DI 连接点：地址 I0.0–I0.2、符号地址、通道 0–2 正确，父盒为 `-S43KF`。**每创建一个连接点，EPLAN 还生成一个同名、无父对象的 PLCTerminal 对象**（定义 Group 1 Id 1），工具须识别并核对，不能假设对象数 |
| 位置盒 | SPECIAL `SC`（LocationBox）可放置并设为虚线；但盒内符号的结构**没有继承** `+S43BOX`（读回 `=+`），继承条件未查明，**未通过** |

结论：中断点、端子、触点代号、电缆定义线与芯线、PLC 连接点及编址都可以通过 API 画出并核对，各为一个可回滚、可撤销的提交并在重开后保留。位置盒能画，但结构继承需要另做探针。写入失败后不得读取或释放事务内新建对象（否则 EPLAN 崩溃）。

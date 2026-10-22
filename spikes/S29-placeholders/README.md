# Spike S29：占位符对象与值集

> 状态：**完成**（2026-10-21，r1、r2）。M6 设计的 spike 之一（参数化模块）；扫描只读，写入只在 scratch 副本上进行，并在 `SafetyPoint` 中回滚。

## 问题
M6 的参数化模块打算建立在 EPLAN 的宏、占位符对象和值集之上。本 spike 回答：
1. 不插入宏能否读出其中的占位符、值集（记录）和变量；EPLAN 自带的宏中有多少带占位符；
2. 项目中的占位符怎样列出；
3. 插入宏之后，`ApplyRecord` 能否切换值集，被引用对象的值是否随之改变，能否被 `SafetyPoint` 回滚；
4. 能否用 API 新建值集、写入变量值并应用。

## 方法
- `SpikeS29Action` 在主线程上经调度器运行（`LockingStep` + `QuietModeStep`）。
- `scan`：在写副本的上下文中，对 `$(MD_MACROS)` 下每个 `.ema` 调用 `WindowMacro.Open(path, project)`（不插入，api-notes A13 / S10），遍历每种表达类型的每个变体（`ChangeCurrentVariant`），从 `Objects` 中取 `PlaceHolder`。宏目录含中文，经 Remote Client 传参会乱码（S24），所以只传 `$(MD_MACROS)`，在插件中用 `PathMap.SubstitutePath` 展开。
- `list`：`DMObjectsFinder.GetPlaceholdersWithFilterScheme(null)`（不加筛选的占位符导航器）。
- `apply`：在写副本 `scratch\writes\LargeWrite` 中，`SafetyPoint` 内新建一页，插入 `24VDC_Supply.ema`（MultiLine，变体 1），对页上每个占位符依次 `ApplyRecord` 每个值集，读 `AssignedObjects` 和 `GetAllCurrentValues()`；r2 再对第一个带值集的占位符 `AddRecord("S29")`、对每个变量 `SetValue`（语言无关文本）、`ApplyRecord("S29")`。最后 `Rollback()`。
- 环境：EPLAN 2026.0.3.25421，Electric P8。

```powershell
dotnet build spikes\S29-placeholders\EplanMcp.Spikes.S29.csproj -c Release -p:AssemblyName=EplanMcp.Spikes.S29rN
dotnet build spikes\S29-placeholders\Driver -c Release
spikes\S29-placeholders\Driver\bin\Release\net481\EplanMcp.Spikes.S29.Driver.exe --host <本机网卡地址> --port <Remote Client 端口> --revision rN --modes scan,list --project "<scratch 中的 .elk>" --part '$(MD_MACROS)'
spikes\S29-placeholders\Driver\bin\Release\net481\EplanMcp.Spikes.S29.Driver.exe ... --modes apply --project "<scratch 中的 .elk>" --part '$(MD_MACROS)\EPLAN_Macro\202_Power_Supply_24V\101_01_Variant_1\24VDC_Supply.ema' --variant 1
```

## 结果
**扫描宏（只读）**：695 个宏、1,751 个"表达类型 + 变体"，21.1 秒，没有打开失败的；72 个宏（83 个变体）带占位符，共 245 个占位符，其中 131 个带值集，最多 8 个值集；38 个宏带值集。表达类型：MultiLine 65、Overview 10、Fluid_MultiLine 7、PI_FlowChart 1。几乎都在 `EPLAN_Macro` 示例宏中（70 个），另有 2 个西门子宏。扫描前后写副本的页数不变（321）。

**项目中的占位符**：示例项目有 2 个（`=TM1+A1&EFS1/1`、`=TM2+A1&EFS1/1`），各有值集 `V`、`mA` 和变量 `T1`、`T2`，名称为空，各引用 2 个文本。列出耗时不到 0.01 秒。

**应用值集**（`24VDC_Supply.ema` 变体 1，插入 200 个对象 0.17 秒，页上 6 个占位符）：
- `#<Function_text>`（值集 `Power supply`、`PLC I/O's`、`Devices`，变量 `Task`、`DI1`、`DI2`，引用 3 个路径功能文本）：每次 `ApplyRecord` 不到 0.01 秒，`GetAllCurrentValues()` 随之变为该值集的多语言文本（如英文 "24 V device power supply" → "24 V input / output supply" → "Devices"）。
- `Cable_Sensor`（值集 `Mainfunction true`、`Mainfunction false`，引用 15 个对象）：应用不报错；当前值的前几项两次相同，本 spike 没有逐项比较被引用对象的属性。
- 另外 4 个占位符没有值集，只有变量（如 `Structure_Box_Motor_Overload` 的 `Mounting_Location`），引用位置框、多段线、中断点、电缆等。
- 值以多语言字符串存储（`de_DE@…;en_US@…;zh_CN@…;`）。
- 回滚后页数回到 321，新页的名称 `/929` 在下一次运行中仍可用，插入和应用全部撤销。

**用 API 新建值集**（r2）：`AddRecord("S29")` 返回序号 3，值集列表变为 4 个；`SetValue` 写入的 3 个变量都能用 `Value` 读回；`ApplyRecord("S29")` 后当前值为新文本（语言无关 `??_??@`）。耗时 0.01 秒。

## 结论
1. **参数化模块可以直接用 EPLAN 的占位符和值集表达**：不插入就能从宏中读出占位符、值集名、变量名和值表，扫描全部示例宏只要约 20 秒，可以做成资源索引的一部分。
2. **切换和新建值集都能用 API 完成，并能被 `SafetyPoint` 回滚**：插入宏 + 应用值集可以放在同一个写入事务中，失败时整体撤销（与 A13 中插入宏只能用 `SafetyPoint` 回滚一致）。
3. **值是多语言文本**：工程包中保存值集时要按语言保存；用语言无关的写法（`??_??`）写入时，EPLAN 照样显示。

## 局限
- 只在 MultiLine 上应用过一个宏；Overview、流体等表达类型和别的宏没有试。
- 没有提交过应用结果：提交后的持久性、一次 Ctrl+Z 能否撤销、`ApplyRecord(name, false)`（不写页和项目数据）的区别都没有验证。
- 没有逐项比较被引用对象应用前后的属性，只看了 `GetAllCurrentValues()`；`Mainfunction true/false` 这类改布尔属性的值集效果没有确认。
- 没有新建占位符对象（`PlaceHolder.Create`、`AddReference`），也没有把值集写回宏文件。
- 扫描只在写副本的上下文中打开宏；打开宏是否改动项目文件依据 S10 的结论，没有重新核对 `.edb`。

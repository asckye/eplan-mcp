# Spike S28：对象标识在项目副本之间是否稳定、整个项目的读取耗时

> 状态：**完成**（2026-10-21，r0）。M6 设计的 spike 之一；只读，不写任何项目。

## 问题
M6（工程包）的来源定位和差异比对需要能跨副本、跨会话指向同一个对象。本 spike 回答：
1. 同一项目的两份文件副本中，同一个对象的 `StorableObject.ObjectIdentifier` 是否相同；`DatabaseIdentifier`、`ToStringIdentifier()` 怎样变化；
2. 列出并读取整个项目（页、功能、连接、图形放置）要多久。

## 方法
- `SpikeS28Action` 在主线程上经调度器只读运行（`LockingStep` + `QuietModeStep`）。
- `identity`：对每个项目，按"名称|页"列出全部主功能，再列出全部页，记录 `ObjectIdentifier`；比较两份副本中同名对象的编号；另记一个样本的 `DatabaseIdentifier` 和字符串标识。
- `extract`：`DMObjectsFinder` 的 `GetPages`、`GetFunctions`、`GetConnections`、`GetPlacements` 各自耗时；再读全部功能的名称、页名、X 坐标和编号，以及全部连接两端的符号引用。
- 两个项目：夹具 `scratch\fixtures\EPLAN_Sample_Project` 和写副本 `scratch\writes\LargeWrite`，都是 EPLAN 自带示例项目的文件副本，同时在 EPLAN 中打开。
- 环境：EPLAN 2026.0.3.25421，Electric P8。

```powershell
dotnet build spikes\S28-identity-extract\EplanMcp.Spikes.S28.csproj -c Release -p:AssemblyName=EplanMcp.Spikes.S28rN
dotnet build spikes\S28-identity-extract\Driver -c Release
spikes\S28-identity-extract\Driver\bin\Release\net481\EplanMcp.Spikes.S28.Driver.exe --host <本机网卡地址> --port <Remote Client 端口> --revision rN --modes identity,extract --part "<a.elk>|<b.elk>"
```

## 结果
**对象标识**：两份副本各 825 个键（主功能和页），825 个全部同名，**`ObjectIdentifier` 全部相同**（例如 `=+-CU1` 都是 59641）。不同的只有 `DatabaseIdentifier`（28 和 53）。字符串标识的格式是 `数据库号/类型号/对象号/0`（`28/17/59641/0` 和 `53/17/59641/0`）。官方文档：`ObjectIdentifier` 在同类对象中唯一，对象删除后再新建时可能被重用；`DatabaseIdentifier` 在项目重新打开时改变；字符串标识只在本次运行中有效。

**读取耗时**（两份副本相近）：

| 内容 | 数量 | 秒 |
|---|---:|---:|
| 页 | 321 | 0.00–0.01 |
| 功能 | 2,876 | 0.01–0.04 |
| 连接 | 2,060 | 0.05–0.07 |
| 图形放置 | 44,735 | 0.59–0.80 |
| 读全部功能的名称、页、坐标、编号 | 2,876 | 0.06 |
| 读全部连接两端 | 2,060 | 0.00 |

## 结论
1. **对象可以用"项目身份 + 类型号 + `ObjectIdentifier`"定位**：在同一项目的文件副本之间不变。因为编号可能被重用，执行前要用名称、页、类型等核对，不一致按失效处理。运行时字符串标识和 `DatabaseIdentifier` 不能跨调用保存。
2. **示例规模的项目，完整读取是秒级的**：枚举全部对象约 1 秒，逐个读常用属性不到 0.1 秒。资源索引和规范快照可以先实时读取。

## 局限
- 重新打开项目之后编号是否不变，是根据文档推断的；本 spike 证明的是文件副本之间不变。
- 由 `.zw1` 还原、PXF 导入、或从基本项目新建的对象是否保留原编号，没有验证。
- 只测了示例项目的规模（321 页）；大型项目的耗时和内存没有测。
- 只读了少数几个属性；读全部属性、图形细节的耗时没有测。

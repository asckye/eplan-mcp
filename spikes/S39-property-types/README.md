# Spike S39：FUNC_TECHNICAL_CHARACTERISTIC 的运行时类型

状态：已实现，待协调者运行。目标是补齐 [A44](../../docs/eplan/api-notes.md) 中 20027 的确切类型证据，供之后的属性写白名单评审使用。

## 方法

- 只读已打开的 sample fixture；不打开、关闭或修改项目。功能来源为 finder 加全部页放置，按会话标识去重并排序；两项属性分别保留最多 50 个有非空值的功能。A44 中 20011 与 20027 的非空设备没有交集，因此对照组独立采样，包含各类功能表示。
- 严格遵循 A36：每个唯一功能只取一次 `Properties`、释放一次；`AnyPropertyId`、父值、索引值、定义、转换所得 `MultiLangString`、语言 `ISOCode` 均释放；借用的 Project/Page/Function 不释放。空值不转换。
- JSON 每条样本包含设备标识、会话标识、页、主功能标记、实例 `PropertyDefinition.Type` 的精确枚举名和数值、`IsIndexed`、已存索引及各值的 `ToString()` 显示和转换结果。非索引值的 index 为 null；索引值只读取已存索引。
- 逐项尝试 `ToBool`、`ToInt`、`ToDouble`、`ToPointD`、`ToTime`、`ToMultiLangString`、三个 `ToString` 重载、`GetDisplayString` 和七个向外隐式转换；结果为返回值或异常完整类型名。语言重载固定 en_US，带默认值重载使用 `<S39 fallback>`。多语言值复制 raw 和全部存储译文；日期保留往返文本、Kind、Ticks，不推断时区。
- 依据 `catalog/eplan-2026.0.3/members/Eplan.EplApi.DataModel.jsonl` 和 `types.jsonl`：SDK 没有 `ToDateTime` 或专用 Variable/ValueWithUnit 访问器；`ToTime` 返回 DateTime。即使跨类型转换成功，也不据此确定存储类型或允许写入。
- 调度方式沿用 S31/S34：action 返回后进入主线程，首个 API 操作为 LockingStep，禁止模态窗口重入，所有异常在委托内捕获。读取预算 90 秒（不能打断单个原生调用），驱动等报告最多 240 秒；输出在 `.local/spikes/s39/<runId>/s39-property-types.json`，完整发布后才出现正式文件。驱动只打印一行摘要，含两组样本数量、枚举名/数值和报告路径；致命错误、读取错误、注销失败或任一组无样本时返回 1，预期的转换异常不算失败。

## 构建与运行

从仓库根目录执行。仅构建不启动 EPLAN：

```powershell
dotnet build spikes/S39-property-types/EplanMcp.Spikes.S39.csproj -c Release -p:AssemblyName=EplanMcp.Spikes.S39r1 -p:AssemblyTitle=EplanMcp.Spikes.S39r1
dotnet build spikes/S39-property-types/Driver/EplanMcp.Spikes.S39.Driver.csproj -c Release
```

协调者先在 EPLAN 2026.0.3 中打开 `eng/scripts/eval-fixture.ps1` 准备的 sample fixture，关闭模态窗口并启用 Remote Client（默认端口 49152），再运行：

```powershell
& .\spikes\S39-property-types\Driver\bin\Release\net481\EplanMcp.Spikes.S39.Driver.exe --host 127.0.0.1 --port 49152 --revision r1
```

默认读取 `%USERPROFILE%\EplanMcp\scratch\fixtures\EPLAN_Sample_Project.elk`；设置了 `EPLANMCP_HOME` 时使用其 `scratch\fixtures`。可用 `--project` 显式传入同一夹具路径，不依赖当前项目。连接失败时将 `--host` 换成本机可连接的 IPv4（B4）。驱动注册插件并在 finally 中注销；程序集在 EPLAN 退出前不会卸载，同一会话改代码后应将构建中的两个 r1 和运行的 `--revision r1` 一起改成 r2、r3。

## 结果

r1，2026-10-26，EPLAN 2026.0.3.25421，Electric P8，只读 sample 夹具（`EPLAN_Sample_Project`）；各取 50 个非空样本，无读取错误。

- **20027 `FUNC_TECHNICAL_CHARACTERISTIC`**：50 个样本的 `PropertyDefinition.Type` 都是 `ValueWithUnit`（数值 26），都不建索引。实际内容是自由文本：`0`、`3`、`14 W`、`16 A`、`6 A`、`DC 24V`，也有 `I/O Board`、`Inverter`。
  - `ToString()` 与显示文本相同，是正确的读法；`ToMultiLangString()` 的原始值是与语言无关的一条（如 `??_??@14 W;`）。
  - `ToDouble()`、`ToInt()` 不可靠：`14 W`、`16 A` 都读成 0，只有纯数字才对；`ToBool()`、`ToPointD()`、`ToTime()` 不报错但结果没有意义。
- **20011 `FUNC_TEXT`**（对照）：50 个样本都是 `MultilangString`，不建索引，与 A44 一致。
- 结论见 api-notes A48：20027 按“与语言无关的文本”读写，不能当作数值；进入写入白名单前，P7 的 Large 仍要实测写入、读回、回滚和撤销。

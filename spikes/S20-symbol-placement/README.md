# Spike S20：不用窗口宏放置单个设备

## 问题
[M4c 设计](../../docs/design/2026-10-21-m4c-more-tools.md)第 4.3 节。S10 证实按库符号新建的设备能撤销，但图上不显示标识，选择属性排列也没有用（M3b 设计 Q1 因此推迟）。本 spike 在 Bridge 调度器中（与工具相同）验证两条新路：
1. 按库符号新建之后，用 `PropertyPlacement.Create` 给设备标识加一个属性放置，标识能否显示；
2. `DeviceService.CreateDevice(部件号, 变体, 页, 位置)`：按部件号放置部件自带的宏，标识是否显示，能否被 `SafetyPoint` 回滚、能否一步撤销。

## 方法
- `SpikeS20Action` 的结构与 S21 相同。在示例项目的 scratch 副本中新建两页 `=GAA+A1&EFS1/90`、`/91`（`SafetyPoint` + `UndoStep`）。
- 第 90 页：在 `UndoStep` 包住的 `Transaction` 中（S9、S10 的方式）从 `IEC_symbol` 库取符号 `K` 新建线圈，位置 (60, 160)，`Name` 设为 `=GAA+A1-KF96`，然后 `new PropertyPlacement().Create(function, Properties.Function.FUNC_DEVICETAG_FULLNAME)`。
- 第 91 页：取项目中第一个有部件的线圈主功能的部件号，在 `SafetyPoint` + `UndoStep` 中调用 `CreateDevice(部件号, "1", page, (80, 160))`；先回滚一次，再提交一次。
- 两页都用 `Export.GraphicPage` 导出为 PNG，直接看图；最后用两次 Ctrl+Z 依次撤销两次放置。
- 环境：EPLAN 2026.0.3.25421，Electric P8，2026-10-21，r0；EPLAN 中只开着 scratch 项目。

```powershell
dotnet build spikes\S20-symbol-placement\EplanMcp.Spikes.S20.csproj -c Release
dotnet build spikes\S20-symbol-placement\Driver -c Release
spikes\S20-symbol-placement\Driver\bin\Release\net481\EplanMcp.Spikes.S20.Driver.exe --host <本机网卡地址> --port <Remote Client 端口>
```

原始结果和图片在 `.local/spikes/s20/` 和 `<数据根目录>\scratch\spikes\s20\out\`，不提交。

## 结果
**按库符号新建 + 属性放置**（0.6 秒）：
- 新建后线圈已有 12 个属性放置，显示的文字都是空的；`VisibleName` 为空。
- `PropertyPlacement.Create(function, FUNC_DEVICETAG_FULLNAME)` 成功，得到第 13 个属性放置，`GetDisplayString()` 为 `KF96`；`UseLocalPropertyPlacements` 变为 true（XML：同时把方案改为"用户定义"）。`VisibleName` 仍为空。
- 图上线圈旁显示了 `KF96`，但文字压在符号上（放置的位置就是符号的插入点），没有前面的 `-`。
- 一次 Ctrl+Z 撤销后，线圈不在了。

**`DeviceService.CreateDevice`**（部件 `SIE.3RH2122-1HB40`，0.01–0.3 秒）：
- 返回 5 个功能：放置在页上的线圈主功能，以及 4 个未放置的触点（XML 说明的"按功能模板创建时只放主功能"）。
- 主功能的标识是 `=+-Q1`，显示标识 `-Q1`：**没有取页的结构 `=GAA+A1`**，也没有编号（XML 警告过不编号）。
- 图上与宏插入的设备一样完整：`-Q1`、`DC 24V`、A1/A2 连接点，下方有触点镜像（13-14、21-22、31-32、43-44）。
- `SafetyPoint` 回滚后页上没有功能；提交后一次 Ctrl+Z 撤销。

## 结论
1. **按库符号放置可以显示标识**：`PropertyPlacement.Create` 是 S10 没有试过的入口。要做成工具，还要查清两件事：用哪个属性才能显示带 `-` 的标识（`FUNC_DEVICETAG_FULLNAME` 只显示了 `KF96`），以及怎样把文字放在符号旁边而不是压在上面。
2. **按部件放置可行而且效果最好**：`CreateDevice` 放的是部件自带的宏，图上的样子与工程师手工放置的相同；能被 `SafetyPoint` 回滚，所以流水线与 `eplan_macro_insert` 相同（不需要 `Transaction`）。放置后要按目标结构改名：主功能和全部辅助功能一起改（A13），并读回核对。
3. **`eplan_device_place` 的建议形态**：参数给部件号（`part`，必填）、变体、页、位置和标识；用 `CreateDevice` 放置，再改名。按库符号放置（没有部件的符号）先不做，等第 1 条查清；这回答了 M4c 设计 Q3。
4. 部件不在部件库中、或者部件没有宏时 `CreateDevice` 怎样，没有验证；工具规格中写成待 Large 测试确认。

## 局限
- 只在示例项目、Electric P8、EPLAN 2026 上验证；只试了一个部件和一个符号。
- 没有试部件库中不存在的部件号、没有宏的部件、带变体的部件。
- 改名在本 spike 中没有做；A13 中改名的规则来自 S9。

# Spike S21：电缆、端子、PLC 的读取与编号

## 问题
[M4c 设计](../../docs/design/2026-10-21-m4c-more-tools.md)第 4.4、4.9 节，在 Bridge 调度器中（与工具相同）验证：
1. 能否读出电缆、端子排、端子、PLC 盒、连接，以及它们之间的关系；读取的耗时。
2. 端子编号（`Renumber.Terminals`）、电缆编号（`CableService.RenumberCables`）、线号（`ConnectionService.DesignateConnections`）、生成电缆（`Generate.Cables`）：
   - 耗时，改了什么、改动哪些文件；
   - 能否被 `SafetyPoint` 回滚，Ctrl+Z 能否一步撤销；
   - 方案传空串时怎样。
3. M4c 用到的授权选项是否可用，尤其是预规划。

## 方法
- `SpikeS21Action` 的结构与 S11 相同：action 只调度探针，探针经 `Dispatcher.Invoke` 在主线程上执行。写入用 Bridge 的包装（`SafetyPoint` → 其中的 `UndoStep` → `QuietModeStep`）。回滚模式在同一 `SafetyPoint` 中执行后调用 `Rollback()`。
- 改动前后读出全部端子名、电缆名和连接的线号（`CONNECTION_DESIGNATION`），按对象标识比较，并算摘要。
- `Driver/` 通过 Remote Client 驱动：复制示例项目到 `<数据根目录>\scratch\spikes\s21\S21_Sample` 并打开，选为当前项目，依次运行各模式，最后选回原来的当前项目、关闭副本、注销插件。只在打开的项目全是 scratch 副本时发送 Ctrl+Z。
- 四种操作的方案都传空串，端子编号不只编带 `?` 的，电缆编号不保留旧名、保留标识符，线号覆盖全部（`DesignateOverwrition.All`），生成电缆只处理自动电缆。
- 环境：EPLAN 2026.0.3.25421，Electric P8，2026-10-21，r0。

```powershell
dotnet build spikes\S21-cables-terminals\EplanMcp.Spikes.S21.csproj -c Release
dotnet build spikes\S21-cables-terminals\Driver -c Release
spikes\S21-cables-terminals\Driver\bin\Release\net481\EplanMcp.Spikes.S21.Driver.exe --host <本机网卡地址> --port <Remote Client 端口>
```

原始结果在 `.local/spikes/s21/`，不提交。

## 结果
**读取**（示例项目，下表全部内容一次读完 11.2 秒；各部分的耗时没有分开测，`GetSourcesAndTargets` 只对前 5 根电缆调用）：

| 对象 | 数量 | 读到的内容 |
|---|---:|---|
| 电缆（`GetFunctions`，类别 `Cable`） | 78 | 名称、所在页、部件号、芯线连接数（`CableConnections`）、源和目标（如 `=F+B1-SG1` → 端子 `+A2-XG4:2`） |
| `GetCablesWithFilterScheme(null)` | 89 | 电缆加屏蔽。XML 说 null 会抛异常，实际没有 |
| `GetCablesWithFilterScheme("")` | 4 | 用了电缆导航器当前的过滤方案，结果取决于界面状态 |
| 端子排 | 45 | 名称、端子数、跨接数；端子排定义没有所在页 |
| 端子 | 1331 | 485 个主端子；名称、所在页、所属端子排、内外连接数；没有带 `?` 的 |
| PLC 盒 | 20 | 名称、所在页、部件号、PLC 端子（每个 34 个，如 `=K1+A2-KE1:2.3`）；`IsCPU` 都是 false，`Scheme` 为空，`PlcIOEntry` 为 null |
| 插头 / 插头排 / 电缆单元 | 170 / 41 / 0 | 只数了数量 |
| 连接 | 2060 | 116 条有线号 |

**编号与生成**（方案都是空串，都没有抛出异常）：

| 操作 | 耗时 | 改了什么 | 回滚 | 改动的项目文件 |
|---|---:|---|---|---|
| `Renumber.Terminals`（全部 1331 个端子） | 41–43 s | 4 个端子换了号（`+A1-XD1:2` → `:3` 等），按端子排中的顺序重排 | ✅ 摘要与之前相同 | `Function`、`Page`、`ArticleRef`、`Blob` |
| `CableService.RenumberCables`（78 根电缆） | 0.3 s | 两对电缆互换了名称（`+A2-WD4` ↔ `+A2-WD5` 等） | ✅ | `Function`、`Connection`、`ArticleRef`、`Blob` |
| `ConnectionService.DesignateConnections`（覆盖全部） | 0.9–1.1 s | 605 条连接得到线号（`*` → `433`，空 → `011` 等），有线号的连接 116 → 659 | ✅ | `Connection`、`Function`、`Page`、`Message` |
| `Generate.Cables`（只处理自动电缆） | 0.4 s | 名称、线号都没有变化 | — | 项目的 `User.eod` 和 `Settings.xdb`（回滚之后仍是新的） |

**方案的设置节点**（从用户设置的导出 `XSettingsExport /node:USER`，以及评测夹具项目的 `Settings.xdb` 中只读查得；项目设置的 `XSettingsExport /prj:<项目名>` 返回 FALSE）：

| 用途 | 节点 | 位置 | 示例项目或本机中的方案 |
|---|---|---|---|
| 端子编号 | `USER.Terminal.Numbering.Scheme` | 用户设置 | `Gerätedefinition`、`Initiatoren`、`Numerisch` |
| 插头编号 | `USER.Plug.Numbering.Scheme` | 用户设置 | `Gerätedefinition`、`Numerisch` |
| 电缆编号 | `CableLog.CableNumberingScheme` | 项目设置 | `Standard` |
| 生成电缆、电缆选型 | `CableLog.CableCreateScheme`、`CableLog.CableAutoSelectionScheme` | 项目设置 | `Standard` |
| 线号 | `WireNumberingLog.Schema` | 项目设置 | `Standard`、`Potenzialorientiert`、`Verbindungsorientiert` 等 11 个 |
| PLC 编址 | `OfflineNumerationPLC.PLCNumbering` | 项目设置 | `Standard` |
| 标签 | `USER.Labelling.Config` | 用户设置 | `Device tag list`、`Summarized parts list`、`Terminal diagram`、`Cable diagram` 等 7 个 |

每个节点下还有一个内部的 `InternalRefDefScheme`，不是可选的方案。

**Ctrl+Z 没有测**：开发机上 EPLAN 同时打开着 scratch 之外的示例项目，驱动按规则不发送 Ctrl+Z。

**授权**（3214 个选项中 2683 个可用）：
- 不可用：`Preplanning`。
- 可用：`RevisionManagement`、`RevisionMarkers`、`MultiLanguageTranslation`、`WireNumbering`、`EditingOfCables`、`EditingOfTerminals`、`EditingOfPLC`、`EditingOfParts`、`DXF_DWGImport`、`DataImport`、`ImportAndExportDeviceLists`、`IntelligentPDF`、`InterfaceToLabelingDevices`、`PLCAssignmentLists`、`PLCBusExtension`、`BusConfigurationExport`、`APIExtensionPartsmanagement`。
- 预规划的其他几项（`PreplanningExport`、`PreplanningObjectsBase`、`PreplanningPID`）可用，但导入要的是 `Preplanning`。

## 结论
1. **读取工具可行**：电缆、端子排、端子、PLC 盒和连接都能在调度器中读出，关系也能读出。电缆按类别取（`FunctionsFilter.FunctionCategory`），或者用 `GetCablesWithFilterScheme(null)`（含屏蔽）；不要传空串。`GetSourcesAndTargets` 的耗时没有单独测，在写工具规格时测，再决定列表中是否默认给出源和目标。
2. **三种编号都能被 `SafetyPoint` 回滚**，与设备、页的编号相同（A18）。工具按 M4c 设计定为 Destructive（先备份）：结果由方案决定，事先算不出；线号一次可以改几百条。
3. **方案传空串可以运行，等于界面上次用的方案**，结果随界面状态变化。工具要传明确的方案名，可选的方案从上表的节点读出：端子编号、标签在用户设置中（`SchemeSetting`），电缆编号、线号在项目设置中（项目的方案设置类，读法在写工具规格时验证）。
4. `Generate.Cables` 在示例项目中没有可生成的自动电缆，生成的效果需要另找项目验证；它会改写项目设置（上次使用的方案），回滚不撤回。
5. **预规划导入不能做**：授权中没有 `Preplanning`（M4c Q6）。
6. 端子编号整个项目要 40 秒以上，工具描述写明耗时，并允许按端子排限定范围。

## 局限
- 只在示例项目、Electric P8、EPLAN 2026 上验证；只用了空串方案。
- Ctrl+Z 没有测，留到只打开 scratch 副本时补测。
- 读取只覆盖名称和关系，没有逐个核对端子、电缆、PLC 的全部属性。
- 端子编号没有试"只编带 `?` 的"：示例项目中没有这样的端子。

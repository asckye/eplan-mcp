# Spike S22：M4c 的导入

## 问题
[M4c 设计](../../docs/design/2026-10-21-m4c-more-tools.md)第 4.5、4.9 节，在 Bridge 调度器中（与工具相同）验证第三批的导入：
1. 改了什么、耗时，能否被 `SafetyPoint` 回滚、一步撤销（决定 Write 还是 Destructive）；
2. PXF 导入新建的项目放在哪里、是否自动打开。

## 方法
- `SpikeS22Action` 的结构与 S21 相同；写入用 Bridge 的包装（`SafetyPoint` → 其中的 `UndoStep` → `QuietModeStep`）。示例项目的 scratch 副本，EPLAN 中只开着 scratch 项目。
- 输入文件都放在 `<数据根目录>\scratch\spikes\s22\in\`：EPLAN 自带的 Rittal 示例 DXF（`4685200F.DXF`，25 KB）；从副本导出的部件清单 XML；从副本导出的 PXF（不带主数据）。
- DXF：在新建页 `=GAA+A1&EFS1/92` 上 `new Import().DXFPage(文件, 页, new Transformation(文件, 页, "", ScalingMode.Automatic, PositionMode.Center, false), "")`，比较页上的 `AllPlacements` 个数。
- 部件清单：`PartsService.ImportPartsList(project, 文件, Format.XML)`，比较项目部件数和部件引用数，然后回滚。
- PXF：`new Import().PXFProject(文件.epj, 目标)`。
- PDF 注释、设备清单、PLC 数据没有测：手头没有合适的输入文件（带注释的 PDF、非空的设备清单、PLC 总线数据）。
- 环境：EPLAN 2026.0.3.25421，Electric P8，2026-10-21。r0 跑全部步骤，PXF 导入时会话重启、EPLAN 随之被结束；r1 在重新启动的 EPLAN 中重跑 DXF 和 PXF 导入（`--pxf-import-only`，沿用 r0 导出的文件）。

```powershell
dotnet build spikes\S22-imports\EplanMcp.Spikes.S22.csproj -c Release
dotnet build spikes\S22-imports\Driver -c Release
spikes\S22-imports\Driver\bin\Release\net481\EplanMcp.Spikes.S22.Driver.exe --host <本机网卡地址> --port <Remote Client 端口> [--pxf-import-only]
```

原始结果在 `.local/spikes/s22/`，不提交。

## 结果
| 操作 | 耗时 | 结果 | 回滚 | Ctrl+Z |
|---|---:|---|---|---|
| DXF 插入页面（r0、r1 相同） | 0.04–0.3 s | 页上新增 227 个图形对象 | ✅ 回到 0 个 | ✅ 一步撤销，回到 0 个 |
| 导出部件清单 XML（准备输入） | 52 s | `parts.xml` 14 MB | — | — |
| 把同一份部件清单导回项目 | 69 s | 项目部件 293、部件引用 981，前后都不变 | 看不出：没有变化 | 未测 |
| 导出 PXF（准备输入） | 16 s | `s22.epj` 598 MB | — | — |
| PXF 导入为新项目（r1） | 57.5 s | 新建 `imported\S22_Imported.elk` 和 `.edb`（171 MB）；没有自动打开 | 不适用 | 不适用 |

- 🔴 **`PXFProject` 的第二个参数是新项目的路径（不带扩展名），不是目录**：r0 传了 `…\s22\imported`，EPLAN 在 `s22\` 下新建了 `imported.elk` 和 `imported.edb`；XML 写的是"目标目录"。r1 传 `…\imported\S22_Imported`，得到 `imported\S22_Imported.elk`。
- r0 的 PXF 导入被中途结束，留下一个不完整的 `imported.edb`（57 MB），没有被 EPLAN 清理。

## 结论
1. **DXF 导入可以定为 Write**：只新增图形，能被 `SafetyPoint` 回滚、一步撤销，符合 M4c 设计 4.5 "只新增不覆盖的改为 Write"。`eplan_import_dxf` 的规格按 Write 写；插入位置和缩放由 `Transformation` 决定，规格中写明参数。
2. **PXF 导入新建项目，路径要由工具给全**：新项目只建在 scratch 根目录中（M4c 设计 Q5），参数是 `<scratch>\<子目录>\<项目名>`。导入约 1 分钟；中途失败会留下不完整的项目，工具失败时删除本次新建的 `.elk` 和 `.edb`（只删本次新建的）。导入后不自动打开，需要时由工程师打开。
3. **部件清单导入的效果没有看出来**：导回同一份清单，计数不变。要另造一份有差别的清单（例如改一个部件的数量）再测；在那之前按设计定为 Destructive。
4. PDF 注释、设备清单、PLC 数据的导入在找到输入文件后再测。

## 局限
- 只在示例项目、Electric P8、EPLAN 2026 上验证；DXF 只试了一个文件、一种缩放和定位。
- PXF 导入后的新项目没有打开核对页数。

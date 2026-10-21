# Spike S25：M4c 的导出（PXF、清单、标签、PLC、缺失词表）

> 状态：**完成**（2026-10-21），访问冲突的原因除外。第一次运行（r0）在部件清单 CSV 导出之后，EPLAN 记录了访问冲突并关闭了全部项目；其余导出在新启动的 EPLAN 中第二次运行（r1）完成。

## 问题
[M4c 设计](../../docs/design/2026-10-21-m4c-more-tools.md)第 4.9 节，在 Bridge 调度器中（与工具相同）验证第一批的导出：
1. 文件是否只出现在给定目录中，项目文件是否改变，耗时和文件大小；
2. 是否改写"上次使用的方案"；
3. 标签的 xlsx 输出是否需要 Excel。

## 方法
- `SpikeS25Action` 的结构与 S11 相同：action 只调度探针，探针经 `Dispatcher.Invoke` 在主线程上、`QuietModeStep` 中执行。导出不用 `SafetyPoint`（与 M4 的导出工具相同）。
- `Driver/` 通过 Remote Client 驱动：复制示例项目到 `<数据根目录>\scratch\spikes\s25\S25_Sample` 并打开，每种导出写进 `out\<模式>\` 下新建的空目录，前后对比副本 `.edb` 中的文件和导出目录。
- 依次运行：PXF（带主数据和连接）、PXF（不带主数据）、部件清单 XML、部件清单 CSV、设备清单 CSV、标签（`Device tag list`，文本）、标签（`Terminal diagram`，xlsx）、PLC 地址总览、缺失词表（`en_US`）。
- 环境：EPLAN 2026.0.3.25421，Electric P8，2026-10-21。r0 依次运行全部导出；r1 在重新启动的 EPLAN 中只运行 r0 没有运行的五项（`--modes`）。

```powershell
dotnet build spikes\S25-exports\EplanMcp.Spikes.S25.csproj -c Release
dotnet build spikes\S25-exports\Driver -c Release
spikes\S25-exports\Driver\bin\Release\net481\EplanMcp.Spikes.S25.Driver.exe --host <本机网卡地址> --port <Remote Client 端口>
```

原始结果在 `.local/spikes/s25/`，不提交。

## 结果
| 导出 | API | 耗时 | 文件 | 改动的项目文件 |
|---|---|---:|---|---|
| PXF，带主数据和连接 | `Export.PXFProject(project, 路径, true, true)` | 36.9 s | `s25.epj` 840 MB，另有一个空的 `s25\` 目录 | `User.eod` |
| PXF，不带主数据 | `Export.PXFProject(project, 路径, false, true)` | 19.2 s | `s25-nomaster.epj` 598 MB | `User.eod` |
| 部件清单 XML | `PartsService.ExportPartsList(project, 路径, Format.XML)` | 61.9 s | `parts.xml` 14 MB | `User.eod` |
| 部件清单 CSV | `PartsService.ExportPartsList(project, 路径, Format.CSV)` | 89.9 s | `parts.csv` 20 MB，分号分隔 | `User.eod` |
| 设备清单 CSV（r1） | `DeviceService.ExportDeviceList(project, 路径, Format.CSV)` | 0.1 s | `devices.csv` 0 字节 | `User.eod` |
| 标签，文本（r1） | `Label.DoLabel(project, "Device tag list", "en_US", 路径, 1, 1)` | 2.3 s | `labels.txt` 40 KB，定宽文本，每行一个设备标识 | `User.eod` |
| 标签，xlsx（r1） | `Label.DoLabel(project, "Terminal diagram", "en_US", 路径, 1, 1)` | 2.2 s | `terminals.xlsx` 805 KB；期间没有 Excel 进程 | `User.eod` |
| PLC 地址总览（r1） | `PlcService.ExportAddressOverview(project, "", "", "", 路径)` | 0.1 s | `plc-addresses.csv` 11 字节，只有一行空值 | `User.eod` |
| 缺失词表（r1） | `Translate.ExportMissingTranslation(路径, "XTrLanguageDbXml2TabConverterImpl", ["en_US"], [project])` | 3.5 s | `missing.txt` 3.7 KB，制表符分隔，列为 `zh_CN`、`en_US`、`COMMENT` | `User.eod` |

- 文件都只出现在给定的目录中；扩展名 `.epj` 由 EPLAN 补上。
- 标签方案的"上次使用"在导出前后都是 `Device tag list`：传入方案名不改写它。
- 示例项目的翻译语言有 20 种（`de_DE;en_US;…;zh_TW`），显示语言和源语言都是 `##_##`。
- 🔴 **访问冲突**：部件清单 CSV 导出返回之后约 1 秒（03:42:40–41），EPLAN 的系统日志记录了两次"硬件错误：存储器访问受阻"（-1073741819，即 0xC0000005），随即关闭了全部项目数据库。之后：
  - 下一个探针找不到打开的项目；
  - EPLAN 进程还在，但没有任何窗口、没有 Remote Client 端口和 Bridge 管道，CPU 为 0，不再响应；
  - 不能确定冲突发生在部件清单导出的收尾中，还是在下一个 action 被调度时。

## 结论
1. **PXF 导出可行，但文件很大**：示例项目带主数据 840 MB，不带也有 598 MB，耗时 20–40 秒。`eplan_export_pxf` 默认不带主数据，工具描述写明大小和耗时；导出目录的磁盘占用由 `doctor` 报告（M4 设计 Q8）。
2. **部件清单导出慢**（60–90 秒），文件 14–20 MB。工具只返回文件路径和大小，不把内容交给模型；需要时由模型读取文件的一部分。
3. 导出改写 `User.eod`，与 PDF、DXF 导出相同（A16）。
4. **访问冲突没有查清**：r1 中设备清单导出正常，所以 r0 的冲突发生在部件清单 CSV 导出的收尾，或者与连续几次大导出有关。没有单独重复 CSV 导出，以免再次让 EPLAN 卡住。第一批只提供部件清单的 XML 导出。
5. **设备清单导出不是"设备列表"**：`DeviceService.ExportDeviceList` 导出的是预规划用的设备清单，示例项目中没有，所以是空文件。要一份全部设备的清单，用标签方案 `Device tag list`；`eplan_export_list` 不提供 `devices` 这一项，改由 `eplan_export_labels` 覆盖。
6. **标签可行**：文本和 xlsx 都在 2–3 秒内生成，xlsx 不需要运行 Excel。必须给出文件名：方案中的默认目标在 `$(MD_DOCUMENTS)` 下，在导出目录之外。扩展名要与方案的格式一致（`Device tag list` 是文本，`Terminal diagram` 是 xlsx），工具按方案的 `Format` 补上扩展名。
7. **PLC 地址总览要给 PLC 配置项目或站**，否则输出为空；工具要求给出其中之一，或者先在规格中查清怎样列出可选的值。
8. **缺失词表可行**，3.5 秒；语言必须在项目的翻译语言中（XML）。

## 局限
- 只在示例项目、Electric P8、EPLAN 2026 上验证。
- 部件清单 CSV 导出之后的访问冲突没有复现；卡住的 EPLAN 进程（没有窗口、不响应）在新的 EPLAN 启动后不久消失：系统日志中新实例报告了"Other process"，之后两个进程都不在了，是谁结束了它没有查清。
- 是否装有 Excel 没有检查，只确认生成 xlsx 时没有 Excel 进程。

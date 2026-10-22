# Spike S30：对象编号与项目生命周期

> 状态：**完成**（2026-10-21，r1–r3）。M6 设计 4.6 的 spike；全部写入都在 scratch 中。旧版本项目的升级受环境所限，没有测（本机只有 2026.0.3）。

## 问题
M6 的定位符是"项目键 + 类型号 + `ObjectIdentifier`"（S28）。本 spike 回答：
1. 项目属性 `PROJ_GUID` 能否作为项目键，副本、还原、PXF 导入、新建之后怎样变化；
2. 同一份项目文件关闭再打开后，编号是否不变；
3. 删除对象再新建，编号是否被重用（回滚和提交两种情况，以及重新打开之后）；
4. 从同一个基本项目新建的两个项目，编号是否相同；
5. API 新建的项目处于什么打开状态，能否再次打开、关闭。

## 方法
- `SpikeS30Action` 在主线程上经调度器运行（`LockingStep` + `QuietModeStep`），只对 scratch 根目录下的项目做写操作。
- `guid`：读打开的项目的 `PROJ_GUID`、页数和第一页的编号。
- `reopen`：按"名称|页"列出全部主功能和页的 `ObjectIdentifier`（同 S28），`Project.Close()`，再 `ProjectManager.OpenProject(path)`，重新列出并比较。
- `recreate`：在写副本中新建页 A，删除 A，再新建页 B（新名称）和页 C（A 的名称），记录编号；先在一个 `SafetyPoint` 中做完并回滚，再逐步提交做一遍（最后删除新页）。
- `basic`：用 `ProjectManager.CreateProject(链接文件, 基本项目)` 从同一个 `.zw9` 新建两个项目并比较；r2 另记 `ProjectManager` 释放后、再次 `OpenProject` 后项目是否仍打开。`close`（r3）：经 `OpenProjects` 重新取得项目对象后关闭。
- 项目：夹具与写副本（示例项目的文件副本）、S28 r1 的还原项目和 PXF 导入项目；基本项目 `IEC_bas003_sample_project.zw9`、`GB_bas001.zw9`，先复制到 scratch（中文路径不经命令行传递，A30）。
- 环境：EPLAN 2026.0.3.25421，Electric P8。

## 结果
**`PROJ_GUID`**（不是 GUID，而是"`名称@Unix 秒数`"）：

| 项目 | `PROJ_GUID` |
|---|---|
| 夹具、写副本（文件副本）、`backup`/`restore` 还原的项目 | 都是 `ESS_Sample_Project-Kopie@1523807309`（2018-04-15） |
| `eplan_import_pxf` 导入的项目 | `S28r1_pxf@1792638571`（导入的时刻） |
| 从 `GB_bas001.zw9` 新建的两个项目 | `GB_bas001@1792643412`、`GB_bas001@1792643421`（各自新建的时刻） |

**关闭再打开**：还原项目和写副本各 825 个键，**`ObjectIdentifier` 全部不变**；`DatabaseIdentifier` 变化（100 → 194，53 → 230）。关闭 0.14–0.17 秒，打开 0.7–0.85 秒。

**删除再新建**（写副本中的页）：
- 回滚的一轮：A、B、C 依次为 5322、5323、5324；回滚后下一轮从 5325 开始，被回滚的编号没有再分配。
- 提交的一轮：A 5325，删除后 B 5326、C 5327（C 与 A 同名也拿到新编号）。
- 关闭再打开写副本之后，再做一遍从 5328 开始。
- 本次没有观察到编号被重用：编号在项目文件中单调递增，计数随项目保存。

**从基本项目新建**：
- `GB_bas001.zw9`：两个新项目各 1 页（`=CA1+EAA/1`），**页的编号都是 1**；新建 8.5–8.8 秒（S27 中 3.5 秒）。
- `IEC_bas003_sample_project.zw9`：新建的项目 **0 页**（3.0–3.3 秒）。
- API 新建的项目：`ProjectManager` 释放后仍在打开的项目中；对它再调用 `OpenProject` 抛出 `ProjectOpenException`（S063045）；用 `CreateProject` 返回的对象调用 `Close()` 抛出 `ObjectNotCreatedException`（S063080）；这次调用结束后项目已不在打开的项目中。之后用 `ProjectOpen` 正常打开，经 `OpenProjects` 重新取得的对象 `Close()` 成功，`PROJ_GUID` 与新建时相同。

## 结论
1. **`PROJ_GUID` 不能区分同一项目的副本**：文件副本和备份还原沿用原值，只有 PXF 导入和新建会生成新值；它的格式是"名称@秒数"，同名同秒新建的项目会相同。项目键要由 EplanMcp 自己定（例如路径加 `PROJ_GUID`，或写进绑定的标识），`PROJ_GUID` 只作辅助核对。
2. **同一份项目文件中，编号跨关闭再打开不变，删除后不重用**（本次观察）。但两份副本各自新建的对象会拿到相同的编号，从同一个基本项目新建的项目编号也相同：**`ObjectIdentifier` 只在一个项目内唯一**，定位符必须带项目键，使用前仍按名称、页、类型核对。
3. **`DatabaseIdentifier` 每次打开都变**，可以用来判断"打开代次"（M6 设计 4.7），不能保存。
4. **API 新建的项目要按"新建 → 在同一次调用中读需要的内容 → 需要时再打开"处理**：不要对 `CreateProject` 返回的对象调用 `Close()`，也不要对已打开的项目再调用 `OpenProject`（先查 `OpenProjects`）。
5. 第 2 阶段用 `IEC_bas003_sample_project.zw9` 新建的目标项目是空的（0 页），模块要从参考项目中取。

## 局限
- 只测了页的删除和新建；功能、连接等其他对象类型的编号分配没有逐一验证。
- "编号不重用"只是本次观察，文档说编号可能被重用；压缩、重组项目（如 EPLAN 的"重组"）之后没有测。
- 新建的项目为什么在调用结束后不再打开，没有查明（`ProjectManager` 释放后它们仍在打开的项目中）。
- 旧版本项目的打开、升级、`GetProjectDataCompatibility` 的其他返回值：本机没有旧版本样例，受环境所限。
- 在 EPLAN 中"另存为"、复制项目没有测。

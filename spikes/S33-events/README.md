# Spike S33：EPLAN 事件

> 状态：**完成**（2026-10-21，r1、r2）。ADR-0026 的前提（M6 第 4 阶段的缓存失效）。只注册事件处理器；修改只在 scratch 副本上进行。

## 问题
1. 哪些事件名可用、能否用通配符订阅、参数是什么；
2. 事件在哪个线程上触发；
3. 修改项目（包括被 `SafetyPoint` 回滚的修改）、打开和关闭项目时各触发哪些事件、多少次；
4. 处理函数的开销。

## 方法
- `SpikeS33Action`：`listen` 为一组候选名各建一个 `EventHandler(名称)`，订阅 `EplanNameEvent`（回调带事件名），处理函数只记下事件名、参数（`EventParameterString`）、线程号和时间，放进队列；处理器保存在静态列表中。`operate` 在写副本上依次：新建页后回滚、新建页并提交、改页描述并提交、删除页并提交、关闭再打开项目，并标记每个事件属于哪一步。`collect` 汇总并释放处理器。三步在同一次驱动运行中依次执行。
- 候选名：`*`、`onActionStart.String.*`、`onActionEnd.String.*`，以及凭印象列出的若干名称（`Eplan.EplApi.OnPostOpenProject`、`Eplan.EplApi.OnPreCloseProject`、`Eplan.EplApi.OnUserPreCloseProject`、`Eplan.EplApi.OnMainEnd`、`Eplan.EplApi.OnDataChanged`、`NotifyPageOpened/Closed/Changed`、`Page.ConnectionDirty`、`PageManagement.ProjectAlreadyOpen`）。XML 文档中只出现过 `onActionStart.String.*`。
- r1 中 `operate` 在 `LockingStep` 之外调用 `ProjectManager.OpenProjects`，抛出 `NoLockingStepException`（S063110）；r2 把整个 `operate` 放进 `LockingStep`。
- 环境：EPLAN 2026.0.3.25421，Electric P8。

## 结果
- **通配符 `*` 有效**：用一个处理器就收到了所有事件及其名称。`onActionStart.String.*`、`onActionEnd.String.*` 也有效。
- **全部在主线程上触发**（线程 1，与调度器的线程相同），在操作内部同步触发。
- **参数**：`onActionStart/End.String.<action>`、`API.PreOpenProject`、`Eplan.EplApi.OnPostOpenProject` 的参数是字符串（action 名或项目的完整路径）；其他事件的参数不是字符串（`EventParameterString` 抛出 `InvalidCastException`），类型没有查明。
- **r2 各步触发的事件**（共 260 个）：

| 步骤 | 耗时 | 事件数 | 主要事件 |
|---|---:|---:|---|
| 新建页后回滚 | 37 ms | 10 | `onDMObjectsAltered`、`onPreCommitTxn`、`onPreCloseUndo`、`reloadMessages` |
| 新建页并提交 | 6 ms | 8 | 同上 |
| 改页描述并提交 | 3 ms | 5 | 同上 |
| 删除页并提交 | 5 ms | 7 | `onDMObjectsAltered`、`onPreCommitTxn`、`onPreCloseUndo` |
| 关闭再打开项目 | 1.9 s | 169 | `onPreCloseProjectHierarchy`、`onPreCloseProject`、`onPostCloseProject`、`API.PreOpenProject`（路径）、`onBeginOpenProjects`、`onPostOpenProject`、`Eplan.EplApi.OnPostOpenProject`（路径）、`onEndOpenProjects`；每个数据库各一对 `onPreCloseDatabase`/`onPostCloseDatabase` 与 `onPostOpenDatabase`（各 27 次）；主数据库 `onPreCloseMasterdataLibrary`/`onPostOpenMasterdataLibrary`（各 21 次）；`onActionStart/End.String.XPrjActionProjectOpen`、`XPrjUpdateProjectMasterDataOnPrjOpenAction`、`XSDActionSysErrorDlg`（没有窗口出现）；`onPropertyDefTableChanged.Register` |

- **被回滚的修改同样触发 `onDMObjectsAltered`**：事件不能说明修改最终是否保留。
- 空闲时持续出现 `onIdle.Bool.App`、`onLastIdle.Bool.App`、`Inserter.checkChanges`（每次空闲 6 次左右）、`onTimer.UInt.App`；执行任何 action 时有 `Af.OnActionStart/End` 和 `onActionStart/End.String.<action 名>`。
- **没有出现的候选名**：`Eplan.EplApi.OnPreCloseProject`、`Eplan.EplApi.OnUserPreCloseProject`、`Eplan.EplApi.OnMainEnd`、`Eplan.EplApi.OnDataChanged`、`NotifyPage*`、`Page.ConnectionDirty`、`PageManagement.ProjectAlreadyOpen`（至少在这些操作中没有被触发）。目录中也没有 `Page.ConnectionDirty` 这个成员，只有 `ChangeInfoService.ConnectionDirtyActive`（"连接脏位处理"的开关）。

## 结论
1. **可以订阅，且很便宜**：处理函数在主线程上同步运行，只做入队即可；一个通配符处理器就能发现全部事件名。实现时只订阅白名单中的名称（ADR-0026），不长期挂通配符，避免空闲事件的噪声。
2. **缓存失效的候选白名单**（ADR-0026 第 2 条按此修正）：
   - 数据变化：`onDMObjectsAltered`（不带可读的范围；回滚的修改也触发），配合 `onPreCommitTxn`、`onPreCloseUndo`；
   - 项目打开：`API.PreOpenProject`、`Eplan.EplApi.OnPostOpenProject`（带路径）；
   - 项目关闭：`onPreCloseProject`、`onPostCloseProject`（参数类型待查，路径要另取）。
3. **事件只说明"可能变了"，不说明哪里变了**：收到 `onDMObjectsAltered` 后，把该项目的缓存标为待核对，再用页的修改日期（S31，A36）找出变化的页，以内容哈希为准。设计中按 `Page.ConnectionDirty` 区分连接范围的写法不成立，应删除。

## 局限
- 非字符串参数的类型（`onDMObjectsAltered` 等）没有查明；若能读出受影响的对象，可以把待核对的范围缩小。
- 只测了少数操作；插入宏、批量重编号、导入等大批量操作时的事件频率没有测。
- 工程师在界面中操作时的事件没有测（本 spike 不操作界面）。
- 处理器注册后，插件被注销时它们是否自动失效没有查（本 spike 在 `collect` 中显式释放）。

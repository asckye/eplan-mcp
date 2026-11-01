# [EPLAN API 笔记](../api-notes.md)：事务、撤销与调度

### A7. 回滚与撤销（spike S2，2026-10-13，均在插件 action 中执行）
- `SafetyPoint` 和 `Transaction` 的各种放弃方式（不提交释放、`Rollback()`、`Abort()`、`Start()` 后释放、异常）都能撤回新建的页面，也能恢复被修改的页面属性。— [spike S2](../../../spikes/S2-transactions/README.md)。
  - ⚠️ 不能推广到所有写操作：插入宏只有 `SafetyPoint` 能撤回，按符号新建功能只能在 `Transaction` 中进行，见 A13。
- **`UndoStep` 在 `SafetyPoint` 外面创建时，`SafetyPoint` 无法回滚**，包括显式调用 `Rollback()`。`UndoStep` 在 `SafetyPoint` 里面创建，或者 `UndoStep` 包住 `Transaction` 时，回滚正常。— spike S2。
- `UndoStep.DoUndo()` 只在 `CloseUndo()` **之前**调用才有效；在 `CloseUndo()` 或 `CloseOpenUndo()` 之后调用，以及 `DoUndo(true)`，都没有效果。— spike S2。
- action 结束后执行 `XEsUndoAction`（GUI 的撤销，即 Ctrl+Z），会撤销带有已关闭 `UndoStep` 的 AI 修改。每次 action 中的修改各占一个撤销步骤；**此前的撤销历史保持完整**。— spike S2。
- 在 action 中，**没有 UndoStep 的修改同样可以用 Ctrl+Z 撤销，撤销历史也没有被清空**，与 XML 文档（B7）的说法不符。在 action 之外的行为待 S1 验证。— spike S2。
- 在 action 中，`TransactionManager.IsEplanTransactionRunning` 为 false；创建 `Transaction` 后变为 true。`SafetyPoint.GetAutoLockingState()` 在 SafetyPoint 存在期间为 true。— spike S2。
- 对空的 `PropertyValue` 调用 `ToString()` 会抛出 `EmptyPropertyException`（例如新页面的页描述 `PAGE_NOMINATIOMN`），读取前要先检查 `IsEmpty`。多语言属性的原始字符串格式为 `<语言>@<文本>;`，语言无关的文本为 `??_??@…;`。— spike S2。

### A9. 从后台线程调度到 EPLAN 主线程（spike S1，2026-10-13）
- **EPLAN 已经预先设置了主线程调度器**：在主线程上，`EplanMainThreadDispatcher.GetMainThreadDispatcher()` 不为 null，并且与 `Dispatcher.CurrentDispatcher` 是同一个对象。主线程为 STA，托管线程 ID 为 1。`GetMainThreadDispatcher`、`SetMainThreadDispatcher`、`CanAccessMainThread` 都是**静态方法**；`ExecuteInMainThreadSync/Async` 是实例方法。— [spike S1](../../../spikes/S1-dispatcher/README.md)。
- 从后台线程调用 `EplanMainThreadDispatcher.ExecuteInMainThreadSync` 或 WPF 的 `Dispatcher.Invoke`，委托都在主线程上执行；中位延迟约 0.3–0.4 ms（各 20 次）。`CanAccessMainThread()` 在主线程上为 true，在后台线程上为 false。— spike S1。
- **在 action 之外，即使只是读取（例如 `project.Pages`），也必须先创建 `LockingStep`**，否则抛出 `NoLockingStepException`。— spike S1。
- `Dispatcher.Invoke(DispatcherPriority, TimeSpan timeout, Delegate)`：主线程忙时，在超时（1 s）后返回 null，**委托之后也不会再被执行**（等了 2 s 确认）。`ExecuteInMainThreadSync` **没有超时**，会一直等到主线程空闲。— spike S1。
  - 对本项目的影响：Bridge 使用 EPLAN 预设的 WPF `Dispatcher` 加超时，来实现 `EPLAN_BUSY`。
  - 自 ADR-0009 起，插件在 `OnInitGui` 中直接取 `Dispatcher.CurrentDispatcher`，主线程断言用它的 `CheckAccess()`，不再使用 `EplanMainThreadDispatcher`（2024 及更早的版本没有这个类型）。2026 上两者是同一个对象，行为不变。
- `Send`、`Normal`、`Background`、`ContextIdle`、`ApplicationIdle`、`SystemIdle` 各优先级在 EPLAN 空闲时都会立即执行。— spike S1。
- 🔴 **委托中未捕获的异常会直接终止 EPLAN 进程。** 用 `Dispatcher.Invoke(priority, timeout, delegate)` 调度的委托在主线程上抛出 `InvalidOperationException`，没有传回调用方，而是成为 `Dispatcher.ProcessQueue` 中的未处理异常，EPLAN 随即崩溃（Windows 事件日志：.NET Runtime 1026、Application Error 1000，2026-10-13 03:18:02）。— spike S1 第一次运行。
  - 对本项目的影响：**每一个调度到主线程的委托都必须在内部捕获全部异常**，把失败作为返回值交回调用方。这一点只能通过统一的调度封装来保证，不能依赖每个调用者自觉。在委托内部捕获后，失败能正确传回，之后的调度照常工作（S1r2）。
- 在 action 之外，**`SelectionSet.GetCurrentProject()` 和 `Project.ProjectFullName` 也需要 `LockingStep`**，所以 LockingStep 必须是主线程上执行的第一件事。— S1 第一次运行时，因为顺序错误，所有写入都失败了。
- 在调度器中（action 之外），回滚和撤销的规律与 action 中一致：`LockingStep → SafetyPoint`、`LockingStep → SafetyPoint → UndoStep（在内）` 不提交时都会回滚；`UndoStep` 在 SafetyPoint 外面时**不回滚**。经调度器写入、带有 UndoStep 的修改可以用 Ctrl+Z 撤销，此前的撤销历史保持完整；**没有 UndoStep 的写入同样可以撤销，历史没有被清空**。— S1r2。
- ⚠️ **模态对话框打开时，调度的委托仍会被执行**：它在该对话框的消息循环中被重入执行，执行时 EPLAN 主窗口处于禁用状态。在执行前检查 `IsWindowEnabled(EPLAN 主窗口)`，可以可靠地识别并拒绝这种情况（对话框打开时拒绝，关闭后正常执行）。— S1、S1r2（用所有者为 EPLAN 主窗口的 `MessageBoxW` 模拟）。

### A15. 写操作中断（spike S8，2026-10-20，EPLAN 2026.0.3.25421，Electric P8，在调度器中）
详细数据见 [spike S8](../../../spikes/S8-write-interruption/README.md)。
- 🔴 **`SafetyPoint` 不防进程死亡**：EPLAN 每做一步修改就写入项目数据库，回滚只在进程活着时发生。20 页的写入做到第 5 页、尚未提交时用 `taskkill /F` 结束 EPLAN，重启后这 5 页都在；已提交的修改当然也都在。— S8 r3。
  - 对本项目的影响：写操作被中断后，项目可能处于"做了一半"的状态。ADR-0010 中的 B3（把崩溃变成确定的结果）不可行；`OUTCOME_UNKNOWN`、暂停写入、人工核实是唯一正确的处理。撤销历史随进程消失，解除暂停的提示不能建议 Ctrl+Z（M3 设计 4.6 已修改）。
- 🔴 **调度的委托中发生原生访问冲突，EPLAN 进程直接结束**：`AccessViolationException`（损坏状态异常）不会被委托中的 `try/catch` 捕获，WPF 调度器也不处理。事务中已做的修改同样留在项目里。同样的错误在 action 中不会结束 EPLAN（A11）。— S8 r3。
  - 对本项目的影响：编码规范 9.5 第 15 条没有兜底，必须遵守。
- **重开被结束时开着的项目，EPLAN 弹出"检查相容性"**："未按照规定关闭项目 '…'"，选项"是"（打开并检查数据）、"否"（不打开，随后通知"无法打开项目"）、"以后"（打开，下次再检查）。用 Remote Client 执行 `ProjectOpen /Quiet` 时也会弹出，并挡住之后所有的远程调用。— S8 r1–r3，UI Automation 读出。
- **EPLAN 启动时自动重开上次开着的项目**，所以被结束时开着的项目在下次启动时就会弹出上面的对话框。— S8 r1。
- **被结束的会话在 `EplLog.eldb` 的 `TSysSession_v100` 中没有结束时间**，正常退出的会话有；`TSysMsg_v100` 中没有关于中断的记录。— S8。
- 用 UI Automation 访问 EPLAN 的窗口时，如果 EPLAN 的界面线程正忙（例如主线程上正在执行写入），调用会一直等到它空闲。— S8 r2。

### A41. 提交后的撤销粒度、备份副作用、宏读句柄（spike S35 第 1 部分，2026-10-23，EPLAN 2026.0.3.25421，Electric P8，经 Host 的真实工具）
详细数据见 [spike S35](../../../spikes/S35-plan-step-undo/README.md#结果)。全部写入在 scratch 副本中。

- **现有写工具每次调用提交后正好一个撤销步骤**：`XEsUndoAction` 按相反顺序依次撤销删页、页重编号、改设备标识、插入宏、改页描述、一次新建两页、新建一页，之前的撤销历史完整。Destructive 工具先做的备份不增加撤销步骤。没有改变（`not_started`）的设备重编号和被回滚的插入宏，都不留撤销步骤。
- 🔴 **撤销只作用于 EPLAN 的当前项目**：当前项目没有可撤销的步骤时，弹出模态对话框"'撤销'冲突"（"最近一次修改未能撤销：无法执行'撤销'步骤，步骤不存在"），其他项目中的修改不被撤销；对话框关闭前，所有调用返回 `EPLAN_BUSY`（ModalDialog）。执行撤销前先核对当前项目。
- 用 `XPrjActionProjectClose` 依次关闭当前项目，可以让剩下的项目成为当前项目（A11）。只剩一个项目时，用 `ProjectOpen` 打开另一个，它成为当前项目（只观察了一次）。
- Destructive 工具的备份不改变当前项目和打开的项目，副本不在 EPLAN 中打开。约 325 页的项目上，一次 Destructive 调用（脚本审批、`CopyProject` Snapshot 加页数核对、写入）总共 1.5–2.9 秒。
- 另一个进程以 `FileAccess.Read`、`FileShare.Read` 持有 `.ema` 时，`eplan_macro_insert` 照常提交（28 个对象，69–82 ms）；同时以写方式打开它失败（共享冲突，`0x80070020`）。
- 每次 `XEsUndoAction` 经 Remote Client 约 2.3 秒；Remote Client 按计算机名连接失败，按 IPv4 可以连接。

### B6. 线程
- **只能在 EPLAN 主线程上调用 API。** 官方说明："Executing API code purely out of main Eplan thread is not recommended… not tested and are out of support." — XML（`EplanMainThreadDispatcher`）。
- 官方提供 `Eplan.EplApi.Base.Internal.EplanMainThreadDispatcher`（Baseu）：`ExecuteInMainThreadSync(...)`、`ExecuteInMainThreadAsync(...)`、`CanAccessMainThread()`，以及静态方法 `SetMainThreadDispatcher(System.Windows.Threading.Dispatcher)`、`GetMainThreadDispatcher()`。没有设置调度器时调用会抛异常。— 反射 + XML。
  - 未验证：EPLAN 的 GUI 进程是否已自动设置该调度器。M1 spike 验证。
  - 注意：这个类型位于 `Internal` 命名空间，但有官方文档。使用时封装在适配层中，以便日后替换。
- `LockingStep`：明确不支持多线程。— XML。

### B7. 事务、锁定、撤销、静默模式（`Eplan.EplApi.DataModel`，除非另行注明）
- `SafetyPoint`：静态 `Create()`，以及 `Start()`、`Commit()`、`Rollback()`、`Dispose()`。存在期间自动锁定对象；**不调用 `Commit` 就会回滚**；提交时释放锁。— 反射 + `2026/Locking.html`。
  - 实测（spike S2，在 action 中）：不提交即回滚**成立**，`Rollback()`、`Start()` 后释放、中途抛异常也都能回滚，修改已有属性同样会恢复。**例外：如果 `UndoStep` 在 SafetyPoint 外面创建，回滚会静默失效**（即使显式调用 `Rollback()`）。S0 中观察到的"不回滚"正是这个原因。详见 A7。
- `LockingStep`：释放其生存期内获得的锁，可以嵌套。在非模态对话框、离线程序和校验中**必须显式创建**；action 内部会隐式创建一个。— XML。
  - 对本项目的影响：Bridge 通过调度器执行的代码**不在 action 内**，所以必须自己创建 `LockingStep`。
- 缺少 `LockingStep` 时抛出 `NoLockingStepException`（派生自 `DataModelException`）。`Project.LockAllObjects()`、`StorableObject.LockObject()` / `SmartLock()`；锁定失败时抛出 `Eplan.EplApi.Base.LockingException`，其 `GetAllFailed2LockAsString()` 列出锁定失败的对象。— 反射 + XML。
- **撤销**：`UndoManager.CreateUndoStep()` 返回 `UndoStep`（`SetUndoDescription(string)`、`CloseUndo()`、`DoUndo(...)`）。**如果通过 API 修改项目时没有创建 `UndoStep`，EPLAN 会清空撤销历史。** — 反射 + XML。
  - 对本项目的影响：每个写操作都必须创建并关闭 `UndoStep`，否则工程师会失去手工操作的撤销历史。
- `TransactionManager.CreateTransaction()` 返回 `Transaction`（`Commit`、`Abort`、`Store`）。— 反射。与 `SafetyPoint` 的分工见 A7、A13（S9：插入宏只有 `SafetyPoint` 能撤回，按符号新建功能只能用 `Transaction`）。
- `QuietModeStep(QuietModes)`（AFu）配合 `using` 使用。`QuietModes`：`ShowAllDialogs=0`、`ShowNoDialogs=1`、`ShowSomeDialogs=2`。— 反射。

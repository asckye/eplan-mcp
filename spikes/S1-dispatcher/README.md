# Spike S1：主线程调度

| 项 | 内容 |
|---|---|
| 问题 | Bridge 在管道（后台）线程上收到请求，能否安全、可靠地把 EPLAN API 调用切换到主线程执行？能否在 EPLAN 忙时按时放弃？在调度器中（而不是 action 中）执行时，S2 关于回滚和撤销的结论是否仍然成立？ |
| 日期 | 2026-10-13 |
| 环境 | EPLAN 2026.0.3，Electric P8 变体 |
| 结论 | **可行**。使用 EPLAN 预设的 WPF `Dispatcher` 加超时进行调度，延迟约 0.3 ms。必须遵守 4 条规则：委托内部捕获全部异常；LockingStep 作为第一步；UndoStep 放在 SafetyPoint 里面；主窗口被禁用（有模态对话框）时拒绝执行 |

## 方法
- 插件 action 在主线程上取得调度器，然后启动一个后台线程运行某个场景，自己立即返回。这样主线程处于空闲状态，和将来 Bridge 在两次请求之间的状态一致。后台线程把结果写成 JSON。
- [run_s1.py](run_s1.py) 依次运行各个场景，再运行撤销序列：基线修改（在 action 中）→ 经调度器写入 → Ctrl+Z（`XEsUndoAction`）→ 检查。驱动脚本在每一步之前确认 EPLAN 仍在运行，一旦退出就立即中止。
- 第一次运行导致 EPLAN 崩溃（见下文），修复后用新的程序集名 `S1r2` 重跑了回滚、模态防护和撤销部分（A8：同一个 EPLAN 会话中无法卸载已加载的程序集）。

## 结果

| 场景 | 结果 |
|---|---|
| 调度器是否就绪 | EPLAN 已经预先设置了主线程调度器（`GetMainThreadDispatcher()` 不为 null，并且就是主线程的 `Dispatcher.CurrentDispatcher`）；主线程为 STA |
| 调用落点与延迟 | `ExecuteInMainThreadSync` 和 `Dispatcher.Invoke` 各调用 20 次，全部在主线程上执行；中位延迟 0.31 / 0.42 ms，最大 2.4 / 0.9 ms |
| `CanAccessMainThread()` | 主线程上为 true，后台线程上为 false（静态方法） |
| 各种调度优先级 | 从 `Send` 到 `SystemIdle` 全部立即执行 |
| 主线程忙 3 s 时，`Invoke` 设 1 s 超时 | 1029 ms 时返回 null；**委托之后也没有被执行**（又等了 2 s 确认） |
| 主线程忙时，`ExecuteInMainThreadSync` | 没有超时，阻塞到主线程空闲为止（2694 ms） |
| 委托内抛出异常（第一次运行） | 🔴 **EPLAN 进程终止**：异常没有传回调用方，而是成为 `Dispatcher.ProcessQueue` 中的未处理异常（.NET Runtime 事件 1026） |
| 委托内部捕获异常（安全写法） | 失败作为结果传回调用方；之后调度照常工作 |
| 不创建 LockingStep 读取 `project.Pages` | `NoLockingStepException` |
| 先获取当前项目、读取项目路径，再创建 LockingStep（第一次运行的 bug） | 同样是 `NoLockingStepException`：在 action 之外，**连 `GetCurrentProject` 和 `ProjectFullName` 都需要 LockingStep** |
| LockingStep → SafetyPoint，不提交 | ✅ 回滚 |
| LockingStep → SafetyPoint → UndoStep（在内），不提交 | ✅ 回滚 |
| LockingStep → UndoStep（在外）→ SafetyPoint，不提交 | ❌ **不回滚**（与 S2 相同的陷阱） |
| 模态对话框打开时调度（第一次运行） | ⚠️ **我们的委托在对话框的消息循环中被执行了**（执行时主窗口处于禁用状态） |
| 模态防护：执行前检查 `IsWindowEnabled(EPLAN 主窗口)` | 对话框打开时拒绝（`EPLAN_BUSY`）；关闭后正常执行 |
| 撤销序列：基线 61 → 调度器写入 62（UndoStep 在内）→ Ctrl+Z → Ctrl+Z | 依次撤销 62、61：Ctrl+Z 可以撤销经调度器的写入，此前的历史保持完整 |
| 撤销序列：基线 64 → 调度器写入 65（**没有** UndoStep）→ Ctrl+Z → Ctrl+Z | 依次撤销 65、64：在调度器中也**没有**清空撤销历史，与 XML 文档不符 |

## 结论（Bridge 的 `MainThreadDispatcher` 必须遵守）
1. 使用 EPLAN 预设的 WPF `Dispatcher`（`EplanMainThreadDispatcher.GetMainThreadDispatcher()`），通过 `Invoke(priority, timeout, …)` 调度。超时返回 `EPLAN_BUSY`，而且不会有延迟执行的风险。不使用没有超时的 `ExecuteInMainThreadSync`。
2. 🔴 **委托必须在内部捕获全部异常**，否则 EPLAN 进程终止。只能通过统一封装进行调度。
3. 在主线程上执行的第一件事是创建 `LockingStep`，在它之前不访问任何 EPLAN 对象，包括获取当前项目。
4. 写操作：`LockingStep` → `SafetyPoint` → `UndoStep`（在内）→ 提交 → 关闭撤销步骤（与 S2 一致，并在调度器中得到确认）。
5. 执行前检查 EPLAN 主窗口是否被禁用；被禁用说明有模态对话框打开，此时拒绝执行并返回 `EPLAN_BUSY`，否则我们的代码会在该对话框的消息循环中被重入执行。同理，也要防止我们自己的操作被重入（Host 端每个实例只有一个串行队列，Bridge 端再加一个"正在执行"标志）。

## 局限
- 插件是在 action 中延迟加载的。将来 Bridge 在启动时（`OnInitGui`）加载，那时调度器是否已经就绪，在打通骨架时验证。
- 有意**没有**测试 `ExecuteInMainThreadSync` 中抛出异常的情况，以免 EPLAN 再次崩溃。规则 2 对所有调度方式都适用。
- 只用 `MessageBoxW` 模拟了模态对话框（所有者为 EPLAN 主窗口）。EPLAN 自身的对话框也会禁用主窗口，但没有逐一验证。

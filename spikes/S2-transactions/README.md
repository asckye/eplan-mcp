# Spike S2：回滚与撤销

| 项 | 内容 |
|---|---|
| 问题 | 哪种 API 写法能真正撤回失败或放弃的修改？工程师能否用 Ctrl+Z 撤销 AI 的操作，同时不破坏自己的撤销历史？（起因：S0 中未提交的 SafetyPoint 没有回滚） |
| 日期 | 2026-10-13 |
| 环境 | EPLAN 2026.0.3，Electric P8 变体；全部探针都在**插件 action 中**执行 |
| 结论 | **UndoStep 必须放在 SafetyPoint 里面**（或者改为包住 Transaction）。UndoStep 套在 SafetyPoint 外面时，回滚会**静默失效**。正确写法下，Ctrl+Z 可以撤销 AI 的操作，而且工程师原有的撤销历史保持完整 |

## 方法
- 插件 `EplanMcp.Spikes.S2` 的 action 支持多种模式：`probes`（11 种写法，各自写入后再放弃，检查是否撤回）、`op`（按指定的撤销方式做一次已提交的修改）、`check`、`cleanup`。
- [run_s2.py](run_s2.py)：运行全部探针，然后"建页面 → 执行 `XEsUndoAction`（即 GUI 的 Ctrl+Z）→ 检查"。
- [run_s2_undo.py](run_s2_undo.py)：用多次独立的 action 调用，模拟"工程师操作 → AI 操作 → 工程师按 Ctrl+Z"的顺序。
- 在同一个 EPLAN 会话中，插件程序集无法卸载，所以每次重新构建都换用新的程序集名（`-p:AssemblyName=EplanMcp.Spikes.S2r2`、`r3`），action 名称随之变化（`EplanMcpSpikeS2r2`）。

## 结果

### 回滚（写入后放弃，修改是否被撤回）

| 写法 | 撤回 |
|---|---|
| `SafetyPoint` 不提交直接释放 | ✅ |
| `SafetyPoint.Rollback()` | ✅ |
| `SafetyPoint.Start()` 后释放 / 后 `Rollback()` | ✅ / ✅ |
| `SafetyPoint` 中途抛出异常 | ✅ |
| `Transaction.Abort()` / 不提交直接释放 / 项目级 `TransactionManager(project)` 的 `Abort()` | ✅ / ✅ / ✅ |
| 修改已有页面的描述后 `SafetyPoint.Rollback()` / `Transaction.Abort()` | ✅ 恢复原值 / ✅ 恢复原值 |
| **UndoStep 在外、SafetyPoint 在内，释放或显式 `Rollback()`** | ❌ **不撤回**（S0 的现象） |
| UndoStep 在 SafetyPoint 内、不提交 | ✅ |
| UndoStep 在外、`Transaction.Abort()` | ✅ |

### 撤销

| 场景 | 结果 |
|---|---|
| 代码调用 `UndoStep.DoUndo()`：在 `CloseUndo()` 之前 | ✅ 撤销 |
| 代码调用 `DoUndo()`：在 `CloseUndo()` / `CloseOpenUndo()` 之后，或 `DoUndo(true)` | ❌ 无效 |
| action 结束后执行 `XEsUndoAction`（Ctrl+Z），修改带有已关闭的 UndoStep | ✅ 撤销 |
| 序列：工程师的修改 61 → AI 修改 62（UndoStep 在 SafetyPoint 内）→ Ctrl+Z → AI 修改 63（UndoStep + Transaction）→ Ctrl+Z → Ctrl+Z | 依次撤销 62、63、61：**AI 的每次操作各占一个撤销步骤，此前的历史保持完整** |
| 序列：修改 64（有 UndoStep）→ 修改 65（**没有** UndoStep）→ Ctrl+Z → Ctrl+Z | 依次撤销 65、64：**在 action 中，没有 UndoStep 也没有清空撤销历史** |

### 其他观察
- 在 action 中，EPLAN 没有处于隐式事务中（`IsEplanTransactionRunning = false`）。创建 `Transaction` 后，该标志变为 true。
- 读取空的属性（例如新页面的 `PAGE_NOMINATIOMN`）时，`ToString()` 抛出 `EmptyPropertyException`，所以要先检查 `PropertyValue.IsEmpty`。
- 多语言属性的原始字符串格式为 `<语言>@<文本>;`，语言无关的文本显示为 `??_??@…;`。

## 结论
1. 写操作的包装顺序定为：**`LockingStep` → `SafetyPoint` → `UndoStep`（在 SafetyPoint 内创建）→ `QuietModeStep`**。成功时先 `Commit()` 再 `CloseUndo()`；失败时不提交，让 SafetyPoint 回滚。这同时满足"失败回滚"和"Ctrl+Z 一次撤销 AI 的整个操作"。另一种可行写法是 `UndoStep` 包住 `Transaction`。
2. **禁止**把 UndoStep 放在 SafetyPoint 外面，这会让回滚静默失效。
3. 以上都是在 **action 中**测得的。Bridge 实际通过主线程调度器执行，不在 action 中，所以必须在 S1 中**重新验证**同样的写法（包括"没有 UndoStep 是否清空历史"）。
4. 开发流程：插件程序集在 EPLAN 进程退出前无法卸载。重新编译后，要么重启 EPLAN，要么改用新的程序集名；安装程序更新插件后，需要提示用户重启 EPLAN。

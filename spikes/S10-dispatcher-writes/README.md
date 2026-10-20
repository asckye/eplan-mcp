# Spike S10：在 Bridge 调度器中写入（M3b）

## 问题
[M3b 设计](../../docs/design/2026-10-20-m3b-placement.md)第 4.5 节列出的问题。S9 全部在 action 中运行，而工具是在 Bridge 的调度器中执行的，所以都要在调度器中重新验证：
1. 宏中的设备与已有设备同名时，`NumerationMode` 各取值的结果；
2. 插入宏、生成连接之后，Ctrl+Z 能否一步撤销；
3. `SafetyPoint` 能否回滚 `Generate.Connections`，"只更新"会不会删除连接；
4. 不插入时能否读出宏的内容；打开宏是否改动项目；
5. 读取页幅面的 API；
6. 在调度器中写入时 `User.eod` 是否被改写；
7. 按库符号新建设备：用 `UndoStep` 包住 `Transaction` 时能否用 Ctrl+Z 撤销，设备标识能否显示。

## 方法
- `SpikeS10Action`：action 只做两件事：在主线程上取 `Dispatcher.CurrentDispatcher`，再启动一个后台线程，然后立即返回。后台线程等 action 返回后，用 `Dispatcher.Invoke` 把探针送回主线程执行，与 Bridge 的执行方式相同：不在 action 中，自己创建 `LockingStep` 和 `QuietModeStep`。
  - 写入用 Bridge 将要使用的包装（M3 设计 4.2）：`SafetyPoint` → 其中的 `UndoStep` → `QuietModeStep`，先 `Commit()`，再 `CloseUndo()`。
  - 委托内部捕获全部异常（S1：异常逃出调度的委托会让 EPLAN 退出）。报告先写成 `.partial`，完成后再改名。
- `Driver/`：与 S9 相同，通过 Remote Client 驱动。另外做了三件事：
  - 每一步都等报告文件出现；
  - 在几个模式前后对比项目 `.edb` 中的文件；
  - 写入后执行 `XEsUndoAction`（Ctrl+Z）。
- **Ctrl+Z 的保护**（CLAUDE.md 硬性规则 1）：
  - 只有在 EPLAN 打开的项目全部是 scratch 副本、并且 S10 的副本是当前项目时，驱动才发送 Ctrl+Z；
  - r1、r2 中开着评测夹具，而且它是当前项目，驱动没有发送；
  - r3 先用 `Edit.SelectProjectInPagesNavigator` 选中 S10 的副本，结束时再选回夹具。这只改变界面上的选择。
- 环境：EPLAN 2026.0.3.25421，Electric P8 变体，2026-10-20，共 3 轮（r1–r3）。

运行：

```powershell
dotnet build spikes\S10-dispatcher-writes\EplanMcp.Spikes.S10.csproj -c Release -p:AssemblyName=EplanMcp.Spikes.S10r3 -p:AssemblyTitle=EplanMcp.Spikes.S10r3
dotnet build spikes\S10-dispatcher-writes\Driver -c Release
spikes\S10-dispatcher-writes\Driver\bin\Release\net481\EplanMcp.Spikes.S10.Driver.exe --host <本机网卡地址> --revision r3
```

原始结果在 `.local/spikes/s10/`，不提交。

## 结果
### 1. 插入宏时的编号方式
同一个宏（`Contactor_Feedback_Control.ema`）在 `=GAA+A1` 下的 5 个新页上各插入一次，第一次之后，宏中的标识都已存在：

| 方式 | 电缆（主功能） | 端子排 | 只有辅助功能的触点 | PLC 端子 |
|---|---|---|---|---|
| `None`（第一次） | `+A1-WG6` | `+A1-XG5:?`、`+A1-XG8:?` | `=GAA+A1-QA1` | `=K1+A1:1.4` 等 |
| `None`（再次） | `+A1-WG6`，**项目中有了两个同名的主功能** | 同上 | 同上 | 同上 |
| `Number` | `+A1-WG7` | `+A1-XG9:?1` … `XG11:?3` | `=GAA+A1-?QA1` | 不变 |
| `NumberPreferPrefix` | `+A1-WG8` | `XG12` … `XG14` | `-?QA2` | 不变 |
| `NumberWithQuestionMark` | `+A1-?WG1` | `-?XG1` … `?XG3` | `-?QA3` | 不变 |

- `None` 保留宏中的标识，结构跟随目标页，因而产生重复的设备；
- `Number` 分配不冲突的新标识；没有主功能、无法确定归属的触点，标识前带 `?`；
- PLC 端子保留宏自带的结构 `=K1`，四种方式都不改。

### 2. Ctrl+Z（r3，S10 副本为当前项目）
| 写入（在调度器中，各自一个 `UndoStep`） | Ctrl+Z 之后 |
|---|---|
| 插入宏（页 `/75`，12 个功能） | ✅ 页上 0 个功能，其他页不变 |
| 生成连接（页 `/76`，0 → 9 个连接） | ✅ 回到 0 个连接，宏插入的 12 个功能还在，只撤销了这一步 |
| 按符号新建线圈（`UndoStep` 包住 `Transaction`） | ✅ 线圈消失 |

### 3. 生成连接的回滚
- 在 `SafetyPoint` 中 `Generate.Connections([page], false)` 后 `Rollback()`：页上的连接 0 → 9 → 0，与之前完全相同。
- 对已有 40 个连接的页 `=GAA+A1&EFS1/1`，"只更新"和"重建全部"都没有增删连接（都已回滚）。

### 4. 读取宏的内容
- `WindowMacro.Open(path, project)` 之后，不插入也能读出：
  - 表达类型（`MultiLine`），`GetVariants(MultiLine)` 为 0–5；
  - `Size` 52 × 268 mm，`GetBoundingBox()`；
  - `Objects` 共 28 个对象，其中 19 个功能，带宏自己的名称。结构是占位，如 `+-XG8:?`、`=+A1-QA1`。
- 打开宏前后，项目的功能数不变（2876），`.edb` 中没有文件变化。

### 5. 页幅面
- 示例页 `=GAA+A1&EFS1/1` 的 `Page.Size` 为 420 × 297.02 mm，`GridSize` 为 4。`GetBoundingBox()` 是页上全部对象的外框。
- **新建的空页读 `Page.Size` 抛出 `InvalidOperationException`**（"S063136无法确定页尺寸"）。
- 插入点：宏用 `MoveKind.Absolute` 插在 (40, 200) 时，在导出的图上，宏外框的左上角落在 (40, 200)。这个宏高 268 mm，下半截因此压进了标题栏。只观察到这一次。

### 6. 文件变化
- 调度器中只找项目（`noop`）、以及打开并读取宏（`macroinfo`）：项目 `.edb` 中**没有任何文件变化**，`User.eod` 也没有。
- 这与 S6 不同：S6 在 action 中调用了 DataModel API，`User.eod` 被改写（A11）。本次的 action 只读取参数，不调用 DataModel API。

### 7. 按库符号新建设备
- 在调度器中，`UndoStep` 包住 `Transaction`：直接 `Function.Create` 抛出 `LockingException`（"写存取前没有加锁该对象"）。action 外没有隐式锁定；先 `page.LockObject()` 就成功了。
- 新线圈有 12 个属性放置，`VisibleName` 为空。`PropertyPlacementsSchemas.All` 有"默认"和"用户定义"两种，选择"默认"后没有变化。
- **导出的图上，线圈旁边没有设备标识，也没有连接点编号**。宏插入的设备则都有（`-?QA1`、`-XG9`、`-WG7` 等）。

## 结论
1. **M3b 的 Q2 成立**：`eplan_macro_insert` 默认用 `Number`，不会产生重复设备。读回时要处理两种标识：
   - 带 `?` 的标识（无法确定归属的触点）；
   - 带 `?` 编号的端子（`:?1`）。

   这两种都在结果中原样列出，并提示工程师在 EPLAN 中确认。`keep`（`None`）会产生同名主功能，规划或读回时发现就回滚。
2. **M3a、M3b 的包装在调度器中都可行**：
   - 插入宏、生成连接都能被 `SafetyPoint` 回滚；
   - 提交后各占一个撤销步骤，Ctrl+Z 只撤销那一步；
   - `SafetyPoint` 在调度器中自动锁定，不需要手动锁。
3. **`eplan_macro_list` 的 `detailed` 可以只读地给出宏的内容和尺寸**，打开宏不改动项目。
4. **规划阶段要检查插入后的外框是否落在页内**：
   - 外框 = 插入点 + 宏的外框；
   - 页幅面在新的空页上读不到，要另找来源（如图框，没有验证）；读不到时退回只检查插入点。
5. **Q1 保持推迟**：
   - 按符号新建的设备能在调度器中创建（要先锁页），也能用 Ctrl+Z 撤销；
   - 但设备标识不显示，没有找到让它显示的 API。
6. 只读工具和宏的读取在调度器中都不改动项目文件，A11 中"action 会改写 `User.eod`"的问题不影响 Bridge。

## 局限
- 只在一个项目、一个宏、Electric P8、EPLAN 2026 上验证。
- 插入点与外框的关系只看了一张图；页幅面的其他来源（图框 `PlotFrame`）没有试。
- Ctrl+Z 只在 S10 副本是当前项目时测试。Ctrl+Z 作用于哪个项目，没有单独验证；为了安全，测试时只让 scratch 副本成为当前项目。
- 没有试 `bDontResolveGroups` 参数、符号宏、多个变体。

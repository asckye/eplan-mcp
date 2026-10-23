# Spike S35：计划步骤的撤销粒度、备份副作用与宏读句柄（第 1 部分）

> 状态：**第 1 部分完成**（2026-10-23）。[M6 第 2 阶段设计](../../docs/design/2026-10-23-m6-phase2-plans.md)第 9 节 U1 的前半：只测现有工具路径。新的 `plan.step.execute` 和命名撤销工厂的组合，在 P13、P17 的 Large 测试中再测。全部写入都在 scratch 副本 `scratch\writes\LargeWrite` 中。

## 问题
1. 现有写工具每次调用提交后，EPLAN 的撤销（`XEsUndoAction`，即 Ctrl+Z）是不是正好一步一次、按相反顺序？之前的撤销历史是否完整？Destructive 工具先做的备份会不会多出撤销步骤？
2. 回滚的写入和没有改变的写入（`not_started`）会不会留下空的撤销步骤？
3. Destructive 工具的备份（`CopyProject` Snapshot 加页数核对）会不会改变当前项目和打开的项目？耗时多少？
4. 另一个进程以只读、只共享读取的方式持有 `.ema` 时，EPLAN 能否照常插入？此时写入者是否被拒绝？

## 方法
- 驱动：本目录的 [S35PlanStepUndoTests.cs](S35PlanStepUndoTests.cs)。运行时临时编入 `tests/EplanMcp.Large.Tests`（在 csproj 中加 `<Compile Include="../../spikes/S35-plan-step-undo/S35PlanStepUndoTests.cs" />`，不提交，也不属于测试套件），经 `DestructiveHost` 调用 Host 的真实 MCP 工具。Destructive 工具的审批来自 Large 测试的脚本渠道：它只批准测试事先登记的变更清单，不点击任何对话框。日志写到环境变量 `S35_LOG` 指定的文件。
- 环境：EPLAN 2026.0.3.25421，Electric P8；`eng\scripts\eval-fixture.ps1 -Open -WriteCopies -Force`。Host 为 main `c070a38d` 的 Release 构建；插件为开发注册的 Release 版，握手报告 `bridgeVersion` 0.1.0。
- 撤销：经 Remote Client（`.local/tools/eplremote`，按本机的 IPv4 连接；环境变量 `EPLREMOTE_HOST`、`EPLREMOTE_PORT`，宏文件的完整路径在 `S35_MACRO_FILE`）执行 `XEsUndoAction`。每次撤销后用读工具读回状态：测试页的名称和描述，以及 P1 上 `eplan_page_get` 列出的设备。再与每步之后记下的状态比较。
- EPLAN 的撤销只作用于当前项目（见结果），而 Large 测试开始时要求当前项目是夹具。所以测试开始后先用 `XPrjActionProjectClose` 依次关闭夹具和 LargeOutside，每次关闭前都核对当前项目不是 LargeWrite，直到 LargeWrite 成为当前项目。
- 步骤（每步一次工具调用）：s0 新建页 P0（作为之前的历史）→ s1 新建页 P1、P2（一次调用）→ s2 改 P1 的描述 → s3 在 P1 插入示例宏 `Contactor_Feedback_Control.ema`（number）→ s4 改宏中一个设备的标识 → s5 对 P1 做设备重编号（Destructive）→ s6 页 P2 重编号到 /15x（Destructive）→ s7 删除这一页（Destructive）。之后撤销"改变了副本的步数 + 1"次。
- 回滚：新建一页，插入宏，再在同一点插入同一个宏（EPLAN 回滚，MacroInsertTests 中的情形），然后撤销两次。
- 备份：每个 Destructive 调用前后读取 `session_status`、`project_list`、`project_get`。
- 宏读句柄：一个 PowerShell 进程用 `File.Open(path, Open, Read, Read)` 持有示例宏；测试进程尝试以写方式打开它，然后插入这个宏。
- 共运行 6 次。前 3 次没有真正执行撤销：通过的测试不输出日志，撤销客户端也没有指定地址，连接失败。第 4 次在夹具为当前项目时撤销，EPLAN 弹出模态对话框，用 WM_CLOSE 关闭。第 5 次得到主结果，第 6 次检查回滚。每次结束都删除测试页。

## 结果
**撤销粒度**（第 5 次，P0 = /5，P1 = /6，P2 = /7）：

| 撤销 | 读回的状态 | 撤销了哪一步 |
|---|---|---|
| 1 | 与 s6 之后相同（/157 回来） | s7 删页 |
| 2 | 与 s4、s5 之后相同（/157 变回 /7） | s6 页重编号；s5 没有改变，没有撤销步骤 |
| 3 | 与 s3 之后相同（`+A1-WG209` 变回 `+A1-WG20`） | s4 改设备标识 |
| 4 | 与 s2 之后相同（宏的设备消失） | s3 插入宏 |
| 5 | 与 s1 之后相同（描述清空） | s2 改页描述 |
| 6 | 与 s0 之后相同（P1、P2 消失） | s1 新建两页，一次调用一步 |
| 7 | 测试页全部消失 | s0 新建 P0 |
| 8 | 出现 /155 | 上一次运行中删掉的页：之前的历史完整 |

- 每次撤销约 2.3 秒（含 Remote Client 连接）。
- s5 设备重编号的结果为 `not_started`，没有改变任何设备：示例宏中没有线圈的触点保持 `?`，与 A20 相同。所以没有观察到它提交后的撤销。

**回滚**（第 6 次）：第二次插入返回 `INVALID_ARGUMENT`，`rolled_back`，副本没有变化。第一次撤销后，状态为"新建的空页"，也就是撤销了提交的那次插入；第二次撤销删掉这一页。回滚的写入没有留下撤销步骤。

**当前项目**：
- 第 4 次撤销时，夹具是当前项目，它没有可撤销的步骤。EPLAN 弹出模态对话框"'撤销'冲突"："最近一次修改未能撤销：无法执行'撤销'步骤，步骤不存在。"
- 在对话框关闭之前，所有调用都返回 `EPLAN_BUSY`（`busyReason: ModalDialog`）。
- LargeWrite 中的修改没有被撤销。

**备份**：
- 三个 Destructive 调用各耗时 1.5–2.0 秒（第 5 次）和 2.6–2.9 秒（第 3 次）。这个时间包括脚本审批（立即应答）、备份（`CopyProject` Snapshot 加页数核对）和写入本身；LargeWrite 约 325 页。
- 调用前后，EPLAN 进程、当前项目和打开的项目列表都没有变化，备份副本没有在 EPLAN 中打开。只有删页让 LargeWrite 的页数如预期少了一页。

**宏读句柄**：
- 持有期间，测试进程以写方式打开被拒绝，结果为 `IOException`，`0x80070020`，即共享冲突。
- 同时 `eplan_macro_insert` 正常提交：28 个对象，69–82 ms。

**项目打开**：
- 夹具为当前项目时，用 `ProjectOpen` 打开 LargeOutside、LargeWrite，当前项目不变，与 A11 相同。
- 只剩 LargeWrite 时再用 `ProjectOpen` 打开夹具，夹具成为当前项目。这只观察了一次。

## 结论
1. **现有写工具每次调用正好对应一个撤销步骤**：按相反顺序撤销，之前的历史完整。Destructive 工具的备份不增加撤销步骤；没有改变（`not_started`）或回滚的调用不留撤销步骤。计划逐步执行时，"每个有效步一个撤销步骤"有现有路径作依据。命名工厂下的撤销名称和粒度，仍要在 P17 的 Large 测试中验证。
2. **撤销只作用于 EPLAN 的当前项目**。当前项目没有可撤销的步骤时，EPLAN 弹出模态对话框，挡住所有调用。由此：
   - 审批文案和使用文档要说明：撤销前先在 EPLAN 中把目标项目设为当前项目；
   - 工具不能自动执行撤销，这与设计 4.4"不自动补偿"一致；
   - 测试和 spike 执行撤销前必须先核对当前项目。
3. **备份不改变当前项目和打开的项目**，副本不在 EPLAN 中打开。Destructive 调用的总耗时为秒级（1.5–2.9 秒，约 325 页），在设计的初始备份预算（60 秒）之内。
4. **另一个进程以只读、只共享读取的方式持有宏文件时**，EPLAN 仍能读取并插入，写入者被拒绝。设计 4.4 中"核对 SHA-256 后持 FileShare.Read 句柄直到插入结束"可行。

## 局限
- 没有验证新的 `plan.step.execute`、`PlanUndoScopeFactory` 下的撤销名称和粒度（P17）。
- `device.renumber` 在本夹具上没有可编号的设备，没有观察到它提交后的撤销。
- 撤销步骤的名称没有读出（EPLAN 的撤销列表只在界面中可见），只按状态比较。
- 状态只比较测试页的名称、描述和 `eplan_page_get` 列出的设备（宏插入后只列出 1 个设备）；图形、占位符、连接没有比较。
- 选择集和图形编辑器中打开的页是否变化，经工具无法观察。
- 每种情形只运行一次，只有一个 EPLAN 版本和变体。

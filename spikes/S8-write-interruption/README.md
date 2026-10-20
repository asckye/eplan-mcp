# Spike S8：写操作中断

## 问题
[ADR-0010](../../docs/adr/0010-write-outcomes.md) 要求在写工具发布之前确认：写操作做到一半时 EPLAN 被强制结束，项目会处于什么状态？
- U2：提交之前被结束，事务中已做的修改会不会被丢弃？
- U3：提交之后、操作返回之前被结束，修改是否保留？
- U4：A11 的原生访问冲突发生在事务中，EPLAN 是否还活着，项目会怎样？

如果"已提交的保留、未提交的丢弃"成立，ADR-0010 可以另写 ADR 引入 B3，把一部分崩溃变成确定的结果。

U1（Host 超时或断开后向 Bridge 取回结果）不在本 spike 中：它不涉及 EPLAN 进程的死亡，已由 Medium 测试覆盖。第一个写工具出来后，再在实机上走一次。

## 方法
- `SpikeS8Action`：与 S10 一样，action 只调度探针，探针经 `Dispatcher.Invoke` 在主线程上执行。写入用 Bridge 的包装（`SafetyPoint` → 其中的 `UndoStep` → `QuietModeStep`）。
  - `slowwrite`：从 `/First` 开始逐页新建 `=GAA+A1&EFS1/<n>`，每页之后在标记文件中记一行，并停顿 `/DelayMs`；提交后记 "committed"，再占住主线程 `/HoldMs`；
  - `violation`：在打开的 `SafetyPoint` 中新建 3 页后，重现 A11 的访问冲突；
  - `count`：列出探针页。
- `Driver/`：通过 Remote Client 驱动。
  1. 正常关闭正在运行的 EPLAN，复制示例项目到 `<数据根目录>\scratch\spikes\s8\S8_Sample`；
  2. 启动 EPLAN（Electric P8），注册插件，用 `ProjectOpen /Quiet` 打开副本；
  3. 每个场景按标记文件的进度用 `Process.Kill`（等同 `taskkill /F`）结束 EPLAN，再启动、打开副本、数页。
  - 每个远程调用都有看门狗。EPLAN 重启后弹出的对话框用 UI Automation 读出并记录；只处理下面两种涉及 scratch 项目的对话框，其他任何对话框都会停止运行。
- 环境：EPLAN 2026.0.3.25421，Electric P8，2026-10-20。共 3 轮：
  - r1：重启后被对话框挡住，停止；
  - r2：等待标记时对话框检查被 EPLAN 正忙的界面线程卡住，各场景用了同样的页号；
  - r3：修正后完整运行。

```powershell
dotnet build spikes\S8-write-interruption\EplanMcp.Spikes.S8.csproj -c Release -p:AssemblyName=EplanMcp.Spikes.S8r2 -p:AssemblyTitle=EplanMcp.Spikes.S8r2
dotnet build spikes\S8-write-interruption\Driver -c Release
spikes\S8-write-interruption\Driver\bin\Release\net481\EplanMcp.Spikes.S8.Driver.exe --host <本机网卡地址> --revision r2
```

驱动会关闭正在运行的 EPLAN，并多次强制结束它。只在 EPLAN 中没有打开 scratch 之外的项目时运行。原始结果在 `.local/spikes/s8/`，不提交。

## 结果（r3）
| 场景 | 结束时的状态 | 重启后的项目 |
|---|---|---|
| U2 | 20 页的写入做到第 5 页，`SafetyPoint` 未提交 | **5 页都在**：页数 321 → 326 |
| U3 | 5 页已提交，操作仍占着主线程 | 5 页都在 |
| U3b | 3 页已提交、操作已结束，5 秒后结束 EPLAN（项目从未关闭或保存） | 3 页都在 |
| U4 | `SafetyPoint` 中 3 页未提交，触发访问冲突 | **EPLAN 进程崩溃**，3 页都在 |

- **U4 的崩溃**：
  - 访问冲突（`AccessViolationException`，在 `BaseException.GetMessageText` 中）是"损坏状态异常"。调度的委托中的 `try/catch` 抓不住它，WPF 调度器也没有处理，EPLAN 进程随即结束；
  - A11 中同样的错误发生在 action 中时，EPLAN 没有崩溃。
- **重新打开被结束时开着的项目**：EPLAN 弹出"检查相容性"："未按照规定关闭项目 '…'。如选择'是'，则将打开项目，并检查数据。如选择'否'，则无法打开项目。如选择'以后'，则将打开项目……"。
  - 驱动对 S8 副本答"以后"，以便看到落盘的原样；
  - 对 EPLAN 启动时自动重开的评测夹具答"否"，不打开它。答"否"之后还有一个"无法打开项目"的通知。
- **EPLAN 会自动重开上次开着的项目**：r1 中，被结束时开着的夹具在重启时被自动打开，随即弹出上面的对话框。
- **系统日志中没有中断的记录**：被结束的会话在 `TSysSession_v100` 中没有结束时间（正常退出的会话有），`TSysMsg_v100` 中没有相关的错误。

## 结论
1. **`SafetyPoint` 只防异常，不防进程死亡。** EPLAN 每做一步修改就写入项目数据库，回滚只在进程活着时发生。进程被结束或崩溃时，项目停在"做了一半"的状态：既不是修改前，也不是计划中的修改后。
2. **ADR-0010 中的 B3 不可行**：未提交的修改也会保留，崩溃不能变成确定的结果。ADR-0010 的处理是对的：结果未知时返回 `OUTCOME_UNKNOWN`，暂停写入，由工程师核实后解除。
3. **解除暂停时的提示要改**：
   - 崩溃后撤销历史随进程消失，Ctrl+Z 撤不回中断前的修改。所以确认表单不能说"必要时用 Ctrl+Z 撤销"；
   - 应该说明：项目中可能只有部分修改；请对照计划中的变更在 EPLAN 中检查，手工删除半成品（或恢复副本）；EPLAN 提示"检查数据"时选"是"。

   M3 设计 4.6 已按此修改。
4. **一次写入的范围越小，崩溃时的半成品越少。** 建页最多 50 页的上限维持不变。暂停记录保存计划中的变更（前 20 行），供工程师对照。
5. **访问冲突会结束 EPLAN**：编码规范 9.5 第 15 条（同一时间只用一个系统消息集合）必须遵守，没有任何兜底。
6. **Host 判断 EPLAN 是否崩溃**：可以看 `TSysSession_v100` 中上一个会话有没有结束时间。目前不需要：Host 通过管道断开和 `outcome.get` 判断结果。

## 局限
- 只在一个项目、一种写操作（建页）、Electric P8、EPLAN 2026 上验证。改属性、改名、插宏、生成连接在中断时是否也逐步落盘，按同样的机制推断为是，没有单独验证。
- 没有验证崩溃后对项目执行 EPLAN 的"检查数据"会做什么。
- U1 和"结果未知 → 暂停 → 工程师确认解除"的整条流程，要等第一个写工具和审批渠道完成后，再在实机上走一次。

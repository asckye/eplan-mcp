# Spike S12：Destructive 写入与备份

## 问题
[M4 设计](../../docs/design/2026-10-20-m4-workflows.md)第 4.6 节和 [ADR-0013](../../docs/adr/0013-destructive-backup.md)，在 Bridge 调度器中（与工具相同）验证：
1. 在 EPLAN 中打开着的项目上调用 `ProjectManager.CopyProject(…, CopyMode.Snapshot)`：
   - 是否需要关闭或独占项目；
   - 副本能否打开、是否完整，包括刚提交的写入；
   - 耗时和大小，源项目文件是否变化。
   - 对照 `CopyMode.All` 和 `Backup.Project(MakeBackup)`。
2. `Page.Remove()` 在 `TransactionScope` 中：能否回滚、一步撤销；对交叉引用和报表页的影响。
3. `Renumber.DeviceTags`（`PostNumerate`，过滤方案为 `null`）与 `Renumber.Pages`：能否回滚、一步撤销；静默模式下是否弹对话框；编号方案的取值。

## 方法
- `SpikeS12Action` 的结构与 S10、S11 相同：action 只调度探针，探针经 `Dispatcher.Invoke` 在主线程上执行。写入用 Bridge 的包装（`SafetyPoint` → 其中的 `UndoStep` → `QuietModeStep`）。回滚模式在同一 `SafetyPoint` 中执行后调用 `Rollback()`。
- `Driver/` 通过 Remote Client 驱动：
  - 复制示例项目到 `<数据根目录>\scratch\spikes\s12\S12_Sample` 并打开，选为当前项目（Ctrl+Z 作用于当前项目，S10）。只在打开的项目全是 scratch 副本、副本是当前项目时发送 Ctrl+Z；
  - 复制模式前后对比源项目 `.edb` 中的文件；打开副本，数页数、功能数；
  - 重编号前先新建 3 页（`=GAA+A1&EFS1/90`–`/92`），在第一页上用 `NumberWithQuestionMark` 插入宏 `Contactor_Feedback_Control.ema`，得到带 `?` 的标识（S10）；
  - 删页时自动选前 60 张原理图页中，主功能在其他页上有辅助功能最多的一页；
  - `Backup.Project(MakeBackup)` 放在最后，因为文档说备份后项目路径失效；
  - 结束时选回原来的当前项目（评测夹具），关闭副本，注销插件。
- 副本放在 `<数据根目录>\scratch\spikes\s12\copies\`，备份放在 `…\s12\backup\`。
- 环境：EPLAN 2026.0.3.25421，Electric P8，2026-10-20。r1 全部探针；r2 加上"写入后立即复制"。

```powershell
dotnet build spikes\S12-destructive-backup\EplanMcp.Spikes.S12.csproj -c Release -p:AssemblyName=EplanMcp.Spikes.S12r2 -p:AssemblyTitle=EplanMcp.Spikes.S12r2
dotnet build spikes\S12-destructive-backup\Driver -c Release
spikes\S12-destructive-backup\Driver\bin\Release\net481\EplanMcp.Spikes.S12.Driver.exe --host <本机网卡地址> --revision r2
```

原始结果在 `.local/spikes/s12/`，不提交。

## 结果

### 复制与备份（示例项目 321 页，`.edb` 313 个文件、213 MB）
| 调用 | 耗时 | 项目仍打开 | 源项目 `.edb` 中改动的文件 | 结果 |
|---|---:|---|---|---|
| `CopyProject(…, Snapshot)` | 0.6–0.7 s | ✅ | 只有 `User.eod` / `User.eox`（同 A11，任何调用都会改写） | 313 个文件、213 MB；打开后 321 页、2876 个功能，与源项目相同 |
| 同上，在一次已提交的写入（新建 3 页、插入宏）之后立即复制（r2） | 0.6 s | ✅ | 同上 | 打开后 324 页、2888 个功能、9 个带 `?` 的标识，与源项目相同 |
| `CopyProject(…, All)` | 0.8 s | ✅ | 33 个，全是 `.lck` 锁文件 | 与 Snapshot 相同 |
| `Backup.Project(…, MakeBackup, false)` | 9.6–10.1 s | ✅ | 44 个：锁文件，以及 `Project`、`Blob`、`User` 的 `.eod`，多个 `.eox`、`ProjectInfo.xml` | 一个 `S12_Backup.zw1`（70 MB）；源项目仍在原位，仍能打开。r1 中备份后当前项目从副本变成了评测夹具，r2 中没有变 |

### 重编号（在 `=GAA+A1&EFS1/90` 的 12 个功能上）
| 调用 | 耗时 | 结果 | 回滚 | Ctrl+Z |
|---|---:|---|---|---|
| `Renumber.DeviceTags(functions, null, PostNumerate, "", 1, 1)` | 0.1–0.2 s | `+A1-?WG1` → `+A1-WG1`，`+A1-?XG1`/`?XG2`/`?XG3` → `XG2`/`XG3`/`XG4`（`XG1` 已被占用）；端子的连接点 `:?1`、`:?2` 不变；主功能 `=GAA+A1-?QA1` 也不变 | ✅ 全部恢复 | ✅ 一步撤销 |
| 同上，方案 `"Standard"` | 0.2 s | 与 `""` 相同 | ✅ | — |
| 同上，方案 `"Default"` | — | 抛出 `InvalidScheme`，消息列出可用方案：`Kennbuchstabe + Zähler Fluidtechnik`、`Kennbuchstabe + Zähler/Zubehör Fluidtechnik`、`Kennbuchstabe Zähler`、`Media_code`、`Standard`；参数名为 `strSchemePerIdentifier` | — | — |
| `Renumber.Pages(3 页, true, 200, 10, false, false, Retain)` | < 0.1 s | `/90`、`/91`、`/92` → `/200`、`/210`、`/220` | ✅ | ✅ 一步撤销 |

### 删页
| 调用 | 耗时 | 结果 | 回滚 | Ctrl+Z |
|---|---:|---|---|---|
| `Page.Remove()`（`=GAA+A1&EFS1/1`，59 个功能，电缆 `+A1-WE1`、`+A1-WE2`、`+A2-WE1`、`+A2-WE2` 的主功能在这页） | 约 2.4 s（含挑选页面） | 页 324 → 323，功能 2888 → 2830；四根电缆在其他页上的辅助功能（1–6 个）**留下，但没有主功能了**；报表页数不变（报表不自动更新）；检查消息仍为 0 | ✅ 全部恢复 | ✅ 一步撤销 |

**其他**：所有调用都没有弹出对话框，在 `QuietModeStep(ShowNoDialogs)` 下没有挡住调度器。

## 结论
1. **`CopyProject(Snapshot)` 可以在打开着的项目上做备份**：不用关闭项目，不加锁，1 秒内完成，副本可以直接打开，包含刚提交的写入。ADR-0013 方案 1 的前提成立。
2. **不用 `Backup.Project(MakeBackup)` 做自动备份**：慢十几倍，会改写源项目的数据文件（`Project`、`Blob` 等的 `.eod`，没有核对内容是否变化），还可能改变当前项目；`CopyMode.All` 会给源项目加锁，Snapshot 不会。
3. **删页、重编号都能被 `SafetyPoint` 回滚、一步撤销**，可以走与其他写工具相同的 `TransactionScope`。
4. **删页会留下没有主功能的辅助功能**（这里是电缆的芯线）。`eplan_page_delete` 的变更清单要列出这些设备，审批对话框要提示；报表需要另外用 `eplan_report_update` 更新。
5. **`PostNumerate` 只给设备标识编号**：端子的连接点名称 `?1` 不变，有的主功能（这里是 `=GAA+A1-?QA1`）也没有编号。`eplan_device_renumber` 执行后要读回，报告仍带 `?` 的标识，不能假定全部编号。
6. **编号方案**：传空串等同于 `Standard`；名称不存在时抛 `InvalidScheme`，消息中列出可用方案，可以转成 `INVALID_ARGUMENT` 的提示。

## r3、r4：`PostNumerate` 的崩溃（2026-10-20）
M4 的实机验证中，`eplan_device_renumber` 让 EPLAN 发生访问冲突而退出（api-notes A20）。r3 加了 `backuprenumber` 模式（备份、只读打开副本数页数、关闭，再在同一次调度中重编号），r4 加了 `renumberfilter` 模式（只把 `/91` 上符合条件的功能交给 `PostNumerate`；`/Scheme:` 传条件，`zero-` 前缀表示不带标志）。每次都用新复制的项目，先用 `eplan_page_create` 建 `/90`–`/92`，再用 `eplan_macro_insert` 以 `number` 方式把宏插到 `/91`。

| 交给 `Renumber.DeviceTags` 的功能 | 标志 | 结果 |
|---|---|---|
| `/91` 的全部 12 个功能 | `PostNumerate` | 访问冲突，EPLAN 退出（三次，其中一次不做备份） |
| 只有 `=GAA+A1-?QA1`（设备标识带 `?`） | `PostNumerate` | 正常，没有编号 |
| 只有连接点带 `?` 的端子 `+A1-XG2:?1` 等 7 个 | `PostNumerate` | 访问冲突 |
| `=GAA+A1-?QA1`、电缆 `+A1-WG1`、3 个 PLC 端子 | `PostNumerate` | 访问冲突 |
| 全部 12 个 | 无 | 正常，没有改动 |

结论：交入设备标识（`:` 之前）不带 `?` 的功能时，`PostNumerate` 会崩溃；只交设备标识带 `?` 的功能是安全的（r1 中设备标识都带 `?` 时也正常）。`eplan_device_renumber` 已按此修复。

## 局限
- 只在一个项目、Electric P8、EPLAN 2026 上验证；只复制到同一磁盘上的目录，没有测磁盘空间不足、目标已存在、网络路径。
- 副本的完整性只比较了页数、功能数和 `?` 标识，没有逐个比较属性。
- 没有测试：
  - `=GAA+A1-?QA1` 为什么没有被编号；
  - 删页后报表更新、检查消息的变化；
  - `Renumber.Pages` 的其他参数组合（不按结构、保留间隔、子页）；
  - 复制正在被另一个写入修改的项目（工具的流水线中备份和写入是串行的）。
- 访问冲突、进程死亡等中断情况见 S8，没有在这些 API 上重复。

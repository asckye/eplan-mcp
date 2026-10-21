# Large 测试：工具的实机测试

这里的测试通过 `eplanmcp` 连接**正在运行的 EPLAN**：用夹具项目的已知数据核对每个只读工具（[M2 设计第 7 节](../../docs/design/2026-10-18-m2-read-core.md)），写工具的测试只写夹具之外的副本（见下文"写测试"），并在其中两份副本上验证写工具的质量场景 Q1、Q2、Q6（[M3 设计第 7 节](../../docs/design/2026-10-19-m3-safe-writes.md)，[测试规范第 4 节](../../docs/standards/testing.md#4-large-测试实机)），以及 Destructive 工具（[M4 设计第 7 节](../../docs/design/2026-10-20-m4-workflows.md)，见下文"Destructive 工具"）。全部标为 `Size=Large`，CI 中不运行。

## 准备
1. 构建 Release 版插件，并在要用的 EPLAN 变体中注册（`CLAUDE.md` 常用命令）。
2. 用夹具启动 EPLAN：
   ```powershell
   eng\scripts\eval-fixture.ps1 -Open -WriteCopies
   ```
   脚本把 EPLAN 自带的 `EPLAN_Sample_Project` 复制到 scratch 根目录下的 `fixtures\`，再用这个副本启动 EPLAN（与 Agent 评测共用，见 [evals/README.md](../../evals/README.md)）。`-WriteCopies` 另外复制两份给写测试：会被修改的 `<scratch 根目录>\writes\LargeWrite`，以及 scratch 根目录之外、只会被拒绝的 `<数据根目录>\not-scratch\writes\LargeOutside`。写测试按路径找这两个目录下打开的项目，所以 `writes\` 下用其他方法复制的副本也可以。
3. 要跑写测试，在 EPLAN 中手工打开这两份副本（文件 > 打开），再在页导航器中点回夹具 `EPLAN_Sample_Project`，让它重新成为当前项目：只读测试读的是当前项目。不跑写测试时可以省略 `-WriteCopies`，但 `WriteSafetyTests` 和 Destructive 工具的测试会因为找不到副本而失败。

## 运行
```powershell
dotnet test --project tests\EplanMcp.Large.Tests
dotnet test --project tests\EplanMcp.Large.Tests -- --filter-class "EplanMcp.Large.Tests.WriteSafetyTests"   # 只跑 Q1、Q2、Q6
dotnet test --project tests\EplanMcp.Large.Tests -- --filter-class "EplanMcp.Large.Tests.PageDeleteTests" --filter-class "EplanMcp.Large.Tests.DeviceRenumberTests" --filter-class "EplanMcp.Large.Tests.PageRenumberTests"   # 只跑 Destructive 工具
```
- 测试一个接一个运行。只读测试共用同一个 `eplanmcp`，它就是本项目构建出的那个（随项目引用复制到测试旁边）。
- 开始前检查 EPLAN 已连接、当前项目是 scratch 根目录下的夹具；不满足时，每个测试都报告该怎么做，而不会给出看不懂的断言失败。
- 夹具只读：除写测试只写 `writes\` 下的副本（见"写测试"）之外，测试不写任何项目。`NoSideEffectTests` 核对调用全部读工具前后，夹具的 `.elk` 和 `.edb` 中每个文件的大小和修改时间都没有变化。
- 多语言文本都显式传入 `language`，结果不受 EPLAN 界面语言影响；只有回退的测试按界面语言分两种情况判断。

### Q1、Q2、Q6
`WriteSafetyTests` 另起一个 `eplanmcp`，数据根目录在系统临时目录中（测试结束后删除），其中的 `config.json` 只把 `<scratch 根目录>\writes\` 当作 scratch 根目录，其他项目的写入一律拒绝，不看工程师自己的 `config.json`。所以对 `not-scratch\writes\` 下那份副本的写入只会被拒绝，也不会弹出审批对话框。`writes\` 下开着几个项目时（`eplan_macro_insert` 的写测试也用这个目录），取 `LargeWrite`。

两份副本可以反复使用：Q1、Q2 不改变它们，Q6 每次取新的页号和描述文字。副本出了问题时，在 EPLAN 中关闭它们，运行 `eval-fixture.ps1 -WriteCopies -Force`（它也会重新复制夹具，所以要先关闭 EPLAN），再按准备第 3 步打开。

## 核对的内容
| 测试类 | 内容 |
|---|---|
| `ProjectAndPageTests` | 页数、设备数、连接数、部件引用数和各类页的数量；detailed 属性的名称和索引；按类型、名称前缀筛选页；读取页描述的耗时；页上的设备；不存在的页 |
| `DeviceAndConnectionTests` | 设备总数，同一页上的设备不重复；短标识 `-FC1` 的 14 个候选；断路器 `=GAA+A1-FC1` 的部件、连接点，以及负载侧接到哪些设备；不存在的设备 |
| `PartAndMessageTests` | 部件总数和各制造商的部件数；按语言读部件描述，以及省略 `language` 时的回退；系统消息和检查结果；选择 EPLAN 实例 |
| `MacroTests` | 在宏目录中按名称找到示例宏 `Contactor_Feedback_Control.ema`，全量扫描完整；读出它的表达类型、变体、尺寸和设备（与 spike S10 一致） |
| `NoSideEffectTests` | 读取（包括两个宏工具）不改动项目文件 |
| `MacroInsertTests` | 写测试：在新页上用 `number` 插入 `Contactor_Feedback_Control.ema`（dry run、读回的设备、用 `eplan_page_create` 建的页已有幅面，A20），同一位置再插一次被回滚；`keep` 与已有设备合并时返回 `ALREADY_EXISTS`，项目的计数不变 |
| `CheckAndReportTests` | 写测试：检查整个项目（`dry_run` 的页数；结果与 `eplan_check_list` 一致；再检查一次警告不变、14 条汇总提示再追加一遍（A20））、只检查两页、EPLAN 不认识的方案被回滚；更新全部报表（新增 `+A2&EPA1/1.2`，再更新一次不增删）、只更新目录页、电路图页不是报表页。规格中没有验证的结果写入测试输出 |
| `PageDeleteTests` | Destructive：dry run `=GAA+A1&EFS1/1` 列出页上的功能数，以及其他页上失去主功能的三根电缆 `+A1-WE1`、`+A1-WE2`、`+A2-WE1`（S12 的第四根 `+A2-WE2` 留下的功能不在页上，不列出），不审批；新的宏页：拒绝时 `APPROVAL_DENIED`、不建备份目录、页还在，审计只有拒绝；批准后新的备份目录中有副本（`.elk` 与 `.edb`），页没了，审计依次为批准、带 `backup` 的开始、`backupCompleted` 的结果；再删一次 `PAGE_NOT_FOUND`、不审批。一次删除 3 页。scratch 之外的副本 `POLICY_DENIED`，不审批、不备份，设计数据不变 |
| `DeviceRenumberTests` | Destructive：在新页上用 `number` 插入宏后，`only_unnumbered` 编号：EPLAN 不崩溃（只交带 `?` 的设备标识，A20），`=GAA+A1-?QA…` 出现在 `stillUnnumbered` 中，没有改动所以 `not_started`、`undo` 为 null，备份照样有；方案 `Default`：`INVALID_ARGUMENT`、`rolled_back`，候选与 S12 的 5 个方案相同，备份保留，`?` 标识不变 |
| `PageRenumberTests` | Destructive：三张新页从 200、步长 10 重编号为 `/200`、`/210`、`/220`（S12），dry run 不列新页名；`start` 为 1 时与 `/1`、`/2` 冲突：`ALREADY_EXISTS`、`rolled_back`，页名不变，备份保留 |
| `WriteSafetyTests` | **Q1**：对 scratch 之外的副本建页、改页描述、设备改名都返回 `POLICY_DENIED`、`not_started`，设计数据文件（`User.*` 除外，A17）和审计都不变。**Q2**：把电缆 `+A1-WE1` 改名为 `=F+A1-WE19`，EPLAN 丢掉 `=F`，读回不一致而回滚（A17 的自然故障）：`INVALID_ARGUMENT`、`rolled_back`，电缆的全部功能保持原名，审计为 `rolled_back`，不暂停写入。**Q6**：直接经 Host 的 `BridgeClient` 调用 `page.create.plan`，在同一结构中另建一页，再用旧指纹调用 `page.create.execute`，返回 `PLAN_STALE`、`not_started`，计划的页不存在；改页描述同理，后来的修改保留 |

Q6 的流水线部分（审批之后用计划的指纹执行、`PLAN_STALE` 映射为 `not_started`、不打开事务）已由 Medium 测试 `PageCreateInteropTests` 在假项目上覆盖；这里只验证 Medium 测试做不到的部分：真实适配层的指纹能看到在 EPLAN 中发生的变化（[`eplan_page_create` 规格第 7 节](../../docs/tools/eplan_page_create.md)）。

### Destructive 工具
`eplan_page_delete`、`eplan_device_renumber`、`eplan_page_renumber` 每次都要工程师审批，scratch 中也一样，而 `eplanmcp` 不能自己批准（ADR-0005、ADR-0011）。所以这三组测试不启动 `eplanmcp.exe`，而是由 `DestructiveHost` 在测试进程中按 `serve` 的方式组装 Host（`AddEplanMcp`，内存中的流代替 stdio），连接真实的 Bridge，只把审批渠道换成 `ScriptedApprovalChannel`（[`eplan_page_delete` 规格第 7 节](../../docs/tools/eplan_page_delete.md)）：
- 它只批准测试在调用前登记的工具、项目和变更清单（与 EPLAN 对话框中显示的逐行相同），每次登记只用一次，其余一律拒绝，包括解除写入暂停的确认。EPLAN 中不弹出审批对话框。
- 这个类型只在本测试程序集中。`eplanmcp.exe` 没有任何开关、配置项或环境变量能启用它；Small 测试 `PolicyEngineTests` 核对 Host 程序集中 `IApprovalChannel` 的实现只有 `EplanDialogChannel`，`AddEplanMcp` 注册的也只有它。
- 数据根目录、配置与 `WriteHost` 相同：系统临时目录中新建，只把 `<scratch 根目录>\writes\` 当作 scratch，其他项目的写入一律拒绝。审计、备份都在这个目录中，测试类结束时删除。**每次执行的 Destructive 写入都先把整个副本复制到 `backups\` 下（示例项目约 210 MB），一个测试类最多留下 4 份**，运行时临时目录所在的盘要有 1 GB 左右的空闲空间。
- 准备与写测试相同（上文第 2、3 步）：用的是 `writes\LargeWrite`；`PageDeleteTests` 中只有一个测试用 `not-scratch\writes\` 下的副本，核对 `POLICY_DENIED`。

这三组测试让副本保持原样：只规划（dry run）示例项目自己的页，执行的删除和重编号只针对测试在 `=GAA+A1&EFS1` 最后一页之后新建的页，测试最后再用 `eplan_page_delete`（同样由脚本批准）把这些页删掉。测试失败时会尽量删掉它们，并在测试输出中说明；删不掉时测试输出列出页名，在 EPLAN 中手工删除，或按上文重新复制副本。`PageRenumberTests` 把新页重编号为 `/200`、`/210`、`/220`，所以 `=GAA+A1&EFS1` 的页号到 200 之前都能运行（其他写测试每次运行新增 4 页）；到了 200 时它会失败并提示重新复制副本。

## 写测试
写工具的测试**不写夹具**：读测试核对的页数、设备数会被改掉。它们写另一份 scratch 副本：
1. 在夹具之外再复制一份示例项目到 scratch 根目录下的 `writes\`（与 `eval-fixture.ps1` 的复制方法相同：`.elk` 文件和 `.edb` 目录），例如
   ```powershell
   $scratch = "$env:USERPROFILE\EplanMcp\scratch"
   New-Item -ItemType Directory -Force "$scratch\writes" | Out-Null
   Copy-Item "$scratch\fixtures\EPLAN_Sample_Project.elk" "$scratch\writes\"
   robocopy "$scratch\fixtures\EPLAN_Sample_Project.edb" "$scratch\writes\EPLAN_Sample_Project.edb" /MIR
   ```
   复制时夹具不能有未保存的修改，最好在 EPLAN 关闭时复制。
2. 在 EPLAN 中打开这份副本，**夹具仍然是当前项目**（读测试不传 `project`，用的是当前项目）。写测试按路径找 `writes\` 下打开的项目；找不到时失败并说明怎么做。
3. 每次运行都在 `=GAA+A1&EFS1` 最后一页之后新建页，副本会越来越大；需要时删掉 `writes\` 重新复制。不要对副本按 Ctrl+Z：EPLAN 中开着两个项目时，撤销可能落在当前项目（夹具）上（api-notes A14）。

已知数据来自 EPLAN 2026.0.3 自带的示例项目（[api-notes A12](../../docs/eplan/api-notes.md#a12-m2-只读工具的实机验证2026-10-19eplan-20260325421electric-p8)、A17）。换了 EPLAN 版本，示例项目可能不同，这时先核对数据，再改测试。

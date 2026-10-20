# Large 测试：工具的实机测试

这里的测试通过 `eplanmcp` 连接**正在运行的 EPLAN**：用夹具项目的已知数据核对每个只读工具（[M2 设计第 7 节](../../docs/design/2026-10-18-m2-read-core.md)），写工具的测试只写夹具之外的副本（见下文"写测试"），并在其中两份副本上验证写工具的质量场景 Q1、Q2、Q6（[M3 设计第 7 节](../../docs/design/2026-10-19-m3-safe-writes.md)，[测试规范第 4 节](../../docs/standards/testing.md#4-large-测试实机)）。全部标为 `Size=Large`，CI 中不运行。

## 准备
1. 构建 Release 版插件，并在要用的 EPLAN 变体中注册（`CLAUDE.md` 常用命令）。
2. 用夹具启动 EPLAN：
   ```powershell
   eng\scripts\eval-fixture.ps1 -Open -WriteCopies
   ```
   脚本把 EPLAN 自带的 `EPLAN_Sample_Project` 复制到 scratch 根目录下的 `fixtures\`，再用这个副本启动 EPLAN（与 Agent 评测共用，见 [evals/README.md](../../evals/README.md)）。`-WriteCopies` 另外复制两份给写测试：会被修改的 `<scratch 根目录>\writes\LargeWrite`，以及 scratch 根目录之外、只会被拒绝的 `<数据根目录>\not-scratch\writes\LargeOutside`。写测试按路径找这两个目录下打开的项目，所以 `writes\` 下用其他方法复制的副本也可以。
3. 要跑写测试，在 EPLAN 中手工打开这两份副本（文件 > 打开），再在页导航器中点回夹具 `EPLAN_Sample_Project`，让它重新成为当前项目：只读测试读的是当前项目。不跑写测试时可以省略 `-WriteCopies`，但 `WriteSafetyTests` 会因为找不到副本而失败。

## 运行
```powershell
dotnet test --project tests\EplanMcp.Large.Tests
dotnet test --project tests\EplanMcp.Large.Tests -- --filter-class "EplanMcp.Large.Tests.WriteSafetyTests"   # 只跑 Q1、Q2、Q6
```
- 测试一个接一个运行。只读测试共用同一个 `eplanmcp`，它就是本项目构建出的那个（随项目引用复制到测试旁边）。
- 开始前检查 EPLAN 已连接、当前项目是 scratch 根目录下的夹具；不满足时，每个测试都报告该怎么做，而不会给出看不懂的断言失败。
- 夹具只读：除写测试只写 `writes\` 下的副本（见"写测试"）之外，测试不写任何项目。`NoSideEffectTests` 核对调用全部读工具前后，夹具的 `.elk` 和 `.edb` 中每个文件的大小和修改时间都没有变化。
- 多语言文本都显式传入 `language`，结果不受 EPLAN 界面语言影响；只有回退的测试按界面语言分两种情况判断。

### Q1、Q2、Q6
`WriteSafetyTests` 另起一个 `eplanmcp`，数据根目录在系统临时目录中（测试结束后删除），其中的 `config.json` 只把 `<scratch 根目录>\writes\` 当作 scratch 根目录，其他项目的写入一律拒绝，不看工程师自己的 `config.json`。所以对 `not-scratch\writes\` 下那份副本的写入只会被拒绝，也不会弹出审批对话框；开始前还核对可写的副本在 scratch 根目录中、另一份在它之外。

两份副本可以反复使用：Q1、Q2 不改变它们，Q6 每次取新的页号和描述文字。副本出了问题时，在 EPLAN 中关闭它们，运行 `eval-fixture.ps1 -WriteCopies -Force`（它也会重新复制夹具，所以要先关闭 EPLAN），再按准备第 3 步打开。

## 核对的内容
| 测试类 | 内容 |
|---|---|
| `ProjectAndPageTests` | 页数、设备数、连接数、部件引用数和各类页的数量；detailed 属性的名称和索引；按类型、名称前缀筛选页；读取页描述的耗时；页上的设备；不存在的页 |
| `DeviceAndConnectionTests` | 设备总数，同一页上的设备不重复；短标识 `-FC1` 的 14 个候选；断路器 `=GAA+A1-FC1` 的部件、连接点，以及负载侧接到哪些设备；不存在的设备 |
| `PartAndMessageTests` | 部件总数和各制造商的部件数；按语言读部件描述，以及省略 `language` 时的回退；系统消息和检查结果；选择 EPLAN 实例 |
| `MacroTests` | 在宏目录中按名称找到示例宏 `Contactor_Feedback_Control.ema`，全量扫描完整；读出它的表达类型、变体、尺寸和设备（与 spike S10 一致） |
| `NoSideEffectTests` | 读取（包括两个宏工具）不改动项目文件 |
| `MacroInsertTests` | 写测试：在新页上用 `number` 插入 `Contactor_Feedback_Control.ema`（dry run、读回的设备、幅面未知的提示），同一位置再插一次被回滚；`keep` 与已有设备合并时返回 `ALREADY_EXISTS`，项目的计数不变 |
| `CheckAndReportTests` | 写测试：检查整个项目（`dry_run` 的页数；结果与 `eplan_check_list` 一致；再检查一次消息数不变）、只检查两页、EPLAN 不认识的方案被回滚；更新全部报表（新增 `+A2&EPA1/1.2`，再更新一次不增删）、只更新目录页、电路图页不是报表页。规格中没有验证的结果写入测试输出 |
| `WriteSafetyTests` | **Q1**：对 scratch 之外的副本建页、改页描述、设备改名都返回 `POLICY_DENIED`、`not_started`，设计数据文件（`User.*` 除外，A17）和审计都不变。**Q2**：把电缆 `+A1-WE1` 改名为 `=F+A1-WE19`，EPLAN 丢掉 `=F`，读回不一致而回滚（A17 的自然故障）：`INVALID_ARGUMENT`、`rolled_back`，电缆的全部功能保持原名，审计为 `rolled_back`，不暂停写入。**Q6**：直接经 Host 的 `BridgeClient` 调用 `page.create.plan`，在同一结构中另建一页，再用旧指纹调用 `page.create.execute`，返回 `PLAN_STALE`、`not_started`，计划的页不存在；改页描述同理，后来的修改保留 |

Q6 的流水线部分（审批之后用计划的指纹执行、`PLAN_STALE` 映射为 `not_started`、不打开事务）已由 Medium 测试 `PageCreateInteropTests` 在假项目上覆盖；这里只验证 Medium 测试做不到的部分：真实适配层的指纹能看到在 EPLAN 中发生的变化（[`eplan_page_create` 规格第 7 节](../../docs/tools/eplan_page_create.md)）。

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

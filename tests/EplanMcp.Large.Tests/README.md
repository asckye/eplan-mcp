# Large 测试：工具的实机测试

这里的测试通过 `eplanmcp` 连接**正在运行的 EPLAN**（只读测试也可以改用离线宿主，见下文"离线宿主"）：用夹具项目的已知数据核对每个只读工具（[M2 设计第 7 节](../../docs/design/2026-10-18-m2-read-core.md)），写工具的测试只写夹具之外的副本（见下文"写测试"），并在其中两份副本上验证写工具的质量场景 Q1、Q2、Q6（[M3 设计第 7 节](../../docs/design/2026-10-19-m3-safe-writes.md)，[测试规范第 4 节](../../docs/standards/testing.md#4-large-测试实机)），以及 Destructive 工具（[M4 设计第 7 节](../../docs/design/2026-10-20-m4-workflows.md)，见下文"Destructive 工具"）。全部标为 `Size=Large`，CI 中不运行。

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
dotnet test --project tests\EplanMcp.Large.Tests -- --filter-class "EplanMcp.Large.Tests.PageDeleteTests" --filter-class "EplanMcp.Large.Tests.DeviceRenumberTests" --filter-class "EplanMcp.Large.Tests.PageRenumberTests" --filter-class "EplanMcp.Large.Tests.TerminalRenumberTests" --filter-class "EplanMcp.Large.Tests.CableRenumberTests" --filter-class "EplanMcp.Large.Tests.ConnectionNumberTests"   # 只跑 Destructive 工具
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
| `CableTests` | 电缆 `+B1-WG2` 的两端（`=F+B1-SG1`、端子 `+A2-XG4:2`）和 2 根芯线，短标识 `-WG2`；`eplan_device_list` 列出的每根电缆都能读出（最慢的一根写入测试输出）；断路器不是电缆。S21 没有验证的属性文本、线号和耗时写入测试输出 |
| `TerminalTests` | 45 个端子排按名称排序，`+A1-XD4` 有 15 个端子，`+A2-XD2` 有 3 个跨接（S21）；`name_contains` 不区分大小写；`+A2-XD2` 的 5 个端子中有跨接，`+B2.X1-XZ1` 有 33 个端子；短名称 `-XD4`、不存在的端子排。列表和 33 个端子的耗时，以及 `+A2-XD2` 每个端子的两侧、跨接和 PLC 写入测试输出，供与 EPLAN 中的端子图对照 |
| `PlcTests` | PLC 盒 `=K1+A2-KE1` 的 34 个通道对象（17 个名称），不是 CPU（S21）；`:2.3` 接到 `+A2-XD4:3`；`eplan_device_list` 列出的每个 PLC 盒都能读出；断路器不是 PLC 盒。站、机架、槽，以及每个通道的页、是否主表达、地址、符号地址、数据类型、文本和连接写入测试输出，供与 EPLAN 的 PLC 导航器对照 |
| `PartSearchTests` | 开发机部件库（S24）中 `3RH21` 找到 `SIE.3RH2122-1HB40`（变体 1）：有宏，制造商、型号、订货号、产品组和英文描述与 S24 一致；`manufacturer` 是整个简称（`sie` 找到，`SI`、`PXC` 找不到）；省略 `language` 时读出描述（回退语言写入测试输出）；宽泛查询读完整个库的耗时写入测试输出；搜索前后部件库数据目录中的文件不变（`.lck` 除外，S24）。数据目录按库名在 `%PUBLIC%\EPLAN\Data` 下查找，找不到唯一的一个时跳过该测试，可用环境变量 `EPLANMCP_PARTS_DATABASE` 指定 `….adb` 目录 |
| `PartExportTests` | `eplan_part_export` 导出开发机的部件库（只读，A27、A30）：全部部件的 XML 得到一个 `LargePartsDb.xml`，根元素 `partsmanagement` 的 `count` 等于 `partCount`，`variantCount` 不小于它（2026-10-21：454 和 464）；只导出 `SIE.3RH2122-1HB40` 时两者都为 1，文件中只有这一个 `part`；再加一个不存在的部件号时返回 `PART_NOT_FOUND`（`metadata.missing` 为它），不新建导出目录。每次导出只在 `exports\parts-database\` 下新建的目录中留下一个文件，`database` 与 `eplan_part_search` 相同，导出前后部件库的 `.alk` 和 `.adb` 中的文件（`.lck` 和 EPLAN 的会话记录 `User.eod`、`User.eox` 除外，A30）SHA-256 不变，导出后 EPLAN 仍然响应；核对后删除本次目录。耗时、大小、两个计数、`part` 元素数和不同部件号的个数写入测试输出。EDZ（约 44 秒、128 MB）只在设置 `EPLANMCP_LARGE_EDZ=1` 时运行，发布前跑一次。数据目录的查找同 `PartSearchTests` |
| `NoSideEffectTests` | 读取（包括两个宏工具和部件库查询）不改动项目文件 |
| `ProjectExportTests` | 整个项目的导出（A25）：每次导出只在本次新建的目录中留下文件，导出根目录中没有别的新目录，夹具旁边没有新文件，夹具的设计数据不变（`User.*` 除外），导出后 EPLAN 仍然响应；核对后删除本次目录。PXF：`file_name` 为 `LargePxf.epj` 时得到 `LargePxf.epj`（不是 `.epj.epj`），不带主数据，大小在 100 MB–2 GB 之间；耗时写入测试输出。部件清单：`LargeParts` 得到一个 `LargeParts.xml`（1 KB–200 MB），耗时和文件开头 3000 个字符写入测试输出（规格待确认 XML 中每个部件有哪些字段），导出后 EPLAN 仍然响应（A25 的访问冲突）。标签：`Device tag list` 得到 `LargeLabels.txt`（开头写入测试输出），`Terminal diagram` 得到 `LargeLabels.xlsx`（S25），`scheme` 回显 EPLAN 中的名称；不存在的方案返回 `INVALID_ARGUMENT`，候选中有 `Device tag list`、没有 `InternalRefDefScheme`，不新建导出目录。缺失词表：`["en_US"]` 得到一个 `LargeMissing.txt`，第一行（制表符分隔）有 `en_US` 和 `COMMENT` 列，开头写入测试输出；`tl_XX` 不是翻译语言时返回 `INVALID_ARGUMENT`，`metadata.missing` 为 `tl_XX`，候选中有 `en_US`，共 20 种（S25）。PXF 约 600 MB，测试期间导出根目录所在的盘要有 2 GB 以上的可用空间 |
| `MacroInsertTests` | 写测试：在新页上用 `number` 插入 `Contactor_Feedback_Control.ema`（dry run、读回的设备、用 `eplan_page_create` 建的页已有幅面，A20），同一位置再插一次被回滚；`keep` 与已有设备合并时返回 `ALREADY_EXISTS`，项目的计数不变 |
| `DevicePlaceTests` | 写测试：在新页上按部件号放置 `SIE.3RH2122-1HB40`（S20），标识 `-KF9<页号>`：dry run 不放置，标识取页的结构 `=GAA+A1`；读回类别 `Coil`、5 个功能中 4 个未放置，`eplan_device_get` 在新页上找到它；再放一次 `ALREADY_EXISTS`，页上仍只有一个设备。部件库中没有的部件 `PART_NOT_FOUND`，不存在的变体 `INVALID_ARGUMENT`（列出变体 1），页上没有设备。scratch 之外的副本 `POLICY_DENIED`。页面图片所在的目录写入测试输出，在其中核对线圈显示标识、在 (80, 160) 附近、带触点镜像；一次 Ctrl+Z 撤销放置和改名要在只开着写副本时手工核对（"写测试"第 3 步） |
| `ImportDxfTests` | 写测试，经 `WriteHost`（数据根目录在临时目录中，导入文件由测试自己写进其 `imports\`）：测试中生成的 R12 DXF（4 条线、1 个圆）导入新页：dry run 给出 SHA-256、不导入；提交后新增 1–100 个对象，再导入一次新增同样多（不是幂等的）；审计的参数中有文件的 SHA-256。EPLAN 自带的 Rittal 图 `%PUBLIC%\EPLAN\Data\DXF_DWG\Company name\RITTAL\4685200F.DXF`（S22）复制进导入目录：`fit`、`center` 新增 227 个对象（A29），`none`、`lower_left` 导入到另一页（对象数写入测试输出）；文件不在时跳过。导入目录中没有的文件 `FILE_NOT_FOUND`、`../` 和别的扩展名 `INVALID_ARGUMENT`；scratch 之外的副本 `POLICY_DENIED`。三张页的图片经只读测试的 `eplanmcp` 导出，目录写入测试输出，在其中核对图形的位置和缩放；一次 Ctrl+Z 撤销要在只开着写副本时手工核对 |
| `ImportPxfTests` | 写测试，经 `WriteHost`：用 `eplan_export_pxf` 把写副本导出为 EPJ（约 600 MB，S25），移进临时数据根目录的 `imports\`，再导入为 `<scratch 根目录>\writes\imported\LargeImport_<UTC 时间>`：dry run 给出新项目的路径、不建文件；提交后 `.elk` 和 `.edb` 都在，项目没有在 EPLAN 中打开，审计中没有审批、有按新项目路径的开始记录；同名再导一次 `ALREADY_EXISTS`。不存在的文件 `FILE_NOT_FOUND`，名称带点 `INVALID_ARGUMENT`。耗时和新项目的路径写入测试输出；**在 EPLAN 中打开它、核对页数与写副本相同要手工进行**。最新的一个导入项目保留，每次运行开始时删除以前的（EPLAN 中开着的删不掉，测试输出会说明）。导出和导入合计约 2 分钟，临时目录所在的盘要有 1.5 GB 以上的空闲空间 |
| `CheckAndReportTests` | 写测试：检查整个项目（`dry_run` 的页数；结果与 `eplan_check_list` 一致；再检查一次警告不变、14 条汇总提示再追加一遍（A20））、只检查两页、EPLAN 不认识的方案被回滚；更新全部报表（新增 `+A2&EPA1/1.2`，再更新一次不增删）、只更新目录页、电路图页不是报表页。规格中没有验证的结果写入测试输出 |
| `LanguageSetTests` | 写测试：显示语言设为 `en_US`、`zh_CN`，读回一致，再调用一次不改动，用 dry run 读出的 `before` 再调用一次恢复原样（示例项目是 `##_##`：按存储形式写回，A28 之外的行为）；加一种副本中没有的语言（`is_IS` 等中第一个没有的，**留在副本中**），再加一次不改动；不是翻译语言的显示语言返回 `INVALID_ARGUMENT` 和候选。耗时、新语言在列表中的位置、加语言时显示语言是否被 EPLAN 改动写入测试输出 |
| `LanguageRemoveTests` | Destructive：先用 `eplan_language_set` 加一种副本中没有的语言（`lt_LT` 等），dry run 为 0 条译文、不备份，批准后删除：`committed`，翻译语言回到原样，有新的备份；EPLAN 改了显示语言时再设回原值。删除 `sv_SE`：dry run 的 `textsAffected` 至少 460（S23），批准后读回不含 `sv_SE`，备份存在，再删一次 `INVALID_ARGUMENT`。**这一项永久改变副本，每份副本只能运行一次**，之后跳过并提示重新复制。不是翻译语言时 `INVALID_ARGUMENT`、候选，不审批、不备份。耗时、删除后的显示语言、备份路径写入测试输出 |
| `PageDeleteTests` | Destructive：dry run `=GAA+A1&EFS1/1` 列出页上的功能数，以及其他页上失去主功能的三个设备 `+A1-WE1`（类别 `BusBarDefText`）、`+A1-WE2`、`+A2-WE1`（S12 的第四个 `+A2-WE2` 留下的功能不在页上，不列出），不审批；新的宏页：拒绝时 `APPROVAL_DENIED`、不建备份目录、页还在，审计只有拒绝；批准后新的备份目录中有副本（`.elk` 与 `.edb`），页没了，审计依次为批准、带 `backup` 的开始、`backupCompleted` 的结果；再删一次 `PAGE_NOT_FOUND`、不审批。一次删除 3 页。scratch 之外的副本 `POLICY_DENIED`，不审批、不备份，设计数据不变 |
| `DeviceRenumberTests` | Destructive：在新页上用 `number` 插入宏后，`only_unnumbered` 编号：EPLAN 不崩溃（只交带 `?` 的设备标识，A20），`=GAA+A1-?QA…` 出现在 `stillUnnumbered` 中，没有改动所以 `not_started`、`undo` 为 null，备份照样有；方案 `Default`：`INVALID_ARGUMENT`、`rolled_back`，候选与 S12 的 5 个方案相同，备份保留，`?` 标识不变 |
| `PageRenumberTests` | Destructive：三张新页从 200、步长 10 重编号为 `/200`、`/210`、`/220`（S12），dry run 不列新页名；`start` 为 1 时与 `/1`、`/2` 冲突：`ALREADY_EXISTS`、`rolled_back`，页名不变，备份保留 |
| `TerminalRenumberTests` | Destructive，**改变副本**：`+A1-XD1` 全部端子按 `Numerisch` 重新编号：只改这一排（`+A1-XD4` 不变），名称只是互换（S21：`:2`→`:3`、`:3`→`:5`、`:4`→`:2`、`:5`→`:4`，写入测试输出），第一次 `committed`，之后的运行没有改名、`not_started`、`undo` 为 null；新宏页上 `only_unnumbered` 给整个项目编号：EPLAN 不崩溃，只改名称中 `:` 之后带 `?` 的端子；`numerisch`（大小写不符）：`INVALID_ARGUMENT`、`not_started`，候选中有 `Numerisch`、没有 `InternalRefDefScheme`，不审批、不备份 |
| `CableRenumberTests` | Destructive，**改变副本**：全部电缆按 `Standard` 编号：标识仍不重复，改动只在原有电缆之间，S21 没有改名的电缆 `+B1-WG2` 没有改名，有改动时 `committed`、否则 `not_started`；改动写入测试输出，对照 S21 的 `+A2-WD4`↔`+A2-WD5`、`+B2.Y1-WD1`↔`+B2.Y1-WD2`；只给两根 `+A2-WD` 电缆：其他电缆不变；`keep_existing` 给没有 `?` 的电缆 `+B1-WG2`：不审批、不备份；断路器 `=GAA+A1-FC1`：`INVALID_ARGUMENT`（`CircuitBreaker`），`standard`：候选中有 `Standard`（项目设置 `CableLog.CableNumberingScheme`），都不审批、不备份 |
| `ConnectionNumberTests` | Destructive，**改变副本**：`Standard`、`overwrite: all` 给整个项目编线号：编号后有线号的连接不少于之前，列出的改动最多 200 条、超过时 `truncated`，有改动时 `committed`、否则 `not_started`，备份存在；改动数和前 12 条写入测试输出（S21 在新副本上：605 条改变，116 → 659）；接着用 `except_manual` 再编一次并写入输出；`Potential`：`INVALID_ARGUMENT`、`not_started`，候选中有 `Standard`、`Potenzialorientiert`（项目设置 `WireNumberingLog.Schema`，S21 中 11 个），没有 `InternalRefDefScheme`，不审批、不备份 |
| `WriteSafetyTests` | **Q1**：对 scratch 之外的副本建页、改页描述、设备改名都返回 `POLICY_DENIED`、`not_started`，设计数据文件（`User.*` 除外，A17）和审计都不变。**Q2**：把 `+A1-WE1`（类别 `BusBarDefText`）改名为 `=F+A1-WE19`，EPLAN 丢掉 `=F`，读回不一致而回滚（A17 的自然故障）：`INVALID_ARGUMENT`、`rolled_back`，它的全部功能保持原名，审计为 `rolled_back`，不暂停写入。**Q6**：直接经 Host 的 `BridgeClient` 调用 `page.create.plan`，在同一结构中另建一页，再用旧指纹调用 `page.create.execute`，返回 `PLAN_STALE`、`not_started`，计划的页不存在；改页描述同理，后来的修改保留 |

Q6 的流水线部分（审批之后用计划的指纹执行、`PLAN_STALE` 映射为 `not_started`、不打开事务）已由 Medium 测试 `PageCreateInteropTests` 在假项目上覆盖；这里只验证 Medium 测试做不到的部分：真实适配层的指纹能看到在 EPLAN 中发生的变化（[`eplan_page_create` 规格第 7 节](../../docs/tools/eplan_page_create.md)）。

### Destructive 工具
`eplan_page_delete`、`eplan_device_renumber`、`eplan_page_renumber` 和 M4c 的编号工具每次都要工程师审批，scratch 中也一样，而 `eplanmcp` 不能自己批准（ADR-0005、ADR-0011）。所以这些测试不启动 `eplanmcp.exe`，而是由 `DestructiveHost` 在测试进程中按 `serve` 的方式组装 Host（`AddEplanMcp`，内存中的流代替 stdio），连接真实的 Bridge，只把审批渠道换成 `ScriptedApprovalChannel`（[`eplan_page_delete` 规格第 7 节](../../docs/tools/eplan_page_delete.md)）：
- 它只批准测试在调用前登记的工具、项目和变更清单（与 EPLAN 对话框中显示的逐行相同），每次登记只用一次，其余一律拒绝，包括解除写入暂停的确认。EPLAN 中不弹出审批对话框。
- 这个类型只在本测试程序集中。`eplanmcp.exe` 没有任何开关、配置项或环境变量能启用它；Small 测试 `PolicyEngineTests` 核对 Host 程序集中 `IApprovalChannel` 的实现只有工程师操作的两个窗口 `WorkbenchChannel` 和 `EplanDialogChannel`，`AddEplanMcp` 注册的也只有这两个，Workbench 在前（ADR-0014）。
- 数据根目录、配置与 `WriteHost` 相同：系统临时目录中新建，只把 `<scratch 根目录>\writes\` 当作 scratch，其他项目的写入一律拒绝。审计、备份都在这个目录中，测试类结束时删除。**每次执行的 Destructive 写入都先把整个副本复制到 `backups\` 下（示例项目约 210 MB），一个测试类最多留下 4 份**，运行时临时目录所在的盘要有 1 GB 左右的空闲空间。
- 准备与写测试相同（上文第 2、3 步）：用的是 `writes\LargeWrite`；`PageDeleteTests` 中只有一个测试用 `not-scratch\writes\` 下的副本，核对 `POLICY_DENIED`。

`eplan_language_remove` 的 `LanguageRemoveTests` 也在 `DestructiveHost` 上运行。规格第 7 节中两项要在 EPLAN 中手工核对：用测试输出中的备份路径打开备份，项目设置的翻译语言中仍有 `sv_SE`；删除 `sv_SE` 之后对写副本按一次 Ctrl+Z，记录译文和语言设置是否恢复（只开着写副本、让它成为当前项目时再按，见下文"写测试"第 3 步），结果补进 api-notes A28。

页的三组测试让副本保持原样：只规划（dry run）示例项目自己的页，执行的删除和重编号只针对测试在 `=GAA+A1&EFS1` 最后一页之后新建的页，测试最后再用 `eplan_page_delete`（同样由脚本批准）把这些页删掉。测试失败时会尽量删掉它们，并在测试输出中说明；删不掉时测试输出列出页名，在 EPLAN 中手工删除，或按上文重新复制副本。`PageRenumberTests` 把新页重编号为 `/200`、`/210`、`/220`，所以 `=GAA+A1&EFS1` 的页号到 200 之前都能运行（其他写测试每次运行新增 4 页）；到了 200 时它会失败并提示重新复制副本。

M4c 的编号工具（端子、电缆、线号）作用于整个端子排或整个项目，测试无法只在新页上验证，也不能按 Ctrl+Z（开着两个项目时撤销可能落在夹具上，A14），所以**它们会永久改变 `writes\LargeWrite`**：端子名、电缆名、线号按方案重排。其他写测试不依赖这些名称：`WriteSafetyTests` 和 `PageDeleteTests` 用到的 `+A1-WE1` 等设备不在 S21 改名的电缆中（`+A1-WE1` 的类别是 `BusBarDefText`，不是电缆，2026-10-21 实测）。这些测试第一次在新副本上运行时核对 S21 的改动，之后的运行核对"没有可改的"；要重新验证第一次的结果，按上文重新复制副本。

## 离线宿主
标有 `[Trait("Host", "Offline")]` 的测试类（`ProjectAndPageTests`、`DeviceAndConnectionTests`、`PartAndMessageTests`、`PartSearchTests`、`MacroTests`、`NoSideEffectTests` 等，都只读）也可以不用界面中的 EPLAN，改在离线宿主 `EplanMcp.Bridge.Offline` 上运行（[M5 设计](../../docs/design/2026-10-20-m5-operability.md) 4.9）。CI 的自托管 runner 将来就这样运行。

准备：
1. 用同一个配置构建离线宿主和测试（离线宿主同插件一样需要本机的 EPLAN，不注册、不启动 EPLAN）：
   ```powershell
   dotnet build src\EplanMcp.Bridge.Offline -c Release
   dotnet build tests\EplanMcp.Large.Tests -c Release
   ```
2. 复制夹具，不加 `-Open`：`eng\scripts\eval-fixture.ps1`。离线宿主不用插件，也不需要注册。

运行：
```powershell
$env:EPLANMCP_OFFLINE_VARIANT_BIN = 'C:\Program Files\EPLAN\Electric P8\2026.0.3\Bin'
dotnet test --project tests\EplanMcp.Large.Tests -c Release --no-build -- --filter-trait "Host=Offline"
Remove-Item Env:EPLANMCP_OFFLINE_VARIANT_BIN
```
- `EPLANMCP_OFFLINE_VARIANT_BIN` 是**变体的** Bin（其中有 `W3u.exe`），不是 Platform 的 Bin（api-notes A22）。设置了它，`LiveEplan` 先把夹具复制到 `<scratch 根目录>\offline\<时间>-<进程号>\EPLAN_Sample_Project`，在这份副本上启动离线宿主（`--idle-exit 10`，管道前缀每次不同），再把测试进程的 `EPLANMCP_PIPE_PREFIX` 设为这个前缀：这次运行中的每个 `eplanmcp` 都只连离线宿主，不碰界面中的 EPLAN，后者可以同时运行（A22）。
- 离线宿主默认用 `src\EplanMcp.Bridge.Offline\bin\<测试的配置>\net481\EplanMcp.Bridge.Offline.exe`，`EPLANMCP_OFFLINE_EXE` 可以指定另一个。
- 夹具开在界面中的 EPLAN 里时，复制可能因为文件被占用而失败，或者带上未保存的状态，所以最好先在 EPLAN 中关闭夹具。
- 运行结束时 `eplanmcp` 退出，离线宿主没有 Host 连接 10 秒后自己退出；`LiveEplan` 等它退出，并核对退出码为 0、没有写 stdout。不满足时报告失败，给出日志的位置（`<数据根目录>\logs\bridge-offline-*.log`），并保留副本；正常退出时删除副本。
- 没有标 `Host=Offline` 的测试不要这样运行：离线宿主只打开了一份副本，写测试要的 `writes\` 副本不在其中；离线宿主没有界面、不提供审批（`approval.ask`），scratch 之外的写入和 Destructive 工具只会被拒绝。

不经测试、手工启动离线宿主（例如查问题时），先复制一份项目到 scratch，再：
```powershell
src\EplanMcp.Bridge.Offline\bin\Release\net481\EplanMcp.Bridge.Offline.exe --variant-bin 'C:\Program Files\EPLAN\Electric P8\2026.0.3\Bin' --project "<scratch 中的副本>.elk" --pipe-prefix EplanMcpManual. --idle-exit 300
```
`eplanmcp` 在 `EPLANMCP_PIPE_PREFIX=EplanMcpManual.` 下只找到它。按 Ctrl+C 或空闲 300 秒后，它停止管道、关闭项目、退出 EPLAN。退出码：0 正常，1 EPLAN 或 Bridge 失败（见日志），2 命令行错误。

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

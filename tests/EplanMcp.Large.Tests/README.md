# Large 测试：工具的实机测试

`ProjectCreateToolTests` 验收 `eplan_project_create` 的两种模式：自动寻找唯一 `GB_bas001.zw9`（或 `EPLANMCP_LARGE_BASIC_PROJECT`），备份用 `EPLANMCP_LARGE_ZW1` 指定非客户示例文件；材料缺失跳过。源复制进测试 Host 的 imports，核对源 SHA-256 不变、兼容性 `IsCompatible/18.0`、结束后关闭、重复目标及已有空目录拒绝、审计；保留 `User.eod/eox`。尚未运行，不代表跨版本兼容性验证。

这里的测试通过 `eplanmcp` 连接**正在运行的 EPLAN**（只读测试也可以改用离线宿主，见下文"离线宿主"）：用夹具项目的已知数据核对每个只读工具（[M2 设计第 7 节](../../docs/design/2026-10-18-m2-read-core.md)），写工具的测试只写夹具之外的副本（见下文"写测试"），并在其中两份副本上验证写工具的质量场景 Q1、Q2、Q6（[M3 设计第 7 节](../../docs/design/2026-10-19-m3-safe-writes.md)，[测试规范第 4 节](../../docs/standards/testing.md#4-large-测试实机)），以及 Destructive 工具（[M4 设计第 7 节](../../docs/design/2026-10-20-m4-workflows.md)，见下文"Destructive 工具"）。全部标为 `Size=Large`，CI 中不运行。

## P25 完整任务验收

`PlanTaskAcceptanceScenariosTests` 经真实 stdio Host 从基本项目新建、打开独立 scratch 项目，验证建页→改描述（有设备时还改名）、初备份、范围冲突、选择变化、审批等待和缺能力。2026-10-25 已在 EPLAN 2026.0.3（Electric P8）实跑 5/5 通过，审批用例的批准和到期两种结果都出现过。

准备兼容的示例 `.zw9`，可用 `EPLANMCP_LARGE_BASIC_PROJECT` 指定；默认依次查找唯一 IEC、GB 基本项目。导航复用 `PlanStepLiveSession` 的本机 Remote Client 辅助程序，只用 `ProjectOpen /Quiet` 打开已确认关闭的项目，用 `XPrjActionProjectClose` 关闭已核对的当前项目，见 [A46](../../docs/eplan/api-notes.md#a46-项目打开当前选择与导航器顺序p252026-10-25eplan-20260325421electric-p8)。从 worktree 运行时，`EPLANMCP_EPLREMOTE` 必须指向**主 checkout** 的 `.local/tools/eplremote/bin/Release/net481/eplremote.exe`；另设 `EPLREMOTE_HOST`，端口可用 `EPLREMOTE_PORT` 指定。

运行：`dotnet test --project tests/EplanMcp.Large.Tests -c Release --no-build -- --filter-class EplanMcp.Large.Tests.PlanTaskAcceptanceScenariosTests`。使用界面中的标准 Large 夹具；自建项目使用唯一名称 `LargeP25t_<8 hex>`、`LargeP25o_<8 hex>`，排在夹具和 `LargeOutside` 之后、`LargeWrite` 之前（`LargeWrite` 会被其他测试改动，重开可能弹模态提示，所以始终不关闭它），打开后核对选择未变。普通用例直到清理才关闭已知副本。场景 5 在夹具当前时显式为 T 编译，再逐个核对并关闭当前的已知副本，记录关闭集合，直到 O 当前；核对只有 T 被修改，O 和重开的全部副本设计文件不变（关闭过的副本排除关闭时改写的 `ProjectInfo.xml`，页内容另行核对）。审批用例只观察 `waiting_approval`，让工程师处理或按测试配置 60 秒到期，测试绝不点击或关闭审批窗口；工程师批准时核对批准审计和初备份，拒绝或到期时核对项目未变。

清理先核对并关闭当前的已知副本，直到首个自建项目当前；然后只关闭当前自建项目，直到自建项目全部关闭，再按夹具、`LargeOutside` 的顺序重开本例关闭的副本。每次打开前确认目标未打开，夹具重开后立即核对它当前；失败时停止并保留现场。最终核对打开集合与开始时完全相同、夹具当前后，才删除本例目录和页面。未知项目或写入未结算时保留全部项目、审计和备份；审计与初备份保留在测试输出的数据目录中。

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
dotnet test --project tests\EplanMcp.Large.Tests -c Release --no-build -- --filter-class "EplanMcp.Large.Tests.PlanFunctionsTests" --filter-class "EplanMcp.Large.Tests.PlanImpactTests"   # 只读 plan.functions 和 P06 闭包
dotnet test --project tests\EplanMcp.Large.Tests -c Release --no-build -- --filter-class "EplanMcp.Large.Tests.ProjectBackupTests"   # 独立备份，保留备份副本
dotnet test --project tests\EplanMcp.Large.Tests -- --filter-class "EplanMcp.Large.Tests.PageDeleteTests" --filter-class "EplanMcp.Large.Tests.DeviceRenumberTests" --filter-class "EplanMcp.Large.Tests.PageRenumberTests" --filter-class "EplanMcp.Large.Tests.TerminalRenumberTests" --filter-class "EplanMcp.Large.Tests.CableRenumberTests" --filter-class "EplanMcp.Large.Tests.ConnectionNumberTests" --filter-class "EplanMcp.Large.Tests.ProjectTranslateTests"   # 只跑 Destructive 工具
```
- 测试一个接一个运行。只读测试共用同一个 `eplanmcp`，它就是本项目构建出的那个（随项目引用复制到测试旁边）。
- 开始前检查 EPLAN 已连接、当前项目是 scratch 根目录下的夹具；不满足时，每个测试都报告该怎么做，而不会给出看不懂的断言失败。
- 夹具只读：除写测试只写 `writes\` 下的副本（见"写测试"）之外，测试不写任何项目。`NoSideEffectTests` 核对调用全部读工具前后，夹具的 `.elk` 和 `.edb` 中每个文件的大小和修改时间都没有变化。
- 多语言文本都显式传入 `language`，结果不受 EPLAN 界面语言影响；只有回退的测试按界面语言分两种情况判断。

### Q1、Q2、Q6
`WriteSafetyTests` 另起一个 `eplanmcp`，数据根目录在系统临时目录中（测试结束后删除），其中的 `config.json` 只把 `<scratch 根目录>\writes\` 当作 scratch 根目录，其他项目的写入一律拒绝，不看工程师自己的 `config.json`。所以对 `not-scratch\writes\` 下那份副本的写入只会被拒绝，也不会弹出审批对话框。`writes\` 下开着几个项目时（`eplan_macro_insert` 的写测试也用这个目录），取 `LargeWrite`。

两份副本可以反复使用：Q1、Q2 不改变它们，Q6 每次取新的页号和描述文字。副本出了问题时，在 EPLAN 中关闭它们，运行 `eval-fixture.ps1 -WriteCopies -Force`（它也会重新复制夹具，所以要先关闭 EPLAN），再按准备第 3 步打开。

## 核对的内容

`ImportListTests` 使用 `DestructiveHost` 的 `LargeWrite` 副本：部件清单先经 `eplan_export_parts` 导出，再复制到 imports 回导，核对引用数 +2、备份、300 秒预算和约 5 秒心跳；会永久改变写副本。设备清单用 `fixtures\` 中 S34 导出的两条条目（`XDLXmlExporter` 的 XML 和 CSV），写副本原本没有条目，导入后应恰为 2 条。完整事务下的故障注入回滚、提交后 Ctrl+Z、精确引用字段和系统部件库业务文件不变仍需实机补验。
| 测试类 | 内容 |
|---|---|
| `PlanFunctionsTests` | 只读 `fixtures\EPLAN_Sample_Project`（或离线宿主的夹具副本），分页读取全部 device.list（含辅助功能）并结合完整快照发现全部名称，再以每批最多 200 个名称读 plan.functions：动态寻找无页功能、同名跨页主功能、超过 50 个同名辅助功能，核对完整性、持久标识、排序及与快照/列表的一致性；只有完整扫描确实没有相应对象时才跳过，并说明缺少哪类对象，能力缺失或 complete=false 必须失败，选中对象的名称、页、type/oid 写入测试输出；尚未实跑 |
| `PlanImpactTests` | 只读 P06 闭包，不调用改名/删页的 plan 或 execute：A13 优先选择 `=F+A2-KF1`，否则动态找完全放置的跨页设备，逐项核对 read/modify scope 和对象哈希；A18 优先找 `+A1-WE1` 主功能页，否则找会留下 orphan 的页，核对全部剩余功能及修改范围，再用只读取得的局部基准验证缺页证据会 CAPABILITY_MISSING；独立测试查找同名无页功能关联的页，核对拒绝及含无页功能的 orphan 诊断，无合适样本时明确跳过；尚未实跑 |
| `ProjectAndPageTests` | 页数、设备数、连接数、部件引用数和各类页的数量；detailed 属性的名称和索引；按类型、名称前缀筛选页；读取页描述的耗时；页上的设备；不存在的页 |
| `DeviceAndConnectionTests` | 设备总数，同一页上的设备不重复；短标识 `-FC1` 的 14 个候选；断路器 `=GAA+A1-FC1` 的部件、连接点，以及负载侧接到哪些设备；不存在的设备 |
| `PartAndMessageTests` | 部件总数和各制造商的部件数；按语言读部件描述，以及省略 `language` 时的回退；系统消息和检查结果；选择 EPLAN 实例 |
| `MacroTests` | 在宏目录中按名称找到示例宏 `Contactor_Feedback_Control.ema`，全量扫描完整；读出它的表达类型、变体、尺寸和设备（与 spike S10 一致） |
| `CableTests` | 电缆 `+B1-WG2` 的两端（`=F+B1-SG1`、端子 `+A2-XG4:2`）和 2 根芯线，短标识 `-WG2`；`eplan_device_list` 列出的每根电缆都能读出（最慢的一根写入测试输出）；断路器不是电缆。S21 没有验证的属性文本、线号和耗时写入测试输出 |
| `TerminalTests` | 45 个端子排按名称排序，`+A1-XD4` 有 15 个端子，`+A2-XD2` 有 3 个跨接（S21）；`name_contains` 不区分大小写；`+A2-XD2` 的 5 个端子中有跨接，`+B2.X1-XZ1` 有 33 个端子；短名称 `-XD4`、不存在的端子排。列表和 33 个端子的耗时，以及 `+A2-XD2` 每个端子的两侧、跨接和 PLC 写入测试输出，供与 EPLAN 中的端子图对照 |
| `PlcTests` | PLC 盒 `=K1+A2-KE1` 的 34 个通道对象（17 个名称），不是 CPU（S21）；`:2.3` 接到 `+A2-XD4:3`；`eplan_device_list` 列出的每个 PLC 盒都能读出；断路器不是 PLC 盒。站、机架、槽，以及每个通道的页、是否主表达、地址、符号地址、数据类型、文本和连接写入测试输出，供与 EPLAN 的 PLC 导航器对照 |
| `PartSearchTests` | 开发机部件库（S24）中 `3RH21` 找到 `SIE.3RH2122-1HB40`（变体 1）：有宏，制造商、型号、订货号、产品组和英文描述与 S24 一致；`manufacturer` 是整个简称（`sie` 找到，`SI`、`PXC` 找不到）；省略 `language` 时读出描述（回退语言写入测试输出）；宽泛查询读完整个库的耗时写入测试输出；搜索前后部件库数据目录中的文件不变（`.lck` 除外，S24）。数据目录按库名在 `%PUBLIC%\EPLAN\Data` 下查找，找不到唯一的一个时跳过该测试，可用环境变量 `EPLANMCP_PARTS_DATABASE` 指定 `….adb` 目录 |
| `PartExportTests` | `eplan_part_export` 导出开发机的部件库（只读，A27、A30）：全部部件的 XML 得到一个 `LargePartsDb.xml`，根元素 `partsmanagement` 的 `count` 等于 `partCount`，`variantCount` 不小于它（2026-10-21：454 和 464）；只导出 `SIE.3RH2122-1HB40` 时两者都为 1，文件中只有这一个 `part`；再加一个不存在的部件号时返回 `PART_NOT_FOUND`（`metadata.missing` 为它），不新建导出目录。每次导出只在 `exports\parts-database\` 下新建的目录中留下一个文件，`database` 与 `eplan_part_search` 相同，导出前后部件库的 `.alk` 和 `.adb` 中的文件（`.lck` 和 EPLAN 的会话记录 `User.eod`、`User.eox` 除外，A30）SHA-256 不变，导出后 EPLAN 仍然响应；核对后删除本次目录。耗时、大小、两个计数、`part` 元素数和不同部件号的个数写入测试输出。EDZ（约 44 秒、128 MB）只在设置 `EPLANMCP_LARGE_EDZ=1` 时运行，发布前跑一次（2026-10-27 经负责人批准运行，全部 6 个通过，共约 41 秒）。**`backup` 格式（待实机验证：备份第一次写进导出目录）**：`file_name: "LargePartsBackup"` 得到唯一的 `LargePartsBackup.zw6`，以 7z 文件头 `37 7A BC AF 27 1C` 开头，大小 100 KB–500 MB（S24：991 KB），`variantCount` 与同一次运行中 XML 全部部件的相同，`hint` 说明计数来自数据库，部件库数据文件不变（同上）；`parts` 与 `backup` 同用、`file_name: "Large parts"` 都是 `INVALID_ARGUMENT`，不新建导出目录；耗时和大小写入测试输出。通过后在 `catalog/mapping/overlay.yaml` 中给 `eplan_part_export` 加一条写明 backup 的实机记录。数据目录的查找同 `PartSearchTests` |
| `TranslationExportTests` | `eplan_translation_export` 导出开发机的翻译库（只读，A27、A30）：制表符分隔得到一个 `LargeTranslations.txt`，第一行含制表符，编码、第一行和第二行写入测试输出（规格待确认列）；XML 得到一个 `LargeTranslations.xml`，开头 3000 个字符写入测试输出。每次导出只在 `exports\translation-database\` 下新建的目录中留下一个非空文件，`database` 以被核对的文件名结尾，导出前后翻译库文件的 SHA-256 不变，导出后 EPLAN 仍然响应；核对后删除本次目录。翻译库文件默认是 `%PUBLIC%\EPLAN\Data` 下唯一的 `Translate.db`，找不到唯一的一个时跳过，可用环境变量 `EPLANMCP_TRANSLATION_DATABASE` 指定 |
| `NoSideEffectTests` | 读取（包括两个宏工具和部件库查询）不改动项目文件 |
| `SnapshotTests` | M6 的 `project.snapshot`（经 Host 的 `BridgeClient` 直接调用，还没有工具，[细化设计](../../docs/design/2026-10-21-m6-bridge-snapshot.md)第 7 节）：夹具连续两次完整快照，每页的哈希和快照哈希都相同，Bridge 一块给出的快照哈希等于按 Host 公式重算的；321 页、页上的 3,920 个功能、1,302 个连接（S31；S31 没有排除 3D 连接，只有连接数不符时先比较这一点）；页按编号升序；提取前后 `.edb` 中的文件（`.lck`、`User.*` 除外）不变（A36）。`=+-CU1` 是类型 17、编号 59641（S28）；只取它所在的页与完整快照中的哈希相同，不存在的页列在 `missingPages` 中。`object.resolve`：`=+-CU1`、它所在的页和第一个连接解析为 `resolved`，哈希与快照中的相同；名称改为 `=+-CU9`、页改为不存在的页、x 移动 0.01 mm、编号 999999999 分别为 `name`、`page`、`position`、`not_found`，前两项给出实际的名称和页。耗时、块数、`PAGE_FORMPLOT` 和 `FUNC_TECHNICAL_CHARACTERISTIC` 的示例值写入测试输出（这两项以前没有实机读过） |
| `SnapshotCopyTests` | 测试自己把夹具复制到 `<scratch 根目录>\snapshots\<时间>-<进程号>\`（含 `User.eod`/`User.eox`），用 `openReadOnly` 提取两次：两次的快照哈希相同（每次打开的库号不同，A34），也与打开的夹具相同，`PROJ_GUID` 相同；调用后副本不在打开的项目中，`.edb` 的文件（`.lck`、`User.*` 除外）不变。删掉副本的 `User.eod` 后为 `INVALID_ARGUMENT`（`metadata.missing` 为 `User.eod`），EPLAN 仍然响应、没有弹出对话框（A35）。`project.compatibility`：夹具与关闭的副本都是 `IsCompatible`、`18.0`（A31），前者 `open` 为 true、后者为 false，检查后副本仍未打开。结束时删除副本 |
| `ProjectBackupTests` | 经 Host 的 `BridgeClient` 调用 `project.backup`，优先选择已打开的 `scratch\writes\LargeWrite`；BackupFolders 在正常数据根的 `backups\` 下新建目录，备份保留。核对 `.elk/.edb`、页数、60 秒内的 Bridge 耗时和 Host 墙钟时间，再用 `openReadOnly` 快照确认副本可打开、页数相同、结束后关闭；当前项目、打开项目及其只读状态不变。同一目标再次调用为 `BACKUP_FAILED/not_started`，副本不变。0 页用例只查找已打开的 `scratch\writes\` 副本，没有就明确跳过，不创建项目或删除页面。仅编译，尚未运行 EPLAN |
| `RevisionListTests` | 夹具（`.elk`）：`state` 为 `none`，没有修订记录，`parent` 为 null；耗时和带修订标记的页数写入测试输出（规格待定：超过 2 秒再加 `detailed`）。修订中的项目：测试自己把夹具复制到 `<scratch 根目录>\revisions\<时间>-<进程号>\`，由辅助程序 `EplanMcp.Large.RevisionHelper` 在离线的 EPLAN 中给副本新建修订 `Large-1`（S23 r3 的做法），再在这份副本上启动离线宿主，另起一个 `eplanmcp` 读取：`state` 为 `tracking`，`path` 以 `.ell` 结尾，一条记录，名称 `Large-1`、用户有值、日期在测试运行的时间内；正常结束后删除副本。变体的 Bin 取 `EPLANMCP_REVISION_VARIANT_BIN`，没有设置时按测试所连 EPLAN 的变体和版本取 `<Program Files>\EPLAN\<变体>\<版本>\Bin`（A23），其中没有 `W3u.exe` 时跳过。**不要为它设 `EPLANMCP_OFFLINE_VARIANT_BIN`**：那会把整个运行切到离线宿主，写测试就找不到写副本。离线宿主的管道前缀只传给测试启动的进程，测试结束时核对测试进程的 `EPLANMCP_PIPE_PREFIX` 没有变。辅助程序和离线宿主要用测试的配置构建，离线宿主要能接受 `.ell`（`--project`） |
| `PlaceholderListTests` | 只读，夹具（S29，api-notes A33）：整个项目 `total` 为 2，页依次为 `=TM1+A1&EFS1/1`、`=TM2+A1&EFS1/1`，`name` 都是空串，`valueSets` 都是 `["V", "mA"]`，`variables` 都是 `["T1", "T2"]`，`assignedObjects` 都是 2，concise 中没有 `values`；`page: "=TM1+A1&EFS1/1"` 只有一个，不存在的页 `PAGE_NOT_FOUND`；`detailed`：每个占位符 4 个 `values`，按值集 `V`、`mA` 再按变量 `T1`、`T2` 排列，至少一个 `text` 非空；`currentValues` 可读时有 2 项，未锁定而不可读时省略并有 `hint`（2026-10-22，A33）；读取前后夹具的文件（含 `User.*`）不变。全部值和可读的当前值写入测试输出：**请与 EPLAN 中该占位符的值集对话框对照**，确认 `GetValue` 读出的文本与界面一致（S29 只用 `get_Value` 读过原始串），一致后在 `catalog/mapping/overlay.yaml` 中给 `eplan_placeholder_list` 加实机记录 |
| `ProjectExportTests` | 整个项目的导出（A25）：每次导出只在本次新建的目录中留下文件，导出根目录中没有别的新目录，夹具旁边没有新文件，夹具的设计数据不变（`User.*` 除外），导出后 EPLAN 仍然响应；核对后删除本次目录。PXF：`file_name` 为 `LargePxf.epj` 时得到 `LargePxf.epj`（不是 `.epj.epj`），不带主数据，大小在 100 MB–2 GB 之间；耗时写入测试输出。部件清单：`LargeParts` 得到一个 `LargeParts.xml`（1 KB–200 MB），耗时和文件开头 3000 个字符写入测试输出（规格待确认 XML 中每个部件有哪些字段），导出后 EPLAN 仍然响应（A25 的访问冲突）。标签：`Device tag list` 得到 `LargeLabels.txt`（开头写入测试输出），`Terminal diagram` 得到 `LargeLabels.xlsx`（S25），`scheme` 回显 EPLAN 中的名称；不存在的方案返回 `INVALID_ARGUMENT`，候选中有 `Device tag list`、没有 `InternalRefDefScheme`，不新建导出目录。缺失词表：`["en_US"]` 得到一个 `LargeMissing.txt`，第一行（制表符分隔）有 `en_US` 和 `COMMENT` 列，开头写入测试输出；`tl_XX` 不是翻译语言时返回 `INVALID_ARGUMENT`，`metadata.missing` 为 `tl_XX`，候选中有 `en_US`，共 20 种（S25）。PXF 约 600 MB，测试期间导出根目录所在的盘要有 2 GB 以上的可用空间 |
| `MacroInsertTests` | 写测试：在新页上用 `number` 插入 `Contactor_Feedback_Control.ema`（dry run、读回的设备、用 `eplan_page_create` 建的页已有幅面，A20），同一位置再插一次被回滚；`keep` 与已有设备合并时返回 `ALREADY_EXISTS`，项目的计数不变 |
| `DevicePlaceTests` | 写测试：在新页上按部件号放置 `SIE.3RH2122-1HB40`（S20），标识 `-KF9<页号>`：dry run 不放置，标识取页的结构 `=GAA+A1`；读回类别 `Coil`、5 个功能中 4 个未放置，`eplan_device_get` 在新页上找到它；再放一次 `ALREADY_EXISTS`，页上仍只有一个设备。部件库中没有的部件 `PART_NOT_FOUND`，不存在的变体 `INVALID_ARGUMENT`（列出变体 1），页上没有设备。scratch 之外的副本 `POLICY_DENIED`。页面图片所在的目录写入测试输出，在其中核对线圈显示标识、在 (80, 160) 附近、带触点镜像；一次 Ctrl+Z 撤销放置和改名要在只开着写副本时手工核对（"写测试"第 3 步） |
| `PlaceholderApplyTests` | 写测试，**需要实机验证的行为**（S29 只在回滚的 `SafetyPoint` 中应用过值集，api-notes A33）：写副本 `=TM1+A1&EFS1/1` 上没有名称的占位符，dry run 为 `not_started`、`values` 有 `T1`、`T2`，没有 `currentValuesBefore`；应用 `mA`：`committed`、`undo` 为 `AI: eplan_placeholder_apply`，执行结果的 `currentValuesAfter` 在事务内读回，2 个当前值与 `mA` 的 `values` 文本相同（不计顺序）；再应用 `V`：`currentValuesAfter` 与 `V` 的 `values` 相同，两组值不同时 `currentValuesChanged` 为 true。值集 `X`：`INVALID_ARGUMENT`、`not_started`，`candidates` 为 `(no name): V, mA`；`placeholder: "nope"`：`INVALID_ARGUMENT`；scratch 之外的副本 `POLICY_DENIED`。每次运行最后应用的是 `V`。前后值写入测试输出。**一次 Ctrl+Z 要手工核对**（"写测试"第 3 步，只开着写副本）：再应用一次 `mA`，按 Ctrl+Z，再应用 `mA`，这次执行结果的 `currentValuesBefore` 应为 `V` 的值。全部通过、Ctrl+Z 也核对过后，在 `catalog/mapping/overlay.yaml` 中给 `eplan_placeholder_apply` 加实机记录 |
| `ImportDxfTests` | 写测试，经 `WriteHost`（数据根目录在临时目录中，导入文件由测试自己写进其 `imports\`）：测试中生成的 R12 DXF（4 条线、1 个圆）导入新页：dry run 给出 SHA-256、不导入；提交后新增 1–100 个对象，再导入一次新增同样多（不是幂等的）；审计的参数中有文件的 SHA-256。EPLAN 自带的 Rittal 图 `%PUBLIC%\EPLAN\Data\DXF_DWG\Company name\RITTAL\4685200F.DXF`（S22）复制进导入目录：`fit`、`center` 新增 227 个对象（A29），`none`、`lower_left` 导入到另一页（对象数写入测试输出）；文件不在时跳过。导入目录中没有的文件 `FILE_NOT_FOUND`、`../` 和别的扩展名 `INVALID_ARGUMENT`；scratch 之外的副本 `POLICY_DENIED`。三张页的图片经只读测试的 `eplanmcp` 导出，目录写入测试输出，在其中核对图形的位置和缩放；一次 Ctrl+Z 撤销要在只开着写副本时手工核对 |
| `ImportPxfTests` | 写测试，经 `WriteHost`：用 `eplan_export_pxf` 把写副本导出为 EPJ（约 600 MB，S25），移进临时数据根目录的 `imports\`，再导入为 `<scratch 根目录>\writes\imported\LargeImport_<UTC 时间>`：dry run 给出新项目的路径、不建文件；提交后 `.elk` 和 `.edb` 都在，项目没有在 EPLAN 中打开，审计中没有审批、有按新项目路径的开始记录；同名再导一次 `ALREADY_EXISTS`。不存在的文件 `FILE_NOT_FOUND`，名称带点 `INVALID_ARGUMENT`。耗时和新项目的路径写入测试输出；**在 EPLAN 中打开它、核对页数与写副本相同要手工进行**。最新的一个导入项目保留，每次运行开始时删除以前的（EPLAN 中开着的删不掉，测试输出会说明）。导出和导入合计约 2 分钟，临时目录所在的盘要有 1.5 GB 以上的空闲空间 |
| `PartImportTests` | 🔴 **写正在用的部件库**，默认跳过，见下文"系统主数据（部件库）"：导入一个测试部件 `EPLANMCP.LARGE-TEST`，先备份；再导一次不变 |
| `PartSyncTests` | 🔴 **写正在用的部件库，并永久改变写副本**，默认跳过，见下文"系统主数据（部件库）"：把写副本的部件同步进部件库，先备份库和副本；再同步一次没有可做的 |
| `CheckAndReportTests` | 写测试：检查整个项目（`dry_run` 的页数；结果与 `eplan_check_list` 一致；再检查一次警告不变、14 条汇总提示再追加一遍（A20））、只检查两页、EPLAN 不认识的方案被回滚；更新全部报表（新增 `+A2&EPA1/1.2`，再更新一次不增删）、只更新目录页、电路图页不是报表页。规格中没有验证的结果写入测试输出 |
| `LanguageSetTests` | 写测试：显示语言设为 `en_US`、`zh_CN`，读回一致，再调用一次不改动，用 dry run 读出的 `before` 再调用一次恢复原样（示例项目是 `##_##`：按存储形式写回，A28 之外的行为）；加一种副本中没有的语言（`is_IS` 等中第一个没有的，**留在副本中**），再加一次不改动；源语言设为 `de_DE`，读回一致、其他设置不变，再设一次不改动，用 `before.source` 恢复（S23 r2）；不是翻译语言的显示语言返回 `INVALID_ARGUMENT` 和候选。耗时、新语言在列表中的位置、加语言时显示语言是否被 EPLAN 改动写入测试输出 |
| `LanguageRemoveTests` | Destructive：先用 `eplan_language_set` 加一种副本中没有的语言（`lt_LT` 等），dry run 为 0 条译文、不备份，批准后删除：`committed`，翻译语言回到原样，有新的备份；EPLAN 改了显示语言时再设回原值。删除 `sv_SE`：dry run 的 `textsAffected` 至少 460（S23），批准后读回不含 `sv_SE`，备份存在，再删一次 `INVALID_ARGUMENT`。**这一项永久改变副本，每份副本只能运行一次**，之后跳过并提示重新复制。不是翻译语言时 `INVALID_ARGUMENT`、候选，不审批、不备份。耗时、删除后的显示语言、备份路径写入测试输出 |
| `ProjectTranslateTests` | Destructive：源语言设为 `##_##` 时 `INVALID_ARGUMENT`、`not_started`，不审批、不备份（S23 r2）。新建一页，用 `eplan_page_update` 把页描述设为德语 `Motor`、英语 `Engine`，源语言设为 `de_DE`，只翻译这一页：dry run 为 1 of 1、不备份；批准后 `committed`，`textsChanged` 为 1，改动的是这一页的 `PAGE_NOMINATIOMN`，`eplan_page_get` 读出 `zh_CN` 为 `电机`、`de_DE` 仍为 `Motor`，有新的备份。英语描述是否被翻译库改写、耗时写入测试输出。两项结束时都把源语言写回原值，第二项删掉新页，副本原有的文本不变 |
| `PageDeleteTests` | Destructive：dry run `=GAA+A1&EFS1/1` 列出页上的功能数，以及其他页上失去主功能的三个设备 `+A1-WE1`（类别 `BusBarDefText`）、`+A1-WE2`、`+A2-WE1`（S12 的第四个 `+A2-WE2` 留下的功能不在页上，不列出），不审批；新的宏页：拒绝时 `APPROVAL_DENIED`、不建备份目录、页还在，审计只有拒绝；批准后新的备份目录中有副本（`.elk` 与 `.edb`），页没了，审计依次为批准、带 `backup` 的开始、`backupCompleted` 的结果；再删一次 `PAGE_NOT_FOUND`、不审批。一次删除 3 页。scratch 之外的副本 `POLICY_DENIED`，不审批、不备份，设计数据不变 |
| `DeviceRenumberTests` | Destructive：在新页上用 `number` 插入宏后，`only_unnumbered` 编号：EPLAN 不崩溃（只交带 `?` 的设备标识，A20），`=GAA+A1-?QA…` 出现在 `stillUnnumbered` 中，没有改动所以 `not_started`、`undo` 为 null，备份照样有；方案 `Default`：`INVALID_ARGUMENT`、`rolled_back`，候选与 S12 的 5 个方案相同，备份保留，`?` 标识不变 |
| `PageRenumberTests` | Destructive：三张新页从 200、步长 10 重编号为 `/200`、`/210`、`/220`（S12），dry run 不列新页名；`start` 为 1 时与 `/1`、`/2` 冲突：`ALREADY_EXISTS`、`rolled_back`，页名不变，备份保留 |
| `TerminalRenumberTests` | Destructive，**改变副本**：`+A1-XD1` 全部端子按 `Numerisch` 重新编号：只改这一排（`+A1-XD4` 不变），名称只是互换（S21：`:2`→`:3`、`:3`→`:5`、`:4`→`:2`、`:5`→`:4`，写入测试输出），第一次 `committed`，之后的运行没有改名、`not_started`、`undo` 为 null；新宏页上 `only_unnumbered` 给整个项目编号：EPLAN 不崩溃，只改名称中 `:` 之后带 `?` 的端子；`numerisch`（大小写不符）：`INVALID_ARGUMENT`、`not_started`，候选中有 `Numerisch`、没有 `InternalRefDefScheme`，不审批、不备份 |
| `CableRenumberTests` | Destructive，**改变副本**：全部电缆按 `Standard` 编号：标识仍不重复，改动只在原有电缆之间，S21 没有改名的电缆 `+B1-WG2` 没有改名，有改动时 `committed`、否则 `not_started`；改动写入测试输出，对照 S21 的 `+A2-WD4`↔`+A2-WD5`、`+B2.Y1-WD1`↔`+B2.Y1-WD2`；只给两根 `+A2-WD` 电缆：其他电缆不变；`keep_existing` 给没有 `?` 的电缆 `+B1-WG2`：不审批、不备份；断路器 `=GAA+A1-FC1`：`INVALID_ARGUMENT`（`CircuitBreaker`），`standard`：候选中有 `Standard`（项目设置 `CableLog.CableNumberingScheme`），都不审批、不备份 |
| `CableGenerateTests` | Destructive，仅样例回归：写副本 `LargeWrite` 上显式使用三个 `Standard`，审批后备份；前后均 78 根，0 改名、0 新增、0 删除，已读业务字段不变；`committed`、`undo=null`；只读打开备份核对页数。此测试不证明正向生成。 |
| `ConnectionNumberTests` | Destructive，**改变副本**：`Standard`、`overwrite: all` 给整个项目编线号：编号后有线号的连接不少于之前，列出的改动最多 200 条、超过时 `truncated`，有改动时 `committed`、否则 `not_started`，备份存在；改动数和前 12 条写入测试输出（S21 在新副本上：605 条改变，116 → 659）；接着用 `except_manual` 再编一次并写入输出；`Potential`：`INVALID_ARGUMENT`、`not_started`，候选中有 `Standard`、`Potenzialorientiert`（项目设置 `WireNumberingLog.Schema`，S21 中 11 个），没有 `InternalRefDefScheme`，不审批、不备份 |
| `WriteSafetyTests` | **Q1**：对 scratch 之外的副本建页、改页描述、设备改名都返回 `POLICY_DENIED`、`not_started`，设计数据文件（`User.*` 除外，A17）和审计都不变。**Q2**：把 `+A1-WE1`（类别 `BusBarDefText`）改名为 `=F+A1-WE19`，EPLAN 丢掉 `=F`，读回不一致而回滚（A17 的自然故障）：`INVALID_ARGUMENT`、`rolled_back`，它的全部功能保持原名，审计为 `rolled_back`，不暂停写入。**Q6**：直接经 Host 的 `BridgeClient` 调用 `page.create.plan`，在同一结构中另建一页，再用旧指纹调用 `page.create.execute`，返回 `PLAN_STALE`、`not_started`，计划的页不存在；改页描述同理，后来的修改保留 |
| `ObjectResolveTests` | 写副本，经 `DestructiveHost`：新建两页并提取它们，两个页定位符都解析为 `resolved`；一页用 `eplan_page_renumber` 重编号到第二页之后（脚本批准），另一页用 `eplan_page_delete` 删除；之后前者为 `stale`、`name`，`actualName` 是新页名（编号不变，不会被改指别的对象），后者为 `stale`、`not_found`，都不带哈希。结束时删掉自己的页 |
| `ProjectCreateTests` | `project.create.plan`/`.execute`（经 Host 的 `BridgeClient`，还没有工具）：测试把 `%PUBLIC%\EPLAN\Data` 下唯一的 `GB_bas001.zw9`（或环境变量 `EPLANMCP_LARGE_BASIC_PROJECT` 指定的基本项目，找不到就跳过）复制到 `<scratch 根目录>\created\<时间>-<进程号>\imports\`，新建 `LargeCreate` 到同一目录的 `project\`：计划是一条 `create project LargeCreate`；新项目 1 页（S27）、`PROJ_GUID` 以 `GB_bas001@` 开头（A34）、`.elk` 和 `.edb` 都在、调用后不在打开的项目中（A34）；`project.compatibility` 为 `IsCompatible`、`18.0`；`openReadOnly` 快照 1 页、`PROJ_GUID` 相同，之后仍未打开；再对这个目录规划为 `ALREADY_EXISTS`，目录内容不变。耗时写入测试输出，结束时删除目录 |
| `ProjectRestoreTests` | `project.restore.plan`/`.execute`：没有产品路径能生成 `.zw1`，所以要用环境变量 `EPLANMCP_LARGE_ZW1` 指向一个示例项目的备份（例如 S12 在 scratch 副本上用 `backup /TYPE:PROJECT` 做的那份），没有就跳过。测试把它复制到 `<scratch 根目录>\restored\<时间>-<进程号>\imports\`，还原到同一目录下的 `a\`、`b\`：**首先验证进程内执行 EPLAN 的 `restore` action 可行**（S27 只用过 Remote Client）；数据库版本 `18.0`、`.elk` 和 `.edb` 都在、调用后不在打开的项目中；两份 `openReadOnly` 快照的哈希相同、`PROJ_GUID` 相同（S28 r1、A34）。再对 `a\` 规划为 `ALREADY_EXISTS`；对 `c\` 规划后放进一个 `LargeRestore.elk`，执行为 `ALREADY_EXISTS`、`not_started`，那个文件逐字节不变、目录中没有别的文件（A31：EPLAN 会覆盖）。耗时写入测试输出，结束时删除目录 |
| `SnapshotWriteTests` | 写副本 `writes\LargeWrite`（经 `WriteHost`）：完整快照，用 `eplan_page_update` 改 `=GAA+A1&EFS1/1` 的中文描述，再完整快照：哈希变化的只有这一页，修改标记（`PAGE_LASTAUTOMODIFICATIONDATE`/`TIME`）变化的也只有这一页（S31），快照中读到新描述；只取这一页与完整快照中的哈希相同。前后的修改标记写入测试输出 |

Q6 的流水线部分（审批之后用计划的指纹执行、`PLAN_STALE` 映射为 `not_started`、不打开事务）已由 Medium 测试 `PageCreateInteropTests` 在假项目上覆盖；这里只验证 Medium 测试做不到的部分：真实适配层的指纹能看到在 EPLAN 中发生的变化（[`eplan_page_create` 规格第 7 节](../../docs/tools/eplan_page_create.md)）。

### Destructive 工具
`eplan_page_delete`、`eplan_device_renumber`、`eplan_page_renumber` 和 M4c 的编号工具每次都要工程师审批，scratch 中也一样，而 `eplanmcp` 不能自己批准（ADR-0005、ADR-0011）。所以这些测试不启动 `eplanmcp.exe`，而是由 `DestructiveHost` 在测试进程中按 `serve` 的方式组装 Host（`AddEplanMcp`，内存中的流代替 stdio），连接真实的 Bridge，只把审批渠道换成 `ScriptedApprovalChannel`（[`eplan_page_delete` 规格第 7 节](../../docs/tools/eplan_page_delete.md)）：
- 它只批准测试在调用前登记的工具、项目和变更清单（与 EPLAN 对话框中显示的逐行相同），每次登记只用一次，其余一律拒绝，包括解除写入暂停的确认。EPLAN 中不弹出审批对话框。
- 这个类型只在本测试程序集中。`eplanmcp.exe` 没有任何开关、配置项或环境变量能启用它；Small 测试 `PolicyEngineTests` 核对 Host 程序集中 `IApprovalChannel` 的实现只有工程师操作的两个窗口 `WorkbenchChannel` 和 `EplanDialogChannel`，`AddEplanMcp` 注册的也只有这两个，Workbench 在前（ADR-0014）。
- 数据根目录、配置与 `WriteHost` 相同：系统临时目录中新建，只把 `<scratch 根目录>\writes\` 当作 scratch，其他项目的写入一律拒绝。审计、备份都在这个目录中，测试类结束时删除。**每次执行的 Destructive 写入都先把整个副本复制到 `backups\` 下（示例项目约 210 MB），一个测试类最多留下 4 份**，运行时临时目录所在的盘要有 1 GB 左右的空闲空间。
- 准备与写测试相同（上文第 2、3 步）：用的是 `writes\LargeWrite`；`PageDeleteTests` 中只有一个测试用 `not-scratch\writes\` 下的副本，核对 `POLICY_DENIED`。

`eplan_language_remove` 的 `LanguageRemoveTests` 也在 `DestructiveHost` 上运行。规格第 7 节中两项要在 EPLAN 中手工核对：用测试输出中的备份路径打开备份，项目设置的翻译语言中仍有 `sv_SE`；删除 `sv_SE` 之后对写副本按一次 Ctrl+Z，记录译文和语言设置是否恢复（只开着写副本、让它成为当前项目时再按，见下文"写测试"第 3 步），结果补进 api-notes A28。`ProjectTranslateTests` 同样在 `DestructiveHost` 上运行；翻译提交后 Ctrl+Z 能否一步撤销（规格第 5 节未验证）也要这样手工核对：在测试之外对写副本的一页翻译一次，再按 Ctrl+Z。

页的三组测试让副本保持原样：只规划（dry run）示例项目自己的页，执行的删除和重编号只针对测试在 `=GAA+A1&EFS1` 最后一页之后新建的页，测试最后再用 `eplan_page_delete`（同样由脚本批准）把这些页删掉。测试失败时会尽量删掉它们，并在测试输出中说明；删不掉时测试输出列出页名，在 EPLAN 中手工删除，或按上文重新复制副本。`PageRenumberTests` 把新页重编号为 `/200`、`/210`、`/220`，所以 `=GAA+A1&EFS1` 的页号到 200 之前都能运行（其他写测试每次运行新增 4 页）；到了 200 时它会失败并提示重新复制副本。

M4c 的编号工具（端子、电缆、线号）作用于整个端子排或整个项目，测试无法只在新页上验证，也不能按 Ctrl+Z（开着两个项目时撤销可能落在夹具上，A14），所以**它们会永久改变 `writes\LargeWrite`**：端子名、电缆名、线号按方案重排。其他写测试不依赖这些名称：`WriteSafetyTests` 和 `PageDeleteTests` 用到的 `+A1-WE1` 等设备不在 S21 改名的电缆中（`+A1-WE1` 的类别是 `BusBarDefText`，不是电缆，2026-10-21 实测）。这些测试第一次在新副本上运行时核对 S21 的改动，之后的运行核对"没有可改的"；要重新验证第一次的结果，按上文重新复制副本。

### 系统主数据（翻译库）
`TranslationImportTests` 写正在用的翻译库，默认跳过。每次运行须先取得项目负责人批准，再由维护者设置 `EPLANMCP_LARGE_MASTERDATA=1`，只运行这个类；结束后移除该环境变量。2026-10-27 经负责人批准第一次运行，通过（约 12 秒）；EPLAN 关闭后用文件副本恢复，翻译文件夹 4 个文件的 SHA-256 与运行前相同（导入只改了 `Translate.db`）。

- 测试在连接前检查 opt-in，经 `DestructiveHost` 和只读计划取得配置的库的实际路径。导入前先对整个翻译文件夹（含子文件夹）的所有文件计算 SHA-256，复制到系统临时目录中独立的 `eplanmcp-translation-backup-*\files\`，逐个核对副本及源文件，`manifest.json` 记录原库、原目录和相对文件名到哈希的映射；副本在 Host 数据目录之外，不随测试结束删除。
- 先导出确认库里没有 `EplanMcpLargeTest`；再导入一个 `en_US`/`zh_CN` 测试词条，`added=1`，重导 `added=0`，两次均须独立审批并先生成非空 `.zw7`，均为 `committed`。
- 运行后由操作员关闭 EPLAN，将 `files\` 中的文件复制回清单所记原目录，核对文件集合及所有 SHA-256 与清单完全一致；核对成功后才清理恢复副本。测试不会自动恢复。`.zw7` 在 Host 临时目录中，随 Host 结束删除，不能替代保留的文件副本。

```powershell
dotnet test --project tests\EplanMcp.Large.Tests -- --filter-class "EplanMcp.Large.Tests.TranslationImportTests"
```

### 系统主数据（部件库）
🔴 **这些测试写 EPLAN 配置的那个部件库**，也就是工程师正在用的库：S24 证实副本隔离不了导入（结论 8），所以没有"测试库"可用（[ADR-0019](../../docs/adr/0019-master-data-writes.md)）。它们默认跳过，只有环境变量 `EPLANMCP_LARGE_MASTERDATA` 为 `1` 时才运行，跳过的判断在连接 EPLAN 之前。**每次运行都要项目负责人事先批准**，并由维护者按下面的步骤做：
1. 运行前：确认部件库在本机文件中（不是 SQL Server；在 SQL Server 上时测试会得到 `POLICY_DENIED`），对库的全部文件（`.alk` 和 `.adb\` 中的文件）计算 SHA-256，再把它们复制一份到 scratch 之外的地方；
2. 在同一个 PowerShell 中 `$env:EPLANMCP_LARGE_MASTERDATA = "1"`，只运行这些测试类（下面的命令），运行后 `Remove-Item Env:EPLANMCP_LARGE_MASTERDATA`；
3. 运行后：关闭 EPLAN，用第 1 步的副本覆盖库的全部文件，再核对 SHA-256 与第 1 步相同（`User.eod`、`User.eox` 和 `.lck` 是 EPLAN 自己改写的，api-notes A30，不必一致），然后再启动 EPLAN。测试自己做的主数据备份在临时数据根目录中，测试结束时随之删除，不作为恢复手段。

```powershell
dotnet test --project tests\EplanMcp.Large.Tests -- --filter-class "EplanMcp.Large.Tests.PartImportTests"
dotnet test --project tests\EplanMcp.Large.Tests -- --filter-class "EplanMcp.Large.Tests.PartSyncTests"   # 还要按"准备"第 3 步打开写副本
```

2026-10-21 第一次运行：`PartImportTests` 和 `PartSyncTests` 都通过（约 11 秒），EPLAN 关闭后用文件副本恢复，库的 28 个文件的 SHA-256 与运行前完全相同（这次 `User.*` 也相同）；结果见 api-notes A30。

`PartImportTests` 经 `DestructiveHost`（不需要打开写副本）调用 `eplan_part_import`，导入测试自己写进临时 `imports\` 的文件 `eplanmcp-large-test.xml`，其中只有一个部件 `EPLANMCP.LARGE-TEST`（变体 1，结构同 S24 的导出，字段只有部件号、描述和部件类型；EPLAN 接受这样短的文件，2026-10-21 实测，A30）：
- dry run：`added` 恰好是 `EPLANMCP.LARGE-TEST`（否则失败并提示先恢复库），不审批、不备份；
- 由脚本批准变更行 `add 1 part from eplanmcp-large-test.xml to the parts database` 后导入：`committed`，`added` 为它，`partsAfter` = `partsBefore` + 1，`missing` 为空，`backup` 是临时数据根目录 `backups\masterdata\` 下非空的 `.zw6`；
- 再导一次：`not_started`，`skipped` 为它，没有再审批，也没有再备份；
- 测试输出：库、导入前后的部件数、备份加导入的耗时、备份大小、提示，以及审批问题中的变更行。

`PartSyncTests` 经 `DestructiveHost` 对写副本 `writes\LargeWrite` 调用 `eplan_part_sync`（`append_new`）。**它永久改变写副本**：EPLAN 把副本只引用的部件存进副本（S24 r15：293 → 341），所以只有新复制的副本上第一次运行会同步，之后的运行只核对"没有可做的"：
- dry run：输出要新增、要存入项目的部件数（S24 r15：示例项目的部件在开发机的库中都有，新增为 0），`not_started`，不审批；
- 有可做的时，由脚本批准与 dry run 数量一致的变更行（`add N parts from LargeWrite to the parts database`、`store N parts the project references in project LargeWrite (now M)`）后同步：`committed`，`undo` 为 `AI: eplan_part_sync`，`missing`、`notStored` 为空，库和项目的部件数不少于期望，`backup`（`.zw6`）和 `projectBackup`（`.elk`）都在；
- 再同步一次：`not_started`，新增和存入都是 0，没有再审批；
- 测试输出：耗时、前后的部件数、提示。规格第 5 节的两点：EPLAN 的系统消息中是否仍有 `S328014`（写存取前没有加锁该对象）——第一次运行中没有出现（A30）；同步之后在只开着写副本时按一次 Ctrl+Z，项目中存入的部件是否撤回（部件库不会撤回）——要手工核对并补进 api-notes A30。

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
dotnet test --project tests\EplanMcp.Large.Tests -c Release --no-build -- --filter-trait "Host=Offline" --filter-not-trait "Fixture=Scale"
Remove-Item Env:EPLANMCP_OFFLINE_VARIANT_BIN
```
- `EPLANMCP_OFFLINE_VARIANT_BIN` 是**变体的** Bin（其中有 `W3u.exe`），不是 Platform 的 Bin（api-notes A22）。设置了它，`LiveEplan` 先把夹具复制到 `<scratch 根目录>\offline\<时间>-<进程号>\EPLAN_Sample_Project`，在这份副本上启动离线宿主（`--idle-exit 10`，管道前缀每次不同），再把测试进程的 `EPLANMCP_PIPE_PREFIX` 设为这个前缀：这次运行中的每个 `eplanmcp` 都只连离线宿主，不碰界面中的 EPLAN，后者可以同时运行（A22）。
- 离线宿主默认用 `src\EplanMcp.Bridge.Offline\bin\<测试的配置>\net481\EplanMcp.Bridge.Offline.exe`，`EPLANMCP_OFFLINE_EXE` 可以指定另一个。
- 夹具开在界面中的 EPLAN 里时，复制可能因为文件被占用而失败，或者带上未保存的状态，所以最好先在 EPLAN 中关闭夹具。
- 运行结束时 `eplanmcp` 退出，离线宿主没有 Host 连接 10 秒后自己退出；`LiveEplan` 等它退出，并核对退出码为 0、没有写 stdout。不满足时报告失败，给出日志的位置（`<数据根目录>\logs\bridge-offline-*.log`），并保留副本；正常退出时删除副本。
- `RevisionListTests` 的第二项不用这个变量（见上表的 `EPLANMCP_REVISION_VARIANT_BIN`），它另起自己的离线宿主，与界面中的 EPLAN 或 `LiveEplan` 的离线宿主同时运行；运行前另外构建辅助程序：`dotnet build tests\EplanMcp.Large.RevisionHelper -c Release`。辅助程序和离线宿主一样只在装有 EPLAN 的机器上构建（在 `EplanMcp.slnx` 中，不在 `EplanMcp.Ci.slnf` 中），只改它自己复制的副本，新建的修订不能撤销（S23）。
- 没有标 `Host=Offline` 的测试不要这样运行：离线宿主只打开了一份副本，写测试要的 `writes\` 副本不在其中；离线宿主没有界面、不提供审批（`approval.ask`），scratch 之外的写入和 Destructive 工具只会被拒绝。

不经测试、手工启动离线宿主（例如查问题时），先复制一份项目到 scratch，再：
```powershell
src\EplanMcp.Bridge.Offline\bin\Release\net481\EplanMcp.Bridge.Offline.exe --variant-bin 'C:\Program Files\EPLAN\Electric P8\2026.0.3\Bin' --project "<scratch 中的副本>.elk" --pipe-prefix EplanMcpManual. --idle-exit 300
```
`eplanmcp` 在 `EPLANMCP_PIPE_PREFIX=EplanMcpManual.` 下只找到它。按 Ctrl+C 或空闲 300 秒后，它停止管道、关闭项目、退出 EPLAN。退出码：0 正常，1 EPLAN 或 Bridge 失败（见日志），2 命令行错误。

## 写测试

- `ImportPlcTests` 经 `DestructiveHost` 对 `LargeWrite` 验证事务内重复保护：当前没有 PLC 总线导出工具，可把这份写副本的 `Phoenix1` 标准格式自导出文件路径放进 `EPLANMCP_LARGE_PLC_PBF`；未设置时查找 `%USERPROFILE%\EplanMcp\scratch\spikes\s34\**\export-00\roundtrip.pbf` 中最新的 S34 示例项目自导出文件，两者都没有才跳过。预检不完整仍经审批、备份执行，期望 `ALREADY_EXISTS/rolled_back`、保留备份、冲突名单含 `=F+A2-KE1`，前后功能/PLC 盒/部件引用数相同（S34 新副本为 2,876 / 20 / 2,900；其他写测试可改变基线）。
- PLC 无冲突目标的正向导入、新增对象之间同名的实际含义、提交后一次 Ctrl+Z 和备份恢复仍待 Large 验证；本测试不宣称覆盖这些发布门槛。

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

待补：按 eplan_cable_generate spec 第 7 节，由工程师准备能实际新增自动电缆的 scratch 夹具，再编写正向生成、更新与恢复测试；形成条件、完整选型/连接字段及提交后一次 Ctrl+Z 均未验证，本步不编写正向夹具测试。

### Action whitelist batch 2 (v0.4.0)

`ActionRunTests` 的三项晋升测试只写 `scratch\writes\LargeWrite`，通过 `PlanStepLiveSession` 按 A41/A46 选择当前项目并执行 `XEsUndoAction`。同步测试需要保留 S26 r3 的表示文本差异（请用新鲜示例副本）；断言提交有可观察文本变化，随后一次撤销恢复文本与业务快照、哨兵页仍存在，再一次撤销移除哨兵页。`SwitchProjectType` 测试会打开 `eval-fixture.ps1 -WriteCopies` 生成的 `scratch\writes\LargeWriteType`（名称排在 `LargeWrite` 之后，打开它不改变当前项目，A46），用来确认切换类型不影响其他打开的项目；它之后保持打开，P25 也把它当作已知副本。finally 必要时经产品工具恢复 LargeWrite 为 Schematic。2026-10-26 在 EPLAN 2026.0.3 上三项全部通过，结论见 api-notes A50。

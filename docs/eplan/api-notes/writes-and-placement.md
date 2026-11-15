# [EPLAN API 笔记](../api-notes.md)：写入、放置与模块

### A13. 写入 API 的实机验证（spike S9，2026-10-20，EPLAN 2026.0.3.25421，Electric P8，在 action 中）
示例项目的 scratch 副本。详细数据见 [spike S9](../../../spikes/S9-write-api/README.md)。
- 🔴 **按库符号新建功能不能在 `SafetyPoint` 中进行**：`new Function().Create(page, symbolVariant)` 及带区域的重载，在 `SafetyPoint` 中总是抛出 `LockingExceptionFailedLockAttempt`（"S063176"），锁不住的是新建的功能本身（`InvalidHandleException`）。预先锁定页、`Project.LockAllObjects()`、`UndoStep` 在内、在 `SafetyPoint` 中嵌套 `Transaction` 都没有用。不加包装或在 `Transaction` 中则成功；`Transaction` 不提交（`Abort()` 或直接释放）后，项目中没有这个功能。EPLAN 系统日志中没有记录。— S9 r2–r12。
- 🔴 **插入窗口宏只能用 `SafetyPoint` 回滚**：`Insert.WindowMacro` 在 `SafetyPoint` 中插入后 `Rollback()`，插入的 28 个对象全部失效；在 `Transaction` 中插入后 `Abort()`，对象全部**留在页上**。— S9 r10、r11。
  - 对本项目的影响：A7 中 S2 的结论（两种包装都能回滚）只对建页、改属性成立。S2 定下的 `LockingStep → SafetyPoint → UndoStep` 适用于 M3a 的工具和插入宏，**不适用于按符号放置设备**。M3b 要在"用宏放置"和"按符号放置时改用 `UndoStep → Transaction`、且同一次写入不插宏"之间选择。
- 在 `Transaction` 中新建的功能，提交之前 `DMObjectsFinder.GetFunctions` 找不到（带不带过滤器都一样）。— S9 r10、r11。
- `SafetyPoint` 的公开成员只有 `Create`、`Start`、`Commit`、`Rollback`、`GetAutoLockingState`、`Dispose`，**不能关闭自动锁定**。`Insert` 提供 `WindowMacro`、`SymbolMacro`、`PageMacro`、`PrePlanningMacro`。— 反射。
- **建页**：`Page.Create(project, DocumentType.Circuit, PagePropertyList)` 中，页名前缀对应 `=` → `DESIGNATION_PLANT`、`+` → `DESIGNATION_LOCATION`、`&` → `DESIGNATION_DOCTYPE`、`/` → `PAGE_COUNTER`。**项目页结构中没有的部分（如 `DESIGNATION_FUNCTIONALASSIGNMENT`）被静默丢掉**；与已有页重名时抛出 `NameConflictException`（"S063054"）。`Page.Name` 可以赋值完整页名，会被拆成各部分。— S9。
  - 对本项目的影响：建页后读回页名并与请求比较，不一致就回滚并报参数错误。
- **改设备标识**：只给主功能的 `Name` 赋值时，辅助功能（包括其他页上的交叉引用）保留旧标识并**脱离设备**；先读出 `SubFunctions`，再把主功能和全部辅助功能改成同一个标识，设备保持完整。**`Function.Name` 不检查重名**（同一页上可以有两个 `=GAA+A1-KF94` 主功能），并且会**丢掉该类别不使用的结构部分**（给电缆赋值 `=F+A2-KF19` 得到 `+A2-KF19`）。`SafetyPoint.Rollback()` 能恢复主功能和全部辅助功能的标识。— S9。
  - 对本项目的影响：改名必须连同辅助功能一起改；规划阶段自己检查新标识是否已占用；执行后读回比较。
- 按符号新建的功能 `VisibleName` 为空，页面上不显示标识（与 A3 一致）；宏插入的设备有显示标识（`-QA1`）。宏中的设备取目标页的结构（`=GAA+A1-QA1`），端子没有编号（`+A1-XG5:?`）。— S9。
- 窗口宏要选对表达类型：西门子 `3RT201x-1Bx41x.ema` 只有 `ArticlePlacement`，电路图页要用有 `MultiLine` 的宏（`WindowMacro.RepresentationTypes`）。在同一位置再插入同一个宏会抛出 `BaseException`（"S029181"，相同符号叠放）。— S9。
- **`Generate.Connections(new[] { page }, false)` 把页上画出的自动连线变成 `Connection` 对象**（宏：0 → 9 个；两个上下对齐的线圈之间：2 个），可以在 `SafetyPoint` 中执行。— S9。

### A14. 在 Bridge 调度器中写入（spike S10，2026-10-20，EPLAN 2026.0.3.25421，Electric P8）
探针在 action 返回之后经 `Dispatcher.Invoke` 在主线程上执行，与 Bridge 相同；写入的包装是 `SafetyPoint` → 其中的 `UndoStep` → `QuietModeStep`。详细数据见 [spike S10](../../../spikes/S10-dispatcher-writes/README.md)。
- **插入宏的编号方式**（`Insert.WindowMacro(…, NumerationMode)`）：
  - `None` 保留宏中的标识、结构跟随目标页。再次插入同一个宏时，项目中出现**两个同名的主功能**（`+A1-WG6`）；
  - `Number` 分配不冲突的新标识（`WG7`、`XG9`），端子编号为 `:?1`、`:?2`；没有主功能的触点得到带 `?` 的标识（`=GAA+A1-?QA1`）；
  - `NumberWithQuestionMark` 给所有标识加 `?`；
  - PLC 端子保留宏自带的结构（`=K1+A1:1.4`），四种方式都不改。
  - — S10。
- **在调度器中，插入宏、生成连接、按符号新建设备（`UndoStep` 包住 `Transaction`）各占一个撤销步骤**。S10 的副本是当前项目时，`XEsUndoAction` 只撤销最后那一步。— S10 r3。
  - ⚠️ 测试 Ctrl+Z 时，要先确认打开的项目全是 scratch 副本，并且要撤销的项目是当前项目：EPLAN 中开着其他项目时，撤销可能落到它上面。`Edit.SelectProjectInPagesNavigator(project)` 可以把副本选为当前项目。
- `SafetyPoint` 能回滚 `Generate.Connections`（页上 0 → 9 → 0 个连接）。对连接已经齐全的页，"只更新"和"重建全部"都不增删连接。— S10。
- **调度器中没有隐式锁定**：不加 `SafetyPoint` 时（例如按符号新建设备时只用 `Transaction`），写入前要自己锁定，否则抛出 `LockingException`（"写存取前没有加锁该对象"）。`SafetyPoint` 存在期间会自动锁定，插宏、生成连接、建页都不需要手动锁。— S10 r1、r2。
- **按符号新建的设备在图上不显示标识和连接点编号**：导出的 PNG 中只有符号本身。新功能有 12 个属性放置；`PropertyPlacementsSchemas.All` 有"默认""用户定义"两种，选择"默认"后也没有变化。宏插入的设备则显示标识。— S10 r3，`Export.GraphicPage`。
  - `GraphicPage(page, 目录, Format.PNG, 24, 2000, Compression.None, false)` 每页写一个文件，文件名是页名把 `/` 换成 `_`（`=GAA+A1&EFS1_72.png`），两页分别约 32 KB、44 KB。是否改动项目文件没有比较。— S10 r3 的原始结果。
- **不插入就能读出宏的内容**：`WindowMacro.Open(path, project)` 后，`Objects` 返回全部对象（示例宏 28 个，其中 19 个功能，带宏自己的占位结构，如 `+-XG8:?`）；还能读出 `Size`、`GetBoundingBox()`、`GetVariants(type)`。打开前后项目的功能数和 `.edb` 文件都不变。— S10。
- **页幅面**：`Page.Size` 对已有内容的页返回幅面（示例页 420 × 297.02 mm），对**新建的空页抛出 `InvalidOperationException`**（"S063136无法确定页尺寸"）。`Page.GetBoundingBox()` 是页上全部对象的外框。— S10。
- 用 `MoveKind.Absolute` 插入宏时，插入点对应宏外框的左上角（只观察了一次）。— S10 r3，导出的 PNG。
- **调度器中的读取不改写项目文件**：只找项目、或打开并读取宏，项目 `.edb` 中都没有文件变化，包括 `User.eod`。A11 中的改写来自在 action 中调用 DataModel API。— S10，对比文件的大小和修改时间。

### A17. M3a 写工具的实机验证（2026-10-20，EPLAN 2026.0.3.25421，Electric P8）
`eplan_page_create`、`eplan_page_update`、`eplan_device_update` 的 Release 构建，经 `eplanmcp` 调用，每个工具用一份新复制的示例项目（scratch 中），另有一份 scratch 之外的副本只用来验证拒绝。步骤和结果记在 PR #109、#114、#121 中。
- **Q1**：对 scratch 之外的副本建页返回 `POLICY_DENIED`、`not_started`，设计数据和审计都不变。— 实测。
- **Q3：一次写入就是一个撤销步骤**：四次建页（其中一次建 4 页）后按四次 Ctrl+Z，依次撤销四次写入；改页描述、改设备标识同样一一对应。— 实测。
- **Q2：事务中失败时整体回滚，并且不留空的撤销步骤**：把 `+A1-WE1`（类别 `BusBarDefText`，A24）改名为 `=F+A1-WE19` 时，EPLAN 把名称改成了 `+A1-WE19`（这类设备不用 `=`，A13），读回不一致，返回 `INVALID_ARGUMENT`、`rolled_back`；它和它的 6 个辅助功能都保持原名。随后的第一次 Ctrl+Z 撤销的是之前那次成功的改名，说明回滚掉的写入没有在撤销列表中留下步骤。— 实测。
- `Page.Create` 支持 `Circuit`、`CircuitSingleLine`、`Overview`、`Graphics` 四种类型；页描述中的 `;`、`@` 原样写入并读回（`a;b@c`）。— 实测。
- 只改一种语言的页描述时，其他语言保持不变；清除一页唯一的一种语言后，描述为空。— 实测。
- 在调度器中给主功能和全部辅助功能的 `Function.Name` 赋值：另一页上的辅助功能（交叉引用）跟着改名，部件引用和 6 个连接都不变。— 实测（`=F+A2-KF1` → `=F+A2-KF19`）。
- 写入只产生 `execution_started`、`execution_result` 两条审计记录；被拒绝的调用（策略、重名、找不到）不写审计。`eplanmcp audit verify` 通过。— 实测。
- 待查：对 EPLAN 中的当前项目（`M3_OUT`）调用任何工具（包括只读的 `eplan_page_list`）后，它的 `User.eod` / `User.eox` 都被改写；同时打开的非当前项目（`M3_PC`）上，连续三次读取都没有改写。A12 中当前项目（评测夹具）也没有被改写，原因不明。只涉及 EPLAN 按用户保存的数据，设计数据不变。— 实测。

### A18. 复制、备份、删页、重编号（spike S12，2026-10-20，EPLAN 2026.0.3.25421，Electric P8，在调度器中）
详细数据见 [spike S12](../../../spikes/S12-destructive-backup/README.md)。
- **`ProjectManager.CopyProject(源 .elk, 目标 .elk, CopyMode.Snapshot)` 可以复制打开着的项目**：项目保持打开，不加锁，示例项目（313 个文件、213 MB）0.6–0.7 秒；源项目只改写 `User.eod` / `User.eox`（同 A11）。副本可以直接打开，页数、功能数与源项目相同；在一次已提交的写入之后立即复制，副本包含这次写入。— S12。
- `CopyMode.All` 同样可用（0.8 秒），但会改写源项目的 `.lck` 锁文件。`Backup.Project(…, Backup.Type.MakeBackup, false)` 用时约 10 秒，得到一个 `.zw1`（70 MB），会改写源项目多个 `.eod` / `.eox` 和 `ProjectInfo.xml`；源项目仍在原位、仍打开，但一次运行中当前项目随之改变。— S12。
- **`Page.Remove()`、`Renumber.DeviceTags`、`Renumber.Pages` 都能被 `SafetyPoint` 回滚，提交后 Ctrl+Z 一步撤销**，在 `QuietModeStep(ShowNoDialogs)` 下都没有弹出对话框。— S12。
- **删页会留下没有主功能的辅助功能**：删除 `+A1-WE1`（类别 `BusBarDefText`，A24；S12 中称为电缆）等设备的主功能所在的页后，其他页上的辅助功能仍在。报表页不随之更新。— S12。
- **`Renumber.DeviceTags(IFunctionBase[], null, Renumber.Enums.DeviceTags.PostNumerate, 方案, 1, 1)` 只给设备标识中的 `?` 编号**：`-?XG1` → `-XG2`（跳过已占用的 `XG1`）；端子连接点的 `?1` 不变；`=GAA+A1-?QA1` 也没有被编号（原因未查）。方案传 `""` 等同于 `Standard`；名称不存在时抛 `InvalidScheme`，消息列出可用的方案名。这个重载的第 4 个参数是方案（异常中的参数名为 `strSchemePerIdentifier`）；XML 文档中 `Project` 重载的参数名与类型顺序不一致，不要照着它传参。— S12。
  - 对本项目的影响：`eplan_device_renumber` 执行后读回，报告仍带 `?` 的标识；`eplan_page_delete` 的变更清单列出会失去主功能的设备。

### A19. M3b 写工具的实机验证（2026-10-20，EPLAN 2026.0.3.25421，Electric P8）
`eplan_connection_generate` 和 `eplan_macro_insert` 的 Release 构建，经 `eplanmcp` 调用，每个工具用一份新复制的示例项目（scratch 中），EPLAN 中只打开它并让它成为当前项目。步骤和结果记在 PR #136、#138 中。
- **生成连接**：
  - 已齐全的页 `=GAA+A1&EFS1/1`（40 条）先规划后执行，40 → 40，没有 `PLAN_STALE`：`Page.AllPlacements` 的 `ToStringIdentifier()` 在两次调度之间稳定。34 张原理图页（1057 条连接）一次生成 0.8 秒，没有增删。— 实测。
  - 宏页上 0 → 9 条（同 S10），事务中、提交前新生成的连接能按 `Connection.Page` 读到，与随后 `eplan_connection_list` 的 9 条一致。一次 Ctrl+Z 撤销这一步，宏的设备仍在；再生成又是 9 条，再执行一次没有增删。— 实测。
  - **没有增删的生成不留撤销步骤**：对齐全的页执行两次后按一次 Ctrl+Z，撤销的是之前那次真正的生成。也就是说这样的生成没有改动数据（包括没有删掉再重建连接），工具按 ADR-0010 返回 `not_started`。— 实测。
- **插入窗口宏**（`Contactor_Feedback_Control.ema`，52 × 268 mm，变体 0–5）：
  - 新建的空页上 (40, 280) 用 `NumerationMode.Number` 插入 28 个对象、12 个标识：端子 `+A1-XG2`–`XG4`、电缆 `+A1-WG1`，没有线圈的触点为 `=GAA+A1-?QA1`，PLC 端子保留宏的 `=K1+A1:1.4`–`1.6`（同 S10）。一次 Ctrl+Z 撤销整个插入。— 实测。
  - 同一点再插入：EPLAN 拒绝叠放，在调度器中同样抛出异常，回滚后页上没有残留。— 实测。
  - 插入后页上有内容时，`Page.Size` 可读（420 × 297.02，X 为宽）；外框按 `[x, x+宽] × [y−高, y]` 计算，(40, 200) 超出页面而被拒绝。外框与图纸上实际位置是否一致，没有导出图片核对。— 实测。
  - 在已有内容的页上另一位置插入，按"插入后的功能减去插入前的"读回，只得到新插入的 12 个标识（`XG5`、`XG6`、`WG2`、`?QA2`，`XG3` 增加端子 `:?3`、`:?4`）。— 实测。
  - `keep`：项目中没有同名设备时插入成功（`+A1-WG6`、`=GAA+A1-QA1`）；在同一页另一位置再用 `keep` 插入时，第二根 `+A1-WG6` 使主功能变多，返回 `ALREADY_EXISTS` 并回滚，页上不变。— 实测。
  - 变体 2：插入成功，PLC 端子为 `=K1+A1:3.4`–`3.6`。— 实测。
- 两个工具在插件日志和 `EplLog.eldb` 中都没有留下错误（叠放被拒绝时插件日志有一条预期的警告）。— 实测。

### A20. M4 工具的实机验证（2026-10-20，EPLAN 2026.0.3.25421，Electric P8）
各工具链的 Release 构建，经 `eplanmcp` 调用，每次用一份新复制的示例项目（scratch 中），EPLAN 中只打开它并让它成为当前项目。Destructive 工具的审批由项目负责人在 EPLAN 对话框中点击。
- **检查**（`Check.VerifyProject` / `VerifyPages`）：
  - 整个项目：6 条警告、14 条提示，与 `eplan_check_list` 一致；一次 Ctrl+Z 回到 0 条。— 实测。
  - **再检查一次是追加，不是替换**：警告仍是 6 条，14 条汇总提示（编号 999，每个检查区域一条）每次都再加一遍（20 → 34）。所以 `eplan_check_run` 不是幂等的。— 实测。
  - **按页检查会删掉其他对象的消息**：检查 `=GAA+A1&EFS1/1`、`/2` 后，原来挂在 3D 连接上的 6 条警告没有了。— 实测。
  - 不存在的检查方案：`VerifyProject` 和 `VerifyPages` 都抛出带编号 S029123 的异常，消息列出全部检查方案；回滚后消息不变。— 实测。
- **报表**：更新全部报表新增 `+A2&EPA1/1.2`（同 A16），一次 Ctrl+Z 撤销；再更新不增删；只更新 `&AAB1/1` 不增删。按 `Page.ReportBlock` 不为 null 数出 221 张报表页，A16 按页类型数的是 204 张，两种口径不同。— 实测。
- **导出**：PDF 文件名就是给出的名称（没有 `.pdf.pdf`）；整个项目 322 页黑白 PDF 约 13 秒、6.1 MB；DXF 在给定目录下按"项目名\结构"建子目录；PNG、JPG 都可用。**图片导出也只改写 `User.eod` / `User.eox`**，设计数据不变。不存在的 PDF 方案时，候选只有 `EPLAN_default_value`。— 实测。
- **删页与备份**：`CopyProject(Snapshot)` 得到 314 个文件、213 MB 的完整副本；以只读方式打开副本数页数再关闭后，当前项目不变，副本也不留在打开的项目中。一次 Ctrl+Z 恢复被删的页。工程师拒绝时不执行，也不建备份目录。— 实测。
- **页重编号**：`/90`–`/92` 从 200、步长 10 得到 `/200`、`/210`、`/220`，一次 Ctrl+Z 撤销。新页号与已有的页重名时，`Renumber.Pages` 抛出带编号 S029167（"页名不明确"）的 `BaseException`，页名不变。— 实测。
- 🔴 **`Renumber.DeviceTags(…, PostNumerate, …)` 在某些输入上发生原生访问冲突，EPLAN 进程直接结束**（同 A15，无法捕获）：
  - 用 `NumerationMode.Number` 插入宏后，把这页的全部功能交给它：崩溃（三次）。只交端子 `+A1-XG2:?1` 这类连接点带 `?`、设备标识不带 `?` 的功能：崩溃。交 `=GAA+A1-?QA1`、电缆 `+A1-WG1`（主功能）和 PLC 端子：崩溃。只交 `=GAA+A1-?QA1`：正常。不带 `PostNumerate` 交全部功能：正常（没有改动）。S12 中设备标识都带 `?` 时也正常。
  - 结论：崩溃的输入里都有设备标识不带 `?` 的功能。**只把设备标识（`:` 之前）带 `?` 的功能交给 `PostNumerate`**，修复后的 `eplan_device_renumber` 在同样的页上不再崩溃。
  - `=GAA+A1-?QA1`（没有线圈的触点）`PostNumerate` 不编号（同 A18）；这样没有改动的编号不留撤销步骤，按一次 Ctrl+Z 撤销的是之前的插宏。— 实测。
- 被强制结束过的项目，之后用 `ProjectOpen … /Quiet` 打开失败（返回 false）；EPLAN 重启后可能先弹出恢复类对话框，挡住所有调用。— 实测。
- **进度与取消**（main 82332d5 的 Release 构建，`writes\LargeWrite`）：带 `progressToken` 调用 `eplan_report_update`，第 5 秒收到心跳 `Updating reports (5 s)`，第 7 秒返回结果。`eplan_check_run` 开始 1 秒后客户端发 `notifications/cancelled`：插件日志记下取消请求，2.3 秒后在下一个安全点停下并回滚，审计为 `rolled_back`、`CANCELLED`，被取消的请求没有回复（MCP 规定），检查消息没有增加。— 实测。
- 用 `eplan_page_create` 建的新页在规划时就能读到幅面，`eplan_macro_insert` 因此按幅面检查外框；S10 中由 spike 建的空页读不到 `Page.Size`。— 实测。
- 全部 35 个 Large 测试（只读、Q1/Q2/Q6、插宏、检查与报表）在夹具和 `writes\` 副本上通过。— 实测。
- **更新报表会新增检查消息**：在新页上插宏、生成连接、只检查这一页（5 个错误）之后更新全部报表，`eplan_check_list` 读到 25 个错误。所以要报告检查结果时，先更新报表、再检查。— Agent 评测实测。
- Agent 评测：有了 `eplan_page_renumber` 之后，"改页号"可以做到，模型会去调用它（Destructive，弹出审批对话框，无人值守时 120 秒后过期），所以退役了评测题 `refuse-page-rename`。— Agent 评测实测。
- **第二批 Agent 评测**（`evals/tasks-attended`，项目负责人在场，在 EPLAN 中审批三次，Claude Sonnet 5.5，main 105b16d 的 Release 构建）：删页并更新报表、新建 `/90`–`/92` 后改为 `/200`–`/220`、改名为 `?` 标识后编号，三题一次全部解出，各 3 次调用，共 0.22 美元。模型删页前先用 `dry_run` 看受影响的设备；三题都报告了备份的位置。— Agent 评测实测。
- **`PostNumerate` 不复用空出的号**：`=F+A2-KF1` 改名为 `=F+A2-?KF1` 后对它所在的页编号，得到 `=F+A2-KF3`（同页已有 `-KF2`），刚空出的 `-KF1` 没有用上。编号只改了这一个设备，`stillUnnumbered` 为空。— Agent 评测实测。

### A26. 不用窗口宏放置单个设备（spike S20，2026-10-21，EPLAN 2026.0.3.25421，Electric P8，在调度器中）
详细数据见 [spike S20](../../../spikes/S20-symbol-placement/README.md)。示例项目的 scratch 副本。
- 按库符号新建的功能已有 12 个属性放置，但显示的文字都是空的；`new PropertyPlacement().Create(function, Properties.Function.FUNC_DEVICETAG_FULLNAME)` 之后图上显示 `KF96`（没有 `-`，压在符号的插入点上），`UseLocalPropertyPlacements` 变为 true，`VisibleName` 仍为空。— S20。
- `DeviceService.CreateDevice(部件号, "1", page, 位置)`：放置部件自带的宏（主功能，图上带标识、技术参数、连接点和触点镜像），其余功能未放置；标识为 `=+-Q1`，不取页的结构、不编号。能被 `SafetyPoint` 回滚，提交后一次 Ctrl+Z 撤销。— S20。

### A33. 占位符对象与值集（spike S29，2026-10-21，EPLAN 2026.0.3.25421，Electric P8，在调度器中）
详细数据见 [spike S29](../../../spikes/S29-placeholders/README.md)。写入只在 scratch 副本上、在 `SafetyPoint` 中回滚。
- `WindowMacro.Open(path, project)` 后，对每种表达类型的每个变体 `ChangeCurrentVariant`，`Objects` 中的 `PlaceHolder` 能读出名称、值集（`NumberOfRecords`、`get_NameOfRecord(i)`）、变量（`VariableNames`）和值表（`get_Value(record, variable)`）。C# 中 `NameOfRecord`、`Value` 是带参数的属性，要调用访问器方法。扫描 `$(MD_MACROS)` 下 695 个宏、1,751 个变体用时 21 秒，72 个宏带占位符（38 个带值集，最多 8 个），项目页数不变。— S29。
- `DMObjectsFinder.GetPlaceholdersWithFilterScheme(null)` 列出项目中的占位符（示例项目 2 个，值集 `V`、`mA`）。— S29。
- 插入宏后 `ApplyRecord(name)` 不到 0.01 秒，`GetAllCurrentValues()` 随值集改变；`AddRecord(name)` 返回新值集的序号，`SetValue(record, variable, MultiLangString)` 写入的值能读回并应用。插入和应用都随 `SafetyPoint.Rollback()` 撤销。值以多语言字符串存储。提交后的持久性和 Ctrl+Z 未验证。— S29。
- `PlaceHolder.GetAllCurrentValues()` 在占位符没有锁定时抛 `LockingException`（写存取前没有加锁该对象），只读操作中不能调用；在 `SafetyPoint` 中可以读（自动锁定，A14）。— 2026-10-22，写副本上的 Large 测试。

### A37. 模块写回为宏、新建占位符、授权选项（spike S32，2026-10-21，EPLAN 2026.0.3.25421，Electric P8，在调度器中）
详细数据见 [spike S32](../../../spikes/S32-macro-create/README.md)。写入只在 scratch 副本上，在 `SafetyPoint` 中回滚。
- `WindowMacro.Create(路径, MultiLine, 0, page.AllPlacements, 描述)` 把一页的 201 个放置写成 `.ema`（220 KB），`PageMacro.Create(路径, [page], 描述)` 写成 `.emp`；重新打开和再插入时，占位符、值集、变量、引用都保留。写出的宏文件记下 `SourceProject="<来源项目的完整路径>"`（含本机用户名）。— S32。
- 新建参数化占位符：`new PlaceHolder().Create(page)`、`AddVariable`、`AddReference`、`SetPropertyEntry(对象, 属性, "<变量名>")`、`AddRecord`、`SetValue`，之后 `ApplyRecord` 把值写进被引用对象的属性。🔴 属性条目的值必须写成尖括号中的变量名，写 `V1` 会被当作字面文本。宏文件中占位符为 `O106`，属性条目为 `S115x1904`（`A1911` 属性号，`A1913` 值，如 `??_??@ <Task>;`）。— S32。
- `License.GetLicenseModules(ref Dictionary<LicenseOptions, bool>)` 一次读出全部 3,214 个授权选项；开发机（演示授权）2,683 个可用，5 个 Cogineer 选项都可用，但安装目录中没有 Cogineer 的程序集。`HasOption` 只读；`LockOption` 会占用授权直到 EPLAN 退出，不要调用。— S32。

### A45. 设备改名的连带变化（LP-9，2026-10-25，EPLAN 2026.0.3.25421，Electric P8，LargeWrite scratch 副本）

- `device.update` 把 `=F+A2-KF1` 改名后，同页数据随之改变：接在它各功能上的连接的 `start`/`end` 中 function 全名（type/oid/pin 不变）、其他设备功能的 20403 `FUNC_PLCSYMBOLICADDRESS_CALCULATED`（如 `=F+A2-KF1:A1` → `=F+A2-<新>:A1`）、被改名功能自身的可见名 `shown`。
- 可见名会省略与页结构相同的段：`=F+A2-KF1` 的 `shown` 是 `=F-KF1`，不是全名的后缀。
- 计划校验只把“旧全名精确替换为新全名”和“可见名仅末段设备标识替换（结构不变）”算作改名的派生变化，其他变化仍按范围分类。
— 经 Host 的 `eplan_workflow_plan`/`eplan_plan_apply` 实跑，读取改名前后 standard 快照比对。

### A51. 插入宏并应用占位符值集的提交、撤销与回滚（spike S36 第 1 部分，2026-10-27，EPLAN 2026.0.3.25421，Electric P8，只在 scratch 副本上）
详细数据见 [spike S36](../../../spikes/S36-module-commit/README.md)。

- 在一个 `SafetyPoint → UndoStep → QuietModeStep(ShowNoDialogs)` 中新建页、插入 `24VDC_Supply.ema`、新建占位符并 `ApplyRecord`，提交后关闭再打开项目，全部对象、占位符值表和属性实际值逐字节不变；整个过程没有对话框。— S36 dp/fp。
- 这样的提交正好是一个撤销步骤：当前项目一次 `XEsUndoAction` 恢复写入前的全部状态（含页和项目属性），之前独立提交的哨兵页仍在，再撤销一次才删除；`ApplyRecord` 之后回滚不留撤销步骤，也不留改变。— S36 du/fu/dr/fr。
- `ApplyRecord(name)` 同时写对象、页和项目属性；`ApplyRecord(name, false)` 只写对象属性，页属性 11011 和项目属性 10901[1000] 不变。— S36。
- 布尔（20093）、普通文本（20027）、部件编号（20100[1]）都按值集生效；写部件编号后功能的部件引用随之改为新部件（数量 1）。— S36。
- 🔴 多语言属性（20011）按整体替换：值集只给 en_US 时，原有的 de_DE 被清除。生成时值集必须含全部要保留的语言，或先读出再合并。— S36。
- 🔴 对同一对象反复取得并释放属性列表后，EPLAN 在之后的终结器（`Page.Dispose(Boolean)` → `StorableObject.!StorableObject()`，`NullReferenceException`）中崩溃（0xc0000005）；每个对象只取一次属性列表、读写完后释放一次（A36）。— S36 r1。
— 协调人经 spike S36 第 1 部分实测，原始日志在 `.local` 中，不提交。

### A56. 模块引用闭包与写回宏（spike S36 第 2 部分，2026-10-27，EPLAN 2026.0.3.25421，Electric P8，只在 scratch 副本上）
详细数据见 [spike S36](../../../spikes/S36-module-commit/README.md)。

- 插入的示例宏有 7 个占位符，绑定到路径文字、位置盒、电缆与连接定义点、端子、中断点和功能文本；**被绑定对象可能在其他页有配对**（中断点另一端、相关位置盒），闭包会跨页；有变量映射不到对象时闭包不完整，模块写入须拒绝。— S36 第 2 部分。
- 写回：新建占位符和值集 → `WindowMacro.Create` 存为新 `.ema` → 重新打开 → 再插入 → `ApplyRecord`，布尔、多语言（值集给全语言时保留全部）、部件引用、技术参数逐项一致，一次提交、一次撤销、失败回滚无空步、重开持久，约 18 秒。— S36 第 2 部分。
- 🔴 **宏文件写在项目事务之外**：回滚和撤销后 `.ema` 仍在磁盘上，工具须自行删除失败产生的文件或列为产物。— S36 第 2 部分。
- 一次失败中 `SafetyPoint.Rollback()` 抛出“不能重新恢复原有状态,因为其不再由系统管理”；回滚前须先记录原始异常，回滚失败按结果未知处理。— S36 第 2 部分。

### A82. 从部件创建与放置功能的探针依据（2026-11-02，EPLAN 2026.0.3，本机官方 XML 核对，未运行 EPLAN）
- `Eplan.EplApi.HEServicesu.xml` 的 `DeviceService.CreateDevice(Project, string partNumber, string partVariant, FunctionPropertyList, ref List<IFunctionBase>)` 将创建的 2D、3D 功能写入传入列表；最后一参的 ref 依据见 A70，探针传新建空列表。层级属性列表缺少代号及计数器时按项目编号设置生成名称；无匹配变体时默认选择第一个可用变体。实际返回类型、模板及放置状态、名称和部件关系由本次 Large 探针观察，不把 XML 的简略说明当作实机结论。
- `DataModelu.xml` 的 `Project.ParseNameAndProps(int functionCategory, string deviceName, UniversalPropertyList outputProps, UniversalPropertyList additionalProps)` 解析设备名称并填入两份属性列表；类别来自原生功能观察，不使用未记载的默认整数。`FUNC_FULLNAME`、`FUNC_IDENTDEVICETAG` 为只读计算属性，不能由它们存在 setter 就断言可用来给 `CreateDevice` 指定名称。
- `Eplan.EplApi.DataModelu.xml` 的 `Function.PlaceAt(Page, PointD, DocumentTypeManager.DocumentType, SymbolVariant)` 说明：瞬态功能模板会先实例化，已实例化功能则在项目中新建功能；放置模板会更新该对象的 `IsCoveredTemplate`，但先前取得的其他模板包装器不会同步更新。`FunctionBase.Name` 的异常说明包含瞬态对象，故模板字段读取失败须逐项保留，不能据此丢弃该部件的其他观察。
- 同一 XML 的 `Function.AddArticleReference(string partNr, string variant, uint count)` 返回新增的 `ArticleReference`，将系统部件库存入项目，只有部件已经存在于系统或项目库时才赋给功能；`ArticleReference.PartNr`、`VariantNr`、`Count` 可读。`Function.IsMainFunction` 文档说明只有主功能可有部件引用；实际赋值结果由探针记录，不自动更改主辅功能。
- `Eplan.EplApi.MasterDatau.xml` 的 `MDPartsManagement.OpenDatabase()` 打开已配置部件库，不自动升级旧格式；`MDPartsDatabase.GetPartVariants(string partNumber)` 返回该编号全部变体并按变体名排序。`MDPart` 继承的 `MDPartsDatabaseItem.Properties` 暴露部件属性；`ExistingIds` 给出已有属性，`MDPropertyDefinition.Name` 给出属性名。`ARTICLE_MACRO` 为 22010，XML 描述为图形宏（说明为 3D 宏）；不得仅凭字段名把任意宏用途视为已验证。探针只读取并关闭库，不导入、修改或升级。
- 功能观察使用 `Function.FunctionCategory`、`IsMainFunction`、`Pins`、`Placement.IsPlaced`、`SymbolReference.SymbolVariant`；后者允许 null。`PinBase.Index` 是逻辑或图形连接点索引，`Pin.Name` 是实际连接点代号（从 `FunctionDefinition` 取得时允许 null），`Pin.Designation` 是内部代号。指定属性为 20022/20023（功能连接点代号/描述，索引 1–1000）、20028/20029（连接点代号/描述，只读，索引 1–100）、20400（PLC 地址）、20402（手工符号地址）、20406（手工插头代号）、20011（功能文本）；只记录非空实际值，保留索引。
- `SymbolVariant.Parent` 返回所属 `Symbol`，其 `Name` 为符号名，`Properties.SYMB_NR`（16002）为符号在库内的编号；`SymbolVariant.SymbolLibraryName`、`VariantNr` 分别为库名与变体编号。上述值仅作原始观察，不推定任一部件预先关联了符号。
- `Graphics.PropertyPlacement.Parent` 与 `DisplayedProperty` 用于确认功能归属及属性 ID/索引。`GetDisplayString()` 的 XML 同时称返回 GUI 显示字符串、列出 `InvalidOperationException: Always thrown`，因此按请求调用并逐项记录返回值或异常，实际可用性待实机确认。读属性值期间继续遵守 A80 的所有者存活要求。
- 仅登记、不调用的端子入口（`HEServicesu.xml`）：`CreateTerminalDevice(Project, string fullDeviceTag, string partNumber, string partVariant, string numberingPattern, bool numberByLevels)`；另一个重载在最后 bool 前增加 `int newTerminalDevicePosition, int newTerminalSortCode`。XML 说明按部件功能模板及编号模式创建端子，第一个端子为主端子并接收部件；本次不验证这一行为。
- **实机结果**（2026-11-02 21:17–22:05，EPLAN 2026.0.3.25421，Electric P8，scratch 副本 LargeWrite 的新页，`PartsFirstSpikeTests` 三轮）：
  1. `CreateDevice(Project, …, ref list)`：SIE.6ES7131-4BD01-0AA0 得到 6 个持久的未放置 PLC 端子（`:1` 为主功能），符号为 `PLC_CBOX_LEFT_PLUG`/`PLC_CBOX_CON`，20023 依次为 DI0、DI2、DC24V、DI1、DI3、DC24V，没有宏。对其中两个调用 `PlaceAt(Circuit, PLC_CBOX)`，另建了已放置的同名功能（非主功能），原来的未放置功能仍在，即重复。按钮 SIE.3SB3400-0B（有宏）同样如此。
  2. `CreateDevice(part, variant, page, point)`：主功能 `:1` 放在插入点，使用模板符号 `PLC_CBOX_LEFT_PLUG`，带 20023，器件自动编号为 `-K1`、`-K2`；其余 5 个是持久的未放置功能，没有符号。主功能的 `FunctionTemplates` 返回 6 个模板，均已被覆盖，没有可以实例化的对象。
  3. 对这些未放置功能调用 `Function.Place(page, point, true, true, true)`：另建了已放置的同名功能，没有符号（页上显示红叉），原功能仍在。
  4. `CreateDeviceListItem` 加 `GetTemplatesFromDeviceList`：返回 6 个临时的未覆盖模板，器件标识为空（`=+`），没有符号，`IsMainFunction` 均为 false。
  5. 先放符号 `SSD`、再 `AddArticleReference(SIE.3SB3400-0B)`：部件引用写到了主功能上；部件的模板（`SSMNO`，引脚 `.3`/`.4`）仍是临时的未覆盖模板，没有生成未放置功能。连接点代号相同，但符号和功能定义不同，模板不被覆盖。
  - 结论：产品用 `CreateDevice(page)` 放主功能（与 EPLAN"插入设备"相同）；其余功能用 `PlaceAt` 加指定符号放置，然后在同一事务中删除原来的未放置功能。删除这一步待正式工具的 Large 测试验证。
- **改名与命名创建**（2026-11-02 23:40–2026-11-03 00:40，同上环境，`PartsFirstSpikeTests` 第 4–5 轮）：
  1. `CreateDevice(page)` 生成的未放置功能不能改名：事务内给 `Function.Name` 赋值能读到新名，提交后变回 `=+-K3:2` 等原名，并脱离已改名的主功能；对它们写 1100、1200、20013、20014 均被拒绝（S063113）。已放置的主功能写 1100 也被拒绝（高层代号来自所在页），但赋 `Name` 有效（A13）。
  2. `ParseNameAndProps` 加 `CreateDevice(Project, …, 名称属性, ref list)` 一次建出全部功能，名称、模板符号、20023 描述、主功能上的部件引用都正确（`=GAA+A1-KC…:1…:7`）。
  3. **在同一事务中对上述刚建出的 PLC 主功能调用 `PlaceAt`**：传入功能自带的 `SymbolVariant` 时，EPLAN 内部抛出 `NullReferenceException`（`Terminal.setHandle`）并回滚；改为传入事务前从符号库解析的变体（A52）时，**EPLAN 进程崩溃（0xc0000374，堆损坏）**。第 1 轮用其它符号（`PLC_CBOX`）放置同类功能时没有出现。产品不得使用这一路径。

### A85. 用 `NameService.RenameDevice` 给按部件建出的器件改名（2026-11-03，EPLAN 2026.0.3.25421，Electric P8）
依据：`Eplan.EplApi.HEServicesu.xml` 的 `NameService.RenameDevice(Function, FunctionBasePropertyList, bool bRenameCDPsAlso, bool bKeepDescribingProps)`
（"Rename all functions of device"）。实机：scratch 副本 LargeWrite 的新页，`PartsFirstSpikeTests` 的 `rename-device` 步骤，一次运行。
- 做法：同一事务中 `DeviceService.CreateDevice(part, variant, page, point)`，再用 `Project.ParseNameAndProps` 解析目标标识，把
  1100 `DESIGNATION_PLANT`、1200 `DESIGNATION_LOCATION`、20013 `FUNC_CODE`、20014 `FUNC_COUNTER` 写进一个新的 `FunctionBasePropertyList`
  （离线列表可写，A82 中拒绝写入的是功能对象本身），调用 `RenameDevice(主功能, 列表, false, true)`，返回 void，然后提交。— 实测
- 结果（SIE.6ES7131-4BD01-0AA0，6 个功能，其中 5 个未放置）：提交后 6 个功能全部改为 `=GAA+A1-KR…:1…:7`，未放置功能仍属该器件，
  旧标识下没有剩余功能；20022/20023（DI0、DI2、DC24V、DI1、DI3、DC24V）不变；主功能上的部件引用保留。单功能部件
  （SIE.3SB3400-0B 按钮）同样改名成功，部件引用保留。这解决了 A82 中逐个给 `Function.Name` 赋值时未放置功能还原、脱离器件的问题。— 实测
- 未放置功能在事务内的 ID 形如 `53/17/22197/1`，提交后变为新的持久 ID（`53/17/69886/0`）；提交后的读回按新标识查找，不能沿用事务内的 ID。— 实测
- **崩溃**：同一事务中 `CreateDevice` → `RenameDevice` → 对一个未放置的 PLC 功能 `PlaceAt(page, point, Circuit, IEC_symbol/PLC_CBOX 变体 0)`
  → 对原未放置功能 `Remove()`，EPLAN 进程崩溃（0xc0000374，堆损坏），没有写出结果，崩溃前系统日志无相关记录；未区分是 `PlaceAt` 还是 `Remove` 引起。
  产品不使用这条路径；非 PLC 部件的同一路径未测。— 实测

### A92. 第 6 轮工程数据写入的实测结果（2026-11-04，EPLAN 2026.0.3，Pro Panel，LargeWrite）
- `eplan_property_set` 的 B8 条目写入和读回通过：盒子 20161、20408，总览点 20400，原理图点 20011，一步撤销恢复（`PlcPropertyB8Tests`）。B13 的类型由此确认。
- 原生类型是 `PLC`、`Terminal`、`BusTerminal` 的对象同样用 `FunctionPropertyList` 读写，按 Function 的属性字典处理。
- 写前快照如果覆盖全项目的 PLC 属性，在 323 页的项目上超过 30 秒。防误改检查的范围收窄到被写入的那张卡。
- `interruption_point.symbol_variant` 能选 BP 变体 0（`InterruptionExplicitVariantTests`）；`AddArticleReference` 可以加到已有的非主端子上（`ExistingNonMainTerminal_AssignsPart_ReadsArticleReferences_OneUndoRestores`）；`Page.GridSize` 单位是毫米，新建 Overview 页写 4 后读回为 4（`PageGridWriteTests`）；电位定义点 33003、31006、31004、31002 可写（`PotentialEngineeringTests`）。以上都能一步撤销。

### A93. 部件数据要靠 `DeviceService.UpdateDevice` 写入（2026-11-04，EPLAN 2026.0.3，LargeWrite 探针）
- `AddArticleReference` 只写引用和零件自身的字段（20100、20101、20190、20200、20919 等），功能定义和技术数据不动。官方 XML：`UpdateDevice` 接受设备的主功能或连接，把部件数据写到主功能，函数模板写到设备的所有功能。
- 探针结果，写入的值都和样本一致：端子 `PXC.3211813` 的 20188 由 `100/6/0` 变成 `100/6/1`，20057 变成 6；`PXC.3036563` 的 20230 变成 4；`SIE.3VA1032-2ED32-0AA0` 的 20027 变成 `In = 32A`，20022 变成连接点 1 到 6。
- 连接定义点要更新两次，都返回 true：`UpdateDevice(connection)` 把 31048、31059 写到连接和点上；`UpdateDevice(point)` 把点的 20188 由 `4/1/1` 变成 `4/1/4`，20026、20117 随之改变。之后重新生成连接，这些值仍在。
- 连接自身的 20188 由连线情况决定（同一端子排的相邻端子是 `4/1/7` 鞍形跳线），和点的 20188 互不影响。
- 点的 20188、20121 可以按属性写入，读回和重新生成后不变；20121 写 0 后 19021 变成“未定义”，20470 变成 0。
- `Function.Create(page, FunctionDefinition)` 建出的对象即使再 `SetSymbolVariant` 成 CDP，类型仍是 `Function`，不是 `ConnectionDefinitionPoint`，不能用来建定义点。
- 撤销取决于作用域：`SafetyPoint` 提交之后再调用 `UpdateDevice`，`XEsUndoAction` 报 S328050（步骤不可用）并弹对话框，部件已在项目里时也一样；放在 `FunctionTransactionScope(undoCommitted: true)` 的撤销步骤里，一步撤销能还原（`ExistingTerminalConnection_DefinitionPointOnly_PreservesEndpoints_OneUndoRestores`，带部件）。
- 第二个部件引用只有在 `SafetyPoint` 作用域里能加上。普通 `Transaction` 或 `FunctionTransactionScope` 里，第一个能加，第二个抛 S063074（未能添加部件），有没有先 `UpdateDevice` 都一样（端子 `PXC.3211813` 加 `PXC.3022218`）。所以 `eplan_device_update` 的 `part` 走 `SafetyPoint`，`update_from_parts` 单独一次调用，走 `FunctionTransactionScope`。
- 🔴 连接和定义点上不要调用 `UpdateDevice`。探针和回归测试里它都正常，一步撤销也能还原；但 16 页重画中，更新完 30 个定义点后，同一页插入电位定义宏时 `Insert.WindowMacro` 抛 AccessViolationException，EPLAN 终止（0xe0434352，2026-11-04 14:47）。同样的流程不调用这两次更新（第 12 次）全程没有崩溃。只出现过一次，原因没有查清。工具改为明确写 20188、20121、31048。
- 🔴 同一个包装对象不能取两次 `Properties` 再各释放一次。第二次取到的是已释放的列表，读出别的对象的值（定义点读回 `100/6/1`）或抛 S063109，之后 EPLAN 崩溃（0xc0000374、0xc0000005，位置不固定）。放进同一个 `using` 只读一次后不再出现。
- 🔴 `UpdateDevice` 之后，它之前取到的包装对象已经失效：继续用会读到别的对象（2000 由 `53/59/69844/0` 变成 `53/4/26/0`），并可能让 EPLAN 崩溃（0xc0000374，探针第一次，2026-11-04 12:12）。每个事务都要重新取对象。

### A95. 部件优先路线与名称/数量补充（2026-11-04，负责人 LargeWrite 探针，EPLAN 2026.0.3）
- `DeviceService.CreateDevice` 为 PXC.3211813、3212140、3036563、3211822 建出非主 Terminal，模板 X2_2/0、20188 为 100/6/1 或 100/6/3；PXC.3211819 为 X/0、100/6/2。默认名称 `+-X1` 可在同一事务改为 `+A1-XQ10:1`，`NameService(page).AdjustVisibleName` 返回 true，显示名为 `-XQ10`。这些是负责人已实测的行为；工具集成及主功能替换 FAH1、Q_3LI/6、LSW2C 仍待本轮 Large。
- `RIT.3581000` 的 CreateDevice 抛 S063114；RIT.3450500、RIT.3451500 未放出任何对象。BCP 在首次提交后新事务中可追加 RIT.3451500/1；同名 BBDEF 保持主功能，BCP 为非主 Busbar。
- 中断点仅赋 Name 时 VisibleName、20002、20010 为空；AdjustVisibleName 返回 true 后 `-2Z3` 可读。示例 `=GAA-2L1` 的显示名为 `-2L1`。
- 主功能在 SafetyPoint 内追加第二个部件 `AddArticleReference("RIT.9340030", "1", 2)` 读回数量 2；普通 Transaction 或 FunctionTransactionScope 会对第二个部件抛 S063074。本条补充 A93 的已知条件。
- 非主 Terminal 的 20367、20809 为 String，20035 为 Long，均已写入读回；未附部件的端子写 20054、20044 抛 S063109，不列入白名单。BCP 的 20011 写 PE 读回 `??_??@PE;`。

### A98. 部件的未放置功能用 `Function.Assign` 放到符号上（2026-11-04，EPLAN 2026.0.3，LargeWrite 探针和 Large）
- 部件路线放置后，线圈已放置，其余功能未放置（`IsPlaced` false，读 `SymbolVariant` 抛 NullReferenceException），`Pins` 已有名称。
- 三个入口实测：`Function.Place` 放出红叉，原功能保留；`Function.PlaceAt` 符号正常，但原功能保留、标识文字重叠；**`unplaced.Assign(target)`** 让已放置的符号得到模板的引脚名，未放置功能消失，标识只显示一次，触点映像干净。`Assign` 要在符号提交后的下一个事务里调用。
- 先放符号、后分配部件时，`AddArticleReference` 不产生未放置功能对象，`eplan_device_get` 只列出已放置的功能。但线圈的触点映像会多出部件模板里没有匹配上的触点行：模板的主触点是主触点符号，普通常开 `S` 匹配不上，`13/14` 能匹配。所以这个顺序不推荐，应先放部件，再用 `join_device` 放其余符号。
— Large `DevicePlaceTests.UnplacedAssignment`、探针 `FunctionPlaceSpikeTests`（未合入）。
- `DimensionItem.CreateAngular` 之后同一事务读 `SecondPoint` 得到 (0,0)，EPLAN 计算的角度文字正常；角度标注不用这个读回做核对。

### A99. 提交后读二维码内容或尺寸文字会让撤销失败（2026-11-04，EPLAN 2026.0.3，Electric P8，LargeWrite 探针和 Large）
- 提交后读 `QRCode.GetQRCode()`、`DimensionItem.DimensionText` 或 `DimensionCircle.DimensionText`，之后 `XEsUndoAction` 弹“撤销冲突，步骤不存在”，对象留在页面上。
- 同样在提交后读标识符、位置、属性 ID、画笔、边界、分组不受影响；二维码内容改读属性 `QRCODE_BLOCK_VALUE` 也不受影响。
- 所以 `eplan_annotation_create` 只在创建事务内读尺寸文字，`eplan_page_get` 不返回 `dimension_text`。
— Large `AnnotationCreateTests`、探针 `AnnotationUndoProbeTests`（未合入）。

### A117. 占位符引用无效属性时读不出当前值（2026-11-07，EPLAN 2026.0.3.25421，Electric P8，官方 EES 码垛系统示例副本）
- `PlaceHolder.GetAllCurrentValues()` 对引用了无效属性的占位符抛 `PropertyNotFoundException`（`S063109属性无效`），不是 A33 里的 `LockingException`。`eplan_placeholder_list` 的 `detailed` 原来因此整体报 INTERNAL；工具改为把这样的占位符和未锁定的一样标记为取不到当前值，值集和变量照常返回（2026-11-07 实机复验：码垛示例 7 个占位符，3 个取不到当前值，列表正常返回）。`eplan_placeholder_apply` 现对这种占位符照常应用值集，省略当前值及其变化标记并给出提示（2026-11-07 实机复验：码垛示例 `=GAB+S1&EFS/1` 的 `#<EES_COM>` 套用 `DC O.K.` 已提交，结果带提示）。

### A120. 宏里的占位符与插入时套用值集（2026-11-07，EPLAN 2026.0.3.25421，Electric P8，从公司基本项目建的 scratch 项目 EES1）
- `eplan_macro_get` 不插入就读出占位符和值集：P8 数据目录的官方宏库 `EPLAN_Macro` 136 个窗口宏中 36 个带值集。例如 `203_Electrical_Engine/001_Option_1/Contactor_Feedback_Control.ema` 的 `#<Function_text>` 有 8 个值集（Forward control、Reverse control、Feed Forward……），变量 `DI_Drive_Control`（功能文字）和 `DT`（设备代号）。
- `eplan_macro_insert` 带 `values: {"#<Function_text>": "Reverse control"}`：插入和 `ApplyRecord` 在同一事务中提交，读回的当前值正好是该值集的值（`驱动装置"给料,向后"`、`{{QA2}}`）；写错的值集在预览时就被拒绝，并列出可选的值集。`Insert.WindowMacro` 返回的放置对象里包含插入的 `PlaceHolder`。

### A131. “不在部件清单中输出”是带索引的属性（2026-11-10，EPLAN 2026.0.3.25421，Pro Panel，LargeWrite）
- **20105 `FUNC_ARTICLE_SUPPRESSINPARTSLIST` 的运行时定义：** 布尔型、带索引（`IsIndexed` 为 true）、非内部、非只读、非名称组成，计算标记为空。PLC 盒 `=F+A2-KE1` 上没有值时读回 `empty`、没有类型。推测每个部件引用各有一个值，没挂部件的设备该写哪个索引、写后是否从部件清单消失，都还没实测。
- **起因：** 71 页复刻第 15 页网络总览里的交换机 `+S1-K8` 是主功能、没挂部件，EPLAN 仍在部件清单里列出它，原图没有这一行，后面各行因此错开一行。`eplan_property_set` 只写不带索引的属性，规划时以“运行时定义与登记不一致”拒绝；拒绝消息现在列出运行时的类型和各标志。支持带索引写入的分支 `feat/suppress-in-parts-list` 暂停。

### A132. 先放触点、后加部件不会生成模板功能（2026-11-10，EPLAN 2026.0.3.25421，Pro Panel，LargeWrite）
- **步骤：** 线圈 `-KPA5` 不带部件放置，再以同一标识放 4 个常开触点（引脚 `1/L1 2/T1`、`3/L2 4/T2`、`5/L3 6/T3`、`13 14`），然后 `eplan_device_update` 追加 `SIE.3RT2015-1BB41-1AA0`，再 `update_from_parts`。
- **结果：** 两步之后设备仍只有这 4 个已放置触点，没有未放置功能，引脚不变；两步的 `assignedFunctions` 都是 0。`AddArticleReference` 不生成模板功能，`DeviceService.UpdateDevice` 在已有匹配触点时也不生成。两次撤销恢复到放置后的快照。
- **对照：** 带部件放置设备（`CreateDevice`）会按模板生成 4 个未放置触点，`join_device` 放置时逐个分配（`DevicePlaceTests.JoinDevice_AssignsFourUnplacedContacts_OneUndoRestores`，同日通过）。

### B12. 放置编辑成员（2026-10-31 离线核对；2026-11-01 实机日志复验）
`eplan_api_search/get` 核对 Placement.Remove、CopyTo(Group)、Function.RemoveFromPage、Page.AllPlacements、Edit.Rotate(Placement[],PointD,double)、Mirror(Placement[],PointD,PointD,bool)；Rotate、TextBase.Rotation、Rectangle.Angle 均用弧度，文本 -16002 表示继承图层角度（工具拒绝该文字的旋转/镜像）。`PlacementEditSpikeTests` 与测试原生 helper 检查删除功能/连接、复制标识、旋转支点、镜像文字和单步撤销；行为结论以下述实机日志为准，离线编译不算实机验证。

- 前一轮探针的两项测试修复见提交 `66a2cf35`：`eplan_object_find` 的公开字段为 `items`；属性文字初始化须先设 `UseLocalPropertyPlacements=true`，再重新取得本地属性放置。随 2026.0.3 提供的 `Eplan.EplApi.DataModelu.xml` 的 `Graphics.PropertyPlacement` 类型说明指出，符号变体的属性放置来自只读符号库，直接修改会抛 `BaseException`。以下复验日志已替换前一轮同名文件，不能再用当前文件的行号引用旧失败。
- 2026-11-01，EPLAN 2026.0.3，Electric P8，LargeWrite scratch，串行、单实例：复验日志（`../../.local/failures/q-edit-PlacementEditSpikeTests.log`，本工作区未提供该日志） 共 15 项、8 失败、7 成功。原生探针已越过 seed，以下为其 `before/during/afterCommit/after` 的实际读数，不能概括为整套测试通过。本次修复未启动 EPLAN，仍须实机复验撤销链。
- **轴对齐矩形旋转/镜像后会重新排序角点，`Location` 为新外框左下角，`Angle` 仍为 0**。原生 `(220,200)..(230,210)` 绕自身旋转 90°，读回 `(210,200)..(220,210)`、位置 `(210,200)`；以 x=210 镜像为 `(190,200)..(200,210)`，以 y=150 镜像为 `(220,90)..(230,100)`。产品测试第 2、40 行的两项 rolled_back 来自把矩形位置当固定锚点、角度当作累加值；已修正 planner，并用产品原始三矩形布局 `(40,200)..(50,210)`、`(100,180)..(110,190)`、`(180,160)..(190,170)` 作 Small 读回回归，保留位置/外框/角度不符即回滚的检查。— 原生 rotate 报告第 60623 行起，mirror 第 181850 行起，horizontal 第 409141 行起。
- **零角度自由文字镜像后 `Rotation` 仍为 0，外框按镜像改变**。`Mirror note` 在 `(100,200)`，原框 `(100,200)..(128.93,205.99025)`；以 x=210 镜像后位置 `(320,200)`、框 `(291.07,200)..(320,205.99025)`；以 y=150 镜像后位置 `(100,100)`、框 `(100,94.00975)..(128.93,100)`。绕自身旋转 90° 时自由文字角度为 π/2，符号的属性文字角度仍为 0，部分属性相对偏移随符号旋转；可见标识的外框保持横排，不能只旋转原文本外框。产品修正零角度自由文字镜像预测，Small 逐项复现上述读数；原生测试按种类精确检查矩形、自由文字及属性文字角度。— 同上及 mirror rollback 报告第 303077 行起。
- **符号编辑后的引脚取实际变体几何，并非原引脚坐标的刚体变换**。本探针 SPECIAL/PCP 起始偏移 `(0,3)`、Up，旋转 90° 后为 `(-2,0)`、Left（页上 x=338，旧断言误算 337）；水平镜像后为 `(0,-4)`、Down（页上 y=96，旧断言误算 97）。IEC_symbol/K 水平镜像后两点偏移为 `(0,-4)`、Down 与 `(0,3.99)`、Up。探针针对这些固定种子逐点检查实际变体坐标与方向，仍使用原 6 位小数断言；不扩大容差，不把变体差异推广到其他符号。产品仍须拒绝与批准预测不符的实际引脚/子项读回。— rotate 第 60623、530368 行起，horizontal 第 409141 行起。
- **属性放置的局部偏移也并非每项都作刚体变换**：两个 K 分别有 13 项（带新增标识）与 12 项；首项、两项 `(-0.6,±1.4)` 和新增标识 `(8,8)` 的偏移随旋转/镜像改变，其余项如 `(-6,-3)`、`(-6,-3.84)`、`(0.526,-1.513)`、`(0.5,0)` 保持原偏移。PCP 两项原为 `(0,-1)`，只首项变换，第二项保持原偏移。探针按这组已记录的槽位精确比较全部子项的位置、文本、零角度及数量，保留回滚前后全量相等断言；这不是其他符号或其他属性方案的通用规则。— 同上五份原生失败报告的 children。
- 原生 rotate、mirror 的 rollback 报告中，`after` 与 `before` 的全部对象、功能、连接完全一致；提交报告的 `during` 与 `afterCommit` 一致。提交用例在几何断言处已失败，**尚不能从这些失败用例证明后续一次撤销或显式支点检查成功**。— rotate rollback 第 530368 行起；mirror rollback 第 303077 行起。
- 产品 delete 的第 21 行是 `APPROVAL_DENIED/not_started`：测试没有给 `ScriptedApprovalChannel` 登记本次删除的批准；已用公开预览的 `predicted` 定位符、位置、角度生成精确批准行，并检查返回备份。拒绝行为不需修改产品策略。该份 q 日志没有关联 ID、`api_try` 或验收夹具失败；当天 `bridge-slotA.log` 含其他分支的历史异常，不能把它们当作本次编辑失败的堆栈。

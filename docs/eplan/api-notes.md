# EPLAN API 笔记（已验证的事实）

> 本文件只收录**有出处的事实**：官方文档链接、反射得到的签名，或实机验证记录（日期 + 环境）。
> 没有验证过的内容不写进来；写代码时遇到不确定的 EPLAN 行为，先验证，再补到这里。
> 每条格式：**事实** — 出处 / 验证方式。

---

## A. 实机验证记录（2026-10-12，EPLAN 2026.0.3，Pro Panel 变体，Windows 11）

### A1. 环境与进程
- **从桌面启动的 EPLAN 会继承 explorer 的环境变量。** 用户 PATH 原始长度 4604 字符时，Windows 登录过程没有加载 `HKCU\Environment` 下的任何变量，explorer 和 EPLAN 的 `TEMP=TMP=C:\WINDOWS\TEMP`；把 PATH 缩到 316 字符并广播 `WM_SETTINGCHANGE("Environment")` 后，explorer 立即恢复正常，不需要重新登录。— 读取进程 PEB 中的环境块验证。
- **`TEMP=C:\WINDOWS\TEMP` 时，EPLAN 的 C# 脚本全部无法编译**，系统消息为 `CS2001 未能找到源文件 C:\WINDOWS\TEMP\xxxx.0.cs` / `CS2008`。用 Windows PowerShell 5.1 的 `Add-Type`（同为 .NET Framework CodeDOM）可以在 EPLAN 之外复现；TEMP 改为用户目录后正常。— 实机复现。
  - 对本项目的影响：`doctor` 自检必须检查 EPLAN 进程的 TEMP 和用户 PATH 长度；安装程序**绝不修改 PATH**。
- **远程调用 `ExecuteScript` 时，即使脚本编译失败，Remote Client 仍然返回成功**（约 0.05 秒返回）。失败只记录在 EPLAN 系统消息里。— 实机验证。
  - 对本项目的影响：不能把 Remote Client 的返回值当作操作成功的依据。
- **EPLAN 系统日志**在 `C:\Users\Public\EPLAN\<变体>\EplLog.eldb`。它是 SQLite 数据库，文本为 UTF-8，消息在表 `TSysMsg_v100`（字段包括 `Time`、`ErrorLevel`、`Module`、`Description`），会话在表 `TSysSession_v100`。— 只读打开数据库验证。
- **Remote Client 连接**：`EplanRemoteClient.GetActiveEplanServersOnLocalMachine` 返回空列表，但直接连接 `localhost:49152` 成功。正在运行的实例登记在 `HKCU\Software\EPLAN\RemoteServer\<端口>`（`ProcessID`、`EplanFullPath`、`EplanProductVariant`）。— 实机验证。
  - 2026-10-20（Electric P8）：EPLAN 的 49152 端口**只监听各网卡地址，没有监听 127.0.0.1**，连接 `localhost` 被拒绝，改用本机网卡地址后成功。端口因此对局域网开放，而 Remote Client 没有 TLS（B4）。— `netstat`，spike S9。

### A2. 授权（`Eplan.EplApi.ApplicationFramework.License`）
- `new License()` 之后，`GetLicenseModules(ref Dictionary<LicenseOptions,bool>)` 返回全部授权选项及其可用状态；`HasOption(LicenseOptions)` 只做检查，不占用授权。不要调用 `LockOption`，它会占用一个授权直到 EPLAN 退出。— 实机调用，见 [API 文档](https://eplan.help/en-us/Infoportal/Content/api/2025/Eplan.EplApi.AFu~Eplan.EplApi.ApplicationFramework.License~GetLicenseModules.html)。
- 开发机授权：类型 `Borrowed`；3214 个选项中 2683 个可用；包括 `APIExtension`、`APIExtensionPartsmanagement`、`UsingUserScripts`、`CommandLineActions`、全部 2000 个 `APIRuntimeApplication####`、`ElectricP8`、`ProPanelProfessional`。— 实机调用。
  - 对本项目的影响：Bridge 启动时检查 `APIExtension`，缺失时给出明确错误，而不是在后续调用中莫名失败。借用授权有到期时间，授权丢失要能被识别为一类单独的错误。

### A3. DataModel 行为（通过脚本在 scratch 项目上验证）
- **`Function.Create(Page, SymbolVariant, PointD, PointD)` 创建的功能没有名称**（名称为 `=+`），需要另外赋值 `Function.Name` 才有设备标识。赋值后名称按项目结构格式化，例如传入 `-K1` 得到 `=+-K1`。
- **通过 API 设置的设备标识，在图纸上不显示标识文字**（属性文本没有自动放置）。— 导出 PNG 观察。待查：需要调用哪个 API 才能生成属性放置。
- **两个引脚在同一轴线上相对放置时，EPLAN 会画出自动连线，但不会立刻生成逻辑 `Connection` 对象**；执行"生成连接"（`generate` 动作，TYPE=CONNECTIONS）之后才出现。— 读取 `Project.GetConnections` 前后对比。
- 新建的电路页（示例项目）栅格为 4 mm，幅面 420 × 297 mm。坐标单位为毫米，原点在左下角（y 向上增大）。— 实机读取。
- `StorableObject.ToStringIdentifier()` 形如 `33/17/69835/0`，**只在当前 EPLAN 会话内有效**，不能作为持久标识。
- 示例项目 `EPLAN_Sample_Project`：321 页、2876 个功能，按名称读取全部页面约 0.2 秒。

### A5. 未签名插件与授权（spike S0，2026-10-13，Electric P8 变体）
- **未签名、无强名称的 net481 插件可以注册、延迟加载并执行**：先用 `EplApiModuleAction /register:"<dll>"` 注册（`bLoadOnStart = false`），第一次调用它的 action 时加载。实际加载位置是影子副本 `%APPDATA%\EPLAN\ShadowCopyAssemblies\<pid>\<程序集名>\bin\`。— [spike S0](../../spikes/S0-licence/README.md)。
- 在 321 页的项目上，插件读取页面、读取功能、建页面、删页面全部成功，**没有出现 5 页的限制**；EPLAN 系统日志中没有授权或签名相关的警告。授权：`Borrowed`，`APIExtension = true`。— spike S0。
  - 局限：只验证了这一份授权。其他授权类型的行为未知（见 ADR-0007）。
- 通过 Remote Client 执行 `EplApiModuleAction /register` 和 `/unregister:"<程序集标题>"`（在静默模式下）都返回成功，没有弹出对话框。— spike S0。
- action 参数名在 EPLAN 内部被转为小写（日志显示 `/report:…`），`ActionCallingContext.GetParameter("Report", …)` 照样能取到值，**参数名不区分大小写**。— spike S0。
- 插件 action 中未处理的异常**不会导致 EPLAN 崩溃**：EPLAN 记录完整的堆栈，并报告"操作 … 失败"；Remote Client 的调用仍然返回成功。— spike S0 第一次运行。
- 以下 EPLAN 类型实现了 `IDisposable`（分析器 CA2000 提示）：`DMObjectsFinder`、`UndoManager`、`PagePropertyList`、`Page`。— 编译 spike S0。

### A7. 回滚与撤销（spike S2，2026-10-13，均在插件 action 中执行）
- `SafetyPoint` 和 `Transaction` 的各种放弃方式（不提交释放、`Rollback()`、`Abort()`、`Start()` 后释放、异常）都能撤回新建的页面，也能恢复被修改的页面属性。— [spike S2](../../spikes/S2-transactions/README.md)。
  - ⚠️ 不能推广到所有写操作：插入宏只有 `SafetyPoint` 能撤回，按符号新建功能只能在 `Transaction` 中进行，见 A13。
- **`UndoStep` 在 `SafetyPoint` 外面创建时，`SafetyPoint` 无法回滚**，包括显式调用 `Rollback()`。`UndoStep` 在 `SafetyPoint` 里面创建，或者 `UndoStep` 包住 `Transaction` 时，回滚正常。— spike S2。
- `UndoStep.DoUndo()` 只在 `CloseUndo()` **之前**调用才有效；在 `CloseUndo()` 或 `CloseOpenUndo()` 之后调用，以及 `DoUndo(true)`，都没有效果。— spike S2。
- action 结束后执行 `XEsUndoAction`（GUI 的撤销，即 Ctrl+Z），会撤销带有已关闭 `UndoStep` 的 AI 修改。每次 action 中的修改各占一个撤销步骤；**此前的撤销历史保持完整**。— spike S2。
- 在 action 中，**没有 UndoStep 的修改同样可以用 Ctrl+Z 撤销，撤销历史也没有被清空**，与 XML 文档（B7）的说法不符。在 action 之外的行为待 S1 验证。— spike S2。
- 在 action 中，`TransactionManager.IsEplanTransactionRunning` 为 false；创建 `Transaction` 后变为 true。`SafetyPoint.GetAutoLockingState()` 在 SafetyPoint 存在期间为 true。— spike S2。
- 对空的 `PropertyValue` 调用 `ToString()` 会抛出 `EmptyPropertyException`（例如新页面的页描述 `PAGE_NOMINATIOMN`），读取前要先检查 `IsEmpty`。多语言属性的原始字符串格式为 `<语言>@<文本>;`，语言无关的文本为 `??_??@…;`。— spike S2。

### A9. 从后台线程调度到 EPLAN 主线程（spike S1，2026-10-13）
- **EPLAN 已经预先设置了主线程调度器**：在主线程上，`EplanMainThreadDispatcher.GetMainThreadDispatcher()` 不为 null，并且与 `Dispatcher.CurrentDispatcher` 是同一个对象。主线程为 STA，托管线程 ID 为 1。`GetMainThreadDispatcher`、`SetMainThreadDispatcher`、`CanAccessMainThread` 都是**静态方法**；`ExecuteInMainThreadSync/Async` 是实例方法。— [spike S1](../../spikes/S1-dispatcher/README.md)。
- 从后台线程调用 `EplanMainThreadDispatcher.ExecuteInMainThreadSync` 或 WPF 的 `Dispatcher.Invoke`，委托都在主线程上执行；中位延迟约 0.3–0.4 ms（各 20 次）。`CanAccessMainThread()` 在主线程上为 true，在后台线程上为 false。— spike S1。
- **在 action 之外，即使只是读取（例如 `project.Pages`），也必须先创建 `LockingStep`**，否则抛出 `NoLockingStepException`。— spike S1。
- `Dispatcher.Invoke(DispatcherPriority, TimeSpan timeout, Delegate)`：主线程忙时，在超时（1 s）后返回 null，**委托之后也不会再被执行**（等了 2 s 确认）。`ExecuteInMainThreadSync` **没有超时**，会一直等到主线程空闲。— spike S1。
  - 对本项目的影响：Bridge 使用 EPLAN 预设的 WPF `Dispatcher` 加超时，来实现 `EPLAN_BUSY`。
  - 自 ADR-0009 起，插件在 `OnInitGui` 中直接取 `Dispatcher.CurrentDispatcher`，主线程断言用它的 `CheckAccess()`，不再使用 `EplanMainThreadDispatcher`（2024 及更早的版本没有这个类型）。2026 上两者是同一个对象，行为不变。
- `Send`、`Normal`、`Background`、`ContextIdle`、`ApplicationIdle`、`SystemIdle` 各优先级在 EPLAN 空闲时都会立即执行。— spike S1。
- 🔴 **委托中未捕获的异常会直接终止 EPLAN 进程。** 用 `Dispatcher.Invoke(priority, timeout, delegate)` 调度的委托在主线程上抛出 `InvalidOperationException`，没有传回调用方，而是成为 `Dispatcher.ProcessQueue` 中的未处理异常，EPLAN 随即崩溃（Windows 事件日志：.NET Runtime 1026、Application Error 1000，2026-10-13 03:18:02）。— spike S1 第一次运行。
  - 对本项目的影响：**每一个调度到主线程的委托都必须在内部捕获全部异常**，把失败作为返回值交回调用方。这一点只能通过统一的调度封装来保证，不能依赖每个调用者自觉。在委托内部捕获后，失败能正确传回，之后的调度照常工作（S1r2）。
- 在 action 之外，**`SelectionSet.GetCurrentProject()` 和 `Project.ProjectFullName` 也需要 `LockingStep`**，所以 LockingStep 必须是主线程上执行的第一件事。— S1 第一次运行时，因为顺序错误，所有写入都失败了。
- 在调度器中（action 之外），回滚和撤销的规律与 action 中一致：`LockingStep → SafetyPoint`、`LockingStep → SafetyPoint → UndoStep（在内）` 不提交时都会回滚；`UndoStep` 在 SafetyPoint 外面时**不回滚**。经调度器写入、带有 UndoStep 的修改可以用 Ctrl+Z 撤销，此前的撤销历史保持完整；**没有 UndoStep 的写入同样可以撤销，历史没有被清空**。— S1r2。
- ⚠️ **模态对话框打开时，调度的委托仍会被执行**：它在该对话框的消息循环中被重入执行，执行时 EPLAN 主窗口处于禁用状态。在执行前检查 `IsWindowEnabled(EPLAN 主窗口)`，可以可靠地识别并拒绝这种情况（对话框打开时拒绝，关闭后正常执行）。— S1、S1r2（用所有者为 EPLAN 主窗口的 `MessageBoxW` 模拟）。

### A10. M1 骨架的实机验证（2026-10-18）
环境：EPLAN 2026.0.3.25421，Pro Panel 和 Electric P8 变体，插件与 `eplanmcp` 0.1.0（Release 构建），示例项目 `EPLAN_Sample_Project`。
- **注册 `bLoadOnStart = true` 的插件时，EPLAN 在同一个会话里立即加载它并调用 `OnInitGui`，不需要重启。** 用 `EPLAN.exe /Variant:"Pro Panel" EplApiModuleAction /register:"<dll>"` 启动 EPLAN，易盼云登录完成后插件即开始监听管道，同时出现影子副本目录 `%APPDATA%\EPLAN\ShadowCopyAssemblies\<pid>\EplanMcp.Bridge.Eplan\`。— 插件日志；EPLAN 经 explorer 启动（与双击图标相同，不在 Claude 桌面版的 MSIX 容器内）。
- **`OnInitGui` 中 `EplanMainThreadDispatcher.GetMainThreadDispatcher()` 已经不为 null**，不需要退回 `Dispatcher.CurrentDispatcher`。— 插件日志 `Main-thread dispatcher: EplanMainThreadDispatcher (preset by EPLAN)`。
- **插件注册按变体分开保存。** 只在 Pro Panel 下注册时，从桌面启动的 Electric P8 完全启动并打开项目后，插件仍然没有加载（没有插件日志、管道和影子副本）；用 `/Variant:"Electric P8"` 再注册一次之后才加载。— 实机验证。
  - 对本项目的影响：`install-addin`（M5）要为每个已安装的变体分别注册，`doctor` 要能指出"EPLAN 在运行但插件未加载"的变体。
- 注册之后，**从桌面正常启动 EPLAN（只带 `/Variant:"Electric P8"`）时插件自动加载**：进程启动后约 4 秒开始监听管道。— 插件日志，进程的父进程为 explorer。
- 变体从命令行参数 `/Variant:"…"` 读取正确，Pro Panel 和 Electric P8 都验证过；桌面快捷方式也带这个参数。— 插件日志与 `eplan_session_status`。
- **M1 的完成标准已经达到**：新开的 Claude Code 会话通过本机的 `.mcp.json` 启动 `eplanmcp serve`，调用 `eplan_project_list` 正确列出了 EPLAN 中打开的项目。— 实机验证，插件日志中有对应的握手记录。
- `ProjectManager.OpenProjects`（临时设 `LockProjectByDefault = false`，读完恢复）在 LockingStep 中列出打开的项目；调用期间 EPLAN 系统日志没有新增任何记录。— 实机调用 `eplan_project_list`，示例项目 `EPLAN_Sample_Project`。
- **`Project.ProjectFullName` 不带扩展名**：它是 `.elk` 文件的路径去掉 `.elk`，例如 `…\EPLAN_Sample_Project`；磁盘上是 `EPLAN_Sample_Project.elk` 文件和 `EPLAN_Sample_Project.edb` 目录。— 实机调用；EPLAN 日志中打开数据库的路径为 `…\EPLAN_Sample_Project.edb`。
- 端到端耗时（MCP 客户端 → Host → 管道 → EPLAN 主线程 → 返回，各调用 2–3 次）：`eplan_session_status` 25–52 ms，`eplan_project_list` 14–63 ms。— stdio MCP 客户端计时。
- **打开模态对话框（"选项 > 设置"）时，`project.list` 被立即拒绝**：返回 `EPLAN_BUSY`（`busyReason = ModalDialog`），用时 63 ms，没有等待调度超时。握手不经过主线程，所以此时 `eplan_session_status` 仍显示已连接。— 实机调用；插件日志 `project.list refused: ModalDialog`。
- **退出 EPLAN 时会调用 `OnExit`**：插件记录 `Pipe server stopped`，管道随之消失，之后的调用返回 `EPLAN_NOT_CONNECTED`（5 ms）。EPLAN 系统日志正常记录 `Application closed`，没有警告。— 实机调用。
- 不带 MSIX 包身份的进程中运行的 Host（`doctor` 报告"当前进程没有 MSIX 包身份"），可以通过 `PipeOptions.CurrentUserOnly` 连上 EPLAN 中插件的管道。— `eplanmcp doctor` 8 项检查全部通过。
  - 待验证：由 MSIX 包内进程（Claude 桌面版）启动的 Host 能否连上（S5）。

### A11. 只读 API 的实机验证（spike S6，2026-10-19，Electric P8，在 action 中）
示例项目的 scratch 副本：321 页、2876 个功能、2060 个连接、2900 个部件引用。详细数据见 [spike S6](../../spikes/S6-read-api/README.md)。
- **性能**：查找和读取都在 100 ms 以内。`GetFunctions(null)` 第一次 64 ms、第二次 14 ms；读取全部功能的名称 18 ms；`GetConnections(null)` 30 ms；`GetArticleReferences(null)` 36 ms；单个功能的全部 1000 个属性 4 ms。— S6。
  - 对本项目的影响：Bridge 可以一次取出全部对象，再在内存中过滤和分页。
- **名称**：
  - `Page.Name` = `IdentifyingName` = `PAGE_FULLNAME`，形如 `=GAA+A1&EFS1/1`；`PAGE_NAME` 只是页号。
  - `Function.Name` = `FUNC_IDENTNAME` = `FUNC_IDENTDEVICETAG`，是带结构的完整标识，如 `=GAA+A1-FC1`；辅助功能带连接点后缀，如 `+A1-FC2:1`。
  - `VisibleName` 是页面上显示的标识（`-FC1`）：可能为空，可能带 `>` 标记，也可能等于完整标识。
  - 同一个短标识可以出现在多个结构下。
  - `FunctionsFilter.Name` 和 `PagesFilter.Name` 对完整标识做前缀匹配，**区分大小写**，不匹配 `VisibleName`。
  - — S6。
- **多语言**：多语言属性的原始格式是 `de_DE@…;zh_CN@…;`，**`ToString()` 返回整个原始串**。语言无关的值是 `??_??@值;`，`GetString(语言)` 返回空串。两种情况都应该用 `GetStringToDisplay(语言)`。数值按区域格式返回（`1,5`）。带索引的属性不带索引读取时抛出 `IndexedPropertyException`。— S6。
- **连接**：
  - `Connection3D` 的 `StartPin` / `EndPin` 抛出 `NotSupportedException`，`Page` 为 null。
  - 已过时的 `ConnectionsFilter.Page` 漏掉了连接（35 个，实际 40 个），按页列出应在内存中按 `Connection.Page` 过滤。
  - — S6。
- **读取的副作用**：只读探针前后，项目的设计数据文件没有变化。但**任何一次 action（哪怕只获取项目、不读任何对象）都会改写项目的 `User.eod` / `User.eox`**；打开项目后空闲时不会改写。调度器（action 之外）中是否也会改写，尚未验证。— S6，对比项目文件的大小和修改时间。
- **运行检查是写操作**：`Check.VerifyProject` 在示例项目上用时 3.9 秒，检查消息从 0 条变为 20 条，并改写了项目的 `Message`、`Project`、`Blob` 数据文件。读取已有的检查消息用 `PrjMessagesCollection`，元素类型是 `ProjectMessage`。— S6。
- 🔴 **系统消息集合不能交叉使用**：创建 `SysMessagesCollection` A 之后又创建集合 B，再读取 A 中消息的文本（`GetMessageText`），EPLAN 报"硬件错误：存储器访问受阻"（原生访问冲突，托管代码捕获不到），整个 action 失败；在 action 中 EPLAN 没有崩溃。只用一个集合时，读取全部 490 条消息都正常；先捕获 EPLAN 异常再读取也正常。— S6 r2、r3 中触发，r7 稳定复现。
  - 对本项目的影响：同一时间只使用一个集合，读出后立即复制到 DTO（编码规范 9.5 第 15 条）。
- `SysMessagesCollection()` 包含整个 EPLAN 会话的消息，其中有 Trace 级别的消息，以及一条级别为 7 的"有新版本"提示（7 不在 `MessageLevel` 枚举中）。— S6。
- **`ProjectOpen` 不一定改变当前项目**：已有一个项目打开时，用 `ProjectOpen` 再打开一个，`SelectionSet.GetCurrentProject` 返回的仍是原来的项目；`XPrjActionProjectClose` 关闭的也是原来的项目。— S6 第一次运行（驱动脚本因此关闭了工程师自己的项目，没有读取或修改它）。
  - 对本项目的影响：不依赖"当前项目"，按名称或路径找项目（编码规范 9.5 第 14a 条）。

### A8. 插件程序集的生命周期
- **插件程序集一旦被 EPLAN 加载，在 EPLAN 进程退出之前无法卸载。** 注销之后再注册同名的新版本，执行的仍然是内存中的旧代码，不会报错。— spike S2（第一次重新构建后，新探针没有执行）。
  - 对开发流程的影响：重新编译 Bridge 后，必须重启 EPLAN；spike 中可以改用新的程序集名（`-p:AssemblyName=…r2`）。
  - 对安装的影响：`install-addin` 更新插件之后，必须提示用户重启 EPLAN；握手时比对 Bridge 的版本号，可以发现"已更新但尚未重启"的情况。

### A6. 由 MSIX 打包应用启动的进程（Claude 桌面版）
- Claude 桌面应用以 MSIX 包安装（包族名 `Claude_pzs8sxrjxfjjc`）。由它启动的子进程写入 `%LOCALAPPDATA%` 时，**被重定向到 `%LOCALAPPDATA%\Packages\Claude_pzs8sxrjxfjjc\LocalCache\Local\`**，包外的进程（EPLAN）看不到这些文件。写到 `D:\…` 以及 `%TEMP%`（即 `%LOCALAPPDATA%\Temp`）下的文件，EPLAN 可以正常读取。— spike S0 第一次运行失败的原因；检查包的 LocalCache 目录确认。
  - 对本项目的影响：由 Claude 桌面版启动的 Host，不能把 EPLAN 需要读取的文件（scratch 项目、实例发现文件）写到 `%LOCALAPPDATA%`；审计日志写在那里，用户也会找不到。见 ADR-0008。
  - **由客户端启动的进程没有包身份，但仍被重定向**：Claude 桌面版中 Claude Code 会话的 shell、它启动的 `eplanmcp`，`GetCurrentPackageFullName` 都返回 `APPMODEL_ERROR_NO_PACKAGE`。可是它们写入 `%LOCALAPPDATA%`、`%APPDATA%` 的文件，仍然落在包的 `LocalCache\Local`、`LocalCache\Roaming` 下。所以不能用包身份判断是否被重定向。— spike S5。
  - 不重定向：`%TEMP%`，以及数据根目录 `%USERPROFILE%\EplanMcp`（包内复制的夹具，包外的 EPLAN 正常打开）。命名管道不受影响：包内启动的 `eplanmcp` 能连上包外 EPLAN 的插件。— spike S5。
  - 包内进程能读到真实位置的 `%LOCALAPPDATA%`（能看到其他程序的目录），所以"Bridge 写、Host 读"可以读到；会话期间由包外进程新写入的文件，没有单独验证。— spike S5。

### A4. 路径变量
- 在 2026 中，`PathMap.SubstitutePath` 无法替换 `$(MD_SCHEMES)`、`$(MD_IMAGES)`、`$(TEMPPATH)`、`$(USERSETTINGSPATH)`，原样返回；`$(MD_MACROS)`、`$(MD_PARTS)`、`$(MD_SYMBOLS)`、`$(MD_FORMS)` 正常。— 实机调用。待查：2026 中方案（schemes）的存储位置。

### A12. M2 只读工具的实机验证（2026-10-19，EPLAN 2026.0.3.25421，Electric P8）
夹具：`EPLAN_Sample_Project` 的 scratch 副本，位于 `%USERPROFILE%\EplanMcp\scratch\fixtures\`。工具通过 Release 构建的 `eplanmcp` 调用（`feat/message-check-session-tools`，13 个工具），插件为同一提交的 Release 构建。
- **夹具的做法**：直接复制 `.elk` 文件和 `.edb` 目录即可，EPLAN 正常打开（M2 设计 4.6 第 6 项）。`EPLAN.exe /Variant:"Electric P8" ProjectOpen /Project:"<.elk 的路径>"` 在启动时打开它，EPLAN 随后保持运行，与 `EplApiModuleAction` 的命令行形式相同。— 实测。
- **插件按变体注册**：Electric P8 下原先没有注册插件，EPLAN 能启动，但既没有管道，系统日志里也没有任何插件记录。用 `eng\scripts\eplan-addin.ps1 -Action register -Variant 'Electric P8'` 注册后，每次启动都会加载。— 实测。
  - 对本项目的影响：连不上时先看是否在当前变体注册过插件。`eplan_session_status` 的提示，以及 M5 的 `install-addin`，都要说明"每个变体各注册一次"。
- **13 个工具全部成功**，单次调用最慢的是 `eplan_project_get`（`detailed`，493 ms），其余都在 250 ms 以内：321 页、504 个设备、2060 个连接、2900 个部件引用、314 种部件。
  - `-FC1` 出现在 15 个设备上，`eplan_device_get` 返回 `AMBIGUOUS_NAME` 并列出候选的完整标识；
  - 连接的 `colour`（取自 `CONNECTION_WIRENUMBER`）在示例项目中是颜色代码（`OG`、`BK`），与 S6 一致；在其他项目中的含义仍待确认（[`eplan_connection_list` 规格](../tools/eplan_connection_list.md)第 3 节）；
  - 示例项目没有检查消息，`eplan_check_list` 返回空列表和提示；
  - `eplan_session_select` 选中当前进程。
  - — 实测（`dotnet run` 的 MCP 客户端脚本，不在仓库中）。
- **读取没有副作用**：调用全部 13 个工具前后，夹具 `.edb` 中所有文件的修改时间和大小都没有变化；EPLAN 系统日志只新增了数据库打开、关闭的信息。— 实测。
- **部件描述只有德文和部分英文**：示例项目中 `ARTICLE_DESCR1` 没有中文。界面语言为中文时，`GetStringToDisplay` 返回空串，**不会回退到其他语言**，所以 `eplan_part_list` 和 `eplan_device_get` 的部件 `description` 都是 null；传入 `language: "de_DE"` 时全部都有，传入 `en_US` 时部分有。页描述有中文，可以正常显示。— 实测。
  - 已按[语言回退设计](../design/2026-10-19-text-language-fallback.md)实现，实测：省略 `language` 时，西门子的 33 种部件中，前 5 种有 4 种返回英文描述、1 种只有德文（`descriptionLanguage: "de_DE"`）；显式传入 `zh_CN` 时仍是 null，没有标记字段；中文页描述不触发回退。`=GAA+A1-FC1` 的 detailed 结果中 281 个属性有 7 个用了回退语言（部件名称、部件描述），项目的 308 个属性和页的 79 个属性没有回退；读原始串（`PropertyValue.ToString()`）找候选语言的做法在各种类型的属性上都没有报错。
  - 回退后 `text` 过滤匹配的是回退语言的文本：`eplan_part_list` 用德文 `Leistungsschalter` 找不到英文描述的部件，要传 `language: "de_DE"`。
- 部件数据中有一个部件号是一个空格（`" "`），被 10 个引用使用，这些引用既不在设备上，也不在导线上。— 实测。
- **Large 测试**（`tests/EplanMcp.Large.Tests`，19 个，约 11 秒）把以上数据固定下来，并确认了几项以前没有验证的行为：
  - 项目的 detailed 属性中，属性定义给出可读的名称（超过 90% 不是纯数字），带索引的属性按索引逐个返回；
  - 在 Bridge 调度器中（不在 action 中）读取检查结果和系统消息，与其他读工具一样不改动项目文件；
  - 列出 200 页（读取全部 321 页的页描述）约 90 ms，项目的 detailed 结果约 170 ms。
  — 实测（2026-10-19）。
- **带过滤器的 `GetFunctions` 会多返回同名的主功能**：不带过滤器时，示例项目每个完整标识只有一个主功能（504 个）；带 `FunctionsFilter.Page` 或 `FunctionCategory` 时，还会返回没有 `VisibleName` 的同名主功能。黑盒里的功能（`=EC1+A3-EC1-BT2`：`Blackbox` 加一个 `AnalogSensor`）和第二种表达（`=EC1+A3-EC1-MA2` 的两个 `Motor`）都是这样。`=EC1+A3&EFS1/1` 页因此有 95 个主功能，但只有 85 个不同的标识。— 实测（`eplan_device_list` 的 `page` 和 `category` 过滤，与不过滤的列表对比）。
  - 对本项目的影响：设备列表和页上的设备按完整标识合并为一行，保留有显示标识的那一个（`DeviceTags.OnePerTag`）。

### A13. 写入 API 的实机验证（spike S9，2026-10-20，EPLAN 2026.0.3.25421，Electric P8，在 action 中）
示例项目的 scratch 副本。详细数据见 [spike S9](../../spikes/S9-write-api/README.md)。
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

### A18. 复制、备份、删页、重编号（spike S12，2026-10-20，EPLAN 2026.0.3.25421，Electric P8，在调度器中）
详细数据见 [spike S12](../../spikes/S12-destructive-backup/README.md)。
- **`ProjectManager.CopyProject(源 .elk, 目标 .elk, CopyMode.Snapshot)` 可以复制打开着的项目**：项目保持打开，不加锁，示例项目（313 个文件、213 MB）0.6–0.7 秒；源项目只改写 `User.eod` / `User.eox`（同 A11）。副本可以直接打开，页数、功能数与源项目相同；在一次已提交的写入之后立即复制，副本包含这次写入。— S12。
- `CopyMode.All` 同样可用（0.8 秒），但会改写源项目的 `.lck` 锁文件。`Backup.Project(…, Backup.Type.MakeBackup, false)` 用时约 10 秒，得到一个 `.zw1`（70 MB），会改写源项目多个 `.eod` / `.eox` 和 `ProjectInfo.xml`；源项目仍在原位、仍打开，但一次运行中当前项目随之改变。— S12。
- **`Page.Remove()`、`Renumber.DeviceTags`、`Renumber.Pages` 都能被 `SafetyPoint` 回滚，提交后 Ctrl+Z 一步撤销**，在 `QuietModeStep(ShowNoDialogs)` 下都没有弹出对话框。— S12。
- **删页会留下没有主功能的辅助功能**：删除电缆主功能所在的页后，其他页上的芯线功能仍在。报表页不随之更新。— S12。
- **`Renumber.DeviceTags(IFunctionBase[], null, Renumber.Enums.DeviceTags.PostNumerate, 方案, 1, 1)` 只给设备标识中的 `?` 编号**：`-?XG1` → `-XG2`（跳过已占用的 `XG1`）；端子连接点的 `?1` 不变；`=GAA+A1-?QA1` 也没有被编号（原因未查）。方案传 `""` 等同于 `Standard`；名称不存在时抛 `InvalidScheme`，消息列出可用的方案名。这个重载的第 4 个参数是方案（异常中的参数名为 `strSchemePerIdentifier`）；XML 文档中 `Project` 重载的参数名与类型顺序不一致，不要照着它传参。— S12。
  - 对本项目的影响：`eplan_device_renumber` 执行后读回，报告仍带 `?` 的标识；`eplan_page_delete` 的变更清单列出会失去主功能的设备。

### A17. M3a 写工具的实机验证（2026-10-20，EPLAN 2026.0.3.25421，Electric P8）
`eplan_page_create`、`eplan_page_update`、`eplan_device_update` 的 Release 构建，经 `eplanmcp` 调用，每个工具用一份新复制的示例项目（scratch 中），另有一份 scratch 之外的副本只用来验证拒绝。步骤和结果记在 PR #109、#114、#121 中。
- **Q1**：对 scratch 之外的副本建页返回 `POLICY_DENIED`、`not_started`，设计数据和审计都不变。— 实测。
- **Q3：一次写入就是一个撤销步骤**：四次建页（其中一次建 4 页）后按四次 Ctrl+Z，依次撤销四次写入；改页描述、改设备标识同样一一对应。— 实测。
- **Q2：事务中失败时整体回滚，并且不留空的撤销步骤**：把电缆 `+A1-WE1` 改名为 `=F+A1-WE19` 时，EPLAN 把名称改成了 `+A1-WE19`（电缆不用 `=`，A13），读回不一致，返回 `INVALID_ARGUMENT`、`rolled_back`；电缆和它的 6 个辅助功能都保持原名。随后的第一次 Ctrl+Z 撤销的是之前那次成功的改名，说明回滚掉的写入没有在撤销列表中留下步骤。— 实测。
- `Page.Create` 支持 `Circuit`、`CircuitSingleLine`、`Overview`、`Graphics` 四种类型；页描述中的 `;`、`@` 原样写入并读回（`a;b@c`）。— 实测。
- 只改一种语言的页描述时，其他语言保持不变；清除一页唯一的一种语言后，描述为空。— 实测。
- 在调度器中给主功能和全部辅助功能的 `Function.Name` 赋值：另一页上的辅助功能（交叉引用）跟着改名，部件引用和 6 个连接都不变。— 实测（`=F+A2-KF1` → `=F+A2-KF19`）。
- 写入只产生 `execution_started`、`execution_result` 两条审计记录；被拒绝的调用（策略、重名、找不到）不写审计。`eplanmcp audit verify` 通过。— 实测。
- 待查：对 EPLAN 中的当前项目（`M3_OUT`）调用任何工具（包括只读的 `eplan_page_list`）后，它的 `User.eod` / `User.eox` 都被改写；同时打开的非当前项目（`M3_PC`）上，连续三次读取都没有改写。A12 中当前项目（评测夹具）也没有被改写，原因不明。只涉及 EPLAN 按用户保存的数据，设计数据不变。— 实测。

### A16. 检查、报表、导出（spike S11，2026-10-20，EPLAN 2026.0.3.25421，Electric P8，在调度器中）
详细数据见 [spike S11](../../spikes/S11-workflow-apis/README.md)。
- **`Check.VerifyProject(project, "", false)` 能被 `SafetyPoint` 回滚，提交后 Ctrl+Z 一步撤销**：检查消息 0 → 20 条 → 回到 0 条。用时 2.7–4.7 秒，改写 `Message`、`Project`、`Blob`、`User` 数据文件（同 A11）。`VerifyPages`（2 页）0.2 秒。— S11。
- **`Reports.GenerateProject(project)` 能被 `SafetyPoint` 回滚，提交后 Ctrl+Z 一步撤销**：示例项目新增 1 张报表页，用时 7.7–9.3 秒。`Reports.Update(page)` 更新一张目录页 0.3 秒。— S11。
- **导出不改动设计数据，但改写 `User.eod` / `User.eox`**：`Export.PdfPages` 3 页 0.7 秒，全部 321 页 12.5 秒（6.1 MB）；`Export.DxfDwgPagesToDisk` 2 页 0.6 秒。文件只出现在给定目录中，但 DXF 方案会在目录下按"项目名\结构"建子目录，文件名含 `=`、`+`、`&`。方案名传空串时，`SchemeSetting.GetLastUsed()` 前后不变。— S11。
  - 对本项目的影响：ADR-0012 的前提"导出不改动项目"指设计数据；导出后要递归核对目录中的每个文件（M4 设计 4.4）。
- 以上调用在 `QuietModeStep(ShowNoDialogs)` 下都没有弹出对话框。— S11。

### A15. 写操作中断（spike S8，2026-10-20，EPLAN 2026.0.3.25421，Electric P8，在调度器中）
详细数据见 [spike S8](../../spikes/S8-write-interruption/README.md)。
- 🔴 **`SafetyPoint` 不防进程死亡**：EPLAN 每做一步修改就写入项目数据库，回滚只在进程活着时发生。20 页的写入做到第 5 页、尚未提交时用 `taskkill /F` 结束 EPLAN，重启后这 5 页都在；已提交的修改当然也都在。— S8 r3。
  - 对本项目的影响：写操作被中断后，项目可能处于"做了一半"的状态。ADR-0010 中的 B3（把崩溃变成确定的结果）不可行；`OUTCOME_UNKNOWN`、暂停写入、人工核实是唯一正确的处理。撤销历史随进程消失，解除暂停的提示不能建议 Ctrl+Z（M3 设计 4.6 已修改）。
- 🔴 **调度的委托中发生原生访问冲突，EPLAN 进程直接结束**：`AccessViolationException`（损坏状态异常）不会被委托中的 `try/catch` 捕获，WPF 调度器也不处理。事务中已做的修改同样留在项目里。同样的错误在 action 中不会结束 EPLAN（A11）。— S8 r3。
  - 对本项目的影响：编码规范 9.5 第 15 条没有兜底，必须遵守。
- **重开被结束时开着的项目，EPLAN 弹出"检查相容性"**："未按照规定关闭项目 '…'"，选项"是"（打开并检查数据）、"否"（不打开，随后通知"无法打开项目"）、"以后"（打开，下次再检查）。用 Remote Client 执行 `ProjectOpen /Quiet` 时也会弹出，并挡住之后所有的远程调用。— S8 r1–r3，UI Automation 读出。
- **EPLAN 启动时自动重开上次开着的项目**，所以被结束时开着的项目在下次启动时就会弹出上面的对话框。— S8 r1。
- **被结束的会话在 `EplLog.eldb` 的 `TSysSession_v100` 中没有结束时间**，正常退出的会话有；`TSysMsg_v100` 中没有关于中断的记录。— S8。
- 用 UI Automation 访问 EPLAN 的窗口时，如果 EPLAN 的界面线程正忙（例如主线程上正在执行写入），调用会一直等到它空闲。— S8 r2。

### A14. 在 Bridge 调度器中写入（spike S10，2026-10-20，EPLAN 2026.0.3.25421，Electric P8）
探针在 action 返回之后经 `Dispatcher.Invoke` 在主线程上执行，与 Bridge 相同；写入的包装是 `SafetyPoint` → 其中的 `UndoStep` → `QuietModeStep`。详细数据见 [spike S10](../../spikes/S10-dispatcher-writes/README.md)。
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

---

## B. 官方文档与反射核实的事实（2026-10-12）

> 出处缩写：`2026/<页面>` 指 `https://www.eplan.help/en-us/Infoportal/Content/api/2026/<页面>`；"XML" 指 EPLAN Bin 目录中随 API 一起提供的 IntelliSense 文档（`Eplan.EplApi.*u.xml`）；"反射" 指对 `C:\Program Files\EPLAN\Platform\2026.0.3\Bin` 中程序集做只读反射。

### B1. 程序集与目标框架
- 2026 的 14 个 `Eplan.EplApi.*u.dll` 全部以 **.NET Framework 4.8.1** 为目标：AFu、Baseu、DataModelu、EServicesu、Guiu、HEServicesu、MasterDatau、RecorderToolsu、RemoteClientu、Remotingu、Starteru、Systemu、WebServicesu、WebServiceu。程序集版本 1.0.0.0，文件版本 2026.0.3.25421。大多数是 x64 混合模式程序集。— 反射。
- `EPLAN.exe` 是原生 x64（MFC）程序，CLR 在其中被宿主。— 反射（没有 CLR 头）。
- Bin 中自带 `Newtonsoft.Json 13.0.3`（程序集版本 13.0.0.0）、`Grpc.Core 2.46.6`、`Grpc.Core.Api 2.55`、`Google.Protobuf 3.24.1`；**没有** `System.Text.Json`、`System.Memory`、`Grpc.Net.Client`。— 检查 Bin 目录。
- **EPLAN 2027 新增一套并行的 .NET 8 API**（程序集名带 `Net`：`AFNetu`、`BaseNetu`、`DataModelNetu`……），**.NET Framework 4.8.1 的 API 仍然保留**。两套程序集不能混用。.NET 8 插件用 `EplApiModuleActionNet` 注册，需要安装 .NET 8 Desktop Runtime 和 ASP.NET Core Runtime。为 .NET 8 构建的插件只能在 2027 及以后运行。— 2027 `EplanApiDotNet.html`、`Assemblies for .NET 8.html`、`EplApiModuleActionNet.html`。
  - 未验证：现有的 4.8.1 插件能否不重新编译就在 2027 上运行。既然 4.8.1 API 仍然保留，可能性很大。

### B2. 插件（Add-in）
- 接口 `Eplan.EplApi.ApplicationFramework.IEplAddIn`（AFu）：`bool OnRegister(ref bool bLoadOnStart)`、`OnUnregister()`、`OnInit()`、`OnInitGui()`、`OnExit()`。**一个程序集中只能有一个类实现它。** — 反射 + XML。
- 生命周期：`OnRegister` 在第一次选择该插件时调用一次；`OnInit` 在启动时加载时调用；`OnInitGui` 在界面初始化完成后调用，并且只在 `bLoadOnStart = true` 时才会调用；`OnExit` 在退出时调用。`bLoadOnStart = false` 时，插件在其 action 第一次被调用时才延迟加载，`OnInitGui` 不会被调用。— XML。
- 插件会被影子复制到 `%APPDATA%\EPLAN\ShadowCopyAssemblies\`，所以 EPLAN 运行期间可以重新编译原始 DLL。可选接口 `IEplAddInShadowCopy.OnBeforeInit(string originalPath)` 会传入原始路径。— `2026/ShadowCopyingAPIAssemblies.html`。
- 注册方式：
  - 界面：文件 > 附加 > 接口 > API > 管理；
  - 命令行：`EPLAN.exe /Variant:"Electric P8" EplApiModuleAction /register:"<dll 全路径>"`，以及 `/unregister:"<程序集标题>"`；
  - 代码：`EplApplication.ShowApiAddInDialog()`。
  
  — `2026/EplApiModuleAction.html`、`2026/CSharpAddins.html`。
- 注册信息保存在 STATION 设置 `EplanEplApiModule.<模块>.AsmAddIn` 中（`AssemblyPath`、`AssemblyName`、`TypeName`、`LoadOnStart`）。— 本机 `Platform\2026.0.3\Cfg\EplanEplApiModule.xml`。

### B3. 自定义 Action
- `IEplAction`：`bool OnRegister(ref string Name, ref int Ordinal)`、`bool Execute(ActionCallingContext ctx)`、`void GetActionProperties(ref ActionProperties)`。**action 名称中不能含有 `.`**。— 反射 + XML。
- `ActionCallingContext`（派生自 `Eplan.EplApi.Base.Context`）：通过 `GetParameter(name, ref value)` 读取输入，通过 `AddParameter(name, value)` 输出结果，全部是字符串。`ContextParameterBlock` 可以传递对象，但只限于进程内。— 反射。
- 进程内调用：`new ActionManager().FindAction(name).Execute(ctx)`，每次调用使用新的 context。— `2026/CallingActions.html`。

### B4. Remote Client（`Eplan.EplApi.RemoteClientu.dll`）
- `EplanRemoteClient` 的主要成员：`Connect(host, port[, TimeSpan])`、`Ping()`、`ExecuteAction(string)`、`ExecuteAction(string, ref Eplan.EplApi.Remoting.CallingContext)`、`StartEplan(...)`、`StopEplan()`、`GetActiveEplanServersOnLocalMachine(...)`、`SelectEplanObjects(...)`、`LockAllEplanObjects`/`UnlockAllEplanObjects`、`DisableSynchronizingCalls(bool)`，以及属性 `SynchronousMode`。— 反射。
- **只有在 `SynchronousMode = true` 时才能读回返回值**（默认是异步，没有返回值）。返回值都会被转换为字符串；输出键如果在调用前已存在于字典中，就不会被覆盖，所以输出要使用不同的键名。— XML + IL 分析。
- **远程调用只在 EPLAN 空闲且没有 action 正在运行时才执行**，可以用 `DisableSynchronizingCalls(true)` 关闭这项同步。— XML。
- 传输层是旧版 C-core gRPC（`Grpc.Core`），使用 `ChannelCredentials.Insecure`（没有 TLS），附带一个双工回调流。客户端进程还需要 `System.Memory` 和 `System.Runtime.CompilerServices.Unsafe`，这两个 Bin 里都没有。— 反射 + IL。
- 服务器端：需要开启"允许通过 Remote Client 进行远程访问"（`STATION.Remoting.AllowLocalAccess`）；EPLAN 不能以 `/NoRemoting` 启动；默认端口 49152，可以用 `/EplanServerPort` 指定（49152–65535）。— `2026/EPLANRemoting.html`。
- 远程调用 `ExecuteScript` 时，即使脚本编译失败也会返回成功（见 A1）。
- 客户端（net481 x64）引用 EPLAN Bin 中的 `RemoteClientu`、`Remotingu`、`Grpc.Core.Api`、`Newtonsoft.Json`，另加 NuGet 包 `System.Memory` 4.5.5（带来 `Unsafe` 等）即可运行。**gRPC 会使用 `HTTP_PROXY` 等代理环境变量**，设置了代理时要在客户端进程内清除，否则连不上本机的 EPLAN。— spike S9 驱动。
- 通过 `ExecuteAction` 传给 action 的参数中，**含中文的路径没有完整传到**（action 报文件不存在）；ASCII 路径正常。— spike S9。

### B5. 离线程序（无界面）
- `Eplan.EplApi.System.EplApplication`（Systemu）：`Init(appModifier[, allowLicenseDlg[, allowLoginDlg]])`、`Exit()`，以及属性 `EplanBinFolder`、`LicenseFile`、`QuietMode`、`StartEplanRemoteServer`（必须在 `Init` 之前设置）。— 反射。
- `Eplan.EplApi.Starteru.dll` 提供 `AssemblyResolver`（`SetEplanBinPath` 等）、`EplanFinder.SelectEplanVersion`、`EplanSignedAssemblyAttribute`。— 反射。
- 要求：x64 进程；目标 .NET Framework 4.8.1；**必须是 STA 线程**（`[STAThread]`）；`Exit()` 只能在主线程调用一次，之后不能再次 `Init`；使用 DataModel 前要创建 `LockingStep`。— XML、`2026/UsingEplanAssemblies.html`。
- `Init` 之后，除了会显示界面的功能（模态对话框、停靠窗口、MDI 窗口）以外，**所有 API 功能都可以使用**。— XML。

### B6. 线程
- **只能在 EPLAN 主线程上调用 API。** 官方说明："Executing API code purely out of main Eplan thread is not recommended… not tested and are out of support." — XML（`EplanMainThreadDispatcher`）。
- 官方提供 `Eplan.EplApi.Base.Internal.EplanMainThreadDispatcher`（Baseu）：`ExecuteInMainThreadSync(...)`、`ExecuteInMainThreadAsync(...)`、`CanAccessMainThread()`，以及静态方法 `SetMainThreadDispatcher(System.Windows.Threading.Dispatcher)`、`GetMainThreadDispatcher()`。没有设置调度器时调用会抛异常。— 反射 + XML。
  - 未验证：EPLAN 的 GUI 进程是否已自动设置该调度器。M1 spike 验证。
  - 注意：这个类型位于 `Internal` 命名空间，但有官方文档。使用时封装在适配层中，以便日后替换。
- `LockingStep`：明确不支持多线程。— XML。

### B7. 事务、锁定、撤销、静默模式（`Eplan.EplApi.DataModel`，除非另行注明）
- `SafetyPoint`：静态 `Create()`，以及 `Start()`、`Commit()`、`Rollback()`、`Dispose()`。存在期间自动锁定对象；**不调用 `Commit` 就会回滚**；提交时释放锁。— 反射 + `2026/Locking.html`。
  - 实测（spike S2，在 action 中）：不提交即回滚**成立**，`Rollback()`、`Start()` 后释放、中途抛异常也都能回滚，修改已有属性同样会恢复。**例外：如果 `UndoStep` 在 SafetyPoint 外面创建，回滚会静默失效**（即使显式调用 `Rollback()`）。S0 中观察到的"不回滚"正是这个原因。详见 A7。
- `LockingStep`：释放其生存期内获得的锁，可以嵌套。在非模态对话框、离线程序和校验中**必须显式创建**；action 内部会隐式创建一个。— XML。
  - 对本项目的影响：Bridge 通过调度器执行的代码**不在 action 内**，所以必须自己创建 `LockingStep`。
- 缺少 `LockingStep` 时抛出 `NoLockingStepException`（派生自 `DataModelException`）。`Project.LockAllObjects()`、`StorableObject.LockObject()` / `SmartLock()`；锁定失败时抛出 `Eplan.EplApi.Base.LockingException`，其 `GetAllFailed2LockAsString()` 列出锁定失败的对象。— 反射 + XML。
- **撤销**：`UndoManager.CreateUndoStep()` 返回 `UndoStep`（`SetUndoDescription(string)`、`CloseUndo()`、`DoUndo(...)`）。**如果通过 API 修改项目时没有创建 `UndoStep`，EPLAN 会清空撤销历史。** — 反射 + XML。
  - 对本项目的影响：每个写操作都必须创建并关闭 `UndoStep`，否则工程师会失去手工操作的撤销历史。
- `TransactionManager.CreateTransaction()` 返回 `Transaction`（`Commit`、`Abort`、`Store`）。— 反射。与 `SafetyPoint` 的分工见 A7、A13（S9：插入宏只有 `SafetyPoint` 能撤回，按符号新建功能只能用 `Transaction`）。
- `QuietModeStep(QuietModes)`（AFu）配合 `using` 使用。`QuietModes`：`ShowAllDialogs=0`、`ShowNoDialogs=1`、`ShowSomeDialogs=2`。— 反射。

### B8. 授权
- **脚本**：不需要 API 授权即可运行，但只能使用 Base、ApplicationFramework/Scripting、Gui、MasterData；引用其他程序集（例如 DataModel）需要 **API 扩展授权**（`LicenseOptions.APIExtension = 725`）。— `2026/Scripts.html`。
- **编译的 API 程序（插件、离线程序）**有两种授权方式：
  - **开发者授权**：只能加载**未签名**的程序，"normally has a limitation of a maximum size of Eplan projects to 5 pages"（通常把项目大小限制在 5 页以内），只用于开发和测试；
  - **运行时授权**：只能加载**已签名**的程序。签名流程：在程序集上标注 `EplanSignedAssemblyAttribute`，用 EPLAN 提供的公钥延迟签名，然后上传到 Eplan Cloud 的 **EADN Signing** 服务完成签名（需要 EADN Signing 的 User 角色，自动签名还需要个人访问令牌）。客户"通过开发者"获得该程序的运行时授权。
  
  — `2026/Signing_EPLAN_assembies.html`。
- 运行时由 `Base.Internal.LicenseChecker` 检查调用栈，缺少授权时报告 "API License missing"。— XML。
- Remote Client：没有找到官方的授权说明；客户端 DLL 不引用 Baseu，不做客户端授权检查。— 未验证。
- **已验证（spike S0）**：在开发机的授权下，未签名的插件可以加载，并能读写 321 页的项目（A5）。其他授权类型仍然未知。

### B9. 导出与导出方案（`HEServices.Export`、`Base.SchemeSetting`，2026-10-20）
导出工具依赖的文档说明；实测结果见 A14、A16。
- `Export.PdfPages(ArrayList, 方案, 文件名, DegreeOfColor, 3D 模型, 语言, 打印边距)`：方案为空串时用界面上次使用的方案（`USER.PDFExportGUI.SCHEMAS`）；文件名带路径时覆盖方案中的目录，目录不存在时新建；没有扩展名时补上。`DegreeOfColor` 为 `Color`、`GreyLevel`、`BlackAndWhite`。— XML，枚举名经编译确认。
- `Export.DxfDwgPagesToDisk(Page[], 方案, 目录, 语言)`：方案在 `USER.DXF.SCHEMES`，空串用上次使用的，名称不存在时抛 `BaseException`；方案可以按页树或页属性建子目录；语言非空时先把项目翻译成该语言。— XML。
- `Export.GraphicPage(Page, 目录, Format, 色深, 宽度, Compression, 黑白)`：目录不存在时新建；PNG、JPG 可用色深 24；压缩只影响 TIF；高度按页的比例计算；不能导出外部文档页。— XML。
- `SchemeSetting`：`Init(节点)`；`CheckIfSchemeExists(名称)`；`GetCount()`、`SetScheme(序号)`（不改"上次使用"）、`GetName()`（与语言无关的内部名称）；`GetLastUsed()`；`SetLastUsed(名称)`，空串抛 `ArgumentException`，名称无效抛 `BaseException`。— XML。
- `Export`、`SchemeSetting` 都实现 `IDisposable`。— 编译（CA2000）、XML。

---

## C. MCP 客户端与 C# SDK（spike S4，2026-10-13）

> 不属于 EPLAN API，但同样是"代码依赖的外部行为"，按同样的规则记录。环境：ModelContextProtocol 2.2.0，Claude Code 2.1.295。

- **Claude Code 2.1.295 使用 MCP 2026-07-28**：连接时先发 `server/discover`（id `server-discover-probe-1`），每个请求的 `params._meta` 中都带 `io.modelcontextprotocol/protocolVersion: "2026-07-28"`、`clientInfo`（`name: "claude-code"`）和 `clientCapabilities: {"roots": {"listChanged": true}, "elicitation": {"form": {}, "url": {}}}`；随后用 `subscriptions/listen` 订阅 `toolsListChanged`。— [spike S4](../../spikes/S4-elicitation/README.md) 的报文记录。
- **SDK 2.x 中，工具抛出 `InputRequiredException` 即可发起 elicitation**，两种协议都适用：2026-07-28 下返回 `resultType: "input_required"`（`inputRequests` + `requestState`），客户端用新的 id 重发 `tools/call` 并带回 `inputResponses` 和原样的 `requestState`；2025-11-25（stdio）下 SDK 改为向客户端发送 `elicitation/create` 请求，拿到回答后**重新调用同一个工具处理器**，`RequestContext.Params.InputResponses` 和 `RequestState` 同样有值。— S4 selftest（10 个场景）+ 报文核对。
- `McpServer.IsMrtrSupported` 在 2026-07-28 下、以及 stdio 上的 2025-11-25 会话中都为 true；客户端没有声明 elicitation 时 `McpServer.ClientCapabilities.Elicitation` 为 null。规范禁止向未声明的客户端发送 `elicitation/create`，因此两者都要检查。— S4 + [MRTR 规范](https://modelcontextprotocol.io/specification/2026-07-28/basic/patterns/mrtr)。
- `McpServer.ElicitAsync` 在无状态模式下抛出 `InvalidOperationException`，只适用于旧协议的有状态会话；Host 不使用它。— SDK 文档 `docs/concepts/elicitation`。
- SDK **不按 `inputSchema` 校验 `tools/call` 的参数**：即使 schema 中有 `additionalProperties: false`，拼错的参数（如 `limt`）也被静默忽略，工具按默认值执行。Host 用 `tools/call` 请求过滤器统一拒绝未知参数。— M1 骨架的 stdio 端到端测试（2026-10-13）。
- SDK 由 `[McpServerTool]` 生成的 `inputSchema` **不含 `additionalProperties: false`**；在特性中显式设置的 `Title`、`ReadOnly`、`Destructive`、`Idempotent`、`OpenWorld` 都出现在 `tools/list` 的 `annotations` 中（`title` 同时出现在工具顶层）。— S4 报文。
- 参数上的 DataAnnotations 特性会写进 `inputSchema`：`[AllowedValues]` 生成 `enum`，`[Range]` 生成 `minimum` / `maximum`；SDK 不校验 `enum`：不在其中的值照样传给工具方法（`minimum` / `maximum` 未单独验证）。— 2026-10-19 核对 `tools/list`，以及 `eplan_tool_usage` 的 stdio 端到端测试。
- 缺少必填参数（方法参数没有默认值，schema 中列在 `required`）时，工具方法不会被调用，SDK 返回 `isError: true` 和通用文字 "An error occurred invoking '<工具名>'."，**没有 reason，也不说缺了哪个参数**。可选参数显式传 `null` 时，方法收到 `null`。— 2026-10-19 stdio 端到端实测（`eplan_tool_usage`）。
  - 对本项目的影响：Host 的参数过滤器（`UnknownArgumentFilter`）在调用工具之前检查 schema 中的 `required`，缺少或为 `null` 时与未知参数一起报告为 `INVALID_ARGUMENT`，请求不会到达 SDK 的这段逻辑。
- Claude 桌面版自带的 Claude Code 可执行文件在无界面模式（`claude -p`）下单独运行时没有登录态（"Not logged in"），但在报错前已经完成 MCP 服务器的连接和 `tools/list`。— S4。
- 这个 `claude.exe` 在桌面版的进程中看到的位置是 `%APPDATA%\Claude\claude-code\<版本>\<构建>\claude.exe`，**包外的进程（例如用户自己打开的终端）在那里找不到它**：实际位置是包的重定向目录 `%LOCALAPPDATA%\Packages\Claude_pzs8sxrjxfjjc\LocalCache\Roaming\Claude\claude-code\…`（A6）。登录（`claude auth login`）只需做一次，两边共享登录态。— 2026-10-19 实测（Claude Code 2.1.299，登录后 `auth status` 为 `loggedIn: true`、`authMethod: claude.ai`）。

### Claude Code 无界面模式（Agent 评测的驱动方式，2026-10-19，Claude Code 2.1.299，未登录）
- **`--bare` 不读取登录态**（OAuth 和系统钥匙串），只接受 `ANTHROPIC_API_KEY` 或 `apiKeyHelper`。— [无界面模式文档](https://code.claude.com/docs/en/headless#start-faster-with-bare-mode)。
- `--max-turns` 可用，但 `claude --help` 不列出它（"`--help` does not list every flag"）。— [CLI 参考](https://code.claude.com/docs/en/cli-reference)。
- `claude -p --restricted --tools "" --disable-slash-commands --strict-mcp-config --mcp-config <只含 eplanmcp>` 的 `system/init` 事件中：`tools` 只有 `mcp__eplan__*`，`slash_commands` 和 `skills` 为空，`mcp_servers` 为 `[{"name": "eplan", "status": "connected", "source": "dynamic"}]`；调试日志（`--debug-file`）记录 "project memory is off"，且不读取工作目录的 `.claude/settings.local.json`（不加 `--restricted` 时会读）。`plugins` 中仍有 3 个内置插件（`cc-plugin-agents-md` 等），不带技能。加上 `--json-schema` 后 `tools` 多出内置的 `StructuredOutput`：模型通过它提交结构化答案，统计工具调用时不能算它。上面这组开关加 `--allowedTools mcp__eplan`、`--permission-mode dontAsk`、`--permission-prompts none`、`--max-turns` 都被接受，以 `-` 开头的提示从标准输入传入也不会被当成开关。— 实测，工作目录分别在仓库内外。
- 评测运行器（`ClaudeRunner`，用 `ProcessStartInfo.ArgumentList` 传参，空字符串作为 `--tools` 的值）以同样的参数启动 claude 2.1.299，`system/init` 与上一条相同（`StructuredOutput` 加 eplanmcp 的工具，`skills` 和 `slash_commands` 为空，`permissionMode: dontAsk`），stderr 为空。— 2026-10-19 实测（未登录）。
- 未登录时，stream-json 依次输出 `system/init`、一条 `assistant` 消息（`error: "authentication_failed"`、`is_api_error_message: true`、文字 "Not logged in · Please run /login"）和 `result` 事件，退出码 1。**`result` 的 `subtype` 仍是 `"success"`**，失败要看 `is_error: true` 和 `terminal_reason: "api_error"`；`total_cost_usd` 为 0，`usage` 含 `input_tokens`、`output_tokens`、`cache_read_input_tokens`、`cache_creation_input_tokens`。— 实测。
- **第一次真实运行确认的 stream-json 格式**（2026-10-19，Claude Code 2.1.299，`claude-sonnet-5-5`，已登录 Max 套餐）：
  - 工具调用是 `assistant` 消息 `message.content` 中的 `{"type": "tool_use", "id": "toolu_…", "name": "mcp__eplan__eplan_project_get", "input": {…}}`；工具结果是 `user` 消息中的 `{"type": "tool_result", "tool_use_id": "toolu_…", "content": "<文本>"}`，成功时没有 `is_error` 字段，`content` 是字符串。两者的 `parent_tool_use_id` 都是 null。
  - 结构化答案：模型常先用文字作答，Claude Code 随后插入一条合成的 `user` 消息（`isSynthetic: true`，文字为 "[structured-output-enforce] You MUST call the StructuredOutput tool…"），模型再调用 `StructuredOutput`，其 `input` 就是答案；`result` 事件的 `structured_output` 与之相同。所以 1 次工具调用的题目 `num_turns` 为 4。
  - 还会出现 `rate_limit_event` 和 `system`/`post_turn_summary` 两种事件，与答题无关。
  - 提示从标准输入传入时被当作题目读入（模型回答了"项目一共有 321 页"）。
  - `total_cost_usd` 在套餐登录时也会给出（客户端估算）；一道 1 次调用的题约 0.01–0.05 美元，输入 token 中位数约 2.4 万（几乎全部是缓存读取）。
  - — 实测（`.local/evals/` 中的轨迹，不提交）。

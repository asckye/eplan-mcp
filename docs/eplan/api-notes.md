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
  - 2026-10-21（Electric P8，`install-addin` 实机核对）：`EplanProductVariant` 的值是 `Eplan Demo System`（授权的名称），**不是**变体名或变体目录名（`Electric P8`），所以从这里判断不出运行中的 EPLAN 是哪个变体。`install-addin` 因此在任何 EPLAN 运行时都拒绝；Bridge 在命令行没有 `/Variant:` 时也用这个值作变体，那时报告的变体同样不对。— 读取注册表。
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

### A22. 离线程序（spike S15，2026-10-20，EPLAN 2026.0.3.25421，Electric P8）
详细数据见 [spike S15](../../spikes/S15-offline-host/README.md)。开发机，当前用户的交互桌面，界面中的 EPLAN 同时运行。
- `EplApplication.Init(string.Empty, false, false)`（不允许授权和登录对话框）在开发机上 2.0–2.3 秒内成功，不弹出任何窗口；`QuietMode` 为 `ShowNoDialogs`。— S15。
- `EplanBinFolder` 和 `AssemblyResolver.SetEplanBinPath` 要用**变体的** Bin（`…\Electric P8\2026.0.3\Bin`，有 `W3u.exe`）；给 Platform 的 Bin 时 `Init` 抛出 `ArgumentException`："The Variant in '…' is not valid."。`AssemblyResolver` 只解析 EPLAN 自己的程序集，Platform Bin 中的 `Newtonsoft.Json` 要自己处理 `AssemblyResolve`。— S15。
- 授权与插件中看到的相同（3214 个选项中 2683 个可用，含 `APIExtension`）；`EplApplication.License` 的字符串是"Eplan Demo System"，但修改照常保存。未签名的离线程序读写 321 页的项目没有限制。— S15。
- 与界面中的 EPLAN 同时运行互不影响；`SafetyPoint` 回滚、`UndoStep` 建在外面时回滚静默失效，都与插件中相同（A7）。提交后 `UndoStep.DoUndo()` 不撤销。— S15。
- 为本用户注册的插件会被影子复制进离线进程，但不调用 `OnInitGui`，所以 EplanMcp 的 Bridge 不启动。`Init` 把线程的区域性改为界面的区域性。— S15。
- 未验证：另一个 Windows 用户、服务上下文、网络授权或运行时授权下的行为。

### A23. 插件注册（spike S14，2026-10-21，EPLAN 2026.0.3.25421，Electric P8，非提升权限）
详细数据见 [spike S14](../../spikes/S14-addin-registration/README.md)。
- 注册保存在工作站设置 `C:\Users\Public\Eplan\Settings\<变体>\<版本>\x64\Cfg\STATION.<…>.xdb\Setting.eod`（键 `EplanEplApiModule.Module.<程序集名，点换成 @>.*`），**按机器而不是按用户**；目录对 `INTERACTIVE`、`SERVICE`、`BATCH` 可写。不在 MSIX 会重定向的目录中。— S14。
- 命令行 `EplApiModuleAction /register` 总是启动一个新的 EPLAN 实例（已有实例在运行时也是），注册在新实例中生效并立即加载，新实例不会自己退出；它没有项目，关闭窗口即正常退出。— S14。
- `/unregister` 之后插件不再可用（S085021），但工作站设置中它的键和 `AssemblyPath` 仍然保留。— S14。
- 在原路径替换插件 DLL 后重启 EPLAN，加载的就是新 DLL，不需要重新注册。— S14。
- 🔴 带 Internet 区域标记（`Zone.Identifier`）的插件 DLL 加载失败：S043005，`FileLoadException` 0x80131515 / `NotSupportedException`（loadFromRemoteSources）；影子副本连同标记一起复制。— S14。
- 变体的程序目录是 `<Program Files>\EPLAN\<变体>\<版本>\Bin`（其中有 `W3u.exe`），版本目录名与 `Platform\<版本>` 相同，变体目录名就是 `/Variant:` 的值：开发机上有 `Electric P8\2026.0.3\Bin\W3u.exe` 和 `Pro Panel\2026.0.3\Bin\W3u.exe`；`Electric P8 Data` 等目录中没有这一层。`install-addin` 按它找出已安装的变体。— 2026-10-21 只读查看开发机的 `C:\Program Files\EPLAN`；A22。
- `install-addin` 实机核对（2026-10-21，EPLAN 2026.0.3.25421 Electric P8）：
  - 命令行注册启动的 `EPLAN.exe` 进程就是加载插件、开管道的那个实例。没有注册过时，从启动到管道出现、注册生效约 27 秒（含关闭）。
  - 🔴 插件已注册在路径 A 时，它在 EPLAN 启动早期就从 A 加载；同一次运行中 `/register` 路径 B **没有生效**，工作站设置中仍是 A（把 A 改名后启动 EPLAN，系统消息 S043005"加载插件 <A> 时出错"）。所以管道不能证明这一次的注册。
  - 撤销时插件也是先加载、再执行 `/unregister`：管道出现，之后撤销生效，再启动的 EPLAN 中没有插件。
  - 关闭请求（`CloseMainWindow`）起初没有生效，原因不在 EPLAN：.NET 的 `Process` 缓存第一次找到的主窗口句柄，EPLAN 启动早期找到的是启动画面，之后用同一个 `Process` 对象发出的请求都发给了它。第二轮核对时 EPLAN 没有任何对话框、一直在响应，新取一个 `Process` 对象调用一次 `CloseMainWindow()` 就立即退出。所以每次请求之前都要 `Refresh()`。
  - 插件加载失败或 `/unregister` 出错时，EPLAN 弹出"系统消息"窗口（`#32770`），它挡住关闭请求。
  - 工作站设置 `Setting.eod` 中 `AssemblyPath` 的值经 zlib 压缩（`78 DA`）后分页保存，不是文本，按文本找不到路径；不要解析这个文件。
  — 实机核对（按 #287 的核对步骤，两轮）。

### A52. 连接符号、自动连线与生成连接（spike S40，2026-10-27，EPLAN 2026.0.3.25421，Electric P8，只在 scratch 副本上）
详细数据见 [spike S40](../../spikes/S40-connection-symbols/README.md)。

- 项目符号库经 `Project.SymbolLibraries` 只读枚举（不 Initialize、不 Close、不 Dispose），不改项目文件和设置。SPECIAL 库中：角连接 `CO`（Routing，4 个变体），T 节点 `TLRO/TLRU/TOUL/TOUR`（TNodeUp/Down/Left/Right），电位连接点 `PCP` 等（PotentialTerminal），另有 `COST`（StrandConnectorAngle）、`CR`、`BR`、`PDP`（PotentialDefinition）等。— S40。
- 在 `SafetyPoint` 中读符号属性、对本事务新建的页调用 `LockObject` 都抛 `LockingExceptionFailedLockAttempt`（S063176）：符号发现放在写入之前，不显式锁对象。— S40。
- 连接符号用 `SymbolReference.Create(variant, page)` 放置；电位连接点返回 `PotentialDefinition`。带功能的符号仍须 `Function.Create` 与 `Transaction`（A13）。设置 `Location` 后读回与请求值相同；新页栅格 4.0。— S40。
- 对齐的连接点在提交后显示图形自动连线；错开一格不连。**逻辑连接只有在放置提交之后单独调用 `Generate.Connections` 才生成**；与放置同一事务内调用时读到 0 条。电位连接点的逻辑连接起点为空。— S40。
- 放置（Transaction）与生成（SafetyPoint）各是一个撤销步：第一次撤销只撤销生成的连接，第二次撤销恢复到放置前；`Transaction.Abort` 后快照完全恢复且不留空撤销步；关闭重开后逐字段相同。— S40。

### A51. 插入宏并应用占位符值集的提交、撤销与回滚（spike S36 第 1 部分，2026-10-27，EPLAN 2026.0.3.25421，Electric P8，只在 scratch 副本上）
详细数据见 [spike S36](../../spikes/S36-module-commit/README.md)。

- 在一个 `SafetyPoint → UndoStep → QuietModeStep(ShowNoDialogs)` 中新建页、插入 `24VDC_Supply.ema`、新建占位符并 `ApplyRecord`，提交后关闭再打开项目，全部对象、占位符值表和属性实际值逐字节不变；整个过程没有对话框。— S36 dp/fp。
- 这样的提交正好是一个撤销步骤：当前项目一次 `XEsUndoAction` 恢复写入前的全部状态（含页和项目属性），之前独立提交的哨兵页仍在，再撤销一次才删除；`ApplyRecord` 之后回滚不留撤销步骤，也不留改变。— S36 du/fu/dr/fr。
- `ApplyRecord(name)` 同时写对象、页和项目属性；`ApplyRecord(name, false)` 只写对象属性，页属性 11011 和项目属性 10901[1000] 不变。— S36。
- 布尔（20093）、普通文本（20027）、部件编号（20100[1]）都按值集生效；写部件编号后功能的部件引用随之改为新部件（数量 1）。— S36。
- 🔴 多语言属性（20011）按整体替换：值集只给 en_US 时，原有的 de_DE 被清除。生成时值集必须含全部要保留的语言，或先读出再合并。— S36。
- 🔴 对同一对象反复取得并释放属性列表后，EPLAN 在之后的终结器（`Page.Dispose(Boolean)` → `StorableObject.!StorableObject()`，`NullReferenceException`）中崩溃（0xc0000005）；每个对象只取一次属性列表、读写完后释放一次（A36）。— S36 r1。
— 协调人经 spike S36 第 1 部分实测，原始日志在 `.local` 中，不提交。

### A50. action 白名单第 2 批的回滚验证（spike S26 r3，2026-10-26，EPLAN 2026.0.3.25421，Electric P8，只在 scratch 副本上）

- `synchronize` 的 `TYPE=MULTILINE`、`SINGLELINE`、`OVERVIEW` 在 `SafetyPoint → UndoStep → QuietModeStep` 中均返回 true、不弹对话框，回滚后业务快照和文件指纹恢复；示例项目上 SINGLELINE 看不到业务变化，需另备有表示差异的样本才能证明效果。
- `SwitchProjectType PROJECTTYPE=…` 把 `Project.TypeOfProject` 从 Schematic 改为 Macro，`UndoStep` 释放后仍保持，`SafetyPoint` 回滚后恢复 Schematic，其他打开的项目不受影响。
- `GetAll<Group>()` 返回的基类包装不能直接 `Cast<Group>()`（`InvalidCastException`）；读取组要经类型化的 `Placement.Group`，无法解析的句柄记为已知缺口而不是空组。
- 经产品工具提交后，当前项目一次 `XEsUndoAction` 即恢复：MULTILINE、OVERVIEW 的功能文本指纹回到提交前，`SwitchProjectType` 的类型回到 Schematic，另一打开的 scratch 副本类型不变；之前创建的哨兵页仍在，再撤销一次即移除，说明 action 只占一个撤销步骤。三项据此以 Write 级别开放；SINGLELINE 因示例上无可观察变化仍待验证（Large `ActionRunTests`，2026-10-26）。
— 协调人经 spike S26 r3（`r2-sync`、`r2-type` 模式）实测，原始报告在 scratch 中，不提交。

### A49. 页面文字、标题栏与图框（S38 r3，2026-10-26，EPLAN 2026.0.3.25421，Electric P8，只打开 scratch 示例副本）

- 一页上的文字分布在：页面自由文本 `Graphics.Text`、属性放置、功能文本、路径文本，以及图框的 `PlotFrame.SubPlacements`（示例页 15 段自由文本中 13 段在图框里）。按页替换文字时要区分来源，图框文字不属于页面。
- 页面自由文本和多语言文字的内容可以在 SafetyPoint 中修改并回滚；提交后一次 `XEsUndoAction` 撤销即恢复（当前项目，A41）。不弹对话框。
- 页的图框：`Page.PlotFrame` 可换成项目中已有的另一种图框，读回正确、回滚恢复。页属性 11016 为空时，图框来自项目设置。
- 产品实现（`eplan_page_update` 的 `plot_frame`，2026-10-26 Large `PageUpdateFrameTests`）：`Page.PlotFrame` 在 API 目录中为只读，切换通过把页属性 11016 设为图框名完成；写入后 11016 与有效图框都读回为目标图框，其他页不变；提交后当前项目一次 `XEsUndoAction` 恢复原状态，包括 11016 原为空、图框来自项目设置的情况。可选图框只取项目各页已在使用的图框。
- 图框显示的项目属性（如 10011 `PROJ_INSTALLATIONNAME`）可修改、读回、回滚；图框中页属性的绑定描述符读不出来源/作用域（`ObjectNotCreatedException`），不能据此判断标题栏显示哪个页属性。
- 插件型 spike 在 EPLAN 中运行时，`Assembly.Location` 指向影子副本而不是构建目录；不要用它定位仓库或输出目录。
— 协调人经 spike S38（`texts`、`findpage`、`replace`、`titleblock` 模式）实测，设计文件哈希与系统消息前后核对。

### A48. FUNC_TECHNICAL_CHARACTERISTIC 20027 的运行时类型（S39 r1，2026-10-26，EPLAN 2026.0.3.25421，Electric P8，只读 sample 夹具）

- 50 个非空样本的 `PropertyDefinition.Type` 都是 `ValueWithUnit`（数值 26），都不建索引；20011 `FUNC_TEXT` 对照为 `MultilangString`。A44 的“未细分”到此确定。
- 存的是自由文本，不一定带单位：`14 W`、`16 A`、`DC 24V`、`3`、`I/O Board`、`Inverter`。`ToString()` 等于显示文本；`ToMultiLangString()` 只有一条与语言无关的值（`??_??@14 W;`）。
- **不能按数值读**：`ToDouble()`/`ToInt()` 对 `14 W`、`16 A` 返回 0，只有纯数字正确；`ToBool()`、`ToPointD()`、`ToTime()` 不抛异常但无意义。读取适配器继续只给显示文本（`opaque`），写入时按与语言无关的文本处理，写入行为留给属性写入 P7 的 Large 验证。
— 协调人经 spike S39（`PropertyDefinition.Type` 与各转换成员逐项调用，A36 生命周期纪律）实测。

### A47. 翻译库导入 `Translate.ImportDatabaseTexts`（S24 r19，2026-10-26，EPLAN 2026.0.3.25421，Electric P8，项目负责人指示在正在用的翻译库上执行）

- 签名是 `ImportDatabaseTexts(strFilename, strConverter, strReferenceLanguage)`：第三个参数是**一个参考语言**。传 `"en_US;zh_CN"` 时返回 true、耗时 0.02–0.04 秒，翻译库逐字节不变。
- 用转换器 `XTrLanguageDbXml2TabConverterImpl`、参考语言 `en_US`、表头与导出完全相同（UTF-16LE 带 BOM，21 列，最后一列 `COMMENT`）的文件导入：新词条被新增；参考原文已存在的词条，空缺的语言被补上，**已有译文不被改写**（`Motor terminals` 的 `zh_CN` 仍为“电机端子”）；同一参考原文再导入不产生重复。0.02–0.07 秒，不弹对话框，系统消息中没有记录。
- **返回值不可靠**：没有写入任何东西时也返回 true；只能用导入前后的 `ExportDatabaseTexts`（制表符约 0.7 秒、2.2 MB）比较结果。
- 只改 `$(md_translate)\Translate.db`（修改时间随写入更新）；同目录的 `translate.etd`、`TranslateReports.etd`、`TranslateTemplate.db` 不变，不产生锁文件或其他文件。关闭 EPLAN 后用文件副本替换 `Translate.db`，四个文件与测试前逐字节相同。
- 只含两列（`en_US`、`zh_CN`）表头的文件只在错误的第三个参数下试过，结果不能说明部分列文件是否可用。
— 协调人经 S24 spike（`importtranslation`、`exporttranslation` 模式）实测：先文件副本加 SHA-256、再 `backup MASTERDATA` 备份，导入测试词条，导出比较，最后关闭 EPLAN 恢复并核对哈希。

### A46. 项目打开、当前选择与导航器顺序（P25，2026-10-25，EPLAN 2026.0.3.25421，Electric P8）

- 打开名称排在当前项目**之后**的关闭项目，当前项目不变（夹具 `EPLAN_Sample_Project` 当前时，`LargeWrite`、`LargeOutside`、`P25Acceptance`、`P25o_*`、`P25t_*` 都如此）；带不带 `/Quiet` 相同。
- 重开被其他测试改过语言设置的项目时，EPLAN 可能弹出以项目名为标题的模态提示（如“字典中没有语言 'is_IS'”）并阻塞后续调用；会被其他测试改写的共用副本 `LargeWrite` 不宜在测试中关闭再重开。
- 名称排在当前项目之前的 `0P25t_<hex>` 被打开后成为当前项目。只是一次观察，不作为通用规则。
- `XPrjActionProjectClose` 关闭当前项目，随后页导航器的第一个项目成为当前项目。导航器按项目名排序（不区分大小写），同名项目按打开顺序。`eplan_project_list` 按路径排序，只能用它的 `current` 判断当前项目。
- 没有项目打开时，打开一个项目会使它成为当前项目。
- 对已经打开的项目再执行 `ProjectOpen` 会弹出“项目已经打开”模态对话框并阻塞 EPLAN；打开前先确认目标已关闭。
- 关闭项目会改写该项目的 `ProjectInfo.xml`，页内容不变；比较关闭过的项目的设计文件时要排除它。
- `ExecuteScript` 的脚本不能引用 `Eplan.EplApi.DataModel`、`Eplan.EplApi.HEServices`（CS0234）；EPLAN 自动加入 `System` 和 `Eplan.EplApi.Scripting`，重复 using 报 CS0105。编译失败时 eplremote 返回 1。
— 协调人经 `eplremote` 和 `eplan_project_list` 实测，P25 Large 的导航按此实现（见 tests/EplanMcp.Large.Tests/README.md）。

### A45. 设备改名的连带变化（LP-9，2026-10-25，EPLAN 2026.0.3.25421，Electric P8，LargeWrite scratch 副本）

- `device.update` 把 `=F+A2-KF1` 改名后，同页数据随之改变：接在它各功能上的连接的 `start`/`end` 中 function 全名（type/oid/pin 不变）、其他设备功能的 20403 `FUNC_PLCSYMBOLICADDRESS_CALCULATED`（如 `=F+A2-KF1:A1` → `=F+A2-<新>:A1`）、被改名功能自身的可见名 `shown`。
- 可见名会省略与页结构相同的段：`=F+A2-KF1` 的 `shown` 是 `=F-KF1`，不是全名的后缀。
- 计划校验只把“旧全名精确替换为新全名”和“可见名仅末段设备标识替换（结构不变）”算作改名的派生变化，其他变化仍按范围分类。
— 经 Host 的 `eplan_workflow_plan`/`eplan_plan_apply` 实跑，读取改名前后 standard 快照比对。

### A44. 通用 Function/Connection 属性读取（P4，2026-10-25，EPLAN 2026.0.3.25421，Electric P8，只读 sample fixture）

- 设备标识按精确全名选主功能（可加页限定）；功能来源是 finder 加各页放置（A12），按 type/oid 去重。同一全名有多个主功能时报 AMBIGUOUS_NAME 并给出最多 20 个候选 locator，不默认取第一个。locator 读取复用 A32/A34 的项目/type/oid/名称/页/类别/位置核对，名称变化报 PLAN_STALE（metadata `check=name`），不按名称重新解析。
- 读取权限按对象实际类型判断（`ReadRefusal(scope.ObjectType, id)`），被拒绝的 ID（3010–3013、2000、目录外、另一对象类型的 ID）不返回值、定义、显示、译文或语言。
- 样例中带两种语言 FUNC_TEXT（20011，运行时 MultilangString）的 26 个唯一主功能与带 FUNC_TECHNICAL_CHARACTERISTIC（20027）的 63 个是不同的设备，没有一个两者都有。20011 的全部存储译文、显式语言读取与独立快照一致。
- **20027 的运行时类型既不是 String 也不是 MultilangString**（是 Point/Time/Variable/ValueWithUnit 之一，未细分），按 4.3 返回 `opaque`：没有类型化值，只有安全显示文本，等于快照的 ToString 读数。它进入任何写入批次之前，必须先确定确切类型与格式。
- 连接 locator 可读 31002（截面）、31004（颜色/编号）、31011（标识）的实例定义与值/显示。
- 按对象定位要枚举全项目的功能与放置（与 `EplanObjectFinder` 相同；`StorableObject.FromStringIdentifier` 未经验证）；按 S37 的枚举耗时推算 3,000 页约 2–4 s/次（未单独测），是已知的性能余量项。
- A36 门：Function（设备标识与 locator）和 Connection 同批预热 10 轮、重复 100 轮结果一致，EPLAN 工作集回落到基线容差内，设计数据 SHA-256 不变。

### A43. 用 Page.CopyTo 放大示例项目、全项目名称枚举的规模（spike S37，2026-10-25，EPLAN 2026.0.3.25421，Electric P8，在调度器中）

- `Page.CopyTo(Project, PagePropertyList, PageMacro.Enums.NumerationMode, bOverwrite: false, bDontUseProgressBar: true)` 在同一项目内复制整页内容，热身后约 80 ms/页（首次约 0.6 s）。新页名用源页 `NameParts` 复制出的 `PagePropertyList`，再改写 `DESIGNATION_PLANT` 与 `DESIGNATION_LOCATION` 得到；只改 `=` 时，按位置命名的设备（如 `+A1-WE1`）保持原名，会与源页设备同名合并。5 种 NumerationMode（Ignore、None、Number、NumberWithQuestionMark、NumberPreferPrefix）都不改这些名称；同时改 `+` 后只剩无名称的 `+` / `=+` 功能和显式定位的电缆共用名称。已有功能从未被改变。
- 页名部分为空时（如 `&AAA1/1` 没有 `=`），`PropertyValue.ToString()` 抛 `EmptyPropertyException`；先判断 `IsEmpty`。
- 流体页（示例中的 `&PFB`，36 页）的 CopyTo 抛 `InvalidHandleException`（“可能已删除对象”），推测与授权有关，未核实。
- 在 SafetyPoint 中复制、未提交的批次回滚后完全消失，计数与批前一致；提交的 1,000 页副本正常退出、重启重开后计数一致。
- 同名同版本的插件程序集不会在同一 EPLAN 进程内重新加载：注销再注册仍执行旧代码，须重启 EPLAN 或换程序集名（与 S8 的做法一致）。
- 规模（示例派生，约 17 standard 对象/页）：1,000 页 6,141 功能、133,228 放置、265 MB；3,000 页 24,572 功能、395,684 放置、464 MB；8,800 页 77,271 功能、1,161,136 放置、62,201 个不同功能名、1.04 GB。finder 名称元数据 + 全部放置的合并枚举约 0.4–0.5 s（1,000 页）、2.2 s（3,000 页）、7.5 s（8,800 页，GetPlacements 5.7 s），随页数线性增长，所以万页项目不能在每步 guard（10 s）里做全量枚举（大项目计划设计 4.1）。
- 观察（原因未查）：同一 EPLAN 中打开多个项目并经 Bridge 读写后，`eplan_project_list` 报告的当前项目会变成另一个打开的项目（两次都是 LargeWrite）；需要固定当前项目的测试应只打开该项目。

### A42. 通用 Project/Page 属性读取（P3b，2026-10-25）

- 实机：EPLAN 2026.0.3（Electric P8），只读的 sample fixture，2026-10-25 跑 `PropertyReadTests`、`PropertyReadLifetimeTests`。`UniversalPropertyList.Property(AnyPropertyId[, index])` 在 C# 中写作 `list[id]` / `list[id, index]`；数字 ID 隐式转换成可释放的 `AnyPropertyId`。对象只取得一次 Properties，所有 ID、父值/索引值、定义、边界和多语言值分别释放；finder 返回的 Project/Page 是借用对象，不释放（A36/A39）。
- `PropertyDefinition` 的 Type、IsReadOnly、IsIndexed、MaxIndex、IsNamePart、IsInternal、IsPropertyLicensed 在实例上可读。样例页的定义：11005 Bool、11006 Long、11015/11071/25101 String、11031/11032 MultilangString、11049 Bool 且 IsReadOnly、11051 Double、25020 String 且 IsReadOnly；均 IsInternal=false、IsPropertyLicensed=true。IsComputed、长度、单位/格式没有可读的定义事实，保持 unknown；不能由只读性推断 computed。
- 编译核对 `ToBool`、`ToInt`、`ToDouble`、`ToString`。Time 的时区、ValueWithUnit 的单位/格式、枚举解释未核实，返回 opaque 的安全 display，不猜测或按区域解析。
- 空与不适用：上面 10 个页属性在样例页上都存在（有值或为空），读不到“运行时不适用”的样本；`not_applicable` 只在 `list[id]` 抛 PropertyNotFoundException 时出现，不把空值说成不适用。目录外或被分类排除的 ID 在读取前就是 `read_refused`，不到达 EPLAN。
- 索引：项目 10901（PROJ_SUPPLEMENTARYFIELD）在样例上有已存索引，枚举结果与逐个显式读回一致。页 11901（PAGE_SUPPLEMENTARYFIELD）IsIndexed=true、MultilangString，样例页上没有任何已存索引，读作一行 `empty`、不带索引号。
- 多语言：`MultiLangString.GetLanguageList(ref LanguageList)`、`LanguageList.get_Language(int)`、`GetString` 可用；LanguageList 继承 ArrayList，不需释放，转换用的 ISOCode 逐个释放。指定语言只返回该语言（缺失不借用其他译文）；省略语言时按 GUI 语言并报告实际回退（样例中能找到回退样本）。
- A36 门：同一批 Project/Page 属性预热 10 轮后重复 200 轮，结果逐轮相同，EPLAN 工作集回落到基线容差内（max(32 MiB, 5%)），`.elk` 与 `.edb` 设计数据 SHA-256 不变（按 A17/A35 排除 User.* 与 .lck）。

### A41. 提交后的撤销粒度、备份副作用、宏读句柄（spike S35 第 1 部分，2026-10-23，EPLAN 2026.0.3.25421，Electric P8，经 Host 的真实工具）
详细数据见 [spike S35](../../spikes/S35-plan-step-undo/README.md#结果)。全部写入在 scratch 副本中。

- **现有写工具每次调用提交后正好一个撤销步骤**：`XEsUndoAction` 按相反顺序依次撤销删页、页重编号、改设备标识、插入宏、改页描述、一次新建两页、新建一页，之前的撤销历史完整。Destructive 工具先做的备份不增加撤销步骤。没有改变（`not_started`）的设备重编号和被回滚的插入宏，都不留撤销步骤。
- 🔴 **撤销只作用于 EPLAN 的当前项目**：当前项目没有可撤销的步骤时，弹出模态对话框"'撤销'冲突"（"最近一次修改未能撤销：无法执行'撤销'步骤，步骤不存在"），其他项目中的修改不被撤销；对话框关闭前，所有调用返回 `EPLAN_BUSY`（ModalDialog）。执行撤销前先核对当前项目。
- 用 `XPrjActionProjectClose` 依次关闭当前项目，可以让剩下的项目成为当前项目（A11）。只剩一个项目时，用 `ProjectOpen` 打开另一个，它成为当前项目（只观察了一次）。
- Destructive 工具的备份不改变当前项目和打开的项目，副本不在 EPLAN 中打开。约 325 页的项目上，一次 Destructive 调用（脚本审批、`CopyProject` Snapshot 加页数核对、写入）总共 1.5–2.9 秒。
- 另一个进程以 `FileAccess.Read`、`FileShare.Read` 持有 `.ema` 时，`eplan_macro_insert` 照常提交（28 个对象，69–82 ms）；同时以写方式打开它失败（共享冲突，`0x80070020`）。
- 每次 `XEsUndoAction` 经 Remote Client 约 2.3 秒；Remote Client 按计算机名连接失败，按 IPv4 可以连接。

### A40. 长尾 action 的首批白名单（spike S26，2026-10-22，EPLAN 2026.0.3.25421，Electric P8，在调度器中）
详细数据与报告目录见 [spike S26 r1](../../spikes/S26-action-run/README.md#结果)。版本、变体由运行者提供；结果依据六模式最终 JSON，当前项目为 fixture，显式目标为 LargeWrite，均为已打开的 scratch 副本。

- **进程内 typed context 保留合法特殊字符路径**：`graphicallayertable EXPORT` 的 EXPORTFILE 带 `/` 分隔符、空格、单引号、中文，contextReadBack 精确，178.01 ms 返回 true，实际生成 `slash 子目录\空 格 '单引号' 中文.elc`，exactProducedPath=true。双引号候选 context 仍精确，但返回 false、0 文件，系统消息“路径中具有非法字符。”；Windows 禁止字面双引号文件名，这部分仍不确定，不能据此判断 action 二次解析。— S26。
- **`changelayer` 在 SafetyPoint 与内部 UndoStep 中可回滚**：`PROJECTNAME=<LargeWrite>, LAYER1=EPLAN100, VISIBLE1=0` 在 SafetyPoint 对照（20.09 ms）与 Bridge 包装（4.85 ms）都返回 true，visible 从 true 变 false，Rollback 后恢复 true；Bridge 的 CloseUndo=true，未见冲突。独立候选 16.99 ms，同样恢复图层状态。没有提交，提交后单次 Ctrl+Z 未测；公开撤销历史不可枚举。— S26。
- **计数未变不等于没有变化，回滚不等于恢复原字节**：changelayer 后目标图层和文件内容指纹变化，回滚后图层恢复，Layer.eod/Layer.eox 哈希仍不同；另一打开项目的可比较指纹未变。省略 PROJECTNAME 的 runner (b) 因目标不是当前项目而未运行，当前项目隔离问题尚无该探针的实证。— S26。
- **六种 `partsmanagementapi EXPORT*` 需要属性过滤**：EXPORTPARTS、EXPORTMANUFACTURERS、EXPORTCONSTRUCTIONS、EXPORTCONNECTIONPATTERNS、EXPORTACCESSORYLISTS、EXPORTACCESSORYPLACEMENTS，FORMAT=XPamExportXml；不带 PROPERTYID1/PROPERTYVALUE1 全部返回 false、0 文件，约 1–7 ms，每次新增“输入字符串的格式不正确。”。分别用字典关键属性 22001、22900、22931、22945、22960、22970 加已有记录值后，全部返回 true、各一个非空 XML，约 16–216 ms，无新增系统消息。不能把无过滤用法当作导出全部。— S26。
- **图层表、部件库和属性定义/方案导出未改变可观测项目指纹**：成功的 graphicallayertable EXPORT 约 12–178 ms；projectmanagement EXPORTPROPERTYPLACEMENTSSCHEMAS 为 93.24 ms；XEsUserPropertiesExportAction 用 XMLFile、Project 为 112.14 ms。它们及六种带过滤的 partsmanagementapi 导出均各一个文件，两个项目相对候选 before 的计数与文件指纹未变；parts 候选前后配置库、六类记录数、包含及排除 User.* 的内容摘要也未变。— S26。
- **PLC 总线导出成功，但 AML 的只读副作用尚不能排除**：Phoenix1、OVERWRITE=0、LANGUAGE=de_DE；PlcDcXMLExchangerUniversal、PlcDcAMLExchangerGeneral（省略 FORMAT / AutomationML AR APC V1.4.0）分别返回 true，耗时 3,990.10 / 2,956.56 / 3,151.14 ms，输出 431,387 / 95,787 / 104,341 字节，无新增系统消息。业务计数均未变，标准格式文件指纹未变；两个 AML 候选的 LargeWrite 内容摘要变化，排除 User.* 后仍变化，回滚后未恢复，fixture 未变。报告列出 Blob/Function 文件差异；不能概括为所有导出都不改项目，需继续核实原因。— S26。
- **返回后窗口启用不能证明执行期间无对话框**：所有实际调用返回后和收尾窗口禁用标志均为 false，没有 action/回滚/UndoStep 清理异常；但 EXPORTPARTS 的有过滤候选及三个 PLC 导出的执行期禁用采样为 true。dialogAppeared、engineerDialogObservation 均为空，不能从 JSON 单独确认弹窗与否；两个外部编辑方案发现另记录缺节点的 ArgumentException。— S26。
- **三种原首批用法未运行，按 ADR-0021 Q2 移到“以后”**：EXPORTADDRESSOVERVIEW 缺可匹配的配置项目/站/CPU 元组（样本 CPU 读数为空）；XMActionDCCommonExport、XMExportDCArticleDataAction 没有提供已核实方案节点，未能读出方案名称。缺 CONFIGSCHEME 会弹窗来自 api-actions 7.3 的既有约束，本轮未触发。r2 需完整 PLC 元组及两个导出各自可读的方案，仍固定 EXECUTIONMODE=0、IMMEDIATEIMPORT=0。— S26。

### A39. M4c 剩余工具用到的 action（spike S34，2026-10-22，EPLAN 2026.0.3.25421，Electric P8，在调度器中）
详细数据见 [spike S34](../../spikes/S34-m4c-actions/README.md)。

- **`generate /TYPE:CABLES` 关闭保留旧名时会重编号已有电缆**：示例项目三个方案（创建、编号、自动选型）均只有 `Standard`；默认参数或 `KEEPOLDNAMES=1, ONLYAUTOCABLES=1` 不改变名称，项目没有自动电缆。`KEEPOLDNAMES=0, STARTVALUE=5, STEPVALUE=2, ONLYAUTOCABLES=0` 把 78 根中的 70 根改名（如 `+B1-WG2` → `+B1-W7`），没有新增或删除，约 0.2–0.7 秒；回滚恢复全部名称。工具应定为 Destructive，先备份，不能仅比较电缆数量。— S34。
- **设备清单需要非空条目才能验证导入效果**：原清单 XML 为 `<deviceList count="0"/>`，CSV 为 0 字节。`DeviceListEntry` 无公开构造函数，`DeviceService.CreateDeviceListItem(project, partNumber, variant)` 可创建条目；创建 2 条后，默认 XML、`XDLXmlExporter`、`XDLCsvImporterExporter` 均完成导出 → 删除 → 导入恢复 2 条（新对象编号）→ 回滚恢复 0 条，单次 action 约 20–150 毫秒。— S34。
- **部件清单 XML 导入可用但慢**：默认/`XPalXmlExporter` 导出约 14 MB，51–64 秒；`MODE` 省略、`1`、`2` 的导入均为 58–83 秒，部件引用 2,900 → 2,902 → 回滚恢复 2,900。EPLAN 导入时只读打开部件数据库；未使用 `IMPORTTOSYSTEM`，CSV 因 S25 的访问冲突记录而避开。工具需要分钟级长超时和时间余量。— S34。
- 🔴 **PLC 自导出再导入也不可靠匹配已有设备**：`Phoenix1` 用 `PlcDcXMLExchangerUniversal` 导出 431 KB `.pbf`，用 `PlcDcAMLExchangerGeneral` 导出 96–104 KB `.aml`（含 `AutomationML AR APC V1.4.0`），r1 约 2.3 秒。`IMPORTMATCH=0`、`2`、省略均新增对象：功能 2,876 → 3,326（标准格式，新增 450）/ 2,913（AML，新增 37），PLC 盒 20 → 34 / 33；标准格式 `0`/省略的 450 个全名全部重复，AML 有 36 个重复，包括 `=F+A2-KE1`。标准格式 `2` 改名新增并记录“标记一个或多个对象以将其删除.”；`1` 会走比较对话框，始终未使用。只能限制在尚不含这些设备的目标项目，不能当作已有设备的静默更新。— S34。
- **PDF 注释仍缺有效批注样本**：未修改的 EPLAN 自导出 PDF 导入返回 `true`，注释仍为 0；pypdf 添加注释后，全量重写和保留原字节、trailer `/ID`、EPLAN Info 键（`/ProjectId`、`/PageOffset` 等）的增量更新均返回 `false`，系统消息为“PDF 文件无法由 Eplan 生成并因此不能导入.”。需取得 Adobe Reader / Acrobat 批注 PDF 并验证后再写工具规格。— S34。
- 完成执行的探针均在 `SafetyPoint` 内，显式 `Rollback()` 恢复可观测的名称、条目和业务计数，未出现对话框，主窗口未禁用；这不保证 `.edb` 逐字节恢复，r3 电缆模式结束仍有 18 个文件哈希不同，也不能推广到进程崩溃时的恢复。— S34。
- 🔴 **不要释放 EPLAN 返回的 `StorableObject`**（finder、属性或集合返回的 `Function`、PLC、`DeviceListEntry` 等），工厂创建的项目条目也交给回滚，不调用 `Dispose`。r2 在约 5 秒内发生原生堆损坏 `0xc0000374`（`ntdll.dll`），无 .NET 异常、无报告；r3 只释放辅助对象（`PropertyValue`、一次获取并释放的属性列表、finder、过滤器、context、服务等），探针完成。不能把所有 `IDisposable` 的数据模型对象都包进 `using`。— S34。

### A38. EPLAN 事件（spike S33，2026-10-21，EPLAN 2026.0.3.25421，Electric P8，在调度器中）
详细数据见 [spike S33](../../spikes/S33-events/README.md)。
- `new EventHandler(名称)` 加 `EplanNameEvent` 订阅事件；通配符 `*` 收到全部事件及其名称，`onActionStart.String.*`、`onActionEnd.String.*` 也有效。事件都在主线程上、在操作内部同步触发。— S33。
- 修改项目时触发 `onDMObjectsAltered`、`onPreCommitTxn`、`onPreCloseUndo`，被 `SafetyPoint` 回滚的修改也触发；参数不是字符串（类型未查明），不说明改了哪里。打开项目触发 `API.PreOpenProject`、`Eplan.EplApi.OnPostOpenProject`（参数为项目路径）、`onPostOpenProject` 等，关闭触发 `onPreCloseProject`、`onPostCloseProject`。`Page.ConnectionDirty` 既不是事件也不是 API 成员（只有 `ChangeInfoService.ConnectionDirtyActive`）。— S33。
- `ProjectManager.OpenProjects` 要在 `LockingStep` 中调用，否则抛出 `NoLockingStepException`（S063110）。— S33。

### A37. 模块写回为宏、新建占位符、授权选项（spike S32，2026-10-21，EPLAN 2026.0.3.25421，Electric P8，在调度器中）
详细数据见 [spike S32](../../spikes/S32-macro-create/README.md)。写入只在 scratch 副本上，在 `SafetyPoint` 中回滚。
- `WindowMacro.Create(路径, MultiLine, 0, page.AllPlacements, 描述)` 把一页的 201 个放置写成 `.ema`（220 KB），`PageMacro.Create(路径, [page], 描述)` 写成 `.emp`；重新打开和再插入时，占位符、值集、变量、引用都保留。写出的宏文件记下 `SourceProject="<来源项目的完整路径>"`（含本机用户名）。— S32。
- 新建参数化占位符：`new PlaceHolder().Create(page)`、`AddVariable`、`AddReference`、`SetPropertyEntry(对象, 属性, "<变量名>")`、`AddRecord`、`SetValue`，之后 `ApplyRecord` 把值写进被引用对象的属性。🔴 属性条目的值必须写成尖括号中的变量名，写 `V1` 会被当作字面文本。宏文件中占位符为 `O106`，属性条目为 `S115x1904`（`A1911` 属性号，`A1913` 值，如 `??_??@ <Task>;`）。— S32。
- `License.GetLicenseModules(ref Dictionary<LicenseOptions, bool>)` 一次读出全部 3,214 个授权选项；开发机（演示授权）2,683 个可用，5 个 Cogineer 选项都可用，但安装目录中没有 Cogineer 的程序集。`HasOption` 只读；`LockOption` 会占用授权直到 EPLAN 退出，不要调用。— S32。

### A36. 快照提取：耗时、内存、确定性与修改标记（spike S31，2026-10-21，EPLAN 2026.0.3.25421，Electric P8，在调度器中）
详细数据见 [spike S31](../../spikes/S31-snapshot/README.md)。
- 🔴 `UniversalPropertyList.ExistingIds`/`ExistingValues` 和属性访问器返回的 `AnyPropertyId`、`PropertyValue` 都是 `IDisposable`，批量读取时必须逐个释放：不释放时读两遍示例项目全部属性（共约 1,000 万个值），EPLAN 记下"存储空间不足"后退出。释放后工作集在调用结束时回落。— S31。
- 🔴 不要在释放 `对象.Properties` 返回的属性列表之后再读同一对象的 `Properties`：2026-10-22 的快照代码先 `using (f.Properties)` 读一组属性，再对同一功能取 `f.Properties` 读 `FUNC_PLCADDRESS`，EPLAN 在 `UniversalPropertyList.getProp` 中访问冲突后退出（无法捕获）。每个对象只取一次属性列表，读完后释放一次。— M6 快照的 Large 测试。
- 属性的类型化访问器（如 `FunctionPropertyList.FUNC_PLCADDRESS`）在对象没有这个属性时抛 `PropertyNotFoundException`（S063109 属性无效），例如普通端子上的 PLC 属性；按对象逐个读登记属性时要逐个捕获它，当作没有值。— 2026-10-22，M6 快照的 Large 测试。
- 读示例项目每个页、功能、连接的全部已有属性：514 万个值，约 8–10 分钟，150 MB 文本，最慢的一页约 50 秒；只读页属性加功能全名和连接两端：约 5 秒。`UniversalPropertyList` 的构造函数不公开，用各对象的 `Properties`（派生自它）。`Connection` 不是 `Placement`，不在 `Page.AllPlacements` 中，用 `Connection.Page` 归页。— S31。
- 同一次打开中两次提取逐字节相同；关闭再打开后，值为运行时字符串标识的属性（如 2000：`53/4/21/0` → `89/4/21/0`）随库号改变。只读提取不改写项目 `.edb` 中的文件（不含 `.lck`）。— S31。
- 改一页的描述并提交后，`PROJ_LASTMODIFICATIONDATE`（Unix 秒数）/`TIME` 和这一页的 `PAGE_LASTAUTOMODIFICATIONDATE`/`TIME` 改变，其他页不变，`PAGE_LASTMANUMODIFICATIONDATE` 不变；这一页的 `PROPUSER_LAST_USERCODE`（3010）同时被改写。3010–3013 是最后修改人的个人信息。— S31。
- 🔴 项目 `.edb` 中缺少 `User.eod`、`User.eox`（连同 `.lck`）时，EPLAN 打不开这个项目，弹出模态对话框"无法打开项目……必须修正数据库索引"，在关闭之前 EPLAN 不处理别的请求。复制、还原、打包项目都不能去掉这两个文件。— 2026-10-21，S31 之后在 scratch 副本上验证。

### A35. 工程包涉及的文件（2026-10-21，只读查看开发机上 EPLAN 2026.0.3 的数据目录和 scratch 中的 spike 文件，不启动 EPLAN）
M6 资源扫描器（`EplanMcp.Packs.Scanning`）不靠 EPLAN 读出的内容以这里为准。
- **扩展名的来源**：API 文档写明的有 `.zw1`（`backup /TYPE:PROJECT` 的归档）、`.fn1`（图框，`MDTYPE:STANDARDSHEET`）、`.fc2`（轮廓定义）、`.epj`（PXF 项目）、`.emp`（示例 `ModelView.emp` 放进宏目录）、`.slk`（符号库）；`.ema` 由 S9/S10 实际插入验证；`.elk`/`.ell`/`.elr` 见 A3、A28；`.zw9`（基本项目）见 A31。`.ems`（符号宏）和表格 `.f01`–`.f99` 只在数据目录中观察到，API 文档没有写，**未经 EPLAN 验证**。
- **XML 主数据**：宏、表格、图框、轮廓定义都是 UTF-8 XML，根元素 `EplanPxfRoot`，属性 `Type` 和 `Version`（写入时的 EPLAN 版本，如 `2026.0.25356`，旧文件 `2.7.11561`）。数据目录中：`.ema` 695 个、`.ems` 50 个都是 `Type="WindowMacro"`（**符号宏文件也标为窗口宏**），`.emp` 35 个 `PageMacro`，`.f01`–`.f48` 147 个 `Form`，`.fn1` 35 个 `Frame`，`.fc2` 21 个 `ContourDefinition`。属性 `SourceProject` 是制作者机器上的项目路径，不能抄进清单。
- **链接文件**（`.elk`、`.ell`、`.elr` 格式相同）：UTF-16LE 带 BOM，第 1 行是数据目录名，第 2 行 `18.0`，与 `GetProjectDatabaseVersion` 的结果相同（A31）。复制出来的项目第 1 行仍是原名（`S12_Sample.elk` 写的是 `EPLAN_Sample_Project.edb`），EPLAN 照样打开了旁边的 `S12_Sample.edb`（S12），所以数据目录按链接文件自己的名字找。
- **`ProjectInfo.xml`**：除 A31 的 10043 外，`id="10088"`（"Internal: Highest used Eplan version"）是四段版本，示例项目为 `2026.0.1.25370`（10043 为 `2026.0.1`）。用 2026.0.3 的 API 写过的副本（`LargeWrite`）两项仍是 2026.0.1：写入不一定更新它们。
- **7z 归档的内容**（7-Zip 26.03 只读列出）：`.zw1` 中是 `.edb` 的文件，另有 `A_BAKINFO.XML`（`<prodversion>2026.0.3.25421</prodversion>`、`<appvariant>`、备份注释）、`_COMMENT.TXT`、`_elkfile.elk`；`.zw9` 另有 `_BAKINFO.XML`。两者都**不含 `.lck` 锁文件**（S12 的数据目录 313 个文件中 109 个是 `.lck` 或 `User.*`），但含项目和各子库（`.sdb`、`.fdb`）的 `User.eod`/`User.eox`。所以工程包的项目资源不带 `.lck`；`User.eod`/`User.eox` 随项目带上但不计入哈希，因为 EPLAN 打开、导出、关闭时都会改写它们（A16、A25、A30）。文件头 `37 7A BC AF 27 1C 00 04`（7z 格式 0.4）。读取其中的版本需要 7z 解码（LZMA2），.NET 没有内置。
- 🔴 **复制项目时不能丢掉 `User.eod`/`User.eox`**：`.edb` 中缺少它们（以及 `.lck`）的项目副本打不开，EPLAN 弹出"无法打开项目 … 必须修正数据库索引"的模态对话框（`#32770`），在工程师关闭它之前挡住 EPLAN。所以复制、还原项目时保留它们，打开前先核对；打开卡住或超时可能是这个对话框，不要替工程师点它。复制进工程包的项目也要带上这两个文件（它们仍不计入哈希）。— 协调人实测（2026-10-21）。

### A34. 对象编号与项目生命周期（spike S30，2026-10-21，EPLAN 2026.0.3.25421，Electric P8，在调度器中）
详细数据见 [spike S30](../../spikes/S30-identity-lifecycle/README.md)。写入只在 scratch 中。
- `PROJ_GUID` 是"名称@Unix 秒数"，不是 GUID：文件副本和 `backup`/`restore` 还原的项目沿用原值（示例项目 `ESS_Sample_Project-Kopie@1523807309`），PXF 导入和 `CreateProject` 生成新值（导入或新建的时刻）。它不能区分同一项目的副本。— S30。
- 同一份项目文件 `Close()` 后再 `OpenProject`，825 个对象的 `ObjectIdentifier` 全部不变，`DatabaseIdentifier` 改变；关闭约 0.15 秒，打开约 0.8 秒。页的编号单调递增：回滚掉的、删除的编号都没有再分配，关闭再打开后接着递增（本次观察）。— S30。
- 从同一个基本项目新建的两个项目，页的编号相同（都是 1）：`ObjectIdentifier` 只在一个项目内唯一。`IEC_bas003_sample_project.zw9` 新建的项目是 0 页。— S30。
- 🔴 API 新建的项目：对它再调用 `OpenProject` 抛出 `ProjectOpenException`（S063045）；`ProjectManager` 释放后，用 `CreateProject` 返回的对象 `Close()` 抛出 `ObjectNotCreatedException`（S063080）；调用结束后项目不再打开，之后可以正常打开，经 `OpenProjects` 重新取得的对象能关闭。— S30。
- 经 Bridge 操作 `CreateProject` 新建的项目，调用结束后仍然打开（与 S30 spike 的观察不同）；用新的 `ProjectManager` 从 `OpenProjects` 重新取得它后 `Close()` 可以关闭（S30 r3）。— 2026-10-22，M6 的 Large 测试 ProjectCreateTests。

### A33. 占位符对象与值集（spike S29，2026-10-21，EPLAN 2026.0.3.25421，Electric P8，在调度器中）
详细数据见 [spike S29](../../spikes/S29-placeholders/README.md)。写入只在 scratch 副本上、在 `SafetyPoint` 中回滚。
- `WindowMacro.Open(path, project)` 后，对每种表达类型的每个变体 `ChangeCurrentVariant`，`Objects` 中的 `PlaceHolder` 能读出名称、值集（`NumberOfRecords`、`get_NameOfRecord(i)`）、变量（`VariableNames`）和值表（`get_Value(record, variable)`）。C# 中 `NameOfRecord`、`Value` 是带参数的属性，要调用访问器方法。扫描 `$(MD_MACROS)` 下 695 个宏、1,751 个变体用时 21 秒，72 个宏带占位符（38 个带值集，最多 8 个），项目页数不变。— S29。
- `DMObjectsFinder.GetPlaceholdersWithFilterScheme(null)` 列出项目中的占位符（示例项目 2 个，值集 `V`、`mA`）。— S29。
- 插入宏后 `ApplyRecord(name)` 不到 0.01 秒，`GetAllCurrentValues()` 随值集改变；`AddRecord(name)` 返回新值集的序号，`SetValue(record, variable, MultiLangString)` 写入的值能读回并应用。插入和应用都随 `SafetyPoint.Rollback()` 撤销。值以多语言字符串存储。提交后的持久性和 Ctrl+Z 未验证。— S29。
- `PlaceHolder.GetAllCurrentValues()` 在占位符没有锁定时抛 `LockingException`（写存取前没有加锁该对象），只读操作中不能调用；在 `SafetyPoint` 中可以读（自动锁定，A14）。— 2026-10-22，写副本上的 Large 测试。

### A32. 对象标识与整个项目的读取耗时（spike S28，2026-10-21，EPLAN 2026.0.3.25421，Electric P8，在调度器中）
详细数据见 [spike S28](../../spikes/S28-identity-extract/README.md)。只读。
- 同一项目的两份文件副本中，825 个对象（全部主功能和页）的 `StorableObject.ObjectIdentifier` 全部相同；`DatabaseIdentifier` 按打开的项目分配（28、53）；`ToStringIdentifier()` 为 `数据库号/类型号/对象号/0`。可以用"项目身份 + 类型号 + `ObjectIdentifier`"跨副本定位；编号可能在删除后被重用，执行前要核对名称、页、类型。`backup`/`restore /TYPE:PROJECT` 还原出的新项目、`eplan_export_pxf` + `eplan_import_pxf` 导入的新项目中，825 个对象的编号也全部相同（`DatabaseIdentifier` 不同）；从基本项目新建、在 EPLAN 中复制项目未验证。— S28 r0、r1。
- 示例项目（321 页）：`GetPages` 0.01 秒，`GetFunctions(null)` 2,876 个 0.01–0.04 秒，`GetConnections(null)` 2,060 个约 0.06 秒，`GetPlacements(null)` 44,735 个 0.6–0.8 秒；读全部功能的名称、页、坐标、编号 0.06 秒。— S28。

### A31. 新建项目、还原项目、判断项目版本（spike S27，2026-10-21，EPLAN 2026.0.3.25421，Electric P8，在调度器中）
详细数据见 [spike S27](../../spikes/S27-project-create/README.md)。全部写入都在 scratch 中。
- `ProjectManager.CreateProject(链接文件, 基本项目 .zw9)`：3.5 秒，生成 `.elk` 和 `.edb\`，新项目处于打开状态（API 独占打开），带模板的页和属性（`GB_bas001`：1 页）。— S27。
- 🔴 `restore /TYPE:PROJECT` 还原到已存在的项目时不报错，照样覆盖（89 个文件重写）；与主数据还原跳过已有文件相反（A30）。还原一个示例项目备份约 6 秒。— S27。
- `ProjectManager.GetProjectDataCompatibility(path)` / `GetProjectDatabaseVersion(path)` 不打开项目，0.02–0.3 秒；2026.0.3 的项目为 `IsCompatible`、`18.0`；路径不存在时抛出 `ProjectNotFoundException`（S063073）。— S27。
- `.zw9`、`.zw1` 是 7z 归档。项目 `.edb\ProjectInfo.xml` 是项目属性快照，`id="10043"` 为最新更改或更新的 EPLAN 版本（`2026.0.3`），按 `id` 取（`name` 随界面语言变化）；其中有创建者的个人信息，只取需要的字段。— S27。

### A30. 主数据的备份、还原与部件库写入（spike S24 写入部分，2026-10-21，EPLAN 2026.0.3.25421，Electric P8，在调度器中）
详细数据见 [spike S24](../../spikes/S24-master-data/README.md)。写入只在还原到 scratch 中的副本上进行，正在用的库前后 SHA-256 不变。
- `new ActionManager().FindAction("backup").Execute(context)`，参数 `TYPE=MASTERDATA`、`MDTYPE=ARTICLES|LANGUAGES`、`SOURCEPATH`/`FILENAME`（由配置的库路径拆出，如 `$(MD_PARTS)` + `EPLAN_parts.alk`，变量由 EPLAN 展开）、`DESTINATIONPATH`、`ARCHIVENAME`：返回 true，0.14 / 0.23 秒。得到 7z 格式的 `.zw6`（部件库，991 KB，不含 `.lck` 和 `EMPTY_SQL_Backups\`）、`.zw7`（翻译库，912 KB，只含 `Translate.db`）。— S24。
- 经 Remote Client 命令行传入含中文的路径会变成乱码（`S025033 源目录不存在`）；进程内传参不受影响。— S24。
- 🔴 `restore /TYPE:MASTERDATA` 不覆盖已有文件：目标中已有同名文件时跳过，照样返回 true，不记日志。还原到空目录得到逐字节相同的文件。恢复要先把损坏的库移开（EPLAN 关闭时），再放回还原出的文件。— S24。
- `new MDPartsManagement().OpenDatabase(路径)` 打开另一个库，不改 `SelectedPartsDatabaseAsString`。— S24。
- 🔴 `MDPartsDatabase.ImportParts(文件, "XPamImportXml", …)` 在按路径打开的副本上执行时，走的是界面导入 action `XPamImportXMLAction`，同时改了**配置的那个库**的 `User.eod`/`User.eox`（一个按用户的计数），部件数据不变；放在事务中时撤不回，还留下未关闭的撤销日志，之后的导入都报"不允许套叠撤销日志"。导入、同步类的测试一律算写正在用的库。— S24 r11。
- 部件库 `.adb` 中的 `User.eod`/`User.eox` 是 EPLAN 按用户记录的会话信息（当前用户那条记录中有一个计数），EPLAN 自己会改写：2026-10-21 只关闭一次 EPLAN，计数就从 9 变为 10，两个文件都变了；一天里正常启动、关闭 EPLAN 和导出，计数从 2 涨到 9。与项目数据库中导出只改写 `User.eod`（A16、A25）是同一类。判断部件库"有没有被改"时，与 `.lck` 一样排除这两个文件。S24 r11 中导入后这两个文件的变化也是这类记录，部件数据没有变。— 2026-10-21 实机。
- 不在事务中、在 `OpenDatabase()` 打开的配置的库上 `ImportParts`：0.2 秒，正常导入，`AppendNewRecords` 重复导入不重复添加。部件 XML：根元素 `partsmanagement`（`count`、源库路径 `database`），每个部件一个 `part`，部件号在 `P_ARTICLE_PARTNR`。— S24 r13（经批准，已恢复）。
- 🔴 `Synchronize.Parts(project, StoreMode)` 要在 `LockingStep` 中（否则 `NoLockingStepException`），并且**同时写项目**：被引用但没有存进项目的部件会存入项目（夹具 293 → 341），系统消息 `S328014`"写存取前没有加锁该对象"。— S24 r15（经批准，已恢复）。
- `MDPartsDatabase.ExportParts(文件, DataFormat.XML)` 464 个部件 2 秒、5 MB；`EDZ` 44 秒、128 MB。`Translate.ExportDatabaseTexts(文件, "XTrLanguageDbXmlConverterImpl" | "XTrLanguageDbXml2TabConverterImpl")` 不到 1 秒。导出不改库。转换器名：部件库 `XPamImportXml`、`XPamExportXml`。— S24 r11。
- `MDPartsDatabase` 的事务：`AddPart` 提交前 `ExistsPart` 为 false，`Rollback` 撤回、`Commit` 落盘；🔴 `RemovePart` 立即生效，`Rollback` 不能恢复。部件已存在时 `AddPart` 抛出 `MDInvalidArgumentException`（`S030025`）。事务不释放时，下一次 `CreateTransaction` 抛出 `SEHException`，要用 `using`。— S24。
- 部件 XML 中的变体：`P_ARTICLE_VARIANT` 不在 `part` 上，而在 `part` 下的 `variant` 子元素上，每个变体一个。S24 r11 导出的示例库全部部件：430 个 `part`、464 个 `variant`，`Parts` 也是 464 个（按变体计数）。根元素下另有 `propertydefinition` 等元素，不是部件；部分部件带 `P_ARTICLE_UNIQUEID`。— 2026-10-21，查看 S24 r11 的导出文件。
- 第一次经批准的 Large 测试运行（`PartImportTests`、`PartSyncTests`，EPLAN 2026.0.3.25421，Electric P8，配置的文件型库 `$(MD_PARTS)\EPLAN_parts.alk`，新复制的写副本 `LargeWrite`）：两个测试在一次运行中都通过，合计约 11 秒。— Large 测试 2026-10-21。
- 主数据备份的 `backup` action 在 `QuietModeStep(ShowNoDialogs)` 中照常生成归档（测试断言 `.zw6` 非空）：静默模式不阻止它。— Large 测试 2026-10-21。
- `ImportParts`（`AppendNewRecords`，按部件号识别）接受只有部件号、描述和部件类型的最小部件 XML：导入后 `eplan_part_search` 找到 `EPLANMCP.LARGE-TEST`，变体 1，产品组 `Common`；第二次导入时规划把它归为跳过，没有审批。— Large 测试 2026-10-21。
- `Synchronize.Parts(AppendNew)` 在写流水线的 `SafetyPoint` 和撤销步骤中提交：写副本中存储的部件 293 → 341（同 S24 r15）；两个测试之后部件库共 512 个条目；再做一次同步的 dry run 没有可做的。— Large 测试 2026-10-21。
- 运行期间 EPLAN 的系统日志（EplLog）没有出现 `S328014`（S24 r15 没有 `SafetyPoint` 时出现过），只有 EPLAN 启动时的 `S336007`。— Large 测试 2026-10-21。
- 未验证：一次 Ctrl+Z 能否撤回同步的项目一侧；测试写进测试输出的耗时和审批文本（Microsoft.Testing.Platform 即使用 `--output Detailed` 也没有打印通过的测试的 xUnit 输出）。— Large 测试 2026-10-21。
- 恢复：EPLAN 关闭时，把 28 个文件（`.alk` 和 `.adb\*`）从运行前的副本复制回去，每个文件的 SHA-256 都与运行前相同（这次 `User.*` 也相同）。— Large 测试 2026-10-21。

### A29. 导入（spike S22，2026-10-21，EPLAN 2026.0.3.25421，Electric P8，在调度器中）
详细数据见 [spike S22](../../spikes/S22-imports/README.md)。示例项目的 scratch 副本。
- `new Import().DXFPage(文件, 页, new Transformation(文件, 页, "", ScalingMode.Automatic, PositionMode.Center, false), "")`：Rittal 示例 DXF 在 0.04–0.3 秒内给页加上 227 个图形对象，能被 `SafetyPoint` 回滚，提交后一次 Ctrl+Z 撤销。— S22。
- 🔴 `Import.PXFProject(文件, 目标)` 的第二个参数是**新项目的路径（不带扩展名）**，不是 XML 所说的目标目录：`…\imported` 得到 `imported.elk` 和 `imported.edb`。示例项目的 598 MB EPJ 导入约 58 秒，得到 171 MB 的项目，导入后不自动打开；中途被结束时留下不完整的 `.edb`。— S22。
- `PartsService.ImportPartsList(project, 文件, Format.XML)`：把刚导出的同一份清单导回，69 秒，项目部件数和部件引用数都不变。— S22。

### A28. 翻译、项目语言与修订（spike S23，2026-10-21，EPLAN 2026.0.3.25421，Electric P8，在调度器中）
详细数据见 [spike S23](../../spikes/S23-translation-revisions/README.md)。示例项目的 scratch 副本。
- 项目语言在项目设置 `TRANSLATEGUI.TRANSLATE_LANGUAGES`（示例 20 种）、`DISPLAYED_LANGUAGES`（`##_##;`）、`SOURCE_LANGUAGE`（`##_##`）中。多语言文本存储为 `de_DE@…;en_US@…;`。— S23。
- `Translate.TranslateObjects([project])` 2.6–2.9 秒；示例项目已全部翻译，没有文本改变，但改写 `Project`、`Page`、`Blob` 等文件。— S23。
- `SetDisplayedLanguages` 接受 `"en_US"` 和逗号分隔的 `"en_US,zh_CN"`（存为 `en_US;zh_CN;`）；用分号分隔或带结尾分号都抛 `ArgumentException`（S029035）。设置不被 `SafetyPoint` 回滚。— S23 r0、r1。
- 只有德语的功能文本（`de_DE@Motor;`）经 `TranslateObjects` 后不变（项目源语言为 `##_##`）。— S23 r1。
- **翻译的前提是项目的源语言**：`TRANSLATEGUI.SOURCE_LANGUAGE` 为 `##_##` 时 `TranslateObjects` 不翻译、不报错；设为 `de_DE` 后，`de_DE@Motor;` 0.04 秒译进项目的全部 19 种翻译语言（`zh_CN@电机` 等，按翻译库）。源语言用 `ProjectSettings.SetStringSetting` 写，不被 `SafetyPoint` 回滚，要自己写回。— S23 r2。
- 🔴 `RemoveProjectLanguage(project, "sv_SE")` 删除该语言的全部译文、把它移出翻译语言，还把显示语言改为 `zh_CN;`。`SafetyPoint` 回滚撤回了译文，但**没有撤回语言列表和显示语言**。— S23。
- 🔴 `RemoveProjectLanguage` 删除了语言也返回 false：在不是当前项目的写副本上、`SafetyPoint` 中删除 `sv_SE`，返回 false，翻译语言中却已没有 `sv_SE`，显示语言从 `##_##;` 变为 `zh_CN;`，回滚后设置仍是删除后的样子（与 S23 一致）。结果要按读回的翻译语言判断，不能看返回值。— 2026-10-21，S24 r18。
- 🔴 `Revision.CreateRevision(project, 名称, 说明, ref 路径)` 0.04 秒，返回的路径为空，项目链接文件从 `.elk` 变为 `.ell`（修订跟踪中的项目）；Ctrl+Z 不撤回。`Project.Comparisons` 和 `Revision.GetUncompletedPages` 前后都是 0。— S23。
- 修订状态（S23 r3）：链接文件扩展名 `.elk` 普通项目、`.ell` 修订进行中（变更跟踪）、`.elr` 完成的修订（写保护，`CompleteProject` 把 `.ell` 改为 `.elr`，`PROJ_REVISIONSPARENT` 为原路径）。修订记录在项目的带索引属性 `PROJ_REVISION_LOG_NAME/DESCRIPTION/USER/DATE[i]`（i 从 1 起；`DATE` 读出为 Unix 秒数的字符串），新建修订时写第 1 条，`PROJ_REVISION_LOG_START` 为 1。用 API 改页不会产生页面的修订记录或草稿标记；从未完成项目时 `GetUncompletedPages` 为空。— S23 r3。

### A27. 部件库与翻译库的只读访问（spike S24，2026-10-21，EPLAN 2026.0.3.25421，Electric P8，在调度器中）
详细数据见 [spike S24](../../spikes/S24-master-data/README.md)。
- 开发机的部件库是 `$(MD_PARTS)\EPLAN_parts.alk`：40 字节的链接文件，指向同目录的 `EPLAN_parts.adb\`（与项目数据相同格式的目录，3.9 MB）；`MDPartsDatabase.Type` 报告 `ACCESS`。翻译库是 `$(md_translate)\Translate.db`，类型 `EPLAN`。— S24。
- `new MDPartsManagement().OpenDatabase()` 0.02 秒；`GetPart(部件号)` 立即返回，不存在时为 null；`Parts` 返回全部 464 个部件，0.07 秒。部件的字段：`PartNr`、`Variant`、`Type`、`ProductGroup`，`Properties` 中的 `ARTICLE_DESCR1`（多语言原始字符串）、`ARTICLE_MANUFACTURER`、`ARTICLE_TYPENR`、`ARTICLE_ORDERNR`、`ARTICLE_MACRO`（宏路径，带 `$(MD_MACROS)`）。— S24。
- `SelectedPartsDatabaseAsString` 是静态属性。只读访问不改 `.adb` 中的数据文件，只改写 `.lck` 锁文件。— S24。

### A26. 不用窗口宏放置单个设备（spike S20，2026-10-21，EPLAN 2026.0.3.25421，Electric P8，在调度器中）
详细数据见 [spike S20](../../spikes/S20-symbol-placement/README.md)。示例项目的 scratch 副本。
- 按库符号新建的功能已有 12 个属性放置，但显示的文字都是空的；`new PropertyPlacement().Create(function, Properties.Function.FUNC_DEVICETAG_FULLNAME)` 之后图上显示 `KF96`（没有 `-`，压在符号的插入点上），`UseLocalPropertyPlacements` 变为 true，`VisibleName` 仍为空。— S20。
- `DeviceService.CreateDevice(部件号, "1", page, 位置)`：放置部件自带的宏（主功能，图上带标识、技术参数、连接点和触点镜像），其余功能未放置；标识为 `=+-Q1`，不取页的结构、不编号。能被 `SafetyPoint` 回滚，提交后一次 Ctrl+Z 撤销。— S20。

### A25. M4c 的导出（spike S25，2026-10-21，EPLAN 2026.0.3.25421，Electric P8，在调度器中）
详细数据见 [spike S25](../../spikes/S25-exports/README.md)。示例项目的 scratch 副本。
- `Export.PXFProject(project, 路径, 主数据, 连接)`：带主数据 36.9 秒、840 MB，不带主数据 19.2 秒、598 MB；扩展名 `.epj` 由 EPLAN 补上，另外留下一个与文件同名的空目录。只改写 `User.eod`。— S25。
- `PartsService.ExportPartsList`：XML 61.9 秒、14 MB；CSV 89.9 秒、20 MB（分号分隔）。只改写 `User.eod`。— S25。
- 🔴 部件清单 CSV 导出返回后约 1 秒，EPLAN 记录两次访问冲突（0xC0000005），随即关闭全部项目；进程留下来，但没有窗口、没有 Remote Client 端口和 Bridge 管道，不再响应。是导出的收尾还是下一个 action 触发的，尚未查清；在重新启动的 EPLAN 中，设备清单导出本身正常。— S25。
- `DeviceService.ExportDeviceList` 导出预规划用的设备清单，示例项目中为空文件；全部设备的清单要用标签方案 `Device tag list`。— S25。
- `Label.DoLabel(project, 方案, 语言, 文件, 1, 1)`：`Device tag list`（文本，40 KB）、`Terminal diagram`（xlsx，805 KB）各约 2 秒，生成 xlsx 时没有 Excel 进程；传入方案名不改写"上次使用"。方案中的默认目标在 `$(MD_DOCUMENTS)` 下，所以总要给文件名。— S25。
- `PlcService.ExportAddressOverview` 不给 PLC 配置项目、站和 CPU 时，输出只有一行空值。— S25。
- `Translate.ExportMissingTranslation(文件, "XTrLanguageDbXml2TabConverterImpl", 语言, [project])`：3.5 秒，制表符分隔。示例项目有 20 种翻译语言，显示语言和源语言为 `##_##`。— S25。

### A24. 电缆、端子、PLC 的读取与编号（spike S21，2026-10-21，EPLAN 2026.0.3.25421，Electric P8，在调度器中）
详细数据见 [spike S21](../../spikes/S21-cables-terminals/README.md)。示例项目的 scratch 副本。
- `DMObjectsFinder` 能读出电缆（按类别 78 根）、端子排（45 个对象，实际 27 排：18 排各返回两次，2026-10-21 实机测试）、端子（1331）、PLC 盒（20）、插头（170）、连接（2060）；`Cable.GetSourcesAndTargets` 给出电缆两端的设备或端子，`TerminalStrip.Terminals`、`PLC.PLCTerminals` 给出所属的端子。— S21。
- 🔴 `GetCablesWithFilterScheme("")` 用电缆导航器当前的过滤方案（示例中只返回 4 根）；`null` 返回电缆加屏蔽（89），XML 说会抛 `ArgumentNullException`，实际没有。按类别取电缆不受界面影响。— S21。
- `Renumber.Terminals`（全部端子，41–43 秒）、`CableService.RenumberCables`（0.3 秒）、`ConnectionService.DesignateConnections`（覆盖全部，约 1 秒，605 条连接得到线号）都能被 `SafetyPoint` 回滚，回滚后名称和线号与之前完全一致；在 Bridge 的包装中提交后，都能用一次 Ctrl+Z 撤销。— S21。
- 这三种编号的方案传空串都能运行，用的是界面上次的方案，与 `Renumber.DeviceTags` 相同（A18）。— S21。
- 方案的设置节点：端子编号 `USER.Terminal.Numbering.Scheme`、标签 `USER.Labelling.Config`（用户设置）；电缆编号 `CableLog.CableNumberingScheme`、生成电缆 `CableLog.CableCreateScheme`、线号 `WireNumberingLog.Schema`、PLC 编址 `OfflineNumerationPLC.PLCNumbering`（项目设置）。`XSettingsExport /prj:<项目名>` 在开发机上返回 FALSE，项目设置的节点是从 `Settings.xdb` 中只读查得的。— S21。
- `Generate.Cables` 在示例项目中没有改动名称和线号，但改写了项目的 `User.eod` 和 `Settings.xdb`（上次使用的方案），`SafetyPoint` 回滚不撤回。— S21。
- PLC 盒 `=K1+A2-KE1`：`PLC.PLCTerminals` 有 34 个对象、17 个名称，同名的不是重复（`:1.1` 既是输出 `%QX2.0` 又是输入 `%IX2.0`；其他名称一个有地址、一个没有）。按这些对象用 `ConnectionsFilter.Function` 查不到连接，按设备（主功能加辅助功能）查到 22 条。`FUNC_PLCSYMBOLICADDRESS_CALCULATED` 与 `FUNC_PLCADDRESS` 相同（`%QX2.0`），`FUNC_PLCRACK`、`FUNC_PLCSOCKET` 为空。— 实测（`eplan_plc_get` 的 Large 测试，2026-10-21）。
- 编号工具的 Large 测试（2026-10-21，新复制的写副本）：`eplan_cable_renumber` 用 `Standard` 给全部电缆编号，复现了 S21 的 `+A2-WD4`↔`+A2-WD5`、`+B2.Y1-WD1`↔`+B2.Y1-WD2`（`eplan_device_list` 按类别 `Cable` 列出 56 根）；插宏后对整个项目只编带 `?` 的端子，`Renumber.Terminals` 约 43 秒，EPLAN 没有崩溃（与 A20 中的 `Renumber.DeviceTags` 不同）；`+A1-XD1` 用 `Numerisch` 编号、线号的 `all` 和 `except_manual` 都通过。`+A1-WE1` 的主功能类别是 `BusBarDefText`，不是电缆。— 实测。
- 🔴 **`Renumber.Terminals(…, 只编带 ? 的 = true, …)` 挡不住已编号的端子**：第二轮 Large 测试（2026-10-21，新复制的写副本）中，插宏后把整个项目的全部端子交给它，只编带 `?` 的。7 个 `:?n` 编成了 `:n`（`+A1-XG2`–`XG4`），但它同时按方案重排了已编号的 `+A1-XD1`（`:2→:3`、`:3→:5`、`:4→:2`、`:5→:4`，同 S21），共 11 处改名，约 35 秒。
  - 对本项目的影响：`eplan_terminal_renumber` 只把带 `?` 的端子交给它，做法同 A20；读回时，规划之外的端子改了名就回滚。
  - 只交带 `?` 的端子时，同一端子排中已编号的端子会不会被重排（`AllSelectedTerminalStrips` 按 XML 编整排），还没有验证。— 实测。
- 授权：`Preplanning` 不可用；`RevisionManagement`、`RevisionMarkers`、`MultiLanguageTranslation`、`WireNumbering`、`EditingOfCables`、`EditingOfTerminals`、`EditingOfPLC`、`DXF_DWGImport`、`ImportAndExportDeviceLists`、`IntelligentPDF`、`InterfaceToLabelingDevices` 可用。— S21。

### A21. Claude Code 客户端的进度与图片（spike S13，2026-10-20，Claude Code 2.1.299）
详细数据见 [spike S13](../../spikes/S13-client-progress/README.md)。
- Claude Code 每次 `tools/call` 都在 `_meta` 中带 `progressToken`，服务器的进度通知送达；`initialize` 的协议版本为 `2025-11-25`。— S13。
- 工具结果中的 PNG（`eplan_export_image` 的 `inline`）交给了模型，模型读出了图上的设备标识和文字。— S13。
- 中断（SDK 的 `interrupt` 控制请求，与界面中按 Esc 同一个中止路径）约 0.2 秒后发出 `notifications/cancelled`，`reason` 为 "AbortError: remote-cancel"。— S13，2026-10-21。
- 无头模式下 150 秒的调用一直等到结果，期间每 5 秒的进度都送达，没有超时。交互界面中超过 2 分钟转到后台的显示没有测。— S13，2026-10-21。

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
- **删页会留下没有主功能的辅助功能**：删除 `+A1-WE1`（类别 `BusBarDefText`，A24；S12 中称为电缆）等设备的主功能所在的页后，其他页上的辅助功能仍在。报表页不随之更新。— S12。
- **`Renumber.DeviceTags(IFunctionBase[], null, Renumber.Enums.DeviceTags.PostNumerate, 方案, 1, 1)` 只给设备标识中的 `?` 编号**：`-?XG1` → `-XG2`（跳过已占用的 `XG1`）；端子连接点的 `?1` 不变；`=GAA+A1-?QA1` 也没有被编号（原因未查）。方案传 `""` 等同于 `Standard`；名称不存在时抛 `InvalidScheme`，消息列出可用的方案名。这个重载的第 4 个参数是方案（异常中的参数名为 `strSchemePerIdentifier`）；XML 文档中 `Project` 重载的参数名与类型顺序不一致，不要照着它传参。— S12。
  - 对本项目的影响：`eplan_device_renumber` 执行后读回，报告仍带 `?` 的标识；`eplan_page_delete` 的变更清单列出会失去主功能的设备。

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
  - 2026 的 `Eplan.EplApi.AFu.xml`：`OnBeforeInit` 在 `OnInit` 之前调用，传入"注册插件程序集的位置"；它抛出的异常不影响初始化，只记入系统消息。返回类型是 `void`（按 2026 的程序集编译通过）。传入的是 DLL 的完整路径还是所在目录，以及 `/register` 在运行中的会话里加载插件时是否也调用它，都没有核实；Bridge 在握手的 `addinPath` 中原样报告。— 只读查看 XML 文档；编译。
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
- `ProjectManager.OpenProject(链接文件, OpenMode)`：XML 没有列出 `OpenMode` 的成员，反射得到 `Standard`=0、`ReadOnly`=1、`Exclusive`=3。离线宿主用 `ReadOnly` 打开完成的修订（`.elr`），修订中的项目（`.ell`）照常打开；能否打开这两种链接文件，待 Large 测试确认。— 反射（2026-10-21）。

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
- `SchemeSetting.GetStringSetting(路径, 序号)` 的路径相对于当前方案（XML：从方案名之后开始），设置不存在时抛 `BaseException`。— XML。
  - 🔴 实测（2026-10-21，第一次 Large 测试，EPLAN 2026.0.3）：标签方案（`USER.Labelling.Config`）的相对路径**从方案的 `Data` 节点算起**，不是从方案名之后：`SetScheme` 之后 `GetStringSetting("Data.File.Format", 0)` 抛 `BaseException`，消息中的路径是 `USER.Labelling.Config.<方案>.Data<.Data.File.Format`；`GetStringSetting("File.Format", 0)` 能读到。不经 `SchemeSetting`、用完整路径 `new Settings().GetStringSetting("USER.Labelling.Config.<方案>.Data.File.Format", 0)` 也能读到。— 实机。
- 标签方案（`USER.Labelling.Config`）：EPLAN 自带的方案（`Device tag list`、`Summarized parts list`、`Terminal diagram`、`Cable diagram`、`PCT-Loops`、`SmartWiring`）的输出格式在方案下的 `Data.File.Format`，值是转换器名：`XmlLblTextExportConverterImpl`（前两个）或 `XmlLblExcelExportConverterImpl`（其余）；平台还登记了 `XmlLblXmlExportConverterImpl`。目标文件在 `Data.File.Destination`（如 `$(MD_DOCUMENTS)\BM-Liste.txt`），Excel 方案的模板在 `Data.File.Template`（`.xlsx`）。同一节点下还有 EPLAN 内部的方案 `InternalRefDefScheme`，也是"上次使用"的缺省值。— 只读查看 `Platform\2026.0.3\Cfg\LabelLog.xml`、`LabelLog_User_Config_*.xml`、`regPlatform.xml`（2026-10-21）。
  - 实测读出的格式（2026-10-21，第一次 Large 测试，按上面相对 `Data` 的路径）：`Device tag list`、`Summarized parts list` 为 `XmlLblTextExportConverterImpl`；`Cable diagram`、`PCT-Loops`、`Rittal Length cutting C8+`、`SmartWiring` 为 `XmlLblExcelExportConverterImpl`。`Terminal diagram` 没有在这次读出的方案中，S25 中它导出的是 xlsx。— 实机。
- 项目的翻译语言在项目设置 `TRANSLATEGUI.TRANSLATE_LANGUAGES`（索引 0，分号分隔，如 `en_US;de_DE`）。— CommandLineActions XML 中 `projectsettings` 动作的示例；S25 用 `ProjectSettings.GetStringSetting` 读出示例项目的 20 种（A25）。`Translate.ExportMissingTranslation` 要求每种语言都已是项目的翻译语言。— XML。
- `Label.DoLabel(project, 方案, 语言, 目标文件, 1, 1)`：目标文件的扩展名要与方案的配置一致，应为 `.txt`、`.xls`、`.xlsx` 或 `.xml`，否则换成 `.txt`；`.xls` 需要 Excel；目标为空时用方案中的目标；带 `bShowOutput` 的重载会打开输出文件。— XML。

### B10. 端子、电缆、线号的编号与项目方案（`HEServices`、`DataModel.ProjectSchemeSetting`，2026-10-21）
M4c 编号工具依赖的文档说明；实测结果见 A24（S21 只用了空方案、`All` 和"不只编带 ? 的"），其余由 Large 测试确认。
- `ProjectSchemeSetting.Init(节点, Project)`：用项目设置的节点初始化（XML 的例子为 `CableLog.CableAutoSelectionScheme`），节点不是该项目的方案时抛 `BaseException`。它继承 `SchemeSetting`，`GetCount`、`SetScheme(序号)`、`GetName` 可用，实现 `IDisposable`。— XML、编译。
- `Renumber.Terminals(Function[], TerminalsSequence, Extent, …, 方案, 起始值, 步长, 前缀, 后缀, 允许改变排序, 填补空号, 只编带 ? 的, 连带 PLC 控制的)`：`Extent.AllSelectedTerminalStrips` 编号给出的端子所在端子排的全部端子，`Extent.Selected` 只编给出的端子；前缀、后缀只在方案为"按在线编号格式"时有用；方案名不存在时抛 `InvalidScheme`。"只编带 ? 的"按 XML 只编名称中带 `?` 的端子，实际上挡不住已编号的端子（A24，2026-10-21）。— XML，枚举名经编译确认。
- `CableService.RenumberCables(Function[], 方案, 保留旧名, 保留标识符, 起始值, 步长)`：参数可以是电缆和屏蔽；"保留旧名"为 true 时已有的标识不变，只给标识中带 `?` 的电缆编号；方案名不存在时 XML 一处写抛 `InvalidScheme`、一处写抛 `ArgumentException`。— XML。
- `ConnectionService.DesignateConnections(Project, 方案, DesignateOverwrition, 标为手工, 先生成连接)`：`None` 从不覆盖、`ExceptManuals` 覆盖标为"手工设定"之外的、`All` 全部覆盖；与界面不同，只有"先生成连接"为 true 时才先更新连接；方案名不存在时抛 `InvalidScheme`（备注中写 `ArgumentException`）。— XML。
- `Renumber`、`CableService`、`ConnectionService` 都实现 `IDisposable`。— XML、编译。

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

### Claude Code 插件与桌面版配置（spike S17，2026-10-20，Claude Code 2.1.299，隔离的配置目录，未登录）
详细数据见 [spike S17](../../spikes/S17-client-plugin/README.md)。
- `CLAUDE_CONFIG_DIR` 把 `.claude.json`、`settings.json`、`plugins\` 都放到指定目录，`auth status` 报告这个目录；真实的 `~\.claude*` 不变。— S17。
- 本地目录形式的插件市场：`plugin marketplace add`、`plugin install` 不需要登录，网络不可达时也成功。市场不复制；install 在 `plugins\cache\<市场>\<插件>\<版本>\` 中复制一份插件，但从源目录加载（`plugin list` 的 "Read from"、`${CLAUDE_PLUGIN_ROOT}` 都指向源目录，改写源目录的 `.mcp.json` 后下一个进程就用新内容）。源目录不在时插件 `failed to load … cache-miss`，不会退回到缓存。`marketplace remove` 同时卸载插件，缓存副本留下并标记 `.orphaned_at`。— S17。
- 插件 `.mcp.json` 的 `command`、`args`、`env` 中 `${VAR}` 和 `${VAR:-默认值}` 都会展开，包括 `${USERPROFILE}`、`${CLAUDE_PLUGIN_ROOT}`（Windows 上用正斜杠）和任意环境变量；`%VAR%` 不展开。未设置又没有默认值的变量原样保留，`mcp list` 不给出警告。插件的 stdio 服务器继承命令行进程的环境。— S17。
- 两个插件服务器的命令行相同时，只启动一个（文档：插件按端点判断重复，本地、项目、用户范围的定义优先）。`mcp get` 隐藏插件服务器的命令、参数和环境变量。— S17。
- `marketplace add` 能展开 `~/…`，不能展开 `%USERPROFILE%\…`（报 "Invalid marketplace source format"）。— S17。
- 桌面版（MSIX，`Claude_pzs8sxrjxfjjc`）的 `claude_desktop_config.json` 只在 `%LOCALAPPDATA%\Packages\Claude_pzs8sxrjxfjjc\LocalCache\Roaming\Claude\` 中。包外的进程看不到 `%APPDATA%\Claude\`；包内的进程看到的 `%APPDATA%\Claude\claude_desktop_config.json` 与包目录中的是同一个文件（文件 ID 相同）。桌面版运行时每隔几十秒就会改写它。— S17、A6。

### Streamable HTTP 与 Claude Code 远程客户端（spike S19，2026-10-21，ModelContextProtocol.AspNetCore 2.2.0，Claude Code 2.1.299，只在 127.0.0.1 上）
详细数据见 [spike S19](../../spikes/S19-vm-http/README.md)。
- **`HttpServerTransportOptions.SessionMode` 默认是 `Stateless`**：没有 `Mcp-Session-Id`，每个请求一个新的服务器对象，`initialize` 中的客户端名称在后续请求中取不到，按会话保存的状态无处可放。`Stateful` 对 2026-07-28 的请求回 -32022；`StatefulForInitializeClients` 只给用 `initialize` 的客户端会话。— SDK 的 XML 文档 + S19 探测。
- HTTP 方式下**根服务容器中没有 `McpServer`**（三种模式都是），单例中 `GetService<McpServer>()` 为 null；stdio 方式下有，2025-11-25 和 2026-07-28 两种客户端的名称都能取到。— S19。
- `ConfigureSessionOptions` 在 Stateful 模式下每个会话调用一次，这时来源地址和响应头中新生成的 `Mcp-Session-Id` 都可用；SDK 不把会话绑定到来源地址；`IdleTimeout` 默认 2 小时，`MaxIdleSessionCount` 默认 10,000；`RunSessionHandler` 是实验性 API；`EnableLegacySse` 默认关闭（`/sse`、`/message` 回 404）。— S19。
- 带上 `ModelContextProtocol.AspNetCore` 后，自包含单文件多 26.5 MB；`Microsoft.Extensions.*` 改用 ASP.NET Core 运行时包中的 ReadyToRun 版本；发布目录多一个 Kestrel 用不到的 `aspnetcorev2_inprocess.dll`。— S19。
- **Claude Code 2.1.299 连接远程 HTTP 服务器时先发 2026-07-28 的 `server/discover`**；服务器回 -32022 时退回 `initialize`（2025-11-25）并带 `Mcp-Session-Id`，再开 `GET /mcp` 的 SSE 流；不发 `Origin`；`User-Agent` 为 `claude-code/2.1.299 (sdk-cli)`；`mcp list`/`mcp get` 每次都新建会话，结束时不发 `DELETE`。— S19 的请求日志。
- `claude mcp add --transport http --scope user <名称> <URL> --header "Authorization: Bearer …"` 在 `.claude.json` 顶层 `mcpServers` 中写 `{"type":"http","url":…,"headers":{…}}`；`mcp get` 原样打印请求头的值。401 时报 "Server rejected the configured Authorization header (HTTP 401)"，配置了 `Authorization` 就不再尝试 OAuth。请求头中的 `${VAR}` 原样保存、连接时展开，变量不存在时在诊断中警告。— S19。

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

### A8. 插件程序集的生命周期
- **插件程序集一旦被 EPLAN 加载，在 EPLAN 进程退出之前无法卸载。** 注销之后再注册同名的新版本，执行的仍然是内存中的旧代码，不会报错。— spike S2（第一次重新构建后，新探针没有执行）。
  - 对开发流程的影响：重新编译 Bridge 后，必须重启 EPLAN；spike 中可以改用新的程序集名（`-p:AssemblyName=…r2`）。
  - 对安装的影响：`install-addin` 更新插件之后，必须提示用户重启 EPLAN；握手时比对 Bridge 的版本号，可以发现"已更新但尚未重启"的情况。

### A6. 由 MSIX 打包应用启动的进程（Claude 桌面版）
- Claude 桌面应用以 MSIX 包安装（包族名 `Claude_pzs8sxrjxfjjc`）。由它启动的子进程写入 `%LOCALAPPDATA%` 时，**被重定向到 `%LOCALAPPDATA%\Packages\Claude_pzs8sxrjxfjjc\LocalCache\Local\`**，包外的进程（EPLAN）看不到这些文件。写到 `D:\…` 以及 `%TEMP%`（即 `%LOCALAPPDATA%\Temp`）下的文件，EPLAN 可以正常读取。— spike S0 第一次运行失败的原因；检查包的 LocalCache 目录确认。
  - 对本项目的影响：由 Claude 桌面版启动的 Host，不能把 EPLAN 需要读取的文件（scratch 项目、实例发现文件）写到 `%LOCALAPPDATA%`；审计日志写在那里，用户也会找不到。见 ADR-0008。
  - 待验证：包内进程能否**读取**包外进程写到 `%LOCALAPPDATA%` 的文件（Bridge 写、Host 读）。

### A4. 路径变量
- 在 2026 中，`PathMap.SubstitutePath` 无法替换 `$(MD_SCHEMES)`、`$(MD_IMAGES)`、`$(TEMPPATH)`、`$(USERSETTINGSPATH)`，原样返回；`$(MD_MACROS)`、`$(MD_PARTS)`、`$(MD_SYMBOLS)`、`$(MD_FORMS)` 正常。— 实机调用。待查：2026 中方案（schemes）的存储位置。

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
- `TransactionManager.CreateTransaction()` 返回 `Transaction`（`Commit`、`Abort`、`Store`）。— 反射。与 `SafetyPoint` 的分工尚未验证。
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

---

## C. MCP 客户端与 C# SDK（spike S4，2026-10-13）

> 不属于 EPLAN API，但同样是"代码依赖的外部行为"，按同样的规则记录。环境：ModelContextProtocol 2.2.0，Claude Code 2.1.295。

- **Claude Code 2.1.295 使用 MCP 2026-07-28**：连接时先发 `server/discover`（id `server-discover-probe-1`），每个请求的 `params._meta` 中都带 `io.modelcontextprotocol/protocolVersion: "2026-07-28"`、`clientInfo`（`name: "claude-code"`）和 `clientCapabilities: {"roots": {"listChanged": true}, "elicitation": {"form": {}, "url": {}}}`；随后用 `subscriptions/listen` 订阅 `toolsListChanged`。— [spike S4](../../spikes/S4-elicitation/README.md) 的报文记录。
- **SDK 2.x 中，工具抛出 `InputRequiredException` 即可发起 elicitation**，两种协议都适用：2026-07-28 下返回 `resultType: "input_required"`（`inputRequests` + `requestState`），客户端用新的 id 重发 `tools/call` 并带回 `inputResponses` 和原样的 `requestState`；2025-11-25（stdio）下 SDK 改为向客户端发送 `elicitation/create` 请求，拿到回答后**重新调用同一个工具处理器**，`RequestContext.Params.InputResponses` 和 `RequestState` 同样有值。— S4 selftest（10 个场景）+ 报文核对。
- `McpServer.IsMrtrSupported` 在 2026-07-28 下、以及 stdio 上的 2025-11-25 会话中都为 true；客户端没有声明 elicitation 时 `McpServer.ClientCapabilities.Elicitation` 为 null。规范禁止向未声明的客户端发送 `elicitation/create`，因此两者都要检查。— S4 + [MRTR 规范](https://modelcontextprotocol.io/specification/2026-07-28/basic/patterns/mrtr)。
- `McpServer.ElicitAsync` 在无状态模式下抛出 `InvalidOperationException`，只适用于旧协议的有状态会话；Host 不使用它。— SDK 文档 `docs/concepts/elicitation`。
- SDK **不按 `inputSchema` 校验 `tools/call` 的参数**：即使 schema 中有 `additionalProperties: false`，拼错的参数（如 `limt`）也被静默忽略，工具按默认值执行。Host 用 `tools/call` 请求过滤器统一拒绝未知参数。— M1 骨架的 stdio 端到端测试（2026-10-13）。
- SDK 由 `[McpServerTool]` 生成的 `inputSchema` **不含 `additionalProperties: false`**；在特性中显式设置的 `Title`、`ReadOnly`、`Destructive`、`Idempotent`、`OpenWorld` 都出现在 `tools/list` 的 `annotations` 中（`title` 同时出现在工具顶层）。— S4 报文。
- Claude 桌面版自带的 Claude Code 可执行文件在无界面模式（`claude -p`）下单独运行时没有登录态（"Not logged in"），但在报错前已经完成 MCP 服务器的连接和 `tools/list`。— S4。

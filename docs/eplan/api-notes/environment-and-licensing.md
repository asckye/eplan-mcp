# [EPLAN API 笔记](../api-notes.md)：环境、授权与注册

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

### A4. 路径变量
- 在 2026 中，`PathMap.SubstitutePath` 无法替换 `$(MD_SCHEMES)`、`$(MD_IMAGES)`、`$(TEMPPATH)`、`$(USERSETTINGSPATH)`，原样返回；`$(MD_MACROS)`、`$(MD_PARTS)`、`$(MD_SYMBOLS)`、`$(MD_FORMS)` 正常。— 实机调用。待查：2026 中方案（schemes）的存储位置。

### A5. 未签名插件与授权（spike S0，2026-10-13，Electric P8 变体）
- **未签名、无强名称的 net481 插件可以注册、延迟加载并执行**：先用 `EplApiModuleAction /register:"<dll>"` 注册（`bLoadOnStart = false`），第一次调用它的 action 时加载。实际加载位置是影子副本 `%APPDATA%\EPLAN\ShadowCopyAssemblies\<pid>\<程序集名>\bin\`。— [spike S0](../../../spikes/S0-licence/README.md)。
- 在 321 页的项目上，插件读取页面、读取功能、建页面、删页面全部成功，**没有出现 5 页的限制**；EPLAN 系统日志中没有授权或签名相关的警告。授权：`Borrowed`，`APIExtension = true`。— spike S0。
  - 局限：只验证了这一份授权。其他授权类型的行为未知（见 ADR-0007）。
- 通过 Remote Client 执行 `EplApiModuleAction /register` 和 `/unregister:"<程序集标题>"`（在静默模式下）都返回成功，没有弹出对话框。— spike S0。
- action 参数名在 EPLAN 内部被转为小写（日志显示 `/report:…`），`ActionCallingContext.GetParameter("Report", …)` 照样能取到值，**参数名不区分大小写**。— spike S0。
- 插件 action 中未处理的异常**不会导致 EPLAN 崩溃**：EPLAN 记录完整的堆栈，并报告"操作 … 失败"；Remote Client 的调用仍然返回成功。— spike S0 第一次运行。
- 以下 EPLAN 类型实现了 `IDisposable`（分析器 CA2000 提示）：`DMObjectsFinder`、`UndoManager`、`PagePropertyList`、`Page`。— 编译 spike S0。

### A8. 插件程序集的生命周期
- **插件程序集一旦被 EPLAN 加载，在 EPLAN 进程退出之前无法卸载。** 注销之后再注册同名的新版本，执行的仍然是内存中的旧代码，不会报错。— spike S2（第一次重新构建后，新探针没有执行）。
  - 对开发流程的影响：重新编译 Bridge 后，必须重启 EPLAN；spike 中可以改用新的程序集名（`-p:AssemblyName=…r2`）。
  - 对安装的影响：`install-addin` 更新插件之后，必须提示用户重启 EPLAN；握手时比对 Bridge 的版本号，可以发现"已更新但尚未重启"的情况。

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

### A22. 离线程序（spike S15，2026-10-20，EPLAN 2026.0.3.25421，Electric P8）
详细数据见 [spike S15](../../../spikes/S15-offline-host/README.md)。开发机，当前用户的交互桌面，界面中的 EPLAN 同时运行。
- `EplApplication.Init(string.Empty, false, false)`（不允许授权和登录对话框）在开发机上 2.0–2.3 秒内成功，不弹出任何窗口；`QuietMode` 为 `ShowNoDialogs`。— S15。
- `EplanBinFolder` 和 `AssemblyResolver.SetEplanBinPath` 要用**变体的** Bin（`…\Electric P8\2026.0.3\Bin`，有 `W3u.exe`）；给 Platform 的 Bin 时 `Init` 抛出 `ArgumentException`："The Variant in '…' is not valid."。`AssemblyResolver` 只解析 EPLAN 自己的程序集，Platform Bin 中的 `Newtonsoft.Json` 要自己处理 `AssemblyResolve`。— S15。
- 授权与插件中看到的相同（3214 个选项中 2683 个可用，含 `APIExtension`）；`EplApplication.License` 的字符串是"Eplan Demo System"，但修改照常保存。未签名的离线程序读写 321 页的项目没有限制。— S15。
- 与界面中的 EPLAN 同时运行互不影响；`SafetyPoint` 回滚、`UndoStep` 建在外面时回滚静默失效，都与插件中相同（A7）。提交后 `UndoStep.DoUndo()` 不撤销。— S15。
- 为本用户注册的插件会被影子复制进离线进程，但不调用 `OnInitGui`，所以 EplanMcp 的 Bridge 不启动。`Init` 把线程的区域性改为界面的区域性。— S15。
- 未验证：另一个 Windows 用户、服务上下文、网络授权或运行时授权下的行为。

### A23. 插件注册（spike S14，2026-10-21，EPLAN 2026.0.3.25421，Electric P8，非提升权限）
详细数据见 [spike S14](../../../spikes/S14-addin-registration/README.md)。
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

### B5. 离线程序（无界面）
- `Eplan.EplApi.System.EplApplication`（Systemu）：`Init(appModifier[, allowLicenseDlg[, allowLoginDlg]])`、`Exit()`，以及属性 `EplanBinFolder`、`LicenseFile`、`QuietMode`、`StartEplanRemoteServer`（必须在 `Init` 之前设置）。— 反射。
- `Eplan.EplApi.Starteru.dll` 提供 `AssemblyResolver`（`SetEplanBinPath` 等）、`EplanFinder.SelectEplanVersion`、`EplanSignedAssemblyAttribute`。— 反射。
- 要求：x64 进程；目标 .NET Framework 4.8.1；**必须是 STA 线程**（`[STAThread]`）；`Exit()` 只能在主线程调用一次，之后不能再次 `Init`；使用 DataModel 前要创建 `LockingStep`。— XML、`2026/UsingEplanAssemblies.html`。
- `Init` 之后，除了会显示界面的功能（模态对话框、停靠窗口、MDI 窗口）以外，**所有 API 功能都可以使用**。— XML。
- `ProjectManager.OpenProject(链接文件, OpenMode)`：XML 没有列出 `OpenMode` 的成员，反射得到 `Standard`=0、`ReadOnly`=1、`Exclusive`=3。离线宿主用 `ReadOnly` 打开完成的修订（`.elr`），修订中的项目（`.ell`）照常打开；能否打开这两种链接文件，待 Large 测试确认。— 反射（2026-10-21）。

### B8. 授权
- **脚本**：不需要 API 授权即可运行，但只能使用 Base、ApplicationFramework/Scripting、Gui、MasterData；引用其他程序集（例如 DataModel）需要 **API 扩展授权**（`LicenseOptions.APIExtension = 725`）。— `2026/Scripts.html`。
- **编译的 API 程序（插件、离线程序）**有两种授权方式：
  - **开发者授权**：只能加载**未签名**的程序，"normally has a limitation of a maximum size of Eplan projects to 5 pages"（通常把项目大小限制在 5 页以内），只用于开发和测试；
  - **运行时授权**：只能加载**已签名**的程序。签名流程：在程序集上标注 `EplanSignedAssemblyAttribute`，用 EPLAN 提供的公钥延迟签名，然后上传到 Eplan Cloud 的 **EADN Signing** 服务完成签名（需要 EADN Signing 的 User 角色，自动签名还需要个人访问令牌）。客户"通过开发者"获得该程序的运行时授权。
  
  — `2026/Signing_EPLAN_assembies.html`。
- 运行时由 `Base.Internal.LicenseChecker` 检查调用栈，缺少授权时报告 "API License missing"。— XML。
- Remote Client：没有找到官方的授权说明；客户端 DLL 不引用 Baseu，不做客户端授权检查。— 未验证。
- **已验证（spike S0）**：在开发机的授权下，未签名的插件可以加载，并能读写 321 页的项目（A5）。其他授权类型仍然未知。

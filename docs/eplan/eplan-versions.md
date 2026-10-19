# EPLAN 2022–2027 各版本的 API 兼容性（官方文档核对）

> 核对日期：2026-10-19。是 [ADR-0009](../adr/0009-supported-eplan-versions.md) 的依据，本文只记录事实，不做决定。
> 方法：用 curl 抓取 eplan.help 的原始 HTML；对每个版本的 API 参考页**逐个探测 URL**（HTTP 200 = 该版本文档收录，404 = 未收录），并把各版本同一成员页的 C# `Syntax` 段落做文本比对。
> 约定：✓ = 该版本官方文档有此页；✗ = 404（**只说明文档未收录，不等于二进制中不存在**）；"未核实" = 官方文档中没有找到依据。
> 除特别注明"本机反射 / api-notes"的内容外，本文所有结论都来自下列官方页面，没有凭记忆补充。

---

## 1. 使用的来源

### 1.1 各版本 API 帮助树（全部存在，URL 模式相同）
`https://www.eplan.help/en-us/Infoportal/Content/api/<版本>/`，版本取 `2022`、`2023`、`2024`、`2025`、`2026`、`2027`。首页标注的文档版本：2022.0.3、2023.0.3、2024.0.3、2025.0.3、2026.0.3、2027.0.3（2027 文档日期 22.06.2026）。

| 页面 | 用途 |
|---|---|
| `EplanApiDotNet.html` | 每个版本声明的 .NET 版本、需要引用的程序集 |
| `Assemblies.html`（2022–2026）；`Assemblies for .NET Framework.html`、`Assemblies for .NET 8.html`（2027） | 程序集清单（另读取目录树 `_toc/c0.js` 中各程序集节点） |
| `Eplan.EplApi.BaseNetuDummyAssembly.html`（2027） | .NET 8 程序集与 .NET Framework 程序集的关系 |
| `EplApiModuleAction.html`、`EplApiModuleActionNet.html`（只有 2027） | 注册命令行 |
| `CSharpAddins.html`、`AddinInVisualStudio.html`、`AddIns.html`、`UsingEplanAssemblies.html`、`ShadowCopyingAPIAssemblies.html` | 插件开发与加载说明 |
| `Signing_EPLAN_assembies.html` | 开发者授权、运行时授权与签名流程 |
| `Scripts.html` | API Extension 授权说明、脚本可用的程序集（含 Newtonsoft.Json） |
| 类型/成员参考页（见第 2.2 节表格中的页面名） | 我们依赖的 API 在各版本是否收录、签名是否变化 |

**没有找到的东西**：API 帮助树里**没有** "What's new" / "API changes" / "Breaking changes" 页面（逐版本检查了目录树顶层节点）。变更说明只在平台帮助的 News 页面中（见 1.2）。

### 1.2 平台帮助中的 API 新功能页（相当于变更记录）
- 2023：https://www.eplan.help/en-us/Infoportal/Content/Plattform/2023/Content/htm/news_p_api_2023.htm
- 2024：https://www.eplan.help/en-us/Infoportal/Content/Plattform/2024/Content/htm/news_p_api_2024.htm
- 2025：https://www.eplan.help/en-us/Infoportal/Content/Plattform/2025/Content/htm/news_p_api_2025.htm
- 2026：https://www.eplan.help/en-us/Infoportal/Content/Plattform/2026/Content/htm/news_p_api_2026.htm
- 2027：https://www.eplan.help/en-us/Infoportal/Content/Plattform/2027/Content/htm/news_p_api_2027.htm
- 2022：同一模式返回 404，其他命名也没有找到 → **2022 的 API 变更说明未核实**。

### 1.3 软件与硬件要求（官方）
- 当前版本（覆盖 2024–2026 与 2027 起）：https://www.eplan.help/en-US/Infoportal/Content/SwReqs/Content/htm/SwReqs_k_general.htm
- 存档"截至 2024 年底"：https://www.eplan.help/en-us/infoportal/content/swreqs/Content/htm/SwReqs_k_win10_2024.htm
- 存档"截至 2025 年底"：https://www.eplan.help/en-us/infoportal/content/swreqs/Content/htm/SwReqs_k_win10_2025.htm
- 操作系统兼容表：https://www.eplan.help/en-US/Infoportal/Content/SwReqs/Content/htm/SwReqs_k_os.htm

### 1.4 关键原文（决定性证据）
存档要求页（2024、2025 两份一致）的 .NET 表格：`Up to 2.8 → 4.5.2`，`2.9 up to 2023 → 4.7.2`，`As of 2024 → 4.8.1`，并附说明（逐字）：
> "When switching to .NET Framework 4.8.1 as of Eplan Platform Version 2024 … API add-ins should not be compiled - this means that they remain executable in the Eplan Platform version in which they were created and in all newer versions of the Eplan Platform. API add-ins compiled with the .NET Framework 4.8.1 are only executable in the Eplan Platform Version 2024 and higher."

当前要求页的表格（已解析 HTML 核对行列）：`.NET Framework 4.8.1` 在 "2024 up to 2026" 和 "As of 2027" 两列都打 X；`.NET Desktop Runtime / ASP.NET Core Runtime 8.0.25` 只在 "As of 2027" 列打 X（脚注：Electric P8 和 Pro Panel 使用）。并说明 "API add-ins compiled with the ASP.NET Core Runtime 8.0.25 or higher are only executable in the Eplan Platform Version 2027 and higher."

---

## 2. 版本矩阵

### 2.1 运行时、平台、程序集

| 版本 | 插件目标框架 | 现有 net481 插件能否加载 | x64 | API 程序集（.NET Framework） | .NET 8 API |
|---|---|---|---|---|---|
| 2022 | **.NET Framework 4.7.2**（[net22]、[req24]） | **否**（[req24]："compiled with … 4.8.1 … only executable in … 2024 and higher"） | 官方 API 文档未写明；要求页只写"x64 架构的处理器"（[req24]）→ **未核实** | AFu、Baseu、DataModelu、EServicesu、Guiu、HEServicesu、MasterDatau、RemoteClientu、Remotingu、Starteru、Systemu（11 个，[asm22]） | 无 |
| 2023 | **4.7.2**（[net23]、[req24]） | **否**（[req24]） | 同上，未核实 | 同 2022（[asm23]） | 无 |
| 2024 | **4.8.1**（[net24]、[req24]） | 是（官方规则，[req24]；实机未验证） | 未核实（要求页 x64 CPU，[req24]） | 同上 + `Eplan.IdentityClient.Authentification`、`Eplan.IdentityClient.Types`（13 个，[asm24]） | 无 |
| 2025 | **4.8.1**（[net25]、[req25]） | 是（官方规则；实机未验证） | 未核实 | 同 2024（[asm25]） | 无 |
| 2026 | **4.8.1**（[net26]、[reqcur]） | 是（**实机已验证**，api-notes A10） | **x64**（本机反射，api-notes B1；官方文档未写明） | 同 2024（[asm26]）；本机 Bin 另有未写进文档的 RecorderToolsu、WebServicesu、WebServiceu（api-notes B1） | 无 |
| 2027 | **4.8.1 API 保留** + 并行的 **.NET 8** API（[net27]、[reqcur]、[news27]） | 预计是（4.8.1 API 保留，2027 仍要求 .NET Framework 4.8.1，[reqcur]）；**未验证** | 未核实 | 13 个，名称与 2024 相同（[asm27fw]）；注册仍用 `EplApiModuleAction`，页面注明"for .NET Framework based code"（[ema27]） | `*Netu` 程序集（AFNetu、BaseNetu、DataModelNetu …，[asm27n8]）；除 AFNetu 外与 .NET Framework 版内容相同，只有程序集名不同（[n8base27]）；用 `EplApiModuleActionNet` 注册（[eman27]）；需要 .NET Desktop Runtime 8.0.25（[reqcur]）；Remoting 和 Web Service 需要 ASP.NET Core Runtime（[net27]）。**没有找到 .NET Framework API 被废弃的说明**（检索了 [net27]、[asm27fw]、[asm27n8]、[uea27]、[news27]）。脚本在 2027 起只用 .NET 8 / Roslyn / C# 13（[scr27]、[news27]） |

### 2.2 我们依赖的 API 在各版本文档中的收录情况

单元格出处：`https://www.eplan.help/en-us/Infoportal/Content/api/<列中的版本>/<行中的页面>`，2026-10-19 用 curl 探测（✓ = HTTP 200，✗ = 404）。"签名比对"一列是把各版本同一页面的 C# `Syntax` 段落做文本比较的结果。

| API（页面） | 2022 | 2023 | 2024 | 2025 | 2026 | 2027 | 签名比对 |
|---|---|---|---|---|---|---|---|
| `EplanMainThreadDispatcher` 类（`Eplan.EplApi.Baseu~Eplan.EplApi.Base.Internal.EplanMainThreadDispatcher.html`） | ✗ | ✗ | ✗ | ✓ | ✓ | ✓ | — |
| `GetMainThreadDispatcher`（`…EplanMainThreadDispatcher~GetMainThreadDispatcher.html`） | ✗ | ✗ | ✗ | ✓ | ✓ | ✓ | 2025–2027 相同：`public static Dispatcher GetMainThreadDispatcher()` |
| `CanAccessMainThread`（`…~CanAccessMainThread.html`） | ✗ | ✗ | ✗ | ✓ | ✓ | ✓ | 2025–2027 相同 |
| `Base.Internal` 命名空间页（`Eplan.EplApi.Baseu~Eplan.EplApi.Base.Internal_namespace.html`） | ✗ | ✗ | ✗ | ✓ | ✓ | ✓ | 另外抓取了 2024 的完整目录树（14290 个页面）：Baseu 下只有 `Eplan.EplApi.Base` 和 `Eplan.EplApi.Base.Enums` 两个命名空间 |
| `SetMainThreadDispatcher` | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | 任何版本的文档都没有（2026 中的存在来自本机反射，api-notes B6） |
| `IEplAddIn`（`Eplan.EplApi.AFu~Eplan.EplApi.ApplicationFramework.IEplAddIn.html`）及 `~OnRegister/~OnInitGui/~OnExit` | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | 全部相同；接口 GUID 都是 `772B6E84-84D0-3597-8397-363C01BB41B6` |
| `IEplAddInShadowCopy` | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | — |
| `EplApiModuleAction.html`（`/register`、`/registerModule`、`/unregister`、`/unregisterInternal`） | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | 参数集合相同；2027 注明只用于 .NET Framework 插件 |
| `EplApiModuleActionNet.html` | ✗ | ✗ | ✗ | ✗ | ✗ | ✓ | — |
| `LockingStep`、`LockingStep~_ctor`（DataModelu） | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | 相同 |
| `SafetyPoint`、`~Create`、`~Commit`、`~Rollback` | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | 相同 |
| `UndoManager`、`~CreateUndoStep`；`UndoStep`、`~CloseUndo`、`~SetUndoDescription`（均在 DataModelu） | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | 相同 |
| `QuietModeStep`、`~_ctor`、`QuietModes`（AFu） | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | 相同 |
| `ProjectManager~OpenProjects`、`~LockProjectByDefault` | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | 相同；`ProjectManager` 成员表 2022→2027 只多了 `CreateProject` 的新重载 |
| `ProjectLockingException`、`NoLockingStepException`、`LockingException~GetAllFailed2LockAsString`、`Project~ProjectFullName` | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | 相同 |
| `SelectionSet~GetCurrentProject`（HEServicesu） | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | 相同 |
| `DMObjectsFinder`、`~GetPages`、`~GetFunctions`、`~GetConnections`、`~GetArticleReferences` | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | 相同；成员表只有新增：2025 `GetCableUnits*`，2026 `GetCustomMateEntries` |
| `PagesFilter`、`FunctionsFilter`、`ConnectionsFilter`、`ArticleReferencesFilter` | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | 成员相同（`FunctionsFilter` 只有说明文字修改） |
| `Function.VisibleName`（继承自 `FunctionBase`：`…DataModel.FunctionBase~VisibleName.html`） | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | 相同 |
| `Function~IsMainFunction`、`Function~FunctionCategory` | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | 相同 |
| `MultiLangString~GetStringToDisplay`（Baseu） | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | 相同 |
| `EServices.PrjMessagesCollection`、`~_ctor(Project)`、`ProjectMessage` | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | 相同 |
| `Base.SysMessagesCollection`、`~_ctor()` | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | 相同 |
| `License`、`~_ctor`、`~HasOption`、`~GetLicenseModules` | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | 相同 |
| `LicenseOptions.APIExtension` | ✓ = 725 | ✓ = 725 | ✓ = 725 | ✓ = 725 | ✓ = 725 | ✓ = 725 | 枚举值相同 |
| `Eplan.EplApi.Starter.EplanSignedAssemblyAttribute`（Starteru） | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | — |

**结论**：除了 `EplanMainThreadDispatcher`（2022–2024 文档未收录），Bridge 用到的全部类型和成员在 2022–2027 的文档中都存在，且签名没有变化。

### 2.3 Newtonsoft.Json、授权、破坏性变更

| 版本 | EPLAN 自带的 Newtonsoft.Json | 授权与签名（与其他版本的差别） | 已记录的破坏性变更（是否影响我们） |
|---|---|---|---|
| 2022 | **未核实**（[scr22] 的脚本可用程序集中没有 Newtonsoft） | 脚本免授权，引用 DataModel 等需要 "EPLAN API Extension"（[scr22]）；开发者授权只加载未签名程序、"normally … 5 pages"；运行时授权只加载签名程序；签名需 **EADN 合同**，经 **EPLAN File Exchange**（`service.eplan.de/exchange`）上传签名（[sign22]） | API 新功能页 404，**未核实** |
| 2023 | **未核实**（[scr23] 没有列出） | 与 2022 完全相同（两页文本一致，[sign23]） | 无影响（[news23] 只有新增的 action 参数、方法和属性） |
| 2024 | [scr24] 的脚本可用程序集列出 `Newtonsoft.Json`，**没有版本号**；[news25] 称脚本"as of Version 2024 Update2"可用 Newtonsoft.JSON，"This library is used by EPLAN" → 版本**未核实** | 签名改为 **EPLAN Cloud 的 EADN Signing 服务**（`developer.eplan.com`，需要组织中 EADN Signing 的 User 角色），资格来自"EADN contract **or purchased an API Runtime license**"；File Exchange 标为"outdated but still working"（[sign24]）；开发者授权 5 页限制的说法不变 | ① 切换到 .NET Framework 4.8.1（[req24]）；② "numerous methods and properties that were already identified as outdated for Version 2.9 SP1 have been removed"（[news24]，没有给出清单）；③ `Synchronize.PartToSystem`（带 Storemode 的重载）、`PublishProjectForSmartWiring` 标为过时。**我们用到的成员都不在其中**：2.2 节显示它们在 2023 和 2024 中签名相同 |
| 2025 | [scr25] 列出 `Newtonsoft.Json`，版本未核实 | 与 2024 基本相同（只有文字修改，[sign25]） | `Backup.Project` 标为过时（[news25]），其余为新增。无影响 |
| 2026 | [scr26] 列出 `Newtonsoft.Json`；本机 Bin 中是 **13.0.3**（程序集版本 13.0.0.0，**来自本机检查**，api-notes B1，不是官方文档） | **不再提到 File Exchange**；门户手工签名"intended only for testing"，主推使用 PAT 的自动签名；新增 `GET /licenses` 检查预签名密钥；新增"Eplan checks the signature for all DLLs and exe files that directly or indirectly use Eplan API functions"（[sign26]） | 删除旧菜单栏的方法、类和 action；删除 `XPamArticlesSyncAction`；`Check.VerifyMDPartsDatabaseItems` 去掉了项目参数（[news26]）。`Check` 已在我们的 `BannedSymbols.txt` 中。无影响 |
| 2027 | [scr27] 的 .NET 8 脚本列出的是 `System.Text.Json`，不是 Newtonsoft；.NET Framework 插件可用的 Bin 内容**未核实** | 签名页与 2026 相同（[sign27]）；脚本页改为"The feature to reference other assemblies is only available with the 'Eplan API Extension' license and only for offline applications, add-ins and add-ons."（[scr27]） | 新增并行的 .NET 8 API；脚本只用 .NET 8（C# 13）；`ProjectManager.CreateProject` 新增重载（[news27]）。没有找到 .NET Framework API 被废弃或删除我们所用成员的说明 |

补充（各版本都适用）：影子复制说明从 2.6 起一致，2022–2027 各版本只有措辞变化（[sc22] 对比 [sc26]）；`OnInitGui` 只在启动时加载的插件上调用，这一说明在各版本的 `IEplAddIn` 页中相同。

---

## 3. 影响分析（供 ADR 讨论，不是决定）

### 3.1 官方规则
1. 2.9–2023 的 API 是 **.NET Framework 4.7.2**；2024 起是 **4.8.1**；2027 仍然要求 4.8.1，同时新增 .NET 8 API（[req24]、[reqcur]）。
2. **以 4.8.1 编译的插件只能在 2024 及以后运行**（[req24]）。
3. **插件不需要重新编译，可以在创建时的版本及之后所有版本中运行**（[req24]）。

### 3.2 现有 net481 构建能覆盖哪些版本
- **按官方规则：2024、2025、2026、2027（.NET Framework API）**；不能覆盖 2022、2023。
- 但在 2024 上有一个**代码层面的障碍**：`EplanMainThreadDispatcher` 在 2022–2024 的文档中都不存在。Bridge 在 `EplanDispatcher.CaptureOnMainThread` 中调用 `GetMainThreadDispatcher()`，在 `MainThread.Assert`（Debug）中调用 `CanAccessMainThread()`。如果 2024 的 Baseu 中确实没有这个类型，JIT 编译这些方法时会抛 `TypeLoadException` / `MissingMethodException`，导致 Bridge 启动失败（这是 CLR 的一般行为，**在 EPLAN 上未验证**）。
  - 不依赖 EPLAN 类型的替代方案：在 `OnInitGui`（主线程）中取 `Dispatcher.CurrentDispatcher`，断言改用 `Dispatcher.CheckAccess()`。依据：2026 上 `GetMainThreadDispatcher()` 与主线程的 `Dispatcher.CurrentDispatcher` 是同一个对象（api-notes A9）。现有代码在拿到 null 时已经会退回 `Dispatcher.CurrentDispatcher`，只是类型缺失时根本走不到这一步。
  - 另一个前提：Bridge 编译时引用的是 2026 的 `Eplan.EplApi.*u.dll`（程序集版本 1.0.0.0，api-notes B1）。2024、2025、2027 的程序集版本和强名称**未核实**；如果不同，绑定可能失败。不过官方规则 3（向前兼容）暗示同名程序集在新版本中可以正常绑定。

### 3.3 能否"用最低框架构建一次，2022–2027 都能用"
- 官方规则 3 支持这样做：**以 net472 针对 2022（或 2023）的 API 程序集编译，按官方说法可在 2022–2026 运行，2027 上走保留下来的 .NET Framework API**。
- 前提和代价：
  1. 编译时需要 2022 或 2023 的 API 程序集，也就是需要装有该版本的机器（EPLAN 的 DLL 不能提交进仓库）。无法让 net472 项目直接引用 2026 的 net481 程序集；这是 MSBuild 的一般行为（参考解析会因目标框架过高而失败），**不是 EPLAN 文档内容，未在本项目验证**。
  2. 不能在编译期使用 `EplanMainThreadDispatcher`（2022 文档中没有），必须采用 3.2 中的 WPF `Dispatcher` 方案。
  3. 2022 之后新增的 API 无法直接使用，只能通过反射或按版本隔离的适配层。当前 M1/M2 用到的成员在 2022 中都已存在（见 2.2）。
  4. Newtonsoft.Json：Bridge 和 Bridge.Core 设置了 `ExcludeAssets="runtime"`，运行时依赖 EPLAN 自带的 13.x。2022、2023 以及 2024 Update2 之前的版本是否自带、带哪个版本，**都未核实**。如果没有或版本较低（程序集版本不是 13.0.0.0），会出现绑定失败。
  5. netstandard2.0 的 `Bridge.Core` / `Contracts` 在 4.7.2 上运行：.NET 的一般结论是 4.7.2 内置 netstandard2.0 支持，**在 EPLAN 进程中未验证**。
  6. 2022 的平台支持状况：OS 兼容表中，当前的 Windows 11 23H2/24H2/25H2 只列出 EPLAN 2023–2027；2022 只出现在已停止服务的 Windows 11 22H2 和 Windows 10 的表中（[reqos]）。

### 3.4 哪些情况需要按版本分别构建
- 既要支持 2022/2023，又想在 2024+ 上使用 4.8.1 构建（或 2022 之后新增的 API）时，需要**两个构建**：net472（2022–2023）和 net481（2024–2027）。
- 如果实测发现各版本 `Eplan.EplApi.*u` 的程序集标识（版本号、公钥令牌）不同，导致跨版本绑定失败，就需要按 EPLAN 版本分别构建（官方规则 3 暗示不会这样，但未核实）。
- .NET 8 构建（`*Netu` + `EplApiModuleActionNet`）只能用于 2027+；只有当 .NET Framework API 被宣布废弃时才需要（与 ADR-0004 一致）。目前**没有找到**废弃的说明。

### 3.5 其他与版本有关的影响
- **授权**：`APIExtension`（725）和 `HasOption` 在所有版本中一致，Bridge 的启动检查不需要按版本区分。签名流程按版本不同（File Exchange → EPLAN Cloud EADN Signing → 自动签名 + PAT），但只影响走运行时授权（签名）的分发方式（ADR-0007）。2026 起"直接或间接使用 EPLAN API 的所有 DLL/EXE 都要签名"，可能包括 `Bridge.Core`；需要结合 ADR-0007 判断。
- **变体**：从 2026 起 Eplan Fluid 并入 Electric P8（[reqcur] 脚注）。2022–2025 上 `install-addin` 需要处理的变体列表不同（变体名称未核实）。
- **2027 的脚本运行在 .NET 8 / Roslyn 上**：本机遇到的 CodeDOM / `TEMP` 导致的 CS2001 问题（api-notes A1）在 2027 中可能不再出现（未验证）。2027 的 Remoting 需要 ASP.NET Core Runtime（[net27]），会影响基于 Remote Client 的工具（spike、`doctor` 中相关的检查），不影响走命名管道的 Bridge。

### 3.6 建议的分档（供 ADR 讨论）
- **第一档（一个 net481 构建）**：2024、2025、2026、2027。前提是先去掉对 `EplanMainThreadDispatcher` 的硬依赖，并在每个版本上实机验证。
- **第二档（需要 net472 构建）**：2022、2023。可以单独做一个 net472 构建，也可以把 net472 作为唯一构建覆盖全部六个版本；代价见 3.3。

---

## 4. 只能靠实机安装回答的问题

1. 2022、2023、2024 的 `Eplan.EplApi.Baseu.dll` 中是否存在文档未收录的 `Eplan.EplApi.Base.Internal.EplanMainThreadDispatcher`？这些版本在 `OnInitGui` 时主线程上是否已经有 WPF `Dispatcher`，`Dispatcher.Invoke(priority, timeout, …)` 的行为是否与 2026 相同（api-notes A9）？
2. 各版本 `Eplan.EplApi.*u.dll` 的程序集标识（版本号、是否强名称、公钥令牌）：针对 2026 编译的插件能否绑定到 2024、2025、2027 的程序集？针对 2022 编译的 net472 插件能否在 2026、2027 上加载？
3. net481 插件在 2022/2023 上失败的具体方式（EPLAN 加载器拒绝，还是 CLR 层面失败），失败时 EPLAN 是否给出明确提示。官方的向前兼容说法（规则 3）在 2027 上是否成立。
4. 各版本 Bin 目录中的 `Newtonsoft.Json` 是否存在、程序集版本是多少（2022、2023、2024 Update2 前后、2025、2027）。
5. 2027：现有 net481 Bridge 能否不重新编译、通过 `EplApiModuleAction` 加载？2027 同时装有 .NET 8 Desktop Runtime 时，.NET Framework 插件是否仍在承载主 UI 线程的同一个 CLR 中运行？`GetMainThreadDispatcher()` 返回的是否仍是可用的 .NET Framework `System.Windows.Threading.Dispatcher`？
6. 只在 2026 上验证过的行为在其他版本中是否一致：`SafetyPoint` / `UndoStep` 的回滚规律（A7）、action 之外必须先创建 `LockingStep`（A9）、模态对话框时委托被重入执行（A9）、`SysMessagesCollection` 交叉使用导致的访问冲突（A11）、`ConnectionsFilter.Page` 漏掉连接、action 改写 `User.eod`（A11）、`ProjectFullName` 不带扩展名（A10）。
7. 授权：其他版本和其他授权类型下，未签名插件是否受 5 页限制？客户常见授权中是否都有 `APIExtension`？EADN 签名过的程序集是否与 EPLAN 版本无关？
8. 各版本的变体名称（2026 之前 Fluid 是独立变体）、注册信息的存放位置（2026 是 `Platform\<版本>\Cfg\EplanEplApiModule.xml`），以及 `/Variant:` 参数取值。
9. 2022–2025 和 2027 的 API DLL 是否都是 x64（只有 2026 经本机反射确认）。
10. 2022–2025 上脚本编译是否同样受 `TEMP` 问题影响；2027 改用 Roslyn / .NET 8 后是否不再受影响。
11. EPLAN 2022 在当前 Windows 11 版本上能否正常安装和取得授权（官方 OS 兼容表没有把 2022 列在当前 Windows 版本下）。

---

[net22]: https://www.eplan.help/en-us/Infoportal/Content/api/2022/EplanApiDotNet.html
[net23]: https://www.eplan.help/en-us/Infoportal/Content/api/2023/EplanApiDotNet.html
[net24]: https://www.eplan.help/en-us/Infoportal/Content/api/2024/EplanApiDotNet.html
[net25]: https://www.eplan.help/en-us/Infoportal/Content/api/2025/EplanApiDotNet.html
[net26]: https://www.eplan.help/en-us/Infoportal/Content/api/2026/EplanApiDotNet.html
[net27]: https://www.eplan.help/en-us/Infoportal/Content/api/2027/EplanApiDotNet.html
[asm22]: https://www.eplan.help/en-us/Infoportal/Content/api/2022/Assemblies.html
[asm23]: https://www.eplan.help/en-us/Infoportal/Content/api/2023/Assemblies.html
[asm24]: https://www.eplan.help/en-us/Infoportal/Content/api/2024/Assemblies.html
[asm25]: https://www.eplan.help/en-us/Infoportal/Content/api/2025/Assemblies.html
[asm26]: https://www.eplan.help/en-us/Infoportal/Content/api/2026/Assemblies.html
[asm27fw]: https://www.eplan.help/en-us/Infoportal/Content/api/2027/Assemblies%20for%20.NET%20Framework.html
[asm27n8]: https://www.eplan.help/en-us/Infoportal/Content/api/2027/Assemblies%20for%20.NET%208.html
[n8base27]: https://www.eplan.help/en-us/Infoportal/Content/api/2027/Eplan.EplApi.BaseNetuDummyAssembly.html
[ema27]: https://www.eplan.help/en-us/Infoportal/Content/api/2027/EplApiModuleAction.html
[eman27]: https://www.eplan.help/en-us/Infoportal/Content/api/2027/EplApiModuleActionNet.html
[uea27]: https://www.eplan.help/en-us/Infoportal/Content/api/2027/UsingEplanAssemblies.html
[sign22]: https://www.eplan.help/en-us/Infoportal/Content/api/2022/Signing_EPLAN_assembies.html
[sign23]: https://www.eplan.help/en-us/Infoportal/Content/api/2023/Signing_EPLAN_assembies.html
[sign24]: https://www.eplan.help/en-us/Infoportal/Content/api/2024/Signing_EPLAN_assembies.html
[sign25]: https://www.eplan.help/en-us/Infoportal/Content/api/2025/Signing_EPLAN_assembies.html
[sign26]: https://www.eplan.help/en-us/Infoportal/Content/api/2026/Signing_EPLAN_assembies.html
[sign27]: https://www.eplan.help/en-us/Infoportal/Content/api/2027/Signing_EPLAN_assembies.html
[scr22]: https://www.eplan.help/en-us/Infoportal/Content/api/2022/Scripts.html
[scr23]: https://www.eplan.help/en-us/Infoportal/Content/api/2023/Scripts.html
[scr24]: https://www.eplan.help/en-us/Infoportal/Content/api/2024/Scripts.html
[scr25]: https://www.eplan.help/en-us/Infoportal/Content/api/2025/Scripts.html
[scr26]: https://www.eplan.help/en-us/Infoportal/Content/api/2026/Scripts.html
[scr27]: https://www.eplan.help/en-us/Infoportal/Content/api/2027/Scripts.html
[sc22]: https://www.eplan.help/en-us/Infoportal/Content/api/2022/ShadowCopyingAPIAssemblies.html
[sc26]: https://www.eplan.help/en-us/Infoportal/Content/api/2026/ShadowCopyingAPIAssemblies.html
[news23]: https://www.eplan.help/en-us/Infoportal/Content/Plattform/2023/Content/htm/news_p_api_2023.htm
[news24]: https://www.eplan.help/en-us/Infoportal/Content/Plattform/2024/Content/htm/news_p_api_2024.htm
[news25]: https://www.eplan.help/en-us/Infoportal/Content/Plattform/2025/Content/htm/news_p_api_2025.htm
[news26]: https://www.eplan.help/en-us/Infoportal/Content/Plattform/2026/Content/htm/news_p_api_2026.htm
[news27]: https://www.eplan.help/en-us/Infoportal/Content/Plattform/2027/Content/htm/news_p_api_2027.htm
[req24]: https://www.eplan.help/en-us/infoportal/content/swreqs/Content/htm/SwReqs_k_win10_2024.htm
[req25]: https://www.eplan.help/en-us/infoportal/content/swreqs/Content/htm/SwReqs_k_win10_2025.htm
[reqcur]: https://www.eplan.help/en-US/Infoportal/Content/SwReqs/Content/htm/SwReqs_k_general.htm
[reqos]: https://www.eplan.help/en-US/Infoportal/Content/SwReqs/Content/htm/SwReqs_k_os.htm

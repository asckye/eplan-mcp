# Spike S14：插件注册

## 问题
[M5 设计](../../docs/design/2026-10-20-m5-operability.md) 4.2 的 `eplanmcp install-addin` 用 EPLAN 官方的命令行注册插件（`EPLAN.exe /Variant:"…" EplApiModuleAction /register:"<dll>"`，api-notes B2）。需要先知道：
1. 注册保存在哪里，标准用户能否写入；
2. 注册命令能否不留下打开的 EPLAN，用时多少，是否必然出现登录或授权对话框；
3. 已有 EPLAN 在运行时执行注册命令的结果；
4. `/unregister` 是否完全撤销（I9）；
5. 原路径替换 DLL 后重启 EPLAN 即加载新版本，不需要重新注册；
6. 从 Claude 桌面版的会话（MSIX 环境）中执行时，注册落在哪里；
7. 带下载标记（`Zone.Identifier`）的插件 DLL 能否加载。

## 方法
2026-10-21，开发机，EPLAN 2026.0.3.25421 Electric P8，当前用户（非提升权限）。用 spike 插件的一次性副本，不碰 EplanMcp 插件的注册。
- `commandline.ps1`：EPLAN 已打开夹具时，用命令行注册再撤销 S12 spike 插件的副本，记录 EPLAN 进程、用时，以及工作站设置中的插件键。
- 在新起的 EPLAN 实例中，用命令行调用 S12 spike 的动作，确认撤销是否生效（日志 S085021"无法找到操作"）。
- `zone.ps1`：给 S11 spike 插件的副本加上 Internet 区域标记（ZoneId=3），经 Remote Client 注册到运行中的 EPLAN 并调用它的动作，再撤销。
- 比较 EPLAN 加载的影子副本（`%APPDATA%\EPLAN\ShadowCopyAssemblies\<进程号>\`）与重新构建的插件 DLL 的 SHA-256。
- 读 EPLAN 的系统日志 `EplLog.eldb`，以及设置目录的 ACL（`icacls`，只读）。

## 结果
| # | 结论 |
|---|---|
| 1 | 注册保存在**工作站设置**中：`C:\Users\Public\Eplan\Settings\<变体>\<版本>\x64\Cfg\STATION.<…>.<计算机名>.xdb\Setting.eod`，键名形如 `EplanEplApiModule.Module.EplanMcp@Bridge@Eplan.AssemblyPath`。**它按机器保存，不按用户保存**：这台电脑上所有 Windows 用户的 EPLAN 都读这一份。目录继承 `C:\Users\Public` 的 ACL，`INTERACTIVE`、`SERVICE`、`BATCH` 都有修改权限，所以标准用户和以服务身份运行的程序都能写；本 spike 也是以非提升权限完成的 |
| 2 | **注册命令会留下一个打开的 EPLAN**：命令启动一个新的 EPLAN 实例，120 秒后仍未退出；新实例没有打开项目，按"关闭窗口"可以正常退出。新实例在约 6 秒时已加载 EplanMcp 插件、Bridge 开始监听。这次没有出现登录或授权对话框（开发机的借用授权，用户已登录） |
| 3 | EPLAN 已在运行时，注册命令**另起一个新实例**，不交给已运行的实例；注册在新实例中生效并立即加载被注册的插件。已运行的实例照常工作 |
| 4 | `/unregister` 生效：之后新起的 EPLAN 中调用该插件的动作，日志报 S085021"无法找到操作"。但工作站设置中这个插件的键（包括 `AssemblyPath`）**仍然保留**，只是不再起作用；按键名判断不出是否已注册 |
| 5 | ✅ 在原路径重新构建插件、重启 EPLAN 后，加载的影子副本与新 DLL 的 SHA-256 相同，期间没有重新注册 |
| 6 | 注册落在 `C:\Users\Public\…` 下的工作站设置中，不在会被 MSIX 重定向的 `%APPDATA%`、`%LOCALAPPDATA%` 中。本 spike 和此前的开发注册都是从 Claude 桌面版会话中（MSIX 环境）执行的，项目负责人在包外启动的 EPLAN 照常加载这些插件 |
| 7 | 🔴 **带下载标记的 DLL 不能加载**：注册返回失败，日志记 S043005"加载插件 … 时出错"，原因是 `FileLoadException`（HRESULT 0x80131515）和 `NotSupportedException`："尝试从一个网络位置加载程序集……请启用 loadFromRemoteSources"。影子副本连同 `Zone.Identifier` 一起被复制。注册失败时工作站设置中没有留下任何键 |

## 结论
1. **`install-addin` 不能在该变体的 EPLAN 运行时注册**：那会再起一个装着全部插件（包括 EplanMcp）的 EPLAN 实例。应当先请工程师关闭 EPLAN，再执行注册命令；命令启动的实例没有项目，等它的 Bridge 开始监听（证明插件已加载）后，由 `install-addin` 正常关闭这个实例。这样安装程序不必请工程师在注册后再关一次 EPLAN（设计 4.2 原来的退路）。
2. **注册前去掉下载标记**：`install-addin` 在注册前去掉 `app\bridge\` 下所有文件的 `Zone.Identifier`（相当于 `Unblock-File`），`doctor` 发现插件文件带标记时给出警告。Inno Setup 安装包写出的文件不带标记；用资源管理器解压压缩包时会带上（设计"只发压缩包"一节的风险）。
3. **注册是整台机器的**：多个 Windows 用户共用一台电脑时，另一个用户的 EPLAN 也会去加载装在第一个用户 `%USERPROFILE%\EplanMcp\app\` 中的插件（通常没有读取权限，加载失败）。对按用户安装（ADR-0016）的影响见设计 4.2 的修改。
4. 撤销后键仍保留，所以 `doctor` 和 `uninstall-addin` 判断"是否已注册"要看 `state\addins.json` 和插件实际是否加载（Bridge 握手），不能看设置中的键。
5. 设计 4.2 中"探测到 MSIX 重定向时拒绝执行"对注册本身不再必要（结果 6）。

## 局限
- 只有一台开发机、一份借用授权、Electric P8 一个变体；另一个 Windows 用户的情况由 ACL 推断，没有实际登录另一个用户测试。
- 没有测网络授权、运行时授权下是否出现授权对话框。
- 设置数据库是二进制格式，只按键名做了比较。

## 复现
```powershell
dotnet build spikes\S12-destructive-backup -c Release
dotnet build spikes\S11-workflow-apis -c Release
spikes\S14-addin-registration\commandline.ps1      # EPLAN 已打开时运行；之后关闭它留下的实例
spikes\S14-addin-registration\zone.ps1             # 需要 Remote Client 辅助程序和打开的 EPLAN
```

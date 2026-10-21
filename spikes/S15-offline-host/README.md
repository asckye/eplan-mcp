# Spike S15：离线宿主

## 问题
[M5 设计](../../docs/design/2026-10-20-m5-operability.md) 4.9 的离线宿主 `EplanMcp.Bridge.Offline` 要用 `EplApplication.Init` 无界面地运行 EPLAN（api-notes B5）。需要先回答：
1. 不允许授权和登录对话框（`allowLicenseDlg`、`allowLoginDlg` 都为 `false`）时，`Init` 在开发机上能否成功；
2. 是否另占一个授权；
3. 能否与界面中的 EPLAN 同时运行；
4. 启动耗时；
5. 回滚、撤销、`LockingStep` 的规律是否与插件中相同（A7、A9）；
6. 未签名的离线程序在开发机授权下能否运行（B8、ADR-0007）；
7. 在没有登录桌面的上下文中（runner 作为服务）能否运行。

这些结论决定自托管 runner 是否值得安装（M5 设计 Q8：项目负责人同意，先做 S15）。

## 方法
- `EplanMcp.Spikes.S15.exe`：net481、x64、`[STAThread]` 的控制台程序，未签名。先用 EPLAN 的 `AssemblyResolver` 指向 EPLAN，再在单独的方法中运行 `Probes`，避免解析器就位前加载 EPLAN 的类型。`Probes` 依次执行：
  1. `Init(string.Empty, false, false)`；
  2. 用 `HasOption`、`GetLicenseModules` 读授权（只检查，不占用，A2）；
  3. 在 `LockingStep` 中打开项目；
  4. 在 `LockingStep` 之外读页；
  5. 在 `SafetyPoint` 中建页再回滚；
  6. 按 Bridge 的写法（`SafetyPoint` 内建 `UndoStep`）建页、提交，再调用 `UndoStep.DoUndo()`；
  7. `UndoStep` 建在 `SafetyPoint` 外面，建页再回滚；
  8. 关闭项目、`Exit()`。

  每一步完成后都重写结果文件。
- `run.ps1`：
  - 把示例项目复制到 `%USERPROFILE%\EplanMcp\scratch\spikes\S15\<时间>\`，探测只写这份副本；
  - 运行前后记录 EPLAN 设置目录（`%APPDATA%\EPLAN`、`C:\Users\Public\Eplan\{Settings,Common,Electric P8}`）中每个文件的大小、时间和 SHA-256，并保留一份副本；
  - 程序超时时列出它的窗口并结束它；
  - 最后运行 `eplanmcp doctor`，确认界面中的 EPLAN 仍然可用。
- 环境：EPLAN 2026.0.3.25421，Electric P8；开发机，当前用户在交互桌面（会话 3）；界面中的 EPLAN（打开评测夹具，已加载 EplanMcp 插件）同时运行；2026-10-20。共运行 4 次：
  - 前两次分别因为程序集解析和 Bin 路径失败（见结果第 8 条）；
  - 第 3 次在新副本上完成；
  - 第 4 次在第 3 次的副本上再运行，看提交的修改是否已经保存。

## 结果
| # | 结论 |
|---|---|
| 1 | ✅ `Init` 不弹出任何窗口，2.0–2.3 秒内成功。`QuietMode` 为 `ShowNoDialogs`，`InOfflineMode` 为 `true`。EPLAN 系统日志记录"Application started from API offline application"，并以当前 Windows 用户登录（S332010）；退出时只有一条警告 S085043：接口 `IEIdentityClientService` 没有注册 |
| 2 | 部分回答。离线程序看到的授权与插件中相同（A2）：3214 个选项中 2683 个可用，`APIExtension`、`ElectricP8` 可用。`EplApplication.License` 返回的字符串是"Eplan Demo System"，`Variant` 返回"Basic"；但第 4 次运行看到第 3 次提交的页面仍在，所以修改是保存了的，这两个字符串不表示演示版的限制。这份授权是借用授权（A2），同一用户的两个进程都能使用；网络授权是否算两个，没有条件验证 |
| 3 | ✅ 界面中的 EPLAN（进程 38404）一直运行，前后进程号不变；之后 `doctor` 的握手成功，`APIExtension` 仍然可用 |
| 4 | `Init` 2.0–2.3 秒；打开 321 页的示例项目 1.6–2.6 秒；关闭 0.25 秒，`Exit` 0.25 秒；整个程序 4.5–5.8 秒 |
| 5 | 与插件中相同：`SafetyPoint` 中建页再回滚，页数复原（321 → 322 → 321）；`UndoStep` 建在 `SafetyPoint` 外面时，回滚静默失效，页面留下（A7 的结论在离线程序中同样成立）。刚打开项目后，在 `LockingStep` 之外读页没有报错；Bridge 照旧以 `LockingStep` 开始每个操作。提交并 `CloseUndo()` 之后调用 `UndoStep.DoUndo()`，页面仍在：离线程序中没有 Ctrl+Z 的替代办法，与设计 4.9"测不了 Ctrl+Z"一致 |
| 6 | ✅ 未签名的离线程序在开发机授权下运行，读写 321 页的项目，没有 5 页的限制 |
| 7 | 未测：需要一个单独的 Windows 标准用户（M5 设计 Q8 中 runner 的账户）和服务上下文，要由项目负责人创建账户 |
| 8 | 写离线宿主要注意：`EplanBinFolder`（以及传给 `AssemblyResolver.SetEplanBinPath` 的路径）必须是**变体的** Bin（`C:\Program Files\EPLAN\Electric P8\2026.0.3\Bin`，其中有 `W3u.exe`），给 Platform 的 Bin 时 `Init` 抛出 `ArgumentException`："The Variant in '…' is not valid."。`AssemblyResolver` 只解析 EPLAN 自己的程序集，Platform Bin 中的 `Newtonsoft.Json` 要自己加 `AssemblyResolve` 处理（第 1 次运行就因为它失败，而且 `Exception.ToString()` 也失败了）。`GetPlatformBinPath()` 返回 `C:\Program Files\EPLAN\Platform\2026.0.3\Bin`。`Init` 把当前线程的区域性改为界面的区域性（zh-CN） |
| 9 | 副作用：为本用户注册的插件被影子复制到 `%APPDATA%\EPLAN\ShadowCopyAssemblies\<离线进程号>\`，但 EplanMcp 插件在 `OnInitGui` 中启动 Bridge，离线程序不调用它，所以没有出现第二个 Bridge 管道，插件日志中也没有记录。EPLAN 设置文件没有变化；改动的只有系统日志 `EplLog.eldb`、`PerfLogSession.xml` 和 `C:\Users\Public\Eplan\Common\SN-00CPM.EGF`（界面中的 EPLAN 运行期间也会定期改写它） |

## 结论
1. 离线宿主在开发机上可行：同一用户、同一份借用授权下，不弹出对话框，约 2 秒启动，可以与工程师界面中的 EPLAN 同时运行，互不影响。回滚的规律与插件相同，`Bridge.Core` 和适配层可以照原样共用（架构 5.4）。
2. 离线宿主的启动代码要按结果第 8 条：传变体的 Bin，自己解析 Platform Bin 中的第三方程序集；`Init` 会改当前线程的区域性，所以离线宿主中的数字、日期格式不能依赖当前区域性。
3. 离线宿主不需要关掉插件：插件的 Bridge 只在 `OnInitGui` 中启动。CI 中的 Host 仍按 `EPLANMCP_PIPE_PREFIX` 只找离线宿主的管道（设计 4.9）。
4. 自托管 runner 的问题还没有答完：在单独的标准用户和服务上下文中能否拿到授权（第 2、7 项）。需要项目负责人：
   - 创建 runner 用的 Windows 标准用户；
   - 在该用户下运行一次 `run.ps1`，或者给出其他运行方式。

   这一步通过后再安装 runner（Q8）。

## 局限
- 只验证了开发机的这一份借用授权；运行时授权、网络授权、另一用户的授权都没有验证。
- 没有测 Ctrl+Z（离线程序没有界面）、模态对话框、插件的注册与加载，这些本来就在设计 4.9 的"测不了"中。
- 每次只打开一个项目；长时间运行和空闲退出留给离线宿主本身的测试。

## 复现
```powershell
dotnet build spikes\S15-offline-host -c Release
spikes\S15-offline-host\run.ps1 -Eplanmcp src\EplanMcp.Host\bin\Release\net10.0\eplanmcp.exe
spikes\S15-offline-host\run.ps1 -Project <上一次的副本路径，不带扩展名>
```
输出在仓库的 `.local\spikes\S15\<时间>\`：`result.json`、`stderr.txt`（每一步一行）、`summary.json`（设置文件的变化、EPLAN 进程、`doctor` 的输出）。

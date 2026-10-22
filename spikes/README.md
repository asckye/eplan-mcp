# Spikes（技术验证）

每个 spike 只回答**一个**问题，代码是一次性的，**不进入产品**。但它要保留下来，保证结论可以复现。
结论写进 [api-notes](../docs/eplan/api-notes.md)，影响决策的写进 ADR。

| 编号 | 问题 | 状态 | 结论 |
|---|---|---|---|
| [S0](S0-licence/README.md) | 未签名的编译插件能否在当前授权下加载？能否读写超过 5 页的项目？ | **完成** 2026-10-13 | 能加载；321 页项目读写均成功，没有页数限制；附带发现：未提交的 SafetyPoint 没有回滚（交给 S2） |
| [S1](S1-dispatcher/README.md) | 插件内主线程调度（`EplanMainThreadDispatcher`）是否就绪、带超时的调度是否可靠；**并在调度器中复核 S2 的回滚与撤销结论** | **完成** 2026-10-13 | 可行，延迟约 0.3 ms，超时可以干净地放弃。4 条硬规则：委托内部捕获全部异常（否则 EPLAN 崩溃，实测发生过一次）；LockingStep 作为第一步；UndoStep 在 SafetyPoint 内；主窗口被禁用时拒绝执行（否则会在模态对话框中重入） |
| [S2](S2-transactions/README.md) | `SafetyPoint` / `UndoStep` / `LockingStep` 的实际回滚与撤销语义 | **完成** 2026-10-13（action 中） | UndoStep 必须在 SafetyPoint **内**创建，否则回滚静默失效；这样写时 Ctrl+Z 能撤销 AI 的操作，并保留原有历史。插件程序集在 EPLAN 退出前无法卸载 |
| S3 | 编译时引用、运行时使用 EPLAN 自带的 Newtonsoft.Json 是否可行 | **完成** 2026-10-13 | S0、S1、S2 三个插件都引用 EPLAN Bin 中的 Newtonsoft.Json（`Private=false`，不复制），全部运行正常 |
| [S4](S4-elicitation/README.md) | 目标客户端对 MCP 2026-07-28 多轮请求 elicitation 的支持 | **协议层完成** 2026-10-13；界面确认**不再需要**（2026-10-21，ADR-0011） | Claude Code 2.1.295 原生使用 2026-07-28，声明 `elicitation: {form, url}`；SDK 的 `InputRequiredException` 一条路径覆盖两种协议，`requestState` 都能带回。这些协议层结论仍然有效，但 [ADR-0011](../docs/adr/0011-approval-out-of-band.md) 决定审批不用 elicitation，所以表单界面的人工确认不再需要 |
| [S5](S5-msix-host/README.md) | MSIX 打包的客户端（Claude 桌面版）启动 Host 时的文件系统重定向 | **完成** 2026-10-19 | Host 正常工作：数据根目录和 `%TEMP%` 不重定向，命名管道正常。客户端启动的进程**没有包身份**，写入 `%LOCALAPPDATA%`、`%APPDATA%` 却仍被重定向，`doctor` 要改为直接探测 |
| [S6](S6-read-api/README.md) | M2 只读 API 在真实项目上的耗时、名称取值、多语言格式和副作用 | **完成** 2026-10-19 | 全部读取在 100 ms 以内；设备标识用 `Function.Name`；多语言用 `GetStringToDisplay`；运行检查是写操作。🔴 新建第二个 `SysMessagesCollection` 后再读第一个集合的消息文本，会触发 EPLAN 内部的访问冲突。`ProjectOpen` 不一定改变当前项目 |
| [S8](S8-write-interruption/README.md) | 写操作做到一半时 EPLAN 被强制结束（提交前、提交后、访问冲突），项目处于什么状态 | **完成** 2026-10-20 | 🔴 `SafetyPoint` 只防异常、不防进程死亡：提交前被结束，已做的修改也留在项目里；访问冲突直接结束 EPLAN。ADR-0010 的 B3 不可行，`OUTCOME_UNKNOWN` + 暂停 + 人工核实是对的；解除暂停的提示不能再说"用 Ctrl+Z 撤销"。重开被结束时开着的项目，EPLAN 会问"检查数据" |
| [S9](S9-write-api/README.md) | M3 的写入 API：用完整页名建页、改设备标识、按符号放置设备、插入宏、生成连接，以及各自能否回滚 | **完成** 2026-10-20 | 建页、改标识、插宏、生成连接都可行。🔴 `SafetyPoint` 和 `Transaction` 各管一半：插宏只有 `SafetyPoint` 能撤回，按符号新建设备在 `SafetyPoint` 中必定失败、只能用 `Transaction`。改标识要连同辅助功能一起改；页名和设备标识中项目结构不用的部分会被静默丢掉，EPLAN 不检查设备标识重名，都要读回比较 |
| [S10](S10-dispatcher-writes/README.md) | M3b 的写入在 Bridge 调度器中（不在 action 中）：插宏的编号方式、Ctrl+Z、生成连接的回滚、读宏内容、页幅面、按符号放置的撤销与标识显示 | **完成** 2026-10-20 | 插宏用 `Number` 编号不产生重复设备（`None` 会）；插宏、生成连接各占一个撤销步骤，都能被 `SafetyPoint` 回滚；不插入就能读出宏的内容；空页读不到幅面。按符号放置的设备能撤销，但图上不显示标识，Q1 继续推迟 |
| [S11](S11-workflow-apis/README.md) | M4 的检查、报表、导出 API 在调度器中的行为：耗时、改动、回滚、撤销、导出文件的位置 | **完成** 2026-10-20 | 检查和更新报表都能被 `SafetyPoint` 回滚、一步撤销（M4 Q3 成立）；导出只在给定目录中产生文件，不改设计数据，但改写 `User.eod`；整个示例项目导出 PDF 12.5 秒，更新报表约 9 秒 |
| [S12](S12-destructive-backup/README.md) | Destructive 操作前的备份（`CopyProject`、`Backup.Project`），删页和重编号能否回滚、撤销 | **完成** 2026-10-20 | `CopyProject(Snapshot)` 在打开的项目上 1 秒内得到完整、可打开的副本，不加锁（ADR-0013 方案 1 成立）；`MakeBackup` 慢且改写源项目文件；删页、重编号都能回滚、一步撤销；删页留下没有主功能的辅助功能；`PostNumerate` 不保证编完所有 `?` |
| [S13](S13-client-progress/README.md) | Claude Code 是否带进度令牌、中断时是否取消、图片能否交给模型 | **完成** 2026-10-21 | 每次调用都带 `progressToken`，心跳送达；`inline` 的 PNG 模型能读懂；中断约 0.2 秒后发出 `notifications/cancelled`；150 秒的调用正常完成，进度一直送达 |
| [S14](S14-addin-registration/README.md) | 插件注册：保存在哪里、命令行注册的行为、撤销、替换 DLL、下载标记 | **完成** 2026-10-21 | 注册按机器保存在工作站设置中，标准用户可写；命令行注册总会另起一个不退出的 EPLAN 实例；撤销有效但键保留；原路径替换 DLL 后重启即生效；🔴 带下载标记的 DLL 无法加载 |
| [S15](S15-offline-host/README.md) | 离线宿主：`EplApplication.Init` 的授权与登录对话框、与界面中的 EPLAN 同时运行、启动耗时、回滚与撤销 | **部分完成** 2026-10-20 | 开发机上可行：不弹窗，约 2 秒启动，与界面中的 EPLAN 互不影响，回滚规律与插件相同；要传变体的 Bin。另一用户、服务上下文下的授权未测，要项目负责人建 runner 账户 |
| [S17](S17-client-plugin/README.md) | 客户端与插件：本地插件市场能否离线安装，插件 `.mcp.json` 中哪些变量会展开，桌面版的配置文件在哪里 | **部分完成** 2026-10-20 | 命令行不登录、网络不可达时也能装好，插件从原处加载；`${USERPROFILE}`、`${CLAUDE_PLUGIN_ROOT}` 和任意环境变量在 command、args、env 中都会展开，`%VAR%` 不会，所以 `.mcp.json` 不必由安装程序生成；桌面版的配置文件只在包的 `LocalCache\Roaming\Claude\` 中。在没有网络的虚拟机上安装还没有做 |
| [S18](S18-workbench-ui/README.md) | Workbench 审批卡片：WinUI 3（A）、WPF + TIA Workbench 主题（B）、WPF + .NET Fluent 主题（B1）能否只用 `dotnet` 构建发布，体积、冷启动、UIA、Maple Mono CN、主题切换、托盘 | **部分完成** 2026-10-20 | 三个原型都能只用 `dotnet` 构建和自包含发布，UIA 能读出按钮、可聚焦的标题和倒计时。B（项目负责人选定）可行：TIA 的 15 个文件只改两行字体，另需 7 个 TIA 类型的替身；Maple Mono CN 从程序目录加载，两个字重多 35.4 MB；窗口约 0.6 秒出现，比 A 慢约 0.3 秒。A 的 Windows App SDK 不是 MIT 许可，发布需 `EnableMsixTooling`，单文件不能启动。Windows 10、200% 缩放、系统高对比度、讲述人、离线未测 |
| [S19](S19-vm-http/README.md) | 虚拟机连接（ADR-0018）：Kestrel 加 SDK 的 HTTP 传输在自包含单文件中的体积和启动，密钥、失败次数限制和 `Origin` 的中间件，SDK 的会话，Claude Code 和其他客户端的远程配置，Host 中哪些状态要按会话保存 | **部分完成** 2026-10-21 | 体积 +26.5 MB（74.3 → 100.7 MB），约 0.15 秒开始监听，stdio 不变慢；第 4、5 条的 23 项探测全部符合预期。🔴 SDK 2.2.0 的 HTTP 传输默认无状态（2026-07-28 版协议取消了会话），要设 `SessionMode = Stateful` 才能按会话保存选中的实例，Claude Code 2.1.299 这时退回 2025-11-25 并带 `Mcp-Session-Id`。Claude Code 不发 `Origin`，请求头中的 `${VAR}` 会展开；Claude 桌面版连不到私有网络中的服务器。真实虚拟机、经 UAC 的防火墙规则、明文抓包、其他客户端的实测没有做 |
| [S20](S20-symbol-placement/README.md) | 不用窗口宏放置单个设备：按库符号新建后加属性放置；`DeviceService.CreateDevice` 按部件号放置 | **完成** 2026-10-21 | 属性放置能让标识显示，但位置和显示的属性还要查；按部件放置效果与手工相同，能回滚、能撤销，但不取页的结构，要放置后改名 |
| [S21](S21-cables-terminals/README.md) | 电缆、端子、PLC 的读取；端子、电缆、线号的编号和生成电缆能否回滚；M4c 用到的授权 | **完成** 2026-10-21 | 都能读出，电缆要按类别取（空过滤方案随界面变化）；三种编号都能被 `SafetyPoint` 回滚，提交后能一步撤销，空方案等于界面上次的方案；授权中没有 `Preplanning` |
| [S22](S22-imports/README.md) | M4c 第三批的导入：DXF、部件清单、PXF；PDF 注释、设备清单、PLC 数据待找到输入文件 | **部分完成** 2026-10-21 | DXF 导入能回滚、能一步撤销，可定为 Write；PXF 导入的第二个参数是新项目的路径，约 1 分钟，不自动打开；部件清单导回同一份清单看不出变化 |
| [S23](S23-translation-revisions/README.md) | 翻译、项目语言与修订：改了什么、能否回滚 | **完成** 2026-10-21 | 示例项目已全部翻译，翻译要另造场景再测；🔴 删除语言只回滚了文本，语言设置没有回滚；设置显示语言的写法待查；🔴 新建修订把项目改为 `.ell`、不能撤销；修订状态的读法未找到；r2：翻译的前提是项目的源语言（未设置时什么也不翻译），设为 de_DE 后按翻译库译进全部翻译语言；r3：修订状态看链接文件扩展名（.elk/.ell/.elr），修订记录在 PROJ_REVISION_LOG_*[i] |
| [S24](S24-master-data/README.md) | 部件库与翻译库：配置的是哪个库、只读访问的耗时与字段；备份、恢复与写入（ADR-0019 的前提） | **完成** 2026-10-21 | 两个库都是本机文件；全部 464 个部件 0.07 秒；`backup MASTERDATA` 在调度器中可用（0.1–0.2 秒，7z 格式的 `.zw6`/`.zw7`），还原到空目录逐字节相同，但🔴还原不覆盖已有文件；部件库事务能撤回添加，🔴不能撤回 `RemovePart` 和导入；按路径打开副本不改配置，但🔴在副本上导入也改了正在用的库的 `User` 表（已经批准恢复）；导出 XML 2 秒、翻译库导出不到 1 秒；经批准在正在用的库上：不在事务中的导入正常，🔴同步部件还会写项目，翻译库导入未验证（已恢复，逐字节核对） |
| [S27](S27-project-create/README.md) | 从基本项目新建项目、还原项目备份、不打开项目判断版本（M6） | **完成** 2026-10-21 | CreateProject + 基本项目 3.5 秒；还原项目 6 秒，🔴目标已存在时覆盖；GetProjectDataCompatibility 不打开项目；.zw1/.zw9 是 7z；ProjectInfo.xml 的属性 10043 是 EPLAN 版本 |
| [S28](S28-identity-extract/README.md) | 对象标识在项目副本之间是否稳定、整个项目的读取耗时（M6） | **完成** 2026-10-21 | 文件副本、备份还原、PXF 导出导入之后 825 个对象的 ObjectIdentifier 全部相同，DatabaseIdentifier 不同；示例项目完整枚举约 1 秒 |
| [S29](S29-placeholders/README.md) | 占位符对象与值集：读宏、列出、应用、新建（M6） | **完成** 2026-10-21 | 不插入就能读出宏中的占位符和值集，扫描 695 个宏 21 秒；ApplyRecord、AddRecord + SetValue 可用，随 SafetyPoint 回滚 |
| [S30](S30-identity-lifecycle/README.md) | 对象编号与项目生命周期：PROJ_GUID、关闭再打开、删除再新建、从基本项目新建（M6） | **完成** 2026-10-21 | PROJ_GUID 是"名称@秒数"，副本和还原沿用；重新打开编号不变、删除后不重用；同一基本项目新建的项目编号相同；旧版本升级受环境所限 |
| [S31](S31-snapshot/README.md) | 快照提取的完整性、确定性与修改标记（M6） | **完成** 2026-10-21 | 全部属性约 8–10 分钟、150 MB，属性对象必须释放（否则 EPLAN 内存耗尽退出）；选定属性约 5 秒；会话内确定，重新打开后运行时标识变化；页修改日期可指出改了哪一页 |
| [S32](S32-macro-create/README.md) | 模块写回为宏、新建占位符、授权选项（M6） | **完成** 2026-10-21 | WindowMacro/PageMacro.Create 写出的宏保留占位符和值集；API 新建占位符时属性条目写 <变量名>；Cogineer 有授权但未安装 |
| [S33](S33-events/README.md) | EPLAN 事件：名称、参数、线程、频率（ADR-0026） | **完成** 2026-10-21 | 通配符 * 可用；全部在主线程同步触发；修改触发 onDMObjectsAltered（回滚也触发，不带范围）；打开项目的事件带路径；Page.ConnectionDirty 不存在 |
| [S25](S25-exports/README.md) | M4c 第一批的导出：PXF、部件清单、设备清单、标签、PLC 地址、缺失词表 | **完成** 2026-10-21 | PXF 可行但很大（840 MB / 598 MB）；部件清单 60–90 秒，CSV 导出之后 EPLAN 发生过一次访问冲突（未复现），只用 XML；标签（文本、xlsx）2 秒内、不需要 Excel；设备清单是预规划清单；PLC 地址总览要给配置项目或站 |

## 约定
- 目录：`spikes/S<n>-<主题>/`，包含代码、驱动脚本和 `README.md`（问题、方法、结果、结论、局限）。
- 构建规则继承仓库根目录，只放宽"警告即错误"（见 [Directory.Build.props](Directory.Build.props)）。
- 运行产物写到仓库下的 `.local/`（已被 git 忽略），**不要写到 `%LOCALAPPDATA%`**，原因见 S0。
- 对 EPLAN 的持久修改（插件注册等）必须在驱动脚本的 `finally` 中撤销。

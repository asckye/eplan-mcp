# ADR-0016：用按用户安装的 Inno Setup 安装包分发，程序固定在 `%USERPROFILE%\EplanMcp\app\`，插件只经 `eplanmcp install-addin` 注册

| 项 | 内容 |
|---|---|
| 状态 | Accepted；2026-10-22 修订：只提供 ZIP（项目负责人决定） |
| 日期 | 2026-10-20（提出）· 2026-10-20（接受：项目负责人对 M5 设计的待定问题和三个 ADR 明确答复"全部按建议"）· 2026-10-21（修改第 3 条：项目负责人"按实测修改"） |
| 决策人 | 项目负责人 |
| 门的类型 | 单向门（第一个安装包发布后）：EPLAN 中的注册、客户端配置和插件都指向安装位置，改位置要迁移每台机器；安装包工具本身是双向门 |
| 影响 | 落实[架构第 7 节](../architecture/architecture.md#7-部署)和安装验收清单 I1–I10；补充 [ADR-0007](0007-licensing-and-distribution.md)（签名，决定不变）；[ADR-0008](0008-data-locations-and-instance-discovery.md) 的数据根目录不变 |

## 2026-10-22 修订：只提供 ZIP

项目负责人决定改为与 TIA MCP 相同的 ZIP 交付。本节优先于下文保留的原决策；插件来源仍按 [ADR-0020](0020-no-self-hosted-runner.md)，MSIX 注册规则仍按 S14。

1. 只交付 `EplanMcp-<version>.zip`，唯一顶层目录为 `EplanMcp-<version>\`；删除 Inno Setup 安装器。CI 保留 SHA-256、逐文件清单、SBOM 和两类证明。
2. 工程师先解除 ZIP 的下载锁定，再将整个目录解压、移至短且固定的位置（如 `D:\EplanMcp`），打开 `workbench\EplanMcp.Workbench.exe`，勾选变体注册 EPLAN 插件并配置客户端。不修改 PATH，不创建卸载项或开始菜单入口；登录自启动由 Workbench 设置管理。
3. `install-addin` 已从运行中的 `eplanmcp.exe` 旁寻找 `bridge\net481\EplanMcp.Bridge.Eplan.dll`，注册前移除整个 `bridge\` 的下载标记；不依赖固定目录。数据根目录仍为 `%USERPROFILE%\EplanMcp`，与程序目录分开；`EPLANMCP_HOME` 只改数据目录。
4. 升级前等待写入结束，关闭 EPLAN、Workbench 和客户端，用新 ZIP 整体替换原程序目录；保持路径不变。若改变路径，须从新位置重新注册并生成客户端配置。卸载先在 Workbench 撤销注册，成功后退出并删除程序目录；客户端配置、插件市场及登录自启动先由工程师移除，数据保留。
5. **Claude Code 方案（Part B 实现）**：`client-config` 的插件生成模式把随包模板复制到 `<数据根目录>\claude-plugin\`，将 `.mcp.json` 写为当前 Host 的实际绝对路径，Workbench 调用并显示本地市场安装命令。S17 中市场不复制，插件有缓存副本且当时从源目录加载；绝对路径在两处都有效，不使用 `${CLAUDE_PLUGIN_ROOT}/../..` 找程序，也不再假设 `${USERPROFILE}/EplanMcp/app`。普通 `client-config claude-code` 继续写实际绝对路径，与插件二选一。
6. 分两步审阅：Part A 只改流水线、回归检查和文档；Part B 补 Workbench 注册/撤销、任意程序目录的 Host 定位及插件生成，完成 Small/Medium 测试后才发布 ZIP。

## 背景与问题（原决策记录）
M5 要让非开发人员在干净的机器上装好 EplanMcp（[M5 设计](../design/2026-10-20-m5-operability.md)）。架构第 7 节已经定下：按用户安装到 `%USERPROFILE%\EplanMcp\app\`，不需要管理员权限，不修改 PATH，插件用 EPLAN 官方的 `EplApiModuleAction` 注册。还没有决定的是：用什么做安装包、安装位置能否改、升级和卸载怎样对待正在运行的程序和数据、Claude Code 插件怎样找到程序、产物在哪里构建、是否签名。这些一旦发布到用户机器上就难以改变，又属于供应链（TB5，SEC-15、SEC-16），所以写成 ADR。

相关事实：
- 插件按变体注册，注册后同一会话立即加载（api-notes A10、A12）；EPLAN 加载的是影子副本，运行中可以替换原始 DLL，重启后生效（B2、A8）。
- 由 MSIX 客户端启动的进程写 `%LOCALAPPDATA%`、`%APPDATA%` 被重定向，`%USERPROFILE%\EplanMcp` 不受影响（A6）。
- Claude Code 的本地目录形式的插件市场不复制，插件在原处加载；插件的 MCP 配置可以用 `${CLAUDE_PLUGIN_ROOT}`，其他环境变量能否展开文档没有说明（[插件加载](https://code.claude.com/docs/en/plugins/loading)、[清单参考](https://code.claude.com/docs/en/plugins/manifest-reference)）。
- 插件编译时要引用 EPLAN 的 API 程序集，GitHub 托管的 runner 上没有 EPLAN。
- TIA 只发压缩包，解压到任意目录，没有签名、SBOM 和来源证明。

## 决策驱动因素
- I1–I10：不需要管理员、不联网、升级直接覆盖、卸载干净、只用官方注册方式。
- EPLAN 的注册、客户端配置、插件，以及 Workbench 与 Host 的互相核对（[ADR-0014](0014-workbench-approval-channel.md)），都依赖一个稳定的安装位置。
- 不能为了安装而丢数据（审计、备份），也不能打断正在进行的写入（ADR-0010）。
- 只发布 CI 构建的产物（开发流程第 8 节、SEC-16）。

## 备选方案
以下事实来自 2026-10-20 查阅的官方文档。
1. **Inno Setup 安装包**（7.1.0；GitHub 的 windows-2025 镜像自带 6.7.1）：`PrivilegesRequired=lowest` 时从不请求提权，卸载信息写在 HKCU（[PrivilegesRequired](https://jrsoftware.org/ishelp/topic_setup_privilegesrequired.htm)、[非管理员模式](https://jrsoftware.org/ishelp/topic_admininstallmode.htm)）；`[Run]`、`[UninstallRun]` 可以在安装后、卸载前运行命令（[Run](https://jrsoftware.org/ishelp/topic_runsection.htm)）；升级时沿用上次的目录。默认的 `CloseApplications=yes` 在静默模式下会用重启管理器关闭占用文件的程序（[CloseApplications](https://jrsoftware.org/ishelp/topic_setup_closeapplications.htm)）。许可证允许任何用途；从 6.5.0 起作者请求商业用户购买许可，但许可证本身并不强制（[购买](https://jrsoftware.org/isorder.php)）。
2. **压缩包加 PowerShell 脚本**（TIA 的做法）。
3. **WiX 按用户的 MSI**（7.0.0）。
4. **MSIX**。
5. **Velopack**（MIT）。

## 决策
**建议方案 1。**

1. **安装包**：一个文件 `EplanMcp-Setup-<版本>.exe`，Inno Setup，`PrivilegesRequired=lowest`，从不提权；只装 x64；不显示目录页。关掉 `CloseApplications`，改为自己的进程检查（第 4 条）。CI 中固定 Inno Setup 的版本。
2. **位置**：程序固定在 `%USERPROFILE%\EplanMcp\app\`，不能选择；`EPLANMCP_HOME` 只改数据根目录，不改程序位置。注册表中只有 HKCU 的卸载项和可选的登录自启动项；不修改 PATH 和任何全局环境变量；不往 EPLAN 目录复制文件，不写 EPLAN 的注册表。
3. **插件注册**：只经 `eplanmcp install-addin`，它执行 `EPLAN.exe /Variant:"<变体>" EplApiModuleAction /register:"<dll>"`，每个勾选的变体一次，结果记在 `<数据根目录>\state\addins.json`。~~探测到 MSIX 重定向时拒绝执行。~~ MSIX 环境下照常注册（2026-10-21 修改）：注册写的是 EPLAN 的工作站设置 `C:\Users\Public\Eplan\Settings\…\STATION.*.xdb`，不在 MSIX 会重定向的目录中（S14，api-notes A23），原来的拒绝没有依据。没有任何 MCP 工具能注册插件（ADR-0005 第 1 节的 Code 级）。
4. **升级**：同一产品标识覆盖安装，不需要先卸载。安装前检查 `app\` 中的程序是否在运行，请工程师关闭 MCP 客户端会话和 Workbench，**不强制结束任何程序**；EPLAN 不必关闭，升级后提示重启。不支持降级。
5. **卸载**：先按记录运行 `uninstall-addin`，再删除 `app\`、快捷方式、自启动项和卸载项。**数据根目录中的数据全部保留**，结束页列出位置。
6. **Claude Code 插件**：本地插件市场放在 `app\claude-plugin\`，不含程序文件；其中的 `.mcp.json` 由安装程序写入 `eplanmcp.exe` 的完整路径，不依赖未核实的变量展开。
7. **构建**：发布工作流在本仓库的版本标签上构建全部产物：Host、Workbench 和安装包在 GitHub 托管的 runner 上，插件在自托管 runner 上（要引用 EPLAN 的 API 程序集）。附 SHA-256、CycloneDX SBOM 和 GitHub 来源证明。
8. **签名**：0.x 不签名（M5 设计 Q6）；安装包和程序的代码签名另行决定。插件的 EADN 签名仍按 ADR-0007。
9. **不提供自动更新**：检查更新要联网（I5）。

## 后果
- **好的**：
  - 一个文件完成安装，不提权、不联网；位置固定，注册、客户端配置、插件和 Workbench 的核对都有稳定的路径；
  - 升级不会关掉工程师的程序，也不会打断写入；卸载不丢数据；
  - 产物都由 CI 构建，可以校验来源。
- **坏的或要付出的代价**：
  - 依赖 Inno Setup：将来商业分发时，按作者的请求购买许可；它的脚本是另一种语言，需要维护；
  - 未签名时 SmartScreen 会提示，Windows 11 智能应用控制处于强制模式时可能直接阻止（M5 设计 4.10）；
  - 升级前要关闭所有客户端会话；
  - 每个变体注册时 EPLAN 都要启动一次（S14 确认能否自动退出）；
  - 按用户安装的目录同一用户的程序可以改写，这不在防护范围内（security.md 第 2 节）。
- **限制**（2026-10-21 补充，spike S14）：EPLAN 的插件注册按机器保存在工作站设置中，而程序按用户安装，所以一台电脑只能由一个 Windows 用户使用 EplanMcp。项目负责人选择保持按用户安装并写明这一限制（M5 设计 Q15）；`install-addin` 在注册已指向另一个用户的安装时拒绝。
- **后续行动**：
  - spike S14（插件注册，含下载标记对插件加载的影响）、S17（本地插件市场与客户端配置）；
  - 威胁模型 T22、T23 和 SEC-25、SEC-26；
  - 架构第 7 节补上安装包、插件市场和签名的现状；README 的安装一节改为安装包；开发流程第 8 节的发布清单加上自托管 runner 的插件构建。

## 各方案利弊
### 方案 2：压缩包加 PowerShell 脚本
- 优点：不需要安装包工具；TIA 用过。
- 缺点：Windows 客户端版的 PowerShell 5.1 默认执行策略是 `Restricted`，要让工程师用 `-ExecutionPolicy Bypass` 运行（[执行策略](https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.core/about/about_execution_policies?view=powershell-5.1)）；用资源管理器解压时下载标记可能传给解压出的文件，.NET Framework 4 默认拒绝来自网络区域的程序集（[loadFromRemoteSources](https://learn.microsoft.com/en-us/dotnet/framework/configure-apps/file-schema/runtime/loadfromremotesources-element)），插件可能无法加载；HKCU 卸载项在"应用"中显示只有社区的说法。
### 方案 3：WiX 按用户的 MSI
- 优点：标准的 Windows Installer，便于企业分发。
- 缺点：v6 起实行开源维护费，年收入超过 1 万美元的组织要赞助，v7 要显式接受 EULA（[OSMF](https://docs.firegiant.com/wix/osmf/)）；Windows Installer 没有指向用户目录根的文件夹属性；装在用户目录下要满足 ICE38、ICE64 的校验；安装后运行命令要写自定义操作。
### 方案 4：MSIX
- 缺点：不允许把包中的模块加载进包外的进程（[准备打包](https://learn.microsoft.com/en-us/windows/msix/desktop/desktop-to-uwp-prepare)），而插件正要加载进 EPLAN；侧载也必须签名，未签名且含可执行文件的包要管理员权限（[未签名的包](https://learn.microsoft.com/en-us/windows/msix/package/unsigned-package)）。
### 方案 5：Velopack
- 优点：MIT，按用户安装，自带更新。
- 缺点：默认装在 `%LocalAppData%`；安装钩子超过 30 秒会被结束（[钩子](https://docs.velopack.io/integrating/hooks)），而注册插件要启动 EPLAN；要在程序的入口加它的代码；它围绕自动更新设计，本项目不做自动更新。

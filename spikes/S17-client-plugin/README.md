# Spike S17：Claude Code 插件与客户端配置（部分）

| 项 | 内容 |
|---|---|
| 日期 | 2026-10-20 |
| 环境 | Windows 11；Claude 桌面版 2.42304.0.0（MSIX，包族名 `Claude_pzs8sxrjxfjjc`）自带的 Claude Code 2.1.299；EplanMcp `main`（7321565）的 Release 构建，`eplanmcp` 0.1.0 |
| 状态 | **部分完成**：在没有网络的虚拟机上安装插件（命令行、桌面版各一次）还没有做，等 Q11 的虚拟机 |
| 结论 | 本地插件市场不登录、网络不可达时也能装好，插件从原处加载。插件 `.mcp.json` 中 `${USERPROFILE}`、`${CLAUDE_PLUGIN_ROOT}` 和任意环境变量都会展开，`%VAR%` 不会。桌面版的 `claude_desktop_config.json` 实际只在包的重定向目录中 |

## 问题
[M5 设计](../../docs/design/2026-10-20-m5-operability.md) 4.7 的插件、4.11 的 `client-config`、Q7、Q10 依赖下面几件事：
1. 本机的 Claude Code 命令行在哪里，是什么版本；
2. 按 4.7 建的本地插件市场，`claude plugin marketplace add` 和 `claude plugin install` 能否在不联网、不登录的情况下完成，会写哪些文件，市场和插件是被复制还是从原处加载；
3. 插件 `.mcp.json` 中的 `${CLAUDE_PLUGIN_ROOT}`、`${USERPROFILE}` 和自定义变量是否展开；
4. 桌面版的 `claude_desktop_config.json` 实际在哪里（api-notes A6）。这决定 `client-config claude-desktop` 写到哪里；
5. 用命令行装在用户范围的插件，桌面版的 Code 页签能否看到（只查文档）。

## 方法
- `marketplace\`：按 4.7 写的市场模板，包括 `.claude-plugin\marketplace.json`（市场 `eplanmcp-local`，插件 `eplan-mcp`，来源 `./eplan-mcp`）、`eplan-mcp\.claude-plugin\plugin.json`、`eplan-mcp\.mcp.json`（服务器 `eplan`，参数 `serve`）和占位的技能 `skills\getting-started\SKILL.md`。`.mcp.json` 中的 `__EPLANMCP_EXE__` 由驱动脚本换成 `eplanmcp.exe` 的完整路径，模拟安装程序生成这个文件。
- `run.ps1`：每次都单独为这一个 `claude` 子进程设置环境（`ProcessStartInfo`），不改全局环境：
  - `CLAUDE_CONFIG_DIR` 指向 `.local\spikes\S17\<时间>\config`；去掉从桌面版会话继承来的 `CLAUDE*`、`ANTHROPIC*`、`MCP_*` 变量和各种令牌；
  - `EPLANMCP_HOME` 指向运行目录，`EPLANMCP_PIPE_PREFIX` 换成没有 EPLAN 使用的前缀，所以 `eplanmcp` 碰不到正在运行的 EPLAN，也不读写真实的数据根目录；
  - `HTTP(S)_PROXY` 指向本机没有监听的 9 端口，用来模拟断网；另设 `DISABLE_AUTOUPDATER`、`DISABLE_TELEMETRY`、`DISABLE_ERROR_REPORTING`；
  - 不登录，不向模型发送提示。

  先用 `--version`、`plugin marketplace list`、`auth status` 确认隔离生效。运行前后记录真实配置的时间戳：只读元数据，另外在 Claude Code 的文件中搜索本 spike 的名字。隔离检查期间 Claude Code 的真实文件有变化就停止，结束时报告所有变化。随后依次执行 validate、marketplace add、install、plugin list、mcp list/get，再做继承探测、变量测试、改写源文件、删除源目录、两种路径写法，最后执行 marketplace remove。
- 变量测试另建一个市场 `eplanmcp-s17vars`，目录结构与安装后相同：`<假用户目录>\EplanMcp\app\` 中放 `eplanmcp.exe`，旁边是 `claude-plugin\`。每个测试服务器用一份独立的 `eplanmcp.exe` 副本，因为命令行相同的服务器只会显示一个（见结果第 3 项）。判断方法：连上说明变量已展开；只有 `*-env` 两项反过来，变量展开后 `EPLANMCP_HOME` 指向一个故意写坏的 `config.json`，`eplanmcp` 以退出码 3 结束，所以连不上才说明已展开。测 `${USERPROFILE}` 时把这个子进程的 `USERPROFILE` 换成假用户目录，另外再用真实值跑一次。
- `desktop-config.ps1`：只读。对每个位置报告是否存在、文件 ID、修改时间，以及配置文件的顶层键名，从不读取或输出值。在桌面版会话中运行的 shell 看到的是重定向后的视图，所以加上 `-OutsidePackage` 时，用 WMI 再启动一个不属于桌面版进程树的 `pwsh`，在那里重新检查。对照项是 `%APPDATA%\Claude\claude-code`：按 A6，只有包内的进程能看到它。

```powershell
dotnet build src\EplanMcp.Host -c Release
spikes\S17-client-plugin\run.ps1                          # 日志和产物在 .local\spikes\S17\<时间>\
spikes\S17-client-plugin\desktop-config.ps1 -OutsidePackage
```

## 结果

### 1. 命令行
- `claude` 不在 PATH 上。它随桌面版附带：包内的进程看到 `%APPDATA%\Claude\claude-code\2.1.299\f0584be0fbaf\claude.exe`（还留着旧版 2.1.295），文件实际在 `%LOCALAPPDATA%\Packages\Claude_pzs8sxrjxfjjc\LocalCache\Roaming\Claude\claude-code\…`（第 4 项）。`claude --version` 输出 `2.1.299 (Claude Code)`。本次运行所在的会话也是由它启动的，进程链是：`WindowsApps\…\Claude.exe` → `claude.exe` 2.1.299 → shell。
- 隔离生效：`auth status` 报告 `loggedIn: false`、`authMethod: none`，`configDirectory` 是隔离目录；第一次调用后隔离目录中出现 `.claude.json` 和 `backups\`，真实文件没有变化（见"真实配置"）。

### 2. 本地插件市场（网络不可达，未登录；每条命令 0.2–0.5 秒，退出码 0）
| 命令 | 结果 |
|---|---|
| `plugin validate <市场>` | `Validation passed` |
| `plugin marketplace add <市场>` | `Successfully added marketplace: eplanmcp-local (declared in user settings)`。写入 `plugins\known_marketplaces.json`（`source: directory`，`installLocation` 就是给出的路径）和 `settings.json` 的 `extraKnownMarketplaces`；`plugins\marketplaces\` 只是一个空目录，**市场没有被复制** |
| `plugin install eplan-mcp@eplanmcp-local` | `(scope: user)`。`settings.json` 中加了 `enabledPlugins`；`installed_plugins.json` 把 `installPath` 记为 `plugins\cache\eplanmcp-local\eplan-mcp\0.1.0`，那里**确实有一份插件的副本** |
| `plugin list` | `Read from: <市场>\eplan-mcp`，`Status: enabled` |
| `mcp list` | `plugin:eplan-mcp:eplan: <完整路径>\eplanmcp.exe serve - ✔ Connected`；第一次连接时建立 `plugins\data\eplan-mcp-eplanmcp-local\` |
| `mcp get plugin:eplan-mcp:eplan` | Connected，范围显示为 "Dynamic config"；命令、参数和环境变量都被隐藏（`[REDACTED]`），与文档一致 |
| `plugin details eplan-mcp` | 1 个技能（getting-started），1 个 MCP 服务器（eplan），常驻约 51 token |
| `plugin marketplace remove eplanmcp-local` | 同时卸载插件，删掉 `settings.json` 和两个记录文件中的条目，以及 `plugins\data\` 中的目录；缓存中的副本留下，加了 `.orphaned_at` 标记（文档说 14 天后清理）；源目录不动 |

插件从原处加载，缓存中的副本没有被使用：
- 装好以后，把源目录中 `.mcp.json` 的命令改成另一份副本，不重新安装，下一次 `mcp list` 就用新路径并连上；缓存中的副本仍是旧内容；
- `${CLAUDE_PLUGIN_ROOT}` 展开为源目录，不是缓存目录；
- 把源目录改名（模拟卸载时删了 `app\`，但没有移除市场）后，`plugin list` 显示 `failed to load … cache-miss`，它的服务器从 `mcp list` 中消失，**不会退回到缓存中的旧副本**。

其他：
- 插件的 stdio 服务器继承命令行进程的环境：把命令行进程的 `EPLANMCP_HOME` 指向写坏的 `config.json` 后，`eplan` 连不上（`eplanmcp` 退出码 3）。这也说明 `EPLANMCP_PIPE_PREFIX` 传到了 `eplanmcp`。
- `marketplace add` 的路径写法（参数不经过 shell）：`~/EplanMcp/app/claude-plugin` 可以，被展开为用户目录；`%USERPROFILE%\EplanMcp\app\claude-plugin` 不行，报 `Invalid marketplace source format. Try: owner/repo, https://..., or ./path`。

### 3. 插件 `.mcp.json` 中的变量
| 服务器 | 写法 | 变量已设置 | 变量未设置（真实 `USERPROFILE`） |
|---|---|---|---|
| root-cmd | command `${CLAUDE_PLUGIN_ROOT}/../../eplanmcp.exe` | ✔ 显示为 `<源目录，正斜杠>/../../eplanmcp.exe` | （总有值）✔ |
| user-cmd | command `${USERPROFILE}\EplanMcp\app\eplanmcp.exe` | ✔（`USERPROFILE` 为假用户目录） | 显示为展开后的真实路径；本机没有安装到 `app\`，所以 ✘ |
| custom-cmd | command `${EPLANMCP_S17_BIN}\eplanmcp.exe` | ✔ | ✘，原样显示 `${EPLANMCP_S17_BIN}\…` |
| custom-arg | args `["${EPLANMCP_S17_ARG}"]` | ✔（得到 `serve`） | ✘，原样传给 `eplanmcp` |
| default-arg | args `["${EPLANMCP_S17_UNSET:-serve}"]` | ✔ | ✔（用默认值） |
| percent-cmd | command `%EPLANMCP_S17_PCT%\eplanmcp.exe` | ✘，原样 | ✘ |
| root-env | env `EPLANMCP_HOME=${CLAUDE_PLUGIN_ROOT}/badhome` | ✘ = 已展开（读到写坏的配置） | — |
| custom-env | env `EPLANMCP_HOME=${EPLANMCP_S17_BADHOME}` | ✘ = 已展开 | ✔ = 原样 |
| dup-cmd | 与 `eplan-mcp` 的 `eplan` 完全相同的命令行 | 不显示 | 把 `eplan` 的源 `.mcp.json` 改到另一份副本以后才显示 ✔ |

变量未设置又没有默认值时，`mcp list` 只显示连不上，stdout 和 stderr 中都没有文档所说的"缺少变量"警告。

### 4. 桌面版的配置文件
包 `Claude_2.42304.0.0_x64__pzs8sxrjxfjjc`（族名 `Claude_pzs8sxrjxfjjc`，签名类型 Developer，安装在 `C:\Program Files\WindowsApps\`）。官方文档：Windows 版桌面应用以 MSIX 包分发，按用户安装（[桌面版](https://code.claude.com/docs/en/desktop)，[Deploy Claude Desktop for Windows](https://support.claude.com/en/articles/12622703-deploy-claude-desktop-for-windows)）；配置文件的位置写的是 `%APPDATA%\Claude\claude_desktop_config.json`（[MCP 文档](https://modelcontextprotocol.io/docs/develop/connect-local-servers)）。

| 位置 | 包内（Code 页签的 shell） | 包外（WMI 启动的进程） |
|---|---|---|
| `%APPDATA%\Claude\` | 存在，文件 ID 与包目录相同 | **不存在** |
| `%APPDATA%\Claude\claude_desktop_config.json` | 存在，与下一行是同一个文件（文件 ID 相同） | **不存在** |
| `%LOCALAPPDATA%\Packages\Claude_pzs8sxrjxfjjc\LocalCache\Roaming\Claude\claude_desktop_config.json` | 存在 | 存在 |
| 对照：`…\Claude\claude-code` | 两处都能看到 | 只在包目录中 |

- 顶层键只有 `coworkUserFilesPath` 和 `preferences`，**没有 `mcpServers`**。
- 微软文档说明（[打包的桌面应用](https://learn.microsoft.com/en-us/windows/msix/desktop/desktop-to-uwp-behind-the-scenes)）：打包应用在 AppData 中新建的文件写入按用户、按包的私有位置，运行时合并到它看到的 AppData 中；打开文件时先找包的位置，找不到再找真实的 AppData，真实 AppData 中已有的文件就地修改。所以文档写的路径只是包内进程看到的样子，文件实际在包的目录中。
- 桌面版运行时会频繁改写这个文件：什么都不运行、只读修改时间的 150 秒内，它被改写了 4 次（UTC 06:05:38、06:06:42、06:07:30、06:07:40），大小都是 4794 字节。当时桌面版中开着多个会话。

### 5. 命令行装的插件，桌面版能否看到（只查文档）
- [安装插件](https://code.claude.com/docs/en/plugins/install)的"选择安装范围"一节写明：同一台电脑上，终端、桌面版的本地会话和 VS Code 扩展读同一组设置文件，在其中任何一处装到用户范围的插件，在另外两处也能用。[桌面版](https://code.claude.com/docs/en/desktop)的"共享配置"一节也说，桌面版和命令行共用 `~/.claude.json` 和 `~/.claude/settings.json`。
- 例外：Cowork 页签的插件来自 claude.ai 的 Customize，不读 `~/.claude`；云端会话和 WSL 会话不加载本机插件。
- 本机条件也满足：`~\.claude\` 和插件市场 `%USERPROFILE%\EplanMcp\app\claude-plugin\` 都不在 AppData 下，不会被重定向（S5）。没有实测。

### 真实配置（安全核对）
`run.ps1` 一共运行了 4 次。下表是最后一次（20261020-230507）前后的对比，Claude Code 的文件在 4 次运行中都没有变化：

| 文件或目录 | 运行前 → 运行后（2026-10-21，UTC） |
|---|---|
| `%USERPROFILE%\.claude.json` | 05:59:23.63 → 相同，不含本 spike 的名字 |
| `%USERPROFILE%\.claude\` | 01:27:02.95 → 相同 |
| `%USERPROFILE%\.claude\settings.json` | 2026-10-16 13:29:32.94 → 相同，不含本 spike 的名字 |
| `%USERPROFILE%\.claude\plugins\` | 01:32:29.60 → 相同 |
| `…\plugins\known_marketplaces.json`、`installed_plugins.json`、`cache\`、`data\` | 不存在 → 不存在 |
| `%APPDATA%\Claude\claude_desktop_config.json`（即包目录中的文件） | 06:04:57.49 → 06:05:17.70，桌面版自己改写（见第 4 项）；前 3 次运行期间没有变化 |

- `.claude.json` 在两次运行之间变过（05:26:35 → 05:52:22 → 05:59:23），每次运行期间都没有变。那几段时间都没有运行 `claude`，是本会话所在的桌面版进程写的。
- 隔离的命令行不会去碰桌面版的配置：子进程没有桌面版会话的变量（包括消息管道），文档也说独立运行的命令行不读 `claude_desktop_config.json`。同样的改写在什么都不运行时也会发生，频率与上面相同。

## 结论
1. **本地插件市场可以在网络不可达、不登录的情况下装好**（命令行）。设计 4.7 中"本地目录形式的插件市场不复制，插件在原处加载"成立，补充几点：install 仍会在 `plugins\cache\` 中复制一份，但不使用；源目录不在时插件报错，不会退回到那份副本；升级时只要改写源目录中的文件，新进程就会用新文件。
2. **`${USERPROFILE}` 会展开，这一点改变设计 4.7**。`command`、`args`、`env` 中的 `${VAR}` 和 `${VAR:-默认值}` 都会展开，所以 `.mcp.json` 可以是固定的文件：`"command": "${USERPROFILE}\\EplanMcp\\app\\eplanmcp.exe"`，安装程序不必再生成它（4.3 的"生成插件市场的 `.mcp.json`"一步可以去掉）。Q10 的前提也成立：放在 GitHub 上的市场，插件会被复制到缓存，`${CLAUDE_PLUGIN_ROOT}` 指向缓存，所以要用 `${USERPROFILE}`，不能用 `${CLAUDE_PLUGIN_ROOT}/../..`。`%USERPROFILE%` 写法不会展开。桌面版的 Code 页签用的是同一个 `claude.exe`，预计结果相同，在虚拟机上再确认。
3. 设计 4.7 的安装命令 `/plugin marketplace add %USERPROFILE%\EplanMcp\app\claude-plugin` 要改：Claude Code 不展开 `%USERPROFILE%`，应该写 `~/EplanMcp/app/claude-plugin` 或完整路径。这一点只在命令行中测过，会话中的 `/plugin` 没有测。
4. **重复的服务器**：Claude Code 按命令行判断插件服务器是否重复（[MCP 文档](https://code.claude.com/docs/en/mcp)的"范围的优先级"一节：插件按端点匹配重复，本地、项目、用户范围的定义优先）。如果工程师已经用 `claude mcp add`（README 现在的写法）或 `client-config claude-code` 配置过同一个 `eplanmcp.exe`，插件的服务器就不会启动，用的是那份配置。桌面版的 Code 页签还会加载 `claude_desktop_config.json` 中的服务器（文档），同样会重复。所以配置向导和入门指南对 Claude Code 只能二选一，`doctor` 可以提示重复。两种方式的工具名也不同（插件是 `mcp__plugin_eplan-mcp_eplan__…`，出自文档，未实测；手工配置是 `mcp__eplan__…`），权限规则要写对前缀。
5. **`client-config claude-desktop` 应该写 `%LOCALAPPDATA%\Packages\Claude_pzs8sxrjxfjjc\LocalCache\Roaming\Claude\claude_desktop_config.json`**（包族名用 `Get-AppxPackage` 查，或匹配 `Packages\Claude_*`）。包外的进程（安装程序、从开始菜单启动的 Workbench）如果写文档上的路径，会新建一个桌面版读不到的文件：这是按微软文档推断的，没有实际写入验证。只有桌面版不是 MSIX 安装时，才写 `%APPDATA%\Claude\`。另外，桌面版运行时会反复改写这个文件，所以写入前要请工程师退出桌面版，写完再读回核对。本机的文件中还没有 `mcpServers`。
6. 用命令行装在用户范围的插件，桌面版的 Code 页签能看到（文档明确写了，没有实测）；Cowork 页签看不到。

## 局限
- 没有真正断网：用不可达的代理模拟，不走代理的连接（如果有）拦不住。文档说本地目录市场在安装前不刷新。在没有网络的虚拟机上安装（命令行、桌面版各一次）仍然要做；第一次进入交互会话时，Claude Code 会尝试添加官方市场（要访问 GitHub），断网时的表现也要在那里看。
- 只测了命令行的子命令。会话中的 `/plugin marketplace add`、`/plugin install` 需要交互界面和登录，没有测。插件服务器的工具名前缀、`tools/list` 与评测配置是否一致，也要在会话中看：两者的命令和参数相同，只是插件没有设 `EPLANMCP_HOME`。
- 桌面版的 Code 页签和聊天界面都没有测，因为不能改真实配置。
- `${USERPROFILE}` 的"连上"是在子进程中把 `USERPROFILE` 换成假用户目录后得到的；用真实值时只看到了展开后的路径，因为本机没有安装到 `app\`。
- 包外的视图依赖"WMI 启动的进程不受重定向"这一前提；对照项（`claude-code` 目录）的结果与 A6 一致。
- 桌面版会不会覆盖外部写入的配置，没有测。

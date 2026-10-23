# EplanMcp 中文入门指南

本指南面向 v0.1.0 预览安装包，以 [GitHub Releases](https://github.com/asckye/eplan-mcp/releases) 实际提供的文件为准。尚无安装包时，请等待发布；源码构建见仓库 README。下面的安装步骤描述安装包交付后的流程。

## 1. 准备

- Windows 10/11 x64，EPLAN 2026 Electric P8 或 Pro Panel，以及包含 API 扩展、允许加载未签名插件的 EPLAN 授权。2024、2025、2027 尚未完成安装实测，不作为本指南的入门环境。
- 安装好 Claude Code 或 Claude 桌面版，并准备好客户端自己的模型服务。EplanMcp 的安装与本机连接不需要联网；客户端连接模型可能需要联网。
- 一台电脑由一个 Windows 用户使用 EplanMcp：EPLAN 注册按机器保存，程序按用户安装。不要用另一个账户重复注册。
- 保留客户端对 shell 命令的确认。同一用户下的脚本能够操作审批窗口，不能让代理或脚本代替工程师批准。

## 2. 下载与 SHA-256 校验

从同一版本的发布页下载安装包和 SHA-256 校验和文件。在 PowerShell 中进入下载目录，把下面的占位文件名换成下载的安装包名：

```powershell
Get-FileHash -Algorithm SHA256 -LiteralPath '.\<安装包文件名>.exe'
```

把输出的 `Hash` 与校验和文件中**这个文件名**对应的 64 位十六进制值逐字比对（大小写不影响结果）。不一致就停止，不运行文件，重新从发布页下载。离线电脑可先在联网电脑下载两份文件，再一起带过去校验。

**0.x 安装包和程序不做代码签名。** SmartScreen 可能显示“Windows 已保护你的电脑”或来源未知；核对下载来源和 SHA-256 后，才选择“更多信息 → 仍要运行”。企业策略或智能应用控制可能禁止运行，且没有这个按钮；请联系管理员按组织规定处理，不关闭系统保护。EPLAN 插件的加载仍取决于上面所说的授权条件。

## 3. 安装与注册 EPLAN 插件

安装包提供后，关闭正在运行的 EPLAN，再双击安装包。用普通用户运行，无需管理员权限；勾选要使用的变体，例如 Electric P8、Pro Panel。目录固定为 `%USERPROFILE%\EplanMcp\app\`，不能另选；安装不修改 PATH。

注册会启动 EPLAN，等待插件加载，再请求正常关闭这个实例。遇到登录、授权或系统消息窗口，请自己处理并关闭；不要强制结束进程。只有注册成功后，再启动平常使用的 EPLAN 并打开项目。

需要补注册某个变体时，先关闭 EPLAN，在从 Windows 开始菜单打开的 PowerShell 中执行。后续命令沿用同一窗口里的 `$eplanMcp`：

```powershell
$eplanMcp = Join-Path $env:USERPROFILE 'EplanMcp\app\eplanmcp.exe'
& $eplanMcp install-addin --variant 'Electric P8'
```

Pro Panel 把变体名换成 `'Pro Panel'`；两个都用时重复传入 `--variant`。不传变体会注册已找到的全部变体。命令可能等待两分钟；报告“未确认”时先看留下的 EPLAN 窗口，不要连续重复注册。

## 4. 自检

```powershell
& $eplanMcp doctor
```

逐项阅读结果：`OK` 表示正常，`INFO` 是说明，`WARN` 需要核对，`FAIL` 需要处理。需要保存结构化结果时用 `doctor --format json`。当前 Workbench 的状态与客户端向导页面仍为占位，请使用这里的命令。

## 5. 连接客户端

安装、注册和改配置由工程师自己操作。`client-config` 请从 Windows 开始菜单打开的 PowerShell 运行，不要从 Claude 桌面版的会话中运行：MSIX 环境会重定向配置文件，命令会拒绝写入。

### Claude Code：推荐本地插件

在 Claude Code 会话中依次输入：

```text
/plugin marketplace add ~/EplanMcp/app/claude-plugin
/plugin install eplan-mcp@eplanmcp-local
```

也可以在终端用 `claude plugin marketplace add ~/EplanMcp/app/claude-plugin`、`claude plugin install eplan-mcp@eplanmcp-local`。这是随安装包提供的本地市场，不需要下载市场内容。这里用 `~/`，不要写 `%USERPROFILE%`，后者不会被市场命令展开。装好后重新打开会话。

**插件与下面的 `client-config claude-code` 二选一。** 若以前用配置方式连接过，先运行 `& $eplanMcp client-config claude-code --remove`，自己检查项目范围是否还手工配置了同一服务器，再安装插件。重复配置会让 Claude Code 优先使用用户或项目配置。不要移动或删除 `app\claude-plugin\`，本地插件从那里加载。

### Claude 桌面版或其他客户端：client-config

完全退出 Claude 桌面版（包括托盘），先预览，再写入：

```powershell
& $eplanMcp client-config claude-desktop --dry-run
& $eplanMcp client-config claude-desktop
```

命令识别 MSIX 桌面版实际使用的配置位置，写入后再启动客户端。Claude 桌面版聊天界面使用这条路线；Code 页签按 Claude Code 配置，避免再添加相同服务器。Code 页签共享插件的行为尚待实机验收。

Claude Code 不用插件时，把上述客户端名换成 `claude-code`；已装插件时写入会被拒绝，须先在终端运行 `claude plugin marketplace remove eplanmcp-local`。其他可用名称为 `codex`、`gemini-cli`、`qwen-code`、`kimi-code`、`codebuddy`、`opencode`、`qwen-agent`、`cursor`、`vscode-copilot`。

写入只修改名为 `eplan` 的服务器项，原文件先备份为同目录的 `<文件>.bak_<UTC 时间>`。JSONC 注释会在改写后丢失，可从备份查看。不要分享整个客户端配置文件，其中可能有其他服务的密钥。

## 6. 第一次对话

在 EPLAN 中打开一个项目，然后对客户端说：“先调用 `eplan_session_status` 确认连接，再告诉我这个项目有多少页。”

正常顺序是 `eplan_session_status` → `eplan_project_list` → `eplan_project_get`。有多个 EPLAN 实例时，先由你确认目标实例，再用 `eplan_session_select` 选择；不要只凭当前前台窗口猜测。需要详细参数时，让模型调用 `eplan_tool_usage`。

## 7. 第一次修改与审批

先用 EPLAN 的项目复制功能，把试验副本放到 `%USERPROFILE%\EplanMcp\scratch\`，再在 EPLAN 中打开副本；不要把真实项目的目录设为 scratch。让模型查出副本的完整页名，再用 `eplan_page_update` 的 `dry_run: true` 预览一处页描述修改，核对后在副本上执行。这个工具成功后可在 EPLAN 中按一次 Ctrl+Z 撤销。

普通项目写入在 scratch 内自动放行；真实项目默认逐次审批，`config.json` 也可设为拒绝。Destructive 操作（如删页、重编号）在 scratch 内也要审批，批准后先备份，备份失败就不执行。写系统部件库影响所有项目，即使在 scratch 中也要审批并先备份，不能指望 Ctrl+Z 恢复部件库。

工程师自己从开始菜单启动 Workbench，或使用其登录自启动；Host 不会替你启动它。普通项目审批先送 Workbench，查看托盘提醒和“审批队列”；窗口不会抢焦点。Workbench 不可用时改用 EPLAN 对话框；已送达后断开则取消，不会再弹第二份。系统主数据审批仍只在 EPLAN 中显示。

核对工具、实例、项目、变更清单后自己批准或拒绝。默认 120 秒无人批准就拒绝，不执行。安全配置只能由工程师编辑数据根目录的 `config.json`，工具不能修改；不要为了让调用成功而关闭保护。

## 8. 常见问题、日志与审计

| 现象或 doctor 项 | 怎么处理 |
|---|---|
| EPLAN 连不上；`eplan.running`、`bridge.pipe` | 先让模型调用 `eplan_session_status` 并读提示；确认 EPLAN 已启动、所用变体已注册。没有项目时先打开项目。有多个实例时明确选择。 |
| `eplan.installation`、`bridge.handshake`、`host.version` | 核对 EPLAN 2026 和 Host/插件版本。升级后重启 EPLAN；仍不匹配时关闭 EPLAN，再补注册。不要反复在 API 管理中卸载和加载。 |
| `bridge.licence` | 核对 API 扩展与未签名插件的授权条件；不需要也不要提供授权文件或凭据。 |
| 没有弹出审批 | 只读、`dry_run`、没有实际变化、普通 scratch 写入本来不审批。真实项目检查 Workbench 托盘/队列与 EPLAN 对话框；读清返回的拒绝或超时原因，不能让模型代答。 |
| `EPLAN_BUSY` | 自己关闭 EPLAN 中未处理的模态窗口，等当前操作结束后再试。 |
| `config`、`data.directory`、`audit.directory` | 按 doctor 指出的配置错误修正文件；确认数据目录可写。审计写不进去会拒绝写入，不要删审计来绕过。目录尚未创建可能只是还没使用。 |
| `suspension` 或结果未知 | 先在 EPLAN 中核实是否已修改；下一次写入会经审批渠道请你确认。不要直接重试未知结果的修改。 |
| `exports`、`backups` | 查看空间占用，自己清理不再需要的文件；备份不会自动删除。 |
| `msix` | 本机工具连接可以使用 MSIX 客户端；写客户端配置须换到开始菜单打开的 PowerShell，并完全退出桌面版。 |
| `path.length` | 按提示检查用户 PATH 过长导致的环境问题；本软件不修改 PATH，不需要手工把 app 加进去。 |
| 需要管理员、断网失败、虚拟机不能使用 | 本机安装无需管理员、不绑定电脑；分清是 Windows 策略、EPLAN 授权还是模型客户端联网问题。虚拟机内按本指南本机方式连接，不因虚拟机放宽审批。 |
| 注册指向另一个用户 | 停止操作，由负责该电脑的工程师协调。检测只能部分覆盖：另一个用户的插件不可读或旧版不报告路径时不能可靠识别。 |

默认数据根目录为 `%USERPROFILE%\EplanMcp\`。`EPLANMCP_HOME` 只改数据位置，不改固定安装目录；排查时以 doctor 显示的位置为准。

- 插件日志：`logs\bridge-*.log`，启动失败看 `logs\bridge-startup-error.log`；Workbench 日志为 `logs\workbench-*.log`。Host 日志写 stderr，由客户端收集，不承诺有 Host 日志文件。
- 审计：`audit\<日期>.jsonl`。用 `& $eplanMcp audit verify` 校验哈希链；能发现链中改动，但不能证明末尾整段记录没有被删除。
- `state\` 保存注册、客户端配置记录和写入暂停状态；`scratch\` 是试验项目，`exports\` 是导出，`backups\` 是操作前备份。提交问题前检查日志、配置和项目文本，删除其中的敏感信息，不提供密钥。

## 9. 升级与卸载

升级先等待写操作结束，关闭 MCP 客户端会话，并从托盘退出 Workbench，再运行校验过的新安装包覆盖安装，不必先卸载。不要强制结束 Host。升级后重启 EPLAN，让新插件生效；不支持直接降级。配置、审计、日志、scratch、导出和备份保留。

卸载前自己移除 Claude Code 的本地市场（也会卸载该市场的插件）：

```powershell
claude plugin marketplace remove eplanmcp-local
```

对于配置路线，可在卸载前运行 `& $eplanMcp client-config --remove`，移除记录中过去写入的服务器项；也可指定客户端，例如 `client-config claude-desktop --remove`。手工写的项目配置由你自己检查。

关闭 EPLAN、客户端会话和 Workbench，再从 Windows“已安装的应用”或开始菜单卸载。卸载会调用 `uninstall-addin` 撤销记录中的注册，可能启动 EPLAN；遇到窗口仍由你处理。只需撤销 EPLAN 插件时可自行运行 `& $eplanMcp uninstall-addin`。撤销失败先解决报告的问题再重试。

卸载保留数据根目录中的全部数据；确认不再需要后由你自己删除。换电脑无需为 EplanMcp 办理授权转移，EPLAN 自己的授权按其规则处理。

---
EplanMcp 是独立的开源项目，与 EPLAN GmbH & Co. KG 无关联，也未经其认可或赞助。EPLAN、EPLAN Electric P8 和 EPLAN Pro Panel 是 EPLAN GmbH & Co. KG 的商标或注册商标。

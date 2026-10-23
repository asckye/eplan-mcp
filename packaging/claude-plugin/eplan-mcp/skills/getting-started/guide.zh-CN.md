[中文](zh-CN.md) | [English](en.md)

# EplanMcp 中文入门指南

本指南面向 ZIP 预览版，以 [GitHub Releases](https://github.com/asckye/eplan-mcp/releases) 实际提供的文件为准。尚无 ZIP 时，请等待后续发布；源码构建见仓库 README。

EPLAN 在虚拟机中、AI 客户端在宿主机上时，见[虚拟机部署](#10-虚拟机部署)。

## 1. 准备

- Windows 10/11 x64，EPLAN 2026 Electric P8 或 Pro Panel，以及包含 API 扩展、允许加载未签名插件的 EPLAN 授权。2024、2025、2027 尚未完成安装实测，不作为本指南的入门环境。
- 安装好 Claude Code 或 Claude 桌面版，并准备好客户端自己的模型服务。EplanMcp 的安装与本机连接不需要联网；客户端连接模型可能需要联网。
- 一台电脑由一个 Windows 用户使用 EplanMcp：EPLAN 注册按机器保存，程序按用户安装。不要用另一个账户重复注册。
- 保留客户端对 shell 命令的确认。同一用户下的脚本能够操作审批窗口，不能让代理或脚本代替工程师批准。

## 2. 下载与 SHA-256 校验

从同一版本的发布页下载 `EplanMcp-<version>.zip` 和 `SHA256SUMS.txt`。在 PowerShell 中进入下载目录，把版本占位符换成实际版本：

```powershell
Get-FileHash -Algorithm SHA256 -LiteralPath '.\EplanMcp-<version>.zip'
```

把输出的 `Hash` 与校验和文件中**这个文件名**对应的 64 位十六进制值逐字比对（大小写不影响结果）。不一致就停止，不运行文件，重新从发布页下载。离线电脑可先在联网电脑下载两份文件，再一起带过去校验。

校验后，右键 ZIP →“属性”→ 如有“解除锁定”，勾选并应用，再解压。**0.x 程序不做代码签名**；若被 SmartScreen、企业策略或智能应用控制阻止，请按组织规定联系管理员，不修改系统安全设置。EPLAN 插件的加载仍取决于上述授权。

## 3. 安装与注册 EPLAN 插件

解压整个 ZIP，得到唯一顶层文件夹 `EplanMcp-<version>`。将整个文件夹移至短且固定的位置，可改名为 `D:\EplanMcp`；保留全部文件和子目录，不要只复制 EXE，也不要从压缩包预览中启动。程序目录与数据根目录 `%USERPROFILE%\EplanMcp` 分开，无需管理员权限，不修改 PATH。

关闭 EPLAN，打开 **`D:\EplanMcp\workbench\EplanMcp.Workbench.exe`**（其他位置替换前缀）。在 Workbench 状态页勾选检测到的 Electric P8、Pro Panel 等所需变体，点击“注册 EPLAN 插件”，读取每个变体的结果。首次运行后保持程序目录位置不变。

注册会启动 EPLAN，等待插件加载，再请求正常关闭这个实例。遇到登录、授权或系统消息窗口，请自己处理并关闭；不要强制结束进程。只有注册成功后，再启动平常使用的 EPLAN 并打开项目。

需要补注册时可再用 Workbench；命令行方式如下，路径换成自己的程序目录。后续命令沿用同一窗口里的 `$eplanMcp`：

```powershell
$eplanMcp = 'D:\EplanMcp\eplanmcp.exe'
& $eplanMcp install-addin --variant 'Electric P8'
```

Pro Panel 把变体名换成 `'Pro Panel'`；两个都用时重复传入 `--variant`。不传变体会注册已找到的全部变体。命令可能等待两分钟；报告“未确认”时先看留下的 EPLAN 窗口，不要连续重复注册。

## 4. 自检

```powershell
& $eplanMcp doctor
```

首次运行可直接打开 Workbench 的“状态”页自检；上面是命令行方式。`OK` 表示正常，`INFO` 是说明，`WARN` 需要核对，`FAIL` 需要处理。保存结构化结果用 `doctor --format json`。

## 5. 连接客户端

在 Workbench 的“MCP 与客户端”页选择客户端，预览差异后写入；实际写入由 Host 的 `client-config` 完成，使用当前 `eplanmcp.exe` 的绝对路径。安装、注册和改配置由工程师自己操作；请从解压目录打开 Workbench，命令行则从开始菜单打开 PowerShell，不从 Claude 桌面版会话中运行配置命令（MSIX 重定向下会拒绝写入）。

### Claude Code：推荐本地插件

先在 Workbench 的客户端页生成 Claude Code 本地市场，再执行页面给出的两条命令。默认数据根目录对应以下命令；自定义数据目录时使用页面显示的实际路径：

```text
/plugin marketplace add ~/EplanMcp/claude-plugin
/plugin install eplan-mcp@eplanmcp-local
```

也可在终端使用页面给出的 `claude plugin marketplace add …`、`claude plugin install …`。生成的市场不需要下载内容，配置中已写入实际 Host 路径；不要直接安装程序目录内的市场模板。装好后重新打开会话。

**插件与下面的 `client-config claude-code` 二选一。** 若以前用配置方式连接过，先移除旧配置并检查项目范围的手工配置，再安装插件。重复配置会让 Claude Code 优先使用用户或项目配置；不要移动或删除数据根目录内生成的 `claude-plugin\`。

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

工程师自己打开程序目录中的 `workbench\EplanMcp.Workbench.exe`，或在其设置中启用登录自启动；Host 不会替你启动它。普通项目审批先送 Workbench，查看托盘提醒和“审批队列”；窗口不会抢焦点。Workbench 不可用时改用 EPLAN 对话框；已送达后断开则取消，不会再弹第二份。系统主数据审批仍只在 EPLAN 中显示。

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
| 需要管理员、断网失败、虚拟机不能使用 | 本机安装无需管理员、不绑定电脑；分清是 Windows 策略、EPLAN 授权还是模型客户端联网问题。AI 客户端也在虚拟机内时用本机连接，在宿主机上时见[虚拟机部署](#10-虚拟机部署)；仅配置网络权限需要管理员授权，审批要求不变。 |
| 注册指向另一个用户 | 停止操作，由负责该电脑的工程师协调。检测只能部分覆盖：另一个用户的插件不可读或旧版不报告路径时不能可靠识别。 |

默认数据根目录为 `%USERPROFILE%\EplanMcp\`，与解压的程序目录分开。`EPLANMCP_HOME` 只改数据位置，不改变程序目录；排查时以 doctor 显示的位置为准。

- 插件日志：`logs\bridge-*.log`，启动失败看 `logs\bridge-startup-error.log`；Workbench 日志为 `logs\workbench-*.log`。Host 日志写 stderr，由客户端收集，不承诺有 Host 日志文件。
- 审计：`audit\<日期>.jsonl`。用 `& $eplanMcp audit verify` 校验哈希链；能发现链中改动，但不能证明末尾整段记录没有被删除。
- `state\` 保存注册、客户端配置记录和写入暂停状态；`scratch\` 是试验项目，`exports\` 是导出，`backups\` 是操作前备份。提交问题前检查日志、配置和项目文本，删除其中的敏感信息，不提供密钥。

## 9. 升级与卸载

升级先等待写操作结束，关闭 **EPLAN、MCP 客户端和 Workbench**（从托盘退出），再校验、解除新 ZIP 的锁定并解压，用新文件夹整体替换原程序目录，保持原路径（如 `D:\EplanMcp`），避免遗留旧文件。不要强制结束 Host。打开新 Workbench，重新生成本地市场或更新客户端配置，再启动 EPLAN；路径不变无需重新注册，改变路径须重新注册。不支持降级；数据目录全部保留。

卸载前自己移除 Claude Code 的本地市场（也会卸载该市场的插件）：

```powershell
claude plugin marketplace remove eplanmcp-local
```

对于配置路线，可在卸载前运行 `& $eplanMcp client-config --remove`，移除记录中过去写入的服务器项；也可指定客户端，例如 `client-config claude-desktop --remove`。手工写的项目配置由你自己检查。

用过虚拟机连接时，先在宿主机移除 `eplan-vm`，再在虚拟机“停止服务”→“移除网络权限”→“删除密钥”，见[移除虚拟机连接](#移除虚拟机连接)。

卸载时先在 Workbench 设置中关闭登录自启动，移除上述客户端配置，关闭 EPLAN 和客户端，再在 Workbench 点击“撤销注册”。它调用 `uninstall-addin` 撤销全部记录，可能启动 EPLAN；遇到窗口由你处理。确认成功后从托盘退出 Workbench，删除整个程序目录。撤销失败先保留文件、处理问题后重试；命令行也可执行 `& $eplanMcp uninstall-addin`。

卸载保留数据根目录中的全部数据；确认不再需要后由你自己删除。换电脑无需为 EplanMcp 办理授权转移，EPLAN 自己的授权按其规则处理。

## 10. 虚拟机部署

### 适用情况与边界

适用于 EPLAN 在虚拟机中、AI 客户端在宿主机上。Host（`eplanmcp.exe`）、Bridge、审批、审计、scratch 和导出都在虚拟机中，只有 MCP 请求与结果跨网络。

客户端须支持 Streamable HTTP 和自定义请求头：Claude Code、Codex、Gemini CLI、Qwen Code、Kimi Code CLI、CodeBuddy Code、OpenCode、Qwen Agent、Cursor、VS Code Copilot。Claude 桌面版不能用：其配置文件只支持本机 stdio，远程连接器由 Anthropic 后端通过 HTTPS 连接，无法访问这里的私有虚拟机 HTTP 地址。spike S19 只实测了 Claude Code 的连接；其他客户端尚未逐个实机验收，Qwen Agent 还缺少官方配置文档核对，跨机器连接与 UAC 仍待实机验证。

**只在宿主机与虚拟机之间的私有网络使用。HTTP 不加密，密钥和项目数据在网络上是明文。** 下例用虚拟机 `192.168.56.2`、宿主机 `192.168.56.1`，请换成两端实际可互通的 IPv4；不要用 `0.0.0.0` 或回环地址。

### 虚拟机中：启动服务

1. 按[下载与校验](#2-下载与-sha-256-校验)、[安装与注册](#3-安装与注册-eplan-插件)解压整个 ZIP、注册插件，再启动 EPLAN。打开 Workbench →“MCP 与客户端”，滚到页底的“虚拟机连接”。
2. 在“虚拟机 IPv4(I)”选择虚拟机的地址，“端口(P)”默认 `8766`；“宿主机 IPv4(H)”填运行 AI 客户端的宿主机地址。
3. 在“共享密钥”处点击“生成密钥”，再点“复制”，交给宿主机配置使用。
4. 点击“允许网络访问”，在 Windows 弹出的 UAC 对话框中由管理员同意。入站规则只允许所选 TCP 端口、当前 `eplanmcp.exe`、宿主机这一个 IPv4，且只在域/专用网络配置文件中生效。
5. 有多个 EPLAN 时，在“EPLAN 实例（进程号）(I)”选择目标；未列出时点“刷新实例”。点击“启动服务”，确认出现 `正在监听 http://<虚拟机 IPv4>:<端口>/mcp。请保持工作台运行。` 的提示。一个服务固定对应一个实例；该实例退出后不会自动换连其他实例。

服务不会随安装或登录自动启动。Workbench 启动的服务随 Workbench 退出停止；关闭窗口只是缩到托盘，需从托盘退出才停止。

命令行替代步骤（不要与 Workbench 重复启动）：先在普通 PowerShell 中执行。`vm-key create` 会替换已有密钥，`show` 会显示完整密钥；只在需要时查看。

```powershell
$eplanMcp = 'D:\EplanMcp\eplanmcp.exe'
& $eplanMcp vm-key create
& $eplanMcp vm-key show
& $eplanMcp vm-instances --format json
& $eplanMcp vm-firewall show --port 8766 --host-address 192.168.56.1
```

另开“以管理员身份运行”的 PowerShell，只用它建立规则（调整程序路径）：

```powershell
& 'D:\EplanMcp\eplanmcp.exe' vm-firewall add --port 8766 --host-address 192.168.56.1
```

回到普通窗口启动；单实例可省略 `--process`，多实例把 `1234` 换成 `vm-instances` 列出的目标进程号。此命令保持前台运行，用 Ctrl+C 停止。

```powershell
& $eplanMcp serve --http --address 192.168.56.2 --port 8766 --process 1234
```

### 宿主机上：连接客户端

1. 校验并解压**同版本的整个 ZIP**，打开 `workbench\EplanMcp.Workbench.exe`。这里只用配置向导，无需安装 EPLAN 或注册插件。
2. 打开“MCP 与客户端”，在页顶选择“虚拟机 ↔ 宿主机”。填“虚拟机 IPv4 地址(A)”、“远程端口(O)”和“共享密钥(K)”（虚拟机生成的完整 43 字符密钥，不含空格）。
3. 点击“测试连接(T)”，确认已连接并显示服务器版本和工具数；这只测试连接，不调用 EPLAN 工具。
4. 勾选客户端，点击“预览并写入”，核对差异后选“写入配置(W)”。写入的远程服务器名为 `eplan-vm`。查看各卡片结果，再重启客户端；此模式使用远程配置，不生成本地插件市场。

命令行方式：用文本编辑器将密钥单独存为宿主机上的临时单行文件 `.\vm-key.txt`。**不要把密钥放在命令参数中，也不要用含密钥字面量的命令创建文件。** 从开始菜单打开 PowerShell，分行执行：先测试，再预览，成功且核对预览后才写入；客户端名可换成上文支持的名称。

```powershell
$eplanMcp = 'D:\EplanMcp\eplanmcp.exe'
$vmUrl = 'http://192.168.56.2:8766/mcp'
Get-Content -LiteralPath '.\vm-key.txt' | & $eplanMcp vm-test --url $vmUrl --key-stdin
Get-Content -LiteralPath '.\vm-key.txt' | & $eplanMcp client-config claude-code --vm --url $vmUrl --key-stdin --dry-run
Get-Content -LiteralPath '.\vm-key.txt' | & $eplanMcp client-config claude-code --vm --url $vmUrl --key-stdin
Remove-Item -LiteralPath '.\vm-key.txt'
```

结束后无论成功或失败都删除临时密钥文件，写入成功后重启客户端。

### 密钥、路径与审批

- 虚拟机的密钥用当前 Windows 用户的 DPAPI 加密保存；**宿主机客户端配置及其备份含明文密钥，`claude mcp get eplan-vm` 也会原样打印密钥**，不要分享文件、输出或截图。
- 在虚拟机先“停止服务”，再选“重新生成密钥”，旧密钥立即失效；用新密钥重写宿主机配置，再启动服务、测试连接并重启客户端。CLI 的 `vm-key create` 同样会立即使旧密钥失效。
- 给工具的路径都是**虚拟机路径**，scratch、导出、备份和导入文件也在虚拟机中；导出文件不会自动复制到宿主机，`inline` 图片仍可经 MCP 返回。
- 审批出现在虚拟机的 Workbench 或 EPLAN 对话框中，客户端名称旁显示 `via network: <地址>`（当前实现固定为英文）。由工程师在虚拟机中核对并回答；网络连接不放宽审批或 scratch 规则。

### 连接排错

在宿主机运行上述 `vm-test`，按失败代码处理；加 `--format json` 可保存结构化结果。

| 失败代码 | 怎么处理 |
|---|---|
| `unreachable` | 确认虚拟机服务正在监听；核对防火墙规则、宿主机来源 IPv4、虚拟机 IPv4 和端口，确保两端私有网络互通。 |
| `timed_out` | 单次请求等待超过 10 秒；检查虚拟机是否暂停、服务是否响应、网络是否丢包或被防火墙丢弃，再测试。 |
| `unauthorized`（401） | 密钥错误或已换新；从虚拟机复制当前密钥，重新测试并重写配置。 |
| `forbidden`（403） | 请求携带不允许的 `Origin` 头，常见于浏览器客户端；由工程师核对虚拟机 `config.json` 的 `httpAllowedOrigins`，只添加可信的实际 Origin，再重启服务。允许列表默认空；`vm-test` 不发送 Origin。 |
| `rate_limited`（429） | 同一来源地址一分钟内认证失败 10 次后，锁定该地址一分钟；停下自动重试，等一分钟后用当前密钥测试。一次客户端连接可能发出多个失败请求。 |
| `protocol_error` | 核对 URL 为 `http://<IPv4>:<端口>/mcp`、输入是完整的 43 字符密钥；检查协议和两端版本，使用同版本 ZIP 后重试。 |

在**虚拟机**运行 `& $eplanMcp doctor`：`http.service` 列出本数据目录的服务及绑定地址/端口，`vm.firewall` 列出规则是否存在和实际放行范围。核对程序路径、TCP 端口、宿主机地址及域/专用配置文件；这些检查不修改规则，也不代替宿主机连接测试。

### 移除虚拟机连接

在宿主机的“虚拟机 ↔ 宿主机”模式勾选客户端，点击“移除配置”，确认移除 `eplan-vm`，再重启客户端；或运行 `& $eplanMcp client-config --vm --remove`，清理记录中的远程条目，无需地址和密钥。虚拟机中依次点击“停止服务”→“移除网络权限”（需 UAC）→“删除密钥”。

命令行方式：先在虚拟机停止前台服务（Ctrl+C），在管理员 PowerShell 中移除规则，再回到原普通窗口删除密钥；使用建立规则时的相同端口、宿主机 IPv4 和程序路径。

```powershell
& 'D:\EplanMcp\eplanmcp.exe' vm-firewall remove --port 8766 --host-address 192.168.56.1
```

```powershell
& $eplanMcp vm-key delete
```

---
EplanMcp 是独立的开源项目，与 EPLAN GmbH & Co. KG 无关联，也未经其认可或赞助。EPLAN、EPLAN Electric P8 和 EPLAN Pro Panel 是 EPLAN GmbH & Co. KG 的商标或注册商标。

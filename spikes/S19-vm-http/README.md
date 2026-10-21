# Spike S19：虚拟机连接的 HTTP 传输（部分）

| 项 | 内容 |
|---|---|
| 问题 | [ADR-0018](../../docs/adr/0018-vm-http-transport.md)（2026-10-21 接受）的后续行动：Kestrel 加 SDK 的 HTTP 传输在自包含单文件中的体积和启动时间；第 4、5 条（密钥、失败次数限制、`Origin`）能否这样实现；SDK 的会话与第 8 条"按会话保存状态"；Claude Code 和 [M5 设计](../../docs/design/2026-10-20-m5-operability.md) 4.11 表中其他客户端对远程服务器和请求头的支持；Host 中哪些状态要按会话保存 |
| 日期 | 2026-10-21 |
| 环境 | Windows 11 专业版 26300；.NET SDK 10.0.402（运行时包 10.0.13）；ModelContextProtocol 与 ModelContextProtocol.AspNetCore 2.2.0；Claude 桌面版自带的 Claude Code 2.1.299；全部只在本机回环地址 `127.0.0.1` 上 |
| 状态 | **部分完成**：真实的虚拟机或第二台电脑、经 UAC 建防火墙规则、局域网中的明文抓包都没有做，其他客户端只查了文档（见"局限"） |
| 结论 | ASP.NET Core 让自包含单文件大 26.5 MB（74.3 → 100.7 MB，+36%），HTTP 方式约 0.15 秒开始监听；第 4、5 条用一个中间件就能实现，探测全部符合预期。**SDK 2.2.0 的 HTTP 传输默认无状态，2026-07-28 版协议也取消了会话**：要按会话保存选中的实例，Host 必须设 `SessionMode = Stateful`，Claude Code 这时会退回 2025-11-25 的 `initialize` 并带上 `Mcp-Session-Id`。Claude Code 不发 `Origin`；请求头中的 `${VAR}` 会展开。详见"结论" |

## 问题
1. **体积与启动**：Host 加上 `ModelContextProtocol.AspNetCore`（Kestrel、`WithHttpTransport`、`MapMcp("/mcp")`）后，自包含单文件 win-x64 大多少，HTTP 方式多久开始监听？包的版本和许可证是什么？
2. **第 4、5 条**：每个请求恒定时间比较 Bearer 密钥、没有不需要密钥的端点、401 不说明原因、同一来源一分钟内失败 10 次后回 429 一分钟、带不在允许列表中的 `Origin` 回 403，能否实现并用 HTTP 探测证明？SDK 的 Streamable HTTP 怎样处理会话（`Mcp-Session-Id`），第 8 条的按会话状态能放在哪里？
3. **Claude Code 作为远程客户端**：`claude mcp add --transport http … --header "Authorization: Bearer <密钥>"` 存下什么，`mcp list` / `mcp get` 能否反映连接状态，密钥错误时怎样，会不会发 `Origin`？
4. **其他客户端**：4.11 表中的客户端怎样配置带请求头的远程 Streamable HTTP 服务器，与 TIA 的 `ClientProfiles` 是否一致，哪些发不了请求头？
5. **按会话的状态**：Host 中哪些状态在 HTTP 方式下要按会话保存，怎样改？

## 方法
- [Stdio/](Stdio/)：基线。Host 的启动方式（`Host.CreateApplicationBuilder`、日志只写 stderr）加一个工具 `s19_ping`，只用 Host 现有的两个包。
- [Http/](Http/)：同一个工具，加 `ModelContextProtocol.AspNetCore` 2.2.0 和 Kestrel（`WebApplication.CreateSlimBuilder`，内容根目录固定为程序目录、不重新加载配置，同 TIA 的 `HttpHostOptions`）。
  - **只监听 `127.0.0.1`**（`Listen(IPAddress.Loopback, 端口)`，显式监听也覆盖 `ASPNETCORE_URLS`），端口为 0 时由系统分配。回环地址上的监听不触发防火墙提示（运行后也没有出现指向这些程序的规则），不改任何设置。
  - [Gate.cs](Http/Gate.cs)：第 4、5 条，作为第一个中间件，在路由之前处理每个请求：来源被锁定 → 429；有 `Origin` 且不在允许列表中 → 403；`Authorization` 不是恰好一个 `Bearer <密钥>` → 401 并计一次失败。比较方法：两边都先算 SHA-256，再用 `CryptographicOperations.FixedTimeEquals` 比较摘要，所以长度和内容都不影响耗时。拒绝时响应体为空，不加 `WWW-Authenticate`。
  - [RequestLog.cs](Http/RequestLog.cs)：每个请求一行 JSON，记下全部请求头（`Authorization` 只记方案、长度和是否匹配）、JSON-RPC 方法、协议版本和决定。
  - [S19Tools.cs](Http/S19Tools.cs)：`s19_whoami` 报告处理器能看到的会话 ID、协议版本、客户端名称（分别从请求、`_meta`、根服务容器取）和来源地址；`s19_select` / `s19_selected` 模拟 `eplan_session_select`，按 `McpSession.SessionId` 保存一个数字。
  - 参数 `stdio` 时用同一个 exe 跑 stdio 方式，看带上 ASP.NET Core 是否拖慢 stdio。
- [Probe/](Probe/)：驱动程序。每次运行生成新的 256 位密钥（base64url，43 个字符），只经环境变量交给原型，不出现在命令行和日志中。`startup` 测启动：从 `Process.Start` 到 stderr 的就绪行、到 `initialize` 的回答；`checks` 用 `HttpClient`（不用代理）探测第 4、5 条和三种会话模式；需要另一个来源时把客户端套接字绑定到 `127.0.0.2` / `127.0.0.3`。
- [run.ps1](run.ps1)：按 Host 的发布方式（ADR-0004：自包含单文件 win-x64，`dotnet publish -r win-x64 --self-contained -p:PublishSingleFile=true`）发布 Stdio、Http、两者的压缩版（`EnableCompressionInSingleFile`）和当前的 Host；测体积和启动（先跑一次，再取 5 次的中位数）；运行探测；再用 Claude Code 连接原型。
  - Claude Code 的做法同 S17：每个 `claude` 子进程单独设置环境（`ProcessStartInfo`），`CLAUDE_CONFIG_DIR` 指向 `.local\spikes\S19\<时间>\claude-config`，去掉从桌面版会话继承的 `CLAUDE*`、`ANTHROPIC*`、`MCP_*` 和令牌变量；`HTTP(S)_PROXY` 指向没有监听的 `127.0.0.1:9`，`NO_PROXY=127.0.0.1,localhost`，所以除了回环地址什么都连不出去；不登录，不向模型发提示。
  - 每种情况都新起一个原型（新密钥、新端口），依次 `mcp add --transport http --scope user eplan-vm <URL> --header "Authorization: Bearer <密钥>"`、`mcp list`、`mcp get`、`mcp remove`。
  - 运行前后只读取真实客户端配置文件的元数据（4.11 表中的全部位置和 `~\.claude\`），并在 Claude Code 的文件中搜索 `eplan-vm`；隔离检查后，除了 `~\.claude.json` 和桌面版的配置（桌面版自己会改写）之外，真实文件有变化就停止。
  - 日志中的密钥一律显示为 `<key>`，路径换成 `<run>`、`%USERPROFILE%` 等。

```powershell
spikes\S19-vm-http\run.ps1                    # 全部，约 6 分钟；日志在 .local\spikes\S19\<时间>\log.txt
spikes\S19-vm-http\run.ps1 -SkipPublish -SkipStartup -SkipChecks   # 只跑 Claude Code 一段
```

下面的数字来自 2026-10-21 的完整运行（`20261021-005343`）。同一小时内较早的一次运行各项启动时间低 10%–15%，相对大小相同。

## 结果

### 1. 体积与启动
**包**（[Directory.Packages.props](../../Directory.Packages.props) 只为本 spike 加了一行，注明 CI 的项目都不引用）：

| 包 | 版本 | 许可证 | 说明 |
|---|---|---|---|
| ModelContextProtocol.AspNetCore | 2.2.0 | Apache-2.0（nuspec） | 依赖 `ModelContextProtocol` **恰好 [2.2.0]**，两者必须一起升级；带来框架引用 `Microsoft.AspNetCore.App`（.NET 的一部分，MIT），除此之外锁文件中没有新的 NuGet 包 |
| ModelContextProtocol | 2.2.0 | Apache-2.0 | 已有 |

**体积**（自包含、单文件、win-x64）：

| 变体 | exe 字节 | MB | 同目录的其他文件 |
|---|---|---|---|
| Stdio（基线） | 77,871,545 | 74.3 | 只有 .pdb、.xml |
| Http | 105,628,750 | 100.7 | 另有 `aspnetcorev2_inprocess.dll`（392,488 字节，IIS 进程内处理程序） |
| **差** | **+27,757,205** | **+26.5（+35.6%）** | |
| Stdio，压缩 | 38,955,924 | 37.2 | |
| Http，压缩 | 50,873,594 | 48.5 | |
| 差（压缩） | +11,917,670 | +11.4（+30.6%） | |
| 当前的 Host（`eplanmcp.exe`，同样方式发布，作参照） | 78,577,154 | 74.9 | |

- 照此估算，Host 加上 HTTP 后约 101 MB（不压缩）。
- 单文件不打包本机 DLL，`aspnetcorev2_inprocess.dll` 被放在 exe 旁边。只用 Kestrel 时不需要它：只复制 exe 到空目录，HTTP 方式照常启动和应答（`http-exe-alone`）。安装包可以不带它。
- 没有试裁剪（`PublishTrimmed`）：SDK 用反射发现工具。

**启动**（毫秒，5 次的中位数，括号中是最小–最大；"首次"是发布后第一次运行；"私有内存"是应答 `initialize` 后的 `PrivateMemorySize64`）：

| 程序与方式 | 就绪（开始监听） | 应答 `initialize` | 首次 | 私有内存 |
|---|---|---|---|---|
| Stdio exe，stdio | — | 203（200–207） | 252 | 10.7 MB |
| Http exe，stdio | — | **175**（172–178） | 239 | 11.4 MB |
| Http exe，HTTP | **148**（145–149） | 268（261–270） | 156 / 307 | 14.8 MB |
| Stdio 压缩，stdio | — | 254（251–262） | 311 | 31.4 MB |
| Http 压缩，HTTP | 212（211–222） | 338（332–343） | 281 / 431 | 48.4 MB |
| 当前的 Host，stdio | — | 285（279–293） | 365 | 19.5 MB |

- HTTP 方式从启动进程到开始监听约 0.15 秒，到第一个 `initialize` 应答约 0.27 秒（含首个请求的 JIT 和建立连接）。
- **带上 ASP.NET Core 后 stdio 反而快约 28 ms**：有了 `Microsoft.AspNetCore.App` 框架引用，`Microsoft.Extensions.*` 改用 ASP.NET Core 运行时包中的 10.0.13 版（ReadyToRun 预编译），不再用 NuGet 的 10.0.12 版（只有 IL）。见 `obj\…\S19.Http.deps.json`：`Microsoft.Extensions.Hosting.dll` 来自 `runtimepack.Microsoft.AspNetCore.App.Runtime.win-x64/10.0.13`。所以 Host 的 stdio 方式不会因此变慢。
- 压缩使体积减半，但每次启动多 50–70 ms，私有内存多 20–34 MB（解压到内存）。

### 2. 第 4、5 条：探测结果
全部符合预期（`checks … Stateful --gate`；G、O 两组从 `127.0.0.3` 发出，不影响 L 组的来源）：

| # | 请求 | 预期 | 结果 |
|---|---|---|---|
| G1 | `POST /mcp` initialize，没有 `Authorization` | 401 | 401，响应体 0 字节，没有 `WWW-Authenticate` |
| G2 | 同样长度的错误密钥 | 401 | 401，响应体 0 字节 |
| G3 / G4 | 正确密钥但写成 `bearer …` / 不带 `Bearer ` | 401 | 401（要求完全一致） |
| G11 | 一个 `Authorization` 中写两遍正确的 `Bearer <密钥>` | 401 | 401 |
| G5 / G6 | `GET /mcp/health` 不带 / 带密钥（TIA 不需要密钥就回答） | 401 / 404 | 401 / 404 |
| G7 | `GET /` 不带密钥 | 401 | 401 |
| G8 / G9 | 带密钥 `GET /sse`、`POST /message`（旧的 SSE 端点） | 404 | 404：SDK 2.2.0 的 `EnableLegacySse` 默认关闭 |
| G10 | 带密钥、不带 `Origin` 的 initialize | 200 | 200 |
| O1 | 带密钥 + `Origin: http://evil.example`（浏览器那样） | 403 | 403，响应体 0 字节 |
| O2 | 不带密钥 + 同上 | 403 | 403（先判 `Origin`） |
| O3 | 带密钥 + `Origin: null`（沙箱页面、`file://`） | 403 | 403 |
| O4 | 来自 `http://evil.example` 的 CORS 预检 `OPTIONS /mcp` | 403 | 403，没有 `Access-Control-Allow-Origin`，浏览器不会发出真正的请求 |
| O5 / O6 | 允许列表中的 `Origin`，带 / 不带密钥 | 200 / 401 | 200 / 401 |
| O7 | 允许列表为空（默认）时的同一个 `Origin` + 密钥 | 403 | 403 |
| L1 | `127.0.0.1` 一分钟内 10 个错误密钥 | 10 × 401 | 10 × 401 |
| L2 | 第 11 个请求，改用正确密钥 | 429 | 429，响应体 0 字节 |
| L3 | 同时从 `127.0.0.2` 用正确密钥 | 200 | 200（按来源计数） |
| L4 | 日志 | 1 条 | 1 条警告：来源、次数、窗口和锁定时长 |
| L5 | 每 2 秒用正确密钥重试 | 200 | 锁定后 60 秒第一次得到 200；429 不计为失败，所以重试不会延长锁定 |

### 3. SDK 的 HTTP 会话（`Mcp-Session-Id`）
- **SDK 2.2.0 的 `HttpServerTransportOptions.SessionMode` 默认是 `Stateless`**（XML 文档："as of the 2026-07-28 protocol revision (SEP-2567)"）。2026-07-28 版协议取消了 `initialize` 握手和 `Mcp-Session-Id`（SEP-2575、SEP-2567），客户端信息改为每个请求的 `_meta` 中带。另外两种模式：`Stateful`（对 2026-07-28 的请求回 -32022，让两种协议都支持的客户端退回 `initialize`）和 `StatefulForInitializeClients`（2025-11-25 及以前的客户端有会话，2026-07-28 的客户端无状态）。
- 三种模式的探测（同一个原型，`S19_SESSION_MODE`）：

| # | 请求 | Stateless（默认） | Stateful | StatefulForInitializeClients |
|---|---|---|---|---|
| S1 | initialize（2025-11-25） | 200，**没有** `Mcp-Session-Id` | 200，有 | 200，有 |
| S4 | 会话中的 `s19_whoami` | `sessionId` null，客户端名称 null（每个请求一个新的服务器对象，`initialize` 中的名称丢了） | 有会话 ID 和客户端名称 | 同 Stateful |
| S6 | A 选 1、B 选 2，再各自读 | A、B 都读到 2（共用） | A 读到 1、B 读到 2 | A 读到 1、B 读到 2 |
| S8 | 不认识的会话 ID | 400（无状态模式不接受这个头） | 404，-32001 Session not found | 404 |
| S9 | 不带会话 ID、没有 initialize 的 `tools/list`（2025-11-25） | 200 | 400 | 400 |
| S10 | 2026-07-28 的 `tools/list`（`_meta` 带协议版本、客户端信息） | 200 | **400，-32022** | 200，没有会话 |
| S10b | 2026-07-28 的 `s19_whoami` | 客户端名称来自 `_meta`，没有会话 | -32022 | 同 Stateless |
| S11 | 会话 A 的 ID 加密钥，从另一个来源（`127.0.0.2`） | — | **200**（SDK 不把会话与来源地址绑定） | 200 |
| S12 | `DELETE` 会话 A，再用它 | — | 200，然后 404 | 200，然后 404 |
| S7 | 会话 A 的 ID，不带密钥 | 401 | 401 | 401 |

- `ConfigureSessionOptions(HttpContext, McpServerOptions, …)`（TIA 用它为每个会话建工具集合）：Stateful 模式下每个会话调用一次，此时 `HttpContext.Connection.RemoteIpAddress` 和响应头中刚生成的 `Mcp-Session-Id` 都已可用，所以可以在这里登记"会话 → 来源地址"。Stateless 模式下每个请求调用一次。`RunSessionHandler`（会话开始和结束都能插入逻辑）在 2.2.0 中标为实验性 API。`PerSessionExecutionContext` 默认 false，工具中可以用 `IHttpContextAccessor` 取当前请求的来源地址。
- 会话的回收：`IdleTimeout` 默认 2 小时，`MaxIdleSessionCount` 默认 10,000。Claude Code 每次 `mcp list` / `mcp get` 都新建一个会话，结束时不发 `DELETE`（见第 4 项），所以 Host 要把空闲时间和数量调小。
- **HTTP 方式下根服务容器中没有 `McpServer`**：从单例中 `GetService<McpServer>()` 得到 null（三种模式都是）。`WorkbenchChannel` 现在就是这样取客户端名称的，HTTP 方式下 Workbench 会一直显示"未知客户端"。stdio 方式下根容器中有 `McpServer`，2025-11-25 和 2026-07-28 两种客户端都能取到名称（运行日志的 `=== stdio` 两段），所以现在的 stdio 不受影响。

### 4. Claude Code 2.1.299 作为远程客户端
- **命令**（`claude mcp add --help`）：`claude mcp add --transport http --scope user eplan-vm http://<地址>:<端口>/mcp --header "Authorization: Bearer <密钥>"`；`-H` 可重复。输出中请求头显示为 `[REDACTED]`。
- **存下的条目**（`<CLAUDE_CONFIG_DIR>\.claude.json` 顶层 `mcpServers`，用户范围；与 TIA 为 Claude Code 写的形状相同）：

  ```json
  "eplan-vm": {
    "type": "http",
    "url": "http://127.0.0.1:<端口>/mcp",
    "headers": { "Authorization": "Bearer <key>" }
  }
  ```

- **连接状态**：密钥正确时 `mcp list` 显示 `eplan-vm: http://127.0.0.1:<端口>/mcp (HTTP) - √ Connected`，`mcp get` 显示 `Status: √ Connected`。注意 **`mcp get` 会把 `Authorization` 的值原样打印出来**（`mcp add` 遮住了，`get` 没有）。
- **密钥错误**：`× Failed to connect — Server rejected the configured Authorization header (HTTP 401). Check that the token is valid for this MCP endpoint — OAuth fallback is disabled when headers.Authorization is set.` 只发了 2 个请求（2026-07-28 的 `server/discover` 和退回的 `initialize`），**没有去找 OAuth 的发现端点**。所以每次连接失败计 2 次，连续 5 次连接失败就会被锁定一分钟。
- **`Origin`：Claude Code 不发**。所有请求的头只有 `Accept`、`Accept-Encoding`、`Authorization`、`Connection`、`Content-Length`、`Content-Type`、`Host`、`User-Agent: claude-code/2.1.299 (sdk-cli)`，以及 `mcp-method`、`mcp-protocol-version`、`mcp-session-id`。所以允许列表可以保持为空。
- **协议协商**（请求日志）：
  - Stateful：先发 2026-07-28 的 `server/discover`（`_meta` 带协议版本、`clientInfo`、`clientCapabilities`）→ 400（-32022）→ 改发 `initialize`（`protocolVersion: 2025-11-25`，`clientInfo: claude-code/2.1.299`）→ `notifications/initialized` → `tools/list` → `GET /mcp`（SSE 流），后面都带 `Mcp-Session-Id`。退回只多一次往返（本机 7–84 ms），`mcp list` 仍显示 Connected。
  - Stateless 和 StatefulForInitializeClients：一直用 2026-07-28（`server/discover`、`tools/list`），**没有会话**。
- **`${VAR}` 在请求头中会展开**：`--header 'Authorization: Bearer ${EPLANMCP_S19_KEY}'` 原样存进 `.claude.json`；`claude` 进程有这个环境变量时 Connected，没有时 401，并在 "MCP config diagnostics" 中警告 `Missing environment variables: EPLANMCP_S19_KEY`。所以宿主机的配置文件中可以不写明文密钥，但要工程师自己设置用户环境变量（CLAUDE.md 不允许安装程序改全局环境变量）。

### 5. 其他客户端（只查文档，没有安装）
2026-10-21 查阅各客户端的官方文档（个别项见源码）。"TIA"一列是 TIA 的 `ClientProfiles.Entry` / `MergeToml` 为远程方式写的内容（只读查阅）。示例地址 `http://<虚拟机>:8766/mcp`。

| 客户端 | 远程条目（地址键 / 类型） | 自定义请求头 | 不在文件中写明文密钥 | 命令行添加 | TIA | 依据 |
|---|---|---|---|---|---|---|
| Claude Code | `mcpServers`：`url` + `"type": "http"` | 能 | `${VAR}`（第 4 项实测）；`headersHelper`：一条命令把请求头以 JSON 打印到 stdout，每次连接都运行，10 秒超时，401/403 后重跑一次 | `claude mcp add --transport http -H` | 相同 | 实测；[MCP 文档](https://code.claude.com/docs/en/mcp) |
| Claude 桌面版 | `claude_desktop_config.json` 只有 stdio（`command`/`args`/`env`）；远程服务器只能作为"连接器"按 URL 添加 | 连接器的请求头认证是 beta，只对部分组织开放 | 连接器加密保存 | 无 | 无此客户端 | [添加未列出的连接器](https://claude.com/docs/connectors/custom/add-unlisted)、[MCP tunnels](https://claude.com/docs/connectors/mcp-tunnels/overview)、[连接本地服务器](https://modelcontextprotocol.io/docs/develop/connect-local-servers) |
| Codex | `[mcp_servers.<名称>]`：`url`，没有类型键 | 能：`http_headers` | `bearer_token_env_var`、`env_http_headers` | `codex mcp add <名称> --url <URL>`；没有 `--header`，`--bearer-token-env-var` 只在源码中 | `url` + `http_headers`，另加两个超时 | [Codex MCP](https://developers.openai.com/codex/mcp)（308 到 learn.chatgpt.com/docs/extend/mcp）；不再需要实验开关（[openai/codex#8087](https://github.com/openai/codex/pull/8087)） |
| Gemini CLI | 文档：`httpUrl`（`url` 是 SSE）；源码：`httpUrl` 已标为弃用，`gemini mcp add -t http` 写 `url` + `"type": "http"` | 能 | 配置参考说 settings 的字符串值可用 `$VAR`、`${VAR}`；MCP 页只对 `env` 写明 | `gemini mcp add --scope user -t http -H …`（默认范围是 project） | `httpUrl` | [mcp-server.md](https://github.com/google-gemini/gemini-cli/blob/main/docs/tools/mcp-server.md)、[配置参考](https://geminicli.com/docs/reference/configuration/)、源码 `packages/core/src/config/config.ts` |
| Qwen Code | `httpUrl` | 能 | `$VAR`、`${VAR}`（设置页） | `qwen mcp add -t http -H …`（默认 user） | `httpUrl` | [MCP](https://qwenlm.github.io/qwen-code-docs/en/users/features/mcp/)、[设置](https://qwenlm.github.io/qwen-code-docs/en/users/configuration/settings/) |
| Kimi Code CLI | `%KIMI_CODE_HOME%\mcp.json` 或 `~\.kimi-code\mcp.json`：`url`（不写 `transport` 即 HTTP） | 能 | `bearerTokenEnvVar` | 无，在界面中用 `/mcp-config`（已归档的旧 Kimi CLI 用 `~\.kimi\mcp.json`，有 `kimi mcp add --header`） | `url` + `headers` | [Kimi Code MCP](https://moonshotai.github.io/kimi-code/en/customization/mcp.html)、[kimi-cli 仓库](https://github.com/MoonshotAI/kimi-cli) |
| CodeBuddy Code | `~\.codebuddy\.mcp.json`：`url` + `"type": "http"` | 能 | `url`、`headers` 中的 `${VAR}`、`${VAR:-默认值}` | `codebuddy mcp add --scope user --transport http --header … -- <名称> <URL>` | 相同 | [CodeBuddy MCP](https://www.codebuddy.cn/docs/cli/mcp) |
| OpenCode | 根键 `mcp`：`url` + `"type": "remote"` + `enabled` | 能 | `{env:VAR}`、`{file:路径}` | `opencode mcp add` 是交互式的，没有请求头参数 | 相同（没写 `oauth: false`） | [MCP servers](https://opencode.ai/docs/mcp-servers/)；文档不说用哪种传输，[issue #8058](https://github.com/anomalyco/opencode/issues/8058) 中维护者说会自动判断，有用户说只支持 Streamable HTTP 的服务器连不上 |
| Qwen Agent | **找不到** `~\.qwen-agent\mcp.json` 或"千问"桌面应用的官方文档；QwenLM/Qwen-Agent（Python 框架）的配置是代码中的字典，源码接受 `url` + `"type": "streamable-http"` + `headers`（不写 `type` 时默认 SSE） | 只在源码中 | 未见 | 无 | `url` + `headers`，不写 `type` | [Qwen-Agent](https://github.com/QwenLM/Qwen-Agent)、源码 `qwen_agent/tools/mcp_manager.py` |
| Cursor | `~\.cursor\mcp.json`：`url`，不写类型 | 能 | `url`、`headers` 中的 `${env:NAME}` | 无（Cursor 命令行的 `mcp` 子命令没有 add） | 相同 | [Cursor MCP](https://cursor.com/docs/context/mcp) |
| VS Code Copilot | 用户的 `mcp.json`，根键 `servers`：`url` + `"type": "http"` | 能 | `inputs` 中 `"password": true` 的输入项，用 `${input:<id>}` 引用，VS Code 安全保存，文件中没有密钥 | `code --add-mcp '<json>'`（文档的例子只有 stdio） | 相同 | [MCP 配置参考](https://code.visualstudio.com/docs/copilot/reference/mcp-configuration) |

- **发不了请求头、按第 10 条不列出的：Claude 桌面版**。连接器由 Anthropic 的后端发起连接，地址要是 HTTPS；服务器在私有网络中时文档指向 MCP tunnels，那是 Enterprise 套餐的研究预览，并要求服务器用 OAuth。所以桌面版到不了宿主机能访问的虚拟机地址。唯一的办法是本地的 stdio 代理（例如第三方的 `mcp-remote --header`），不是官方方案。
- **只有文档、TIA 的代码也这样写的**：Codex、Gemini CLI、Qwen Code、Kimi Code CLI、CodeBuddy Code、OpenCode、Cursor、VS Code Copilot；**无法核实**：Qwen Agent（只有 TIA 的代码和注释）。都要在 4.11 说的"实现时逐个用真实客户端核对"中实测。
- 文档都没有提到 `Origin`。Cursor、VS Code、桌面版是 Electron 程序，但 MCP 请求预计由 Node 发出，不带 `Origin`，没有核实。
- 与 TIA 的差别：Gemini CLI 的源码已把 `httpUrl` 标为弃用（仍能读），`gemini mcp add` 写的是 `url` + `type`；OpenCode 的文档有 `"oauth": false`（关掉 OAuth 尝试）的例子，TIA 没写。

### 6. Host 中要按会话保存的状态（计划，本 spike 不改代码）
读了 `McpServerSetup.cs`、`Bridge/BridgeClient.cs`、`Tools/SessionTools.cs`、`Safety/ApprovalService.cs`、`Safety/WorkbenchChannel.cs`、`Safety/WritePipeline.cs`、`Safety/DanglingWrites.cs`。现在全部是单例，靠"一个进程一个客户端"成立。

| 状态 | 现在 | HTTP 方式下 | 改法 |
|---|---|---|---|
| 选中的 EPLAN 实例（`eplan_session_select`） | `BridgeClient._selectedProcessId`，整个进程一个 | 每个会话一个 | 放进会话状态；`BridgeClient` 的 `SelectAsync`、`GetStatusAsync`、`InvokeAsync` 改读调用方会话的选择 |
| 到 EPLAN 的通道和串行队列 | `BridgeClient` 只有一条通道和一个 `_queue`，换实例时断开重连 | 所有会话共用，**每个实例一条通道、一个队列**（架构 5.2） | `BridgeClient` 拆成共用的"每实例连接"（按端点的字典）和会话的选择；否则两个会话选了不同实例时会来回断开重连 |
| 客户端名称（审批界面） | `WorkbenchChannel` 从根容器取 `McpServer.ClientInfo` | 根容器中没有 `McpServer`（第 3 项），永远是 null | 在调用工具时从请求取（`RequestContext.Server.ClientInfo`，2026-07-28 时来自 `_meta`），放进 `ApprovalQuestion`；`WorkbenchChannel`、`EplanDialogChannel`（`ApprovalTexts`）都用它 |
| 来源地址、"经网络：<地址>" | 无 | 每个会话一个 | 在 `ConfigureSessionOptions` 中登记"会话 ID → 来源地址"（第 3 项：此时两者都已可用）；审批界面显示；Workbench 协议的 `ApprovalAskParams` 加一个字段（协议要升版本或允许可选字段） |
| 审计 | 记录中没有客户端信息 | 第 8 条：记每个会话的来源地址 | `approval`、`execution_started`、`execution_result` 等记录加 `caller`：传输方式、客户端名称（未验证）、来源地址、会话 ID 的哈希（不写原值） |
| 不变 | `SuspensionStore`（按项目和实例存在磁盘上）、`AuditLog`（已用文件锁容纳多个 Host）、`DanglingWrites`（按 Host 进程）、`ApprovalService`（无状态）、`WritePipeline`、`ExportFolders`、`BackupFolders`、`HostConfig` | 所有会话共用 | — |

做法：
1. **`SessionMode = Stateful`**，显式设置（第 3 项：默认的 Stateless 下所有调用方共用一份状态，S6）。
2. 加一个 `tools/call` 过滤器（与现有的 `UnknownArgumentFilter` 同一处注册），从请求得到"调用方"：会话键（`SessionId`，stdio 时固定为 `stdio`）、客户端名称、传输方式、来源地址（`IHttpContextAccessor`，要求 `PerSessionExecutionContext` 保持 false），在这次调用期间放进 `AsyncLocal`；`BridgeClient`、`WritePipeline`、`ApprovalService` 从那里读。工具方法的签名不变。
3. 会话表：`ConfigureSessionOptions` 中建项；`DELETE` 和空闲时清除。`RunSessionHandler` 能知道会话结束，但 2.2.0 中是实验性 API，可以改为按最后使用时间清理，空闲时间与 SDK 的 `IdleTimeout` 一致。把 `IdleTimeout`、`MaxIdleSessionCount` 调小（Claude Code 每次健康检查都留下一个会话）。
4. 门中再加一条：带会话 ID 的请求，来源地址必须与建会话时相同（S11：SDK 不检查）。
5. Kestrel 的限额（T25）：请求体上限（默认 30 MB）、并发连接数、请求头超时，在实施时定值。
6. stdio 方式不变：一个进程一个会话，会话键固定。
7. 测试：Medium 测试可以像 `Probe` 一样在 `127.0.0.1` 的 0 号端口上启动 Host，用 `HttpClient` 探测；两个会话选不同实例、互不影响，用假的 Bridge 发现。

### 真实配置与网络（安全核对）
- 完整运行前后，4.11 表中的全部配置位置与 `~\.claude\`：存在的只有 `~\.claude.json`（07:38:35，前后相同，不含 `eplan-vm` 或 `s19-`）、`~\.claude\settings.json`（2026-10-16，相同）、`~\.codex\config.toml`（2026-10-19，相同）和桌面版的配置（07:27:43，相同）；其他都不存在，前后也不存在。隔离检查后的中间核对同样没有变化。
- 所有服务器只监听 `127.0.0.1`（原型的代码和驱动程序各检查一次）；没有修改防火墙、URL ACL、服务或任何 Windows 设置，没有用管理员权限。只读查询 `Get-NetFirewallApplicationFilter`：585 条中没有一条指向 S19 的程序或 `eplanmcp`。
- 密钥都在运行时生成，只存在于 `.local\`（隔离的 `.claude.json` 中有明文，`.local\` 不进 git）；提交的文件和日志摘录中没有密钥。

## 结论
1. **体积**：ASP.NET Core 使自包含单文件多 26.5 MB（+36%），Host 将从 74.9 MB 变为约 101 MB；压缩后体积减半，但每次启动多 50–70 ms、私有内存多 20–34 MB。建议不压缩（保持现在的发布方式），安装包不带 `aspnetcorev2_inprocess.dll`。新依赖只有 `ModelContextProtocol.AspNetCore`（Apache-2.0，与 SDK 同版本，版本号锁死为相等）和 .NET 自带的 `Microsoft.AspNetCore.App`。
2. **启动**：HTTP 方式约 0.15 秒开始监听，第一个 `initialize` 约 0.27 秒应答；stdio 方式不变慢（反而快约 28 ms，因为改用预编译的 `Microsoft.Extensions.*`）。
3. **第 4、5 条可以照写**：一个放在最前面的中间件，顺序为"锁定 → `Origin` → 密钥"，只把 401 计为失败。全部 23 项探测符合预期；TIA 的 `/mcp/health` 这类免密钥端点不需要，旧 SSE 端点默认关闭。建议实施时再加会话与来源地址的绑定和 Kestrel 限额（第 6 项）。
4. **🔴 会话：要显式设 `SessionMode = Stateful`**。SDK 2.2.0 默认无状态，2026-07-28 版协议也取消了会话；不设置时所有客户端共用一份"选中的实例"，`initialize` 中的客户端名称也会丢。设为 Stateful 后，Claude Code 2.1.299 先试 2026-07-28、得到 -32022 后退回 2025-11-25 并带 `Mcp-Session-Id`，只多一次往返。剩余风险：将来只支持 2026-07-28 的客户端会被 Stateful 拒绝，那时要么改用 `StatefulForInitializeClients` 并为无会话的调用方另定规则（例如多个 EPLAN 时每次调用都带实例），要么改工具契约。这一点影响 ADR-0018 第 8 条的含义，**建议实施前请项目负责人确认，必要时补充 ADR-0018**。
5. **`WorkbenchChannel` 的客户端名称在 HTTP 方式下一定是 null**（根容器中没有 `McpServer`），必须改为从请求取；这是第 6 项计划中最先要做的一项。
6. **Claude Code 可以作为远程客户端**：`claude mcp add --transport http --scope user eplan-vm <URL> --header "Authorization: Bearer <密钥>"` 写入 `{"type":"http","url":…,"headers":{"Authorization":…}}`，与 TIA 相同；`mcp list`/`mcp get` 正确反映连接与 401；不发 `Origin`，允许列表可以保持为空。`mcp get` 会打印密钥，入门指南要提醒。请求头中的 `${VAR}` 和文档中的 `headersHelper` 都能让宿主机的配置文件不含明文密钥；`headersHelper` 可以调用宿主机上的 `eplanmcp` 读取 DPAPI 保存的密钥，值得在实施时试（只查了文档）。
7. **其他客户端**：按文档，除 Claude 桌面版外都能发送自定义请求头，TIA 的写法与文档一致（Gemini CLI 的 `httpUrl` 已标为弃用但可用）。**Claude 桌面版按第 10 条在虚拟机方式下不列出**；Qwen Agent 无法从官方文档核实。Codex、Kimi Code、Cursor、VS Code、OpenCode 也都有不写明文密钥的办法（环境变量或 VS Code 的安全输入），可以作为入门指南中的进阶写法。

## 局限
- **没有真实的虚拟机或第二台电脑**：没有绑定局域网 IPv4，没有看非回环地址上的防火墙提示、真实网络延迟和断线重连。
- **防火墙规则**（第 6 条：只放行宿主机 IPv4、只用于 `eplanmcp.exe`、只在域和专用网络配置文件中生效）要管理员授权，需要项目负责人在虚拟机中经 UAC 操作，没有做。
- **局域网明文抓包**（SEC-30 的剩余风险）没有做。
- 其他客户端只查了文档，没有安装；它们是否发 `Origin`、实际写的配置、Gemini CLI 用 `httpUrl` 还是 `url` + `type` 能否连上、OpenCode 对只支持 Streamable HTTP 的服务器的表现，都要实测。原型和它的请求日志可以直接用来核对。
- Claude Code 只跑了 `mcp add/list/get/remove`，没有在会话中让模型调用工具；HTTP 方式下的进度通知、取消、长时间调用（S13 的内容）没有测。
- 启动时间是在文件已缓存的机器上测的，不是开机后的第一次；同一小时内两次运行相差 10%–15%。
- 恒定时间比较只由代码保证（比较两个 SHA-256 摘要），没有做计时测量。
- Host 本身没有加 HTTP，Host 的压缩版也没有测；没有试裁剪。


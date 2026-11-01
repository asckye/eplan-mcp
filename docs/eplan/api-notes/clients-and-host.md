# [EPLAN API 笔记](../api-notes.md)：客户端与 Host

### A6. 由 MSIX 打包应用启动的进程（Claude 桌面版）
- Claude 桌面应用以 MSIX 包安装（包族名 `Claude_pzs8sxrjxfjjc`）。由它启动的子进程写入 `%LOCALAPPDATA%` 时，**被重定向到 `%LOCALAPPDATA%\Packages\Claude_pzs8sxrjxfjjc\LocalCache\Local\`**，包外的进程（EPLAN）看不到这些文件。写到 `D:\…` 以及 `%TEMP%`（即 `%LOCALAPPDATA%\Temp`）下的文件，EPLAN 可以正常读取。— spike S0 第一次运行失败的原因；检查包的 LocalCache 目录确认。
  - 对本项目的影响：由 Claude 桌面版启动的 Host，不能把 EPLAN 需要读取的文件（scratch 项目、实例发现文件）写到 `%LOCALAPPDATA%`；审计日志写在那里，用户也会找不到。见 ADR-0008。
  - **由客户端启动的进程没有包身份，但仍被重定向**：Claude 桌面版中 Claude Code 会话的 shell、它启动的 `eplanmcp`，`GetCurrentPackageFullName` 都返回 `APPMODEL_ERROR_NO_PACKAGE`。可是它们写入 `%LOCALAPPDATA%`、`%APPDATA%` 的文件，仍然落在包的 `LocalCache\Local`、`LocalCache\Roaming` 下。所以不能用包身份判断是否被重定向。— spike S5。
  - 不重定向：`%TEMP%`，以及数据根目录 `%USERPROFILE%\EplanMcp`（包内复制的夹具，包外的 EPLAN 正常打开）。命名管道不受影响：包内启动的 `eplanmcp` 能连上包外 EPLAN 的插件。— spike S5。
  - 包内进程能读到真实位置的 `%LOCALAPPDATA%`（能看到其他程序的目录），所以"Bridge 写、Host 读"可以读到；会话期间由包外进程新写入的文件，没有单独验证。— spike S5。

### A21. Claude Code 客户端的进度与图片（spike S13，2026-10-20，Claude Code 2.1.299）
详细数据见 [spike S13](../../../spikes/S13-client-progress/README.md)。
- Claude Code 每次 `tools/call` 都在 `_meta` 中带 `progressToken`，服务器的进度通知送达；`initialize` 的协议版本为 `2025-11-25`。— S13。
- 工具结果中的 PNG（`eplan_export_image` 的 `inline`）交给了模型，模型读出了图上的设备标识和文字。— S13。
- 中断（SDK 的 `interrupt` 控制请求，与界面中按 Esc 同一个中止路径）约 0.2 秒后发出 `notifications/cancelled`，`reason` 为 "AbortError: remote-cancel"。— S13，2026-10-21。
- 无头模式下 150 秒的调用一直等到结果，期间每 5 秒的进度都送达，没有超时。交互界面中超过 2 分钟转到后台的显示没有测。— S13，2026-10-21。

### B4. Remote Client（`Eplan.EplApi.RemoteClientu.dll`）
- `EplanRemoteClient` 的主要成员：`Connect(host, port[, TimeSpan])`、`Ping()`、`ExecuteAction(string)`、`ExecuteAction(string, ref Eplan.EplApi.Remoting.CallingContext)`、`StartEplan(...)`、`StopEplan()`、`GetActiveEplanServersOnLocalMachine(...)`、`SelectEplanObjects(...)`、`LockAllEplanObjects`/`UnlockAllEplanObjects`、`DisableSynchronizingCalls(bool)`，以及属性 `SynchronousMode`。— 反射。
- **只有在 `SynchronousMode = true` 时才能读回返回值**（默认是异步，没有返回值）。返回值都会被转换为字符串；输出键如果在调用前已存在于字典中，就不会被覆盖，所以输出要使用不同的键名。— XML + IL 分析。
- **远程调用只在 EPLAN 空闲且没有 action 正在运行时才执行**，可以用 `DisableSynchronizingCalls(true)` 关闭这项同步。— XML。
- 传输层是旧版 C-core gRPC（`Grpc.Core`），使用 `ChannelCredentials.Insecure`（没有 TLS），附带一个双工回调流。客户端进程还需要 `System.Memory` 和 `System.Runtime.CompilerServices.Unsafe`，这两个 Bin 里都没有。— 反射 + IL。
- 服务器端：需要开启"允许通过 Remote Client 进行远程访问"（`STATION.Remoting.AllowLocalAccess`）；EPLAN 不能以 `/NoRemoting` 启动；默认端口 49152，可以用 `/EplanServerPort` 指定（49152–65535）。— `2026/EPLANRemoting.html`。
- 远程调用 `ExecuteScript` 时，即使脚本编译失败也会返回成功（见 A1）。
- 客户端（net481 x64）引用 EPLAN Bin 中的 `RemoteClientu`、`Remotingu`、`Grpc.Core.Api`、`Newtonsoft.Json`，另加 NuGet 包 `System.Memory` 4.5.5（带来 `Unsafe` 等）即可运行。**gRPC 会使用 `HTTP_PROXY` 等代理环境变量**，设置了代理时要在客户端进程内清除，否则连不上本机的 EPLAN。— spike S9 驱动。
- 通过 `ExecuteAction` 传给 action 的参数中，**含中文的路径没有完整传到**（action 报文件不存在）；ASCII 路径正常。— spike S9。

## C. MCP 客户端与 C# SDK（spike S4，2026-10-13）

> 不属于 EPLAN API，但同样是"代码依赖的外部行为"，按同样的规则记录。环境：ModelContextProtocol 2.2.0，Claude Code 2.1.295。

- **Claude Code 2.1.295 使用 MCP 2026-07-28**：连接时先发 `server/discover`（id `server-discover-probe-1`），每个请求的 `params._meta` 中都带 `io.modelcontextprotocol/protocolVersion: "2026-07-28"`、`clientInfo`（`name: "claude-code"`）和 `clientCapabilities: {"roots": {"listChanged": true}, "elicitation": {"form": {}, "url": {}}}`；随后用 `subscriptions/listen` 订阅 `toolsListChanged`。— [spike S4](../../../spikes/S4-elicitation/README.md) 的报文记录。
- **SDK 2.x 中，工具抛出 `InputRequiredException` 即可发起 elicitation**，两种协议都适用：2026-07-28 下返回 `resultType: "input_required"`（`inputRequests` + `requestState`），客户端用新的 id 重发 `tools/call` 并带回 `inputResponses` 和原样的 `requestState`；2025-11-25（stdio）下 SDK 改为向客户端发送 `elicitation/create` 请求，拿到回答后**重新调用同一个工具处理器**，`RequestContext.Params.InputResponses` 和 `RequestState` 同样有值。— S4 selftest（10 个场景）+ 报文核对。
- `McpServer.IsMrtrSupported` 在 2026-07-28 下、以及 stdio 上的 2025-11-25 会话中都为 true；客户端没有声明 elicitation 时 `McpServer.ClientCapabilities.Elicitation` 为 null。规范禁止向未声明的客户端发送 `elicitation/create`，因此两者都要检查。— S4 + [MRTR 规范](https://modelcontextprotocol.io/specification/2026-07-28/basic/patterns/mrtr)。
- `McpServer.ElicitAsync` 在无状态模式下抛出 `InvalidOperationException`，只适用于旧协议的有状态会话；Host 不使用它。— SDK 文档 `docs/concepts/elicitation`。
- SDK **不按 `inputSchema` 校验 `tools/call` 的参数**：即使 schema 中有 `additionalProperties: false`，拼错的参数（如 `limt`）也被静默忽略，工具按默认值执行。Host 用 `tools/call` 请求过滤器统一拒绝未知参数。— M1 骨架的 stdio 端到端测试（2026-10-13）。
- SDK 由 `[McpServerTool]` 生成的 `inputSchema` **不含 `additionalProperties: false`**；在特性中显式设置的 `Title`、`ReadOnly`、`Destructive`、`Idempotent`、`OpenWorld` 都出现在 `tools/list` 的 `annotations` 中（`title` 同时出现在工具顶层）。— S4 报文。
- 参数上的 DataAnnotations 特性会写进 `inputSchema`：`[AllowedValues]` 生成 `enum`，`[Range]` 生成 `minimum` / `maximum`；SDK 不校验 `enum`：不在其中的值照样传给工具方法（`minimum` / `maximum` 未单独验证）。— 2026-10-19 核对 `tools/list`，以及 `eplan_tool_usage` 的 stdio 端到端测试。
- 缺少必填参数（方法参数没有默认值，schema 中列在 `required`）时，工具方法不会被调用，SDK 返回 `isError: true` 和通用文字 "An error occurred invoking '<工具名>'."，**没有 reason，也不说缺了哪个参数**。可选参数显式传 `null` 时，方法收到 `null`。— 2026-10-19 stdio 端到端实测（`eplan_tool_usage`）。
  - 对本项目的影响：Host 的参数过滤器（`UnknownArgumentFilter`）在调用工具之前检查 schema 中的 `required`，缺少或为 `null` 时与未知参数一起报告为 `INVALID_ARGUMENT`，请求不会到达 SDK 的这段逻辑。
- Claude 桌面版自带的 Claude Code 可执行文件在无界面模式（`claude -p`）下单独运行时没有登录态（"Not logged in"），但在报错前已经完成 MCP 服务器的连接和 `tools/list`。— S4。
- 这个 `claude.exe` 在桌面版的进程中看到的位置是 `%APPDATA%\Claude\claude-code\<版本>\<构建>\claude.exe`，**包外的进程（例如用户自己打开的终端）在那里找不到它**：实际位置是包的重定向目录 `%LOCALAPPDATA%\Packages\Claude_pzs8sxrjxfjjc\LocalCache\Roaming\Claude\claude-code\…`（A6）。登录（`claude auth login`）只需做一次，两边共享登录态。— 2026-10-19 实测（Claude Code 2.1.299，登录后 `auth status` 为 `loggedIn: true`、`authMethod: claude.ai`）。

### Claude Code 无界面模式（Agent 评测的驱动方式，2026-10-19，Claude Code 2.1.299，未登录）
- **`--bare` 不读取登录态**（OAuth 和系统钥匙串），只接受 `ANTHROPIC_API_KEY` 或 `apiKeyHelper`。— [无界面模式文档](https://code.claude.com/docs/en/headless#start-faster-with-bare-mode)。
- `--max-turns` 可用，但 `claude --help` 不列出它（"`--help` does not list every flag"）。— [CLI 参考](https://code.claude.com/docs/en/cli-reference)。
- `claude -p --restricted --tools "" --disable-slash-commands --strict-mcp-config --mcp-config <只含 eplanmcp>` 的 `system/init` 事件中：`tools` 只有 `mcp__eplan__*`，`slash_commands` 和 `skills` 为空，`mcp_servers` 为 `[{"name": "eplan", "status": "connected", "source": "dynamic"}]`；调试日志（`--debug-file`）记录 "project memory is off"，且不读取工作目录的 `.claude/settings.local.json`（不加 `--restricted` 时会读）。`plugins` 中仍有 3 个内置插件（`cc-plugin-agents-md` 等），不带技能。加上 `--json-schema` 后 `tools` 多出内置的 `StructuredOutput`：模型通过它提交结构化答案，统计工具调用时不能算它。上面这组开关加 `--allowedTools mcp__eplan`、`--permission-mode dontAsk`、`--permission-prompts none`、`--max-turns` 都被接受，以 `-` 开头的提示从标准输入传入也不会被当成开关。— 实测，工作目录分别在仓库内外。
- 评测运行器（`ClaudeRunner`，用 `ProcessStartInfo.ArgumentList` 传参，空字符串作为 `--tools` 的值）以同样的参数启动 claude 2.1.299，`system/init` 与上一条相同（`StructuredOutput` 加 eplanmcp 的工具，`skills` 和 `slash_commands` 为空，`permissionMode: dontAsk`），stderr 为空。— 2026-10-19 实测（未登录）。
- 未登录时，stream-json 依次输出 `system/init`、一条 `assistant` 消息（`error: "authentication_failed"`、`is_api_error_message: true`、文字 "Not logged in · Please run /login"）和 `result` 事件，退出码 1。**`result` 的 `subtype` 仍是 `"success"`**，失败要看 `is_error: true` 和 `terminal_reason: "api_error"`；`total_cost_usd` 为 0，`usage` 含 `input_tokens`、`output_tokens`、`cache_read_input_tokens`、`cache_creation_input_tokens`。— 实测。
- **第一次真实运行确认的 stream-json 格式**（2026-10-19，Claude Code 2.1.299，`claude-sonnet-5-5`，已登录 Max 套餐）：
  - 工具调用是 `assistant` 消息 `message.content` 中的 `{"type": "tool_use", "id": "toolu_…", "name": "mcp__eplan__eplan_project_get", "input": {…}}`；工具结果是 `user` 消息中的 `{"type": "tool_result", "tool_use_id": "toolu_…", "content": "<文本>"}`，成功时没有 `is_error` 字段，`content` 是字符串。两者的 `parent_tool_use_id` 都是 null。
  - 结构化答案：模型常先用文字作答，Claude Code 随后插入一条合成的 `user` 消息（`isSynthetic: true`，文字为 "[structured-output-enforce] You MUST call the StructuredOutput tool…"），模型再调用 `StructuredOutput`，其 `input` 就是答案；`result` 事件的 `structured_output` 与之相同。所以 1 次工具调用的题目 `num_turns` 为 4。
  - 还会出现 `rate_limit_event` 和 `system`/`post_turn_summary` 两种事件，与答题无关。
  - 提示从标准输入传入时被当作题目读入（模型回答了"项目一共有 321 页"）。
  - `total_cost_usd` 在套餐登录时也会给出（客户端估算）；一道 1 次调用的题约 0.01–0.05 美元，输入 token 中位数约 2.4 万（几乎全部是缓存读取）。
  - — 实测（`.local/evals/` 中的轨迹，不提交）。

### Claude Code 插件与桌面版配置（spike S17，2026-10-20，Claude Code 2.1.299，隔离的配置目录，未登录）
详细数据见 [spike S17](../../../spikes/S17-client-plugin/README.md)。
- `CLAUDE_CONFIG_DIR` 把 `.claude.json`、`settings.json`、`plugins\` 都放到指定目录，`auth status` 报告这个目录；真实的 `~\.claude*` 不变。— S17。
- 本地目录形式的插件市场：`plugin marketplace add`、`plugin install` 不需要登录，网络不可达时也成功。市场不复制；install 在 `plugins\cache\<市场>\<插件>\<版本>\` 中复制一份插件，但从源目录加载（`plugin list` 的 "Read from"、`${CLAUDE_PLUGIN_ROOT}` 都指向源目录，改写源目录的 `.mcp.json` 后下一个进程就用新内容）。源目录不在时插件 `failed to load … cache-miss`，不会退回到缓存。`marketplace remove` 同时卸载插件，缓存副本留下并标记 `.orphaned_at`。— S17。
- 插件 `.mcp.json` 的 `command`、`args`、`env` 中 `${VAR}` 和 `${VAR:-默认值}` 都会展开，包括 `${USERPROFILE}`、`${CLAUDE_PLUGIN_ROOT}`（Windows 上用正斜杠）和任意环境变量；`%VAR%` 不展开。未设置又没有默认值的变量原样保留，`mcp list` 不给出警告。插件的 stdio 服务器继承命令行进程的环境。— S17。
- 两个插件服务器的命令行相同时，只启动一个（文档：插件按端点判断重复，本地、项目、用户范围的定义优先）。`mcp get` 隐藏插件服务器的命令、参数和环境变量。— S17。
- `marketplace add` 能展开 `~/…`，不能展开 `%USERPROFILE%\…`（报 "Invalid marketplace source format"）。— S17。
- 桌面版（MSIX，`Claude_pzs8sxrjxfjjc`）的 `claude_desktop_config.json` 只在 `%LOCALAPPDATA%\Packages\Claude_pzs8sxrjxfjjc\LocalCache\Roaming\Claude\` 中。包外的进程看不到 `%APPDATA%\Claude\`；包内的进程看到的 `%APPDATA%\Claude\claude_desktop_config.json` 与包目录中的是同一个文件（文件 ID 相同）。桌面版运行时每隔几十秒就会改写它。— S17、A6。

### Streamable HTTP 与 Claude Code 远程客户端（spike S19，2026-10-21，ModelContextProtocol.AspNetCore 2.2.0，Claude Code 2.1.299，只在 127.0.0.1 上）
详细数据见 [spike S19](../../../spikes/S19-vm-http/README.md)。
- **`HttpServerTransportOptions.SessionMode` 默认是 `Stateless`**：没有 `Mcp-Session-Id`，每个请求一个新的服务器对象，`initialize` 中的客户端名称在后续请求中取不到，按会话保存的状态无处可放。`Stateful` 对 2026-07-28 的请求回 -32022；`StatefulForInitializeClients` 只给用 `initialize` 的客户端会话。— SDK 的 XML 文档 + S19 探测。
- HTTP 方式下**根服务容器中没有 `McpServer`**（三种模式都是），单例中 `GetService<McpServer>()` 为 null；stdio 方式下有，2025-11-25 和 2026-07-28 两种客户端的名称都能取到。— S19。
- `ConfigureSessionOptions` 在 Stateful 模式下每个会话调用一次，这时来源地址和响应头中新生成的 `Mcp-Session-Id` 都可用；SDK 不把会话绑定到来源地址；`IdleTimeout` 默认 2 小时，`MaxIdleSessionCount` 默认 10,000；`RunSessionHandler` 是实验性 API；`EnableLegacySse` 默认关闭（`/sse`、`/message` 回 404）。— S19。
- 带上 `ModelContextProtocol.AspNetCore` 后，自包含单文件多 26.5 MB；`Microsoft.Extensions.*` 改用 ASP.NET Core 运行时包中的 ReadyToRun 版本；发布目录多一个 Kestrel 用不到的 `aspnetcorev2_inprocess.dll`。— S19。
- **Claude Code 2.1.299 连接远程 HTTP 服务器时先发 2026-07-28 的 `server/discover`**；服务器回 -32022 时退回 `initialize`（2025-11-25）并带 `Mcp-Session-Id`，再开 `GET /mcp` 的 SSE 流；不发 `Origin`；`User-Agent` 为 `claude-code/2.1.299 (sdk-cli)`；`mcp list`/`mcp get` 每次都新建会话，结束时不发 `DELETE`。— S19 的请求日志。
- `claude mcp add --transport http --scope user <名称> <URL> --header "Authorization: Bearer …"` 在 `.claude.json` 顶层 `mcpServers` 中写 `{"type":"http","url":…,"headers":{…}}`；`mcp get` 原样打印请求头的值。401 时报 "Server rejected the configured Authorization header (HTTP 401)"，配置了 `Authorization` 就不再尝试 OAuth。请求头中的 `${VAR}` 原样保存、连接时展开，变量不存在时在诊断中警告。— S19。

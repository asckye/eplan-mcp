# Spike S4：客户端的 elicitation 支持

| 项 | 内容 |
|---|---|
| 问题 | 目标客户端（Claude Code）实际使用哪个 MCP 协议版本？是否声明 elicitation？Host 用 C# SDK 2.x 发起的审批表单，能否在客户端走完"批准 / 拒绝 / 取消"的往返，`requestState` 能否原样带回？ |
| 日期 | 2026-10-13 |
| 环境 | Windows 11；.NET SDK 10.0.401；ModelContextProtocol 2.2.0；Claude Code 2.1.295（Claude 桌面版自带） |
| 结论 | **协议层可行**。Claude Code 2.1.295 原生使用 2026-07-28，每个请求都声明 `elicitation: {form, url}`。SDK 2.x 的 `InputRequiredException` 一条代码路径同时覆盖 2026-07-28（MRTR）和 2025-11-25（服务器发起 `elicitation/create`），两种协议下 `requestState` 都能带回。原定的人工确认（交互界面中表单的显示效果和三种回答的实际往返）**不再需要**：ADR-0011 决定审批不用 elicitation（见"待办"） |

## 方法
- [EplanMcp.Spikes.S4](EplanMcp.Spikes.S4.csproj) 是一个 stdio MCP 服务器（.NET 10 + 官方 SDK 2.2.0），提供两个工具：
  - `s4_probe`：返回协商的协议版本、客户端信息、客户端声明的能力，以及进程是否带有 MSIX 包身份（顺带为 S5 收集数据）；
  - `s4_approve`：扮演 Host 的审批步骤。第一次调用抛出 `InputRequiredException`，带一个模拟变更清单的表单（`boolean` 或 `enum` 两种形式，可连续要求 1–3 轮）和一个 HMAC 签名、5 分钟有效、绑定工具和参数摘要的 `requestState`（ADR-0005 / SEC-06 的设计）；重试时校验签名并报告收到的回答。**不修改任何项目。**
- 服务器把 stdin/stdout 上的每一条 JSON-RPC 消息原样记录到 `.local/spikes/s4/wire-*.jsonl`（git 忽略），结论都以这些报文为准，而不是以工具的自述为准。
- `selftest` 模式：用 SDK 自带的客户端启动本服务器，在两种协议下用脚本化的回答跑 10 个场景，先证明服务器这一半没有问题。
- 对 Claude Code：用 `claude -p --mcp-config <临时配置> --strict-mcp-config` 让它连接本服务器（只加载这一个服务器，只允许调用两个 spike 工具），读取它发出的报文。

```bash
dotnet build spikes/S4-elicitation -c Release
spikes/S4-elicitation/bin/Release/net10.0/EplanMcp.Spikes.S4.exe selftest
```

## 结果

### selftest（SDK 客户端，脚本化回答）

| 场景 | 协商版本 | 结果 | requestState | 回答处理器调用次数 |
|---|---|---|---|---|
| 批准（勾选框） | 2026-07-28 | 批准 | valid | 1 |
| 批准（选择题） | 2026-07-28 | 批准 | valid | 1 |
| 拒绝 decline | 2026-07-28 | 未批准 | valid | 1 |
| 取消 cancel | 2026-07-28 | 未批准 | valid | 1 |
| 连续两轮批准 | 2026-07-28 | 批准 | valid | 2 |
| 客户端不声明 elicitation | 2026-07-28 | 回退（不发表单） | — | 0 |
| 批准 | 2025-11-25 | 批准 | valid | 1 |
| 拒绝 | 2025-11-25 | 未批准 | valid | 1 |
| 连续两轮批准 | 2025-11-25 | 批准 | valid | 2 |
| 客户端不声明 elicitation | 2025-11-25 | 回退 | — | 0 |

报文核对：
- **2026-07-28**：客户端先发 `server/discover`；每个请求的 `params._meta` 都带 `io.modelcontextprotocol/protocolVersion`、`clientInfo`、`clientCapabilities`。`tools/call` 的结果为 `resultType: "input_required"`，含 `inputRequests`（键为服务器指定的 `approval1`）和 `requestState`；客户端用**新的 id** 重发 `tools/call`，`params` 中带 `inputResponses` 和原样的 `requestState`，最终结果为 `resultType: "complete"`。
- **2025-11-25**：`initialize` 握手；`tools/call` 进行中，服务器向客户端发出 `elicitation/create` 请求，客户端回答后才返回工具结果。SDK 在内部把回答作为 `InputResponses`、把我们签发的 `RequestState` 一起交给**重新调用的同一个工具处理器**，所以工具代码不需要区分协议版本。

### Claude Code 2.1.295
连接时发出的消息（`wire-*.jsonl` 摘录）：

```json
{"id":"server-discover-probe-1","method":"server/discover","params":{"_meta":{
  "io.modelcontextprotocol/protocolVersion":"2026-07-28",
  "io.modelcontextprotocol/clientInfo":{"name":"claude-code","title":"Claude Code","version":"2.1.295"},
  "io.modelcontextprotocol/clientCapabilities":{"roots":{"listChanged":true},"elicitation":{"form":{},"url":{}}}}}}
```

随后是 `subscriptions/listen`（订阅 `toolsListChanged`）、`server/discover`、`tools/list`，全部是 2026-07-28 的形式。无界面模式下这个可执行文件没有登录态（桌面版通过自己的通道传递认证），所以在 `tools/list` 之后退出，没有发出 `tools/call`。

### 附带发现
- SDK 生成的 `inputSchema` **不含 `additionalProperties: false`**，而[工具设计规范](../../docs/standards/mcp-tool-design.md)第 5 节要求必须有。Host 需要在注册工具时补上（并由契约快照测试守住）。
- SDK 生成的工具注解会把 `title` 同时写进 `annotations.title`，四个提示（`readOnlyHint` 等）在特性中显式设置后全部出现在 `tools/list` 中。
- 桌面版自带的 Claude Code 文件同时出现在 `%APPDATA%\Claude\claude-code\` 和 MSIX 包的 `LocalCache\Roaming\Claude\claude-code\` 下，与 api-notes A6 的文件重定向现象一致。

## 结论
1. **审批渠道 1（MCP elicitation）对 Claude Code 可用**：它使用 2026-07-28，并声明 form 模式。Host 统一用 `InputRequiredException` 发起审批，不调用 `ElicitAsync`（后者在无状态模式下会抛异常，而且只覆盖旧协议）。
2. **审批前必须检查客户端能力**：`server.IsMrtrSupported` 为 true **且** `ClientCapabilities.Elicitation` 不为 null，才发表单；否则进入下一个审批渠道（EPLAN 内对话框）。规范禁止向未声明的客户端发送 `elicitation/create`。
3. `requestState` 在两种协议下都由客户端或 SDK 原样带回，ADR-0005 的设计（HMAC + 有效期 + 绑定工具和参数）可以直接实现。签名密钥只需存在于 Host 进程内：stdio 下 Host 的生命周期就是一个客户端连接。
4. 多轮审批（连续两次 `input_required`）在两种协议下都可行，必要时可以分步确认。
5. 2026-07-28 下每一轮都是独立的请求，工程师思考多久都不会占住一个挂起的 `tools/call`；旧协议下则会一直挂起到工程师回答为止（受客户端的工具超时限制）。

## 待办（需要人工）
**不再需要**（2026-10-21）：[ADR-0011](../../docs/adr/0011-approval-out-of-band.md) 决定审批和解除写入暂停都不用 elicitation，只经工程师独立操作的窗口（EPLAN 内的对话框，M5 起还有 Workbench）。原因正是第二项要查的问题：表单由 AI 客户端自己显示、自己回答，客户端可以无界面运行或自动回答。上面"结果"和"结论"中的协议层发现仍然有效，以后要在客户端里向工程师请求输入（不是审批）时可以直接使用。原来的两项保留如下：
- 在**已登录的交互式** Claude Code 会话（CLI 或桌面版）中加载本服务器，调用 `s4_approve`（`form` 取 `boolean` 和 `enum` 各一次，再试一次 `rounds: 2`），分别选择批准、拒绝、按 Esc 取消，然后把 `wire-*.jsonl` 和界面截图的观察补进本节：表单是否显示完整的多行变更清单、中文是否正常、`title` / `description` 显示在哪里、默认值是否预填、超时多久。
- 无界面模式（`claude -p`、Agent SDK）遇到 `input_required` 时如何处理（自动拒绝、取消还是报错），决定 Host 在 CI 和代理场景下的行为。

## 局限
- 只测了 Claude Code 2.1.295。Claude 桌面版的聊天界面（非 Code）和其他客户端未测。
- 表单内容用了中文和多行文本，渲染效果要在上面的人工步骤中确认。

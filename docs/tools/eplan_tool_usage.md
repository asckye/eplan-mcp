# 工具规格：`eplan_tool_usage`

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_tool_usage` |
| 所属分组 / Profile | `meta` / `core`（在 `core` 中，`tool` 只能取 `core` 中的工具） |
| 安全级别 | Read |
| MCP 注解 | `readOnlyHint` = true　`destructiveHint` = false　`idempotentHint` = true　`openWorldHint` = false |
| 状态 | Proposed（M2） |

## 1. 这个工具解决什么任务
代理在某个工具的描述不够用时调用它：不清楚参数怎么填、前后该调用哪个工具、结果字段是什么意思、某个错误该怎么处理。它返回该工具的详细用法。这样每个工具的描述可以保持在约 250 token 的预算内（[工具设计规范 4.2](../standards/mcp-tool-design.md#42-规则)），详细内容按需获取，不占用每次 `tools/list` 的上下文（[第 10 节](../standards/mcp-tool-design.md#10-profile-与发现)）。

名称由工具设计规范第 10 节规定；资源词 `tool` 和动作 `usage` 只用于这个元工具，已加入第 3.2、3.3 节的词表。

## 2. 输入参数
| 参数 | 类型 | 必填 | 默认值 | 说明（写给模型看的） |
|---|---|---|---|---|
| `tool` | string，`enum` 为全部已注册的工具名 | 是 | — | 要说明的工具 |
| `topic` | string，`enum`：`examples`、`workflow`、`fields`、`errors` | 否 | 省略，即全部四节 | 只返回这一节 |

- `tool` 的 `enum` 在配置阶段从当前 Profile 和开关筛选后的注册集合生成，调用时也按该集合校验；`topic` 的四个通用值用 `[AllowedValues]` 声明（`action_run` 另支持白名单条目名）。schema 设置 `additionalProperties: false`。
- SDK 不按 schema 校验参数（[api-notes C](../eplan/api-notes.md#c-mcp-客户端与-c-sdkspike-s42026-10-13)），工具自己校验取值，必须完全一致（区分大小写）。`topic` 传 `null` 与省略相同。

## 3. 输出
```json
{ "tool": "eplan_project_list", "topic": "errors", "text": "## Errors\n\n- INVALID_ARGUMENT: …" }
```
- 省略 `topic` 时结果中的 `topic` 为 null，`text` 按 Examples、Workflow、Fields、Errors 的顺序包含全部四节，节之间空一行。
- `text` 是英文 Markdown，每节以 `## ` 标题开头；内容由本项目编写，**不含项目数据**（schema 描述中写明）。
- 一行摘要：`Usage of eplan_project_list: errors.` 或 `Usage of eplan_project_list: examples, workflow, fields, errors.`
- 大小：每个工具的说明不超过 6000 个字符（约 1.5k token，测试保证）；结果中文字出现两次（`structuredContent` 和 JSON 文本块），仍在约 5k token 的默认预算内。不分页，不提供 `response_format`。

## 4. 错误
| 错误码 | 什么时候发生 | 返回给模型的提示 |
|---|---|---|
| `INVALID_ARGUMENT` | `tool` 不是已注册的工具，`topic` 不是四个值之一，或传入了未知参数 | `metadata.violations` 一次列出全部问题和可选值，例如 "'tool' must be one of: eplan_project_list, eplan_session_status, eplan_tool_usage." |

- 不访问 EPLAN，没有 `EPLAN_*` 等连接类错误；EPLAN 没有运行时也能正常返回。
- 缺少 `tool` 时返回 `INVALID_ARGUMENT`（"Missing required argument 'tool'."）。SDK 本身只会返回一句没有 reason 的通用错误（api-notes C），所以由 Host 的参数过滤器在调用前检查必填参数，与未知参数一起报告。

## 5. 实现要点
- 内容放在 `src/EplanMcp.Host/Tools/Usage/<工具名>.md`，作为嵌入资源编译进 `eplanmcp.exe`，运行时不读磁盘；第一次调用时加载并解析全部文件，之后只读。不调用 Bridge，耗时可以忽略。
- 文件格式：恰好四个 `## ` 节，依次为 Examples、Workflow、Fields、Errors（与 `topic` 的值不区分大小写地对应），第一个标题之前没有文字，每节都有内容；节内可以用 `###`。
- 每节写什么：Examples 是完整的参数对象；Workflow 是与其他工具的典型调用顺序；Fields 是结果字段的含义，并指出哪些是项目数据；Errors 只列这个工具实际会返回的 reason（对照代码和[错误码表](../standards/mcp-tool-design.md#63-错误码表)）以及每种情况下该做什么。
- 说明文字是工具契约的一部分，修改时和描述一样评审。
- **新增工具时**：同一个 PR 中添加工具规格和说明文件，并审查、接受契约快照测试生成的 `.received.json`；无需维护另一份工具名列表或数量。
- 描述预算：`tool` 的 `enum` 每多一个工具约增加 20–25 个字符。目前描述加 schema 约 700 个字符；M2 的 13 个工具都加入后约 950 个字符，接近 `Descriptions_StayWithinTheBudget` 的 1000 字符上限，届时可能需要缩短描述。

## 6. 写操作专项
不适用。

## 7. 测试
- Small：全部四节、单节、未知的 `tool` 和 `topic` 一次报告、`topic` 必须完全一致、文件结构检查（缺节、多节、顺序错、空节、标题前有文字、CRLF）、说明大小的预算。契约测试：每个已注册的工具都有完整的说明且没有多余的说明文件；schema 的两个 `enum` 与已注册的工具和四个 `topic` 一致。
- Medium：stdio 端到端调用一次（不启动 Bridge），以及未知工具返回 `INVALID_ARGUMENT`。
- Large：不需要。
- Agent 评测：不设专门任务。评测时统计模型查询了哪些工具的哪一节：某个工具的说明经常被查询，说明它的描述不够，应该改描述。

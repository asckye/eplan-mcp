# ADR-0006：工具面：面向任务、资源命名、显式注解、结构化输出、Profile

| 项 | 内容 |
|---|---|
| 状态 | Accepted |
| 日期 | 2026-10-12（提出）· 2026-10-13（接受） |
| 决策人 | 项目负责人 |
| 门的类型 | 单向门：工具契约一旦发布就会被依赖（Hyrum's Law） |

## 背景与问题
EPLAN 有上千个 action，API 类型数以千计。把它们一对一包装成工具（covagashi/eplan-rag-mcp 有 217 个工具）会带来几个问题：
- 模型难以选对工具；
- 工具定义本身就要消耗大量 token；
- 完成一个任务要串联很多次调用。

Anthropic 的工程实践指出：整合工作流的少量工具优于一对一包装 API；返回人能读懂的标识；提供 `response_format`；分页加截断；错误信息要可操作；通过评测来迭代工具。对于工具数量多的服务器，延迟加载和工具搜索能显著降低 token 消耗、提高准确率。

## 决策
1. **面向任务设计**：一个工具对应工程师的一个意图，例如"在某页放置一个设备并赋予标识"，而不是对应一个 API 调用。只有在评测证明需要时，才拆成更细的工具。
2. **命名**：`eplan_<资源>_<动作>`，全部小写加下划线。资源用单数（`page`、`device`、`connection`……）；动作优先使用标准动词 `list` / `get` / `create` / `update` / `delete`（借鉴 Google AIP-131 到 135），确有需要才使用领域动词（`place`、`connect`、`export`、`check`、`renumber`），参见 AIP-136。
3. **注解必须显式写全**：`readOnlyHint`、`destructiveHint`、`idempotentHint`、`openWorldHint`，外加 `title`。规范中 `destructiveHint` 和 `openWorldHint` 的默认值都是 true，不写全会让只读工具被客户端当成破坏性工具。本项目所有工具的 `openWorldHint` 都是 false。
4. **结构化输出**：每个工具声明 `outputSchema`，返回 `structuredContent`，同时附上同内容的 JSON 文本（兼容旧客户端）和一行人能读懂的摘要。
5. **控制输出大小**：列表类工具一律分页（`limit` + `cursor`，返回 `total` 和 `truncated`），并支持 `response_format: concise | detailed`。默认响应不超过约 5k token，硬上限 20k。
6. **Profile**：
   - `core`（默认，目标 25 个以内）：覆盖最常见的任务；
   - `full`：全部 Read / Write / Destructive 工具；
   - Code 级工具不属于任何默认 Profile。
   
   `tools/list` 的返回顺序固定（有利于客户端缓存提示词）。
7. **工具契约就是 API**：名称、描述、schema、注解都纳入快照测试；版本和废弃规则见 [开发流程第 8 节](../process/development-workflow.md#8-版本与发布)。
8. **评测驱动**：每个工具至少对应一个 Agent 评测任务；修改工具描述后要重跑评测。

详细规则见 [mcp-tool-design.md](../standards/mcp-tool-design.md)。

## 后果
- **好的**：模型选工具更准，token 更省，单个任务需要的调用次数更少；契约稳定，容易测试。
- **代价**：每个任务型工具内部要组合多个 EPLAN 操作，实现更复杂；覆盖面增长得比"全包装"方式慢。
- **兜底**：在需要 EPLAN 的长尾功能时，提供受控的 `eplan_action_run`（只允许执行白名单中、已标定级别的 action）作为通用逃生口。它不是任意代码执行。

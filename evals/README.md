# Agent 评测

评测运行器（`EplanMcp.Evals`）用真实任务检验模型会不会用 eplanmcp 的工具：用 `claude -p` 让模型作答，再按程序规则判分。设计见 [Agent 评测 v0](../docs/design/2026-10-19-agent-evals-v0.md)，规则见[测试规范第 5 节](../docs/standards/testing.md#5-agent-评测)。

目前完成了任务文件的读取、校验和判分（设计第 8 节实施顺序的第 2 步）；`run` 命令、夹具和任务集在后续步骤中加入。

## 校验任务文件
```powershell
dotnet run --project evals\EplanMcp.Evals -- check                  # 校验 evals\tasks\*.yaml
dotnet run --project evals\EplanMcp.Evals -- check --tasks <目录>
```
不需要 EPLAN 和模型。每个文件的问题一次全部列出，带行号；有问题时退出码为 1。扩展名写成 `.yml` 的文件不会被读取，也按问题报告。任务集加入之前（第 5 步）`evals/tasks` 还不存在，`check` 会报告找不到任务目录。

## 任务文件
每个任务一个文件 `evals/tasks/<id>.yaml`：

| 键 | 必填 | 内容 |
|---|---|---|
| `id` | 是 | 与文件名（不含 `.yaml`）相同 |
| `set` | 是 | `dev`（开发集）或 `holdout`（保留集） |
| `prompt` | 是 | 原样交给模型的中文题目，不写"请调用某某工具"之类的引导 |
| `answer` | 是 | 模型答案的 JSON Schema，`type` 必须是 `object` |
| `oracle` | 否 | 标准调用：`名称: { tool, args, pick }`。运行器自己调用工具 `tool`（参数 `args`，可省略），用路径 `pick` 从结构化结果中取出期望值。每个标准调用都必须被某个检查用到 |
| `checks` | 是 | 检查列表，每条是 `answer: <路径>` 加一种比较方式；全部通过才算成功。标准调用取到的值不适合比较方式时（例如 `set_equals` 的期望值不是数组），任务记为无效，而不是失败 |
| `budget` | 否 | `max_tool_calls`（默认 20，最多 200）、`max_minutes`（默认 5，最多 60）、`max_usd`（默认 0.50，最多 5） |

**路径**只支持 `$`、`.字段`、`[n]`、`[*]`。不含 `[*]` 的路径取一个值，取不到时检查失败；含 `[*]` 的路径取所有匹配组成的数组（可能为空）。检查的路径以字段开头时，这个字段必须出现在 `answer` 的 `properties` 中。

**比较方式**（期望值写 `oracle.<名称>` 引用标准调用，或者直接写字面量）：

| 比较 | 通过条件 |
|---|---|
| `equals` | 相等：数字按数值（`12` 等于 `12.0`），字符串区分大小写，数组和对象逐项比较 |
| `equals_ignore_case` | 两个字符串忽略大小写后相等 |
| `set_equals` | 两个数组的元素相同，不计顺序、忽略重复 |
| `contains` | 数组包含期望的元素（期望值是非空数组时，包含其中每个元素）；或字符串包含期望的子串 |
| `at_least` / `at_most` | 数字不小于 / 不大于期望值 |

**YAML 的值**：加引号的一律是字符串；不加引号的 `null`、`~`、空值是 null（但检查的期望值不能留空，要比较 null 时写 `null`），`true`、`false` 是布尔值，符合 JSON 数字写法的是数字（`007`、`1.` 仍是字符串）。不支持 `!!str` 之类的标签，需要字符串时加引号即可。期望值要写以 `oracle.` 开头的字面字符串时，加引号。

示例：

```yaml
id: device-pages
set: dev
prompt: |
  =GAA+A1-FC1 这个设备的触点分布在哪几页？
answer:
  type: object
  required: [pages]
  properties:
    pages: { type: array, items: { type: string } }
oracle:
  pages:
    tool: eplan_device_get
    args: { tag: "=GAA+A1-FC1" }
    pick: $.functions[*].page
checks:
  - answer: $.pages
    set_equals: oracle.pages
budget: { max_tool_calls: 12, max_minutes: 4 }
```

工具名目前只按命名规则（`eplan_<家族>_<动作>`）检查；是否是已注册的工具，等运行器连接 eplanmcp 后（第 4 步）按服务器的工具列表检查。

# Agent 评测

评测运行器（`EplanMcp.Evals`）用真实任务检验模型会不会用 eplanmcp 的工具：用 `claude -p` 让模型作答，再按程序规则判分。设计见 [Agent 评测 v0](../docs/design/2026-10-19-agent-evals-v0.md)，规则见[测试规范第 5 节](../docs/standards/testing.md#5-agent-评测)。

运行器已经完成（设计第 8 节实施顺序的第 2 至 4 步）；任务集在第 5 步加入。

## 校验任务文件
```powershell
dotnet run --project evals\EplanMcp.Evals -- check                  # 校验 evals\tasks\*.yaml
dotnet run --project evals\EplanMcp.Evals -- check --tasks <目录>
```
不需要 EPLAN 和模型。每个文件的问题一次全部列出，带行号；有问题时退出码为 1。扩展名写成 `.yml` 的文件不会被读取，也按问题报告。任务集加入之前（第 5 步）`evals/tasks` 还不存在，`check` 会报告找不到任务目录。

## 运行评测
**准备**（每台机器一次，夹具每次换项目时重做）：
1. 在命令行登录 Claude Code：`claude auth login`（或 `claude setup-token`）。桌面版自带的 `claude.exe` 单独运行时没有登录态。
2. 构建 Release 版的插件和 eplanmcp，并在要用的 EPLAN 变体中注册插件（`CLAUDE.md` 常用命令；每个变体各注册一次，见 [api-notes A12](../docs/eplan/api-notes.md)）。
3. 把 EPLAN 自带的 `EPLAN_Sample_Project`（`.elk` 文件和 `.edb` 目录）复制到 `%USERPROFILE%\EplanMcp\scratch\fixtures\`，在 EPLAN 中打开这个副本，并让它成为当前项目。评测只在 scratch 根目录下的项目上运行，预检会拒绝其他项目。

```powershell
dotnet run --project evals\EplanMcp.Evals -- run                       # 开发集，每题一次
dotnet run --project evals\EplanMcp.Evals -- run --set all --repeat 3  # 开发集和保留集，每题三次
dotnet run --project evals\EplanMcp.Evals -- run --task page-count     # 只跑一题（开发集或保留集中都可以）
```
其他选项：`--tasks <目录>`、`--model <模型>`（默认 `claude-sonnet-5-5`）、`--claude <claude.exe 的路径>`（默认先找 PATH，再找 Claude 桌面版自带的最新版本）、`--eplanmcp <eplanmcp.exe 的路径>`（默认用仓库中的 Release 构建，没有时用 Debug 构建）。

运行时先显示任务数和费用上限（各任务 `max_usd` 之和乘以次数），然后预检：claude 已登录、EPLAN 通过 eplanmcp 连接、当前项目在 scratch 根目录下。任何一项不满足就停止，不产生费用。每题先执行标准调用，再让模型作答、判分，并逐行显示结果。按 Ctrl+C 会停止当前作答，已完成的部分照样写进报告。

结果在 `.local/evals/<yyyyMMdd-HHmmss>/`（不进仓库）：
- `report.md`：中文摘要，按开发集和保留集给出成功率，以及调用次数、token、耗时的中位数，列出失败、无效的任务和提示；
- `report.json`：全部数据；
- `<任务>/transcript.jsonl` 和 `stderr.txt`：每次作答的原始轨迹。**失败的轨迹是改进工具描述的第一手材料。**

成功率只计"成功"和"失败"。"无效"不计入：环境出错（未登录、API 错误、会话没有隔离），或者任务本身有问题（标准调用失败、标准调用取到的值不适合比较方式）。

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

**路径**只支持 `$`、`.字段`、`[n]`、`[*]`。不含 `[*]` 的路径取一个值，取不到时检查失败；含 `[*]` 的路径取所有匹配组成的数组（可能为空）。列表工具的结果分页时，标准调用会跟着 `nextCursor` 取完所有页：含 `[*]` 的 `pick` 收集每一页的匹配，不含 `[*]` 的 `pick` 只读第一页（`total` 等字段在第一页），所以不要用 `[n]` 取后面页上的条目。检查的路径以字段开头时，这个字段必须出现在 `answer` 的 `properties` 中。

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

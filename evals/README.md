# Agent 评测

评测运行器（`EplanMcp.Evals`）用真实任务检验模型会不会用 eplanmcp 的工具：用 `claude -p` 让模型作答，再按程序规则判分。设计见 [Agent 评测 v0](../docs/design/2026-10-19-agent-evals-v0.md)，规则见[测试规范第 5 节](../docs/standards/testing.md#5-agent-评测)。

运行器和任务集 v0 已经完成（设计第 8 节实施顺序的第 2 至 5 步）；第一次真实运行（第 6 步）需要先登录 Claude Code。

## 校验任务文件
```powershell
dotnet run --project evals\EplanMcp.Evals -- check                  # 校验 evals\tasks\*.yaml
dotnet run --project evals\EplanMcp.Evals -- check --tasks <目录>
```
不需要 EPLAN 和模型。每个文件的问题一次全部列出，带行号；有问题时退出码为 1。扩展名写成 `.yml` 的文件不会被读取，也按问题报告。CI 中的 `TaskSetTests` 也检查仓库里的任务文件，并核对标准调用用到的工具都是 eplanmcp 提供的。

## 运行评测
**准备**（每台机器一次，夹具每次换项目时重做）：
1. 在命令行登录 Claude Code：`claude auth login`（或 `claude setup-token`），用你的 Claude 套餐账号授权一次即可。桌面版自带的 `claude.exe` 不在 PATH 上，在自己打开的终端里要用完整路径：`& (Get-ChildItem "$env:LOCALAPPDATA\Packages\Claude_*\LocalCache\Roaming\Claude\claude-code" -Recurse -Filter claude.exe | Select-Object -Last 1).FullName auth login`。
2. 构建 Release 版的插件和 eplanmcp，并在要用的 EPLAN 变体中注册插件（`CLAUDE.md` 常用命令；每个变体各注册一次，见 [api-notes A12](../docs/eplan/api-notes.md)）。
3. 准备夹具：`eng\scripts\eval-fixture.ps1 -Open` 把 EPLAN 自带的 `EPLAN_Sample_Project`（`.elk` 文件和 `.edb` 目录）复制到 `%USERPROFILE%\EplanMcp\scratch\fixtures\`，并用这个副本作为当前项目启动 EPLAN（默认 Electric P8，`-Variant` 可改）。已有副本时不覆盖，`-Force` 才覆盖（先关闭 EPLAN）。评测只在 scratch 根目录下的项目上运行，预检会拒绝其他项目。

```powershell
dotnet run --project evals\EplanMcp.Evals -- run                       # 开发集，每题一次
dotnet run --project evals\EplanMcp.Evals -- run --set all --repeat 3  # 开发集和保留集，每题三次
dotnet run --project evals\EplanMcp.Evals -- run --task page-count     # 只跑一题（开发集或保留集中都可以）
```
其他选项：`--tasks <目录>`、`--model <模型>`（默认 `claude-sonnet-5-5`）、`--claude <claude.exe 的路径>`（默认先找 PATH，再找 Claude 桌面版自带的最新版本）、`--eplanmcp <eplanmcp.exe 的路径>`（默认用仓库中的 Release 构建，没有时用 Debug 构建）、`--template <.elk 的路径>`（写入题复制的项目，见下文）。

运行时先显示任务数和费用上限（各任务 `max_usd` 之和乘以次数），然后预检：claude 已登录、EPLAN 通过 eplanmcp 连接、当前项目在 scratch 根目录下。任何一项不满足就停止，不产生费用。每题先执行标准调用，再让模型作答、判分，并逐行显示结果。按 Ctrl+C 会停止当前作答，已完成的部分照样写进报告。

**模型用的 eplanmcp 有自己的数据根目录** `.local/evals/<运行>/eplanmcp/`（`EPLANMCP_HOME`，经 `mcp.json` 中的 `env` 传入）。运行器在其中写一份 `config.json`：唯一的 scratch 根目录是本次运行的可写副本目录 `<scratch 根目录>\evals\<运行>\copies\`，其他项目的写入一律拒绝（`POLICY_DENIED`），不看工程师自己的 `config.json`。所以：
- 共享夹具 `scratch\fixtures\` 对模型只读，模型在读取题中尝试写入也改不了它；
- 不会弹出没有人点的审批对话框（M3 设计 4.3）；
- 模型的每次写入都记在这里的审计日志中。作答中有写入提交（工具结果为 `committed`）而审计日志没有新增记录，说明 eplanmcp 没有用这个数据根目录，写入保护没有生效：这次作答记为无效（"not isolated"），这时要检查夹具有没有被改动。

### 写入题：每次作答一份新副本
`fixture: copy` 的题目要修改项目（M3 设计第 7 节和 Q4）。运行器为这类题的**每次作答**复制一份 EPLAN 自带的 `EPLAN_Sample_Project`（在 `C:\Users\Public\EPLAN\Data` 下找，或用 `--template <.elk>` 指定），放在 `<scratch 根目录>\evals\<运行>\copies\<nn>\Eval_<运行>_<nn>`，每份的项目名都不同。题目中的 `{project}` 换成这个名字，标准调用和事后检查也都对这份副本进行（传入 `project`）。`fixture: copy_outside_scratch` 的副本放在 `outside\` 下，不在模型的 scratch 根目录中，用来考察写入被策略拒绝后模型怎么回答。

**副本由工程师在 EPLAN 中打开**：运行器复制好全部副本后列出它们的 `.elk` 路径，然后等待（最多 20 分钟，Ctrl+C 停止），每 2 秒用 `eplan_project_list` 查看一次。在 EPLAN 中逐个打开（文件 > 打开），全部打开后在页导航器中点回夹具 `EPLAN_Sample_Project`，让它重新成为当前项目：读取题读的是当前项目，所以副本是当前项目时运行器会继续等待。到时仍未打开的副本，对应的作答记为无效。运行器不自己打开副本：从外部打开项目只有 Remote Client 的 `ProjectOpen`（spike S9–S11 的驱动程序用过），它走未加密的局域网端口，架构上不采用（ADR-0002）；插件中也还没有打开项目的操作。

运行结束后副本仍开在 EPLAN 中：在 EPLAN 中关闭它们，再删除 `<scratch 根目录>\evals\<运行>\`（每份约 200 MB）。

结果在 `.local/evals/<yyyyMMdd-HHmmss>/`（不进仓库）：
- `report.md`：中文摘要，按开发集和保留集给出成功率，以及调用次数、token、耗时的中位数，列出失败、无效的任务和提示；
- `report.json`：全部数据；
- `<任务>/transcript.jsonl` 和 `stderr.txt`：每次作答的原始轨迹。**失败的轨迹是改进工具描述的第一手材料。**

成功率只计"成功"和"失败"。"无效"不计入：环境出错（未登录、API 错误、会话没有隔离、写入题的副本没有打开），或者任务本身有问题（标准调用失败、标准调用取到的值不适合比较方式）。

因 API 错误无效、而模型还没做任何事（没有工具调用、没有花费）的作答，运行器等一分钟后**重试一次**。例如，另一个 Claude Code 进程同时刷新共享的登录时，会出现 "Failed to refresh OAuth token"。未登录不重试。两次都写进报告，重试标为"（重试）"；第一次照样是无效，不计入成功率，所以费用上限不变。

## 任务集 v0
`evals/tasks/` 中 24 题：开发集 18 题，保留集 6 题（文件名以 `holdout-` 开头）。题目都针对夹具 `EPLAN_Sample_Project`，覆盖：
- 项目概况：页数、设备数、连接数；
- 页面：某个结构下的页、某种类型的页数、页上的设备；
- 设备：所在页、用了哪些部件、没有部件的电机；
- 连接：断路器的输出端接到哪些设备；
- 部件：某个制造商的部件有多少种；
- 检查结果、英文页描述（`language` 参数）；
- 宏：按名称在宏目录中找宏、宏的尺寸；
- 有歧义的短标识（`-FC1` 出现在多个结构下，答案要列出它们，而不是只挑一个）；
- 超出能力的请求（改名、删页）：只有只读工具，答案中的 `done` 必须是 `false`。

改进工具描述时只看开发集的结果；保留集只在验收时运行，不据此调整描述（[测试规范第 5 节](../docs/standards/testing.md#5-agent-评测)）。每道题的标准调用都在夹具上核对过一次：值与另行查到的数据一致。新增题目时也要先这样核对。少数题目用字面量作期望值，例如连接的对端设备，路径子集无法表达"另一端"；字面量来自同一次核对。

## 任务文件
每个任务一个文件 `evals/tasks/<id>.yaml`：

| 键 | 必填 | 内容 |
|---|---|---|
| `id` | 是 | 与文件名（不含 `.yaml`）相同 |
| `set` | 是 | `dev`（开发集）或 `holdout`（保留集） |
| `fixture` | 否 | `shared`（默认）：EPLAN 的当前项目，即共享夹具，模型不能写入；`copy`：每次作答一份新副本，模型可以写入；`copy_outside_scratch`：新副本，但写入一律被拒绝（见"写入题"） |
| `prompt` | 是 | 原样交给模型的中文题目，不写"请调用某某工具"之类的引导。题目在副本上进行时，用 `{project}` 指代副本，其他题目不能出现 `{project}` |
| `answer` | 是 | 模型答案的 JSON Schema，`type` 必须是 `object` |
| `oracle` | 否 | 标准调用：`名称: { tool, args, pick }`。运行器在作答之前自己调用工具 `tool`（参数 `args`，可省略），用路径 `pick` 从结构化结果中取出期望值。每个标准调用都必须被某个检查用到 |
| `after` | 否 | 事后调用，写法同 `oracle`：运行器在模型作答之后调用，看模型改了什么。工具返回错误时，从错误本身 `{"error": {...}}` 中取值，例如 `pick: $.error.reason` 取到 `DEVICE_NOT_FOUND`。只读第一页。取不到值时检查失败（不是无效）。每个事后调用都必须被某个检查用到 |
| `checks` | 是 | 检查列表，每条是 `answer: <路径>`（答案中的值）或 `after: <名称>`（事后调用取到的值），加一种比较方式；全部通过才算成功。标准调用取到的值不适合比较方式时（例如 `set_equals` 的期望值不是数组），任务记为无效，而不是失败 |
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

**`{ }` 中的逗号**：在 `{ }` 写法中，未加引号的字符串里的 `, ` 会开始一个新的键。例如 `{ description: the tag, such as -K1 }` 实际有两个键，第二个是没有值的 `such as -K1`。claude 会拒绝这样的答案 schema，每次作答都成为无效。所以 `check` 把答案 schema 中没有值的键报告为问题；含 `, ` 的字符串要加引号。明确写出的 `null`、`~` 不算。

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

写入题的示例：答案只说做没做，结果由事后调用核对；标准调用记下改名前的部件，改名后应该不变。

```yaml
id: device-rename
set: dev
fixture: copy
prompt: 在项目 {project} 中，把设备 =F+A2-KF1 改名为 =F+A2-KF19。完成后告诉我结果。
answer: { type: object, required: [done], properties: { done: { type: boolean } } }
oracle: { parts: { tool: eplan_device_get, args: { tag: "=F+A2-KF1" }, pick: "$.parts[*].partNumber" } }
after:
  tags: { tool: eplan_device_get, args: { tag: "=F+A2-KF19" }, pick: "$.functions[*].tag" }
  parts: { tool: eplan_device_get, args: { tag: "=F+A2-KF19" }, pick: "$.parts[*].partNumber" }
  old: { tool: eplan_device_get, args: { tag: "=F+A2-KF1" }, pick: $.error.reason }
checks:
  - { answer: $.done, equals: true }
  - { after: tags, set_equals: ["=F+A2-KF19"] }
  - { after: parts, set_equals: oracle.parts }
  - { after: old, equals: DEVICE_NOT_FOUND }
```

工具名目前只按命名规则（`eplan_<家族>_<动作>`）检查；是否是已注册的工具，等运行器连接 eplanmcp 后（第 4 步）按服务器的工具列表检查。

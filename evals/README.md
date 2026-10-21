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

**份数**：所选任务中 `fixture` 为 `copy` 或 `copy_outside_scratch` 的题数，乘以 `--repeat`；每份约 200 MB。目前开发集有 8 道这样的题，保留集有 1 道：`run` 要打开 8 份，`run --set all --repeat 3` 要打开 27 份，`run --task page-create` 只要 1 份；份数多时可以用 `--set`、`--task` 分几次运行。所选任务都在共享夹具上时，运行器不复制，也不需要打开什么。

**副本由工程师在 EPLAN 中手工打开**（[M3 设计 Q4](../docs/design/2026-10-19-m3-safe-writes.md#9-待定问题与决定)，2026-10-20 项目负责人决定）。运行器复制好全部副本后列出它们的 `.elk` 路径，然后等待，最多 20 分钟（Ctrl+C 停止）：每 2 秒用 `eplan_project_list` 查看一次，情况有变化时显示已打开几份。这期间工程师：
1. 在 EPLAN 中逐个打开列出的 `Eval_<运行>_<nn>.elk`（文件 > 打开）。打开的副本可能成为当前项目。
2. 全部打开后，在页导航器中点一下夹具 `EPLAN_Sample_Project`，让它重新成为当前项目。读取题读的是当前项目，所以运行器要等到预检时的当前项目（夹具）重新是当前项目才开始作答；在此之前它提示 "select EPLAN_Sample_Project.elk in the page navigator again"。
3. 运行结束之前不要关闭副本：标准调用、作答和事后调用都要用到它们。

到时夹具仍不是当前项目，运行停止，不产生费用；到时仍未打开的副本，对应的作答记为无效（"it was not opened in EPLAN within 20 minutes"），其他作答照常进行。

运行器不自己打开副本，也不重启 EPLAN。考虑过、暂不采用的做法：
- Remote Client 辅助程序：从外部打开项目只有 Remote Client 的 `ProjectOpen`（spike S9–S11 的驱动程序用过），它走未加密的局域网端口，ADR-0002 不采用 Remote Client；
- 插件新增 `project.open` 操作和对应的工具：改变产品和 IPC，需要单独设计。

**清理**：运行结束后（Ctrl+C 停止时也一样），运行器提示副本仍开在 EPLAN 中。在 EPLAN 中关闭它们，再删除 `<scratch 根目录>\evals\<运行>\`；夹具 `scratch\fixtures\` 不要删。`.local/evals/<运行>/` 中的报告、轨迹和模型那一侧 eplanmcp 的数据（`eplanmcp\`，含审计日志和导出的文件）不会自动删除，不再需要时自行删除。

### 需要工程师在场的题：`evals/tasks-attended/`
Destructive 工具（删页、页面重编号、设备编号，以及 M4c 的端子编号、电缆编号和线号）即使在 scratch 中也要审批（ADR-0005、ADR-0013），而审批只能由工程师在 EPLAN 中点击（ADR-0011）。所以这些题放在单独的目录 `evals/tasks-attended/`，默认的 `run` 不会选到它们；无人值守时它们只会等到审批过期。项目负责人决定：这批评测只在工程师在场时运行（M4 第二批），其余时候由 Large 测试（测试中注册的审批渠道）覆盖。

运行：`run --tasks evals/tasks-attended --set all`。工程师照常打开副本；作答中 EPLAN 弹出审批对话框时，核对变更清单后点"批准"。每次审批前都会先做备份（ADR-0013）；备份放在运行器的数据根目录 `.local/evals/<运行>/eplanmcp/backups/` 中，随运行目录一起清理。

### 导出题：作答中导出的文件
模型那一侧的 eplanmcp 把导出的文件写在它的数据根目录下，`.local/evals/<运行>/eplanmcp/exports/<项目名>/<UTC 时间>-<后缀>/`（M4 设计 4.4），成功的导出不会被删除。运行器在每次作答前后各列一次这个目录，新出现的文件就是这次作答导出的。事后调用 `exported_files` 读的就是它们：它不是 eplanmcp 的工具，运行器自己回答，不调用工具，也不能用作标准调用。
- 结果是 `{ "files": [{ "path", "name", "bytes" }], "count" }`：`path` 相对于 `exports\`（以项目名开头），`name` 是文件名，按 `path` 排序；
- 题目在副本上进行时，只算这份副本的导出：服务器按项目名建导出目录，模型漏传 `project`、导出了共享夹具时，那个文件不算；
- `args` 只能有 `extension`（如 `pdf`，带不带点、大小写都可以），只列这种扩展名的文件。

导出只写文件、不改设计数据（A16、A20），所以导出题可以在共享夹具上进行。

结果在 `.local/evals/<yyyyMMdd-HHmmss>/`（不进仓库）：
- `report.md`：中文摘要，按开发集和保留集给出成功率，以及调用次数、token、耗时的中位数，列出失败、无效的任务和提示；
- `report.json`：全部数据；
- `<任务>/transcript.jsonl` 和 `stderr.txt`：每次作答的原始轨迹。**失败的轨迹是改进工具描述的第一手材料。**

成功率只计"成功"和"失败"。"无效"不计入：环境出错（未登录、API 错误、会话没有隔离、写入题的副本没有打开），或者任务本身有问题（标准调用失败、标准调用取到的值不适合比较方式）。

因 API 错误无效、而模型还没做任何事（没有工具调用、没有花费）的作答，运行器等一分钟后**重试一次**。例如，另一个 Claude Code 进程同时刷新共享的登录时，会出现 "Failed to refresh OAuth token"。未登录不重试。两次都写进报告，重试标为"（重试）"；第一次照样是无效，不计入成功率，所以费用上限不变。

## 任务集
`evals/tasks/` 中 34 题：开发集 26 题，保留集 8 题（文件名以 `holdout-` 开头）。题目都针对夹具 `EPLAN_Sample_Project`，覆盖：
- 项目概况：页数、设备数、连接数；
- 页面：某个结构下的页、某种类型的页数、页上的设备；
- 设备：所在页、用了哪些部件、没有部件的电机；
- 连接：断路器的输出端接到哪些设备；
- 部件：某个制造商的部件有多少种；
- 检查结果、英文页描述（`language` 参数）；
- 宏：按名称在宏目录中找宏、宏的尺寸；
- 有歧义的短标识（`-FC1` 出现在多个结构下，答案要列出它们，而不是只挑一个）；
- 写入（在副本上，事后调用核对结果）：建两页并写中文页描述、只改一种语言的页描述、设备改名（辅助功能和部件跟着走）、一次改两页的描述；
- M4 第一批：运行检查（只检查一次整个项目）、更新全部报表，在副本上；导出 PDF、PNG 图片、DXF，在共享夹具上，核对导出的文件；端到端的 `workflow-contactor-feedback`（M4 设计第 7 节）：新建页、插入接触器反馈控制回路、生成连接、检查、更新报表、导出这一页的 PDF，报告的错误数和文件名与事后查到的一致；
- 超出能力或不被允许的请求：页改名（没有这样的工具，也不能拿新建的空页顶替）、删页、改 scratch 之外的项目（策略拒绝）：答案中的 `done` 必须是 `false`，项目不变。

改进工具描述时只看开发集的结果；保留集只在验收时运行，不据此调整描述（[测试规范第 5 节](../docs/standards/testing.md#5-agent-评测)）。每道题的标准调用都在夹具上核对过一次：值与另行查到的数据一致。新增题目时也要先这样核对；写入题还要在一份副本上手工做一遍题目要求的修改，核对事后调用取到的值。少数题目用字面量作期望值，例如连接的对端设备，路径子集无法表达"另一端"；字面量来自同一次核对。M4 第一批的 6 题（`check-run`、`report-update`、`export-pdf`、`export-image`、`holdout-export-dxf`、`workflow-contactor-feedback`）中的字面量和预期的结果来自 api-notes A16、A19、A20 的实机验证，题目本身还没有在夹具和副本上运行过：第一次运行时按上面的办法核对。

`refuse-rename` 已经退役：它假定只有读工具，而 `eplan_device_update` 出现之后，模型可能真的在共享夹具上改名。改名改由 `device-rename` 在副本上考察。

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
| `after` | 否 | 事后调用，写法同 `oracle`：运行器在模型作答之后调用，看模型改了什么。工具返回错误时，从错误本身 `{"error": {...}}` 中取值，例如 `pick: $.error.reason` 取到 `DEVICE_NOT_FOUND`。只读第一页。取不到值时检查失败（不是无效）。`tool: exported_files` 是作答中导出的文件（见"导出题"）。每个事后调用都必须被某个检查用到 |
| `checks` | 是 | 检查列表，每条是 `answer: <路径>`（答案中的值）或 `after: <名称>`（事后调用取到的值），加一种比较方式；全部通过才算成功。标准调用取到的值不适合比较方式时（例如 `set_equals` 的期望值不是数组），任务记为无效，而不是失败；事后调用取到的期望值不适合时，检查失败 |
| `budget` | 否 | `max_tool_calls`（默认 20，最多 200）、`max_minutes`（默认 5，最多 60）、`max_usd`（默认 0.50，最多 5） |

**路径**只支持 `$`、`.字段`、`[n]`、`[*]`。不含 `[*]` 的路径取一个值，取不到时检查失败；含 `[*]` 的路径取所有匹配组成的数组（可能为空）。列表工具的结果分页时，标准调用会跟着 `nextCursor` 取完所有页：含 `[*]` 的 `pick` 收集每一页的匹配，不含 `[*]` 的 `pick` 只读第一页（`total` 等字段在第一页），所以不要用 `[n]` 取后面页上的条目。检查的路径以字段开头时，这个字段必须出现在 `answer` 的 `properties` 中。

**比较方式**（期望值写 `oracle.<名称>` 引用标准调用，`after.<名称>` 引用事后调用，或者直接写字面量。`after.` 用于作答之前不知道的值，例如模型报告的检查错误数要与事后 `eplan_check_list` 的计数相同，报告的文件名要包含实际导出的文件名）：

| 比较 | 通过条件 |
|---|---|
| `equals` | 相等：数字按数值（`12` 等于 `12.0`），字符串区分大小写，数组和对象逐项比较 |
| `equals_ignore_case` | 两个字符串忽略大小写后相等 |
| `set_equals` | 两个数组的元素相同，不计顺序、忽略重复 |
| `contains` | 数组包含期望的元素（期望值是非空数组时，包含其中每个元素）；或字符串包含期望的子串 |
| `at_least` / `at_most` | 数字不小于 / 不大于期望值 |

**YAML 的值**：加引号的一律是字符串；不加引号的 `null`、`~`、空值是 null（但检查的期望值不能留空，要比较 null 时写 `null`），`true`、`false` 是布尔值，符合 JSON 数字写法的是数字（`007`、`1.` 仍是字符串）。不支持 `!!str` 之类的标签，需要字符串时加引号即可。期望值要写以 `oracle.` 或 `after.` 开头的字面字符串时，加引号。

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

工具名目前只按命名规则（`eplan_<家族>_<动作>`）检查，事后调用 `exported_files` 除外；是否是已注册的工具，等运行器连接 eplanmcp 后（第 4 步）按服务器的工具列表检查。

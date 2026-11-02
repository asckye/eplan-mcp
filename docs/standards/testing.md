# 测试策略

> 依据：Google《Software Engineering at Google》中的测试规模划分（Small / Medium / Large），以及 Anthropic 关于"用评测驱动工具开发"的建议。
> 本项目的特殊难点：被测对象 EPLAN 需要授权、只能在 Windows 上运行、启动慢，而且会修改磁盘上的项目。因此**绝大多数测试必须能在没有 EPLAN 的情况下运行**，真实 EPLAN 只用在少量 Large 测试上。

---

## 1. 测试规模

规模按**测试允许使用的资源**划分，而不是按"单元/集成"这类说法。这样每个测试该放在哪一层没有歧义。

| 规模 | 允许使用 | 不允许 | 单个测试时长 | 运行时机 |
|---|---|---|---|---|
| **Small** | 单进程内存计算 | 文件系统、网络、管道、EPLAN、`Thread.Sleep` | < 100 ms | 每次构建、本地关卡 |
| **Medium** | 本机文件系统（临时目录）、本机进程间通信（命名管道）、子进程 | EPLAN、外网 | < 5 s | 本地关卡 |
| **Large** | 真实 EPLAN、授权、scratch 项目 | 用户的真实项目 | 分钟级 | 按第 4 节落地条件、发布验收 |
| **Agent 评测** | 真实 EPLAN + 真实模型 | 用户的真实项目 | 每个任务数分钟 | 发布前、工具描述改动后 |

测试类用 trait 标注规模：`[Trait("Size", "Small")]`。CI 按 trait 过滤执行。

不设测试规模比例目标。业务逻辑应从 EPLAN 调用里剥离出来，单独做 Small 测试；真实 API 行为由 Large 验证。

---

## 2. 各组件怎么测

| 组件 | Small | Medium | Large |
|---|---|---|---|
| **Contracts**（共享的请求/响应类型） | 序列化往返、schema 生成 | — | — |
| **Host**（MCP 服务器、工具注册、策略、审计） | 参数校验、策略引擎决策表、错误码映射、分页、截断 | 通过 stdio 启动真实 Host + **Fake Bridge**，用 MCP 客户端走完整协议 | 连接真实 Bridge |
| **Bridge**（EPLAN 内的插件） | 不依赖 EPLAN 类型的逻辑（对象映射、过滤、坐标与栅格计算） | 命名管道服务端 + 假调度器 | 在 EPLAN 中加载，执行真实 API |
| **Workbench**（桌面界面） | 视图模型逻辑：排序、防误触、倒计时、文字清洗、设置文件 | 真实窗口中的页面、焦点、UIA 树和三套调色板的对比度（[workbench-ui](workbench-ui.md) 15.1）；真实管道上 Host 的 `WorkbenchChannel` 对 Workbench 的服务端 | 手工冒烟清单（发布前） |

### 可测试性设计要求
- EPLAN API 的调用集中在 Bridge 的**适配层**，适配层之外的代码不引用 `Eplan.EplApi.*`。
- Host 只依赖 `IBridgeClient` 接口。测试用 `FakeBridgeClient` 实现它，可以按脚本返回结果或注入故障（超时、EPLAN 弹窗阻塞、授权丢失、连接断开）。
- 时间、随机数、文件系统通过接口注入（`TimeProvider`、`IFileSystem`），Small 测试中不使用真实的时钟和磁盘。

---

## 3. 工具契约快照测试

工具的名称、标题、描述、输入输出 JSON Schema、四个注解，以及服务器说明（instructions），是本项目对外承诺的 API（见 [mcp-tool-design.md 第 11 节](mcp-tool-design.md#11-契约变更)）。

- `ToolContractSnapshotTests`（Medium）用 [Verify](https://github.com/VerifyTests/Verify) 把服务器说明和 `tools/list` 中每个工具的完整定义（工具与 `serve` 注册的相同，按名称排序）序列化成 `tests/Contracts.Snapshots/server-contract.verified.json`，提交到仓库。
- 任何导致快照变化的变更集，都必须同时交付新的快照，并在提交正文中说明契约为什么要变（[mcp-tool-design 第 11 节](mcp-tool-design.md#11-契约变更)）。评审时快照的 diff 就是契约变更的清单。
- 描述文字的修改也会体现在快照中。描述会直接影响模型行为，所以同样要评审，必要时重跑 Agent 评测。
- 快照从不自动接受：契约一变，测试就失败（本地和 CI 都一样），Verify 只在旁边写出 `*.received.json`（已被 `.gitignore` 忽略），不会弹出比较工具。
- `CliJsonSnapshotTests`（Medium）用同样的方式固定 `doctor --format json`、`audit verify --format json`、`client-config --format json`、`install-addin`、`uninstall-addin --format json` 和 `vm-test --format json` 的结构（`doctor-json`、`audit-verify-json`、`client-config-json`、`addin-json`、`vm-test-json`，[CLI 参考](../reference/cli.md#json-输出契约)），更新方法与下文相同；结构变化时 `schemaVersion` 加 1。
- 依赖工具精确 schema 的测试类要加 `[Collection(typeof(ToolBuildIsolation))]`，让它在没有其他测试运行时构建工具：几个测试同时构建工具时，SDK 内部存在竞争，生成的 schema 会不稳定（详见该类的注释）。

### 更新快照
1. 运行快照测试：`dotnet test --project tests\EplanMcp.Host.Tests -- --filter-class "EplanMcp.Host.Tests.Tools.ToolContractSnapshotTests"`。契约有变化时测试失败，失败信息逐行列出变化（`-` 是已提交的快照，`+` 是当前结果，带行号）；还没有快照时列出全部内容。
2. 审视差异：`git diff --no-index tests\Contracts.Snapshots\server-contract.verified.json tests\Contracts.Snapshots\server-contract.received.json`，确认每一处变化都是有意的。
3. 接受：用 `.received.json` 覆盖 `.verified.json`（`Move-Item -Force` 两个文件），重新运行测试确认通过，把新快照和代码改动放在同一个变更集中交付。

快照文件由 Verify 写出，是带 BOM 的 UTF-8、LF 换行（`.editorconfig` 中有对应规则）。不要手工编辑快照，改代码后重新生成。

### 面向模型文本的检查
快照只能靠人看，下面两个 Small 测试自动检查模型读到的文字，工具都从注册结果中动态获取：
- `ModelTextTests`：服务器说明、`tools/list` 中的全部内容、用法文件，以及 Host、Bridge.Core、Contracts 程序集中的全部字符串字面量（错误消息、摘要和提示都来自这里）不含中日韩字符（[架构 8.11](../architecture/architecture.md#811-界面语言)）。
- `ToolUsageExampleTests`：工具描述中的 `Example:` 和用法文件 Examples 一节中的每个代码块，都是该工具输入 schema 允许的参数对象；这些文字和服务器说明中出现的 `eplan_*` 名称都是已注册的工具。

确实需要的例外写在测试类开头的清单里，每项都要写理由（例如 `doctor` 的中英双语输出只给工程师看）。某项例外不再被用到时测试会失败，提醒删除它。

---

## 4. Large 测试（实机）

### 环境
- 装有 EPLAN（版本与支持矩阵一致）、具备 API 扩展授权的开发机；不使用自托管 runner（[ADR-0020](../adr/0020-no-self-hosted-runner.md)）。
- 测试只操作 **scratch 根目录**（默认 `%USERPROFILE%\EplanMcp\scratch\`，见 ADR-0008）下的项目；按类的约定清理或保留验收证据。Host 的写入保护会拒绝 scratch 之外的写操作，测试不能关闭这项保护。
- 涉及 EPLAN 行为的变更集，只有受影响 Large 类已在真实机器通过，或已列入 [待验台账](../process/large-pending.md) 且提交正文明确说明待验，才能落到 main。选择类、单机串行批次及发布前完成条件见 [实机验证](../process/live-verification.md)，夹具和命令见 [Large README](../../tests/EplanMcp.Large.Tests/README.md)。只读测试共用夹具脚本复制的示例项目，不自建、也不删除它；M3 的写测试按下文的要求自建夹具。
- 能在离线宿主上运行的 Large 测试类另标 `[Trait("Host", "Offline")]`（[M5 设计](../design/2026-10-20-m5-operability.md) 4.9），按 `--filter-trait "Host=Offline"` 运行，方法见上面的 README。这些类中需要打开规模夹具（`EPLANMCP_LARGE_FIXTURE`）的方法另标 `[Trait("Fixture", "Scale")]`，离线运行（包括 `release-addin.ps1`）用 `--filter-not-trait "Fixture=Scale"` 排除，否则它们只能跳过，而发布脚本不接受跳过。

### 测试夹具项目
- **不把 EPLAN 自带的示例项目提交进仓库**，因为它的再分发许可不明确。
- 夹具分两类：
  1. **代码生成**：测试开始时通过 API 新建一个最小项目（指定模板），再按需创建页面和设备。优先使用这种方式，夹具的内容在代码里一目了然。
  2. **存档文件**：自己制作的小项目，以 EPLAN 备份格式（`.zw1`）放在 `tests/fixtures/projects/`，测试时还原到 scratch 目录。只用于代码难以构造的场景，例如特定版本的旧项目升级。
- 每个夹具都有一份 `README.md`，说明内容、制作方法和使用的 EPLAN 版本。

### 批量实机验证（第 3 批 V1/V2）

仅在专用开发机上运行 `BatchVerification`，先按 Large README 用 `eval-fixture.ps1 -WriteCopies -Force` 准备新的 LargeWrite，并按 A41 配置 `EPLREMOTE_HOST`（本机 IPv4）、`EPLREMOTE_PORT`（默认 49152）及可选的 `EPLANMCP_EPLREMOTE`。`PlanStepLiveSession` 只关闭已知夹具副本，并确认 LargeWrite 是唯一打开的 scratch 项目；`TrialApprovals` 只逐次批准这两个试运行工具，拒绝解除写入暂停。

```powershell
$env:EPLANMCP_BATCH_VERIFY = '1'
$env:EPLANMCP_BATCH_MINUTES = '120'
# 可选：从指定 ID 开始（包含该项），省略则从头开始
# $env:EPLANMCP_BATCH_FROM = 'O:Eplan.EplApi.DataModel.Page.Name'
# 或从指定 ID 的下一项续跑；FROM 与 AFTER 只能设置一个
# $env:EPLANMCP_BATCH_AFTER = 'O:Eplan.EplApi.DataModel.Page.Name'
dotnet test --project tests/EplanMcp.Large.Tests -- --filter-trait 'Batch=Verification'
```

写入（API 与 action）每项上限 90 秒，只读每项 30 秒；整轮默认 120 分钟（可设大于 0、不超过 1440），每完成 25 项输出数量、耗时和当前 ID。条目来自 main 当前生成的 `ApiTryCatalog.PropertyGroups`、`MethodGroups` 与逐组 `Methods(group)`，覆盖设置、句柄、数组和生命周期；方法每组选择参数最少的重载，完整数量由本轮 expected 输出和 Coverage 测试核对，不固定历史计数。接收器取生成的 `ReceiverShape`：服务保留 Project 定位器，静态调用和 Create 工厂省略 target，Remove 使用现有 destructive 审批和执行前备份，不绕过 ADR-0035。布尔和整数不猜值；句柄/via 来源、文件、显式页参数及其他无法自动生成的参数记为 `failed:needs_context`，不调用。对象参数从生成的 `ObjectReadTypes` 支持表和同一 scratch 副本的 `eplan_object_find` 结果取得，数组填入非空的已发现定位器；未发现接收对象记为 `skipped:no_object`。随后执行无参数、无 TYPE、经 Host 校验和 `ActionTryPolicy` 筛选的 action；数量随安装目录变化。结果在 FROM/AFTER 筛选之前记录完整 expected 数量。

结果路径打印在测试输出中：`<scratch>/batch-verification/<运行 ID>/results.json`，每轮独立保存，每项后原子保存，包含日期、真实 EPLAN 版本、条目 ID、kind、outcome、seconds、changed、undo_restores、eplan_messages，以及停止位置。API 写入 ID 后缀为 `#write`，action ID 为 `A:<名称>`。输出、审计与暂停状态都留在 scratch，不写仓库。只读若改变设计、撤销未恢复、结果未知、模态窗口或超时均停跑；先保存当前项的 modal/outcome_unknown 等结果，再持久化暂停，不再执行清理或切换项目，工程师检查后通过正常审批渠道解除暂停。FROM 包含指定项，AFTER 跳过指定项供外部驱动重启后续跑；续跑只选择起点，不解除暂停，也不自动重试未知项。

写值由读值推导：布尔取反、整数加 1、浮点加 0.5、字符串加 `_v`、枚举取下一成员；写后一次 `XEsUndoAction`，比对原值和页快照。action 先打开第一页，设计指纹变化时检查一次撤销；其恢复证据同时核对页快照与设计文件哈希（不含 User/锁文件），数据库字节不恢复时不能晋级。快照有现有范围限制，相等不证明完全没有副作用。

```powershell
dotnet run --project eng/coverage -c Release -- import-verification '<第一轮 results.json 路径>' '<后续轮 results.json 路径>'
dotnet run --project eng/coverage -c Release -- generate
```

导入使用仓库目录和 overlay 的既有分母与排除规则，不依赖本机 action 目录；只接受其中的组/action ID。未知、已移除或已排除 ID 逐项报告并丢弃，已有 verification 中同类旧 ID 一并移除；不会添加目录或排除规则，不改变操作层 4014 分母。本机 UI action 若不在已登记的 action 分母，也只报告不导入。单文件内重复 ID、版本、日期或已知 ID 的证据格式错误时，全部文件拒绝且不修改 overlay。重复导入幂等；按命令行文件顺序合并，同 ID 后面的文件覆盖前面的，其余有效证据及人工维护的 live/api/availability/reached-by-tool 原文保留。报告按三种 kind 分列“通过且撤销恢复／只读通过／失败／未知／跳过／需要上下文”；回滚归失败，但错误含 `EPLAN could not complete` 且无系统消息时记为 `failed:needs_context`。未运行不标为通过或环境阻塞，`verified-whitelist.yaml` 只供审阅，不改变运行时白名单。

### 断言方式
- **结构化断言优先**：用只读工具读回项目状态（页面、设备、连接、属性）后比较，而不是比对截图。
- 读回的 JSON 先做规范化处理（去掉会话内句柄、时间戳、本机路径），再与 golden 文件比较。
- 导出类功能（PDF/PNG/DXF）：检查文件存在、页数、大小范围。需要验证图形时，用图像差异比较并设置阈值，阈值写在测试里。

### 稳定性
- 不稳定的测试先保留证据、查因；每个实机失败用精确输入补 Small 回归。保持 main 的 Large 通过，不弱化断言，不允许“重跑直到通过”；失败待修和未完成验证记入待验台账。
- Large 测试不依赖其他测试结果：只读测试可共用只读夹具，写测试使用隔离副本并按各类约定清理或保留验收证据。单机各批次串行。

---

## 5. Agent 评测

工具最终是给模型用的。单元测试证明工具"能工作"，Agent 评测证明模型"会用"。这是 Anthropic 构建工具时的核心方法：**用真实任务评测，再根据结果改进工具。**

### 任务集
放在 `evals/tasks/*.yaml`，每个任务包含：
```yaml
id: create-motor-starter-page
prompt: |
  在当前项目中新建一页原理图，放置一个电机保护开关 -Q1 和一个接触器 -K1，
  把 -Q1 的输出接到 -K1 的主触点。
fixture: empty-iec-project        # 起始状态
verify:                           # 程序化验证，不依赖模型自述
  - tool: eplan_device_get
    args: { tag: "-Q1" }
    expect: { exists: true }
  - tool: eplan_connection_list
    args: { page: "$created_page" }
    expect: { count_at_least: 3 }
budget: { max_tool_calls: 25, max_minutes: 5 }
```

### 指标
- 任务成功率（由程序验证，不看模型怎么说）
- 每个任务的工具调用次数、输入/输出 token 数、耗时
- 出错的工具调用比例，以及错误后模型能否自行纠正

### 规则
- 任务优先取自真实需求：[API 覆盖矩阵第 4 节](../eplan/api-coverage.md#4-国内工程师的高频任务需求信号)列出了国内工程师付费购买插件来完成的高频任务（批量建页、批量改属性、线号、部件生成等），每一项都应至少对应一个评测任务。
- 任务集分成**开发集**和**保留集**。改进工具描述时只看开发集，保留集用来防止针对任务集过拟合。
- 每次发布前跑完整任务集，结果附在发布说明里。成功率下降则不能发布，除非有书面说明。
- 评测时记录完整的调用轨迹，失败的轨迹是改进工具设计的第一手材料。

---

## 6. 工具与约定

| 用途 | 选择 | 说明 |
|---|---|---|
| 测试框架 | xUnit v3 | 支持 .NET Framework 4.8 和 .NET 10 |
| 断言 | xUnit 自带断言或 Shouldly | **不使用 FluentAssertions 8.x 及以上**：其许可证已改为商业许可 |
| 快照 | Verify 32.x | 工具契约、规范化后的读回结果。**暂不升级到 33.x**：从 33.0 起 Verify 主包的二进制改用 Open Source Maintenance Fee EULA（年营收 1 万美元以上的使用者需付维护费），升级与否由项目负责人决定 |
| 模拟 | 手写 Fake 优先，必要时用 NSubstitute | Fake 能表达"EPLAN 的行为"，比逐个方法打桩更可读 |
| 覆盖率 | coverlet，只作参考 | 不设覆盖率门槛。策略引擎和写入保护这类安全关键代码，要求逐条覆盖决策表 |

### 命名
`方法或场景_条件_预期结果`，例如 `CreatePage_OutsideScratchRoot_IsDeniedWithoutApproval`。

### 必须测失败路径的地方
以下区域的变更集没有失败场景测试不能交付；这是唯一范围清单，其他规范引用本节：
- 审批与写入保护（策略引擎、可写范围）
- 审计写入（磁盘满、文件被占用）
- 路径判断、导出目录与备份（越界、链接、备份失败）
- 管道认证，以及 Workbench 审批界面和管道（身份、畸形帧、断开、迟到回答、防误触）
- Bridge 与 EPLAN 主线程之间的调度（超时、EPLAN 正在显示模态对话框、EPLAN 退出）
- 事务回滚（写到一半出错时，项目状态必须恢复）
- `install-addin`（注册失败、已有插件属于其他用户）
- `eplan_action_run` 白名单校验（未知 action/参数、越界路径和注入）

---

## 7. 测试数量下限

过滤条件写错、`Size` trait 拼错或测试发现失败时，`dotnet test` 可能只运行了很少甚至 0 个测试，结果仍然是"通过"。本地关卡与 CI 的 "Test count floor" 步骤读取测试生成的 TRX 文件，按 [`tests/test-suites.json`](../../tests/test-suites.json) 检查：

- 登记的每个测试程序集（`assembly`）通过的测试数不少于 `minPassed`；没有结果也算失败；
- 运行了但没有登记的测试程序集也算失败：新增测试项目时，在同一个变更集中登记。
- `minPassed` 低于同一次运行实际通过数的 80% 时失败，防止测试增长后下限长期失效。

下限设在当时实际通过数的 90% 左右，只用来发现"整套没跑或大量没跑"，不要求每加一个测试就改。

**下限只升不降。** 测试明显增多后（例如一个里程碑结束时）把下限提高：
1. 在本地运行 `dotnet test --solution EplanMcp.Ci.slnf -- --filter-not-trait "Size=Large"`，或者看 CI 日志中该步骤打印的每个程序集的通过数和当前下限；
2. 把 `minPassed` 改为新通过数的 90%（向下取整到整数），单独作为变更集或随相关变更集更新。

删除或合并测试导致低于下限时，先确认这些测试确实应该删除。降低下限等于放宽检查，必须在提交正文中说明原因，由项目负责人批准。

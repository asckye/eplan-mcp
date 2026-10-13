# 测试策略

> 依据：Google《Software Engineering at Google》中的测试规模划分（Small / Medium / Large），以及 Anthropic 关于"用评测驱动工具开发"的建议。
> 本项目的特殊难点：被测对象 EPLAN 需要授权、只能在 Windows 上运行、启动慢，而且会修改磁盘上的项目。因此**绝大多数测试必须能在没有 EPLAN 的情况下运行**，真实 EPLAN 只用在少量 Large 测试上。

---

## 1. 测试规模

规模按**测试允许使用的资源**划分，而不是按"单元/集成"这类说法。这样每个测试该放在哪一层没有歧义。

| 规模 | 允许使用 | 不允许 | 单个测试时长 | 运行时机 |
|---|---|---|---|---|
| **Small** | 单进程内存计算 | 文件系统、网络、管道、EPLAN、`Thread.Sleep` | < 100 ms | 每次构建、每个 PR |
| **Medium** | 本机文件系统（临时目录）、本机进程间通信（命名管道）、子进程 | EPLAN、外网 | < 5 s | 每个 PR |
| **Large** | 真实 EPLAN、授权、scratch 项目 | 用户的真实项目 | 分钟级 | 每晚、发布前、手动触发 |
| **Agent 评测** | 真实 EPLAN + 真实模型 | 用户的真实项目 | 每个任务数分钟 | 发布前、工具描述改动后 |

测试类用 trait 标注规模：`[Trait("Size", "Small")]`。CI 按 trait 过滤执行。

**比例目标**：Small 约 70%，Medium 约 20%，Large 约 10%。Google 的参考比例约为 80/15/5；本项目的 Bridge 适配层天然需要更多实机测试，所以比例略放宽。如果某个功能只能靠 Large 测试覆盖，说明可测试性设计有问题：业务逻辑应该从 EPLAN 调用里剥离出来，单独做 Small 测试。

---

## 2. 各组件怎么测

| 组件 | Small | Medium | Large |
|---|---|---|---|
| **Contracts**（共享的请求/响应类型） | 序列化往返、schema 生成 | — | — |
| **Host**（MCP 服务器、工具注册、策略、审计） | 参数校验、策略引擎决策表、错误码映射、分页、截断 | 通过 stdio 启动真实 Host + **Fake Bridge**，用 MCP 客户端走完整协议 | 连接真实 Bridge |
| **Bridge**（EPLAN 内的插件） | 不依赖 EPLAN 类型的逻辑（对象映射、过滤、坐标与栅格计算） | 命名管道服务端 + 假调度器 | 在 EPLAN 中加载，执行真实 API |
| **Workbench**（桌面界面） | 视图模型逻辑 | — | 手工冒烟清单（发布前） |

### 可测试性设计要求
- EPLAN API 的调用集中在 Bridge 的**适配层**，适配层之外的代码不引用 `Eplan.EplApi.*`。
- Host 只依赖 `IBridgeClient` 接口。测试用 `FakeBridgeClient` 实现它，可以按脚本返回结果或注入故障（超时、EPLAN 弹窗阻塞、授权丢失、连接断开）。
- 时间、随机数、文件系统通过接口注入（`TimeProvider`、`IFileSystem`），Small 测试中不使用真实的时钟和磁盘。

---

## 3. 工具契约快照测试

工具的名称、描述、输入输出 JSON Schema 和注解，是本项目对外承诺的 API（见 [mcp-tool-design.md](mcp-tool-design.md)）。

- 用 [Verify](https://github.com/VerifyTests/Verify) 把 `tools/list` 的完整结果序列化成快照，提交到仓库（`tests/Contracts.Snapshots/*.verified.json`）。
- 任何导致快照变化的 PR，都必须同时提交新的快照。评审时快照的 diff 就是契约变更的清单。
- 描述文字的修改也会体现在快照中。描述会直接影响模型行为，所以同样要评审，必要时重跑 Agent 评测。

---

## 4. Large 测试（实机）

### 环境
- 自托管 runner，安装 EPLAN（版本与支持矩阵一致），具备 API 扩展授权，runner 标签为 `eplan-<版本>`。
- 测试只操作 **scratch 根目录**（默认 `%USERPROFILE%\EplanMcp\scratch\`，见 ADR-0008）下的项目，测试结束后删除。Host 的写入保护会拒绝 scratch 之外的写操作，测试不能关闭这项保护。

### 测试夹具项目
- **不把 EPLAN 自带的示例项目提交进仓库**，因为它的再分发许可不明确。
- 夹具分两类：
  1. **代码生成**：测试开始时通过 API 新建一个最小项目（指定模板），再按需创建页面和设备。优先使用这种方式，夹具的内容在代码里一目了然。
  2. **存档文件**：自己制作的小项目，以 EPLAN 备份格式（`.zw1`）放在 `tests/fixtures/projects/`，测试时还原到 scratch 目录。只用于代码难以构造的场景，例如特定版本的旧项目升级。
- 每个夹具都有一份 `README.md`，说明内容、制作方法和使用的 EPLAN 版本。

### 断言方式
- **结构化断言优先**：用只读工具读回项目状态（页面、设备、连接、属性）后比较，而不是比对截图。
- 读回的 JSON 先做规范化处理（去掉会话内句柄、时间戳、本机路径），再与 golden 文件比较。
- 导出类功能（PDF/PNG/DXF）：检查文件存在、页数、大小范围。需要验证图形时，用图像差异比较并设置阈值，阈值写在测试里。

### 稳定性
- 不稳定的测试在 24 小时内隔离（标注 `[Trait("Quarantine", "true")]` 并开 Issue），一周内修好或删除。不允许"重跑直到通过"。
- 每个 Large 测试必须独立：自建夹具、自己清理，不依赖执行顺序。

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
- 任务集分成**开发集**和**保留集**。改进工具描述时只看开发集，保留集用来防止针对任务集过拟合。
- 每次发布前跑完整任务集，结果附在发布说明里。成功率下降则不能发布，除非有书面说明。
- 评测时记录完整的调用轨迹，失败的轨迹是改进工具设计的第一手材料。

---

## 6. 工具与约定

| 用途 | 选择 | 说明 |
|---|---|---|
| 测试框架 | xUnit v3 | 支持 .NET Framework 4.8 和 .NET 10 |
| 断言 | xUnit 自带断言或 Shouldly | **不使用 FluentAssertions 8.x 及以上**：其许可证已改为商业许可 |
| 快照 | Verify | 工具契约、规范化后的读回结果 |
| 模拟 | 手写 Fake 优先，必要时用 NSubstitute | Fake 能表达"EPLAN 的行为"，比逐个方法打桩更可读 |
| 覆盖率 | coverlet，只作参考 | 不设覆盖率门槛。策略引擎和写入保护这类安全关键代码，要求逐条覆盖决策表 |

### 命名
`方法或场景_条件_预期结果`，例如 `CreatePage_OutsideScratchRoot_IsDeniedWithoutApproval`。

### 必须测失败路径的地方
以下代码的 PR，没有失败场景测试不能合并：
- 审批与写入保护（策略引擎）
- 审计写入（磁盘满、文件被占用）
- Bridge 与 EPLAN 主线程之间的调度（超时、EPLAN 正在显示模态对话框、EPLAN 退出）
- 事务回滚（写到一半出错时，项目状态必须恢复）

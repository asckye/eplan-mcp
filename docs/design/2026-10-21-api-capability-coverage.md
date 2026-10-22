# 设计文档：EPLAN 官方 API 能力覆盖（第一期：能力目录、口径与阶段计划）

| 项 | 内容 |
|---|---|
| 状态 | Approved（Q1–Q5 已于 2026-10-21 决定） |
| 级别 | L2（不改变工具契约、进程拓扑和安全模型；工具面的分组另见 [ADR-0022](../adr/0022-domain-toolsets.md)（已接受）） |
| 关联 Issue | — |
| 相关 ADR | [0002](../adr/0002-host-bridge-process-model.md)、[0005](../adr/0005-safety-model.md)、[0006](../adr/0006-tool-surface-design.md)、[0009](../adr/0009-supported-eplan-versions.md)、[0010](../adr/0010-write-outcomes.md)、[0012](../adr/0012-export-tier.md)、[0013](../adr/0013-destructive-backup.md)、[0019](../adr/0019-master-data-writes.md)、[0021](../adr/0021-action-run-whitelist.md)、[0022](../adr/0022-domain-toolsets.md)（已接受） |
| 最后更新 | 2026-10-21 |

## 1. 背景
项目负责人 2026-10-21 定下的目标：**EPLAN Platform 2026 官方公开 API 的每一项能力，都能追溯到一个实现、一条访问路径、一份验证证据，或者一条明确的"不做"理由。工具数量不是完成的标准。**

现状：
- [ADR-0006](../adr/0006-tool-surface-design.md) 按任务设计工具，main 上有 47 个工具（`core` 25 个）。
- [api-coverage.md](../eplan/api-coverage.md) 只到命名空间一级，"覆盖"指"这个领域的常见任务有工具"。
- [api-actions.md](../eplan/api-actions.md) 逐条核对了 99 个文档化 action（按 TYPE 拆成 177 行）；[ADR-0021](../adr/0021-action-run-whitelist.md) 给其中 57 种没有任务工具的用法定了白名单方案。
- [api-notes.md](../eplan/api-notes.md) 有 30 条实机验证（A1–A30）和 10 条文档与反射事实（B1–B10），按主题组织，没有按成员编号。

缺的是成员一级的清单：没有分母，就回答不了"某项能力做了没有、证据在哪里"。本设计给出口径、目录格式、生成方式和阶段计划；第一期的其他产物（实现映射、覆盖报告、Action 目录）由并行的工作完成，共用第 3 节的约定。

## 2. 目标与非目标
- **目标（第一期）**：
  1. 机器可读的能力目录：结构（JSON Schema）和 2026.0.3 的首批真实数据，可在任何装有 EPLAN 的机器上重新生成，结果逐字节相同；
  2. 差距分析（第 9 节）；
  3. 现有工具到官方能力的映射，以及自动生成的覆盖报告（并行工作，按第 3、4、7 节的约定）；
  4. 最小包装生成器和代表性样例（第 8 节划定边界）；
  5. 后续阶段的计划和验收标准（第 11 节）。
- **非目标**：
  - 把每个 API 包装成一个 MCP 工具（ADR-0006 不变，工具面怎样组织见 [ADR-0022](../adr/0022-domain-toolsets.md)）；
  - 运行时的任意反射调用（ADR-0021 已否决）；
  - 在仓库中保存官方文档的文字；
  - 2026.0.3 之外的版本数据（开发机只装了这一个版本，ADR-0009 支持的其他版本在有安装时再生成）。

## 3. 计数口径（共享约定）

### 3.1 唯一标识
- **SDK 成员**：用 EPLAN IntelliSense 文件（`Eplan.EplApi.*.xml`）中的 XML 文档注释 ID，原样使用，例如 `M:Eplan.EplApi.HEServices.Translate.TranslateObjects(Eplan.EplApi.DataModel.StorableObject[])`、`T:…`、`P:…`、`F:…`、`E:…`。
- 这与 Roslyn 的 `ISymbol.GetDocumentationCommentId()` 基本一致。差别只在 C++/CLI 的签名修饰（实测）：IntelliSense 保留装箱值类型的 `!`（`System.ValueType!Eplan.EplApi.Base.PointD`）和 `IsSignUnspecifiedByte`，去掉 `IsConst`、`IsLong`、`IsBoxed`，C++ 引用 `T&` 写作 `T`。**以 IntelliSense 为准**，目录生成器按这些规则拼写。
- **Action**：`A:<action>` 或 `A:<action>/<TYPE>`，由 Action 目录定义。
- **重载组**（本项目自己的键，不是 EPLAN 的 ID）：`O:<声明类型>.<名称>`，同一类型上同名的方法或属性的所有重载归为一组。
- **属性 ID**：数据字典中的一项，用（对象类型，属性名）标识，例如（`Function`，`FUNC_TEXT`）；它的成员 ID 由第 5.3 节的规则推出。
- 继承的成员只记在声明它的类型上（元数据本来就只在声明类型上有一行），映射时把派生类型上的调用归到声明类型。

### 3.2 三个层次和分母（EPLAN 2026.0.3.25421 实测）

| 层次 | 内容 | 计数单位 | 进入分母 | 规模 |
|---|---|---|---|---|
| **操作** | 方法（不含构造函数、`Finalize`、`Dispose`）、**对象属性**（见下）、Action 用法 | 重载组；Action 按行 | **是，主分母** | 5,917 个成员 → 4,649 组；机械排除后 **4,342 组**（方法 2,003 组、属性 2,339 组）；另加 Action 177 行 |
| **数据字典** | `*PropertyList` 类的属性 ID 成员和 `Properties.*` 枚举值 | 按对象类型 | 是，按对象类型计（69 个） | 27,703 个成员 → **9,521 个属性 ID**，69 个对象类型；其中 936 个标为过时 |
| **类型** | 类型、枚举值、字段、常量、事件、构造函数、生命周期方法 | — | 否，只用于依赖映射 | 1,158 个类型（类 699、枚举 390、接口 40、委托 15、结构 14），枚举值 6,725，构造函数 963，`Finalize`/`Dispose` 873，字段 77，常量 26，事件 1 |

- **对口径的一处扩展：对象属性计入操作层（已决定，Q1）。** 原定的操作层只有方法。实测非字典的公开属性有 2,633 个，移动、旋转、改名、改文字这类能力都是属性的 setter（如 `Placement.Location`），不计入就看不到。主分母是 **4,342 组**（方法 2,003 组 + 属性 2,339 组）；报告同时单独给出只按方法计的数字（2,003 组），以免改变别人的口径。
- 数据字典按对象类型计：一个对象类型的通用属性读、写路径经过验证，就覆盖这个对象类型的全部属性 ID，不逐个属性计。
- 运算符、委托的 `Invoke` 等留在操作层，但被第 3.3 节的规则排除，所以不进分母。

### 3.3 机械排除规则
生成器只用元数据能判断的规则排除，排除理由取第 4 节的枚举。其余排除（只能交互、架构上不用等，见第 3.3.1 节）都由人在映射数据中逐条写明。

| 规则 | 理由 | 操作层成员数 | 判断方法 |
|---|---|---:|---|
| `native_signature` | internal | 220 | 签名含指针或全局命名空间的 C++ 类型，C# 无法调用 |
| `internal_namespace` | internal | 59 | 命名空间中有 `Internal` 一段 |
| `obsolete_attribute` | deprecated | 106 | 带 `[Obsolete]`；数据字典中另有 2,754 个成员（936 个属性 ID） |
| `operator` | not_applicable | 192 | `op_*` 运算符和类型转换 |
| `delegate_member` | not_applicable | 24 | 委托的 `Invoke`、`BeginInvoke`、`EndInvoke` |
| `object_override` | not_applicable | 71 | 重写的 `ToString`、`Equals(object)`、`GetHashCode` |

规则按表中顺序取第一条命中的。

### 3.3.1 人工排除（Q3，2026-10-21 决定）
下面三个范围由项目负责人决定排除。它们不是机械规则：仍在 4,342 组的分母之内，由人在映射数据中逐条标 `excluded` 并写明原因，不停留在 `registered`。

| 范围 | 组数 | 理由 | 说明 |
|---|---:|---|---|
| 远程相关（`RemoteClient`、`GrpcServer`、`Remoting`、`RemoteServer`） | 253 | `not_applicable` | 架构上不用（[ADR-0002](../adr/0002-host-bridge-process-model.md)） |
| `Gui` | 72 | `gui` | 需要有人操作界面；第 7 阶段设计交互支持时逐项复审 |
| `Scripting` | 3 | `unsafe` | Code 级，默认关闭（[ADR-0005](../adr/0005-safety-model.md)） |

### 3.4 不进目录的内容，以及与 IntelliSense 原始计数的差别
负责人按 IntelliSense 文件统计的规模（44,976 项：类型 1,268、方法 5,743 + 2,299 等）是 12 个 XML 文件的原始条目。目录只收公开 API，差别如下：
- 不同文件之间重复的条目：去重后 42,561 个 ID；
- 非公开成员，或非公开类型（含 C++ 原生类）的成员：954 个 ID；
- `Eplan.EplApi` 命名空间之外的类型（C++/CLI 的实现类型）：1,376 个 ID，对应 3,810 个公开类型；
- 无法对应的 ID：77 个，逐个看过，都是原生辅助函数或属性访问器的文档，没有一个属于公开 API；
- 没有 IntelliSense 文件的程序集不收：`RecorderToolsu`、`WebServicesu`、`WebServiceu`、`Communicationu.erx`。`CommandLineActionsu.erx` 有 IntelliSense 文件，但其中的 43 个 action 类都不公开，由 Action 目录以 `A:` 覆盖。

目录共 43,443 项，其中 40,154 项的 ID 在 IntelliSense 中，3,289 项不在（公开但没有文档）。操作层中没有文档的分母组有 320 个，大多是 `Eplan.EplApi.GrpcServer` 中由 protobuf 生成的类。按 Q2 的决定，它们**留在分母里**，标 `documented: false`（"无文档"），在报告中单独列出；使用前先用 spike 验证行为，不凭类型名推断。
- 320 组中有 186 组同时属于第 3.3.1 节的人工排除范围（远程相关 181、`Gui` 4、`Scripting` 1），它们的状态是 `excluded`，同时保留"无文档"标记。其余 134 组是真正要验证的无文档能力。

## 4. 状态与证据

| 状态 | 含义 | 必须附带的证据 |
|---|---|---|
| `registered` | 已登记在目录中，什么都还没做 | 无（生成器写出的每一项的默认状态） |
| `implemented` | Bridge 或 Host 的代码调用了它，有工具或内部操作经过它 | 编译后的引用（第 7 节）和调用它的工具或 Bridge 操作名 |
| `automated_test` | 有 Small 或 Medium 测试覆盖这条路径 | 测试名 |
| `live_verified` | 在实机 scratch 项目上验证过，含读回 | Large 测试名或 api-notes 条目，EPLAN 版本、变体、授权、日期 |
| `partial` | 只覆盖部分重载、参数或对象类型 | 写明缺什么 |
| `env_blocked` | 缺安装、授权或环境，无法验证 | 写明条件，例如"授权中没有 Preplanning" |
| `excluded` | 有意不做 | 理由：`gui`（只能交互）、`internal`、`deprecated`、`unsafe`、`not_applicable`，加一句说明 |

- 前三个是递进的：`live_verified` 隐含另外两个的证据都在。
- **无法验证的不能标为完成。** 证据必须可重复：能指出测试、用例或 api-notes 条目，而不是"试过可以"。
- 状态按 EPLAN 版本记；换主版本后，`live_verified` 退回 `automated_test`，直到在新版本上重新验证。

### 4.1 完成的定义
一个能力（一个重载组、一行 Action，或一个对象类型的数据字典）标为 `live_verified`，需要同时满足：
1. 写明输入、输出、前置条件和错误：参数的取值范围，返回什么，需要什么状态（项目打开、对象有效、锁定），失败时返回哪个错误码；
2. 有三类测试：有效输入；无效输入；**过时对象**（对象已删除或项目已重新打开后，再用旧的引用）；
3. 写操作有读回证据：执行后重新读取，逐项核对，或者如实说明只能比对指纹（ADR-0021 第 4 节）；
4. 撤销、备份、恢复按数据的种类选择：

   | 数据种类 | 保护手段 |
   |---|---|
   | 项目数据 | `SafetyPoint` 回滚 + 一步撤销（ADR-0005）；Destructive 另加 `CopyProject` 快照（ADR-0013） |
   | 主数据（部件库、翻译库） | 先 `backup MASTERDATA`，只支持文件型库（ADR-0019） |
   | 文件 | 只写进 Host 新建的导出目录（ADR-0012），不覆盖 |
   | 系统设置 | 默认不开放写入；需要时另写 ADR |

5. 有可重复的实机用例：在示例项目的 scratch 副本上，按步骤可以再跑一次；
6. 记下 EPLAN 版本、变体和授权模块；
7. 预览要分清两种情况："精确列出将要发生的变化"（任务工具），和"只列出将要执行的调用"（ADR-0021 的运行器）；
8. 超时要分清"没有执行"、"执行失败"和"结果未知"三种结局（ADR-0010 的 `OUTCOME_UNKNOWN`）。

只读能力不需要第 4 条，第 3 条换成"返回值与 EPLAN 界面或另一条读取路径一致"。

## 5. 目录的布局与模式

### 5.1 布局
```
catalog/
  schema/catalog.schema.json     # JSON Schema（draft 2020-12），定义下面每种文件的一行或整个文档
  eplan-2026.0.3/                # 生成器的输出，不手工修改
    assemblies.json              # 读了哪些文件、程序集版本与文件版本、跳过的原因
    summary.json                 # 按层次、命名空间、种类、对象类型的计数
    types.jsonl                  # 每个公开类型一行，枚举的值写在类型的行里
    members/<命名空间>.jsonl      # 操作层和类型层的成员，每个成员一行
    data-dictionary.jsonl        # 每个属性 ID 一行
```
状态、证据和人工排除**不写进生成目录**，而是放在 `catalog/` 下另外的文件中，按 ID 关联，由映射与报告工作定义；Action 目录同样另外存放。这样重新生成目录不会冲掉人工数据，人工数据中引用了已经消失的 ID 时，报告能发现。

### 5.2 成员与类型的字段
每行一个 JSON 对象，字段顺序固定，取默认值的字段省略：

| 字段 | 说明 |
|---|---|
| `id` | XML 文档注释 ID |
| `kind` | 类型：`class` `struct` `interface` `enum` `delegate`；成员：`method` `property` `operator` `delegate_method` `constructor` `lifecycle` `field` `constant` `event` |
| `layer` | `operation`、`data_dictionary`、`type` |
| `asm`、`ns`、`outer` | 只在类型行：程序集、命名空间、外层类型 |
| `type` | 只在成员行：声明类型的 ID |
| `group` | 操作层的重载组 |
| `access` | 只对派生类可见时为 `protected`，省略即 `public` |
| `static`、`returns`、`params`、`accessors` | 签名：参数名和类型取自元数据；属性的可见访问器 `get`、`set`、`get;set` |
| `value`、`values` | 常量的值；枚举类型的全部值（名称到值） |
| `base`、`interfaces`、`modifiers` | 类型的基类、接口、`abstract`/`sealed`/`static` |
| `documented` | 省略即 true；false 表示 IntelliSense 中没有这个 ID |
| `obsolete`、`excluded` | 过时标记；机械排除的 `{reason, rule}` |

不保存任何文档文字。官方帮助页面的网址规律没有核实，暂不生成链接；来源引用是"程序集 + ID"。

### 5.3 数据字典
`data-dictionary.jsonl` 每行一个属性 ID：`object`、`name`、`value`（属性的数字 ID，来自枚举）、`enum`/`list`/`indexed`（默认 true，缺哪种形式写 false）、`obsolete`、`documented`。成员 ID 由 `summary.json` 中该对象类型的 `propertyList`、`enum` 两个类型推出：`P:<propertyList>.<NAME>`、它的 `(System.Int32)` 重载、`F:<enum>.<NAME>`。

### 5.4 大小
生成的数据共约 3.8 MB（成员 2.6 MB、数据字典 0.8 MB、类型 0.4 MB）。做法：不存文档文字；枚举值并入类型的行；同一属性 ID 的三种形式合成一行；成员行不重复程序集和命名空间；默认值省略。行按 ID 排序，换版本时的差异只出现在变化的行上。

## 6. 目录怎样生成
- `eng/catalog/EplanMcp.Catalog`（net10.0）用 `System.Reflection.Metadata` 读 EPLAN 程序集的元数据表，**不加载、不执行**这些程序集（它们是混合模式的 C++/CLI 程序集，加载就会运行原生代码）；用 IntelliSense 文件判断哪些 ID 有文档。
- 命令：`dotnet run --project eng\catalog\EplanMcp.Catalog -- extract --sdk <EPLAN 的 Bin 目录> --out catalog\eplan-<版本>`。SDK 路径是参数，不写进仓库；输出目录在 SDK 目录之内时拒绝运行，从不往 EPLAN 目录写文件。
- 输出确定：同一份 SDK 两次生成的结果逐字节相同，没有时间戳和本机路径。
- CI 构建和测试生成器（它不引用 EPLAN），但不运行它；生成的数据随 PR 提交，差异由人审阅。
- **版本识别**：`assemblies.json` 记下每个程序集的程序集版本和文件版本（本次全部为 2026.0.3.25421）。每个 EPLAN 版本一个目录，换版本时新建目录，用两个目录的差异生成版本差异报告。

## 7. 映射与证据的来源
- **实现**：读编译后的插件（`EplanMcp.Bridge.Eplan.dll`）的 MemberRef 表，得到它引用的每个 EPLAN 成员的精确 ID，比搜索源代码可靠。再由"工具 → Bridge 操作 → 成员"串起来。
- **Action**：Bridge 中以字符串调用的 action 名，映射到 `A:` 条目。
- **测试**：Small、Medium 测试名；Large 测试名和运行记录。
- **实机证据**：api-notes 的 A、B 条目和 spike；以后新增的 api-notes 条目写明涉及的成员 ID。
- **覆盖报告**：把目录、映射和状态合在一起，按层次、命名空间、对象类型、阶段统计，由程序生成，不手写数字。主分母是 4,342 组；报告同时单独给出只按方法计的数字（Q1），并把"无文档"的 320 组和人工排除的 328 组（第 3.3.1 节）各自单独列出（Q2、Q3）。

## 8. 生成器的边界
- **生成**：目录本身；按签名生成的内部包装（参数和返回值的 DTO、属性读取的样板代码、重载表）；文档中的表格；覆盖报告。
- **手写**（工程语义，生成器给不出来）：
  - 对象标识和重新定位：对象在项目重新打开、撤销之后怎样再找回来（[架构](../architecture/architecture.md) 8.1）；
  - 设备与功能的关系：主功能、辅助功能、设备标识的组成；
  - 多语言值：`MultiLangString` 的语言选择与回退；
  - 单位与坐标：页面坐标、栅格、3D 坐标系和单位；
  - 顺序、前置条件和读回：先锁定、再修改、再读回；
  - 每个 API 的事务、撤销与恢复：能否被 `SafetyPoint` 回滚，是否自己建撤销步骤（api-notes A7、A30）。
- 生成的代码**不直接成为 MCP 工具**：它是任务工具内部的构件，或 ADR-0021 白名单条目的依据。运行时不做反射调用。
- **实现**（第一期）：`eng/coverage` 的 `generate` 按 `catalog/wrappers.yaml` 中列出的目录 ID 生成三种构件：返回值的 DTO 和属性读取代码、方法组的重载表、action 行的参数类；输出提交到仓库，CI 的 `check` 核对，用法见 [catalog/README.md](../../catalog/README.md#内部包装的生成)。生成的代码只在有 Bridge 操作到达时计入覆盖。三个代表性样例：`eplan_placeholder_list`（只读）、`eplan_placeholder_apply`（修改）、`eplan_part_export` 的 `backup` 格式（文件输出），规格在 `docs/tools/`。

## 9. 差距分析（main，2026-10-21）

| 项 | 数字 | 说明 |
|---|---|---|
| 工具 | 47 个（`core` 25） | 不是完成的标准 |
| 插件引用的 EPLAN 类型 | 123 个 | 读 Release 版插件的元数据得到 |
| 插件引用的 EPLAN 成员 | 241 个 | 其中操作层 170 个（169 组），数据字典 27 个属性 ID，类型层 41 个，运算符 3 个 |
| 操作层分母中被引用的组 | **169 / 4,342（3.9%）** | "被引用"只是 `implemented` 的上限，不等于验证过 |
| 数据字典 | 27 个属性 ID 被直接引用 | 另有通用属性读取；按对象类型的验证尚未统计 |
| Action | 99 个 / 177 行 | 按 ADR-0021 的统计（含 M4c 已规划、尚未实现的工具）：任务工具覆盖 61 行（含用 API 代替的 15 行），M4b 计划 19 行，待做 57 行（白名单首批 15 行），有意不做 40 行 |
| 实机证据 | A1–A30、B1–B10 | 按主题，不按成员 ID |

按命名空间（操作层，被引用的组 / 分母组）：

| 命名空间 | 引用 / 分母 | 命名空间 | 引用 / 分母 |
|---|---:|---|---:|
| `DataModel` | 78 / 1,247 | `DataModel.Graphics` | 0 / 414 |
| `HEServices` | 28 / 417 | `DataModel.E3D` | 0 / 368 |
| `Base` | 24 / 268 | `DataModel.Planning` | 0 / 122 |
| `DataModel.EObjects` | 14 / 83 | `EServices.Ged` | 0 / 90 |
| `MasterData` | 12 / 592 | `Gui` | 0 / 72 |
| `EServices` | 7 / 127 | `DataModel.Revisions` | 0 / 20 |
| `DataModel.MasterData` | 5 / 121 | `DataModel.Topology` | 0 / 17 |
| `ApplicationFramework` | 1 / 88 | 远程相关（`RemoteClient`、`GrpcServer`、`Remoting`、`RemoteServer`） | 0 / 253 |

结论：
- 读、写的主干（项目、页、设备、连接、部件、检查、导出）已有，但只占分母的很小一部分；图形、3D、拓扑、预规划、修订（main 上）、图形编辑器交互完全没有引用。
- 没有任何一项能力有成员一级的状态和证据；现有的实机证据要先按成员 ID 重新索引，才能计入 `live_verified`。
- 分母中 253 组属于远程相关，架构上不用（ADR-0002），72 组属于界面（`Gui`），3 组属于脚本（ADR-0005 的 Code 级）；按 Q3 的决定，它们在映射数据中逐条标 `excluded` 并写明理由（第 3.3.1 节），而不是留在 `registered`。

## 10. 版本、模块与授权的识别
- **版本**：目录按版本分目录（第 6 节）；运行时 Bridge 报告 EPLAN 的版本和变体，状态按版本记（第 4 节）。
- **模块与授权**：运行时用 `License` 和 `LicenseOptions` 查询（api-notes A2、B8）。`LicenseOptions` 有 3,214 个值，它们在类型层，用于把能力与授权模块对应起来；对应关系写在映射数据中。
- **本机环境**：EPLAN 2026.0.3（Electric P8 与 Pro Panel 变体）有 API 扩展授权；**没有 Preplanning 授权**，预规划相关能力为 `env_blocked`；Pro Panel 3D 授权**未核实**，第 5 阶段开始前先核实；没有其他 EPLAN 版本。

## 11. 阶段计划

| 阶段 | 范围 | 依赖 | 验收标准 |
|---|---|---|---|
| 1 | 本设计：目录、映射、最小生成器、代表性样例、覆盖报告、阶段计划 | — | 目录可重复生成且已提交；main 上每个工具都映射到目录 ID；覆盖报告由程序从目录和映射生成；第 9 节的数字可由报告复现 |
| 2 | Action 的各分支：177 行全部进入 Action 目录；ADR-0021 的首批 15 行与"以后"30 行 | 1 | 每一行都有非 `registered` 的状态；进入白名单的条目有 spike 证据 |
| 3 | DataModel、Graphics 与属性：符号的新建、放置、移动、旋转、删除，属性的显示（属性放置），连接的生成与检查；69 个对象类型的通用属性读写 | 1 | 范围内每个重载组与对象类型都有非 `registered` 的状态；写能力满足 4.1 |
| 4 | 部件、宏、符号（主数据），PLC 数据交换，翻译，修订 | 1、3（共用属性访问与放置语义） | 同上；主数据写入按 ADR-0019 |
| 5 | Pro Panel：E3D、Topology、布线、生产输出 | 1、3；Pro Panel 3D 授权核实 | 同上；授权缺失时为 `env_blocked` 并写明条件 |
| 6 | Planning（预规划） | 1；Preplanning 授权 | 有授权前全部为 `env_blocked`；有授权后同第 3 阶段 |
| 7 | 事件、交互、自定义检查、转换器、插件扩展；`Gui` 的 72 组逐项复审（Q3） | 1；**新的 ADR** | 见下 |

- 第 7 阶段要把 EPLAN 的事件从 Bridge 推给 Host，再变成 MCP 通知。[ADR-0003](../adr/0003-ipc-named-pipe-json-rpc.md) 中 Bridge 只在请求进行中发 `progress` 通知，没有不请自来的事件，所以这改变 IPC，属于 L3，**需要单独的 ADR**，不在本设计中决定。自定义检查和转换器要在 EPLAN 中注册扩展，同样先写 ADR。
- 每个阶段开始前写自己的设计文档；"非 `registered` 的状态"包括 `excluded` 和 `env_blocked`，但都要有理由。

## 12. 工具面怎样承载这些能力
任务工具仍是主要的工具面（ADR-0006）。能力越多，工具越多，[ADR-0022](../adr/0022-domain-toolsets.md)（已接受）规定按领域分成工具集，按组加载、按需发现，并照顾不支持 `notifications/tools/list_changed` 的客户端。覆盖率不要求每项能力有自己的工具：一项能力可以经任务工具的内部步骤、白名单条目或通用属性读写到达。

## 13. 安全与数据完整性
- 本设计不新增运行时能力。生成器只读 EPLAN 目录，从不加载 EPLAN 程序集，也不往 EPLAN 目录写文件。
- 目录中没有文档文字、授权信息和本机路径（CLAUDE.md 规则 4）。
- 以后的包装和工具照常按 ADR-0005 分级；目录中的一项能力，不因为"登记了"就可以被调用。

## 14. 备选方案
1. **只用 IntelliSense 统计**：否决。XML 包含非公开成员和原生类型，没有可见性和完整签名，数字偏大（第 3.4 节）。
2. **`MetadataLoadContext` 或 Roslyn**：否决。前者要为每个引用（含 .NET Framework 的 `mscorlib`）配置解析，后者要构造编译；直接读元数据表就够了，也没有新依赖。
3. **加载程序集后用反射**：否决。会执行 C++/CLI 的原生初始化代码。
4. **每个 API 一个工具**：否决（ADR-0006、ADR-0021 方案 4）。
5. **把目录放进数据库**：否决。文本行的差异便于审阅。

## 15. 测试与验证
- Small：文档注释 ID 的拼写规则（数组、泛型、指针、修饰、运算符）、分层与排除规则。
- Medium：用测试程序集自身当作"SDK"，生成器算出的 ID 与 C# 编译器生成的 XML 文档逐一相等；两次生成结果相同。
- 数据检查：77 个无法对应的 IntelliSense ID 逐个看过（第 3.4 节）；随机抽查已知成员（如 `Translate.TranslateObjects`、`ProjectPropertyList.PROJ_REVISION_LOG_NAME`）都在目录中。

## 16. 待定问题与决定

| # | 问题 | 结论 / 建议 |
|---|---|---|
| Q1 | 对象属性是否计入操作层分母？ | **已决定**（2026-10-21，项目负责人选择"算，分母 4,342 组"）：**计入**。对象属性（移动、旋转、改名等 setter）算操作；主分母是 4,342 个重载组（方法 2,003 + 属性 2,339）；报告仍单独给出只按方法计的数字。理由见第 3.2 节 |
| Q2 | 公开但没有文档的成员（分母中 320 组）默认登记还是排除？ | **已决定**（2026-10-21，项目负责人选择"留在分母里，标“无文档”"）：**留在分母里**，标 `documented: false`，在报告中单独列出；使用前先用 spike 验证行为。其中 186 组同时属于 Q3 的排除范围（第 3.4 节） |
| Q3 | 远程相关（253 组）、`Gui`（72 组）、`Scripting`（3 组）是否整体排除？ | **已决定**（2026-10-21，项目负责人选择"排除并写明原因"）：**整体排除并写明原因**。远程相关 253 组：架构上不用（ADR-0002）；`Gui` 72 组：需要有人操作界面，第 7 阶段设计交互支持时逐项复审；`Scripting` 3 组：Code 级，默认关闭（ADR-0005）。见第 3.3.1 节 |
| Q4 | 约 3.8 MB 的生成数据提交到仓库是否可以？ | **已决定**（2026-10-21，项目负责人选择"可以"）：**可以**。不计入 PR 行数，换版本时只有变化的行出现在差异中 |
| Q5 | 各阶段是否设覆盖率的数值门槛？ | **已决定**（2026-10-21，项目负责人选择"不设百分比"）：**不设百分比**。阶段完成的条件是范围内没有停在 `registered` 的项 |

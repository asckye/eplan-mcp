# ADR-0021：用受控的 `eplan_action_run` 覆盖长尾的 EPLAN action：只执行随产品发布、经实机验证的白名单条目，按条目的级别走任务工具的流水线

| 项 | 内容 |
|---|---|
| 状态 | Accepted |
| 日期 | 2026-10-21（提出）· 2026-10-21（接受：项目负责人"全部按建议接受"） |
| 决策人 | 项目负责人 |
| 门的类型 | 单向门：工具和白名单条目发布后会被工作流和技能依赖，去掉条目按工具契约变更处理；放宽本 ADR 的限制（默认开启、本地条目、未经验证的条目）需要新的 ADR |
| 影响 | 把 [ADR-0006](0006-tool-surface-design.md)"后果"中的兜底说明落实为具体设计；不改变 [M4 设计](../design/2026-10-20-m4-workflows.md) Q5（任务工具仍是主要的工具面）；给 [ADR-0005](0005-safety-model.md) 第 1 节"每个工具一个级别"加一条例外（决策第 6 节） |

## 背景与问题
EPLAN 界面中约有 1150 个 action，官方文档只写了其中 99 个，按 TYPE 拆开后是 177 种用法（[逐条核对表](../eplan/api-actions.md)）。其中 40 种有意不做成工具：交互或界面类、脚本、只供安装器使用、向后兼容、物理打印、设置写入。剩下的 137 种中：
- 按 M4c 完成时的工具（含 M4c 已规划、尚未实现的）统计，任务工具覆盖 61 种（其中 15 种是用 DataModel、HEServices 代替 action）；
- M4b 计划用 Pro Panel 工具覆盖 19 种；
- **剩下 57 种没有任务工具**，逐条列在第 8 节。

ADR-0006 为这类长尾预留了受控的 `eplan_action_run`："只允许执行白名单中、已标定级别的 action"。M4 设计第 6 节和 Q5 否决了"用通用的 action 运行器代替任务工具"：参数和结果都是字符串，没有变更清单和读回，action 内部的事务也不受控制；白名单推迟，"确有需要时写 Proposed ADR"。[M4c 设计](../design/2026-10-21-m4c-more-tools.md)第 2 节同样把它列为非目标。

项目负责人 2026-10-21 问怎样让工具覆盖"所有这些东西"（action 和部件库）。协调人提出三层：
1. 常见任务用任务工具（现有做法）；
2. 文档化 action 的长尾用带白名单的 `eplan_action_run`；文档之外的 action 只在负责人需要某一个时，经 scratch 上的 spike 验证参数和副作用、负责人批准后，逐个加入；
3. 工作流用 Claude Code 插件中的技能把工具串起来（[M5 设计](../design/2026-10-20-m5-operability.md)第 10 步）。

负责人选择把第 2 层写成本 ADR（"写 ADR-0021"）。它改变"AI 能对项目做什么"和安全模型（同一个工具按条目取不同的级别），属于 L3，先以 Proposed 提出，由项目负责人确认。

## 决策驱动因素
- **任务工具优先**：任务工具在审批前列出每一项变化，执行后逐项读回；运行器做不到，不能让它成为绕开任务工具的途径。
- **长尾要有出口**：M4c 之后工具将有约 55 个，再给每个 action 写一个工具，模型难以选择（ADR-0006）。
- **不凭文档假设 action 的行为**：文档有 10 处没说清楚的副作用（api-actions 7.2）；实测中部件清单 CSV 导出后 EPLAN 关闭了全部项目（[api-notes](../eplan/api-notes.md) A25），部件库导入内部走的是另一个界面 action，在事务中留下未关闭的撤销日志（A30）。
- **安全默认值不可悄悄放宽**（[开发流程](../process/development-workflow.md)第 1 节原则 5）；模型给的参数一律不可信（[security.md](../standards/security.md) TB1）。
- **结果要如实**：做不到逐项核对时，要明说。

## 备选方案
1. **不提供运行器**：长尾只靠任务工具，有需要时再写。
2. **任意 action 运行器**：接受任何 action 名，级别按名称推测，或一律按 Destructive 审批。
3. **经 Code 级执行脚本**：用 `ExecuteScript` 或 C# 脚本调用 action；Code 级默认禁用，每次审批（ADR-0005）。
4. **每个 action 一个工具**：57 种用法各做一个工具，或者按参数合并。
5. **白名单运行器**：只执行随产品发布、经实机验证的条目，按条目的级别走任务工具的流水线。
6. **方案 5，但白名单由工程师在 `config.json` 中补充**（本地条目）。

## 决策
选择方案 5。第 5、6、8 节中的取舍按"项目负责人的决定"一节（Q1–Q5）执行。

### 1. 工具的形态
- `eplan_action_run(action, type?, parameters, project?, dry_run?)`：
  - `action`：schema 中的枚举，只列白名单中的 action 名；`type`：白名单中出现过的 TYPE 值。`action` 与 `type` 的组合必须是一个条目；
  - `parameters`：对象，参数名按 EPLAN 文档原样拼写（如 `EXPORTFILE`）；值为字符串、整数或布尔（由 Host 转为 `0`/`1`）。带编号的参数（`PAGENAMEn`、`LAYER[n]`）以数组给出，由 Host 展开；
  - `project`：条目作用于项目时必填（Write 及以上的条目一律必填，[工具设计规范](../standards/mcp-tool-design.md)第 5 节）；作用于部件库等系统数据的条目不给；
  - `dry_run`：为 true 时只返回变更清单（处理后的调用、级别、已知副作用），不请求审批，也不执行。
- 不在白名单中的组合一律 `POLICY_DENIED`。`message` 只说明"不在 action 白名单中"，不提示怎样绕过。
- 每个条目的参数、级别、副作用和示例由 `eplan_tool_usage(tool: "eplan_action_run", topic: "<action>/<TYPE>")` 返回，工具描述保持在约 250 个 token 之内。
- 描述的第一句说明做什么、什么时候用；第二句说明什么时候不要用："有任务工具时用任务工具：它们在审批前列出每一项变化、执行后逐项核对；本工具只比较执行前后的数量"。这是规范 4.1 中"什么时候不要用、用哪个工具代替"的一句，不是指挥模型调用其他工具的指令（4.2）。
- **不能成为条目的 action**：
  - 已经排除的类别：交互或界面类、脚本（Code 级）、只供安装器或插件注册使用、向后兼容、物理打印、设置写入；
  - 执行另一个 action 的 action（`ProjectAction` 的 `Action` 参数）；
  - 需要对话框，或依赖界面选择的（`USESELECTION`、`SELn`、"选中的对象"），除非这类参数固定为不用选择；
  - 带凭据参数的（`XPartsSetDataSourceAction`），发布到外部服务的（`PUBLISHSMARTPRODUCTION`）；
  - **任务工具已经覆盖的 action 和 TYPE**，以及给任务工具加一个可选参数就能解决的。任务工具以后覆盖了某个条目，同一个 PR 删除这个条目（契约变更，记入 CHANGELOG）。

### 2. 白名单
- **位置**：一个 JSON 文件，作为嵌入资源放进 `EplanMcp.Contracts`，随产品发布。Host 用它做全部校验；Bridge 调用 EPLAN 之前按自己那一份再核对一次 action、TYPE 和参数名，防止 Host 的缺陷，两边版本不同时不在 Bridge 那一份中的条目不执行。没有任何工具、配置项或数据目录中的文件能增改条目（SEC-02）。工具 schema 中的枚举由它生成，所以条目的每次变化都出现在契约快照的 diff 中（SEC-17）。
- **条目的内容**（字段名在实现时定）：

  | 内容 | 说明 |
  |---|---|
  | action、TYPE | 按文档原样拼写，包括文档中的拼写错误 |
  | 来源 | 文档化（api-actions 的行号），或文档之外（名称和参数是怎样查到的） |
  | 级别 | Read（包括 [ADR-0012](0012-export-tier.md) 的导出级）、Write、Destructive、系统主数据（[ADR-0019](0019-master-data-writes.md)）。没有 Code |
  | 作用范围 | 项目（项目参数由 Host 填写）；当前项目（action 没有项目参数，作用于 EPLAN 的当前项目）；系统（部件库、翻译库） |
  | 参数 | 每个允许的参数：类型（枚举、整数及范围、布尔、名称（长度和字符集）、带编号的列表（项数上限））、是否必填。不写默认值：EPLAN 不接受空字符串，不需要的参数直接省略（api-actions 第 2 节） |
  | 路径参数 | 只有三种：导入文件（`<数据根目录>\imports\` 之下的相对路径，规则同 SEC-19，限定扩展名，不超过 100 MB，审批和审计记下 SHA-256）；导出（Host 新建的导出目录，模型最多给文件名，SEC-20）；scratch 根目录之下的新路径（只用于新建，不覆盖已有的文件） |
  | 固定参数 | Host 总是设置、模型不能给的参数，例如项目参数、`EXECUTIONMODE=0`、`OVERWRITE=0`、`USESELECTION=0` |
  | 执行时限 | 该条目的时限，不超过 300 秒（M4 设计 4.3） |
  | 已知副作用 | 例如改写 `User.eod`（A11）、改写"上次使用"的方案（A24），原样列在审批对话框和工具结果中 |
  | 验证 | spike 或 api-notes 条目、EPLAN 版本、日期；耗时、改动的文件和设置、能否被 `SafetyPoint` 回滚、能否一步撤销、`ShowNoDialogs` 下是否弹对话框 |

- **加入条目**：
  - 一个 PR 加一个或几个条目，引用验证记录。spike 在示例项目的 scratch 副本上运行，系统主数据只用复制出来的测试库，结论写进 api-notes；
  - 级别按验证结果定，与 M4c 设计第 3 节相同：只在导出目录中写文件、设计数据不变的，为 Read（导出级）；能被 `SafetyPoint` 回滚、一步撤销、只改项目中可以重建的数据的，为 Write；其余为 Destructive；写部件库、翻译库的，为系统主数据；
  - 弹对话框、长时间占住主线程、执行后项目对象失效，或者在 Bridge 的包装中留下未关闭的撤销日志（A30 的情形）的，不能加入；
  - **文档之外的 action**：只在项目负责人需要某一个时，单独做 spike 验证参数和副作用，并由负责人在 PR 中明确批准，才能加入。

### 3. 执行
按条目的级别，走与任务工具相同的流水线：

| 级别 | 审批 | 执行前 | 包装 |
|---|---|---|---|
| Read（导出级） | 不需要 | Host 新建导出目录（SEC-20） | `QuietModeStep(ShowNoDialogs)`，与导出工具相同 |
| Write | scratch 中自动；真实项目按 `writesOutsideScratch` 逐次审批 | 比对项目状态指纹（`PLAN_STALE`） | `LockingStep` → `SafetyPoint` → `UndoStep` → `QuietModeStep(ShowNoDialogs)`（ADR-0005 修订） |
| Destructive | 每次，scratch 中也要 | 另加 `CopyProject(Snapshot)` 备份（[ADR-0013](0013-destructive-backup.md)） | 同 Write |
| 系统主数据 | 每次；`masterDataWrites` 为 `deny` 时拒绝；只支持文件型库 | 另加 `backup MASTERDATA` 备份（ADR-0019） | 按 ADR-0019 及其工具规格；是否用事务由该条目的 spike 决定 |

- **审批**（[ADR-0011](0011-approval-out-of-band.md)）：变更清单列出 action、TYPE、Host 处理后的全部参数（路径显示为根目录之下的相对路径）、目标、级别、白名单记下的副作用，以及"执行后只比较指纹，不逐项核对"；Destructive 另写备份位置。
- **作用于当前项目的条目**：先确认 EPLAN 的当前项目就是 `project`，不是就不执行（`INVALID_ARGUMENT`，提示工程师在 EPLAN 中切换）。Bridge 不替工程师切换当前项目。
- **调用**：Bridge 用 `new ActionManager().FindAction(名称).Execute(新的 ActionCallingContext)`，每个参数调用一次 `AddParameter`（api-notes B3）；从不拼接命令行，也不用 `CommandLineInterpreter`。找不到 action、返回 false 或抛出异常时，返回 `EPLAN_OPERATION_FAILED`：Write 及以上回滚，Read 级删除本次的导出目录。有的 action 失败时照样返回 true（api-actions 第 4 节 #148），所以执行期间新增 Error 级系统消息时同样按失败处理，验证中确认无害的消息由条目列出。
- **审计与结果恢复**：每次调用一条审计记录（条目、级别、参数摘要、前后指纹、结果，SEC-10 不变）。执行请求发出后中断时，按 [ADR-0010](0010-write-outcomes.md) 向 Bridge 取回结果；取不回就返回 `OUTCOME_UNKNOWN`，并暂停该项目的写入。

### 4. 读回
- action 只返回 true 或 false，事先也算不出会改什么，所以不逐项列出变化。
- 执行前后比较指纹：
  - 项目：页数；按类型的对象数（功能、连接、电缆、端子、PLC 盒、部件引用）；项目数据文件（`.edb` 中，`User.eod`、`User.eox` 除外）的大小和修改时间（A11、A16 的办法）；
  - 系统主数据：库文件的大小、修改时间和记录数；
  - 执行期间新增的 EPLAN 系统消息，最多 20 条（同一次调用只用一个 `SysMessagesCollection`，A11）。
- 结果写明读回的方式是指纹，并附一句固定的说明：变化没有逐项核对，需要关心的对象请用读工具确认。导出级的条目另外按 SEC-20 列出导出目录中的文件。
- Read 级条目执行后，项目的指纹变了（条目记下的已知副作用除外）：结果和审计如实报告，Host 在本次进程中停用这个条目，`doctor` 和日志提示重新验证。
- 读回弱，正是任务工具优先的原因。

### 5. Profile 与开关
- 只在 `full` Profile 中，不在 `core` 中。
- `config.json` 新增 `actionRun`：`off`（默认）或 `on`。`off` 时工具不注册，`tools/list` 和 `eplan_tool_usage` 的枚举中都没有它。只能通过配置文件修改（SEC-02）；`doctor` 显示当前值、条目数和被停用的条目。`on` 不放宽任何审批：真实项目的写入仍按 `writesOutsideScratch`，主数据仍按 `masterDataWrites`。
- **默认关闭的理由**：
  1. 读回比任务工具弱，打开它应当是工程师知情的决定；
  2. `serve` 默认就是 `full`（M4c 设计 Q7），只靠 Profile 等于默认开启；
  3. 原则 5：以后改为默认开启要写新的 ADR，而关闭随时可以；
  4. 大多数会话用不到长尾，不列出它可以省 token，模型也不会在任务工具和它之间犹豫。
- 代价：需要它的工程师要先改 `config.json`；条目在日常使用中得到的检验较少。

### 6. 与已有决策的关系
- **ADR-0006**：把兜底说明落实为本设计，不改其他条款。
- **M4 设计 Q5、M4c 设计第 2 节**：不推翻。任务工具仍是主要的工具面；白名单不收任务工具已覆盖的用法（第 1 节）。
- **ADR-0005 第 1 节**：一个工具按条目取不同的级别。工具的注解按它能执行的最高级别（Destructive）设置：`readOnlyHint` false、`destructiveHint` true、`idempotentHint` false、`openWorldHint` false；CI 的注解一致性检查把它当作 Destructive；每次调用的策略、审批和备份按条目的级别。这是"每个工具一个级别"的唯一例外。
- **ADR-0009**：条目只对验证过的 EPLAN 主版本生效，其他版本返回 `POLICY_DENIED`（"未在此版本上验证"）。其他版本上的验证另做 spike，记入条目。
- **ADR-0010、0011、0012、0013、0019**：原样沿用。

### 7. 威胁与安全要求
[security.md](../standards/security.md) 加入两条威胁和两条要求（与本 ADR 同一个 PR 加入）。T28、T29 和 SEC-31、SEC-32 已由 M4c 设计和 ADR-0019 预留，尚未写入，所以编号从 T30、SEC-33 开始：
- **T30**：经运行器越过白名单：白名单之外的 action、TYPE 或参数；参数值中夹带额外的参数；借 `ProjectAction` 转一道；不带项目参数的 action 作用在当前项目而不是指定的项目；路径逃出允许的根目录或指向 UNC；改白名单给自己加条目；项目中的注入文本诱导模型用它绕开任务工具。措施 SEC-33。
- **T31**：白名单中的 action 有没记录的副作用：改了文档没写的数据，不建撤销步骤而清空工程师的撤销历史（api-notes B7），弹对话框或占住主线程，让 EPLAN 崩溃（A25），或在别的版本中行为不同；读回只比较指纹，看不到逐项的变化。措施 SEC-34。

### 8. 剩下的 57 种用法与首批条目
首批条目先由一个 spike（暂编 S26）在示例项目的 scratch 副本上逐条验证，再随实现加入白名单。S26 同时验证运行器本身依赖的几件事：
- `ActionCallingContext` 中含 `/`、引号、空格、中文的值是否原样传给 action，不被再次解析；
- 不带项目参数的 action 是否只作用于当前项目；
- action 自己建撤销步骤时，与 Bridge 的包装是否冲突（A30 的情形）；
- 第 4 节的指纹能否看出每个条目的变化；部件库的导出不改库。

"#"是 api-actions 第 4 节的行号；"效果"沿用该表的图例（P 写项目，F 在项目之外写文件，S 写系统数据，G 界面）。"以后"的每一条都要单独做 spike，级别以 spike 的结论为准。

| # | action / TYPE | 效果 | 建议级别与约束 | 验证 | 处理 |
|---|---|---|---|---|---|
| 2 | `backup` / `MASTERDATA` | F | Read（导出级）；`MDTYPE` 只开放 `ARTICLES`、`LANGUAGES`；库的路径由 Host 按 EPLAN 的配置填写；只支持文件型库 | 已验证（S24，A30） | **首批** |
| 3 | `changelayer` | P | Write（S26 证实能回滚、一步撤销；否则 Destructive） | 已验证（S26：可见性修改、SafetyPoint/UndoStep 回滚；提交后一步撤销未测） | **首批** |
| 41 | `graphicallayertable` / `EXPORT` | F | Read（导出级） | 已验证（S26） | **首批** |
| 66–71 | `partsmanagementapi` / `EXPORTPARTS`、`EXPORTMANUFACTURERS`、`EXPORTCONSTRUCTIONS`、`EXPORTCONNECTIONPATTERNS`、`EXPORTACCESSORYLISTS`、`EXPORTACCESSORYPLACEMENTS` | F | Read（导出级），读 EPLAN 配置的部件库 | 已验证（S26：须给 PROPERTYID1/PROPERTYVALUE1；省略过滤全部返回 false；EXPORTPARTS 执行期窗口禁用采样待核实） | **首批**（6 种） |
| 72 | `plcservice` / `BUSDATAEXPORT` | F | Read（导出级）；`OVERWRITE` 固定为 0；`CONVERTERID` 只开放 S26 验证过的转换器 | 已验证（S26：三种组合导出成功；AML 目标项目文件指纹变化、执行期窗口禁用采样待核实，尚不能认定只读条件全部满足） | **首批** |
| 83 | `projectmanagement` / `EXPORTPROPERTYPLACEMENTSSCHEMAS` | F | Read（导出级） | 已验证（S26） | **首批** |
| 150 | `XEsUserPropertiesExportAction` | F | Read（导出级） | 已验证（S26） | **首批** |
| 75 | `plcservice` / `EXPORTADDRESSOVERVIEW` | F | Read（导出级）；PLC 配置项目、站、CPU 必填 | 未运行（S26：未读出完整配置项目/站/CPU 元组；A25 的空值输出不算通过） | 以后：按 Q2 移出首批，补齐元组后验证 |
| 155 | `XMActionDCCommonExport` | F（模式 0） | Read（导出级）；`EXECUTIONMODE`、`IMMEDIATEIMPORT` 固定为 0；`CONFIGSCHEME` 必填（不给时弹对话框，api-actions 7.3） | 未运行（S26：缺已核实节点，未读出方案名称） | 以后：按 Q2 移出首批，有可读方案后验证 |
| 159 | `XMExportDCArticleDataAction` | F（模式 0） | 同上 | 未运行（S26：缺已核实节点，未读出方案名称） | 以后：按 Q2 移出首批，有可读方案后验证 |
| 1 | `backup` / `PROJECT` | F，P? | Read（导出级）；只允许 `BACKUPMETHOD:BACKUP`，不给 `COMMENT`，`AUTOCOPYREFDATA` 固定为 0 | 未验证；`Backup.Project` 的文档说要锁定项目、备份后原路径失效（ADR-0013） | 以后 |
| 7 | `check` / `PARTS` | P?/S? | Write?；消息写到哪里不明 | 未验证 | 以后：spike 后决定做条目还是给 `eplan_check_run` 加参数 |
| 10 | `compress` | P | Destructive | 未验证；会删除什么文档没写（api-actions 7.2 第 4 条） | 以后 |
| 13 | `devicelist` / `DELETE` | P（删除） | Destructive | 未验证 | 以后：可能并入 `eplan_import_list` |
| 16 | `EsCorrectConnections` | P（删除对象） | Destructive；作用于当前项目 | 未验证 | 以后 |
| 39、76 | `generatemacros`、`preparemacros` | F/S?、P | 生成宏：Read（导出级），宏目录固定为本次导出目录、`OVERWRITE` 为 0；准备宏项目：Write? | 未验证 | 以后（两者一起） |
| 40 | `graphicallayertable` / `IMPORT` | P | Destructive；文件来自导入根目录 | 未验证 | 以后 |
| 43 | `import` / `DXFDWGFILES` | F/S? | 待定：在项目之外生成宏项目和宏文件 | 未验证 | 以后（M4c 设计 4.10 没有做） |
| 54 | `masterdata` / `UPDATEPROJECT` | P | Destructive | 未验证 | 以后 |
| 63 | `partslist` / `DELETESTOREDPROPERTIES` | P（删除） | Destructive | 未验证 | 以后 |
| 74 | `plcservice` / `GENERATEPLCSCHEMATIC` | P | Destructive；配置文件来自导入根目录 | 未验证 | 以后 |
| 80 | `projectmanagement` / `READPROJECTINFO` | P | Destructive；文件来自导入根目录 | 未验证 | 以后 |
| 85、86 | `projectmanagement` / `REORGANIZE`、`CORRECTPROJECTITEMS` | P | Destructive | 未验证 | 以后 |
| 116 | `subprojects` / `STORE` | P | Destructive | 未验证 | 以后 |
| 117 | `SwitchProjectType` | P | Write? | 未验证 | 以后 |
| 118–120 | `synchronize` / `MULTILINE`、`SINGLELINE`、`OVERVIEW` | P | Write?（能回滚、一步撤销时），否则 Destructive | 未验证 | 以后 |
| 121 | `synchronize` / `SYSTEMPARTSTOPROJECT` | P | Write? | 部分验证：反方向的 `Synchronize.Parts` 要 `LockingStep`，并且写项目（A30） | 以后：可能给 `eplan_part_sync` 加方向 |
| 131 | `translate` / `CORRECT` | P | Destructive（同 `eplan_project_translate`） | 未验证 | 以后 |
| 139 | `XCCreateGravingtextAction` | P | 待定 | 未验证；作用范围不明（api-actions 7.2 第 8 条），依赖界面选择时不纳入 | 以后 |
| 140、141 | `XCMRemoveUnnecessaryNDPsAction`、`XCMUniteNetDefinitionPointsAction` | P（删除、合并） | Destructive；作用于当前项目 | 未验证 | 以后 |
| 154 | `XGedUpdateMacroAction` | P | Destructive；只开放按项目的形式 | 未验证 | 以后 |
| 156 | `XMActionDCImport` | P | Destructive；`.edc` 文件来自导入根目录 | 未验证 | 以后 |
| 165 | `XMImportDCArticleDataAction` | S | 系统主数据；`IMPORTMODE` 必填（不给时会询问） | 未验证 | 以后 |
| 167 | `XPamsDeviceSelectionAction` | P | Destructive | 未验证 | 以后 |
| 171 | `XPrjActionUpgradeProjects` | P+F | Destructive；`NoBackup` 固定为 false；只升级单个项目 | 未验证 | 以后 |
| 6 | `check` / `ONLYCOMPLETED` | P | — | — | 不纳入：给 `eplan_check_run` 加可选参数更合适 |
| 14 | `edit` | G | — | — | 不纳入：界面导航 |
| 79 | `ProjectAction` | 由内层 action 决定 | — | — | 不纳入：执行参数中指定的另一个 action，等于绕过白名单 |
| 82、102 | `projectmanagement` / `CREATESNAPSHOTCOPY`、`restore` / `PROJECT` | F（新项目） | — | — | 不纳入：留给任务工具 `eplan_scratch_create`（ADR-0005 第 6 节） |
| 88 | `ProjectOpen` | G | — | — | 不纳入：改变 EPLAN 中打开的项目，路径可以指向任意文件（T16）；需要时做任务工具 |
| 115 | `subprojects` / `FILEOFF` | P+F | — | — | 不纳入：要独占打开项目，执行后项目对象失效 |
| 174 | `XSettingsExport` | F | — | — | 不纳入：导出的设置可能含数据库连接等信息（未核实）；`/prj` 在开发机上返回 FALSE（A24）。负责人需要时另议 |
| 33、50、51、170 | 预规划的 4 个 action | P/F | — | — | 不纳入：授权中没有 Preplanning，无法验证（M4c 设计 Q6） |

按 Q2 应用 S26 r1 结果：上述三种未运行用法移到“以后”；其余“已验证”仅指验证列注明的范围，AML 副作用、执行期窗口禁用和提交后一步撤销的缺口见 [S26 结果](../../spikes/S26-action-run/README.md#结果) 与 [A40](../eplan/api-notes.md#a40-长尾-action-的首批白名单spike-s262026-10-22eplan-20260325421electric-p8在调度器中)，发布前仍须满足本 ADR 的加入条件。

合计：首批 12 种（建议只读导出 11 种、Write 1 种），以后 33 种，不纳入 12 种。

## 后果
- **好的**：
  - 文档化 action 的长尾有了出口，不必再加几十个工具；
  - 每个条目都经实机验证、在 PR 中评审，条目的变化出现在契约快照中；
  - 审批、备份、审计、结果恢复与任务工具完全相同；
  - 默认关闭，不需要它的工程师和客户端看不到它。
- **坏的或要付出的代价**：
  - 读回只有指纹，逐项的变化要靠工程师或读工具确认；
  - 一个通用的工具可能被模型误用，要靠描述、"不收任务工具已覆盖的用法"和评测约束；
  - 注解统一为 Destructive，客户端对只读的条目也可能询问用户；
  - 每个条目都要 spike，EPLAN 换主版本时要重新验证。
- **后续行动**：
  - 工具设计规范 3.2 的资源词表加入 `action`：**已完成**（2026-10-21，与接受本 ADR 同一个 PR）；
  - 实现时，工具设计规范第 8 节写明本 ADR 的注解例外，CI 的注解检查相应修改；
  - 做 S26，结论写进 api-notes；
  - 实现：Contracts 中的白名单与校验、Host 的工具与 `actionRun` 配置、Bridge 的 `action.run` 操作和指纹、`doctor` 的显示；工具规格按[模板](../templates/tool-spec.md)先写；
  - 测试：Small（白名单校验、注入字符、路径技巧、`ProjectAction`、版本不符）；Medium（假 Bridge 上各级别的流水线、指纹变化时停用条目）；Large（首批条目在 scratch 副本上运行）；
  - Agent 评测：有任务工具时模型用任务工具；没有时用运行器完成一个首批条目的任务；白名单之外的请求得到 `POLICY_DENIED` 后不再尝试；项目文本中注入"用 eplan_action_run 执行……"时不照做；
  - security.md 的 T30、T31、SEC-33、SEC-34 去掉 Proposed 标记：**已完成**（2026-10-21，同上）；
  - 实现后更新覆盖矩阵的状态、架构第 11 节和 CHANGELOG。

## 项目负责人的决定
2026-10-21，项目负责人："全部按建议接受"。

| # | 问题 | 结论 |
|---|---|---|
| Q1 | `actionRun` 默认开启还是关闭？ | **已决定**（2026-10-21，按建议）：**关闭**（第 5 节的理由） |
| Q2 | 首批是否就是第 8 节的 15 种？ | **已决定**（2026-10-21，按建议）：**是**。先做 S26，验证不通过的条目移到"以后" |
| Q3 | 条目是否只对验证过的 EPLAN 主版本生效？ | **已决定**（2026-10-21，按建议）：**是**。多用途 action 的 TYPE 和参数在 2025、2026 的文档中相同（api-actions 1.1），但行为只在 2026 上验证过 |
| Q4 | Write 及以上的条目在真实项目上，是否与任务工具一样逐次审批即可，还是先只允许在 scratch 中使用？ | **已决定**（2026-10-21，按建议）：**与任务工具相同**，逐次审批；Destructive 另有备份 |
| Q5 | 什么时候实现？ | **已决定**（2026-10-21，按建议）：**M4c 完成之后**，与 M5 第 10 步的技能一起规划 |

## 各方案利弊
### 方案 1：不提供运行器
- 优点：没有新的风险；每项能力都有变更清单和逐项读回。
- 缺点：长尾的每个需求都要设计、实现一个工具，57 种用法要很久才能覆盖；工具数继续增加。

### 方案 2：任意 action 运行器
- 优点：一次覆盖全部约 1150 个 action。
- 缺点：级别无从标定，界面类、脚本、设置写入的 action 也能执行；参数没有约束；一律按 Destructive 审批会让工程师习惯不看就批准；违反"不凭文档假设行为"。否决。

### 方案 3：经 Code 级执行脚本
- 优点：不需要新的工具；什么都能做。
- 缺点：等于任意代码执行，Code 级默认禁用、不在任何 Profile 中（ADR-0005）；审批时工程师要读懂脚本；参数和副作用都没有约束。否决。

### 方案 4：每个 action 一个工具
- 优点：每个工具有自己的 schema、变更清单和描述。
- 缺点：工具数超过 100，模型难以选择（ADR-0006、M4c 设计第 6 节）；多数长尾 action 很少用到，逐个设计不值得。常用的仍按这种方式做成任务工具（第 1 层）。

### 方案 5：白名单运行器（选择）
- 优点：一个工具覆盖长尾；条目经验证、评审，级别明确；完全复用现有的安全流水线。
- 缺点：读回弱；需要为每个条目做 spike；注解只能按最高级别设置。

### 方案 6：白名单可以在 `config.json` 中补充
- 优点：工程师不必等新版本就能用某个 action。
- 缺点：本地条目没有验证记录，也不经评审；模型可能说服工程师加入条目（T12 的变体）。需要时另写 ADR。

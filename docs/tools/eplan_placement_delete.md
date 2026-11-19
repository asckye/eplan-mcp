# 工具规格：`eplan_placement_delete`

注释类型 HyperLink、QRCode、Spline、Arc、DimensionItem、DimensionCircle、PlaceHolder、MacroBox 已支持。实际删除在执行事务内重新解析定位器，不保留预检阶段的原生包装对象。

| 项目字段 | 记录 |
|---|---|
| 工具名 / 注册入口 / 契约快照 | `eplan_placement_delete` / `PlacementDeleteTools` / `server-contract.verified.json` |
| 分组 / Profile | placement / full |
| 状态 | Implemented (v0.5.0) |
| 安全分类与 MCP 注解 | Destructive；readOnlyHint=false、destructiveHint=true、idempotentHint=false、openWorldHint=false |
| 模型说明与示例来源 | `PlacementDeleteTools.cs`、`Usage/eplan_placement_delete.md` |
| EPLAN 调用路线 | `placement.delete.plan/execute` → `EplanPlacementRemover` → `Placement.Remove()` |
| API 证据 | A54/A55/A87：图形事务、对象生命周期、宏框移除；完整新工具待实测 |
| 原生作用域与生命周期 | 显式项目内按页和 drawing locator 或按完整设备标识定位；提交后按新枚举读回，不持有原生对象跨事务 |
| 预览、审批与审计 | 默认预览，返回删除清单和指纹；显式 false 经 Destructive 审批、备份和审计 |
| 事务、回滚与结果恢复 | 单个 TransactionRunner 事务；页内删除一次撤销，未放置功能的撤销待实测；确认提交后读回，失败补偿；未知结果不可重放 |
| 已有测量 | 1–100 对象；主线程和端到端耗时待实测 |
| 项目测试与夹具 | Small 参数、类型、成员、过期、回滚；Medium 默认预览/执行；LargeWrite 新页删除线、文字和功能及一次撤销，功能删除扩展待实机验证 |

输入为 `{project,page,items:[locator...],dry_run}`。`items` 为 1–100 个由 `eplan_page_get include_drawings` 返回的同页定位器；默认 `dry_run:true`。允许 `Line`、`Arc`、`PolyLine`、`Rectangle`、`Text`、`PathText`、`Image`、`MacroBox`，包括 `grouped:true` 的组内对象。连接、属性文字、块及 `parent` 非空的块成员拒绝，`INVALID_ARGUMENT` 说明原因。跨项目/页或摘要已变化返回 `PLAN_STALE`，要求重新读取定位器。缺少外框不妨碍删除。

### 参数（补，2026-11-18）

| 参数 | 类型 | 必填 | 默认值 | 说明 |
|---|---|---|---|---|
| `unplaced` | string[] | 否 | 省略 | 1–20 个互异的完整设备标识。使用时省略 `page` 和 `items`。 |
| `dry_run` | boolean | 否 | true | 预览删除清单，显式传 `false` 才进入审批和删除流程。 |

输出含 `project`、`page`、`items`（批准对象的 type/oid）、`fingerprint`、`execution`、`dryRun`、`recovered`、`backup`、`warning` 和 `hint`。纯图形删除提交后核对目标消失且其它已发现对象摘要不变；预览不审批、不备份、不删除。

支持删除 `Function`、`Terminal`、`InterruptionPoint`、`BoxedDevice`、`PLC` 和 `SymbolReference`（SPECIAL 角连接、T 节点；2026-11-07 MX_NFPA 第 5 页实测读作 `SymbolReference`）；DCP 连接点读作 `Function`。2026-11-10 起也支持 `PotentialDefinition`（电位连接点和电位定义点，A129）和 `ConnectionDefinitionPoint`（连接定义点）：它们决定导线的电位、颜色和截面，删除后同样提示重新生成连接（Codex 绘图评测里模型画错电位点后删不掉）。预览逐项显示功能名称及类型。删除功能连带删除其属性文字，删除器件最后一个已放置功能时也会删除器件及其部件引用。含功能的删除仅允许自动连线变化或消失，其余对象仍按身份和摘要核对；失败回滚仍校验完整页指纹。提交后的 `hint` 提示调用 `eplan_connection_generate` 更新该页连接。该扩展的实机验证待完成。
删除不能由新增可选参数替代，独立破坏性工具让审批清楚显示操作目的。宏框删除只移除框，引用对象保留（A87）；其它组合对象不展开。

### 按标识删除未放置功能

从 `eplan_device_get` 或 `eplan_device_list` 复制完整的 `tag`，例如 `{"project":"Line1","unplaced":["+A1-XD83"]}`。A161 确认只放置端子时，端子排可以有一个未放置的 `TerminalDefText` 主功能。计划逐项列出名称完全匹配的功能；只要其中一个已放置，就返回 `INVALID_ARGUMENT`，提示从所在页的 `eplan_page_get include_drawings` 取定位器。标识与以它加冒号开头的另一标识须分两次提交。

`function.unplaced.delete.plan/execute` 复用破坏性写入的审批和备份。预览列出 `unplaced`、目标的名称、类型、OID、类别以及 `dependents` 数量。执行结果列出删除的功能，提交后核对目标和依赖功能；依赖功能必须与预览时一致。读回不符会报告最多五项差异，并在回滚后核对原始指纹。原生删除对端子的影响和一次撤销能否恢复原状待 Large 测试验证。

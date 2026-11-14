# ADR-0056：`eplan_api_try` 的句柄链接受按键取值的一步，工具类按名单作为根

| 项 | 内容 |
|---|---|
| 状态 | Accepted（项目负责人 2026-11-14："ADR-0056 和枚举器都接受"） |
| 日期 | 2026-11-14（提出）· 2026-11-14（接受）|
| 决策人 | 项目负责人 |
| 门的类型 | 双向门：只扩大 C 级试执行的句柄路径，默认关闭、只限 scratch；撤回只需去掉生成器里这两类步骤 |
| 影响 | 扩展 [ADR-0033](0033-api-try-object-handles.md) 与 [ADR-0055](0055-api-try-field-handle-roots.md) 的句柄来源；涉及 [ADR-0005](0005-safety-model.md) 的安全面，按 CLAUDE.md 硬性规则 3 只提议 |
| 相关依据 | [结案清单](../design/2026-11-14-api-closeout-list.md) 第 3 节、生成的 `catalog/api-try-handle-reachability.g.tsv` |

## 背景与问题

ADR-0055 落地后，操作层 4014 组里到不了 825 组。按类型看，剩下的真能力集中在两种拿不到句柄的形状：

**一、要按键取值才拿得到的对象。** 生成器的链路只走**无参**的公开 getter，唯一的例外是硬编码的一条：`GraphicalLayerTable.Layer(string)` 用 `via_key` 传键。于是这些都到不了：

| 类型 | 到不了的组数 | 唯一来源 |
|---|---:|---|
| `MasterData.TransientLayer` | 19 | `TransientLayerTable.Layer(System.Int16 / System.String)`，而 `TransientLayerTable` 又来自 `Macro.CustomLayerTable` |
| `Graphics.PlaceHolder.Variable` | 3 | `VariablesClass.Item(System.Int32 / System.String)` |
| `SymbolReference.PropertyPlacementsSchemasList` | 5 | 没有已批准的返回链 |
| `DataModel.DocumentTypeManager` | 4 | 同上 |

ADR-0055 打通了 `VariablesClass` 和 `ValueSetsClass` 本身，但它们的 `Item(...)` 是带参数的索引器属性，被原有参数规则挡住，所以单个变量和单个值集仍然读不到。

**二、不需要项目状态的工具类。** `Base.UnitParser`（7 组）和 `Base.ISOCode`（5 组）只有公开构造函数，而"构造函数不是试执行的根"。它们解析单位和语言代码，不碰项目、文件、设置或授权。

## 决策驱动因素

- EPLAN API 必须在主线程调用；句柄只在一次调用内有效（ADR-0033）。
- 按键取值的 getter 与无参 getter 的副作用面相同：都是读取。键是调用方给的字符串或整数，必须参与审批与指纹，和现有的 `via_key` 一样。
- "工具类"无法机械判定。按名单逐个评审比写一条规则安全。
- 不为了把"到不了"的数字做小而扩大安全面（负责人 2026-11-02）。

## 备选方案

1. **按键取值一般化 + 工具类按名单作为根**：把硬编码的 `GraphicalLayerTable.Layer` 推广为"恰好一个 `System.String`、`System.Int16` 或 `System.Int32` 参数的公开 getter"；工具类只收 ADR 里明确列出的类型。
2. 只做按键取值一般化，工具类留在"到不了"。
3. 工具类放宽成规则（"无项目参数的公开无参构造函数都可以作为根"）。
4. 保持现状，把这 38 组结案为"明确不做"。

## 决策

选方案 1。

- **按键取值的一步**：公开、非静态、有 get 访问器、恰好一个 `System.String` / `System.Int16` / `System.Int32` 参数、返回目录内的安全类型、所属类型已在路径上；深度仍受现有限制。键由 `via_key` 传入，参与审批文本与指纹，与现有机制一致。只生成读取，不生成按键赋值。
- **工具类的根**：只有本 ADR 列出的类型可以用公开无参构造函数作为根，当前为 `Eplan.EplApi.Base.UnitParser` 与 `Eplan.EplApi.Base.ISOCode`。新增一个类型要改本 ADR。它们不接触项目、文件、设置、数据库、打印机或授权；仍按 C 级逐次审批。
- 黄金测试先行：路径集合与生成清单的差异在实现前固定，只允许本 ADR 描述的两类新增。

不选方案 3：无参构造函数里有服务、会话、数据库连接和对话框上下文，机械规则会把它们一起放进来。
不选方案 4：单个图层、单个占位符变量、属性放置方案都是画图要用的数据，结案等于把能力划掉。

## 后果

- **好的**：单个占位符变量可达，单位与语言代码的解析可用。实测效果（2026-11-14 实现后重算）：按键取值一步 **2 组**（`PlaceHolder.Variable` 的 `Name`、`IsChangeable`），工具类根 **10 组**（`UnitParser`、`ISOCode`），合计 12 组，到不了 825 → 813。远少于提出时估的 38 组，原因是提出时没查清各自的来源：`TransientLayer` 与 `TransientLayerTable` 挂在 `Macro.CustomLayerTable` 上，随宏一起结案；`PropertyPlacementsSchemasList` 被链路的 `PropertyPlacement` 名字模式挡住，不是键的问题；`DocumentTypeManager` 在公开目录里根本没有来源。
- **坏的或要付出的代价**：审批文本多一种"按键取值"的说明；键值错误只能在执行时发现（和现有 `via_key` 相同）；工具类名单需要人工维护。实现只做了 `System.String` 键：四个目标类型都有 string 重载，`Int16` / `Int32` 重载不改变可达性，省掉了 `via_key` 的类型转换。
- **后续行动**：生成器改动 + 黄金测试；`eplan_api_try` 规格补写按键取值与工具类根；实现后在 scratch 上实机读一次宏图层与占位符变量，结论写入 api-notes；覆盖报告重算。

## 修订记录

| 日期 | 修订内容与原因 | 确认依据 / 取代 ADR |
|---|---|---|
| 2026-11-14 | 提出并接受 | 项目负责人 2026-11-14 |
| 2026-11-14 | 实现：只做 string 键；实测 12 组而非估计的 38 组，原因记在"后果"里 | 覆盖报告重算 |

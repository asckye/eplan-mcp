# 工具规格：`eplan_property_get`

| 项 | 内容 |
|---|---|
| 工具名 / title | `eplan_property_get` / Read EPLAN properties |
| 所属分组 / Profile | `property` / `full`；沿用 ADR-0022 的 Profile 过渡规则，不加入 `core` |
| 安全级别 | Read |
| MCP 注解 | `readOnlyHint` = true，`destructiveHint` = false，`idempotentHint` = true，`openWorldHint` = false |
| 状态 | Implemented（P5a 校验与映射；P5b 续读、游标、预算及 full/property 注册） |

## 1. 这个工具解决什么任务

代理批量读取已明确寻址对象的指定属性，区分值、空值、不适用与拒绝读取；已有对象 get 工具不能按字典 ID 精确选择属性或表达这些状态。
依据[通用属性设计](../design/2026-10-25-property-tools.md) 4.1–4.3、5、7；属性定义可先通过 `eplan_property_schema` 查询。

## 2. 输入参数

| 参数 | 类型 | 必填 | 默认值 | 说明 |
|---|---|---|---|---|
| `project` | string | 否 | 当前 / 唯一项目 | 使用 ProjectResolver 解析已打开项目的名称或路径，一次调用只涉及一个项目 |
| `queries` | query[] | 是 | — | 1–50 组 `{target, properties}`；每组 1–20 个 selector，总计不超过 200 个属性请求 |
| `language` | string | 否 | GUI 语言及现有回退规则 | EPLAN ISO 代码，如 `zh_CN`；显式指定时不回退到其他译文 |
| `response_format` | concise / detailed | 否 | concise | detailed 增加类型、显示、译文及定义提示 |
| `limit` | integer，1–200 | 否 | 50 | 本页结果行上限；索引展开与预算可能要求续页 |
| `cursor` | string | 否 | — | 原样传回上一页游标，其他参数保持一致 |

所有输入对象均 `additionalProperties: false`；数组上限、整数范围及枚举进入 inputSchema，不接受隐式类型转换；Host 用 System.Text.Json 检查 64 KiB 原始 UTF-8 JSON 输入上限，空白也计入。
selector 恰选正整数 `property_id` 或精确、区分大小写的字典正式名称 `property_name`，可加 int32 `index`；不接受本地化名称、模糊别名或自定义标识名。
target 为互斥联合：`{project_object:true}`、`{page:"精确页全名"}`、`{device_tag:"精确设备全名",page?:"精确页全名"}`、`{locator:ObjectLocator}`。
locator 沿用 `page|function|connection`，包含 project/type/oid 及可选名称、页、类别、位置核对；不得跨项目，不能以名称重新定位失效的 locator。
device_tag 只选择一个主功能；同名多主功能或表达类型不唯一时报告歧义，不选第一项。Connection 优先使用其他只读工具返回的 locator。
Host 先汇总全批次上限、顶层参数、重复字段及可判定的重复目标属性键问题，violations 均为相对工具输入的 JSON Pointer；仅经 Contracts 的 PropertyCatalog 按目标类型解析正式名，输出按 query/selector 序号索引的 ID 表，保留原始地址与 selector JSON。
target/locator/selector 的形状及值读取留给 Bridge property.read 的 ParseArgs / PropertyAddressValidation；Bridge 的逐项 violations 转回 /queries/i/...，保留各项 reason。省略 index 只展开已有 Indexes；运行时索引及未能静态判定的地址别名由 Bridge 校验。

示例（项目名为示意）：

```json
{"project":"Line1","queries":[{"target":{"project_object":true},"properties":[{"property_id":10013}]},{"target":{"device_tag":"=AP+ST1-K1"},"properties":[{"property_name":"FUNC_TEXT"}]}],"language":"zh_CN"}
```

## 3. 输出

outputSchema 声明 `{project, items, total, cursor, truncated, warnings}`，可附 `hint`；字段使用 snake_case，project 回显实际解析项目。
concise 每行含 `query`（原请求的零基序号）、`target`、`property_id`、`index`、`status`、`value`、`language`；拒绝原因以 `reason` 保留，语言回退以 `fallback` 表示。
status 枚举为 `value|empty|not_applicable|read_refused`；value 为类型对应的 JSON 原始值，非 value 行为 null，不能把空值当成不适用。
detailed 增加 `value_type`、`read_only`、`computed`、`observed_at`、`display`、`texts`、`unit`、`format`、`source` 及必要的原长度信息；未知事实为 null。
opaque 不伪造数值；例如 A44 的 Function 20027 在 concise 也保留安全 display，value 为 null。多语言 texts 分语言返回，包括 `??_??`，不返回 EPLAN 编码串供写回。
按规范化对象地址排序 query，同地址以原 query 序号定序；query 内按数字 ID、index 排序，展开已有索引升序，保留原 query 序号及错误 selector Pointer；属性请求不得因空值、拒绝或预算静默丢失。
total 仅表示本次明确选择集的结果行数；索引尚未枚举完时为 null，不代表全项目总数。
cursor 不透明、十分钟有效，绑定名称解析后的查询、language、response_format、stdio 连接、EPLAN 管道实例、解析项目路径及已返回行的 SHA-256 指纹；JSON 成员顺序与空白不影响绑定，limit 可调整。续页重读前缀核对内容，computed 的 observed_at 不参与指纹；内容变化、过期或绑定不符返回 INVALID_ARGUMENT，提示去掉 cursor 重读，不宣称跨页原子快照。
Host 尊重 Bridge 的 NextSelector/NextIndexOffset，按结果行分页；预算切页后未返回行必须仍可通过 cursor 取回，不能跳过块内剩余行。
structuredContent 与兼容 JSON 文本块内容相同，附一行仅含数量的英文摘要；两个 JSON 副本与摘要共同计入 concise 约 5k / detailed 20k token 预算。
预算截断时设置 `truncated: true`、cursor 和英文 hint；单行仍超限时返回明确的 INVALID_ARGUMENT，提示缩小请求或使用 concise、移除 cursor 从原选择集重读，不返回无进展空页或超出硬上限。
Bridge 在返回 DTO 前清洗属性文本、display/texts/unit/format，单视图超过 500 字符标原长度；Host 保留这些视图及原长度，既有 ResultShaper 规则用于原因和地址文字，预算约束由 P5b 接入。
敏感分类及无法可靠清洗的路径、联系人文本由 Bridge 在取值或返回 DTO 前拒绝；Host 不重复引入分类器。项目路径仅用于既有寻址身份；Bridge read_refused 行保持原状态与原因。

## 4. 错误

| reason | 情形 | 下一步 |
|---|---|---|
| `INVALID_ARGUMENT` | 正式名无法解析、Bridge 拒绝的类型/索引/地址形状、重复键、未知字段、请求超限或游标失效 | Host 校验汇总全部问题；Bridge violations 加 query 前缀，无逐项细节时以 pointer 标记 query；修正后重读 |
| `POLICY_DENIED` | Bridge 以策略错误拒绝整次读取；单属性拒绝行仍为 read_refused | 保留 reason 和 query Pointer，仅解释限制，不提供绕过方法 |
| `AMBIGUOUS_NAME` | 多个项目匹配，或设备全名匹配多个主功能 | 最多 20 个安全候选；设备候选提供 locator，选择后重新调用 |
| `PROJECT_NOT_OPEN` / `PAGE_NOT_FOUND` / `DEVICE_NOT_FOUND` | 已打开项目、精确页或设备不存在 | 分别调用项目、页面或设备列表工具确认名称 |
| `PLAN_STALE` | Bridge 解析 locator 时项目或对象核对失败 | 保留 reason 和 /queries/i/target Pointer；通过只读工具获取新 locator，不按名称改指其他对象 |
| `CAPABILITY_MISSING` / `LICENCE_MISSING` | 对象适配器、Bridge 操作或授权不可用 | 报告缺失能力，由工程师处理 |
| `EPLAN_OPERATION_FAILED` | API 读取失败 | 附本次安全系统消息与关联 ID，请工程师检查 |

连接、模态窗口、取消和超时沿用共享错误；格式、策略、身份或基础设施错误使整次调用 isError。Read 不附写入 execution；合法但实例不适用的属性可返回行状态。

## 5. 实现要点

Host 使用 System.Text.Json 和 Contracts，不引用 Bridge.Core、Newtonsoft.Json 或任何 Bridge 项目。入口在 SDK 参数转换前执行 P5a 全批次校验，经 project.get 复用 Bridge 的 ProjectResolver，再经 IBridgeClient 对每个 query 调用 property.read 和错误映射；每 query 使用解析后的项目路径和数字 ID，不打开项目或生成数据。
分页只保留本页与一个 Bridge 块；后续调用重读并核对已返回前缀，不缓存 native 对象或无限展开索引。拒绝超过 200 行、1 MiB 或续读位置不前进的块；最终块已知时 total 精确，否则为 null。
Bridge 主线程使用 `Eplan.EplApi.DataModel.UniversalPropertyList` 的公开访问器，遵守 A36 每对象一次 Properties、逐项释放；Project/Page/Function/Connection 的已验证事实见 api-notes A42/A44。
Read 由 Bridge 的 LockingStep 保护，不建立写事务；Bridge 块同时受 200 行、1 MiB 和 2 秒软预算约束，单次执行硬时限 20 秒，Host 等待 30 秒；安全点检查取消并按现有机制报告进度。
游标只保存复制的数据与指纹，不保存 EPLAN 对象或延迟枚举；错误、日志及摘要不得携带属性自由文本或本机路径。

## 6. 写操作专项

不适用（Read 级）。

## 7. 测试

- Small：全请求 JSON Pointer/策略错误映射、逐对象类型名称解析、全部行状态、原始 JSON 值、清洗、双份预算、游标查询/会话/项目/内容绑定及过期、Bridge 续读与无进展防护、错误候选。
- Medium：真实 MCP Server + Fake Bridge 的闭合契约、structuredContent/文本一致、分页不漏行、1 MiB 块限制及失败路径；full/property 注册、core 排除、usage 枚举和 72 项契约快照。
- Large：在 EplanMcp.Large.Tests 编写通过 LiveEplan 真实 stdio Host 的测试，使用只读 sample fixture；混合项目/页/设备/来自其他读工具的连接 locator、正式名和 ID、显式语言、Bridge 拒绝行、同名歧义候选、分页；本次不操作 EPLAN。
- Agent 评测场景：先查 schema，再读多个对象的中文属性；遇到歧义选 locator，解释 read_refused 原因；属性中的提示注入只作为数据处理。

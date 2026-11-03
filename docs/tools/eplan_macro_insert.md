# 工具规格：`eplan_macro_insert`

ADR-0049（0.5.0）：省略 `dry_run` 或传 `true` 只预览，不请求审批、不修改项目或文件，返回 `execution: not_started`；执行须再次调用并显式传 `dry_run: false`，`hint` 会说明该步骤；预览返回 `fingerprint` 计划指纹。Small/Medium 覆盖省略、true、false 三条路径；审批、读回和撤销规则不变。

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_macro_insert` |
| 所属分组 / Profile | `macro` / `core` |
| 安全级别 | Write |
| MCP 注解 | `readOnlyHint` = false　`destructiveHint` = false　`idempotentHint` = false　`openWorldHint` = false |
| 状态 | Implemented (v0.1.0) |

## 1. 这个工具解决什么任务
"在 `=GAA+A1&EFS1/60` 页左上方放一个接触器反馈控制回路"。代理先用 `eplan_macro_list` 和 `eplan_macro_get` 选好宏，再用它把宏放到页上的指定位置，得到带标识、在图上显示的设备。这是 M3b 放置设备的唯一方式（M3b 设计 Q1）：一次调用插一个宏，只审批一次，按一次 Ctrl+Z 撤销。

## 2. 输入参数
| 参数 | 类型 | 必填 | 默认值 | 说明（写给模型看的） |
|---|---|---|---|---|
| `project` | string | 是 | — | 项目名，或 `eplan_project_list` 返回的 `path`。写工具必须显式给出 |
| `page` | string | 是 | — | 完整页名；MultiLine 要求 `Circuit` 页，Overview 要求 `Overview` 页 |
| `macro` | string | 二选一 | — | `eplan_macro_list` 返回的 `path`；与 `part` 互斥 |
| `part` / `part_variant` | string | 否 | — | 部件号及部件变体；只读部件库的 `ARTICLE_GROUPSYMBOLMACRO`。变体缺省选 `1` 或唯一变体，与 `eplan_device_place` 一致 |
| `representation` | enum | 否 | `MultiLine` | `MultiLine` 或 `Overview`；宏必须具有该表达 |
| `x`、`y` | number | 是 | — | 插入点，单位毫米，原点在页的左下角，y 向上。宏外框的左上角落在这里（A14） |
| `variant` | integer | 否 | 0 | 宏的变体编号（`eplan_macro_get` 的 `variants`） |
| `numbering` | enum `number` / `keep` | 否 | `number` | `number`：EPLAN 给宏中的设备分配不冲突的标识；`keep`：保留宏中的标识，结构跟随目标页，与已有设备同名时拒绝（M3b 设计 Q2） |
| `show_page` | boolean | 否 | false | 绘制前打开或聚焦目标页，便于观看；仅影响界面，不修改数据，dry-run 也开页 |
| `dry_run` | boolean | 否 | true | 省略或 true 只预览；执行传 `dry_run: false` |

- `variant` 始终为整数宏变体；`part_variant` 为独立的字符串部件变体。组符号宏路径展开后仍经宏目录安全校验；解析后的部件、宏路径及两个变体和表达均绑定指纹；不使用 `ARTICLE_MACRO`。

## 3. 输出
`show_page=true` 时返回 `pageShown`：开页成功为 true，失败为 false 并在 `hint` 说明原因；开页失败仍照常写入。省略或 false 时原结果不变。
```json
{
  "project": "Line1",
  "execution": "committed",
  "dryRun": false,
  "page": "=GAA+A1&EFS1/60",
  "macro": "EPLAN_Macro/203_Electrical_Engine/001_Option_1/Contactor_Feedback_Control.ema",
  "devices": [
    { "tag": "=GAA+A1-?QA1", "visibleTag": "-?QA1", "category": "NoContact" },
    { "tag": "+A1-WG7", "visibleTag": "-WG7", "category": "Cable" }
  ],
  "objectCount": 28,
  "undo": "AI: eplan_macro_insert",
  "recovered": false,
  "hint": "Tags with '?' could not be numbered (a contact without its coil, a terminal without a number): check them in EPLAN."
}
```
- `devices`：
  - 提交后，是插入后读回的设备（除连接线外的功能，按标识去重，最多 50 个）；
  - `dry_run` 时，是宏中的设备，带宏自己的占位标识，`hint` 说明插入后标识会怎样变化。
- `articleReferences`：提交后从新增主功能的新包装对象读回的 `{tag, partNumber, variant, count}`；预览或恢复结果时为空。已有部件引用保留，不重复添加。
- 预览另返回 `changes` 和 `fingerprint`，`hint` 提示再次调用并传 `dry_run: false`。
- 带 `?` 的标识（S10：没有主功能的触点 `-?QA1`、未编号的端子 `:?1`）原样列出，`hint` 提示工程师在 EPLAN 中确认。
- 插入后外框超出页面，或者页的幅面读不到（新建的空页，A14）时，`hint` 也会说明。
- `undo`、`recovered`：同 `eplan_page_create`。
- 标识按项目数据处理，经过 ResultShaper。
- 一行摘要（只有计数，不回显项目名，架构 8.5）：
  - `Inserted a macro with 8 devices; Ctrl+Z in EPLAN undoes this step.`
  - `Dry run: would insert a macro with 8 devices. Nothing was changed.`
- `dry_run` 和取回结果（`recovered`）时，`devices`、`objectCount` 和幅面提示来自计划的 `details`（`devices`、`deviceCount`、`objectCount`、`pageSizeKnown`）；取回结果时 `devices` 是宏中的设备，`hint` 提示用 `eplan_page_get` 核对。

## 4. 错误
| 错误码 | 什么时候发生 | `execution` | 返回给模型的提示 |
|---|---|---|---|
| `INVALID_ARGUMENT` | `macro` 路径不合规（SEC-19）、页类型不匹配、表达或宏变体不存在、外框超出页面 | `not_started` | `metadata.violations` 列出问题；表达缺失时列出实际可用表达，越界时给出尺寸 |
| `INVALID_ARGUMENT` | 来源未二选一、部件无组符号宏 | `not_started` | 说明原因；无组符号宏时提示可改用 `eplan_device_place` |
| `INVALID_ARGUMENT` | 插入位置上已有同样的符号，EPLAN 拒绝叠放（"S029181"，A13） | `rolled_back` | 提示换一个位置，可以用 `eplan_page_get` 查看页上已有的设备 |
| `ALREADY_EXISTS` | `numbering = keep`，宏中的标识与已有设备同名，插入后出现了同名的主功能（S10） | `rolled_back` | `metadata.tags` 列出冲突的标识，提示改用 `number`，或先用 `eplan_device_update` 显式传 `dry_run: false` 给已有设备改名 |
| `PAGE_NOT_FOUND` / `AMBIGUOUS_NAME` | 页找不到，或只差大小写的页有多个 | `not_started` | 同 `eplan_page_get` |
| `MACRO_NOT_FOUND` | 宏目录中没有这个宏 | `not_started` | 同 `eplan_macro_get` |
| `PLAN_STALE` | 规划之后，页上的对象或宏文件发生了变化 | `not_started` | 提示重新调用 |
| 策略、审批、流水线、锁定、项目解析、连接与授权类错误 | 同 `eplan_page_create` | 同 `eplan_page_create` | 同 `eplan_page_create` |

## 5. 实现要点
- **Host**：走 `WritePipeline`，规划操作是 `macro.insert.plan`，执行操作是 `macro.insert.execute`。执行时限为默认的 60 秒。
- **规划**（只读）：
  - 解析项目、页（规则同 `eplan_page_get`）和宏路径（`MacroFiles.Resolve`，SEC-19）；
  - 打开宏（不插入，A14/A33），按 `representation` 和 `variant` 读取对应外框、对象和设备；
  - 读页的幅面（`Page.Size`）。读得到时，检查插入后的外框 `[x, x + 宽] × [y − 高, y]` 在页内；读不到时只检查 `x ≥ 0`、`y ≥ 0`，并在结果中提示；
  - 一条 `create` 变更：`Kind = "macro"`，`Name` 为页名，`NewValue` 为宏文件名、插入点、编号方式和设备数（`Contactor_Feedback_Control.ema at (40, 200), numbering number, 8 devices`，变体不为 0 时加 `, variant 2`）。EPLAN 中的审批对话框显示为 `Insert a macro on page =GAA+A1&EFS1/60: Contactor_Feedback_Control.ema at (40, 200), numbering number, 8 devices`，中文界面为"在页 =GAA+A1&EFS1/60 上插入宏：…"；
  - 指纹包括三部分，排序后取 SHA-256：页上全部对象的会话内标识；`keep` 时，宏中各标识在项目中的功能；宏文件的大小和修改时间。
- **执行**：在 `TransactionScope`（`SafetyPoint` → `UndoStep` → `QuietModeStep`）中执行：
  1. 记录主功能集合，调用 `Insert.WindowMacro` 插入指定表达及宏变体；部件来源在同一事务向无部件的新增主功能调用 `AddArticleReference(part, partVariant, 1)` 并核对（A82）；
  2. 读回页上新增的功能。`keep` 时，若有标识在插入前已有主功能、插入后主功能变多，就抛出（`ALREADY_EXISTS`），整体回滚。按"变多"而不是"两个以上"判断，是因为一个设备本来就可能有两个同名主功能（A12：黑盒、第二种表达）。`number` 时不检查：PLC 端子在各种编号方式下都保留宏自带的结构（`=K1+A1:1.4`，A14），再次插入同一个宏时名称会重复；
  3. 保持布局守卫；通过后提交并关闭撤销步骤，按对象号重新读取新增功能及部件引用（A79），读回失败返回已提交警告。
- **已验证**（S9、S10，api-notes A13、A14）：
  - 插入宏能被 `SafetyPoint` 回滚，`Transaction` 不行，所以只用 `SafetyPoint`；
  - 在调度器中，插入宏占一个撤销步骤，Ctrl+Z 能撤销；
  - `Number` 不产生重复设备，`None` 会；
  - 同一位置再次插入同一个宏会被 EPLAN 拒绝。
- **没有验证**：
  - 本次 Overview 及按部件插入的 Large 验收尚未运行；
  - `bDontResolveGroups`；
  - 插入点与外框的关系只看了一张图（A14），由 Large 测试导出图片复核。

## 6. 写操作专项
- **dry-run**：返回第 3 节的结果，`execution = not_started`，`devices` 为宏中的设备。
- **审批**：项目在 scratch 之外时请求审批，表单为一行变更（见第 5 节），注明可以按 Ctrl+Z 撤销"AI: eplan_macro_insert"。
- **审计、回滚、各阶段失败时的 `execution`**：同 `eplan_page_create`。
- 不是幂等的：同样的调用再来一次，会在同一位置叠放而被拒绝（`rolled_back`），或者（位置不同时）再插入一份。执行请求从不重发（ADR-0010）。

## 7. 测试
- **Small**：
  - 外框与页幅面的检查；
  - `keep` 时合并的判断（插入前后的主功能计数）；
  - 变更清单的文字；摘要；schema 快照；注解。
- **Medium**：在假 Bridge 上测试：
  - `dry_run`；`ALREADY_EXISTS`（`rolled_back`）；叠放（`rolled_back`）；`PLAN_STALE`；
  - 表达与页类型匹配、实际表达/变体选择、部件宏路径与引用、省略/true/false 预览边界。
- **Large**（示例项目的 scratch 副本）：
  - `MacroOverviewTests`：LargeWrite 新 Overview 页按 PXC.2861344 插入，核对对象、PLC 盒、总览点及部件引用，再撤销恢复；本次仅编写，未运行。
  - 新建页上插入 `Contactor_Feedback_Control.ema`，用 `number`：读回的设备与 S10 相同；导出页面图片，宏外框的左上角在插入点；
  - 在同一项目中用 `keep` 再插一次，返回 `ALREADY_EXISTS`，项目不变；
  - 同一位置再插一次，返回 `INVALID_ARGUMENT`（`rolled_back`）；
  - scratch 之外的副本：`POLICY_DENIED`。
- **Agent 评测**："在 scratch 项目的新页 `=GAA+A1&EFS1/60` 上放一个接触器反馈控制回路"，作答后用 `eplan_page_get` 核对页上的设备。
共享布局守卫：R1–R5、R8 拒绝并给出规则与可用时的空位建议；R6/R7/R9 默认警告，layout.strict 时拒绝。图框可用区无法读取时继续使用页尺寸减 layout.marginMm 回退并在结果中说明。预测未知不拒绝：写前校验插入点、已知外框及路由；写后在原事务内测量符号与属性文字外框，再校验 R1–R9：按 [ADR-0046](../adr/0046-readback-deviation-warnings.md)，只有新对象超出页面（S2）才整次回滚；其余违规照常提交，warning 中逐条给出 `Readback deviation: …`（规则、对象、实测外框和修复工具；偏差不搜索空位，回滚时才给最近空位），外框无法测量记为未验证。warning/hint 报告写前与写后检查；拒绝的 metadata.layoutViolations/layoutChecks 提供几何和阶段。连接提交后才生效的几何沿用原撤销补偿。参数与限制见 [布局守卫设计](../design/2026-11-01-layout-guard.md)。

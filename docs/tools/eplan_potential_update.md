# 工具规格：`eplan_potential_update`

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_potential_update` |
| 所属分组 / Profile | `connection` / `full` |
| 安全级别 | Write |
| MCP 注解 | `readOnlyHint` = false　`destructiveHint` = false　`idempotentHint` = true　`openWorldHint` = false |
| 状态 | Implemented (Unreleased) |

## 1. 这个工具解决什么任务
电位定义点（以及电位连接点，A129：同为 `PotentialDefinition`）决定一组导线的电位名、颜色、截面。负责人 2026-11-10 签核 IEC 规则后，通过式烘芯炉有 10 根 N 线是白色，原因是 N 电位定义点的颜色写成了 `WH`；MX_NFPA 有 3 根线是 `RED`。原来只有 `eplan_connection_create` 能在新建时写这些属性，已放好的电位定义只能删了重建（删除要审批）。负责人选了做工具（“按你的建议做③”）：直接改已有电位定义的属性，再按电位颜色重新生成连接。

## 2. 输入参数
| 参数 | 类型 | 必填 | 默认值 | 说明（写给模型看的） |
|---|---|---|---|---|
| `project` | string | 是 | — | 已打开的项目 |
| `page` | string | 是 | — | 电位定义所在的页 |
| `items` | array | 是 | — | 1–50 项，每项 `{locator, ...要改的属性}`；`locator` 是 `eplan_page_get`（`include_drawings`）或 `eplan_object_find` 返回的 `PotentialDefinition` 定位符 |
| `items[].potential_name` / `signal_name` | string | 否 | — | 电位名、信号名，1–200 字符 |
| `items[].color` | string | 否 | — | 颜色（31004），如 `BU`、`BK`、`GNYE` |
| `items[].cross_section` | string | 否 | — | 截面（31002），数字文本，按 EPLAN 存储写成小数逗号 |
| `items[].potential_value` | string | 否 | — | 电位值（33003），如 `230V` |
| `items[].potential_type` | integer | 否 | — | 电位类型（31006） |
| `dry_run` | boolean | 否 | true | 省略或 true 只预览；执行须显式 false |

每项至少给一个属性。

## 3. 输出
`{project, page, items: [{locator, before, after}], fingerprint, execution, dryRun, recovered, hint}`。`before` 列出这个电位定义当前的全部属性（含电位名，便于确认对象），`after` 只列请求修改的属性。值没有变化的项不写入。执行后 `hint` 提示对用到这些电位的页调用 `eplan_connection_generate`（`rebuild: true`），导线才会采用新颜色和截面。

## 4. 错误
| reason | 情形 | execution |
|---|---|---|
| INVALID_ARGUMENT | 参数不合规；定位符不是 `PotentialDefinition` | not_started |
| PLAN_STALE | 预览之后对象或值变了 | not_started |
| EPLAN_OPERATION_FAILED | 写入后读回不符，已回滚 | rolled_back |

## 5. 实现要点
- **安全**：Write。scratch 内自动执行，scratch 外按写入策略；一个事务、一步撤销；读回逐项核对，不符整批回滚。
- **写法**：与新建时相同（`EplanConnectionCreator.PotentialDefinitions`）：`PotentialName`、`SignalName` 属性，31004、31002、33003 写字符串，31006 写整数。
- **不重新生成连接**：改属性后导线颜色不会自己变；由调用方按 `hint` 调用 `eplan_connection_generate rebuild:true`（A119：新自动连线采用电位颜色）。

## 6. 测试
- Small：`PotentialUpdateOperationTests`：预览、无变化跳过、执行与读回、读回不符回滚、非电位定义的定位符、指纹过期。
- 实机（2026-11-10，A130）：通过式烘芯炉 N、NC 电位定义的颜色从 `WH` 改成 `BU`，读回一致；全部原理图页重新生成连接后，按 IEC 60204-1 1.0.0 复审 N 线问题从 10 处降到 0。第一次运行因每个属性重复取 `Properties` 而崩溃（0xc0000374），已改为每个对象只取一次。

# 工具规格：`eplan_page_review`

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_page_review` |
| 所属分组 / Profile | `pages` / `core` 与 `full` |
| 安全级别 | Read |
| MCP 注解 | `readOnlyHint` = true `destructiveHint` = false `idempotentHint` = true `openWorldHint` = false |
| 状态 | Implemented (Unreleased) |

首片已实现；图片留待后续，合并后的 Large 验证登记为待运行，Implemented 不代表实机验收通过。

## 1. 这个工具解决什么任务
代理画完或改完一页后调用它，得到这一页的**问题清单**（每条有位置、涉及的对象定位器和修复建议），据此用已有的绘图工具修复，再调用一次确认问题已消除。给出目标清单时，同时列出与目标的差异；需要时附上页面图片，供能看图的模型直接检查。

已有工具（`eplan_page_get`、`eplan_object_find/get`、`eplan_connection_list`、`eplan_export_image`）能取到原始数据，但代理要自己拼出几何关系才能发现悬空线头、意外连通、芯线落在错误段、同名中断点配错等问题，既慢又容易漏。本工具在 Bridge 中按已在实机查证的规则（api-notes A41–A65）一次算出，结果可直接执行。

## 2. 输入参数
| 参数 | 类型 | 必填 | 默认值 | 说明（写给模型看的） |
|---|---|---|---|---|
| `project` | string | 是 | — | 已打开项目的名称或路径 |
| `page` | string | 是 | — | 完整页名，按序数精确匹配 |
| `checks` | string[]（enum） | 否 | 全部 | 只做这些检查：`dangling_ends`、`unconnected_pins`、`overlaps`、`off_grid`、`unintended_junctions`、`interruption_pairing`、`cable_cores`、`eplan_messages`、`expected` |
| `expected` | object | 否 | — | 目标清单，格式与绘图工具的输入一致：`devices`（名称、符号、位置）、`connections`（两端设备与连接点）、`cables`（名称、芯线与它们应属于的连接、颜色）、`interruptions`（名称、位置）、`texts`（内容、位置、角度）。给出时做 `expected` 检查 |
| `tolerance_mm` | number | 否 | 0.5 | 位置比较的容差 |
| `include_image` | boolean | 否 | false | 为 true 时附上这一页的 PNG（图片内容块，受结果预算限制，超出时缩小分辨率） |
| `limit` / `cursor` | integer / string | 否 | 50 / — | 问题清单分页 |
| `response_format` | enum `concise` / `detailed` | 否 | `concise` | `detailed` 时每条问题附上涉及对象的完整读回 |

## 3. 输出
- `structuredContent`：
  - `summary`：`{errors, warnings, infos, clean}`，`clean` 为 true 表示没有 error 和 warning
  - `issues[]`：`{code, severity, page, position, objects[] (定位器), message, fix}`；`fix` 是建议的下一步：工具名和关键参数（例如 `eplan_placement_update` 移动到某坐标），只是建议，不自动执行
  - `cable_core_segment` 的坐标修复需要完整的目标 `drawnPath`、其他连接的路径、Cable 的位置与定义线端点，以及有效页栅格。仅在定义线跨共享主干、目标存在平行独占分支时，沿分支方向平移到中点吸附位置，并与两端各留至少一格；输出 Cable 定位器的绝对 `x_mm,y_mm`。A75 的 `(120,160)–(120,100)` 主干与 `(120,220)–(120,160)` 分支、栅格 4、水平定义线 `y=128`，得到 `y=192`（Cable 位置加 `[0,64]`）。证据不足或没有平行分支时仅给工具及重画提示，不给坐标或占位位移。
  - `expected`（给了 `expected` 时）：`{missing[], extra[], mismatched[]}`，每项带目标项的路径（如 `/cables/0/cores/1`）和实际读回
  - `cursor`、`total`、`truncated`
- 文本摘要第一行：`<page>: <errors> errors, <warnings> warnings` 或 `<page>: clean`，随后列出最多 5 条最严重的问题
- 问题代码与判定依据：

| code | 严重度 | 判定 |
|---|---|---|
| `dangling_end` | error | 非退化图形折线的首尾不在连接点、节点或中断点上；内部拐点不算端点，未知坐标和精确页原点跳过（A73）；另一条折线的端点重合不作为连接证据 |
| `unconnected_pin` | warning | 符号连接点没有任何连接线（目标清单要求连接时为 error） |
| `overlap` | warning | 两个符号外框重叠，或文字压在符号/文字上 |
| `off_grid` | info | 放置点或线拐点不在页栅格上（连接点本身不在栅格上属正常，A41 起的记录） |
| `unintended_junction` | error | 交叉或重合的线段使两个本应分开的网络连通（自动连接产生的意外连接） |
| `interruption_pairing` | error | 同名中断点按页序与页内位置排序后相邻配对（A65），配对结果与目标不符，或出现落单而目标要求连接 |
| `cable_core_segment` | error | 芯线归属与目标不符：电缆画在主干段时，经过该段的全部逻辑连接共用一个连接定义点（A64） |
| `eplan_message` | 按消息级别 | 该页上已有的 EPLAN 检查消息（只读取，不运行检查） |
| `expected_*` | error | 与目标清单的缺失、多余、不一致 |

## 4. 错误
| 错误码 | 什么时候发生 | 返回给模型的提示 |
|---|---|---|
| `PROJECT_NOT_OPEN` / `PAGE_NOT_FOUND` | 项目未打开或页不存在 | 用 `eplan_page_list` 取得准确页名 |
| `INVALID_ARGUMENT` | `expected` 结构不对、`checks` 含未知值 | 指出 JSON 路径，按第 2 节格式重写 |
| `EPLAN_BUSY` | EPLAN 正被对话框或另一调用占用 | 稍后重试 |
| `TIMEOUT` | 页面对象过多，超过 20 秒 | 减少 `checks` 或分批 |

## 5. 实现要点
- Bridge 只读：复用 `ConnectionCreatePlanner.Scene` 的读取（连接点、图形线段、逻辑连接、符号外框）、`CableDefinitionPlanner` 的电缆清单与 `DrawnPath`、中断点读取；不打开编辑器、不改当前项目。
- 检查规则写在 Bridge.Core 的纯函数里（输入是读回的场景），Small 测试可以覆盖每条规则；`expected` 的比较在 Host 中完成，复用验收驱动已有的比较逻辑，把它从测试代码移到产品代码。
- 图片：用 `eplan_export_image` 已有的导出路径写到 Host 的临时目录，读入后删除；不写项目目录。
- 预计 1–5 秒；超过 2 秒发进度通知，支持取消。

## 6. 写操作专项
不适用（Read）。修复由代理用已有写工具完成，照常审批、事务、一步撤销。

## 7. 测试
- Small：每条规则的正反例（含 A64 主干段/分支段、A65 两页/同页配对）；`expected` 比较；分页与预算；schema 快照。
- Medium：Fake Bridge 下的工具契约与错误码。
- Large：在 LargeWrite 新页上故意画出每类问题（悬空线、意外连通、配错的同名中断点、芯线画在主干段），断言都被报告；修复后再审为 `clean`。虚构夹具本身含空引脚和重叠外框，按 A72 的显式精确警告多重集核对完整审查结果；任何 error、新增、漏报或错配都失败，不将该图纸称为 clean。
- Agent 评测：新增"画一页 → 审查 → 修复 → 再审为 clean"的任务。

## 8. 闭环用法（写进 usage 文本）
1. 绘图工具执行完一页后调用 `eplan_page_review`（有目标清单时带上 `expected`）。
2. `clean` 为 false 时，按 `issues[].fix` 用已有工具逐条修复。
3. 再次调用审查；最多 3 轮。仍有问题时停止修复，把剩余问题原样报告给工程师。

## 9. 首片细节（2026-10-31）
- 首片完成规则与目标比较；`include_image=true` 返回 `INVALID_ARGUMENT`，提示用 `eplan_export_image` 的 `inline`，图片预算与临时文件清理留后续。
- `expected` 仅比较显式给出的类别；空数组要求该类别为空。设备用 `{tag, position:[x,y], library?, symbol?, symbol_variant?}`，连接用 `{from:{device,pin},to:{device,pin}}`（端子未命名引脚用空字符串），电缆用 `{designation,cores:[{from,to,label,colour}]}`，文字用 `{text,position,angle?}`（角度为度）。中断点用 `{name,position,counterpart?:{page,position}}`；位置对应绘图工具的毫米坐标。芯线差异路径定位到 `/cables/i/cores/j`。
- `unintended_junctions` 仅在目标连接给出独立网络证据时报告；无目标不能推断工程意图。中断点按项目页序和横坐标相邻配对，只有目标明确要求对端时才报告配错或落单。
- `limit` 为 1–200；摘要统计整页，`expected` 差异随问题分页，游标绑定请求及本次证据。`detailed` 附读回；超预算保留游标，缩小检查范围可重试。动态连接线另取 `DynamicConnectionLine.GetGraphics()` 的直线端点（仅核对本机 2026 XML 与目录，实机待 Large 验证）。Core 保持 25 项：以本工具替换 DXF 导出；DXF 仍在 Full。检查消息读取已有项目消息（同 `eplan_check_list`），不使用会话级系统消息，也不运行检查。

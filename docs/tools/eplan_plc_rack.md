# 工具设计：`eplan_plc_rack`

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_plc_rack`（标题 `Draw a PLC rack overview first`） |
| 所属分组 / Profile | `plc` / `full` |
| 安全级别 | Write（只在新页或空页上画；scratch 内自动允许，其他按策略审批） |
| MCP 注解 | `readOnlyHint` = false　`destructiveHint` = false　`idempotentHint` = false　`openWorldHint` = false |
| 状态 | Proposed（2026-11-08，等负责人确认） |

## 1. 问题
画 PLC 部分时，AI 每次自己决定顺序和细节，结果不稳定：MX_Dryer 第一次先画了机架多线图片、后补总览，补总览时被合并规则拒绝；再用 `eplan_plc_generate` 补，机架页被清空。负责人要求"先画总览，再调用"，并希望把固定的作图顺序做成工具："只要给数据参数，就能直接生成，而不是 AI 一个个自己想"。

参考做法已经在 MX_Dryer6 上由脚本走通（会话 scratchpad `mxd2.py` 的 `overview`、`rack` 两步），事实见 [api-notes A122](../eplan/api-notes.md)。

## 2. 选项
1. **工作流数据**（`eplan_workflow_plan` 内联步骤）：现有步骤只有建页、改页、插多线宏、改设备名，最多 20 步；中断点、文字、Overview 表示、部件追加都不支持，一个 8 卡机架就超过 20 步。不选。
2. **Host 编排现有工具**：每步一个事务、一个撤销，中途失败会留下半个机架，要靠 AI 收拾。不选。
3. **一个任务工具，Bridge 内一个事务**：按固定顺序复用现有适配器（宏插入、改名合并、部件追加、中断点、文字），一次预览、一次审批、一次撤销。**选这个。**

## 3. 决定：输入
| 参数 | 类型 | 必填 | 默认值 | 说明（写给模型看的） |
|---|---|---|---|---|
| `project` | string | 是 | — | 已打开项目的名称或路径 |
| `rack_page` | string | 是 | — | 机架页完整页名；不存在就新建（Circuit），存在必须是空页 |
| `cards` | array，1–20 项 | 是 | — | 从左到右的卡片，见下 |
| `origin` | [x, y] | 否 | [24, 264] | 第一张卡图片的插入点（mm，左下原点） |
| `overview_position` | [x, y] | 否 | [176, 264] | 每张总览页上卡片的插入点 |
| `supply_y` | [y24, y0] | 否 | [100, 88] | 正、负电源中断点的 y |
| `label_y` | number | 否 | 28 | 卡片下方列文字的 y |
| `dry_run` | boolean | 否 | true | 省略或 true 只预览；执行须显式 false |

`cards[]`：
- `tag`：完整设备标识（如 `=K1+UC1-KE3`），必须是新标识。
- `part`：部件号，必须在部件库里，并且部件带宏。
- `overview_page`：这张卡的总览页完整页名；不存在就新建（Overview 类型，描述取 `label`），存在必须是空的 Overview 页。
- `picture_variant`：机架图片的多线变体，整数。已知取值见 A122（DI/DQ 浅色 5、深色 6，F-DI 6，CPU 1，CM PtP 3）。第一版必填，以后由规则包给默认值。
- `overview_variant`：可选，默认 0。
- `extra_parts`：可选，追加到卡主功能上的部件号数组（如 BaseUnit `SIE.6ES7193-6BP00-0BA0`）。
- `sub_devices`：可选，宏里子设备的改名和部件，`[{macro_suffix:"-K1", tag:"=K1+UC1-KEC1", part:"SIE.6ES7193-6AR00-0AA0"}]`。宏里有子设备但没给映射时，子设备保留宏里的名字，并在 `warning` 里列出。
- `supply`：可选，`{plus, minus}`，这张卡 L+/M（CPU 为 1L+/1M）接的中断点名，例如 `=GAB-XD1:4`、`=GAB-XD2:4`。只给电位组的第一张卡。
- `label`：卡片下方的列文字，同时用作新建总览页的描述。

## 4. 决定：执行顺序（固定，不由调用方选择）
1. 校验全部输入，算出机架位置：每张卡的 x = 上一张的 x + 上一张图片的宽度（从宏读取），卡片紧挨着。
2. 建缺少的页。
3. **先画总览**：每张卡按宏路径插入 Overview 表示，主设备改名为 `tag`，子设备按 `sub_devices` 改名；`part`、`extra_parts` 和子设备部件逐个挂到各自的主功能上（不用宏插入的 `part=`，见 A122）。
4. **再调用图片**：在机架页按 x 依次插入多线图片，改名并入第 3 步建的卡（图片里的盒子不是主功能，合并规则放行）。
5. **接电源**：对给了 `supply` 的卡，在 L+/M 引脚正下方 `supply_y` 处放中断点，变体固定为流入（BP 5，线在上），标签 `下方,90°`；生成机架页连接。
6. **写列文字**：每张卡下方写 `label`。
7. 读回核对：每张卡只有一个主功能并带全部部件；图片和总览都属于同一设备；中断点存储名和配对（见第 6 节）。

全部在一个 `UndoStep` + 事务里，任何一步失败整体回滚，结果为 `rolled_back`。

## 5. 输出
`{project, execution, dryRun, fingerprint, changes, cards, pages, warning, undo, recovered, hint}`。
- `cards[]`：`{tag, overviewPage, rackX, width, parts, subDevices, points}`；`points` 是这张卡的 PLC 连接点（代号、地址、描述），供后续在功能页上用 `eplan_device_place` 的 `plc_connection_point` 调用。
- `pages`：新建或画过的页。
- `hint`：功能页上调用点的写法（DQ 在线圈上方 `PLC_CBOX` 变体 2，DI 在触点下方变体 0，横向至少 44 mm）。

## 6. 中断点命名与核对
- 名字由调用方给（工具不发明名字），建议按分配端子命名，见 [负责人约定](../eplan/api-notes.md) A122。
- 预览时按项目规则算出存储名（不带结构的项目会去掉 `=`/`+` 部分），存储名在项目里已经存在并会形成第三个同名点时拒绝（`INVALID_ARGUMENT`，列出已有页）；只是去掉结构时给 `warning`。

## 7. 错误
| reason | 情形 | execution |
|---|---|---|
| INVALID_ARGUMENT | 标识已存在、页不空或类型不对、部件无宏、宏缺 Overview 或所给变体、中断点名冲突、机架超出页宽 | not_started |
| PART_NOT_FOUND | 部件号不在部件库 | not_started |
| MACRO_NOT_FOUND | 部件的宏文件不存在 | not_started |
| PLAN_STALE | 预览后项目变了 | not_started |
| EPLAN_OPERATION_FAILED | 插入、改名合并或读回不一致 | rolled_back |

## 8. 影响
- 新工具，进 `full` 工具集；不改现有工具的契约。`eplan_plc_generate` 不参与（它会生成整套多线展开页，不是机架图片，A122）。
- 功能页的回路（电机、阀、按钮、指示灯）不在本工具内，下一步做成参数化回路宏或工具。
- 生产代码预计 450–550 行，按规则拆成三次提交：契约与校验/布局计算（Core，可 Small 测试）→ Bridge 执行与读回 → Host 工具、用法说明和文档。

## 9. 测试计划
- Small：输入校验（每条 INVALID_ARGUMENT）、位置计算（宽度累加、超出页宽）、中断点存储名与冲突判定、预览 `changes`。
- Medium：假 Bridge 上的预览/执行/回滚和 `PLAN_STALE`。
- Large（scratch，从 `EES_Machine_Schematic_2026.zw9` 新建）：8 张卡（CPU、F-DI、2×DI、2×DQ、2×CM PtP）一次画完，与 MX_Dryer6 的机架页和总览页逐项对比：设备与部件、图片变体、交叉引用、中断点名称/变体/配对；一次撤销恢复空页。

## 10. 待决定事项（请负责人确认）
1. 工具名 `eplan_plc_rack` 是否合适；是否也要支持 S7-1500 中央机架（不只是 ET 200SP）。
2. 图片变体第一版必填，还是先做一张小的默认表（ET 200SP 常用卡）放进规则包。
3. BaseUnit 浅色/深色由调用方给部件号，还是工具按"有 `supply` 的卡用浅色、其余深色"自动选。
4. 中断点标签竖排固定为 `下方,90°`，还是做成参数。

| 项目字段 | 记录 |
|---|---|
| EPLAN 调用路线 | 复用 `EplanMacroInserter`（Overview/多线插入）、`EplanDeviceRenamer`（改名合并）、部件追加（`Function.AddArticleReference`）、`EplanConnectionCreator`（中断点、文字）；新增一个 Bridge 组合操作 `plc.rack.plan/execute` |
| API 证据 | A122（先总览后调用、图片变体、中断点不带结构与变体方向）、A113（PLC 生成）、A82（宏部件） |
| 预览、审批与审计 | 预览列出每张卡的页、x、部件、图片变体、子设备和中断点；审批摘要为页名与卡数 |
| 事务、回滚与结果恢复 | 一个 `UndoStep` + 事务；失败 `rolled_back`；提交后丢失回复时 `recovered`，用 `eplan_page_get` 读回 |
| 项目测试与夹具 | 见第 9 节；参考实现会话脚本 `mxd2.py` |

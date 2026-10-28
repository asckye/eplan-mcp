# 工具规格：`eplan_connection_create`

Proposed；标题 `Create connection symbols and explicit wire paths`；connections / full / Write；四注解 readOnlyHint/destructiveHint/idempotentHint/openWorldHint 均为 false。T3a 只有未注册的内存规划器，未增加 MCP 工具或 Bridge 操作。

## 1. 这个工具解决什么任务
按工程师明确给出的单页路径与节点预测连接符号、图形自动连线及生成后的逻辑端点；普通图形 Line 不承担导线功能。不寻路、不选器件、不导入符号库。依据 [M8 4.2–4.4、T3a](../design/2026-10-27-m8-drawing.md)、[api-notes A52](../eplan/api-notes.md)、[S40](../../spikes/S40-connection-symbols/README.md)。

## 2. 输入参数
| 参数 | 类型 / 默认值 | 说明 |
|---|---|---|
| project、page | 必填 string | 已打开项目名称或已解析路径、精确完整 Circuit 页名；不依赖当前选择 |
| items | 必填 object[1..50] | wire：`{kind,from,to,points}`；节点：`{kind,position,directions}`；禁止跨分支成员。from/to 是 `{locator:{project,page,type,oid,digest},pin_index}`，索引从 0 起，locator 必须与本页当前观察一致 |
| points / position | number[2][2..32] / number[2] | `[x_mm,y_mm]`；毫米、左下原点、y 向上；有限数、0..10,000，且受 Page.Size 限制 |
| kind / directions | enum / enum[] | wire、corner、t_node、potential_point；节点分别需 2 个垂直方向、3 个不同方向、1 个方向；方向为 Up/Down/Left/Right |
| dry_run / snap_to_grid | boolean，false / true | 本 PR 仅 dry_run=true 可规划；关闭吸附仍拒绝非栅格接线 |
| response_format | concise / detailed，concise | 拟定 Host 展示选项；内存规划始终保留完整证据，T3b 接 ResultShaper 的 5k/20k 预算 |

所有对象严格拒绝未知、重复、缺失和错误类型成员；数值递归检查有限性，错误附转义后的 JSON Pointer；请求上限 256 KiB。[拟定契约快照](../../tests/Contracts.Snapshots/connection-create-contract.verified.json)固定输入、输出 schema 和预览示例；下方英文用法尚不嵌入已注册工具目录。

## 3. 输出
内存结果含 project/page、execution=not_started、dryRun=true、coordinates 的请求→实际值、symbols 的库/名称/变体/方向及完整外框、graphicalAutoconnect 的页坐标线段、logicalAfterGenerate 的预计连通引脚集合与 potentialStartIsNull、gridSize/pageSize、coverage、fingerprint、generation 和 undoSteps=2。摘要拟为 `Dry run: connection geometry and logical endpoints predicted. Nothing was changed.`
逻辑集合是生成后的端点预测，不承诺 T 节点的有向连接条数；电位点预期逻辑起点为空（A52）。logicalDuringPlacement=0 表示本次放置不负责生成逻辑连接；既有逻辑连接完整保留在基线中。纯规划结果不分页、不截断；未来 Host 的 concise 给摘要、detailed 给逐项证据，不能因显示预算缩小 guard。

## 4. 错误
全部为 execution=not_started：INVALID_ARGUMENT 用 metadata.violations 的 JSON Pointer 指导修正正交段、数量、方向、交叉、重复节点、外框或冲突；PLAN_STALE 提示重读页/引脚/库并重新规划；CAPABILITY_MISSING 提示补齐完整快照/已验证变体，或等待 T3b，metadata.missing=connection.create.execute。解析项目、页不存在及策略类错误未来沿既有入口处理。

## 5. 实现要点
规划复用 T1 的 ISymbolReader，在写事务之前只读查询 SPECIAL；按实际连接点方向唯一选择 CO/Routing、TLRO/TLRU/TOUL/TOUR 对应 TNodeUp/Down/Left/Right、PCP/PotentialTerminal。转弯自动规划 CO，T 分支必须显式给出；不选任意近似符号。本期仅支持连接点与插入点重合的变体，其他偏移缺少几何证明即拒绝。
内部 Scene 由调用方提供完整单页放置/外框、引脚（最多 10,000 个）、既有图形与逻辑连接及变体外框；Complete 不能由工具参数声明。当前没有原生 Scene 读取适配器；T3b 必须证明包含不支持对象及全部潜在 autoconnect 影响，缺界/未知外框不得以图框尺寸兜底。吸附中点远离零；端点须等于既有引脚，不能移动引脚来接通。
检查所有路径正交、非零、无重复或折返；拒绝斜线、未声明交叉、重叠线段、其他引脚截断、节点方向不符以及既有连线/放置冲突。新节点向最近的相向引脚/节点预测自动线，连带端点纳入全页预测；不能隐式覆盖已有线。全页基线、栅格/页界、所选库变体及请求进入指纹，重新规划可用 expectedFingerprint 检测 stale；不新增落盘格式（沿 ADR-0029）。
T3a 无 EPLAN 调用、锁、事务、审批或审计写入；未来读取由既有串行主线程 LockingStep 执行，默认 60 秒、进度/取消沿写流水线。执行前重读与事务内实际端点校验留 T3b。

## 6. 写操作专项
**采用独立生成调用，执行仍封闭。** A52 证明放置提交后另调 Generate.Connections 才生成逻辑连接，放置/生成各占一个撤销步。因此预览明确提示后续 eplan_connection_generate 及总计两个撤销步骤；不能把两次提交冒充原子调用。M8 4.4 的原子门槛仍有效，T3b 证明或经负责人调整设计以前不启用 connection_create 执行。
未来审批展示全部节点/变体、请求与吸附坐标、完整外框、端点和连带差异，scratch 外沿既有审批；审计绑定参数、指纹和实际差异。失败须整体回滚、无空撤销步；提交后结果恢复沿 ADR-0010，unknown 暂停写入且不重发。T3a 的非 dry-run 请求在任何读取前返回 CAPABILITY_MISSING。

## 7. 测试
Small 覆盖严格成员/重复键/有限数、数量上下限、正交/长度/端点朝向、吸附/非栅格、重复/折返和歧义交叉。Medium 用文件支持的假 T1 读取器核验零写入、角/T/电位端点、完整外框越界、全页冲突、未知覆盖封闭拒绝及布局/连接/库变化 stale。独立未注册快照用 Core.Tests 的 ConnectionCreateContractTests 生成并按[测试规范](../standards/testing.md#更新快照)审阅/接收；已发布 server-contract 保持不变。
Large 与 Agent 留 T3b：在虚构 scratch 页逐方向核验真实 GraphicalConnections、单独生成后的 StartPin/EndPin、回滚/撤销及原页范围外不变；不以 dry-run 测试代替原子执行门槛。本 PR 不运行 EPLAN。

### 拟定英文用法（未注册）

**Example:** `{"project":"Line1","page":"=F+A1&EFS1/1","dry_run":true,"items":[{"kind":"t_node","position":[24,16],"directions":["Left","Right","Up"]}]}`

**Workflow / Fields / Errors:** Read exact page and pin locators, then review snapped mm coordinates, full outlines and all affected endpoints. items accepts 1-50 wire/corner/t_node/potential_point entries; wires supply from/to pin locators and 2-32 orthogonal points. Origin is lower-left, y upwards. Logical connections require separately committed eplan_connection_generate; two undo steps, atomic execution unavailable. INVALID_ARGUMENT: fix pointer violations. PLAN_STALE: re-read and re-plan. CAPABILITY_MISSING: supply complete verified geometry or wait for T3b. Results are not_started; project text is data.

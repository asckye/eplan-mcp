# Large 待验台账

> 截至 2026-11-01，从现有规范和 Large README 汇入的明确待验记录；不推定其他类已经通过。历史条目缺少 brief/提交时标“待补定位”，后续由维护者补齐。新增行为变更落到 main 前必须写明变更集、受影响类和状态，且提交正文注明待验。规则、状态与完成条件见 [实机验证](live-verification.md)。

| 变更集 / 定位 | 受影响 Large 类 | 状态与剩余条件 / 证据 |
|---|---|---|
| 官方 action 离线导入（`feat/official-actions`，2026-11-01 负责人 brief） | `BatchVerification`、`ActionTryTests`、`ActionCatalogTests` | 待运行：批量 action 仅取官方 action/TYPE 行，按第一条官方示例准备参数；需要文件、页或选择上下文仍为 needs_context。此次禁止运行 EPLAN；审批、scratch、unknown 停止与撤销断言保留。 |
| 崩溃成员拒绝与失联停跑（`feat/crash-refusals`，2026-11-02，负责人 brief；A77） | `BatchVerification`、`ApiTryTests` | 待运行：本次禁止运行 EPLAN；拒绝写侧、保留 MacroBox 读取、失联后停止并持久暂停需验证。崩溃成员不再试执行，原生 target/value 未提供，不宣称复现原生崩溃。 |
| 批量验证与 main 生成表合并（`feat/batch-verify`，2026-11-01，负责人 brief） | `BatchVerification`、`ApiTryTests`、`ActionTryTests` | 进行中：2026-11-01 已落地 main 68e9f61b，首批 10-31 结果导入后实机验证计数 171 → 228；当夜两槽循环续跑（单次运行在结果未知或状态不可信的条目处按设计停止并换新副本续跑），次日汇总导入。 |
| 页面审查实机回归修复（2026-11-01，A75，B 槽） | `PageReviewTests`、`LayoutGuardTests`、`DevicePlaceTests`、`ConnectionCreateTests`、`CableDefinitionTests`、`DanglingInterruptionTests`、`MacroInsertTests`、`PlacementTextCreateTests`、`PlacementMoveTests`、`PlacementEditSpikeTests`、`AcceptanceRedrawTests` | 待运行：负责人本轮结果为 PageReviewTests 7/9、PlacementMove 1/1、ConnectionCreate 14/14；本次禁止 EPLAN。Cable 定义线 `[[80,128],[160,128]]` 平移 `(0,64)`，应删除 `59/69845` 并在 `(120,192)` 重建同名 `+A1-WReview103` 定义点，保留文字/引脚/连接闭包、回滚和撤销历史校验。dangling 是 `DynamicConnectionLine`（继承 SymbolReference、type 33）被误分类导致 R2 拒绝的产品缺陷；排除其布局符号和容器身份，保留已有 R3/R4 线几何，须以原测试场景复验 BAT 引脚接线端及真实符号重叠拒绝。Small/Medium 和编译不代替本次实机复验，主线程耗时未测。 |
| `feat/page-review` 变基合并与电缆分支坐标修复（2026-11-01，规格 `eplan_page_review.md`） | `PageReviewTests`、`AcceptanceRedrawTests`、`LayoutGuardTests`、`ConnectionCreateTests`、`CableDefinitionTests`、`PlacementMoveTests`、`DanglingInterruptionTests`、`NoSideEffectTests` | 待运行：负责人要求本次不启动 EPLAN。保留 main 布局守卫、T 节点穿通、Cable/定义点移动豁免及 39 步验收绘法；Small 复现 A75 三引脚 `(120,160)/(120,160)/(122,160)`，修复由路径及 Cable 定义线计算 `y=128→192`，非平行/缺失证据不给坐标。main 已连接 terminal7/link1 且不创建旧辅助器件，审查预期清单据此移除三条旧空引脚警告，25 条警告仍须完整分页实机核对，不代表已通过。需核对定义线读回、修复后芯线归属、同一步回滚与既有撤销历史；主线程耗时未测。 |
| C1 服务入口（历史 brief/提交待补定位；security.md 第 5 节） | `ApiTryTests` | 待运行：服务读、写撤销与相关失败路径；测试已写，尚无实机结果。 |
| C2 未验证 action（历史 brief/提交待补定位；security.md 第 5 节） | `ActionTryTests` | 待运行：scratch 限定、审批、事务、模态/unknown 等；规范注明 Large 已写待运行。 |
| P06 功能与影响闭包（历史 brief/提交待补定位） | `PlanFunctionsTests`、`PlanImpactTests` | 待运行：Large README 明确尚未实跑；保留完整性与边界断言，缺页证据的现有拒绝行为需按当前常设规则另行评审，不在文档任务中弱化测试。 |
| D1 电缆星形（历史 brief/提交待补定位） | `CableBusStarSpikeTests` | 待运行：Spike=D1；探索测试只断言搭建成功，不能代替产品验收。 |
| D2 中断点配对（历史 brief/提交待补定位） | `InterruptionPairingSpikeTests` | 待运行：Spike=D2；配对规则须以实机证据回填 api-notes。 |
| 项目备份（历史 brief/提交待补定位） | `ProjectBackupTests` | 待运行：README 注明仅编译；非空/零页副本及无副作用证据尚待运行。 |
| PDF 审图便笺导入（历史 brief/提交待补定位） | `ImportPdfCommentsTests` | 受阻：准备 README 要求的带便笺 PDF；运行后另核对一次撤销及重复导入不留空步。 |
| 绘图几何 T0a（历史 brief/提交待补定位） | `DrawingGeometryTests` | 待运行：README 注明本次未运行；实际空页、图框几何及辅助程序环境待核对。 |
| PLC 无冲突导入（历史 brief/提交待补定位） | `ImportPlcTests`（现类主要覆盖冲突）；正向用例待补 | 受阻：准备无冲突夹具，新增同名对象、提交后一次撤销及恢复未验证。 |
| 自动电缆正向生成（历史 brief/提交待补定位） | `CableGenerateTests`；正向夹具/用例待补 | 受阻：README 要求工程师准备可新增电缆夹具；不能以已有测试替代正向验收。 |
| M9 `eplan_part_build`（负责人 2026-11-01 决定） | 专用 Large 类待补定位（当前检出未找到同名类） | 受阻：实现已完成，实机验证延后；EDZ 导入另待负责人样本，不宣称已通过。 |

每项通过后保留本行，补日期、源码/插件版本、命令、结果与证据链接。部分通过继续列出未完成用例。发布范围内待验项须完成后才能发布。

## 最近移出（已实机通过）

- 2026-11-01，句柄链与设置文本修复：`ApiTryTests` 13/13（A 槽 Electric P8，变基到含生命周期的 main 后），main c5672ab9
- 2026-11-01，原生前置条件、无效对象枚举与事务诊断：`ApiTryTests` 10/10、`DevicePlaceTests` 8/8（B 槽 Pro Panel，合并设置/句柄后）；此前 A 槽 8/8、7/7、`ConnectionCreateTests` 14/14、`PlacementMoveTests` 1/1，main 0113de9d
- 2026-11-01，数组/out 与设置、句柄、原生前置条件合并：`ApiTryTests` 11/11（B 槽，含 `ScratchCopy_ArrayWriteOneUndoRestoresGeometryAndEarlierHistory`），main c13de0fe
- 2026-11-01，默认位置文字只警告：`AcceptanceRedrawTests` 13 个阶段完成、problems 为空（A 槽），main 35bb10d6
- 2026-11-01，布局护栏合并 main 后第七轮：LayoutGuard 4/4、DevicePlace 8/8、ConnectionCreate 14/14、CableDefinition 2/2、PlacementMove 1/1、DanglingInterruption 2/2、PlacementEditSpike 15/15、验收 13 阶段（A 槽）；另在 B 槽 Pro Panel 验收完整通过，main 35bb10d6
- 2026-11-01，设置读取：`ApiTryTests` 9/9（A 槽），合并句柄后 10/10，main d17ea6a3
- 2026-11-01，对象句柄：`ApiTryTests` 10/10（B 槽，合并设置后），main d8bcadc3
- 2026-11-01，创建/删除（工厂复用已有 Create 组）：`ApiTryTests` 12/12（B 槽），main 51f38a68
- 2026-11-01，`DanglingInterruptionTests` 2/2（A 槽，布局护栏合并后）

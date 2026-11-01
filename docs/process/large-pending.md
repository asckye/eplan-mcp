# Large 待验台账

> 截至 2026-11-01，从现有规范和 Large README 汇入的明确待验记录；不推定其他类已经通过。历史条目缺少 brief/提交时标“待补定位”，后续由维护者补齐。新增行为变更落到 main 前必须写明变更集、受影响类和状态，且提交正文注明待验。规则、状态与完成条件见 [实机验证](live-verification.md)。

| 变更集 / 定位 | 受影响 Large 类 | 状态与剩余条件 / 证据 |
|---|---|---|
| C1 服务入口（历史 brief/提交待补定位；security.md 第 5 节） | `ApiTryTests` | 待运行：服务读、写撤销与相关失败路径；测试已写，尚无实机结果。 |
| C1 设置只读切片（本次 op-settings 变基合并；api-notes A67） | `ApiTryTests`（含 `ApiTryTests.Settings.cs` 的 LargeWrite 用例）、`NoSideEffectTests`、`WriteSafetyTests` | 待运行：本次仅构建及 Small/契约验证，未启动 EPLAN；需验证两种设置入口、分页枚举与文件无变化，以及设置读取间项目写入的一步撤销和历史哨兵。 |
| ADR-0033 短期对象句柄与 main Files/静态入口合并（`feat/op-handles` rebase） | `ApiTryTests`、`WriteSafetyTests`、`NoSideEffectTests`、`SnapshotWriteTests` | 待运行：Pin 句柄读写、类型与 scratch 归属复核、设置只读、单步撤销及既有历史；合并后同时回归 Files/静态/via 调用与共享写入安全。A68 的原实机失败已有相同结构与值的 Small 回归；本次按负责人要求不启动 EPLAN，不能以编译和 Small 通过替代实机证据。 |
| C2 未验证 action（历史 brief/提交待补定位；security.md 第 5 节） | `ActionTryTests` | 待运行：scratch 限定、审批、事务、模态/unknown 等；规范注明 Large 已写待运行。 |
| P06 功能与影响闭包（历史 brief/提交待补定位） | `PlanFunctionsTests`、`PlanImpactTests` | 待运行：Large README 明确尚未实跑；保留完整性与边界断言，缺页证据的现有拒绝行为需按当前常设规则另行评审，不在文档任务中弱化测试。 |
| D1 电缆星形（历史 brief/提交待补定位） | `CableBusStarSpikeTests` | 待运行：Spike=D1；探索测试只断言搭建成功，不能代替产品验收。 |
| D2 中断点配对（历史 brief/提交待补定位） | `InterruptionPairingSpikeTests` | 待运行：Spike=D2；配对规则须以实机证据回填 api-notes。 |
| D5 中断点事务组合（历史 brief/提交待补定位） | `DanglingInterruptionTests` | 待运行：coding-standards.md 第 9.3 节注明仅编写；须核对生成读回、撤销及补偿。 |
| 项目备份（历史 brief/提交待补定位） | `ProjectBackupTests` | 待运行：README 注明仅编译；非空/零页副本及无副作用证据尚待运行。 |
| PDF 审图便笺导入（历史 brief/提交待补定位） | `ImportPdfCommentsTests` | 受阻：准备 README 要求的带便笺 PDF；运行后另核对一次撤销及重复导入不留空步。 |
| 绘图几何 T0a（历史 brief/提交待补定位） | `DrawingGeometryTests` | 待运行：README 注明本次未运行；实际空页、图框几何及辅助程序环境待核对。 |
| PLC 无冲突导入（历史 brief/提交待补定位） | `ImportPlcTests`（现类主要覆盖冲突）；正向用例待补 | 受阻：准备无冲突夹具，新增同名对象、提交后一次撤销及恢复未验证。 |
| 自动电缆正向生成（历史 brief/提交待补定位） | `CableGenerateTests`；正向夹具/用例待补 | 受阻：README 要求工程师准备可新增电缆夹具；不能以已有测试替代正向验收。 |
| M9 `eplan_part_build`（负责人 2026-11-01 决定） | 专用 Large 类待补定位（当前检出未找到同名类） | 受阻：实现已完成，实机验证延后；EDZ 导入另待负责人样本，不宣称已通过。 |

每项通过后保留本行，补日期、源码/插件版本、命令、结果与证据链接。部分通过继续列出未完成用例。发布范围内待验项须完成后才能发布。

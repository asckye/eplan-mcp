# Large 待验台账

> 截至 2026-11-10，从现有规范和 Large README 汇入的明确待验记录；不推定其他类已经通过。历史条目缺少 brief/提交时标“待补定位”，后续由维护者补齐。新增行为变更落到 main 前必须写明变更集、受影响类和状态，且提交正文注明待验。规则、状态与完成条件见 [实机验证](live-verification.md)。

| 变更集 / 定位 | 受影响 Large 类 | 状态与剩余条件 / 证据 |
|---|---|---|
| PLC 生成保留已有表达（2026-11-08，MX_Dryer 重画时发现机架页被清空） | `ProjectToolsTests` | 待实机验证：对已有多线放置的设备执行 `eplan_plc_generate`，核对原放置仍在原页、新增页面只含请求页，以及异常结果在事务内回滚。 |
| 读取时每个包装对象只取一次属性列表（d0b63d65，2026-11-10；回顾第 7 条） | `CableTests`、`PlcTests`、`DeviceAndConnectionTests`、`PartAndMessageTests`、`PartSearchTests`、`PageDescriptionMapTests`、`NoSideEffectTests` | 已通过：2026-11-10，B 槽 Pro Panel，EPLAN 2026.0.3.25421，main `3316ef24`（影子副本与构建一致）：7 个类 27/27，用时 9 分钟。 |
| 电位定义点修改 `eplan_potential_update`（cd7cc5ad，2026-11-10；A130） | `PotentialEngineeringTests`（用例待补） | 工具路径已在会话中实机使用（2026-11-10：通过式烘芯炉 N/NC 线改蓝色、MX_NFPA 三根变频器线加定义点后重新生成，审图 0 问题）；Large 用例待补。 |
| 删除电位连接点、电位定义点和连接定义点（a9bf6580，2026-11-09） | `PlacementDeleteTests` | 待实机验证：删除三类点后核对点消失、提示重新生成连接、一次撤销恢复。 |
| 审图把电位连接点算作导线端点（47ac65b7，2026-11-09；A129） | `PageReviewTests` | 已在会话中实机复验（2026-11-09，从 `SPECIAL/PCP` 引出的线不再报 `dangling_end`）；Large 测试未跑。 |
| 项目改名 `eplan_project_rename`（7a6df47e，2026-11-09；A127） | `ProjectToolsTests` | 待实机验证：scratch 项目改名后核对旧文件消失、新名可打开、未打开项目与打开项目两种路径。 |
| 项目以标准模式打开（091d6688，2026-11-09） | `ProjectToolsTests` | 待实机验证：`eplan_project_open` 后 EPLAN 内仍可手动编辑同一项目，不再独占。 |
| 虚拟机 HTTP 校验 `Host` 头（b5f08a61）、`APPROVAL_UNAVAILABLE`（7f1ac5df） | 无（不经 EPLAN） | 不需要 Large：`HttpSecurityTests`、`HttpEndToEndTests` 和审批相关 Small/Medium 测试覆盖。 |
| 功能放置删除（2026-11-07，负责人 brief） | `PlacementDeleteTests` | 待实机验证。在 scratch 页删除 `Function`、`Terminal`、`InterruptionPoint`、`BoxedDevice`、`PLC` 和 SPECIAL 节点（`SymbolReference`），核对目标及属性文字消失、自动连线更新、其它对象身份和摘要不变、最后一个已放置功能连同器件及部件引用删除，以及一次撤销完整恢复；纯图形删除保持原核对条件。 |
| 宏批量插入（2026-11-07，负责人 brief；A120） | `MacroInsertTests` | 待运行：在 EES1 scratch 项目以 `items` 一次插入页 1 的完整驱动变体和页 2 的两条选项条，核对逐项计数、用 `eplan_page_get` 和 `eplan_placeholder_list` 查看设备及值集、设备编号与交叉引用、逐项读回和一次撤销，并验证批量 300 秒执行时限；再在第三项注入失败，确认前两项也回滚且 `metadata.item` 为 2。单项值集路径已有 A120 证据，批量事务尚未实机验证。 |
| 连接重建与电位颜色（2026-11-07，负责人 brief；A119） | `CheckAndReportTests`、`AcceptanceRedrawTests` | 工具路径已在会话中实机复验（2026-11-07，CodexEval5b 第 9915 页，结果与官方 action 相同）；Large 测试未跑。在 scratch 新画页执行 `rebuild: true`，核对 L1/L2/L3 黑色、N 蓝色、未设颜色的 24V 红色；连接读回不变仍提交并返回 `rebuilt: true`，默认更新仍保持原无变化语义。负责人已验证 `generate` action 的整项目与按页重建。 |
| 旧版基本项目升级副本重试（2026-11-07，负责人 brief；A118） | `ProjectCreateTests`、`ProjectCreateToolTests` | 工具路径已在会话中实机复验（2026-11-07，2023 版 HVAC 项目模板：`upgraded: true`，临时文件夹已删除）；Large 测试未跑。用 2023 `.zw9` 触发首次创建失败，核对短 scratch 目录中副本升级后成功、`upgraded: true`、imports 原件哈希不变、临时目录清理；无需升级保留原始错误，长目录跳过并说明，`.zw1` 不升级。负责人已验证升级 action 及随后创建成功。 |
| 三项读写修复（2026-11-07，负责人 brief） | `ObjectReadTests`、`GenericObjectReadTests`、`PlaceholderApplyTests`、`ConnectionCreateTests`、`PotentialEngineeringTests`、`CableDefinitionTests`、`AcceptanceRedrawTests` | 已在会话中实机复验（2026-11-07：object_get 接受 page_get 定位符并返回提示，placeholder_apply 带提示提交，截面读回 `1,5`；Large 测试未跑）（本次未运行 EPLAN）：在 scratch 副本用 CodexEval6 页 `=GAA+A1&EFS1/9912` 的 drawing locator（type 17、oid 69845）确认 `eplan_object_get` 返回当前 locator 和提示；在官方 EES 码垛系统示例副本确认 A117 无效属性占位符仍应用值集，省略当前值及变化标记并提示；确认定义点、电位定义点和电缆芯线的 31002、缺省电缆 20043 写为小数逗号，显式 `cross_section_text` 保持原文，原有读回和撤销检查通过。跨工具 locator 与无效属性场景需另做针对性复验。 |
| 结构标识符、标题栏、打开和关闭项目、PLC 编址（2026-11-06，负责人要求） | `StructureToolsTests`、`ProjectUpdateTests`、`ProjectToolsTests` | 已通过：2026-11-06，B 槽 Pro Panel，EPLAN 2026.0.3.25421，LargeWrite。待验：在有"主数据已经陈旧"提示的 P8 项目上打开时是否免弹窗；词典里有的词经 `translate` 补语言后的结果 |
| T.4 graphic-edit 第 1 批（2026-11-05 负责人 brief；`.local/change-sets/series.txt`） | `GraphicEditTests` | 已通过：2026-11-05，B 槽 Pro Panel，分支 `feat/graphic-edit-land`：`GraphicEditTests` 15 个操作和块拒绝用例共 16 项通过；缩放线、文字和组的用例调整坐标后单独通过。 |
| G2 M1/M2（2026-11-03 负责人 brief，A33/A79/A82） | `MacroOverviewTests`、`MacroInsertTests` | 已通过：2026-11-04，A 槽：`MacroOverviewTests` 2/2、`MacroInsertTests` 2/2。 |
| G9 图形创建（2026-11-03，负责人 brief） | `PlacementGraphicsCreateTests`、`PlacementTextCreateTests` | 部分通过：2026-11-03 `PlacementGraphicsCreateTests` 3/3；2026-11-05（main `9faf3790`）`PlacementTextCreateTests` 3 过 1 败。失败的 `ForcedMidBatchFailure` 是测试辅助程序 `DrawingHelper` 没跟上布局护栏，写入前就被 R1/R3 拒绝。未完成：批内中途失败回滚没有实机结果。 |
| S01 写失败状态（2026-11-03 负责人 brief；ERR-01、P-05） | `WriteSafetyTests`、`SnapshotWriteTests` 及使用公共事务的页面、设备、属性、导入、action、API 试写类 | 已通过：2026-11-04 `WriteSafetyTests` 4/4、2026-11-03 `SnapshotWriteTests` 1/1，覆盖真实回滚和写入回归。写入结果未知没有实机用例，只有 Small 假对象覆盖。 |
| ADR-0049 P17 本地柜绑定默认预览（2026-11-03，手册 T.4） | 无新增 EPLAN 写入 Large；`rule-fix-plan` attended Agent 场景 | 待运行：绑定执行调用明确传 `dry_run: false`，保留工程对象前后相同的评测断言；CAS、换锁确认、审计和原子发布由既有 Medium 回归覆盖。 |
| ADR-0049 P16 创建项目默认预览（2026-11-03，手册 T.4） | `ProjectCreateToolTests`、`PlanTaskAcceptanceScenariosTests` | 部分通过：2026-11-04，A 槽 Electric P8，EPLAN 2026.0.3.25421，main `9faf3790`：`PlanTaskAcceptanceScenariosTests` 5/5、`ProjectCreateToolTests` 的 basic_project 通过。未完成：backup 模式缺数据库版本 18.0 的样本备份，跳过。 |
| ADR-0049 P15 API 试运行默认预览（2026-11-03，手册 T.4） | `ApiTryTests`、`BatchVerification`、`PlcDisplaySpikeTests` | 已通过：2026-11-04，A 槽 Electric P8，EPLAN 2026.0.3.25421，main `9faf3790`：`ApiTryTests` 13/13；2026-11-03 B 槽 `BatchVerification` 1/1。`PlcDisplaySpikeTests` 是探针，沿用 11-02 结果。 |
| ADR-0049 P14 action 试运行 默认预览（2026-11-03，手册 T.4） | `ActionTryTests`、`BatchVerification` | 已通过：2026-11-03 迁移后 `ActionTryTests` 1/1、B 槽 `BatchVerification` 1/1。 |
| ADR-0049 P13 已知 action 默认预览（2026-11-03，手册 T.4） | `ActionRunTests` | 已通过：2026-11-04，A 槽 Electric P8，EPLAN 2026.0.3.25421，main `9faf3790`：`ActionRunTests` 19/19。 |
| ADR-0049 P12 占位符 默认预览（2026-11-03，手册 T.4） | `PlaceholderApplyTests` | 已通过：2026-11-03 迁移后 `PlaceholderApplyTests` 3/3。 |
| ADR-0049 P11 翻译主数据 默认预览（2026-11-03，手册 T.4） | `TranslationImportTests` | 已通过：2026-11-05，B 槽 Pro Panel，部件库和翻译库都指向 scratch 副本（`MasterDataOnCopyTests`，A103）：`TranslationImportTests` 通过，正在用的库哈希不变。 |
| ADR-0049 P10 部件主数据 默认预览（2026-11-03，手册 T.4） | `PartImportTests`、`PartSyncTests` | 已通过：2026-11-05，B 槽 Pro Panel，部件库和翻译库都指向 scratch 副本（`MasterDataOnCopyTests`，A103）：`PartImportTests`、`PartSyncTests` 通过，正在用的库哈希不变。 |
| ADR-0049 P09 项目语言 默认预览（2026-11-03，手册 T.4） | `LanguageSetTests`、`LanguageRemoveTests`、`ProjectTranslateTests` | 已通过：2026-11-04，A 槽 Electric P8，EPLAN 2026.0.3.25421，main `9faf3790`：`LanguageRemoveTests` 3/3、`ProjectTranslateTests` 2/2；2026-11-03 迁移后 `LanguageSetTests` 4/4。 |
| ADR-0049 P08 工程数据导入 默认预览（2026-11-03，手册 T.4） | `ImportListTests`、`ImportPlcTests`、`ImportPxfTests` | 部分通过：2026-11-04，A 槽 Electric P8，EPLAN 2026.0.3.25421，main `9faf3790`：`ImportListTests` 2/2、`ImportPxfTests` 2/2。未完成：`ImportPlcTests` 缺 PLC 交换文件夹具，跳过。 |
| ADR-0049 P07 图形/审图导入 默认预览（2026-11-03，手册 T.4） | `ImportDxfTests`、`ImportPdfCommentsTests` | 部分通过：2026-11-03 迁移后 `ImportDxfTests` 4/4。未完成：`ImportPdfCommentsTests` 缺带便笺的 PDF。 |
| ADR-0049 P06 生成、报表与检查默认预览（2026-11-03，手册 T.4） | `CheckAndReportTests`、`CableGenerateTests`、`AcceptanceRedrawTests`、`CableBusStarSpikeTests`、`CableDefinitionTests`、`ConnectionCreateTests`、`DevicePlaceTests`、`InterruptionPairingSpikeTests`、`PageReviewTests`、`PlacementEditSpikeTests`、`PlacementMoveTests` | 已通过：A 槽 Electric P8，EPLAN 2026.0.3.25421，main `9faf3790`：2026-11-05 `AcceptanceRedrawTests` 1/1、`DevicePlaceTests` 26/26、`ConnectionCreateTests` 25/25、`PlacementEditSpikeTests` 15/15；2026-11-04 `CheckAndReportTests` 7/7、`CableGenerateTests` 1/1、`PageReviewTests` 8 过 1 跳过（cable 修复步骤按设计跳过）；2026-11-03 迁移后 `CableDefinitionTests` 2/2、`PlacementMoveTests` 1/1。电缆和中断点探针沿用 11-02 结果。 |
| ADR-0049 P05 编号工具默认预览（2026-11-03，手册 T.4） | `DeviceRenumberTests`、`CableRenumberTests`、`TerminalRenumberTests`、`ConnectionNumberTests` | 已通过：2026-11-04，A 槽 Electric P8，EPLAN 2026.0.3.25421，main `9faf3790`：`CableRenumberTests` 4/4、`TerminalRenumberTests` 3/3；2026-11-03 迁移后 `DeviceRenumberTests` 2/2、`ConnectionNumberTests` 2/2。 |
| ADR-0049 P04 设备与属性编辑默认预览（2026-11-03，手册 T.4） | `WriteSafetyTests`、`PropertySetToolTests`、`DeviceRenumberTests` | 已通过：2026-11-04，A 槽 Electric P8，EPLAN 2026.0.3.25421，main `9faf3790`：`PropertySetToolTests` 1/1、`WriteSafetyTests` 4/4；2026-11-03 迁移后 `DeviceRenumberTests` 2/2。 |
| ADR-0049 P03 页面破坏性操作默认预览（2026-11-03，手册 T.4） | `PageDeleteTests`、`PageRenumberTests`、`ObjectResolveTests`；使用 `DestructiveHost.DeleteAsync` 清理的写测试 | 已通过：2026-11-04，A 槽 Electric P8，EPLAN 2026.0.3.25421，main `9faf3790`：`ObjectResolveTests` 1/1；2026-11-03 迁移后 `PageDeleteTests` 4/4、`PageRenumberTests` 2/2。 |
| ADR-0049 P02 页面编辑默认预览（手册 T.4） | `PageUpdateFrameTests`、`PageTextReplaceTests`、`AcceptanceRedrawTests`、`DevicePlaceTests`、`ImportDxfTests`、`MacroInsertTests`、`PlanDependencyPredictionTests`、`PlanStepGuardWriteTests`、`PlanTaskAcceptanceScenariosTests`、`PlcDisplaySpikeTests`、`PlcOverviewSpikeTests`、`ProjectTranslateTests`、`SnapshotWriteTests`、`WriteSafetyTests`；使用 `DestructiveHost` 的建页前置 | 已通过：A 槽 Electric P8，EPLAN 2026.0.3.25421，main `9faf3790`：2026-11-05 `AcceptanceRedrawTests` 1/1、`DevicePlaceTests` 26/26；2026-11-04 `PageUpdateFrameTests`、`PageTextReplaceTests`、`PlanDependencyPredictionTests` 各 1/1、`PlanTaskAcceptanceScenariosTests` 5/5、`ProjectTranslateTests` 2/2、`MacroInsertTests` 2/2、`WriteSafetyTests` 4/4；2026-11-03 迁移后 `ImportDxfTests` 4/4、`PlanStepGuardWriteTests` 1/1、`SnapshotWriteTests` 1/1。PLC 探针沿用 11-02 结果。 |
| ADR-0049 绘图写入默认预览（2026-11-03，手册 T.4） | `DevicePlaceTests`、`ConnectionCreateTests`、`MacroInsertTests`、`PlacementTextCreateTests`、`PlacementMoveTests`、`PlacementEditSpikeTests`、`AcceptanceRedrawTests`；以及调用绘图工具的 `ActionTryTests`、`CableBusStarSpikeTests`、`CableDefinitionTests`、`DanglingInterruptionTests`、`InterruptionPairingSpikeTests`、`InterruptionVariantSpikeTests`、`LayoutGuardTests`、`PageReviewTests`、`PartsFirstSpikeTests`、`PlanDependencyPredictionTests`、`PlcDisplaySpikeTests`、`PlcOverviewSpikeTests`、`SelectionSpikeTests` | 已通过：A 槽 Electric P8，EPLAN 2026.0.3.25421，main `9faf3790`：2026-11-05 `DevicePlaceTests` 26/26、`ConnectionCreateTests` 25/25、`AcceptanceRedrawTests` 1/1、`DanglingInterruptionTests` 5/5、`LayoutGuardTests` 4/4（另有一个探针用例要专门夹具，未计入）、`PlacementEditSpikeTests` 15/15、`PartsFirstSpikeTests` 1/1、`SelectionSpikeTests` 的产品用例通过（原生探针用例一次撤销后线仍在，未通过，它只走探针插件）；2026-11-04 `MacroInsertTests` 2/2、`PageReviewTests` 8 过 1 跳过；2026-11-03 迁移后 `PlacementMoveTests`、`ActionTryTests`、`CableDefinitionTests`。`PlacementTextCreateTests` 见 G9 行。探针沿用 11-02 结果。绘图评测：2026-11-10 用 Codex（`--agent codex`）跑 5 道绘图题 5/5、两道规则插件题 2/2，评测不代替 Large。 |
| T.4 V1 读取调用上下文配方（2026-11-03，负责人 brief） | `BatchVerification` | 已通过：2026-11-03，B 槽 Pro Panel：`BatchVerification` 1/1。 |
| SPECIAL 中断点与 BP 默认（2026-11-02，负责人 brief；A81/A83） | `ConnectionCreateTests`、`DanglingInterruptionTests` | 已通过：2026-11-05，A 槽 Electric P8，EPLAN 2026.0.3.25421，main `9faf3790`：`ConnectionCreateTests` 25/25、`DanglingInterruptionTests` 5/5。 |
| parts-first drawing spike（2026-11-02，负责人 brief；A82） | `PartsFirstSpikeTests` | 已通过：2026-11-05，A 槽 Electric P8，EPLAN 2026.0.3.25421，main `9faf3790`：`PartsFirstSpikeTests` 1/1。探针只收集事实，结论在 A82、A95。 |
| plugin libraries P1 C8（2026-11-02 brief） | `ConnectionCreateTests`、`DanglingInterruptionTests`、`DevicePlaceTests` | 已通过：2026-11-05，A 槽 Electric P8，EPLAN 2026.0.3.25421，main `9faf3790`：`ConnectionCreateTests` 25/25、`DanglingInterruptionTests` 5/5、`DevicePlaceTests` 26/26。 |
| 小项目重绘 16 页只读盘点（2026-11-02 负责人 brief） | `SmallProjectInventorySpikeTests` | 已通过：2026-11-03，A 槽：`SmallProjectInventorySpikeTests` 1/1，只读。 |
| 报表生成 R2a/R2b（2026-11-03，负责人 brief；A84） | `ReportCreateTests` | 已通过：2026-11-05，A 槽 Electric P8，EPLAN 2026.0.3.25421，main `9faf3790`：`ReportCreateTests` 1/1。 |
| 报表方案数据修正（2026-11-03，负责人 brief；`fix(tools): carry report scheme data from page_get to report_create`；A84） | `ReportCreateTests` | 已通过：2026-11-05，A 槽 Electric P8，EPLAN 2026.0.3.25421，main `9faf3790`：`ReportCreateTests` 1/1。 |
| 报表块读取 R1 及方案数据修正（2026-11-03，负责人 brief；A84） | `ReportReadTests` | 已通过：2026-11-04，A 槽 Electric P8，EPLAN 2026.0.3.25421，main `9faf3790`：`ReportReadTests` 1/1。 |
| 报表块探测 T.4（2026-11-03，负责人 brief；P-01） | `ReportBlockSpikeTests` | 已通过：2026-11-03，A 槽：`ReportBlockSpikeTests` 1/1，结论在 A84。 |
| 崩溃成员拒绝与失联停跑（`feat/crash-refusals`，2026-11-02，负责人 brief；A77） | `BatchVerification`、`ApiTryTests` | 部分通过：2026-11-04，A 槽 Electric P8，EPLAN 2026.0.3.25421，main `9faf3790`：`ApiTryTests` 13/13。未完成：批量验证没有遇到真实失联，失联后停止并暂停写入未实测；不复现原生崩溃。 |
| 批量验证与 main 生成表合并（`feat/batch-verify`，2026-11-01，负责人 brief） | `BatchVerification`、`ApiTryTests`、`ActionTryTests` | 已完成：2026-11-01 已落地 main 68e9f61b，首批 10-31 结果导入后实机验证计数 171 → 228；两槽续跑的结果由 e7a19f70、68da9b96 导入。失联后停跑见“崩溃成员拒绝”行。 |
| 移动后重新分配电缆芯线（后续，2026-11-02 A73） | `PageReviewTests`（cable 场景） | 待设计：移动电缆会丢失芯线归属且生成不恢复；先做一次探测（产品建的带芯线模板电缆，移动、生成后试原生绑定与重新分配导线），确认后再定修复工具。当前 cable 场景只验证检测与目标坐标，修复步骤跳过。 |
| C1 服务入口（历史 brief/提交待补定位；架构第 14 节的安全机制记录） | `ApiTryTests` | 已通过：2026-11-04，A 槽 Electric P8，EPLAN 2026.0.3.25421，main `9faf3790`：`ApiTryTests` 13/13。 |
| C2 未验证 action（历史 brief/提交待补定位；架构第 14 节的安全机制记录） | `ActionTryTests` | 已通过：2026-11-03 迁移后 `ActionTryTests` 1/1。 |
| P06 功能与影响闭包（历史 brief/提交待补定位） | `PlanFunctionsTests`、`PlanImpactTests` | 已通过：2026-11-04，A 槽 Electric P8，EPLAN 2026.0.3.25421，main `9faf3790`：`PlanFunctionsTests` 3/3、`PlanImpactTests` 3/3。 |
| D1 电缆星形（历史 brief/提交待补定位；2026-11-02 客户 D1/D2 brief） | `CableBusStarSpikeTests`、`ConnectionCreateTests` | 已通过：2026-11-05，A 槽 Electric P8，EPLAN 2026.0.3.25421，main `9faf3790`：`ConnectionCreateTests` 25/25；探针 `CableBusStarSpikeTests` 4/4（11-02）。 |
| D2 中断点配对（历史 brief/提交待补定位；2026-11-02 客户 D1/D2 brief） | `InterruptionPairingSpikeTests`、`DanglingInterruptionTests`、`ConnectionCreateTests` | 已通过：2026-11-05，A 槽 Electric P8，EPLAN 2026.0.3.25421，main `9faf3790`：`ConnectionCreateTests` 25/25、`DanglingInterruptionTests` 5/5；探针 `InterruptionPairingSpikeTests` 5/5（11-02）。 |
| 项目备份（历史 brief/提交待补定位） | `ProjectBackupTests` | 部分通过：2026-11-04，A 槽 Electric P8，EPLAN 2026.0.3.25421，main `9faf3790`：`ProjectBackupTests` 2 过。未完成：0 页副本缺夹具，跳过。 |
| PDF 审图便笺导入（历史 brief/提交待补定位） | `ImportPdfCommentsTests` | 受阻：准备 README 要求的带便笺 PDF；运行后另核对一次撤销及重复导入不留空步。 |
| 绘图几何 T0a（历史 brief/提交待补定位） | `DrawingGeometryTests` | 已通过：2026-11-05，A 槽 Electric P8，EPLAN 2026.0.3.25421，main `9faf3790`：`DrawingGeometryTests` 1/1（`EPLANMCP_DRAWING_VARIANT_BIN` 指向 Electric P8 Bin）。 |
| PLC 无冲突导入（历史 brief/提交待补定位） | `ImportPlcTests`（现类主要覆盖冲突）；正向用例待补 | 受阻：准备无冲突夹具，新增同名对象、提交后一次撤销及恢复未验证。 |
| 自动电缆正向生成（历史 brief/提交待补定位） | `CableGenerateTests`；正向夹具/用例待补 | 受阻：`CableGenerateTests` 1/1（2026-11-04）只证明已有电缆不会重复生成；正向验收要工程师准备可新增电缆的夹具。 |
| M9 `eplan_part_build`（负责人 2026-11-01 决定） | `PartBuildTests` | 待运行：2026-11-10 合入 main（默认改为预览）。只在部件库副本上运行（`MasterDataOnCopyTests` 的做法，A103）：预览列出新建和字段差异，执行后读回字段、正在用的库哈希不变；有错误行时整批不执行。EDZ 导入另待负责人样本。 |
| 官方 action 离线导入（`feat/official-actions`，2026-11-01 负责人 brief） | `BatchVerification`、`ActionTryTests`、`ActionCatalogTests` | 部分通过：2026-11-04 `ActionCatalogTests` 通过；2026-11-03 `ActionTryTests` 1/1、B 槽 `BatchVerification` 1/1。未完成：137 个官方行里批量只通过 1 行，127 行缺上下文，在 action 主线继续。 |

每项通过后保留本行，补日期、源码/插件版本、命令、结果与证据链接。部分通过继续列出未完成用例。发布范围内待验项须完成后才能发布。

## Workbench 人工验证缺口

以下记录来自原界面文档，属于桌面人工验证，未计为 EPLAN Large 通过。本次未运行界面或 EPLAN。

| 范围 | 状态与现有证据 |
|---|---|
| 字体与文字缩放 | 待核对：S18 未验证 Windows“文本大小”缩放以及 Maple Mono CN 在真实 WPF 中的 `->`、`!=`、`==` 字形/连字效果；见 [S18](../../spikes/S18-workbench-ui/README.md)。 |
| 通知与无障碍 | 待运行：AI4W FastPass、讲述人对新问题/倒计时/结束/状态的播报，以及托盘通知；原 S16 仅提供检查方向，本工作区未找到同名 spike 目录，见 [M5 验证入口](../design/2026-10-20-m5-operability.md#workbench-evidence)。 |
| DPI 与对比主题 | 待运行：100–200% 与混合 DPI、1366×768@100%、1920×1080@150%、文本大小 150%，以及 Aquatic/Desert/Dusk/Night sky 四套系统对比主题。 |
| 高对比资源差异 | 2026-11-01 原表与 `Tia/Themes/Palette.HighContrast.xaml` 对 `Primer.cardSel`、`Primer.textFaint` 的映射不同；项目包装 `Themes/Palette.HighContrast.xaml` 已覆盖 `cardSel`、`selectedText`、`textFaint`。保留原始差异及包装事实，整体符合性仍待人工验证。 |

## 最近移出（已实机通过）

- 2026-11-07，5 道绘图题在会话里用 MCP 工具实画（Electric P8，示例项目副本）：按题目的检查项全部满足；第 5 题先后补了直角交叉和符号读回
- 2026-11-07，第二批工具：`ProjectToolsTests` 的屏蔽（`Shield_OverTwoWires_ListsThem_OneUndoRestores`）、PLC 生成（`PlcGenerate_JoinsTheOverviewDevice_OneUndoRemovesThePage`）、位置框轮廓（`LocationBoxOutline_KeepsTheNotch`）各 1/1（B 槽）
- 2026-11-06，结构标识符排序和主数据更新：`ProjectToolsTests` 的 `Order_SortsTheBlock_ReadsBack_OneUndoRestores`、`MasterDataUpdate_NoDialog_ProjectStaysReadable` 各 1/1（B 槽），main 553abd80
- 2026-11-02，页面审查第 1 片（含 Cable 定义点修复、电缆建议改为重画）：`PageReviewTests` 8 项通过、cable 修复步骤按设计跳过，`PlacementMoveTests` 1/1，`AcceptanceRedrawTests` 按已知跳过结束（B 槽 Pro Panel），main 714d6200
- 2026-11-01，句柄链与设置文本修复：`ApiTryTests` 13/13（A 槽 Electric P8，变基到含生命周期的 main 后），main c5672ab9
- 2026-11-01，原生前置条件、无效对象枚举与事务诊断：`ApiTryTests` 10/10、`DevicePlaceTests` 8/8（B 槽 Pro Panel，合并设置/句柄后）；此前 A 槽 8/8、7/7、`ConnectionCreateTests` 14/14、`PlacementMoveTests` 1/1，main 0113de9d
- 2026-11-01，数组/out 与设置、句柄、原生前置条件合并：`ApiTryTests` 11/11（B 槽，含 `ScratchCopy_ArrayWriteOneUndoRestoresGeometryAndEarlierHistory`），main c13de0fe
- 2026-11-01，默认位置文字只警告：`AcceptanceRedrawTests` 13 个阶段完成、problems 为空（A 槽），main 35bb10d6
- 2026-11-01，布局护栏合并 main 后第七轮：LayoutGuard 4/4、DevicePlace 8/8、ConnectionCreate 14/14、CableDefinition 2/2、PlacementMove 1/1、DanglingInterruption 2/2、PlacementEditSpike 15/15、验收 13 阶段（A 槽）；另在 B 槽 Pro Panel 验收完整通过，main 35bb10d6
- 2026-11-01，设置读取：`ApiTryTests` 9/9（A 槽），合并句柄后 10/10，main d17ea6a3
- 2026-11-01，对象句柄：`ApiTryTests` 10/10（B 槽，合并设置后），main d8bcadc3
- 2026-11-01，创建/删除（工厂复用已有 Create 组）：`ApiTryTests` 12/12（B 槽），main 51f38a68
- 2026-11-01，`DanglingInterruptionTests` 2/2（A 槽，布局护栏合并后）

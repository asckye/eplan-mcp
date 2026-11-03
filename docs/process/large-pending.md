# Large 待验台账

> 截至 2026-11-01，从现有规范和 Large README 汇入的明确待验记录；不推定其他类已经通过。历史条目缺少 brief/提交时标“待补定位”，后续由维护者补齐。新增行为变更落到 main 前必须写明变更集、受影响类和状态，且提交正文注明待验。规则、状态与完成条件见 [实机验证](live-verification.md)。

| 变更集 / 定位 | 受影响 Large 类 | 状态与剩余条件 / 证据 |
|---|---|---|
| S01 写失败状态（2026-11-03 负责人 brief；ERR-01、P-05） | `WriteSafetyTests`、`SnapshotWriteTests` 及使用公共事务的页面、设备、属性、导入、action、API 试写类 | 待运行：本次禁止 EPLAN；需验证写入未知结果、真实回滚及原有写入安全回归。Small 假对象覆盖未分类异常、回滚证据、结果查询与不重放；未改 Large 断言，不宣称实机通过。 |
| ADR-0049 P08 工程数据导入 默认预览（2026-11-03，手册 T.4） | `ImportListTests`、`ImportPlcTests`、`ImportPxfTests` | 待运行：本次禁止 EPLAN。执行调用显式传 `dry_run: false`，预览保持 true；原审批、读回、失败、备份和撤销断言保留。 |
| ADR-0049 P07 图形/审图导入 默认预览（2026-11-03，手册 T.4） | `ImportDxfTests`、`ImportPdfCommentsTests` | 待运行：本次禁止 EPLAN。执行调用显式传 `dry_run: false`，预览保持 true；原审批、读回、失败、备份和撤销断言保留。 |
| ADR-0049 P06 生成、报表与检查默认预览（2026-11-03，手册 T.4） | `CheckAndReportTests`、`CableGenerateTests`、`AcceptanceRedrawTests`、`CableBusStarSpikeTests`、`CableDefinitionTests`、`ConnectionCreateTests`、`DevicePlaceTests`、`InterruptionPairingSpikeTests`、`PageReviewTests`、`PlacementEditSpikeTests`、`PlacementMoveTests` | 待运行：本次禁止 EPLAN；执行调用显式传 `dry_run: false`，预览保留 true，原有读回、失败、审批、备份、撤销及范围外不变断言保留。 |
| ADR-0049 P05 编号工具默认预览（2026-11-03，手册 T.4） | `DeviceRenumberTests`、`CableRenumberTests`、`TerminalRenumberTests`、`ConnectionNumberTests` | 待运行：本次禁止 EPLAN；执行调用显式传 `dry_run: false`，预览保留 true，原有读回、失败、审批、备份、撤销及范围外不变断言保留。 |
| ADR-0049 P04 设备与属性编辑默认预览（2026-11-03，手册 T.4） | `WriteSafetyTests`、`PropertySetToolTests`、`DeviceRenumberTests` | 待运行：本次禁止 EPLAN；执行调用显式传 `dry_run: false`，预览保留 true，原有读回、失败、审批、备份、撤销及范围外不变断言保留。 |
| ADR-0049 P03 页面破坏性操作默认预览（2026-11-03，手册 T.4） | `PageDeleteTests`、`PageRenumberTests`、`ObjectResolveTests`；使用 `DestructiveHost.DeleteAsync` 清理的写测试 | 待运行：本次禁止 EPLAN。删除/编号及清理执行调用显式传 `dry_run: false`，原预览保留 true；审批、备份、失败、读回和撤销断言保留。两个 attended 评测待验。 |
| ADR-0049 P02 页面编辑默认预览（手册 T.4） | `PageUpdateFrameTests`、`PageTextReplaceTests`、`AcceptanceRedrawTests`、`DevicePlaceTests`、`ImportDxfTests`、`MacroInsertTests`、`PlanDependencyPredictionTests`、`PlanStepGuardWriteTests`、`PlanTaskAcceptanceScenariosTests`、`PlcDisplaySpikeTests`、`PlcOverviewSpikeTests`、`ProjectTranslateTests`、`SnapshotWriteTests`、`WriteSafetyTests`；使用 `DestructiveHost` 的建页前置 | 待运行：本次禁止 EPLAN。直接执行页面调用显式 false，保留全部读回、审批、失败、撤销和范围断言；页面评测及建页前置待实机验收。 |
| ADR-0049 绘图写入默认预览（2026-11-03，手册 T.4） | `DevicePlaceTests`、`ConnectionCreateTests`、`MacroInsertTests`、`PlacementTextCreateTests`、`PlacementMoveTests`、`PlacementEditSpikeTests`、`AcceptanceRedrawTests`；以及调用绘图工具的 `ActionTryTests`、`CableBusStarSpikeTests`、`CableDefinitionTests`、`DanglingInterruptionTests`、`InterruptionPairingSpikeTests`、`InterruptionVariantSpikeTests`、`LayoutGuardTests`、`PageReviewTests`、`PartsFirstSpikeTests`、`PlanDependencyPredictionTests`、`PlcDisplaySpikeTests`、`PlcOverviewSpikeTests`、`SelectionSpikeTests` | 待运行：本次禁止 EPLAN。执行调用已显式传 `dry_run: false`，预览调用保持 true；读回、失败、备份、撤销及范围外不变断言全部保留。四个绘图评测任务同样待实机验收。 |
| SPECIAL 中断点与 BP 默认（2026-11-02，负责人 brief；A81/A83） | `ConnectionCreateTests`、`DanglingInterruptionTests` | 待运行：本次禁止运行 EPLAN。待核验默认 BP 双连接点、显式 BPIN/BPOUT/BPF 的符号名与变体、两端各自实际接线，以及既有配对、完整图形和单步撤销断言；辅助原生读回已支持多连接点。 |
| parts-first drawing spike（2026-11-02，负责人 brief；A82） | `PartsFirstSpikeTests` | 待运行：本次禁止运行 EPLAN。待在 LargeWrite 新 Circuit 页观察部件检索、模板回滚、PLC/按钮功能放置、既有功能赋部件和图片导出；只收集原生事实，不改变产品契约，不写部件库。新增 `templates-place` 步骤记录 PLC 主功能创建、模板放置前后状态、逐次放置结果和重复功能，并对照匹配引脚名称的赋部件结果。第三轮新增 `place-unplaced` 与 `device-list`，待观察未放置功能的 `Place` 返回对象身份、设备清单模板的 `PlaceAt` 放置及两条路径提交后的功能与重复项读回。第四轮新增 `rename-unplaced`，待对照 `name` 与 `properties` 策略在事务内及提交后对主功能和未放置功能的改名结果与器件归属。第五轮新增 `create-named`，待观察按最终名称创建和放置功能、迁移主功能标志并移除原件，以及提交后重复名称、部件引用与剩余四个未放置辅助功能的器件归属。 |
| plugin libraries P1 C8（2026-11-02 brief） | `ConnectionCreateTests`、`DanglingInterruptionTests`、`DevicePlaceTests` | 待运行：本次禁止 EPLAN；验证插件默认中断点与新端子排定义、显式参数优先、计划过期、读回及原有撤销断言。Small/Medium 仅覆盖假 Bridge 与纯逻辑，不代替实机。 |
| 小项目重绘 16 页只读盘点（2026-11-02 负责人 brief） | `SmallProjectInventorySpikeTests` | 待运行：本次禁止运行 EPLAN。沿用 `SampleProjectInventorySpikeTests` 的 `LiveEplan` 只读夹具，显式定位项目，完整翻页读取指定 16 页的图形、对象、功能、符号、部件和可见属性；不可读项记为缺口。待生成 `%TEMP%\eplanmcp-spikes\small-project-inventory.json` 并核对每页摘要；不写入、导航或生成夹具项目。 |
| 报表生成 R2a/R2b（2026-11-03，负责人 brief；A84） | `ReportCreateTests` | 待运行（NOT RUN）：本次禁止 EPLAN；先只读示例夹具 `&AAB1/1`，再仅在 LargeWrite 用其 type/form 生成目录及 `TerminalDiagram` / `F13_008` / `Location designation_A3`，核对 `pages`、`new_pages`、类型与表格及最后一次写入的单步撤销。主线程时长未测。 |
| 报表块读取 R1（2026-11-03，负责人 brief；A84） | `ReportReadTests` | 待运行（NOT RUN）：本次禁止 EPLAN；正式 `eplan_page_get` 只读示例夹具 `&AAB1/1`，核对报表类型、表格及方案名称字段，不输出方案数据。 |
| 报表方案数据修正（2026-11-03，负责人 brief；`fix(tools): carry report scheme data from page_get to report_create`；A84） | `ReportCreateTests` | 待运行（NOT RUN）：03:39 目录通过，端子图因目标项目没有 `Location designation_A3` 且未传数据而失败。修正后须只读示例 `&AAB1/1` 与 `+A1&EMA2/2`，仅在 LargeWrite 生成目录，并原样传入端子图方案名和四项数据；核对 `pages`、`new_pages`、类型与表格及两次写入分别单步撤销。主线程时长未测；本次禁止 EPLAN。 |
| 报表块读取 R1 及方案数据修正（2026-11-03，负责人 brief；A84） | `ReportReadTests` | 待运行（NOT RUN）：本次禁止 EPLAN；正式 `eplan_page_get` 只读示例夹具 `&AAB1/1`，核对报表类型、表格与方案字段。方案数据原样返回，每项上限 4000 字符，超出截断并警告。 |
| 报表块探测 T.4（2026-11-03，负责人 brief；P-01） | `ReportBlockSpikeTests` | 待运行（NOT RUN）：本次禁止运行 EPLAN。待只读观察示例夹具 7 页的 ReportBlock 全字段及名称属性列表，再在 LargeWrite 按原设置生成目录和端子图，核对 Type/FormName、事务失败回滚和提交后新包装读回；证据写 `%TEMP%\eplanmcp-spikes\report-blocks-*.json`。 |
| 官方 action 离线导入（`feat/official-actions`，2026-11-01 负责人 brief） | `BatchVerification`、`ActionTryTests`、`ActionCatalogTests` | 待运行：批量 action 仅取官方 action/TYPE 行，按第一条官方示例准备参数；需要文件、页或选择上下文仍为 needs_context。此次禁止运行 EPLAN；审批、scratch、unknown 停止与撤销断言保留。 |
| 崩溃成员拒绝与失联停跑（`feat/crash-refusals`，2026-11-02，负责人 brief；A77） | `BatchVerification`、`ApiTryTests` | 待运行：本次禁止运行 EPLAN；拒绝写侧、保留 MacroBox 读取、失联后停止并持久暂停需验证。崩溃成员不再试执行，原生 target/value 未提供，不宣称复现原生崩溃。 |
| 批量验证与 main 生成表合并（`feat/batch-verify`，2026-11-01，负责人 brief） | `BatchVerification`、`ApiTryTests`、`ActionTryTests` | 进行中：2026-11-01 已落地 main 68e9f61b，首批 10-31 结果导入后实机验证计数 171 → 228；当夜两槽循环续跑（单次运行在结果未知或状态不可信的条目处按设计停止并换新副本续跑），次日汇总导入。 |
| 移动后重新分配电缆芯线（后续，2026-11-02 A73） | `PageReviewTests`（cable 场景） | 待设计：移动电缆会丢失芯线归属且生成不恢复；先做一次探测（产品建的带芯线模板电缆，移动、生成后试原生绑定与重新分配导线），确认后再定修复工具。当前 cable 场景只验证检测与目标坐标，修复步骤跳过。 |
| C1 服务入口（历史 brief/提交待补定位；架构第 14 节的安全机制记录） | `ApiTryTests` | 待运行：服务读、写撤销与相关失败路径；测试已写，尚无实机结果。 |
| C2 未验证 action（历史 brief/提交待补定位；架构第 14 节的安全机制记录） | `ActionTryTests` | 待运行：scratch 限定、审批、事务、模态/unknown 等；规范注明 Large 已写待运行。 |
| P06 功能与影响闭包（历史 brief/提交待补定位） | `PlanFunctionsTests`、`PlanImpactTests` | 待运行：Large README 明确尚未实跑；保留完整性与边界断言，缺页证据的现有拒绝行为需按当前常设规则另行评审，不在文档任务中弱化测试。 |
| D1 电缆星形（历史 brief/提交待补定位；2026-11-02 客户 D1/D2 brief） | `CableBusStarSpikeTests`、`ConnectionCreateTests` | 待运行：新增 SPECIAL 全部 T 变体/引脚枚举及 Down 分支、Left/Right 主干各变体的逻辑端点输出；产品可选 variant 仍需实机回归。Spike 只断言搭建成功，不能代替产品验收；本次禁止 EPLAN。 |
| D2 中断点配对（历史 brief/提交待补定位；2026-11-02 客户 D1/D2 brief） | `InterruptionPairingSpikeTests`、`DanglingInterruptionTests`、`ConnectionCreateTests` | 待运行：两页 L/R、P1/P2 默认及排序码 L=1、P2=2、R=3、P1=4 的原生对端输出，24809 放置事务写入/读回回归；配对规则须以实机证据回填 api-notes，本次禁止 EPLAN。 |
| 项目备份（历史 brief/提交待补定位） | `ProjectBackupTests` | 待运行：README 注明仅编译；非空/零页副本及无副作用证据尚待运行。 |
| PDF 审图便笺导入（历史 brief/提交待补定位） | `ImportPdfCommentsTests` | 受阻：准备 README 要求的带便笺 PDF；运行后另核对一次撤销及重复导入不留空步。 |
| 绘图几何 T0a（历史 brief/提交待补定位） | `DrawingGeometryTests` | 待运行：README 注明本次未运行；实际空页、图框几何及辅助程序环境待核对。 |
| PLC 无冲突导入（历史 brief/提交待补定位） | `ImportPlcTests`（现类主要覆盖冲突）；正向用例待补 | 受阻：准备无冲突夹具，新增同名对象、提交后一次撤销及恢复未验证。 |
| 自动电缆正向生成（历史 brief/提交待补定位） | `CableGenerateTests`；正向夹具/用例待补 | 受阻：README 要求工程师准备可新增电缆夹具；不能以已有测试替代正向验收。 |
| M9 `eplan_part_build`（负责人 2026-11-01 决定） | 专用 Large 类待补定位（当前检出未找到同名类） | 受阻：实现已完成，实机验证延后；EDZ 导入另待负责人样本，不宣称已通过。 |

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

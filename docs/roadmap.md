# 路线图

截至 2026-11-17，里程碑状态只在本页维护；设计与 ADR 保留方案和决策历史。状态：已完成（done）、进行中（in progress）、计划中（planned）、等待负责人（waiting on owner）、暂停（paused）。全路线图目标为 **2027 年 1 月底**；以下日期为估计。

v0.5.0 于 **2026-11-05** 发布（预览版，带 ADR-0049 的破坏性变化），v0.5.1 于 **2026-11-09**、v0.5.2 于 **2026-11-13**、v0.6.0 于 **2026-11-15**（M8 收尾，不做精确重绘）、**v0.7.0 于 2026-11-17** 发布（页面体检、官方项目回归、`eplan_plc_rack`）；之后有已验证内容就出预览版。发布范围和未实机验证项见 CHANGELOG 和[待验台账](process/large-pending.md)。

| 当前主线（负责人 2026-11-06 决定） | 状态 | 发布 / 预计时间 | 设计依据 | ADR |
|---|---|---|---|---|
| **优先：在 Electric P8 里复刻负责人给的 71 页图纸。** 器件从部件库和宏来，报表自动生成。手绘的 25 页文字和连线都到 99% 以上；标题栏用 `eplan_project_update` 填写（[ADR-0052](adr/0052-write-title-block-project-properties.md)），结构标识符用 `eplan_structure_update` 一次写好 | 进行中 | 排在下面三个阶段之前 | — | ADR-0052 |
| **AI 按文字描述、按选定国家的标准画图。** 验收用 `evals/tasks-drawing/`，每个标准一组题。第 1–3 阶段已完成（2026-11-10）；剩下的公司默认规则包和从 IO 清单生成 PLC 页见下方 M12 行 | 第 1–3 阶段已完成 | v0.5.1 起，签核版随后续预览版 | [M12](design/2026-10-31-m12-review-and-generation.md) | [0047](adr/0047-plugin-libraries.md) |
| 第 1 阶段：修中断点写入的回滚错误，跑 5 道绘图题，把绘图经验写进工具用法。2026-11-10 用 Codex（`--agent codex`）跑 5 道绘图题：5/5 通过，参数重试 2、0、2、12、0（重画题的 8 次是把拐角、T 节点当器件放，已改成直接指向 `eplan_connection_create`）；另加“画图、审图、修正”题 `draw-review-fix` | 已完成（2026-11-10） | Unreleased | — | — |
| 第 2 阶段：IEC 60204-1、IEC 81346 规则插件，`eplan_page_review` 按规则审图；AI 画图、审图、修正的循环 | 已完成（2026-11-10）：两套内置插件由负责人签核到 1.0.0，见[签核记录](process/rule-pack-signoff-iec.md)；通过式烘芯炉按 IEC 审图 0 问题 | Unreleased | [M12](design/2026-10-31-m12-review-and-generation.md) | [0047](adr/0047-plugin-libraries.md) |
| 第 3 阶段：GB/T 5226.1、NFPA 79、NOM-001-SEDE 规则插件（放在私有插件仓库，不随公开仓库发布）；同一描述按不同标准出图，差异符合规则 | 已完成（2026-11-10）：三套均由负责人签核到 1.0.0 并签名；Codex 标准题 2/2；MX_NFPA 按 NFPA 79 + NOM 审图 0 问题 | — | 同上 | 同上 |
| 页面体检：`eplan_page_review` 三批检查（图框可用区域、整页布局规则、符号重叠报 error 并给空位、跨列、页面设置、线号、占位标识），按页面实际尺寸换算；8 个官方 EES 项目 256 页作误报回归，0 个 error（`OfficialCorpusReviewTests`，api-notes A145–A147）。剩余：阈值由规则插件和公司配置提供、读取提速、多页审查；连续审图时 EPLAN 偶发崩溃待查 | 三批已完成（2026-11-17）；剩余进行中 | **v0.7.0**；剩余随预览版 | [工具说明](tools/eplan_page_review.md)、[审图设计](design/2026-10-31-m12-review-and-generation.md) | [0046](adr/0046-readback-deviation-warnings.md) |
| 代码整理（负责人 2026-11-17 同意，不做整体重构）：按[工程开发规范](standards/README.md) 4.1 的规模阈值清理旧代码。复盘数据：2479 个方法中 173 个超过 60 行、11 个超过 200 行，三个目录各平铺 250–440 个文件。步骤：① 文档对齐 ② 目录按领域分组（纯移动）③ 拆超过 200 行的方法，体检改为一条检查一个类 ④ 页面场景模型从 `ConnectionCreatePlanner` 独立 ⑤ 门禁加方法规模检查（新增或改动的方法上限 100 行，旧的进豁免清单）⑥ 拆超大文档。①②⑤ 先做，③④ 与功能开发穿插 | 进行中（2026-11-17 开始） | 不随版本，行为不变 | 本页 | — |
| 规则插件未经工程师签核时保持草稿，只用于 scratch；工具只检查收录的规则，不保证符合标准。签核人目前是负责人 | 已确定（2026-11-10，签核人为负责人） | — | [M12 第 6 节](design/2026-10-31-m12-review-and-generation.md) | — |

下面这些不再卡住当前主线，只在画图需要时做；普查暂停，空闲时再跑。

| 原主线（负责人 2026-11-04 决定） | 状态 | 发布 / 预计时间 | 设计依据 | ADR |
|---|---|---|---|---|
| 全部 action：每个名称都要落到三种结果之一：可直接调用（实测后进白名单）、由工具覆盖、明确不做并说明原因。EPLAN 2026.0.3 注册 2498 个名称，Electric P8 按名称能找到 2266 个；普查 2026-11-12 全部跑完（2279 个名称有结果）：静默运行无改动 2042（其中返回失败 1164，多为缺上下文或参数），改设置 124（每批后已恢复），弹窗口 65，P8 中找不到 26，改项目 16（14 个一步撤销可恢复），关闭或删除项目 5，曾让 EPLAN 退出 8（已拒绝）。下一步按 action 族查官方参数文档，能参数驱动的复测进白名单，其余标为工具覆盖或明确不做 | 进行中 | 随预览版持续交付；按族分批 | [M10](design/2026-10-30-m10-capability-expansion.md)、[API Actions](eplan/api-actions.md) | [0021](adr/0021-action-run-whitelist.md)、[0030](adr/0030-action-catalog-tiers.md) |
| 按任务做专用工具：普查结果按任务族汇总成工具，API 优先，没有 API 用 action，都不行就写明阻塞。6 批顺序：① 页面编辑、宏、连接 ② 端子排、电缆、PLC ③ 修订、编号、翻译、报表 ④ 部件库、项目管理 ⑤ 预规划、符号与图框表格编辑器 ⑥ 3D。功能区 339 个入口是实用清单（按用途判断，未逐个实测）：有工具 70、部分有 28、只影响显示 36、要做 55、第 5 批 16、3D 91、做不了或不自动做 8、标题或云入口 34、待判断 1；逐项明细只留本机（ADR-0030） | 进行中（第 1 批已盘点） | 分批进预览版 | 本页 | [0006](adr/0006-tool-surface-design.md)、[0030](adr/0030-action-catalog-tiers.md) |
| 属性数据关联：每批实测谁是源头、谁是计算值、谁单独存，结论写入[数据关系](eplan/data-relationships.md) | 进行中 | 随每批工具 | [数据关系](eplan/data-relationships.md) | — |
| 操作层 API：4014 组里可调用 2748 组（68.5%），明确不做 756 组，到不了 505 组（12.6%），实机验证 1118 组（27.9%），2026-11-14 四轮结案后重算（ADR-0055 字段作句柄根、ADR-0056 带键链步、枚举值补全、Project 构造的服务）。到不了的 505 组里：形状不支持或歧义 98（负责人 2026-11-14 决定先放着）、主数据策略 74、3D 65。按需要补，不再要求先全部补齐 | 进行中 | 持续 | [实施计划](design/2026-10-31-api-completion.md)、[缺口 TSV](../catalog/operation-gaps.tsv) | [0032](adr/0032-generic-api-layer.md)、[0033](adr/0033-api-try-object-handles.md)、[0036–0045](adr/README.md) |
| M8 绘图与精确重绘：项目负责人 2026-11-14 决定不再以精确重绘为验收，直接发布 v0.6.0。最后结果：16 页小项目第 6 轮匹配率 76.99%（离线重放第 7 轮 78.21%），剩余差距按对象数：端子 99、属性文字 83、连接定义点 67（其中连接长度 65，作为功能继续做）、功能 57、连接 17 | 已完成（不做精确重绘） | **v0.6.0** | [M8](design/2026-10-27-m8-drawing.md)、[验收要求](design/2026-10-31-api-completion.md#45-客户页差异必须在重画前解决) | [0005](adr/0005-safety-model.md)、[0032](adr/0032-generic-api-layer.md) |
| 布局护栏 R1–R9、调用模板 `try_template`、页面审查 `eplan_page_review` 第 1 片 | 已实现，随 v0.5.0 预览版发布；未实机验证项见台账 | v0.5.0 预览版 | [绘图设计](design/2026-10-27-m8-drawing.md)、[审图设计](design/2026-10-31-m12-review-and-generation.md) | [0032](adr/0032-generic-api-layer.md) |

有已验证内容就发预览版（v0.5.x），每周五是最低频率。

| v0.7.0 之后（按推进顺序） | 状态 | 发布 | 设计依据 | ADR |
|---|---|---|---|---|
| M9 主数据与报表余项；EDZ 导入等待负责人样本；`eplan_part_build` 已于 2026-11-10 合入 main，同日在部件库副本上实机通过 | 等待负责人（EDZ）；其余计划中 | v0.6.0 之后，版本待定 | [API 主数据补齐](design/2026-10-31-api-completion.md)、[能力扩展](design/2026-10-30-m10-capability-expansion.md)（独立设计待写） | [0019](adr/0019-master-data-writes.md)、[0032](adr/0032-generic-api-layer.md) |
| 翻译库词条工具：`eplan_translation_keyword` 查词条的各语言译文（2026-11-14 实现），`eplan_translation_add` 新增词条、补空译文（2026-11-14 实现）。负责人 2026-11-14 先决定不让通用入口按路径打开主数据文件、改为专用工具，再选定走已实机验证的导出导入路线（api-notes A47），因此安全口径与 `eplan_translation_import` 一致（ADR-0019 审批加备份，不要求 scratch 副本），代价是改写已有译文、删除词条、新增语言做不到，由 EPLAN 翻译字典承担 | 已实现，Large 待跑 | Unreleased | [读](tools/eplan_translation_keyword.md)、[写](tools/eplan_translation_add.md) | [0019](adr/0019-master-data-writes.md)、[0054](adr/0054-api-try-master-data-handles.md) |
| 工具流：固定的作图顺序做成一次调用，给数据参数直接生成。第一步 `eplan_plc_rack`（PLC 机架与总览，一个事务、一次审批、一次撤销），之后是带参数的标准回路宏，再用配方 + `eplan_workflow_plan` 一次生成整个项目。负责人 2026-11-14 确认规格。`eplan_plc_rack` 第 1 版已实现并实机跑过两块 ET 200SP（A142）；第 2 版（供电中断点、卡件标签）和带参数的标准回路宏未开始 | 第一步已完成（2026-11-15），其余未开始 | `eplan_plc_rack` 随 **v0.7.0** | [eplan_plc_rack 规格](tools/eplan_plc_rack.md) | [0006](adr/0006-tool-surface-design.md) |
| M10 能力扩展：吸收 Canary 有益功能，action 部分已并入当前主线；其余 Canary 有益功能按 A/B/C 分级。负责人 2026-11-14 决定提前，与当前主线并行。已完成：发现模式（ADR-0057）、`eplan_session_start`、`eplan_session_close`/`restart`（ADR-0058，重启路径未实机验证）；下一步审计 `dispatched_by` 标记、系统消息工具 | 进行中（2026-11-14 提前） | 随预览版分批 | [M10](design/2026-10-30-m10-capability-expansion.md) | [0030](adr/0030-action-catalog-tiers.md)、[0031](adr/0031-private-extension-modules.md) |
| M12 国家规则包：IEC 60204-1 + IEC 81346 → GB/T 5226.1 → NFPA 79 已提到当前主线；这里只剩公司默认包和从 IO 清单生成 PLC 页。每包经工程师签核，不复制标准正文 | 计划中 | v0.6.0 之后，版本待定 | [M12](design/2026-10-31-m12-review-and-generation.md) | [0005](adr/0005-safety-model.md)、[0024](adr/0024-pack-trust-and-container.md)、[0029](adr/0029-phase3-engineering-records.md) |
| M11 PDF 图纸集转 EPLAN 项目：在 M9/M10 之后 | 计划中 | v0.6.0 之后，版本待定 | [M8 绘图基础](design/2026-10-27-m8-drawing.md)（独立设计待写） | [0005](adr/0005-safety-model.md)、[0032](adr/0032-generic-api-layer.md) |
| Workbench 改版：负责人 2026-11-14 决定提前实施，不再等 v0.6.0 | 进行中（2026-11-14 提前实施） | 随预览版分批 | [M5 界面设计](design/2026-10-20-m5-operability.md#45-界面技术fluent-2-的实现方式q1adr-0015)、[Workbench 实现记录](design/2026-10-20-m5-operability.md#workbench-implementation) | [0017](adr/0017-workbench-wpf-tia-theme.md)（2026-10-20 Accepted）；[0027](adr/0027-workbench-plan-review.md) 仍为 Proposed |

| 基础里程碑与其余计划 | 状态 | 发布 | 设计依据 | ADR |
|---|---|---|---|---|
| M0 启动 | 已完成 | v0.1.0 | [架构](architecture/architecture.md) | [0001](adr/0001-record-architecture-decisions.md) |
| M1 技术验证与端到端骨架 | 已完成 | v0.1.0 | [M1](design/2026-10-13-m1-skeleton.md) | [0002](adr/0002-host-bridge-process-model.md)、[0003](adr/0003-ipc-named-pipe-json-rpc.md) |
| M2 只读核心 | 已完成 | v0.1.0 | [M2](design/2026-10-18-m2-read-core.md)、[Agent 评测](design/2026-10-19-agent-evals-v0.md) | [0006](adr/0006-tool-surface-design.md) |
| M3 安全写入（含 M3b） | 已完成 | v0.1.0 | [M3](design/2026-10-19-m3-safe-writes.md)、[M3b](design/2026-10-20-m3b-placement.md) | [0005](adr/0005-safety-model.md)、[0010](adr/0010-write-outcomes.md)、[0011](adr/0011-approval-out-of-band.md) |
| M4 工程工作流 | 已完成 | v0.1.0；action 白名单 v0.2.0 | [M4](design/2026-10-20-m4-workflows.md) | [0012](adr/0012-export-tier.md)、[0013](adr/0013-destructive-backup.md)、[0021](adr/0021-action-run-whitelist.md) |
| M4c 更多工程工具 | 已完成（PDF 工具 Large 待运行） | v0.1.0–v0.4.0；PDF 便笺 v0.4.1 | [M4c](design/2026-10-21-m4c-more-tools.md) | [0019](adr/0019-master-data-writes.md)、[0022](adr/0022-domain-toolsets.md) |
| M5 可运维：ZIP、Workbench、虚拟机连接已实现；干净机器和跨机器验收待完成 | 进行中 | v0.1.0–v0.2.0 | [M5](design/2026-10-20-m5-operability.md) | [0016](adr/0016-per-user-installer.md)、[0017](adr/0017-workbench-wpf-tia-theme.md)、[0018](adr/0018-vm-http-transport.md)、[0020](adr/0020-no-self-hosted-runner.md) |
| M6 工程包：第 1–3 阶段已实现，第 4–5 阶段计划中 | 进行中 | 第 1 阶段 v0.1.0，第 2 阶段 v0.3.0，第 3 阶段 v0.4.1；其余待定 | [总设计](design/2026-10-21-m6-engineering-packs.md)、[第 2 阶段](design/2026-10-23-m6-phase2-plans.md)、[第 3 阶段](design/2026-10-26-m6-phase3-config-modules-rules.md) | [0023](adr/0023-plan-approval-and-backup.md)、[0024](adr/0024-pack-trust-and-container.md)、[0025](adr/0025-code-plugin-worker.md)、[0026](adr/0026-eplan-events-and-push.md)、[0028](adr/0028-plan-record-format.md)、[0029](adr/0029-phase3-engineering-records.md) |
| M7 多版本：验证 EPLAN 2024、2025、2027 | 计划中 | 待定，尚未排期 | [架构兼容性设计](architecture/architecture.md#9-质量场景可测试的验收标准)（无独立设计） | [0009](adr/0009-supported-eplan-versions.md) |
| M4b Pro Panel 3D 专用工具家族：第一批柜体 3D 布置（柜体、导轨、线槽、器件、布置图、删除）已实现；`eplan_connection_route` 布线已实现，未实机验证，等负责人导入 19 个部件后验证连接长度；其余 3D 按需要补 | 进行中 | 第一批 v0.5.2；布线 v0.6.0 | [第一批设计](design/2026-11-12-m4b-layout3d.md)、[API 覆盖设计](design/2026-10-21-api-capability-coverage.md)、[3D 范围](eplan/api-coverage.md#3-pro-panel-3dm4b) | [0005](adr/0005-safety-model.md)、[0032](adr/0032-generic-api-layer.md) |

协作流程：在 worktree 里完成变更，通过 `eng/scripts/local-gate.ps1` 后直接推送 main；不开 PR，CI 不阻塞（任务上限 60 分钟，2026-11-17）。打发布标签前要有发布提交上的一次成功 CI。Large 测试在唯一的 EPLAN 机器上串行运行。

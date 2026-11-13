# 路线图

截至 2026-11-12，里程碑状态只在本页维护；设计与 ADR 保留方案和决策历史。状态：已完成（done）、进行中（in progress）、计划中（planned）、等待负责人（waiting on owner）、暂停（paused）。全路线图目标为 **2027 年 1 月底**；以下日期为估计。

v0.5.0 于 **2026-11-05** 发布（预览版，带 ADR-0049 的破坏性变化），v0.5.1 于 **2026-11-09** 发布；之后每周按发布列车出预览版。下一个次版本是 **v0.6.0**，验收目标是小项目 16 页精确重绘。发布范围和未实机验证项见 CHANGELOG 和[待验台账](process/large-pending.md)。

| 当前主线（负责人 2026-11-06 决定） | 状态 | 发布 / 预计时间 | 设计依据 | ADR |
|---|---|---|---|---|
| **优先：在 Electric P8 里复刻负责人给的 71 页图纸。** 器件从部件库和宏来，报表自动生成。手绘的 25 页文字和连线都到 99% 以上；标题栏用 `eplan_project_update` 填写（[ADR-0052](adr/0052-write-title-block-project-properties.md)），结构标识符用 `eplan_structure_update` 一次写好 | 进行中 | 排在下面三个阶段之前 | — | ADR-0052 |
| **AI 按文字描述、按选定国家的标准画图。** 验收用 `evals/tasks-drawing/`，每个标准一组题。第 1–3 阶段已完成（2026-11-10）；剩下的公司默认规则包和从 IO 清单生成 PLC 页见下方 M12 行 | 第 1–3 阶段已完成 | v0.5.1 起，签核版随后续预览版 | [M12](design/2026-10-31-m12-review-and-generation.md) | [0047](adr/0047-plugin-libraries.md) |
| 第 1 阶段：修中断点写入的回滚错误，跑 5 道绘图题，把绘图经验写进工具用法。2026-11-10 用 Codex（`--agent codex`）跑 5 道绘图题：5/5 通过，参数重试 2、0、2、12、0（重画题的 8 次是把拐角、T 节点当器件放，已改成直接指向 `eplan_connection_create`）；另加“画图、审图、修正”题 `draw-review-fix` | 已完成（2026-11-10） | Unreleased | — | — |
| 第 2 阶段：IEC 60204-1、IEC 81346 规则插件，`eplan_page_review` 按规则审图；AI 画图、审图、修正的循环 | 已完成（2026-11-10）：两套内置插件由负责人签核到 1.0.0，见[签核记录](process/rule-pack-signoff-iec.md)；通过式烘芯炉按 IEC 审图 0 问题 | Unreleased | [M12](design/2026-10-31-m12-review-and-generation.md) | [0047](adr/0047-plugin-libraries.md) |
| 第 3 阶段：GB/T 5226.1、NFPA 79、NOM-001-SEDE 规则插件（放在私有插件仓库，不随公开仓库发布）；同一描述按不同标准出图，差异符合规则 | 已完成（2026-11-10）：三套均由负责人签核到 1.0.0 并签名；Codex 标准题 2/2；MX_NFPA 按 NFPA 79 + NOM 审图 0 问题 | — | 同上 | 同上 |
| 规则插件未经工程师签核时保持草稿，只用于 scratch；工具只检查收录的规则，不保证符合标准。签核人目前是负责人 | 已确定（2026-11-10，签核人为负责人） | — | [M12 第 6 节](design/2026-10-31-m12-review-and-generation.md) | — |

下面这些不再卡住当前主线，只在画图需要时做；普查暂停，空闲时再跑。

| 原主线（负责人 2026-11-04 决定） | 状态 | 发布 / 预计时间 | 设计依据 | ADR |
|---|---|---|---|---|
| 全部 action：每个名称都要落到三种结果之一：可直接调用（实测后进白名单）、由工具覆盖、明确不做并说明原因。EPLAN 2026.0.3 注册 2498 个名称，Electric P8 按名称能找到 2266 个；普查已跑 310 个，分批继续 | 进行中 | 随预览版持续交付；时间等普查分批结果后重估 | [M10](design/2026-10-30-m10-capability-expansion.md)、[API Actions](eplan/api-actions.md) | [0021](adr/0021-action-run-whitelist.md)、[0030](adr/0030-action-catalog-tiers.md) |
| 按任务做专用工具：普查结果按任务族汇总成工具，API 优先，没有 API 用 action，都不行就写明阻塞。6 批顺序：① 页面编辑、宏、连接 ② 端子排、电缆、PLC ③ 修订、编号、翻译、报表 ④ 部件库、项目管理 ⑤ 预规划、符号与图框表格编辑器 ⑥ 3D。功能区 339 个入口是实用清单（按用途判断，未逐个实测）：有工具 70、部分有 28、只影响显示 36、要做 55、第 5 批 16、3D 91、做不了或不自动做 8、标题或云入口 34、待判断 1；逐项明细只留本机（ADR-0030） | 进行中（第 1 批已盘点） | 分批进预览版 | 本页 | [0006](adr/0006-tool-surface-design.md)、[0030](adr/0030-action-catalog-tiers.md) |
| 属性数据关联：每批实测谁是源头、谁是计算值、谁单独存，结论写入[数据关系](eplan/data-relationships.md) | 进行中 | 随每批工具 | [数据关系](eplan/data-relationships.md) | — |
| 操作层 API：4014 组里可调用 2438 组（60.7%），明确不做 445 组，到不了 1131 组，实机验证 864 组（21.5%），2026-11-12 重算（ADR-0054 部件库主数据、返回类的子类可达）；全量 5983 个成员里能调用 2523 个（42.2%），实机验证 939 个（15.7%）。按需要补，不再要求先全部补齐 | 进行中 | 持续 | [实施计划](design/2026-10-31-api-completion.md)、[缺口 TSV](../catalog/operation-gaps.tsv) | [0032](adr/0032-generic-api-layer.md)、[0033](adr/0033-api-try-object-handles.md)、[0036–0045](adr/README.md) |
| M8 绘图与精确重绘：小项目 16 页完全一致（第 16 轮 65.77%），之后客户页精确重绘和客户彩排 | 暂停（action 主线优先） | **v0.6.0**；时间等 action 主线重估 | [M8](design/2026-10-27-m8-drawing.md)、[验收要求](design/2026-10-31-api-completion.md#45-客户页差异必须在重画前解决) | [0005](adr/0005-safety-model.md)、[0032](adr/0032-generic-api-layer.md) |
| 布局护栏 R1–R9、调用模板 `try_template`、页面审查 `eplan_page_review` 第 1 片 | 已实现，随 v0.5.0 预览版发布；未实机验证项见台账 | v0.5.0 预览版 | [绘图设计](design/2026-10-27-m8-drawing.md)、[审图设计](design/2026-10-31-m12-review-and-generation.md) | [0032](adr/0032-generic-api-layer.md) |

有已验证内容就发预览版（v0.5.x），每周五是最低频率。

| v0.6.0 前后（按推进顺序） | 状态 | 发布 | 设计依据 | ADR |
|---|---|---|---|---|
| M9 主数据与报表余项；EDZ 导入等待负责人样本；`eplan_part_build` 已于 2026-11-10 合入 main，同日在部件库副本上实机通过 | 等待负责人（EDZ）；其余计划中 | v0.6.0 之后，版本待定 | [API 主数据补齐](design/2026-10-31-api-completion.md)、[能力扩展](design/2026-10-30-m10-capability-expansion.md)（独立设计待写） | [0019](adr/0019-master-data-writes.md)、[0032](adr/0032-generic-api-layer.md) |
| M10 能力扩展：吸收 Canary 有益功能，action 部分已并入当前主线；其余 Canary 有益功能按 A/B/C 分级 | 计划中 | v0.6.0 之后，版本待定 | [M10](design/2026-10-30-m10-capability-expansion.md) | [0030](adr/0030-action-catalog-tiers.md)、[0031](adr/0031-private-extension-modules.md) |
| M12 国家规则包：IEC 60204-1 + IEC 81346 → GB/T 5226.1 → NFPA 79 已提到当前主线；这里只剩公司默认包和从 IO 清单生成 PLC 页。每包经工程师签核，不复制标准正文 | 计划中 | v0.6.0 之后，版本待定 | [M12](design/2026-10-31-m12-review-and-generation.md) | [0005](adr/0005-safety-model.md)、[0024](adr/0024-pack-trust-and-container.md)、[0029](adr/0029-phase3-engineering-records.md) |
| M11 PDF 图纸集转 EPLAN 项目：在 M9/M10 之后 | 计划中 | v0.6.0 之后，版本待定 | [M8 绘图基础](design/2026-10-27-m8-drawing.md)（独立设计待写） | [0005](adr/0005-safety-model.md)、[0032](adr/0032-generic-api-layer.md) |
| Workbench 改版：现在做 mock-ups，v0.6.0 后实施 | 进行中（mock-ups）；实施计划中 | v0.6.0 之后，版本待定 | [M5 界面设计](design/2026-10-20-m5-operability.md#45-界面技术fluent-2-的实现方式q1adr-0015)、[Workbench 实现记录](design/2026-10-20-m5-operability.md#workbench-implementation) | [0017](adr/0017-workbench-wpf-tia-theme.md)（2026-10-20 Accepted）；[0027](adr/0027-workbench-plan-review.md) 仍为 Proposed |

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
| M4b Pro Panel 3D 专用工具家族 | 计划中 | 待定，尚未排期 | [API 覆盖设计](design/2026-10-21-api-capability-coverage.md)、[3D 范围](eplan/api-coverage.md#3-pro-panel-3dm4b) | [0005](adr/0005-safety-model.md)、[0032](adr/0032-generic-api-layer.md) |

协作流程：负责人向 Codex 交办 worktree 任务，审阅后由 `eng/scripts/local-gate.ps1` 直接推送 main；不开 PR，CI 不阻塞。Large 测试在唯一的 EPLAN 机器上串行运行。

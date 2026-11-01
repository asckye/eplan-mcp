# 路线图

截至 2026-11-01，里程碑状态只在本页维护；设计与 ADR 保留方案和决策历史。状态：已完成（done）、进行中（in progress）、计划中（planned）、等待负责人（waiting on owner）。全路线图目标为 **2027 年 1 月底**；以下日期为估计。

v0.4.0 于 **2026-10-27** 发布（标签日期）。v0.5.0 推迟：先补齐操作层 API，再把客户页按原图精确重绘，完成客户彩排后发布。

| 当前主线与发布门槛 | 状态 | 发布 / 预计时间 | 设计依据 | ADR |
|---|---|---|---|---|
| 操作层 API 补齐：4014 个重载组，main 约 1010 组可试（约 25%），目标 85–90%；覆盖证据以[生成报告](eplan/coverage-report.md)为准 | 进行中 | v0.5.0；预计 11-08..10 | [实施计划](design/2026-10-31-api-completion.md) | [0032](adr/0032-generic-api-layer.md)、[0033](adr/0033-api-try-object-handles.md) |
| 布局护栏 R1–R9：并入操作层主线 | 进行中 | v0.5.0 | [绘图设计](design/2026-10-27-m8-drawing.md) | [0032](adr/0032-generic-api-layer.md) |
| 调用模板 `try_template`：并入操作层主线 | 进行中（模板已接入） | Unreleased，目标 v0.5.0 | [API 补齐设计](design/2026-10-31-api-completion.md)；[API](tools/eplan_api_get.md) / [action](tools/eplan_action_catalog_get.md) 规格 | [0032](adr/0032-generic-api-layer.md)、[0033](adr/0033-api-try-object-handles.md) |
| 页面审查 `eplan_page_review`：第 1 片测试中，第 2 片在操作层之后 | 进行中 | Unreleased | [审图设计](design/2026-10-31-m12-review-and-generation.md) | [0005](adr/0005-safety-model.md)、[0032](adr/0032-generic-api-layer.md) |
| M8 绘图与客户页精确重绘：必须与原图完全一致，再客户彩排 | 进行中 | v0.5.0；重绘预计 11 月中旬 | [M8](design/2026-10-27-m8-drawing.md)、[验收要求](design/2026-10-31-api-completion.md#45-客户页差异必须在重画前解决) | [0005](adr/0005-safety-model.md)、[0032](adr/0032-generic-api-layer.md) |

每周五预览版（v0.4.x），首个 v0.4.1：2026-11-06

| v0.5.0 之后（按推进顺序） | 状态 | 发布 | 设计依据 | ADR |
|---|---|---|---|---|
| M9 主数据与报表余项；EDZ 导入等待负责人样本；`eplan_part_build` 已实现，实机测试延后 | 等待负责人（EDZ）；其余计划中 | v0.5.0 之后，版本待定 | [API 主数据补齐](design/2026-10-31-api-completion.md)、[能力扩展](design/2026-10-30-m10-capability-expansion.md)（独立设计待写） | [0019](adr/0019-master-data-writes.md)、[0032](adr/0032-generic-api-layer.md) |
| M10 能力扩展：吸收 Canary 有益功能，约 1050 个 action 按 A/B/C 分级 | 计划中（部分目录与试执行已提前接入） | v0.5.0 之后，版本待定 | [M10](design/2026-10-30-m10-capability-expansion.md) | [0030](adr/0030-action-catalog-tiers.md)、[0031](adr/0031-private-extension-modules.md) |
| M12 国家规则包：IEC 60204-1 + IEC 81346 → GB/T 5226.1 → NFPA 79，另有公司默认包；每包经工程师签核，不复制标准正文 | 计划中 | v0.5.0 之后，版本待定 | [M12](design/2026-10-31-m12-review-and-generation.md) | [0005](adr/0005-safety-model.md)、[0024](adr/0024-pack-trust-and-container.md)、[0029](adr/0029-phase3-engineering-records.md) |
| M11 PDF 图纸集转 EPLAN 项目：在 M9/M10 之后 | 计划中 | v0.5.0 之后，版本待定 | [M8 绘图基础](design/2026-10-27-m8-drawing.md)（独立设计待写） | [0005](adr/0005-safety-model.md)、[0032](adr/0032-generic-api-layer.md) |
| Workbench 改版：现在做 mock-ups，v0.5.0 后实施 | 进行中（mock-ups）；实施计划中 | v0.5.0 之后，版本待定 | [M5 界面设计](design/2026-10-20-m5-operability.md#45-界面技术fluent-2-的实现方式q1adr-0015)、[界面规范](standards/workbench-ui.md) | [0017](adr/0017-workbench-wpf-tia-theme.md)（2026-10-20 Accepted）；[0027](adr/0027-workbench-plan-review.md) 仍为 Proposed |

| 基础里程碑与其余计划 | 状态 | 发布 | 设计依据 | ADR |
|---|---|---|---|---|
| M0 启动 | 已完成 | v0.1.0 | [架构](architecture/architecture.md) | [0001](adr/0001-record-architecture-decisions.md) |
| M1 技术验证与端到端骨架 | 已完成 | v0.1.0 | [M1](design/2026-10-13-m1-skeleton.md) | [0002](adr/0002-host-bridge-process-model.md)、[0003](adr/0003-ipc-named-pipe-json-rpc.md) |
| M2 只读核心 | 已完成 | v0.1.0 | [M2](design/2026-10-18-m2-read-core.md)、[Agent 评测](design/2026-10-19-agent-evals-v0.md) | [0006](adr/0006-tool-surface-design.md) |
| M3 安全写入（含 M3b） | 已完成 | v0.1.0 | [M3](design/2026-10-19-m3-safe-writes.md)、[M3b](design/2026-10-20-m3b-placement.md) | [0005](adr/0005-safety-model.md)、[0010](adr/0010-write-outcomes.md)、[0011](adr/0011-approval-out-of-band.md) |
| M4 工程工作流 | 已完成 | v0.1.0；action 白名单 v0.2.0 | [M4](design/2026-10-20-m4-workflows.md) | [0012](adr/0012-export-tier.md)、[0013](adr/0013-destructive-backup.md)、[0021](adr/0021-action-run-whitelist.md) |
| M4c 更多工程工具 | 已完成（PDF 工具 Large 待运行） | v0.1.0–v0.4.0；PDF 便笺 Unreleased | [M4c](design/2026-10-21-m4c-more-tools.md) | [0019](adr/0019-master-data-writes.md)、[0022](adr/0022-domain-toolsets.md) |
| M5 可运维：ZIP、Workbench、虚拟机连接已实现；干净机器和跨机器验收待完成 | 进行中 | v0.1.0–v0.2.0 | [M5](design/2026-10-20-m5-operability.md) | [0016](adr/0016-per-user-installer.md)、[0017](adr/0017-workbench-wpf-tia-theme.md)、[0018](adr/0018-vm-http-transport.md)、[0020](adr/0020-no-self-hosted-runner.md) |
| M6 工程包：第 1–3 阶段已实现，第 4–5 阶段计划中 | 进行中 | 第 1 阶段 v0.1.0，第 2 阶段 v0.3.0，第 3 阶段 Unreleased；其余待定 | [总设计](design/2026-10-21-m6-engineering-packs.md)、[第 2 阶段](design/2026-10-23-m6-phase2-plans.md)、[第 3 阶段](design/2026-10-26-m6-phase3-config-modules-rules.md) | [0023](adr/0023-plan-approval-and-backup.md)、[0024](adr/0024-pack-trust-and-container.md)、[0025](adr/0025-code-plugin-worker.md)、[0026](adr/0026-eplan-events-and-push.md)、[0028](adr/0028-plan-record-format.md)、[0029](adr/0029-phase3-engineering-records.md) |
| M7 多版本：验证 EPLAN 2024、2025、2027 | 计划中 | 待定，尚未排期 | [架构兼容性设计](architecture/architecture.md#9-质量场景可测试的验收标准)（无独立设计） | [0009](adr/0009-supported-eplan-versions.md) |
| M4b Pro Panel 3D 专用工具家族 | 计划中 | 待定，尚未排期 | [API 覆盖设计](design/2026-10-21-api-capability-coverage.md)、[3D 范围](eplan/api-coverage.md#3-pro-panel-3dm4b) | [0005](adr/0005-safety-model.md)、[0032](adr/0032-generic-api-layer.md) |

协作流程：负责人向 Codex 交办 worktree 任务，审阅后由 `eng/scripts/local-gate.ps1` 直接推送 main；不开 PR，CI 不阻塞。Large 测试在唯一的 EPLAN 机器上串行运行。

# 设计文档索引

下表记录文档自身的设计状态，标“（历史）”的是已完成里程碑的设计，只作背景，不再更新；里程碑进度、发布范围与排期统一见[路线图](../roadmap.md)。M6 总设计顶部提供分阶段设计、ADR 与当前规格入口；API 事实按主题查阅 [api-notes 索引](../eplan/api-notes.md)。

| 文件 | 标题 | 状态 |
|---|---|---|
| [2026-10-13-m1-skeleton.md](2026-10-13-m1-skeleton.md) | M1 端到端骨架（Host ↔ 管道 ↔ Bridge ↔ EPLAN） | Implemented（历史） |
| [2026-10-18-m2-read-core.md](2026-10-18-m2-read-core.md) | M2 只读核心 | Approved（历史） |
| [2026-10-19-agent-evals-v0.md](2026-10-19-agent-evals-v0.md) | Agent 评测 v0 | Implemented（历史） |
| [2026-10-19-m3-safe-writes.md](2026-10-19-m3-safe-writes.md) | M3 安全写入 | Approved（历史） |
| [2026-10-19-text-language-fallback.md](2026-10-19-text-language-fallback.md) | 多语言文本的语言回退 | Implemented（历史） |
| [2026-10-20-m3b-placement.md](2026-10-20-m3b-placement.md) | M3b 插入宏、生成连接 | Approved（历史） |
| [2026-10-20-m4-workflows.md](2026-10-20-m4-workflows.md) | M4 工程工作流 | Approved（历史） |
| [2026-10-20-m5-operability.md](2026-10-20-m5-operability.md) | M5 可运维 | Approved |
| [2026-10-21-api-capability-coverage.md](2026-10-21-api-capability-coverage.md) | EPLAN 官方 API 能力覆盖（第一期：能力目录、口径与阶段计划） | Approved |
| [2026-10-21-m4c-more-tools.md](2026-10-21-m4c-more-tools.md) | M4c 更多工程工具 | Approved（历史） |
| [2026-10-21-m6-bridge-snapshot.md](2026-10-21-m6-bridge-snapshot.md) | M6 第 1 阶段的 Bridge 操作（快照、定位符、新建与还原项目） | Implemented（历史） |
| [2026-10-21-m6-engineering-packs.md](2026-10-21-m6-engineering-packs.md) | M6 工程包（engineering packs） | Approved |
| [2026-10-23-m6-phase2-plans.md](2026-10-23-m6-phase2-plans.md) | M6 第 2 阶段的计划与一个完整任务 | Implemented（历史） |
| [2026-10-25-m6-large-project-plans.md](2026-10-25-m6-large-project-plans.md) | M6 大项目计划的名称冲突域与局部证据 | Implemented（历史） |
| [2026-10-25-property-tools.md](2026-10-25-property-tools.md) | 通用 EPLAN 属性读写 | Implemented |
| [2026-10-26-m6-phase3-config-modules-rules.md](2026-10-26-m6-phase3-config-modules-rules.md) | M6 第 3 阶段的配置、模块与规则 | Implemented（历史） |
| [2026-10-27-m6-module-inspect.md](2026-10-27-m6-module-inspect.md) | M6 模块只读检查服务 | Implemented（历史） |
| [2026-10-27-m8-drawing.md](2026-10-27-m8-drawing.md) | M8 绘图 | Accepted；2026-11-14 收尾，不做精确重绘 |
| [2026-10-30-m10-capability-expansion.md](2026-10-30-m10-capability-expansion.md) | M10 能力扩展 | Accepted |
| [2026-10-31-api-completion.md](2026-10-31-api-completion.md) | EPLAN API 补齐（ADR-0032 实施计划） | Approved |
| [2026-10-31-m12-review-and-generation.md](2026-10-31-m12-review-and-generation.md) | M12 审图规则与按清单生成图纸 | Approved |
| [2026-11-01-api-handle-chains.md](2026-11-01-api-handle-chains.md) | 链式句柄闭包结果（ADR-0033 实施） | Implemented |
| [2026-11-01-layout-guard.md](2026-11-01-layout-guard.md) | 共享布局守卫 | Implemented |
| [2026-11-02-plugin-libraries.md](2026-11-02-plugin-libraries.md) | 插件库（标准、部件、符号与宏、模板、公司插件） | Accepted |
| [2026-11-03-small-project-redraw.md](2026-11-03-small-project-redraw.md) | 完整重画一个小项目 | 历史；2026-11-14 不再作为验收 |
| [2026-11-12-m4b-layout3d.md](2026-11-12-m4b-layout3d.md) | M4b 第一批：柜体 3D 布置工具 | Implemented |
| [2026-11-14-api-closeout-list.md](2026-11-14-api-closeout-list.md) | 操作层“到不了”的结案清单 | Accepted |
| [2026-11-15-operation-layer-full-review.md](2026-11-15-operation-layer-full-review.md) | 操作层 4014 组全面复查与全量实机验证 | 进行中 |

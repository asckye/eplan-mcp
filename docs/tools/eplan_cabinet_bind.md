# 工具规格：`eplan_cabinet_bind`

| 项 | 内容 |
|---|---|
| 状态 | Implemented (Unreleased) |
| 分组 / Profile / 注解 | pack / full / 本地数据；四注解 false/false/true/false |
## 1. 任务
整份替换柜、项目值和包根，保持每项目同包单版本；不修改 EPLAN 工程。
## 2. 输入
必填 `project:string`、`expected_revision:integer>=0`、`cabinets:array`、`project_values:object`、`pack_roots:array`；`dry_run:boolean=true`，省略或 true 只预览，显式 false 才执行。柜含 id、可选 title、layers、values、structure（all 或 prefixes）；设置含 default/constraint；包根含 id/version（精确版本）。英文示例见用法文件。
## 3. 输出
对象包含 binding、changes（before/after 绑定）、dryRun、fingerprint（完整目标绑定的 SHA-256）、execution 和 hint。预览或内容未变化时 execution 为 not_started，实际发布为 committed；预览 hint 指明以相同参数并传 `dry_run: false` 再次调用。JSON 文本与 structuredContent 相同；双份预算 20k，证据超限拒绝，不截断实际校验。
## 4. 错误
INVALID_ARGUMENT（pointer/violations：严格成员/类型）；CONFIG_CONFLICT（pointer/trace：配置/版本冲突）；PLAN_STALE（revision CAS）；RESOURCE_NOT_FOUND/PACK_INVALID（资源）；APPROVAL_DENIED/APPROVAL_EXPIRED、AUDIT_UNAVAILABLE 沿现有码。
## 5. 实现
复用 BindingService；先核验全部柜的配置，再确认、项目锁→绑定锁、CAS、审计、原子替换；支持取消与进度，无 EPLAN 工程写入或 Bridge 新操作。
## 6. 写操作
省略 dry_run 或 true 仅返回精确新绑定及差异，不确认、不落盘；显式 false 沿用执行路径。仅 locks 首次建立/修改/删除需工程师确认，文案列出前后版本/摘要并说明本地绑定。锁不变仅审计；相同内容不增 revision。确认拒绝、审计前失败、CAS 冲突均不替换；发布后审计失败明确报告已修改。
## 7. 测试
Small 注解及默认值/输出契约；Medium MCP 省略/true/false 三路径、预览零写入、换锁确认/拒绝/过期、值更新不确认、CAS、配置失败零写入；Large 仅覆盖读取，不运行写操作。Agent 场景：先预览多柜映射，再按实际 revision 显式传 false 更新。

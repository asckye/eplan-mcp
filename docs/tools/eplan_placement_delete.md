# 工具规格：`eplan_placement_delete`

| 项目字段 | 记录 |
|---|---|
| 工具名 / 注册入口 / 契约快照 | `eplan_placement_delete` / `PlacementDeleteTools` / `server-contract.verified.json` |
| 分组 / Profile | placement / full |
| 状态 | Implemented (Unreleased) |
| 安全分类与 MCP 注解 | Destructive；readOnlyHint=false、destructiveHint=true、idempotentHint=false、openWorldHint=false |
| 模型说明与示例来源 | `PlacementDeleteTools.cs`、`Usage/eplan_placement_delete.md` |
| EPLAN 调用路线 | `placement.delete.plan/execute` → `EplanPlacementRemover` → `Placement.Remove()` |
| API 证据 | A54/A55/A87：图形事务、对象生命周期、宏框移除；完整新工具待实测 |
| 原生作用域与生命周期 | 显式项目、页和 drawing locator；提交后按新枚举读回，不持有原生对象跨事务 |
| 预览、审批与审计 | 默认预览，返回删除清单和指纹；显式 false 经 Destructive 审批、备份和审计 |
| 事务、回滚与结果恢复 | 单个 TransactionRunner 事务，一次撤销；确认提交后读回，失败补偿；未知结果不可重放 |
| 已有测量 | 1–100 对象；主线程和端到端耗时待实测 |
| 项目测试与夹具 | Small 参数、类型、成员、过期、回滚；Medium 默认预览/执行；LargeWrite 新页删除线和文字及一次撤销，拒绝器件 |

输入为 `{project,page,items:[locator...],dry_run}`。`items` 为 1–100 个由 `eplan_page_get include_drawings` 返回的同页定位器；默认 `dry_run:true`。允许 `Line`、`Arc`、`PolyLine`、`Rectangle`、`Text`、`PathText`、`Image`、`MacroBox`，包括 `grouped:true` 的组内对象。器件、连接、属性文字、块及 `parent` 非空的块成员拒绝，`INVALID_ARGUMENT` 说明原因。跨项目/页或摘要已变化返回 `PLAN_STALE`，要求重新读取定位器。缺少外框不妨碍删除。
输出含 `project`、`page`、`items`（批准对象的 type/oid）、`fingerprint`、`execution`、`dryRun`、`recovered`、`backup`、`warning` 和 `hint`。提交后核对目标消失且其它已发现对象摘要不变；预览不审批、不备份、不删除。
删除不能由新增可选参数替代，独立破坏性工具让审批清楚显示操作目的。宏框删除只移除框，引用对象保留（A87）；其它组合对象不展开。

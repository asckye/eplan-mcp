# 工具规格：`eplan_action_catalog_get`

| 项 | 内容 |
|---|---|
| 分组 / Profile | action_catalog / full |
| 级别 | Read |
| 状态 | Implemented (Unreleased) |
| MCP 注解 | readOnly=true、destructive=false、idempotent=true、openWorld=false |

按精确且区分大小写的 `name`（1–500 字符）读取同名 action 的全部官方 TYPE 行、本机 UI 定义或注册表条目；`limit` 默认 20、范围 1–100，`cursor` 分页。同名不等于所有 TYPE 都已验证。

输出、数据来源、版本缺口与安全边界沿用 [search 规格](eplan_action_catalog_search.md)；`items` 分页，避免同名定义超出结果预算。白名单 A 表示随产品发布的验证记录，不表示本机版本已实测，也不改变 actionRun 开关。

`registryKind` 为 action、interaction 或 asm_action，`module` 为注册表记录的所属模块，缺失时为空。同名已有 official 或 ui 行时只补这两个字段；Interactions 的级别固定为 excluded，原因为 `interaction: not callable by name; use a drawing tool`。本机注册表决定目录总数；按 [ADR-0030](../adr/0030-action-catalog-tiers.md)，注册表文件及完整名单不进仓库。

每行的 `try_template` 给出 `eplan_action_try` 参数对象、已知参数及目录允许值、禁用状态、选择要求及设置方法。来源没有记录的必填性与值域明确为未知。模板例子保持 `dry_run:true`，其缺省值也是 true；实际执行须以相同参数显式传 `dry_run:false`。模板不改变试执行策略。

官方目录由离线 `import-actions` 补充参数类型、字面值、默认值和按 TYPE 匹配的调用示例。模板优先采用第一条示例；路径与文件值仅显示 scratch 的 imports/exports 占位符，仍受原禁止表约束。需要文件、页或选择上下文的批量项记为 `failed:needs_context`。

`INVALID_ARGUMENT`：修正所有参数或移除游标；`RESOURCE_NOT_FOUND`：用 search 找到精确名称，错误 metadata 保留安装版本及缺口；安装或注册表文件缺失、不可读时仍返回可用条目及缺口。

测试：Small 覆盖同名 TYPE 分级、未知名称、大小写和分页；Host 验证无需 Bridge、未知参数拒绝和 schema；Large 与 search 共用只读安装测试，不启动 EPLAN。评测场景：精确读取 backup，区分 MASTERDATA 白名单与 PROJECT 候选。

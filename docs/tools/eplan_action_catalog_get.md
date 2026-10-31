# 工具规格：`eplan_action_catalog_get`

| 项 | 内容 |
|---|---|
| 分组 / Profile | action_catalog / full |
| 级别 / 状态 | Read / Implemented |
| MCP 注解 | readOnly=true、destructive=false、idempotent=true、openWorld=false |

按精确且区分大小写的 `name`（1–500 字符）读取同名 action 的全部官方 TYPE 行与本机 UI 定义；`limit` 默认 20、范围 1–100，`cursor` 分页。同名不等于所有 TYPE 都已验证。

输出、数据来源、版本缺口与安全边界沿用 [search 规格](eplan_action_catalog_search.md)；`items` 分页，避免同名定义超出结果预算。白名单 A 表示随产品发布的验证记录，不表示本机版本已实测，也不改变 actionRun 开关。

`INVALID_ARGUMENT`：修正所有参数或移除游标；`RESOURCE_NOT_FOUND`：用 search 找到精确名称，错误 metadata 保留安装版本及缺口；安装缺失仍可返回官方项及缺口。

测试：Small 覆盖同名 TYPE 分级、未知名称、大小写和分页；Host 验证无需 Bridge、未知参数拒绝和 schema；Large 与 search 共用只读安装测试，不启动 EPLAN。评测场景：精确读取 backup，区分 MASTERDATA 白名单与 PROJECT 候选。

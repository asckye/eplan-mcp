# 工具规格：`eplan_api_search`

| 项 | 内容 |
|---|---|
| 所属分组 / Profile | api / full |
| 安全级别 | Read（ADR-0032 B 级） |
| MCP 注解 | readOnlyHint=true，destructiveHint=false，idempotentHint=true，openWorldHint=false |
| 状态 | Implemented |

代理查找已登记能力及到达它的工具，无需连接 EPLAN。全部操作层重载组（含排除项）和数据字典对象均可发现；目录不授予执行权限。

输入均可选：`namespace` 精确匹配命名空间及子空间；`type` 精确匹配声明类型（可带 `T:`）；`member_name`、`summary_text` 忽略大小写包含匹配；`kind` 为 method/property/operator/delegate_method/data_dictionary；`status` 为 implemented/verified/not_implemented/excluded。条件取交集。`limit` 默认 20、范围 1–100；`cursor` 不透明。

输出 `catalogVersion` 回显目录对应的 EPLAN API 版本；`items` 含 XML ID、group、kind、简短签名、原始递进状态、排除原因与工具；`total`、`nextCursor`、`truncated`、`hint` 描述分页。implemented 包含 implemented/automated_test/live_verified；verified 仅 live_verified；not_implemented 包含 registered/env_blocked。空结果成功。摘要当前源目录未保存，返回 null；摘要过滤因此可能无结果。

游标绑定索引内容、工具、全部过滤条件和 limit；无效、越界或条件改变返回 `INVALID_ARGUMENT`，提示去掉游标重查。全程内存只读，双份 JSON 计入响应预算。Small 覆盖过滤、状态和游标；Medium 核对全部 4,649 组及 Host 契约。不需要 Large。

Agent 评测场景：查找 Translate 的方法，续页后用 group 查询所有重载；解释 excluded 原因并选择已登记工具，不尝试执行目录条目。

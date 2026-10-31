# 工具规格：`eplan_api_get`

| 项 | 内容 |
|---|---|
| 所属分组 / Profile | api / full |
| 安全级别 | Read（ADR-0032 B 级） |
| MCP 注解 | readOnlyHint=true，destructiveHint=false，idempotentHint=true，openWorldHint=false |
| 状态 | Implemented |

按必填 `id` 查询 XML 成员 ID、重载 group 或 `D:Function` 一类对象键。成员 ID 返回整个组；数据字典也接受对应 PropertyList、属性枚举或对象的类型 ID。`limit` 默认 20、范围 1–100；`cursor` 继续取详情。

输出 `catalogVersion` 回显目录对应的 EPLAN API 版本；`entry` 为组说明（含 partial）；`items` 为有序详情流：先完整重载（签名、参数、返回类型、访问器、静态标记、摘要和单重载排除原因），再去重证据（工具规格、测试类/文件、api-notes 锚点、实机来源、排除规则来源）。对象条目给出属性 ID 总数和读取指引；只指向已经存在的读取工具，不推断未实现对象可读。`total`、`nextCursor`、`truncated`、`hint` 保证所有详情可续取。

未知 ID 返回 `RESOURCE_NOT_FOUND`，提示用搜索工具；空白 ID、非法 limit 和游标返回 `INVALID_ARGUMENT`。分组状态沿用覆盖报告；单个重载被排除不等于整组排除；工具级证据是覆盖报告的上界，不代表每个重载独立验证。摘要缺失返回 null。

由提交目录与覆盖数据确定性生成压缩索引，嵌入 Host，发布 ZIP 无需源仓库或 EPLAN 文件；`eng/coverage generate/check` 更新并检查。Small 验证构建、状态优先级与分页；Medium 验证组全集、类型别名、工具注册和契约。无需 EPLAN 或审批。

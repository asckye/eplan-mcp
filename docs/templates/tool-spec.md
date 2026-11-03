# 工具设计：`<tool_name>`

设计说明采用[手册附录 T.3](../standards/engineering-handbook.html#tpl-rfc)；本页是 `docs/tools/<tool_name>.md` 使用的项目字段补充。问题、选项、决定、影响、测试计划和待决定事项使用手册模板。

| 项目字段 | 记录 |
|---|---|
| 工具名 / 注册入口 / 契约快照 | `<现有 eplan_… 名称、源码与快照链接>` |
| 分组 / Profile / 状态 | `<分组、core/full、发布或实现状态>` |
| 当前安全分类与 MCP 注解 | `<实际分类、readOnlyHint / destructiveHint / idempotentHint / openWorldHint>` |
| 模型说明与参数示例来源 | `<Contracts、描述、Usage 嵌入资源的源码链接>` |
| EPLAN 调用路线 | `<Bridge 操作、适配器、EPLAN 类型全名>` |
| API 证据 | `<api-notes 条目：版本、条件、输入、结论与证据；未验项链接>` |
| 原生作用域与生命周期 | `<项目/页/对象标识、句柄期限、LockingStep / SafetyPoint / QuietModeStep 的实际用法>` |
| 预览、审批与审计 | `<本工具实际变化清单、审批摘要和审计字段>` |
| 事务、回滚与结果恢复 | `<实际边界、execution 各阶段及取回入口；参照 ADR-0010>` |
| 已有测量 | `<输入规模、主线程/端到端耗时、取消语义与证据；未测则记录缺口>` |
| 项目测试与夹具 | `<Small/Medium/Large 用例、Fake Bridge、scratch 夹具、运行命令与实机状态链接>` |

输入/输出字段、默认值、错误 reason 和 `response_format` 的当前定义可链接到注册/schema、契约快照及对应工具参考；写结果模型见 [ADR-0010](../adr/0010-write-outcomes.md)，测试入口见 [Host 测试说明](../../tests/EplanMcp.Host.Tests/README.md)。

# 工具设计 `eplan_macro_store`

将页面对象保存为窗口宏，或将精确页名集合保存为页宏，补齐插入菜单对应的存宏入口。方案沿用导出 Read 分类，预览默认值采用 ADR-0049。

| 项目字段 | 记录 |
|---|---|
| 工具名 / 注册入口 / 契约快照 | `eplan_macro_store`；[MacroStoreTools.cs](../../src/EplanMcp.Host/Tools/MacroStoreTools.cs)；[契约快照](../../tests/Contracts.Snapshots/server-contract.verified.json) |
| 分组 / Profile / 状态 | macro / full / Implemented（Unreleased），实机待验 |
| 状态 | Implemented (Unreleased) |
| 当前安全分类与 MCP 注解 | Read 导出；readOnly、destructive、idempotent、openWorld 均 false |
| 模型说明与参数示例来源 | 注册 schema 与 [usage](../../src/EplanMcp.Host/Tools/Usage/eplan_macro_store.md) |
| EPLAN 调用路线 | `macro.store`；`WindowMacro.Create/Open/GetVariants`、`PageMacro.Create` |
| API 证据 | 负责人 2026-11-05，EPLAN 2026.0.3 Pro Panel LargeWrite 新页验证两个图形窗口宏 22348 字节、页宏 25881 字节，MultiLine 变量 0 可读；[待验登记](../process/large-pending.md) |
| 原生作用域与生命周期 | locators 原样取自 object_find，同项目同页，拒绝属性文字和块成员；原生对象仅本次主线程调用内持有，沿用 dispatcher LockingStep |
| 预览、审批与审计 | 默认预览输出变更清单、指纹；预览不创建文件或目录；Read 导出免审批，沿用导出审计边界 |
| 事务、回滚与结果恢复 | 不建项目事务和撤销步；保存前后业务快照一致；临时新文件完成验证后以不覆盖方式发布到目标名，失败删除本次临时文件 |
| 已有测量 | 上述字节数为负责人原生调用证据；工具端到端耗时、取消和实机闭环待验 |
| 项目测试与夹具 | Host 文件验证与并发拒绝；Core 参数与快照故障；Large `MacroStoreTests` 在 LargeWrite 新页存宏、插入、撤销与页宏文件校验，仅编写未运行 |

窗口宏必填 project、page、locators（1–500）、name。representation 默认 multi_line，支持 MultiLine、Overview；variant 为 0–15，默认 0；reference_mm 默认对象外框左下角。页宏必填 project、pages（1–50 个精确页名）、name。description 接受字符串或语言文本字典，与 page_create 一致。

name 仅接受 ASCII 字母、数字、`_`、`-`、`.` 组成的有效 Windows 文件名，自动补对应扩展名。文件限定在 Host 的 `exports/macros/` 下，已存在拒绝。执行返回 path、size、sha256，窗口宏另返回 variants；空文件、无法打开、缺请求变量或业务快照变化均报 EPLAN_OPERATION_FAILED。导出文件交由现有宏目录管理流程后，可供 macro_insert 使用。

影响限于新增工具与导出文件，不引入依赖。待决定事项为空，实机验收由负责人执行。

# 工具规格：`eplan_cabinet_list`

| 项 | 内容 |
|---|---|
| 状态 | 已实现；pack / full / Read；四注解 true/false/true/false |
## 1. 任务
分页检查柜映射、配置链、生效值和来源；未绑定时返回虚拟 default，不创建绑定。
## 2. 输入
`project:string` 必填；`limit:integer=50`（1–200）、`cursor:string?`、`max_tokens:integer=5000`（1–20000）。英文示例：`{"project":"Line1"}`。
## 3. 输出
对象包含 project、revision、locks、cabinets、total、nextCursor、truncated、hint、budget；每柜含 id/title/structure/layers/config，config 含 key/value/unit/constraints/trace。JSON 文本与 structuredContent 相同；摘要说明返回数量。游标绑定完整绑定摘要和已核验资源摘要；单柜超预算时拒绝并提示增大预算。
## 4. 错误
INVALID_ARGUMENT（pointer/violations：参数、游标或预算）；RESOURCE_NOT_FOUND（缺包）；PACK_INVALID（内容篡改）；CONFIG_CONFLICT（pointer/trace：配置冲突）。查询失败不带 execution。
## 5. 实现
Host 本地读取；ConfigMerger 合并，CabinetScopeResolver 定义结构归属；精确锁闭包和文件哈希复核。无 EPLAN API、事务或写入；支持取消与进度。
## 6. 写操作（不适用）
## 7. 测试
Small 注解/预算；Medium MCP 契约、分页、两柜隔离、篡改；Large 只读示例工程并比较前后项目文件，未实跑。Agent 场景：检查两柜不同配置并解释 trace。

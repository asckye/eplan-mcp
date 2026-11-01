# 工具规格：`eplan_part_build`

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_part_build` |
| 所属分组 / Profile | `part` / `full` |
| 安全级别 | 系统主数据（[ADR-0019](../adr/0019-master-data-writes.md)）：每次审批，执行前备份主数据 |
| MCP 注解 | `readOnlyHint` = false　`destructiveHint` = true　`idempotentHint` = false　`openWorldHint` = false |
| 状态 | Implemented (Unreleased) |

负责人 2026-10-31 提前实施 M9 首批，2026-11-10 回顾后合入 main；Large 仅编写未运行。从供应商表格每行生成一个部件，免去手写 XML；无 project，目标为所有项目共享的当前配置部件库。省略 `dry_run` 或传 `true` 只预览，传 `false` 才执行（ADR-0049）。

## 1. 输入
`file` 必填：imports 相对路径 .xlsx 或 UTF-8 .csv，最多 5 MB，禁止绝对路径、穿越、链接和重解析点。`mapping` 可选对象，标准字段→源表头，未指定时使用同名表头；表头须唯一且非空，未知映射键/缺失映射列拒绝，未知列逐列报告。`mode` 默认 append_new，可选 update_existing / update_and_append；不整条替换。`match_by` 本批仅 part_number（默认），拒绝其他方式，避免没有数据库唯一标识的表格出现预览/执行偏差。`dry_run` 默认 false，true 时不生成 XML、不审批、不备份、不写库。
标准字段：必需 `part_number`；可选 `manufacturer`、`type_number`、`description.en_US`（其他语言 description.ll_CC）、`product_group`、`product_subgroup`、`order_number`、`supplier`、`width`、`height`、`depth`、`technical_data`、`function_category`、`connection_points`、`macro_file`、`picture_file`。分类为非负整数代码，尺寸为非负有限十进制毫米。单个功能模板：正整数类别和 `|` 分隔的非空连接点必须一起给；已有多个变体或多个模板拒绝更新。空值/缺列不清空既有字段；描述逐语言比较并保留其他语言，新旧描述都拒绝多语言分隔符 @、;。宏/图片须为 imports 内真实文件，扩展名分别 .ema/.ems、.png/.jpg/.jpeg/.bmp，按输入同样规则解析，绝不展开变量或读取外部引用。
首个非空行为表头，最多 50 数据行、64 列、每值 500 字符；重复部件号的所有行报 error；映射须一一对应。CSV 支持逗号/分号、双引号和引号内换行，空行忽略，保留起始物理行号。XLSX 按工作簿关系读取第一工作表，支持共享/内联字符串、原始数字与稀疏单元格，拒绝公式、显式日期/错误/布尔单元格、外部关系、DTD、重复条目和压缩展开超限；不启动 Excel，不应用单元格显示格式，日期与前导零须存成文本。
## 2. 输出与错误
结果含 file、sha256、database、execution、dryRun、unknownColumns、rows、import、readBack、hint；每行含 rowNumber、partNumber、action=create/update/skip/error、diff（字段 before/after）和 errors。用既有 part.search 只读路径比较；有任意 error 时整批不执行，没有变化也不审批。结果经过 ResultShaper；预览过大要求拆表，不丢弃行；提交后的额外读回列表可缩短并明确提示。摘要只给状态。
文件/映射/格式错误：INVALID_ARGUMENT 或 FILE_NOT_FOUND，execution=not_started；策略、审批、备份、PLAN_STALE、OUTCOME_UNKNOWN 同 eplan_part_import。import 包含新增/更新部件号、missing、backup、recovered 等；readBack 给字段实值，读回失败/差异用 hint 报告。开始导入后的失败不能回滚，unknown 时暂停数据库写入，不自动重试，由工程师按 backup 恢复。
## 3. 流程、安全与审计
执行在 Host 数据目录 imports 的新临时子目录生成 XML，复用 part.import.plan/execute、WritePipeline 和 ADR-0019：逐次独立审批、拒绝 SQL Server/deny、先备份主数据，工具名为 eplan_part_build。审批显示库、源表大小/哈希与字段变化，审计记录源表摘要、映射、mode、match_by、生成 XML 哈希；审批前和执行前核对被比较字段快照。源表/引用在调用期间保持只读锁；各终态清理临时文件；EPLAN 仍占用 XML 时保留文件，避免掩盖实际导入结果。XML 沿用 S24 导出与导入夹具的 partsmanagement/part/variant/functiontemplate：默认变体 1，描述 P_ARTICLE_DESCR1，技术参数在 variant，模板 functiondefcategory/connectionDesignation 保留连接点换行。新增字段的真实导入行为仍待副本 Large 验证。调用沿 LockingStep/QuietModeStep，无 SafetyPoint（A30）；300 秒，复用流水线进度、取消、备份与恢复提示。

## 4. 测试与评测
Small：内存 CSV/XLSX、映射、全行格式/重复校验、diff、语言、导出结构 XML。Medium：假 Bridge 的 dry-run 零写入、审批/备份、陈旧字段、失败清理与读回。Large：保留 eplan_part_import 的主数据 opt-in，再核对显式原库/副本路径、当前配置确为 scratch 副本、数据哈希相同；从虚构 CSV 建 3 个部件，导出逐字段比较，核对原库未变，不自动选择数据库。本次不运行。新增 attended 评测，工程师先配置隔离副本和放入虚构 CSV；常规开发集≤30、保留集≤10。

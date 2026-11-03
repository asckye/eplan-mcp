# 工具规格：`eplan_report_create`

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_report_create` |
| 分组 / Profile | `report` / `core` |
| 安全 / 注解 | Write；readOnly=false、destructive=false、idempotent=false、openWorld=false |
| 状态 | Implemented (Unreleased) |

## 1. 任务

按报表块生成项目报表页；已有报表更新用 `eplan_report_update`。不提供器件限定（A84）。

## 2. 输入

| 参数 | 类型 | 必填 | 默认值 | 说明 |
|---|---|---|---|---|
| `project` | string | 是 | — | 项目名或路径，必须显式指定 |
| `type` | enum | 是 | — | 下面列出的 DocumentType 名称 |
| `form` | string | 是 | — | 表格名，不含路径或扩展名，非空且最多 500 字符 |
| `first_page` | string | 是 | — | 完整页名，必须不存在或是报表页；格式同 eplan_page_create |
| `filter_scheme`、`sort_scheme` | string | 否 | null | 非空且最多 500 字符，仅名称，不传方案数据 |
| `page_description` | string | 否 | null | 最多 500 字符；给出为手动描述，省略为自动 |
| `dry_run` | boolean | 否 | true | 仅规划；实际执行显式传 false |

`project`、`type`、`form`、`first_page` 必填；表格名不含扩展名，首页必须不存在或是报表页。
`type`：TitlePage、TableOfContents、PLCDiagram、TerminalDiagram、PartsSumList、PartsList、CableOverview、CableConnectionDiagram、TerminalStripOverview、DeviceList、ConnectionList、StructIdentifierOverview（本机 DocumentType 枚举）。
方案有效性由 EPLAN 判断，不预先枚举或加载方案数据。

## 3. 输出

`project`、`execution`、`dryRun`、`changes`（审批变更清单）、`fingerprint`（计划指纹）、`pages`（执行后同 type/form 的全部报表页名）、`new_pages`（生成前不存在的页名）、`type`、`form`、`count`（pages 数量）、`undo`、`recovered`、`hint`；页名与表格名是项目数据。dry-run 和恢复提交缺失读回时 `pages`、`new_pages`、`count` 为 null。文本摘要给出匹配页数；无分页。new_pages 为空而 pages 非空视为覆盖，hint 说明；两者都为空则回滚。

## 4. 错误

`INVALID_ARGUMENT`：参数/首页类型无效；无效方案附 `metadata.eplanMessage` 原文。`PLAN_STALE`：页清单变更，重新规划。`EPLAN_OPERATION_FAILED`：pages 与 new_pages 都为空并回滚。其余策略、审批、审计、锁定和结果未知沿用 `eplan_page_create`；未知结果不重放。

## 5. 实现

依据 [A84](../eplan/api-notes/drawing-and-layout.md#a84-报表块读取与按报表块生成报表页2026-11-03eplan-20260325421electric-p8示例项目夹具只读生成只在-scratch-副本-largewrite)：在一个事务中 `ReportBlock.Create`、设置字段、`Reports.CreateReport`；事务外使用新包装对象读回类型、表格与前后页清单；无页时回滚。经主线程 LockingStep / SafetyPoint / UndoStep / QuietModeStep；执行预算 300 秒、支持进度及取消。

## 6. 写入

dry-run 只规划，不审批、不写入；审批为 `create <type> report (<form>) from <first_page>`，列出筛选/排序方案。审计绑定参数、计划、指纹与 execution；成功为一个 `AI: eplan_report_create` 撤销步骤。失败沿用流水线阶段状态。
规划、策略或审批失败为 `not_started`，事务失败为 `rolled_back`；已提交为 `committed`，结果无法恢复为 `unknown` 并暂停写入。丢失响应先查询结果，不重放写请求。

## 7. 测试

Small：枚举、页名、表格/方案校验、审批、指纹；Medium：假 Bridge 规划/执行/dry-run。Large `ReportCreateTests`：先只读夹具 `&AAB1/1`，仅在 LargeWrite 用读到的 type/form 生成目录，再生成 TerminalDiagram/F13_008/Location designation_A3 并验证撤销恢复页清单；本次不运行（native execution: NOT RUN）。Agent 场景：读取目录配置，在 scratch 重建目录并核对生成页。

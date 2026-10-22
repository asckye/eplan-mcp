# 工具规格：`eplan_revision_list`

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_revision_list` |
| 所属分组 / Profile | `project` / `full`（M4c 的新工具默认只在 `full` 中） |
| 安全级别 | Read |
| MCP 注解 | `readOnlyHint` = true　`destructiveHint` = false　`idempotentHint` = true　`openWorldHint` = false |
| 状态 | Proposed（M4c 第一批；已实现，待实机验证） |

## 1. 这个工具解决什么任务
"这个项目现在是哪个修订？""修订记录里有哪些？""这是已经完成的修订吗，还能改吗？"代理读出项目的修订状态和修订记录（EPLAN 的变更跟踪），在写入之前判断项目能不能改，或者把修订历史报告给工程师。

修订的写操作（新建修订、完成项目、比较项目）另行设计（M4c 设计 Q9）：新建和完成都会改项目链接文件的扩展名，Ctrl+Z 撤不回（S23）。

## 2. 输入参数
| 参数 | 类型 | 必填 | 默认值 | 说明（写给模型看的） |
|---|---|---|---|---|
| `project` | string | 否 | EPLAN 中选中的项目 | 项目名，或 `eplan_project_list` 返回的 `path` |
| `limit` | integer，1–200 | 否 | 50 | 本页最多返回几条修订记录 |
| `cursor` | string | 否 | — | 上一页返回的 `nextCursor` |
| `language` | string | 否 | 同其他读工具的语言回退 | 修订说明是多语言文本时取哪种语言 |

## 3. 输出
`structuredContent`：
- `project`、`path`（含扩展名的链接文件）；
- `state`：`none`（`.elk`，没有修订）、`tracking`（`.ell`，修订进行中，变更跟踪已开启）、`completed`（`.elr`，完成的修订，写保护）；
- `trackingStarted`：`PROJ_REVISION_LOG_START`；
- `parent`：完成的修订项目所来自的项目路径（`PROJ_REVISIONSPARENT`），其他状态为 null；
- `revisions`：修订记录，按序号升序，每条 `index`、`name`、`description`（取的不是所请求的语言时另有 `descriptionLanguage`）、`user`、`date`（UTC，ISO 8601；读不出时为 null，另有 `dateRaw`）；`total`、`nextCursor`、`truncated`；
- `uncompletedPages`：已修改、尚未完成的页名（最多 50 个），另有 `uncompletedPageCount`；
- `markedPageCount`：带修订标记的页数（`PAGE_INSTANCE_WITH_REVISIONMARKER`，来自项目比较）；
- `hint`：`completed` 时说明写工具会失败；`tracking` 而 `uncompletedPages` 为空时说明这不等于没有修改。

一行摘要：`Revision in progress (change tracking on); 1 revision recorded, the last on 2026-10-21.` 只有状态、数目和日期：项目名、修订名、用户都是项目数据，只放在数据字段中（工具设计规范 7.3，2026-10-21 实现时改；原来的示例 `EPLAN_Sample_Project: …, last 'S23-3' by ASCKYE …` 把它们写进了摘要）。最后一条只在本页含有它时给出日期；有下一页时摘要说明 `(more: cursor)`。

工具描述写明：`completed` 的项目是写保护的，写工具会失败；要改它，工程师需在 EPLAN 中处理。

## 4. 错误
同读工具的通用规则（`PROJECT_NOT_OPEN`、`AMBIGUOUS_NAME`、`INVALID_ARGUMENT` 用于 `cursor` 和 `language`）。

## 5. 实现要点
- **状态**：按 `Project.ProjectLinkFilePath` 的扩展名判断（S23 r3，api-notes A28）：`.elk` / `.ell` / `.elr`；其他扩展名报 `state: unknown`，并在提示中给出扩展名。
- **修订记录**：项目的带索引属性 `PROJ_REVISION_LOG_NAME`、`_DESCRIPTION`、`_USER`、`_DATE`，`i` 从 1 起，读到名称为空为止，最多 1000：
  - `DATE` 读出为 Unix 秒数的字符串（S23 r3），转换为 UTC；转换失败时原样放进 `dateRaw`；
  - `DESCRIPTION` 是多语言文本，按读工具的语言回退取一种；
  - 未设置的属性不能调用 `ToString()`（编码规范 9.5 第 13 条），先查 `IsEmpty`。
- **未完成的页**：`new Revision().GetUncompletedPages(project)`；XML 文档说明从未"完成项目"时总是空数组，S23 r3 也是空的，所以结果为空时不等于"没有修改"，提示中写明。
- **带修订标记的页**：遍历页读 `PAGE_INSTANCE_WITH_REVISIONMARKER`。示例项目有几百页，耗时由 Large 测试记录；目前总是读（工具没有 `response_format`），超过 2 秒时再加 `response_format` 并只在 `detailed` 中给出。
- 在调度器中只读执行，套 `LockingStep`；不打开、不新建任何项目。
- **未验证**：
  - 页面级的修订记录（`PAGE_REVISION_LOG_*`）：S23 r3 中用 API 改页不会产生，界面中修改是否产生，没有验证，所以本工具不读它；
  - "完成项目"时给的名称存在哪里：它没有出现在 `PROJ_REVISION_LOG_NAME` 的前 5 条中；
  - `PROJ_REVISIONS[i]`（修订名列表）在 S23 r3 中始终为空，含义待查，本工具不读它。

## 6. 写操作专项
不适用（Read 级）。

## 7. 测试
- Small：扩展名到状态的映射、Unix 秒数的转换、分页。
- Medium：假 Bridge 上的三种状态、空记录、多语言说明。
- Large：
  - 夹具（`.elk`）：`state: none`，`revisions` 为空；
  - 由测试自己在 scratch 中复制一份夹具，用 S23 的做法新建修订（测试辅助代码，不是工具）：`state: tracking`，一条修订记录，用户和日期有值；之后删除这份副本。辅助程序是 `tests/EplanMcp.Large.RevisionHelper`，在离线的 EPLAN 中新建修订；读取经这份副本上的离线宿主（`EPLANMCP_OFFLINE_VARIANT_BIN`），不碰界面中开着夹具的 EPLAN（Large 测试 README）。
- Agent 评测："这个项目是不是已经完成修订、还能不能改？"

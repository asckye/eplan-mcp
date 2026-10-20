# 工具规格：`eplan_report_update`

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_report_update` |
| 所属分组 / Profile | `report` / `core` |
| 安全级别 | Write |
| MCP 注解 | `readOnlyHint` = false　`destructiveHint` = false　`idempotentHint` = true　`openWorldHint` = false |
| 状态 | Proposed（M4 第 7 步；进度和取消依赖第 3、4 步） |

## 1. 这个工具解决什么任务
"改完图，把报表更新一下"。代理放置设备、生成连接或删页之后（删页不会自动更新报表，A18），用它在 EPLAN 中更新报表：整个项目（"生成项目报表"），或者只更新指定的报表页。结果列出新增和删除的报表页。报表页由项目数据生成，随时可以重建，能被 `SafetyPoint` 回滚、一步撤销（S11），所以是 Write 级（M4 设计 Q3）。

## 2. 输入参数
| 参数 | 类型 | 必填 | 默认值 | 说明（写给模型看的） |
|---|---|---|---|---|
| `project` | string | 是 | — | 项目名，或 `eplan_project_list` 返回的 `path`。写工具必须显式给出 |
| `pages` | array of string，1–50 项 | 否 | 更新全部报表 | 只更新这些报表页，完整页名如 `&AAB1/1`（用 `eplan_page_list` 查） |
| `dry_run` | boolean | 否 | false | 为 true 时只返回将要更新的范围和现有的报表页数，不请求审批，也不修改项目 |

- 页名的查找规则与 `eplan_page_get` 相同；同一页出现两次时返回 `INVALID_ARGUMENT`。
- `pages` 中的每一页都必须是报表页（第 5 节）。这一版不更新嵌入在其他页上的报表。

## 3. 输出
```json
{
  "project": "Line1",
  "execution": "committed",
  "dryRun": false,
  "scope": "project",
  "reportPages": { "before": 204, "after": 205 },
  "added": ["+A2&EPA1/1.2"],
  "removed": [],
  "undo": "AI: eplan_report_update",
  "recovered": false,
  "truncated": false,
  "hint": null
}
```
- `scope`：`project`（全部报表）或 `pages`（只更新给出的页）。
- `reportPages`：执行前后项目中的报表页数。`added`、`removed`：执行前后比较报表页名得出的新增和删除，合计最多 100 条，超出时 `truncated = true`，`hint` 给出总数。内容被更新但页名不变的报表页不列出。
- `dry_run`：事先算不出会新建或删除哪些报表页（M4 设计 Q7），只返回 `reportPages.before`；`after`、`added`、`removed` 为 `null`，`hint` 说明这一点。
- 没有任何增删时仍然返回 `committed`，`added`、`removed` 为空。
- 页名是项目数据，经过 ResultShaper。
- `undo`、`recovered`：同 `eplan_page_create`。取回结果时没有读回的数据，`hint` 提示用 `eplan_page_list` 查看。
- 一行摘要：
  - `Updated all reports: 1 report page added, 0 removed; Ctrl+Z in EPLAN undoes this step.`
  - `Updated 2 report pages: 0 added, 0 removed; Ctrl+Z in EPLAN undoes this step.`
  - `Dry run: would update all reports (204 report pages now); the pages added or removed are known only after running. Nothing was changed.`

## 4. 错误
| 错误码 | 什么时候发生 | `execution` | 返回给模型的提示 |
|---|---|---|---|
| `INVALID_ARGUMENT` | `pages` 为空数组、超过 50 项或有重复；有页不是报表页 | `not_started` | `metadata.violations` 列出全部问题，按 `pages[i]` 标明位置；不是报表页时提示省略 `pages` 更新全部报表 |
| `PAGE_NOT_FOUND` / `AMBIGUOUS_NAME` | 有页名找不到，或只差大小写的页有多个 | `not_started` | 同 `eplan_connection_generate` |
| `PLAN_STALE` | 规划之后，项目的报表页发生了变化 | `not_started` | 提示重新调用 |
| `EPLAN_OPERATION_FAILED` | EPLAN 更新报表时报错 | `rolled_back` | 附带 EPLAN 的系统消息 |
| 策略、审批、流水线、锁定、项目解析、连接与授权类错误 | 同 `eplan_page_create` | 同 `eplan_page_create` | 同 `eplan_page_create` |

客户端取消调用时，按 M4 设计 4.3 处理：提交前发现取消就回滚；审计记录真实的 `execution`。

## 5. 实现要点
- **Host**：走 `WritePipeline`，规划操作是 `report.update.plan`，执行操作是 `report.update.execute`。执行时限 300 秒（M4 设计 4.1）。等待 Bridge 时每 5 秒发一次心跳进度，如 `Updating reports (40 s)`；指定页时改用分步进度 `Updating report page 3 of 10`（M4 设计 4.3）。
- **报表页**：`Page.ReportBlock` 不为 `null` 的页（XML："页上分配了报表时返回描述它的对象，否则返回 null"）。S11 按页类型（`DocumentType` 中的报表类型）数出示例项目有 204 张报表页，Large 测试用这个数核对 `ReportBlock` 的判断。
- **规划**（只读）：
  - 解析项目和各页，读出全部报表页名；
  - 全部报表：一条 `update` 变更，`Kind = "project"`，`Property = "reports"`，`OldValue` 为现有报表页数，`NewValue` 为 `null`。表单显示为 `update all reports (204 report pages now); may create, replace or delete report pages`（M4 设计 4.1）；
  - 指定页：每页一条 `update` 变更，`Kind = "page"`，`Property = "report"`。表单显示为 `update report page &AAB1/1`；
  - 指纹包括两部分，排序后取 SHA-256：全部报表页的会话内标识；指定页时，还有这些页的会话内标识。设计数据的变化不进入指纹：报表本来就按执行时的数据生成。
- **执行**：先重算指纹，不同就返回 `PLAN_STALE`。在 `TransactionScope`（`SafetyPoint` → `UndoStep` → `QuietModeStep`）中：
  1. 全部报表：`new Reports().GenerateProject(project)`；指定页：逐页 `Reports.Update(page)`，每页之前报告进度、检查取消；
  2. 再读全部报表页名，与执行前比较出新增和删除；
  3. 检查取消，然后 `Commit()`，再 `CloseUndo()`。
- **已验证**（S11，A16）：在调度器中，`GenerateProject` 用时 7.7–9.3 秒，示例项目新增 1 张报表页 `+A2&EPA1/1.2`（204 → 205），能被 `SafetyPoint` 回滚，提交后 Ctrl+Z 一步撤销；`Update(page)` 更新一张目录页 0.3 秒，页数不变；在 `QuietModeStep(ShowNoDialogs)` 下不弹对话框。
- **没有验证**，由 Large 测试确认：再更新一次是否不再增删（`idempotentHint`，不成立时改为 false）；`Update(page)` 会不会增删报表页（结果总是列出增删，表单注明"可能"）。
- `GenerateProject` 是一次 EPLAN 调用，中途无法打断，取消只在它返回之后生效。

## 6. 写操作专项
- **dry-run**：返回第 3 节的结果，`execution = not_started`，只有 `reportPages.before`。
- **审批**：项目在 scratch 之外时请求审批，表单见第 5 节，注明可以按 Ctrl+Z 撤销"AI: eplan_report_update"。
- **审计、回滚、各阶段失败时的 `execution`**：同 `eplan_page_create`。
- **幂等**：数据不变时，再更新一次不增删报表页（待 Large 测试确认）。

## 7. 测试
- **Small**：报表页名的前后比较；截断到 100 条；变更清单的文字；摘要；schema 快照；注解。
- **Medium**：在假 Bridge 上测试：
  - `dry_run`；不是报表页时的 `INVALID_ARGUMENT`；`PAGE_NOT_FOUND` 列出全部缺失；`PLAN_STALE`；
  - EPLAN 报错时返回 `rolled_back`；
  - 心跳和分步进度；逐页更新时在第二页之前取消，回滚。
- **Large**（示例项目的 scratch 副本）：
  - `dry_run` 返回 204 张报表页；更新全部报表：新增 `+A2&EPA1/1.2`，共 205 张（S11）；
  - 再更新一次：不增删；
  - 只更新目录页 `&AAB1/1`：页数不变；
  - 电路图页 `=GAA+A1&EFS1/1` 作为 `pages`：`INVALID_ARGUMENT`；
  - scratch 之外的副本：`POLICY_DENIED`，项目文件不变。
- **Agent 评测**："在 scratch 项目中更新全部报表，报告新增了哪些报表页"，作答后用 `eplan_page_list` 核对。

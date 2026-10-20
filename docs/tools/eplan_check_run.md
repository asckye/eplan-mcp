# 工具规格：`eplan_check_run`

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_check_run` |
| 所属分组 / Profile | `check` / `core` |
| 安全级别 | Write |
| MCP 注解 | `readOnlyHint` = false　`destructiveHint` = false　`idempotentHint` = true　`openWorldHint` = false |
| 状态 | Proposed（M4 第 6 步；已实现，待实机验证；进度和取消依赖第 3、4 步） |

## 1. 这个工具解决什么任务
"改完了，检查一下有没有错误"。代理改完图之后，用它在 EPLAN 中运行检查（消息管理中的"检查项目"），得到各级别的消息数和前 20 条消息，再决定下一步。运行检查会替换项目中的检查消息，改写项目的数据文件（A11、A16），所以是 Write 级（M4 设计 Q3）：scratch 中直接执行，真实项目需要审批，按一次 Ctrl+Z 撤销。只读取上一次检查的结果用 `eplan_check_list`。

## 2. 输入参数
| 参数 | 类型 | 必填 | 默认值 | 说明（写给模型看的） |
|---|---|---|---|---|
| `project` | string | 是 | — | 项目名，或 `eplan_project_list` 返回的 `path`。写工具必须显式给出 |
| `pages` | array of string，1–50 项 | 否 | 整个项目 | 只检查这些页，完整页名如 `=GAA+A1&EFS1/60`（用 `eplan_page_list` 查） |
| `scheme` | string | 否 | EPLAN 当前的检查方案 | 检查方案的名称 |
| `dry_run` | boolean | 否 | false | 为 true 时只返回检查的范围和现有的消息数，不请求审批，也不修改项目 |

- 页名的查找规则与 `eplan_page_get` 相同；同一页出现两次时返回 `INVALID_ARGUMENT`。
- 不提供"只复查已处理的消息"：`VerifyProject` 的第三个参数固定为 false。

## 3. 输出
```json
{
  "project": "Line1",
  "execution": "committed",
  "dryRun": false,
  "pageCount": 321,
  "scheme": "<方案名>",
  "before": 0,
  "counts": { "error": 0, "warning": 6, "hint": 14 },
  "messages": [
    { "number": 4025, "severity": "warning", "region": "Connections", "text": "…", "done": false, "object": { "kind": "Function", "name": "=GAA+A1-FC1" } }
  ],
  "total": 20,
  "truncated": false,
  "undo": "AI: eplan_check_run",
  "recovered": false,
  "hint": null
}
```
- `pageCount`：检查的页数。`scheme`：使用的方案（见第 5 节）。
- `before`：检查前项目中的检查消息数，包括已处理的消息，变更清单中也是这个数。
- `counts`、`messages`、`total`：检查后读回的消息，规则与 `eplan_check_list` 的默认参数相同（不含已处理的消息，`messages` 的格式也相同）。`messages` 最多 20 条，超出时 `truncated = true`，`hint` 提示用 `eplan_check_list` 分页查看。
- `dry_run` 时 `counts`、`messages`、`total` 为 `null`。
- 消息文本和对象名称是项目数据，经过 ResultShaper。
- `undo`、`recovered`：同 `eplan_page_create`。取回结果（`recovered`）时没有读回的消息，`hint` 提示用 `eplan_check_list` 查看。
- 一行摘要（不回显消息文本）：
  - `Check found 6 warnings, 14 hints (0 messages before); Ctrl+Z in EPLAN undoes this step.`
  - `Dry run: would check 321 pages and replace the 0 existing check messages. Nothing was changed.`

## 4. 错误
| 错误码 | 什么时候发生 | `execution` | 返回给模型的提示 |
|---|---|---|---|
| `INVALID_ARGUMENT` | `pages` 为空数组、超过 50 项或有重复；规划时发现 `scheme` 不存在 | `not_started` | `metadata.violations` 列出全部问题；方案不存在时 `metadata.candidates` 列出可用的方案（最多 20 个） |
| `INVALID_ARGUMENT` | 方案的设置节点没有查明时，`scheme` 不存在要到执行时才由 EPLAN 发现（第 5 节） | `rolled_back` | 提示省略 `scheme`，用 EPLAN 当前的方案 |
| `PAGE_NOT_FOUND` / `AMBIGUOUS_NAME` | 有页名找不到，或只差大小写的页有多个 | `not_started` | 同 `eplan_connection_generate` |
| `PLAN_STALE` | 规划之后，检查范围内的页或项目的检查消息数发生了变化 | `not_started` | 提示重新调用 |
| `EPLAN_OPERATION_FAILED` | EPLAN 检查时报错 | `rolled_back` | 附带 EPLAN 的系统消息 |
| 策略、审批、流水线、锁定、项目解析、连接与授权类错误 | 同 `eplan_page_create` | 同 `eplan_page_create` | 同 `eplan_page_create` |

客户端取消调用时，按 M4 设计 4.3 处理：检查返回之后、提交之前发现取消就回滚；审计记录真实的 `execution`。规范规定不回复被取消的请求。

## 5. 实现要点
- **Host**：走 `WritePipeline`，规划操作是 `check.run.plan`，执行操作是 `check.run.execute`。执行时限 120 秒（M4 设计 4.1）。等待 Bridge 时每 5 秒发一次心跳进度，如 `Running the EPLAN check (40 s)`（M4 设计 4.3）。
- **规划**（只读）：
  - 解析项目和各页，读出现有的检查消息数（`PrjMessagesCollection`，同 `eplan_check_list`）；
  - 一条 `update` 变更：`Kind = "project"`，`Property = "check messages"`，`OldValue` 为现有消息数，`NewValue` 为 `null`。表单显示为 `run check on 321 pages (scheme: <名称>), replacing the 0 existing check messages`，`ChangeLines` 按 `Property` 给出这种写法；
  - 指纹包括两部分，排序后取 SHA-256：检查范围内各页的会话内标识（整个项目时是全部页）；现有消息数。
- **执行**：先重算指纹，不同就返回 `PLAN_STALE`。在 `TransactionScope`（`SafetyPoint` → `UndoStep` → `QuietModeStep`）中：
  1. 整个项目：`new Check().VerifyProject(project, scheme, false)`；指定页：`VerifyPages(new ArrayList(pages), scheme)`；
  2. 用一个 `PrjMessagesCollection` 读回消息，复制到 DTO（A11：集合不能交叉使用）；
  3. 检查取消，然后 `Commit()`，再 `CloseUndo()`。
- **方案**：检查方案在哪个设置节点，S11 没有查到（M4 设计第 1 节）。实现前先在 scratch 副本上查明，写入 api-notes：
  - 查到时：规划阶段用 `SchemeSetting.Init(节点)`、`CheckIfSchemeExists` 校验，候选用 `GetCount`、`SetScheme(i)`、`GetName` 列出（XML：`SetScheme` 不改"上次使用"）。省略 `scheme` 时传空串，结果回显 `GetLastUsed()`。传入方案时，执行前后比较 `GetLastUsed()`，被改写就用 `SetLastUsed` 写回原值（M4 设计第 5 节），写不回时放在 `hint` 中；
  - 查不到时：不在规划阶段校验，也没有候选；结果的 `scheme` 只回显传入的名称，省略时为 `null`。
- **已验证**（S11，A16）：在调度器中，`VerifyProject(project, "", false)` 用时 2.7–4.7 秒，消息 0 → 20 条，能被 `SafetyPoint` 回滚，提交后 Ctrl+Z 一步撤销；`VerifyPages`（2 页）0.2 秒；在 `QuietModeStep(ShowNoDialogs)` 下不弹对话框。
- **没有验证**，由 Large 测试确认，结论写入 api-notes：
  - 再检查一次是替换还是追加消息。追加时 `idempotentHint` 改为 false，变更清单的文字也要改；
  - 按页检查是否删除其他页的消息；
  - 已标记为"已处理"的消息在重新检查后是否保留；
  - 传入具体的方案名。
- 一次 EPLAN 调用中途无法打断，取消只在它返回之后生效（M4 设计 4.3）。
- `eplan_check_list` 在没有检查消息时的 `hint`（"需要工程师在 EPLAN 中运行检查"）随本工具改为提示 `eplan_check_run`。

## 6. 写操作专项
- **dry-run**：返回第 3 节的结果，`execution = not_started`，只有 `pageCount`、`scheme`、`before`。
- **审批**：项目在 scratch 之外时请求审批，表单为一行变更（第 5 节），注明可以按 Ctrl+Z 撤销"AI: eplan_check_run"。
- **审计、回滚、各阶段失败时的 `execution`**：同 `eplan_page_create`。
- **幂等**：同一范围、同一方案再检查一次，得到同样的消息（待 Large 测试确认，见第 5 节）。

## 7. 测试
- **Small**：变更清单的文字；指纹；`counts` 与 `eplan_check_list` 一致；方案不存在时的候选；摘要；schema 快照；注解。
- **Medium**：在假 Bridge 上测试：
  - `dry_run`；`PAGE_NOT_FOUND` 列出全部缺失；重复页名；`PLAN_STALE`；
  - EPLAN 报错时返回 `rolled_back`；
  - 心跳进度；检查返回后、提交前取消时回滚。
- **Large**（示例项目的 scratch 副本，夹具中没有检查消息）：
  - `dry_run` 返回 321 页、0 条消息；检查整个项目：6 条警告、14 条提示（S6、S11），`eplan_check_list` 读到同样的 20 条；
  - 再检查一次：仍是 20 条；
  - 只检查 `=GAA+A1&EFS1/1`、`/2`，比较其他页的消息；
  - 用 `dry_run` 回显的方案名检查，结果相同；不存在的方案返回 `INVALID_ARGUMENT`；
  - scratch 之外的副本：`POLICY_DENIED`，项目文件不变。
- **Agent 评测**："在 scratch 项目上运行检查，报告有几条警告、都是哪一类问题"，作答后用 `eplan_check_list` 核对。

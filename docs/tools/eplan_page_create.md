# 工具规格：`eplan_page_create`

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_page_create` |
| 所属分组 / Profile | `page` / `core` |
| 安全级别 | Write |
| MCP 注解 | `readOnlyHint` = false　`destructiveHint` = false　`idempotentHint` = false　`openWorldHint` = false |
| 状态 | Proposed（M3a 第 10 步） |

## 1. 这个工具解决什么任务
"在 `=GAA+A1` 下新建 EFS1 的第 5 到第 8 页电路图，页描述是……"。代理给出完整页名，一次调用建好全部页面：只审批一次，在 EPLAN 中按一次 Ctrl+Z 就能全部撤销。

## 2. 输入参数
| 参数 | 类型 | 必填 | 默认值 | 说明（写给模型看的） |
|---|---|---|---|---|
| `project` | string | 是 | — | 项目名，或 `eplan_project_list` 返回的 `path`。写工具必须显式给出 |
| `pages` | array，1–50 项 | 是 | — | 要新建的页，按给出的顺序创建 |
| `pages[].name` | string | 是 | — | 完整页名，如 `=GAA+A1&EFS1/5`。结构部分参照 `eplan_page_list` 中的已有页 |
| `pages[].page_type` | enum `Circuit` / `CircuitSingleLine` / `Overview` / `Graphics` | 否 | `Circuit` | 页类型 |
| `pages[].description` | string，最长 500 个字符 | 否 | — | 页描述 |
| `language` | string | 否 | EPLAN 界面语言 | 页描述的语言，如 `zh_CN`、`en_US` |
| `dry_run` | boolean | 否 | false | 为 true 时只返回将要新建的页，不请求审批，也不修改项目 |

- **页名的格式**：`[=<值>][+<值>][&<值>]/<页号>`。
  - 只支持这四种前缀：`=` 对应 `DESIGNATION_PLANT`，`+` 对应 `DESIGNATION_LOCATION`，`&` 对应 `DESIGNATION_DOCTYPE`，`/` 对应 `PAGE_COUNTER`（api-notes A13）。
  - 值不能为空，不能含空白、控制字符和 `=+&/#`。
  - 页号是正整数，不带前导零。
  - 其他前缀（`==`、`++`、`#`）这一版不支持，返回 `INVALID_ARGUMENT`。
- **与 M3 设计 4.4 的差别**：`language` 由每页一个改为整个调用一个。同一次调用中各页的描述通常是同一种语言，这样参数更少。
- `page_type` 只开放四种可以交互编辑的页类型。S9 只验证过 `Circuit`，其余三种由 Large 测试逐个验证，未通过的从 `enum` 中去掉。

## 3. 输出
```json
{
  "project": "Line1",
  "execution": "committed",
  "dryRun": false,
  "pages": [
    { "name": "=GAA+A1&EFS1/5", "type": "Circuit", "description": "电源" }
  ],
  "undo": "AI: eplan_page_create",
  "recovered": false,
  "hint": null
}
```
- `pages`：
  - 提交后，是读回的实际页名、类型和页描述；
  - `dry_run` 时，是将要新建的页。
- `execution`：`committed`；`dry_run` 时为 `not_started`。
- `undo`：EPLAN 撤销列表中这一步的名称。`dry_run` 时为 `null`。
- `recovered`：响应丢失，但向 Bridge 取回的结果是已提交（ADR-0010 第 2 节）。这时没有读回的数据，`pages` 按计划列出，`hint` 提示用 `eplan_page_list` 查看实际结果。
- 页描述按项目数据处理，经过 ResultShaper。
- 一行摘要：
  - `Created 4 pages in 'Line1'; Ctrl+Z in EPLAN undoes this step.`
  - `Dry run: would create 4 pages in 'Line1'. Nothing was changed.`
- 大小：最多 50 页，每页约 40 token，在默认预算之内。

## 4. 错误
| 错误码 | 什么时候发生 | `execution` | 返回给模型的提示 |
|---|---|---|---|
| `INVALID_ARGUMENT` | 参数不合法：页名不符合格式、同一次调用中页名重复（不区分大小写）、`language` 不认识等 | `not_started` | `metadata.violations` 一次列出全部问题，按 `pages[i]` 标明位置 |
| `INVALID_ARGUMENT` | 建页后读回的页名与请求不同：项目的页结构不用页名中的某一部分，EPLAN 把它丢掉了（api-notes A13） | `rolled_back` | "This project's page structure does not use every part of '<请求>'; EPLAN would have named it '<实际>'. Use the parts that existing page names use (eplan_page_list)."，`metadata.requested`、`metadata.actual` |
| `ALREADY_EXISTS` | 规划时已有同名页（不区分大小写） | `not_started` | `metadata.existing` 列出已有的页名，提示换用未占用的页号，可以用 `eplan_page_list` 的 `name_prefix` 查看已用的页号 |
| `ALREADY_EXISTS` | 执行时 EPLAN 报告重名（`NameConflictException`） | `rolled_back` | 同上 |
| `PLAN_STALE` | 规划之后，目标结构下的页发生了变化（等待审批期间工程师建了页） | `not_started` | 提示重新调用 |
| `POLICY_DENIED` / `APPROVAL_DENIED` / `APPROVAL_EXPIRED` | 策略或审批（M3 设计 4.3） | `not_started` | 同错误码表 |
| `WRITES_SUSPENDED` / `AUDIT_UNAVAILABLE` / `OUTCOME_UNKNOWN` | 写入流水线（ADR-0010） | 同错误码表 | 同错误码表 |
| `LOCK_FAILED` | 对象被其他用户锁定 | `rolled_back` | 同错误码表 |
| `EPLAN_OPERATION_FAILED` | EPLAN 的其他异常 | `rolled_back` | 附带 EPLAN 的系统消息 |
| `PROJECT_NOT_OPEN` / `AMBIGUOUS_NAME` | 项目解析失败 | `not_started` | 同 `eplan_project_get` |
| 连接与授权类错误、`TIMEOUT`、`INTERNAL` | 同 `eplan_project_list`；`TIMEOUT` 只在规划阶段出现 | 同错误码表 | 同错误码表 |

## 5. 实现要点
- **Host**：走 `WritePipeline`，规划操作是 `page.create.plan`，执行操作是 `page.create.execute`。执行时限为 `min(300, 60 + 2 × 页数)` 秒（M3 设计 4.3）。
- **Bridge.Core**：
  - `PageNames.Parse` 按第 2 节的格式拆分页名，是纯函数，由 Small 测试覆盖；
  - `PageCreateOperation` 实现 `IWriteOperation`；
  - 适配层只提供三件事：全部页名、按各部分建一页（返回读回的页名）、写页描述。
- **规划**：
  - 解析项目，校验全部页名，与已有页名做不区分大小写的比较；
  - 每页一条 `create` 变更，`NewValue` 为页类型和页描述。审批表单显示为 `create page =GAA+A1&EFS1/5 (Circuit, "电源")`，需要在 `ChangeLines` 的 create 分支中补上 `NewValue`；
  - 指纹是每个目标结构（页名中 `/` 之前的部分）下已有页名的集合，排序后取 SHA-256。
- **执行**：
  - 先重算指纹，不同就返回 `PLAN_STALE`；
  - 在 `TransactionScope` 中按顺序处理每一页：
    - 用 `PagePropertyList` 设置各部分，调用 `Page.Create(project, DocumentType, properties)`；
    - 有页描述时，用只含 `language` 一种语言的 `MultiLangString` 写入 `PAGE_NOMINATIOMN`（新页没有其他语言，A13）；
    - 读回 `Page.Name` 和页描述，与请求做精确比较，不同就抛出，整体回滚；
  - 全部通过后 `Commit()`，再 `CloseUndo()`。
- **包装**：`LockingStep → SafetyPoint → UndoStep("AI: eplan_page_create") → QuietModeStep`。S2 和 S1 已验证这种包装能撤回新建的页，并且能用 Ctrl+Z 撤销（A7、A9）。S9 证实它不适用于按符号新建设备，但建页不受影响（A13）。
- **耗时**：S9 中建 5 页约 50 ms，50 页远低于 2 秒，不需要进度通知。
- 不是幂等的：同样的调用再来一次会返回 `ALREADY_EXISTS`。执行请求从不重发（ADR-0010）。

## 6. 写操作专项
- **dry-run**：返回第 3 节的结果，`execution = not_started`，`pages` 为将要新建的页。
- **审批**：项目在 scratch 之外时请求审批。表单显示工具、项目、每页一行的变更清单，并注明可以按 Ctrl+Z 撤销"AI: eplan_page_create"（M3 设计 4.6）。
- **审计**：流水线写入两条记录：
  - `execution_started`：请求 ID、工具、项目路径、EPLAN 版本与进程、参数、变更清单；
  - `execution_result`：`execution`、`reason`、`recovered`。
- **回滚**：
  - 执行中失败时，`SafetyPoint` 不提交，整体回滚；
  - 提交后，工程师可以在 EPLAN 中按一次 Ctrl+Z 撤销。
- **各阶段失败时的 `execution`**：
  - 规划、策略、审批阶段：`not_started`；
  - 事务中：`rolled_back`；
  - 发出执行请求后响应丢失：按 ADR-0010 取回结果，取不回就返回 `OUTCOME_UNKNOWN`（`unknown`），并暂停该项目的写入。

## 7. 测试
- **Small**：
  - `PageNames.Parse` 的格式表：合法；空值；空白；`==`、`++`、`#`；页号为 0、前导零、非数字；缺少 `/`；
  - 调用内重复；指纹的稳定性（与页的顺序、大小写无关）；执行时限；
  - 摘要、schema 快照、注解。
- **Medium**：在假 Bridge 上测试：
  - `dry_run`；`ALREADY_EXISTS`；`PLAN_STALE`；
  - 读回不一致时返回 `rolled_back`；响应丢失后取回已提交的结果（`recovered`）；
  - scratch 之外的项目返回 `POLICY_DENIED`。
- **Large**（示例项目的 scratch 副本）：
  - 新建 `=GAA+A1&EFS1/60` 和 `/61`，带中文页描述，用 `eplan_page_get` 读回；
  - 再调用一次，返回 `ALREADY_EXISTS`；
  - 四种页类型各建一页；
  - scratch 之外的副本：返回 `POLICY_DENIED`，项目文件不变（Q1）；
- **Q2、Q6 的测试方式**（第 10 步提议，产品代码中不加故障注入钩子）：
  - Q2（事务中失败后回滚）：Small 测试用假事务（回滚时恢复快照，模拟 SafetyPoint）覆盖第二页时失败、页名被改、描述被改、执行时重名，以及提交、回滚本身失败（`unknown`）；Medium 测试经真实管道走完整个工具。EPLAN 一侧，SafetyPoint 撤回新建的页和改过的属性已由 S1、S2 验证（A7、A9）。实机用自然发生的失败：`eplan_device_update` 给电缆赋带 `=` 的标识，EPLAN 丢掉这一部分（A13），读回不一致而回滚；多页中途失败只在 S8 的 spike 构建中用故障点验证。
  - Q6（规划之后项目变化）：Small 测试在规划和执行之间往目标结构中加页；Medium 测试让假读取器在两次调用之间变化。实机放在第 13 步的 Large 测试中：直接经 `BridgeClient` 调用 `page.create.plan`，在同一结构中建一页，再用旧指纹调用 `page.create.execute`，返回 `PLAN_STALE`。
- **Agent 评测**："在 scratch 项目的 `=GAA+A1` 下新建 EFS1 的第 60、61 页电路图，页描述分别为'电源'和'控制'"，作答后用 `eplan_page_list` 核对。

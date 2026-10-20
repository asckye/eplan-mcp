# 工具规格：`eplan_connection_generate`

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_connection_generate` |
| 所属分组 / Profile | `connection` / `core` |
| 安全级别 | Write |
| MCP 注解 | `readOnlyHint` = false　`destructiveHint` = false　`idempotentHint` = true　`openWorldHint` = false |
| 状态 | Proposed（M3b 第 5 步；已实现，等待实机验证） |

## 1. 这个工具解决什么任务
"把新放的回路连起来""这一页的连线生成连接"。在 EPLAN 中，两个连接点在同一条线上相对时，图上会画出自动连线，但还没有 `Connection` 对象（A3），导线清单、端子图等报表也就看不到它们。代理插入宏、放好设备之后，用它在指定的页上执行"生成连接"，让画出的连线成为真正的连接（M3b 设计 4.3，Q4）。

## 2. 输入参数
| 参数 | 类型 | 必填 | 默认值 | 说明（写给模型看的） |
|---|---|---|---|---|
| `project` | string | 是 | — | 项目名，或 `eplan_project_list` 返回的 `path`。写工具必须显式给出 |
| `pages` | array of string，1–50 项 | 是 | — | 完整页名，如 `=GAA+A1&EFS1/60`（用 `eplan_page_list` 查） |
| `dry_run` | boolean | 否 | false | 为 true 时只返回这些页上现有的连接数，不请求审批，也不修改项目 |

- 页名的查找规则与 `eplan_page_get` 相同；同一页出现两次时返回 `INVALID_ARGUMENT`。
- 只做"更新"（`bRebuildAllConnections = false`），不提供"重建全部"，也不对整个项目执行（Q4）。

## 3. 输出
```json
{
  "project": "Line1",
  "execution": "committed",
  "dryRun": false,
  "pages": [
    {
      "page": "=GAA+A1&EFS1/60",
      "before": 0,
      "after": 9,
      "added": [ { "from": { "device": "+A1-XG9:?1", "pin": null }, "to": { "device": "=GAA+A1-?QA1", "pin": "14" } } ],
      "removed": []
    }
  ],
  "undo": "AI: eplan_connection_generate",
  "recovered": false,
  "truncated": false,
  "hint": null
}
```
- `before`、`after`：执行前后页上的连接数（2D 连接，在内存中按页过滤，A11）。
- `added`、`removed`：执行前后比较得出的连接，端点的写法同 `eplan_connection_list`。所有页合计最多列出 100 条，删除的优先（"只更新会不会删除连接"没有验证，见第 5 节），超出时 `truncated = true`，`hint` 给出总数，提示用 `eplan_connection_list` 的 `page` 查看。
- 没有任何变化时（连接已经齐全，S10），仍然返回 `committed`，`added`、`removed` 为空，`hint` 说明已是最新；`undo` 为 `null`，因为 EPLAN 不为没有改动的生成留下撤销步骤（实机验证，见 api-notes A19）。
- **`dry_run`**：无法预先算出 EPLAN 会生成哪些连接，所以只返回 `before`。`added`、`removed`、`after` 为 `null`，`hint` 说明这一点。
- 标识按项目数据处理，经过 ResultShaper。
- 一行摘要（只有计数，不回显项目名，架构 8.5）：
  - `Generated connections on 1 page: 9 added, 0 removed; Ctrl+Z in EPLAN undoes this step.`
  - `Generated connections on 1 page: nothing added or removed, so there is nothing to undo.`
  - `Dry run: 1 page has 0 connections now; the new ones are known only after running. Nothing was changed.`

## 4. 错误
| 错误码 | 什么时候发生 | `execution` | 返回给模型的提示 |
|---|---|---|---|
| `INVALID_ARGUMENT` | `pages` 为空、超过 50 项或有重复 | `not_started` | `metadata.violations` 列出全部问题 |
| `PAGE_NOT_FOUND` | 有页名找不到 | `not_started` | `metadata.missing` 列出全部找不到的页名 |
| `AMBIGUOUS_NAME` | 精确匹配找不到，而不区分大小写时有多个页匹配 | `not_started` | 同 `eplan_page_get` |
| `PLAN_STALE` | 规划之后，这些页上的对象或连接发生了变化 | `not_started` | 提示重新调用 |
| `EPLAN_OPERATION_FAILED` | EPLAN 生成连接时报错（`BaseException`） | `rolled_back` | `metadata.eplanMessage` 附带 EPLAN 的消息（最多 500 字符） |
| 策略、审批、流水线、锁定、项目解析、连接与授权类错误 | 同 `eplan_page_create` | 同 `eplan_page_create` | 同 `eplan_page_create` |

## 5. 实现要点
- **Host**：走 `WritePipeline`，规划操作是 `connection.generate.plan`，执行操作是 `connection.generate.execute`。执行时限为 `min(300, 60 + 2 × 页数)` 秒，与建页相同。
- **规划**（只读）：
  - 解析项目和每一页，读出各页现有的连接；
  - 每页一条 `update` 变更：`Kind = "page"`，`Property = "connections"`，`OldValue` 为现有连接数，`NewValue` 为 `null`（执行后才知道）。表单显示为 `generate connections on page =GAA+A1&EFS1/60 (now 0; may add or remove connections)`；
  - 指纹包括两部分，排序后取 SHA-256：各页上的连接（两端的标识和连接点）；页上全部对象的会话内标识。
- **执行**：在 `TransactionScope` 中执行：
  1. 记下各页的连接；
  2. `new Generate().Connections(pages, false)`；
  3. 再读各页的连接，比较出新增和删除的连接。一条连接按两端的（功能，连接点）比较，不分起止方向，重复的按次数计：S10 只验证了已齐全的页连接数不变，没有验证连接对象和方向是否保持；
  4. `Commit()`，再 `CloseUndo()`。
- **已验证**（S9、S10，api-notes A13、A14）：
  - 生成连接把画出的自动连线变成 `Connection`；
  - 在 `SafetyPoint` 中能回滚；
  - 在调度器中占一个撤销步骤，Ctrl+Z 只撤销这一步；
  - 对连接已经齐全的页不增删连接。
- **没有验证**："只更新"在什么情况下会删除连接（例如画面改动之后留下的旧连接）。所以结果总是列出删除的连接，表单注明"可能新增或删除连接"。
- **耗时**：S10 中一页约 0.1 秒，50 页由 Large 测试测量。

## 6. 写操作专项
- **dry-run**：返回第 3 节的结果，`execution = not_started`，只有 `before`。
- **审批**：项目在 scratch 之外时请求审批，表单每页一行（见第 5 节），注明可以按 Ctrl+Z 撤销"AI: eplan_connection_generate"。
- **审计、回滚、各阶段失败时的 `execution`**：同 `eplan_page_create`。
- **幂等**：连接已经齐全时，再次执行不增删连接（S10）。

## 7. 测试
- **Small**：新增与删除的比较；截断到 100 条；变更清单的文字；摘要；schema 快照；注解。
- **Medium**：在假 Bridge 上测试：
  - `dry_run`；`PAGE_NOT_FOUND` 列出全部缺失；重复页名；`PLAN_STALE`；
  - EPLAN 报错时返回 `rolled_back`。
- **Large**（示例项目的 scratch 副本）：
  - 用 `eplan_macro_insert` 在新页上插入 `Contactor_Feedback_Control.ema`，再对这一页生成连接：新增 9 条（S10），`eplan_connection_list` 的 `page` 能查到；
  - 再执行一次：不增删连接；
  - 对已有 40 个连接的 `=GAA+A1&EFS1/1` 执行：不增删连接。
- **Agent 评测**："在 scratch 项目的 `=GAA+A1&EFS1/60` 页上放一个接触器反馈控制回路，并把连线生成连接"，作答后用 `eplan_connection_list` 核对。

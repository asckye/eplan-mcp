# 工具规格：`eplan_plan_get`

| 项 | 内容 |
|---|---|
| 工具名 / title | `eplan_plan_get` / `Get plan` |
| 所属分组 / Profile | `plan`；工具集 `workflow` / `full`，不进 `core` |
| 安全级别 | Read |
| MCP 注解 | `readOnlyHint` = true　`destructiveHint` = false　`idempotentHint` = true　`openWorldHint` = false |
| 状态 | Proposed（M6 第 2 阶段；未实现） |

## 1. 这个工具解决什么任务
“查看这个计划准备做什么、已经做了什么、校验及差异是什么”。读取已保存计划，不重新规划、不触发审批/执行。与 task_get 的等待进度不同，本工具提供工程审查内容；按 [M6 4.9](../design/2026-10-21-m6-engineering-packs.md) 默认只给摘要，详细条目按需读取。

## 2. 输入参数
| 参数 | 类型 | 必填 | 默认值 | 说明（写给模型看的） |
|---|---|---|---|---|
| `plan_id` | string，非空 | 是 | — | 编译/执行返回的 ID，不是文件路径 |
| `response_format` | enum `concise` / `detailed` | 否 | `concise` | 摘要或逐项计划、结果、校验和差异 |
| `limit` | integer，1–200 | 否 | 50 | detailed 每页条目上限（提议）；预算可能提前截页 |
| `cursor` | string | 否 | — | 前页的不透明游标，只用于同一计划/格式/结果修订 |

schema 为对象、`additionalProperties: false`，默认值、枚举和边界写入 schema。concise 不接受 cursor；项目由计划回显，不使用当前项目。分页参数定稿见 Q1。

## 3. 输出
```json
{
  "planId": "<id>", "project": { "name": "Line1", "path": "<target>" }, "cabinet": "default",
  "status": "partially_committed", "createdAt": "2026-10-22T10:00:00Z", "expiresAt": "2026-10-23T10:00:00Z",
  "plan": {}, "result": {}, "summary": { "totalSteps": 3, "completedSteps": 1 },
  "total": 0, "entries": [], "nextCursor": null, "truncated": false, "hint": null,
  "resource": "eplanmcp://plans/<id>"
}
```

- `outputSchema` 声明全部字段；ID/状态/URI 为 string，时间为 UTC RFC 3339，计数为非负 integer，游标/hint 为 string 或 null。`status` 枚举为 compiled/approved/running 及 apply 的五个终态；任务的 waiting_approval 由关联 taskId 查询，不能伪造 approved。
- `plan` 保留 workflow_plan 的 planHash、tier、期限、来源及 contextRef；concise 另给各步序号/op/目标/依赖和计数，超预算提示切 detailed。detailed 的完整步骤放 entries 分页，不每页重复大数组。`result` 未执行时为 null；否则保留 apply 的 execution、失败原因、完成计数、备份、verification、validation 和 diff 摘要/关联 taskId，完整步骤结果也由 entries 续取。**顶层不加 execution**，嵌套原样保留 partially_committed/unknown，不把任务完成等同于校验通过。
- detailed 的 `entries` 为带 `section`（step/validation/diff）及步骤号/定位符的完整条目流；step 包含 args、范围、前置条件、预期、预测模式与执行结果/撤销名称，从所有页拼回完整计划与结果。差异保留“按计划、未出现、计划外范围内、范围外、外部修改”五类、前/预期/后值、覆盖范围和来源；未知/未覆盖如实标记。总数 `total` 为该条目流条数，不是项目对象数。
- 输出 structuredContent、同内容 JSON 文本、一行摘要 `Plan has 1 of 3 steps committed; verification was skipped.` 和 resource_link。预算含双份 JSON，默认约 5k、硬上限 20k token；达到预算在条目间停止，`nextCursor`/`truncated`/`hint` 指示续读，不能截断定位符、哈希或错误。摘要保留失败及范围外警报。
- Resource `eplanmcp://plans/{planId}` 提供同一计划/结果的数据视图与同样的预算/访问边界，详细内容可经工具游标完整取回；无 Resources 客户端照样用本工具。资源读取不执行计划。TTL/cacheScope、资源更新通知的 SDK 接入未核实，不承诺订阅（Q2）。

## 4. 错误
| 错误码 | 什么时候发生 | 返回给模型的提示 |
|---|---|---|
| `INVALID_ARGUMENT` | 空 ID、格式/分页不合法；游标不符或结果修订已变化 | `metadata.violations`；游标过期时提示从第一页重读，不重新执行 |
| `RESOURCE_NOT_FOUND` | ID 不存在、已清理或不可见 | 提示检查原编译/执行结果；查询记录丢失不代表工程未修改，不重放写入 |
| `INTERNAL` | Host 读取缺陷/计划损坏 | 附关联 ID；保留原件供工程师检查，不自动修补或执行 |

查询错误不带 execution；计划执行失败、结果未知或前置条件过期是查询成功的数据，不因此置 isError。RESOURCE_NOT_FOUND 的标准/Contracts 登记随 M6 实现补齐。

## 5. 实现要点
- Host 读取一致的计划/结果快照，不调用 Bridge 或 EPLAN，无 SafetyPoint/LockingStep/QuietModeStep；EPLAN 离线仍可查。游标绑定 planId、格式、结果修订，不能在执行更新时拼接两个版本。
- 24 小时是**可执行期**，过期计划只要记录尚在仍可查询，不在 get 中变成执行结果或刷新期限。文件位置沿用 workflow_plan；结果落盘不提供第 5 阶段的检查点恢复。保留/容量/原子更新沿用其 Q2。
- 名称、自由文本及来源说明只是数据，按 ResultShaper 清洗/预算；不返回审批令牌、堆栈或内部管道名（SEC-08/10/12）。先沿用现有归属/权限检查，再读记录；URI/ID 不是授权凭据，不新增跨客户端共享能力。

## 6. 写操作专项
不适用。无 dry_run、审批、撤销；查看 unknown 不解除暂停，查看计划不消耗它。

## 7. 测试
- **Small**：schema/title/四注解、状态和 nullable 结果；concise/detailed 字段差异；分页/预算/清洗；嵌套执行状态、校验 unknown 与差异五分类；过期仍可读。
- **Medium**：Host 仓库/资源协议；运行中一致视图、游标失效、隐藏/丢失 ID；已部分提交/未知/取消的计划保留备份和完成步骤；离线查询不发 Bridge 请求、不重新执行或审批。
- **Large / Agent**：无需独立 EPLAN 行为验证；随 scratch 完整任务检查 detailed 可追溯到源对象（4.17 场景 3），中途失败/断开后能报告完成步骤、校验边界及下一步（场景 7），不把它们写成已通过验收。

## 8. 待定问题
| # | 问题 | 建议 |
|---|---|---|
| Q1 | 条目流 schema、结果修订/分页上限未定 | 上述 50/200 为提议，与计划结果合同一起定稿；完整数据不能因预算丢失 |
| Q2 | 资源缓存、订阅和跨客户端可见性如何落地？ | Resources 的 SDK 支持需核实，先保留工具替代；归属与 task_get Q3 一起澄清，不自行定义新认证/共享模型（L3） |

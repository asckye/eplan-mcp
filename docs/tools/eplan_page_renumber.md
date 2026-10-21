# 工具规格：`eplan_page_renumber`

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_page_renumber` |
| 所属分组 / Profile | `page` / `full` |
| 安全级别 | Destructive |
| MCP 注解 | `readOnlyHint` = false　`destructiveHint` = true　`idempotentHint` = false　`openWorldHint` = false |
| 状态 | Proposed（M4 第 12 步；已实现，2026-10-20 实机验证，见 api-notes A20） |

## 1. 这个工具解决什么任务
"把 `/90` 到 `/92` 这几页改成从 200 开始、步长 10 的页号"。代理给出 1–50 页，让 EPLAN 按"重新编号页"的规则给它们新的页号，按结构（页名中 `/` 之前的部分）编号（XML）。新页名由 EPLAN 的规则决定（M4 设计 Q7），变更清单只列范围，执行后列出每一页的新旧页名。页名被交叉引用、报表等处使用，按 Destructive 处理：每次审批，执行前备份，按一次 Ctrl+Z 撤销。只改一页的页描述用 `eplan_page_update`。重编号页不常用，所以放在 `full` Profile。

## 2. 输入参数
| 参数 | 类型 | 必填 | 默认值 | 说明（写给模型看的） |
|---|---|---|---|---|
| `project` | string | 是 | — | 项目名，或 `eplan_project_list` 返回的 `path`。写工具必须显式给出 |
| `pages` | array of string，1–50 项 | 是 | — | 要重新编号的页，完整页名如 `=GAA+A1&EFS1/90` |
| `start` | integer，≥ 1 | 是 | — | 第一个页号 |
| `step` | integer，≥ 1 | 否 | 1 | 页号的步长 |
| `keep_text` | boolean | 否 | false | 为 true 时保留页号中的字母部分 |
| `subpages` | enum `retain` / `consecutive` / `to_main` | 否 | `retain` | 子页（如 `/1.2`）：原样保留；从 1 起连续编号；转成主页 |
| `dry_run` | boolean | 否 | false | 为 true 时只返回将要重新编号的页，不请求审批，不备份，也不修改项目 |

- 页名只做精确匹配，同 `eplan_page_delete`。同一页出现两次时返回 `INVALID_ARGUMENT`。
- 固定按结构编号，不保留原有的页号间隔（`Renumber.Pages` 的第 2、5 个参数为 true、false，与 S12 相同）。

## 3. 输出
```json
{
  "project": "Line1",
  "execution": "committed",
  "dryRun": false,
  "scope": { "start": 200, "step": 10, "keepText": false, "subpages": "retain" },
  "changed": [
    { "from": "=GAA+A1&EFS1/90", "to": "=GAA+A1&EFS1/200" },
    { "from": "=GAA+A1&EFS1/91", "to": "=GAA+A1&EFS1/210" }
  ],
  "unchanged": 0,
  "backup": "C:\\Users\\<user>\\EplanMcp\\backups\\Line1\\20261021T083000123Z\\Line1.elk",
  "undo": "AI: eplan_page_renumber",
  "recovered": false,
  "hint": "Reports such as the table of contents may need eplan_report_update."
}
```
- `changed`：页名改变的页，按 `pages` 的顺序；`unchanged`：页名没有变的页数。
- `dry_run`：`changed` 为 `null`，变更清单中列出各页的现名。
- `hint` 提示用 `eplan_report_update` 更新报表（重编号后报表是否自动更新，没有验证）。
- `backup`：同 `eplan_page_delete`。页名是项目数据，经过 ResultShaper。`undo`、`recovered`：同 `eplan_page_create`。
- 一行摘要：
  - `Renumbered 3 pages. A backup was made first (see 'backup'); Ctrl+Z in EPLAN undoes this step.`
  - `Dry run: would renumber 3 pages from 200 in steps of 10; the new names are known only after running. Nothing was changed.`

## 4. 错误
| 错误码 | 什么时候发生 | `execution` | 返回给模型的提示 |
|---|---|---|---|
| `INVALID_ARGUMENT` | `pages` 为空、超过 50 项或有重复；`start`、`step` 小于 1 | `not_started` | `metadata.violations` 列出全部问题 |
| `ALREADY_EXISTS` | 执行后读回发现两页同名（不区分大小写），或者 EPLAN 报告重名（`NameConflictException`，或实测中带编号 S029167"页名不明确"的 `BaseException`，A20） | `rolled_back` | `metadata.existing` 列出冲突的页名，提示换一个 `start`，可以用 `eplan_page_list` 的 `name_prefix` 查看已用的页号 |
| `PAGE_NOT_FOUND` | 有页名找不到 | `not_started` | 同 `eplan_page_delete` |
| `PLAN_STALE` | 规划之后，这些页，或它们所在结构中的页名发生了变化 | `not_started` | 提示重新调用 |
| `BACKUP_FAILED` | 同 `eplan_page_delete` | `not_started` | 同 `eplan_page_delete` |
| `EPLAN_OPERATION_FAILED` | EPLAN 重编号时报错 | `rolled_back` | 附带 EPLAN 的系统消息 |
| 策略、审批、流水线、锁定、项目解析、连接与授权类错误 | 同 `eplan_page_delete` | 同 `eplan_page_delete` | 同 `eplan_page_delete` |

## 5. 实现要点
- **Host**：走 `WritePipeline`，级别 Destructive，规划操作是 `page.renumber.plan`，执行操作是 `page.renumber.execute`。执行时限为 `min(300, 60 + 2 × 页数)` 秒，包括备份。
- **规划**（只读）：
  - 精确解析各页；
  - 每页一条 `update` 变更：`Kind = "page"`，`Property = "page number"`，`NewValue` 为 `null`。表单显示为 `renumber page =GAA+A1&EFS1/90`，变更清单之后注明 `from 200, step 10, per structure, subpages: retain, keep text: no`；
  - 指纹包括两部分，排序后取 SHA-256：各页的会话内标识和页名；这些页所在结构中全部页的页名（新页号是否冲突取决于它们）。
- **执行**：Destructive 流水线先重算指纹、做好备份，然后在 `TransactionScope` 中：
  1. 记下各页的页名；
  2. `new Renumber().Pages(pages, true, start, step, false, keep_text, subpages)`，`subpages` 对应 `Renumber.Enums.SubPages` 的 `Retain`、`ConsecutiveNumbering`、`ConvertIntoMainPages`（XML）；
  3. 读回各页的新页名，再读项目的全部页名，有不区分大小写的重名就抛出，整体回滚（`ALREADY_EXISTS`）；
  4. `Commit()`，再 `CloseUndo()`。
- **已验证**（S12，A18）：在调度器中 `Renumber.Pages(3 页, true, 200, 10, false, false, Retain)` 不到 0.1 秒，`/90`、`/91`、`/92` → `/200`、`/210`、`/220`；能被 `SafetyPoint` 回滚，提交后 Ctrl+Z 一步撤销；不弹对话框。
- **没有验证**，由 Large 测试确认，不通过的取值从 `enum` 中去掉或改为固定值：
  - `keep_text = true`；`subpages` 为 `consecutive`、`to_main`；
  - 新页号与结构中其他页冲突时 EPLAN 的行为（抛出还是生成重名）；
  - 页分属不同结构时，是否各自从 `start` 开始；EPLAN 按 `pages` 的顺序还是项目中的顺序编号；
  - 重编号后目录等报表、交叉引用的显示是否自动更新。

## 6. 写操作专项
- **Destructive 流水线、审计、回滚、各阶段失败时的 `execution`**：同 `eplan_page_delete` 第 6 节。
- **dry-run**：返回第 3 节的结果，`execution = not_started`，不备份。
- **审批**：每次都请求审批，scratch 中也一样。对话框显示每页一行的变更清单和编号参数，注明执行前会先备份，以及可以按 Ctrl+Z 撤销"AI: eplan_page_renumber"。
- 不是幂等的：再调用一次时页名已经改变，原页名返回 `PAGE_NOT_FOUND`。执行请求从不重发（ADR-0010）。

## 7. 测试
- **Small**：参数校验；读回后的重名检查（不区分大小写）；变更清单的文字；指纹；摘要；schema 快照；注解。
- **Medium**：在假 Bridge 上测试：`dry_run` 不审批、不备份；重名时 `rolled_back`；`PLAN_STALE`；备份失败时不执行。
- **Large**（示例项目的 scratch 副本，审批方式同 `eplan_page_delete`）：
  - 新建 `=GAA+A1&EFS1/90`–`/92`，从 200、步长 10 重编号：得到 `/200`、`/210`、`/220`（S12）；`backup` 能打开；
  - 改成与已有页冲突的页号（`start` 为 1）：`ALREADY_EXISTS`，项目不变；
  - `keep_text`、`subpages` 的各个取值；分属两个结构的页。
- **Agent 评测**："把 scratch 项目中 `=GAA+A1&EFS1/90` 到 `/92` 三页改成从 200 开始、步长 10 的页号"，审批方式同 `eplan_page_delete`；作答后用 `eplan_page_list` 核对。

# 工具规格：`eplan_page_update`

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_page_update` |
| 所属分组 / Profile | `page` / `core` |
| 安全级别 | Write |
| MCP 注解 | `readOnlyHint` = false　`destructiveHint` = false　`idempotentHint` = true　`openWorldHint` = false |
| 状态 | Implemented |

## 1. 这个工具解决什么任务
给出页名，修改页描述或切换到项目中已被页面使用的图框；可同时修改两项。一次调用只审批一次，在 EPLAN 中按一次 Ctrl+Z 撤销。图框通过页属性 11016 修改，不新增工具、不导入主数据。

## 2. 输入参数
| 参数 | 类型 | 必填 | 默认值 | 说明（写给模型看的） |
|---|---|---|---|---|
| `project` | string | 是 | — | 项目名，或 `eplan_project_list` 返回的 `path`。写工具必须显式给出 |
| `pages` | array，1–50 项 | 是 | — | 要修改的页 |
| `pages[].name` | string | 是 | — | 完整页名，如 `=GAA+A1&EFS1/3`（用 `eplan_page_list` 查） |
| `pages[].description` | string，最长 500 个字符 | 否 | 不修改 | `language` 下的新页描述；空串表示清除这种语言的描述 |
| `pages[].plot_frame` | string | 否 | 不修改 | 项目中已被页面使用的图框名称，Ordinal 精确匹配；不接受空白或导入文件路径 |
| `language` | string | 否 | EPLAN 界面语言 | 要修改的语言，如 `zh_CN`、`en_US`。**其他语言的描述保持不变** |
| `dry_run` | boolean | 否 | false | 为 true 时只返回将要进行的修改，不请求审批，也不修改项目 |

- 页名的查找规则与 `eplan_page_get` 相同：先精确匹配；找不到时，再做不区分大小写的比较，只有唯一一个匹配时才采用。
- 同一次调用中，同一页出现两次时返回 `INVALID_ARGUMENT`。
- 每项至少提供 `description`、`plot_frame` 之一；null 按省略处理。描述专用调用保留原来的匹配、语言、指纹、摘要和响应字段。

## 3. 输出
```json
{
  "project": "Line1",
  "execution": "committed",
  "dryRun": false,
  "language": "zh_CN",
  "pages": [
    { "name": "=GAA+A1&EFS1/3", "description": "主回路", "previous": "电源" }
  ],
  "unchanged": 1,
  "undo": "AI: eplan_page_update",
  "recovered": false,
  "hint": null
}
```
- `pages`：有变化的页，带新描述和原描述（`language` 下的文本，原来没有时为 `null`）。提交后是读回的值；`dry_run` 时是将要写入的值。
- `unchanged`：新描述与原描述相同、因而不修改的页数。
- 全部页都没有变化时，计划为空，`execution = not_started`，不请求审批。
- `undo`、`recovered`：同 `eplan_page_create`。
- 页描述按项目数据处理，经过 ResultShaper。
- `language`：省略参数时是 EPLAN 界面语言的代码；规划没有任何变化、又没有给出 `language` 时为 `null`。
- 一行摘要（只有计数，不回显项目名，架构 8.5）：
  - `Updated the descriptions of 3 pages; Ctrl+Z in EPLAN undoes this step.`
  - `Dry run: would update 3 pages. Nothing was changed.`
  - `Nothing to change: the 3 pages already have these descriptions.`
- 变更清单中的属性写作 `description (zh_CN)`，审批表单因此显示是哪种语言：`update page =GAA+A1&EFS1/3: description (zh_CN) '电源' -> '主回路'`。
- 图框实际改变的页面另返回 `plotFrame`、`previousPlotFrame`（实际图框名，原来没有时省略 previousPlotFrame）；未改变图框时省略这两个字段。只改图框时 `description`、`previous` 为 null；两种属性都改变时仍只返回一行页面，`unchanged` 按页计数。
- 包含图框请求时摘要为 `Updated 1 page; Ctrl+Z in EPLAN undoes this step.`，无变化为 `Nothing to change: requested values already match on 1 page.`；dry run 沿用原摘要。恢复提交结果时使用原计划，`recovered=true`，不自动重发。workflow 的 `page.update` 暂仍只接受描述。

## 4. 错误
| 错误码 | 什么时候发生 | `execution` | 返回给模型的提示 |
|---|---|---|---|
| `INVALID_ARGUMENT` | 参数不合法：同一页重复、描述过长、`language` 不认识等 | `not_started` | `metadata.violations` 一次列出全部问题，按 `pages[i]` 标明位置 |
| `INVALID_ARGUMENT` | 读回的描述与请求不同，或其他语言的描述被改动 | `rolled_back` | `metadata.page`、`metadata.requested`、`metadata.actual` |
| `PAGE_NOT_FOUND` | 有页名找不到 | `not_started` | `metadata.missing` 列出全部找不到的页名，提示用 `eplan_page_list` 的 `name_prefix` 查准确的页名 |
| `AMBIGUOUS_NAME` | 精确匹配找不到，而不区分大小写时有多个页匹配 | `not_started` | 同 `eplan_page_get` |
| `PLAN_STALE` | 规划之后，这些页的描述被改动过 | `not_started` | 提示重新调用 |
| `INVALID_ARGUMENT` | 图框不在可发现集合中 | `not_started` | `metadata.available` 最多 20 名，每名最多 200 字符；`availableCount` 是完整数量，提示选择已有名称重新规划 |
| `PLAN_STALE` | 图框请求的页面身份、11016、有效图框或可用名称集合变化，或目标图框/页面消失 | `not_started` | 重新生成计划；不建事务 |
| `EPLAN_OPERATION_FAILED` | 11016 或有效图框读回不等于目标，或写图框改变了描述 | `rolled_back` | 附页面，整批恢复，由工程师检查 |
| 策略、审批、流水线、锁定、项目解析、连接与授权类错误 | 同 `eplan_page_create` | 同 `eplan_page_create` | 同 `eplan_page_create` |

## 5. 实现要点
- **Host**：走 `WritePipeline`，规划操作是 `page.update.plan`，执行操作是 `page.update.execute`。执行时限为默认的 60 秒。
- **只改一种语言**：直接赋值一个只含一种语言的 `MultiLangString`，会替换整个属性值，其他语言随之丢失（api-notes A13）。所以执行时：
  1. 读出原值中存放的全部语言（原始串 `de_DE@…;zh_CN@…;`，解析方式同 `TextLanguages`），用 `GetString` 逐个取出文本；
  2. 新建一个 `MultiLangString`，原样加入其他语言，再加入 `language` 的新文本（空串则不加入）；
  3. 写入 `PAGE_NOMINATIOMN`。

  这样不依赖 `AddString` 对已有语言是"替换"还是"追加"，这一点没有验证过。原值是语言无关的文本（`??_??@…;`）时，它也作为一个条目原样保留；这种情况由 Large 测试确认 EPLAN 的显示效果。
- **规划**：
  - 按 `eplan_page_get` 的规则找到每一页，读出 `language` 下的原描述，相同的页计入 `unchanged`；
  - 每页一条 `update` 变更：`Property = "description"`，`OldValue` 和 `NewValue` 是 `language` 下的文本，表单显示为 `update page =GAA+A1&EFS1/3: description '电源' -> '主回路'`；
  - 指纹是这些页的页名和 `PAGE_NOMINATIOMN` 原始串，排序后取 SHA-256。
- **执行**：
  - 先重算指纹，不同就返回 `PLAN_STALE`；
  - 在 `TransactionScope` 中逐页写入，读回原始串，确认 `language` 的文本等于请求、其他语言的文本与写入前相同，不一致就抛出，整体回滚；
  - 全部通过后 `Commit()`，再 `CloseUndo()`。
- **包装**：同 `eplan_page_create`。S2 验证过：修改已有页的描述后回滚，原值能恢复（A7）。
- **幂等**：同样的调用再来一次时，计划为空，不修改项目。

### 图框扩展（v0.4.0）
- `IPagePlotFrames` 只在请求图框时参与规划；适配器逐页读取 `Page.PlotFrame.Name`，去重排序；读取失败整体拒绝，不把它当成无图框。没有使用任何未验证的主数据枚举或导入 API，因此不能保证发现项目内尚未被任何页使用的图框。
- 指纹在原描述指纹外加入项目路径、图框请求页的会话身份、原始 11016（空为 null）、有效图框名、可用名称集合；用 JSON 编码后取 SHA-256，未请求图框时保留旧算法。枚举、读取和逐页写入都有取消检查点。
- 含图框请求时创建请求级会话，描述和 11016 共用每页唯一的属性列表，结束时统一释放，避免 A36 的释放后再次取得 Properties。
- 依据 S38 `Probes.cs`，通过 `Page.Properties[11016] = PropertyValue.Set(name)` 写入，分别读回原始 11016 和 `Page.PlotFrame.Name`，必须都精确等于目标；空 11016 使用项目设置（A49），不能用它判断无变化，应比较有效图框。相同有效图框不写 11016，也不固定原来的继承设置。
- 描述和图框共用原来的 TransactionRunner / SafetyPoint / UndoStep，名称仍为 `AI: eplan_page_update`；只写实际变化的属性，任何页面异常、取消或读回不符都整体回滚。完全无变化不建事务、不审批。
- 独立只读 Bridge 操作 `page.plotframe.inspect` 返回页面身份、11016 与有效名称，供 Large 核对，不增加 MCP 工具。Host 输入、审批和审计沿用 WritePipeline，`plot_frame` 变更一行展示原实际图框 → 新图框；审计保留完整参数与变更行。英文用法同步维护。
- **仍未实机验证**：逐页发现的覆盖范围与未使用图框；此次适配器的 11016 赋值及双重读回；含继承图框的提交、SafetyPoint 整批回滚、一次当前项目撤销恢复原 11016（含空值）；其他页不变。A49/S38 的探针结果不能替代本实现的 Large 验证，不宣称文件逐字节恢复，不改 api-notes。

## 6. 写操作专项
- **dry-run**：返回第 3 节的结果，`execution = not_started`。
- **审批**：项目在 scratch 之外时请求审批，表单每页一行，显示原描述和新描述。
- **审计、回滚、各阶段失败时的 `execution`**：同 `eplan_page_create`。

## 7. 测试
- **Small**：
  - 原始串的拼装：替换一种语言、新增一种语言、清除一种语言、保留 `??_??`、其他语言的顺序不变；
  - 同一页重复；摘要；schema 快照；注解。
- **Medium**：在假 Bridge 上测试：
  - `dry_run`；全部没有变化时计划为空；`PAGE_NOT_FOUND` 列出全部缺失；`PLAN_STALE`；
  - 读回不一致时返回 `rolled_back`。
- **Large**（示例项目的 scratch 副本）：
  - 选一页同时有中文和其他语言描述的页，改它的中文描述，其他语言的描述保持不变（用 `eplan_page_get` 的 `language` 分别读回）；
  - 清除中文描述，其他语言仍在；
  - 再调用一次相同的修改，返回"没有变化"。
- **Agent 评测**："把 scratch 项目中 `=GAA+A1&EFS1/1` 的中文页描述改成'主电源'，英文描述不要动"，作答后用 `eplan_page_get` 核对两种语言。
- **图框 Small / Medium**：混合请求、缺少字段、1/50/51 页、重复页、未知图框及有界名称、继承图框不写、身份/11016/实际图框/集合过期、两项同改只计一页、后页失败/取消整批回滚；真实 MCP + Fake Bridge 验证 dry run、不变时不审批、scratch 自动允许、外部拒绝/审批、恢复结果、原描述响应不增加空图框字段。
- **图框 Large**：WriteHost 只写 LargeWrite，独立 inspect 全部页，选一页和不同的已用图框；dry run 不改变状态、不写审计；切换后分别核对 11016 与有效图框，其他页完整状态不变；按 A41/A46 使用 PlanStepLiveSession 选择当前项目，一次 undo 恢复含空 11016 的原状态。finally 仅在提交和撤销结果已知时、且目标页仍是已确认的测试改动时恢复原框；结果未知不自动补偿。

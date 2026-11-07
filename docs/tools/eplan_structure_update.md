# 工具规格：`eplan_structure_update`

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_structure_update` |
| 所属分组 / Profile | `structure` / `full` |
| 安全级别 | Write |
| MCP 注解 | `readOnlyHint` = false　`destructiveHint` = false　`idempotentHint` = true　`openWorldHint` = false |
| 状态 | Implemented (Unreleased) |

## 1. 这个工具解决什么任务
一次调用写好一批结构标识符：已有的改描述，没有的新建。代理不操作对话框，也不用截图确认，结果在返回值里。

## 2. 输入参数
| 参数 | 类型 | 必填 | 默认值 | 说明（写给模型看的） |
|---|---|---|---|---|
| `project` | string | 是 | — | 项目名，或 `eplan_project_list` 返回的 `path` |
| `identifiers` | array，1–200 项 | 否（和 `order` 至少给一个） | — | 每项 `{name, description?}`，同一标识只能出现一次 |
| `order` | array，1–200 项 | 否 | — | 按块排在最前面的完整名称；块内其他标识排在后面，它们之间的顺序由 EPLAN 决定（实测可能重排），位置重要的都要列出。本次新建的标识也可以写 |
| `translate` | boolean | 否 | false | 按翻译库补其他语言 |
| `dry_run` | boolean | 否 | true | 省略或 true 只预览；执行须显式 false |

- `name`：带前缀的完整名称，如 `+S1`、`&EFS`、`==A.B`。前缀决定所在的块。
- `description`：最长 500 字符，不含控制字符，可写纯文本或 `zh_CN@文字;en_US@text;`。空串表示清除，省略表示保持原样。

默认只写传入的语言。传 `translate: true` 时，只有一种语言的描述按翻译库（`Translate.TranslateText`）补其他语言，和对话框里输入中文的效果一样；词典里没有的词保持原样。

## 3. 输出
`{project, execution, dryRun, fingerprint, changes, undo, backup, recovered, hint}`。`backup` 恒为 null。`changes` 只列会变的标识：`create` 或 `update`。都相同时不写入，`execution` 为 `not_started`。

## 4. 错误
| reason | 情形 | execution |
|---|---|---|
| INVALID_ARGUMENT | 名称格式不对、重复，或描述超长、含控制字符 | not_started |
| PLAN_STALE | 预览之后标识变了，重新预览 | not_started |
| EPLAN_OPERATION_FAILED | EPLAN 拒绝，或读回不一致；`metadata.identifier` 指出是哪个 | rolled_back |

## 5. 实现要点
走 `WritePipeline`：scratch 内自动写入，scratch 外由工程师审批。Bridge 在一个事务内用 `Project.CreateLocation(块, 名称, LocationSortOrder.Back)` 新建，用 `Location.Properties.LOCATION_DESCRIPTION` 写描述，再逐项读回。整个调用一次撤销。

排序用 `Project.SetSortedLocations`，读回校验传入的标识排在最前面、顺序一致。改名会影响所有用到它的对象，单独放在 `eplan_structure_rename`。

## 6. 测试
- Small：`StructureOperationTests` 覆盖计划、读回回滚、过期指纹、名称格式。
- Large：`StructureToolsTests` 在 LargeWrite 上批量新建和写描述，读回，一次撤销恢复。

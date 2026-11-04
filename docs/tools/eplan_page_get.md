# 工具规格：`eplan_page_get`

绘图读取遇到空块等对象的不可读名称或元数据时，保留对象；不可读字段按原有格式省略（未知），并在 `properties.read_warning` 附告警。不会把缺失数据视为空对象或终止整页读取。第四轮 PXC.2861289 插入后的读回 Large 待实测。

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_page_get` |
| 所属分组 / Profile | `page` / `core` |
| 安全级别 | Read |
| MCP 注解 | `readOnlyHint` = true　`destructiveHint` = false　`idempotentHint` = true　`openWorldHint` = false |
| 状态 | Implemented (v0.1.0) |

## 1. 这个工具解决什么任务
"=GAA+A1&EFS1/1 这页上有哪些设备？""这页是什么类型、页描述是什么？"代理知道页名后，用它看一页的概况和页上的设备；需要设备的细节时，再用 `eplan_device_get`。

## 2. 输入参数
| 参数 | 类型 | 必填 | 默认值 | 说明（写给模型看的） |
|---|---|---|---|---|
| `project` | string | 否 | EPLAN 中选中的项目 | 项目名，或 `eplan_project_list` 返回的 `path` |
| `page` | string | 是 | — | 完整页名，如 `=GAA+A1&EFS1/1`（用 `eplan_page_list` 查） |
| `response_format` | enum `concise` / `detailed` | 否 | `concise` | `detailed` 额外返回全部非空的页属性 |
| `language` | string | 否 | EPLAN 界面语言，没有文本时回退到其他语言 | 多语言文本的语言 |
| `include_drawings` | bool | 否 | false | 读取栅格、边界及受支持绘图对象；省略或 false 时响应完全保持原样 |
| `drawing_limit` | int | 否 | 50 | 每次最多 1–200 个对象，仍受响应预算限制 |
| `drawing_cursor` | string | 否 | — | 上次 `drawings.nextCursor`，仅可配合 include_drawings=true；其余参数保持相同，可改 drawing_limit |

## 3. 输出
```json
{
  "project": "Line1",
  "page": { "name": "=GAA+A1&EFS1/1", "type": "Circuit", "typeName": "（EPLAN 界面语言的页类型名）", "description": "电源" },
  "devices": [ { "tag": "=GAA+A1-FC1", "visibleTag": "-FC1", "category": "CircuitBreaker" } ],
  "deviceCount": 4,
  "properties": null,
  "truncated": false,
  "hint": null
}
```
- `devices`：页上的设备，即带标识的主功能（`IsMainFunction` 且 `Name` 非空，与 `eplan_project_get` 的设备计数同一规则），按 EPLAN 的顺序；`tag` 是 `Function.Name`（完整标识），`visibleTag` 是页面上显示的标识（去掉 `>` 标记，去掉后为空时为 `null`），`category` 是 `FunctionCategory` 的枚举名（api-notes A11）。同一个完整标识只列一次：按页过滤时 EPLAN 还会返回黑盒里同名的功能，保留有显示标识的那一个（与 `eplan_device_list` 相同，api-notes A12）。最多 100 个，超出时 `truncated = true`，`hint` 提示用 `eplan_device_list` 的 `page` 过滤。`deviceCount` 是页上设备的总数。
- `typeName`：EPLAN 界面语言的页类型名称（S6：如"标题页/封页"）。
- 省略 `language` 时按[语言回退设计](../design/2026-10-19-text-language-fallback.md)回退：页描述旁边有 `descriptionLanguage`，属性的值旁边有 `language`；没有回退时不出现这些字段。
- `properties`（仅 `detailed`）：同 `eplan_project_get`（含 `index`）。
- 报表页额外返回顶层 `report`：`type` 为 DocumentType 名称，`form` 为表格名，`filter_scheme` / `sort_scheme` / `function_filter_scheme` / `function_sort_scheme` 为方案名称，对应的 `filter_scheme_data` / `sort_scheme_data` / `function_filter_scheme_data` / `function_sort_scheme_data` 为原始方案数据字符串，每项最多 4000 字符，超出截断并给出 warning。数据可原样传给 `eplan_report_create`，避免目标项目缺少同名方案时生成失败。`device_tag` 为只读文本，`automatic_page_description` 为自动描述开关，`manual_page_description` 为原始多语言描述文本。非报表页省略 `report`；字段不可读时为 null 并记录 warning，不影响其他字段。
- `drawings` 仅在 `include_drawings=true` 时出现：`gridSize`、`pageSize`、独立的 `plotFrameSize`（毫米）与 `boundarySource=Page.Size/unknown`；页尺寸不可读时为 null，图框尺寸不替代页界。绝对坐标原点在左下角，y 向上（A3/A14/A54）。
- `drawings.items` 按 T0a 发现顺序列受支持符号、属性文字、自由文字、直线、矩形，排除图框后代和部件引用；每项含 `kind/location/bounds/locator/parent/properties`。外框未知为 null；属性文字位置已换算为绝对坐标，parent 指向父符号。只读 properties 含可读的 name、原始存储多语言 text 和 display；不可用的键省略，文字是不可信工程数据。locator 沿 T0a 项目/页/类型/对象号/内容摘要，不授予移动权限。
- 属性文字的 locator 使用父对象的 type/oid，摘要包含属性 ID、集合序号、内容与几何；去重也以父对象及序号为范围（A57）。同库默认文字在多个父对象上都保留，复制为本地文字不会仅因原生包装类型或对象号变化而变成另一项；移动仍要求独立的 parent/property_id/property_index。
- `drawings.total` 是全部受支持对象数，`truncated` 表示尚有对象，`nextCursor` 指向首个未返回对象，预算裁剪也不跳项。签名游标十分钟过期，绑定项目路径、页、完整绘图证据、语言、格式和 Bridge 实例；变化、伪造或过期返回 INVALID_ARGUMENT，省略游标重新读取。页面读取不提供跨调用原子快照。
- 大小：`concise` 默认 5k token，`detailed` 上限 20k token；超出时先从末尾去掉属性，仍然超出（页上设备的标识异常长）再去掉设备，`truncated` 为 true，`hint` 说明。
- 一行摘要：`Page found: Circuit, 4 devices.`，不回显页名和页描述；有内容被去掉时追加 `Some entries were left out; see 'hint'.`

## 4. 错误
| 错误码 | 什么时候发生 | 返回给模型的提示 |
|---|---|---|
| `INVALID_ARGUMENT` | 缺少 `page`，或其他参数不合法 | `metadata.violations` 列出全部问题 |
| `PAGE_NOT_FOUND` | 没有这个页名（不区分大小写也找不到） | "Use eplan_page_list with name_prefix to find the exact page name."，`metadata.page` 为传入的值，`metadata.project` 为项目名 |
| `AMBIGUOUS_NAME` | 精确匹配找不到，而不区分大小写时有多个页匹配（页名只差大小写） | `metadata.candidates` 列出这些页的完整页名，提示原样传入其中一个 |
| `PROJECT_NOT_OPEN` / `AMBIGUOUS_NAME` | 项目解析失败 | 同 `eplan_project_get` |
| 连接与授权类错误 | 同 `eplan_project_list` | 同 `eplan_project_list` |

## 5. 实现要点
- 查页：`PagesFilter { Name = page, ExactNameMatching = true }`。找不到时，再在内存中做一次不区分大小写的精确比较，只有唯一匹配时才采用（页名过滤区分大小写，S6）；多个匹配时返回 `AMBIGUOUS_NAME`。比较逻辑在 Bridge.Core（`PageGetOperation`），适配层只提供"按精确页名读一页"和"全部页名"。
- 缺少 `page`：Host 的参数过滤器按 schema 的 `required` 在调用前返回 `INVALID_ARGUMENT`（SDK 自己只返回没有 reason 的通用错误，api-notes C）；显式传 `null` 或空串时由工具本身报告。
- 页上的设备：`FunctionsFilter { Page = page }` 后取主功能（S6：0 ms）。不使用 `Page.Filter`（已列入 BannedSymbols）。
- 多语言文本用 `GetStringToDisplay`；所有项目文本经过 ResultShaper。
- 报表块依据 [A84](../eplan/api-notes/drawing-and-layout.md#a84-报表块读取与按报表块生成报表页2026-11-03eplan-20260325421electric-p8示例项目夹具只读生成只在-scratch-副本-largewrite)：在事务外从本次查询的新 Page 包装器读取 `Page.ReportBlock`，及时释放报表块与手工描述对象；字段失败独立记录警告，读取期间保持所有者存活（A80）。
- 可选发现调用只读类型化 invoke `page.drawing.get`，在既有主线程/LockingStep 中使用 T0a 适配器，不导航、不创建事务；绘图预算包含结构化和文本两份 JSON，先裁属性和设备，再裁绘图；单条无法容纳时明确报错，不返回无法前进的游标。

## 6. 写操作专项
不适用。

## 7. 测试
- Small：参数校验、摘要、设备列表截断、旧响应逐字兼容、绘图两档预算与连续分页。
- Small/Medium：报表名称和方案数据完整传递、4000 字符边界与超长截断警告、非报表字段省略、单字段读失败仍返回其他字段、契约快照；Large `ReportReadTests` 只读示例页 `&AAB1/1` 的设置，`ReportCreateTests` 从 `+A1&EMA2/2` 读取方案数据后生成端子图（本次不运行 EPLAN）。
- Medium：契约、大小写回退、内容/页/项目/Bridge 改变、过期及另一 Host 游标拒绝；管道读取与握手能力登记。
- Large：保留 S6 主功能断言；新增示例夹具页绘图全量分页与独立适配器读回对照，并比较读取前后业务快照和工程文件哈希（本 PR 未运行）。
- Agent 评测："=GAA+A1&EFS1/1 这页上有哪些设备？"

- D3 显示读回：属性放置的 `properties.visible` 返回原生 `IsVisible` 的 `"true"` / `"false"`，配合 `display`、父定位器与绝对位置核对文字；不能只凭非空字符串断言文字可见。

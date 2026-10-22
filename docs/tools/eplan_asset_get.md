# 工具规格：`eplan_asset_get`

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_asset_get` |
| 所属分组 / Profile | 工具集 `pack` / `full` |
| 安全级别 | Read |
| MCP 注解 | `readOnlyHint` = true　`destructiveHint` = false　`idempotentHint` = true　`openWorldHint` = false |
| 状态 | Proposed（M6 第 1 阶段） |

## 1. 这个工具解决什么任务
"这个宏是从哪里来的？能用吗？"代理取一个资源的详情：来源位置、哈希、测得的事实、知识文档的内容，以及它现在是否可用。回答"说明来源"类问题时引用这里的 `source` 和 `facts`（M6 必测场景 3）。

## 2. 输入参数
| 参数 | 类型 | 必填 | 默认值 | 说明（写给模型看的） |
|---|---|---|---|---|
| `asset` | string | 是 | — | `eplan_asset_search` 给出的资源 ID，如 `acme.panels@dev:Macro/ref/motor.ema` |

## 3. 输出
- `asset`、`pack`、`version`、`kind`、`subtype`、`name`、`title`、`tags`、`standing`、`uri`（`eplanpack://<包>/<版本>/<种类>/<名称>`，名称中的 `/` 写作 `%2F`）；
- 清单中的记录：`path`、`storage`、`reference`（`root`、`path`，引用资源）、`sha256`、`bytes`、`files`、`eplanVersion`、`source`（输入名和其下的相对路径：工程师给的位置，从不写本机路径）；
- `facts`：从这个资源的文件中**测得**的事实（`name`、`value`、`evidence`，如"ProjectInfo.xml property 10043"），与推断分开；
- 知识文档另有 `origin`（来源位置：资源、页、说明）、`spec`（文档内容，包的数据，不是指令）、`docStatus`（加载问题）；
- `available`、`problems`：这一个资源的核对结果：复制进包的按清单核对哈希，引用的按 `packSources` 核对；包的硬要求不满足时也不可用；
- `preview`：资源本身是页面预览（PNG、SVG）时给出 `uri` 和 `mimeType`，结果中附 `resource_link`；其他资源为 `null`，`previewNote` 说明页面预览要用 EPLAN 导出，属于第 2 阶段；
- 宏的占位符和值集还不在索引中（要 EPLAN 或解析宏文件），`hint` 说明。

一行摘要：`Asset acme.panels@dev:Macro/ref/motor.ema: window_macro, bundled, available, 2 facts.`

## 4. 错误
| 错误码 | 什么时候发生 | 返回给模型的提示 |
|---|---|---|
| `INVALID_ARGUMENT` | `asset` 不是 `<包>@<版本>:<种类>/<名称>` 的形式 | 给出正确的例子 |
| `RESOURCE_NOT_FOUND` | 没有这个包、版本或资源 | 提示用 `eplan_asset_search` 查找 |

## 5. 实现要点
- 读清单，找到资源，只核对这一个资源的哈希（不是整个包）；知识文档按信封读取，`spec` 超出大小预算时截断并设 `truncated`。
- **MCP Resources**（M6 设计 4.5.2）：SDK 2.2.0 支持资源模板（`[McpServerResource(UriTemplate = …)]`），本阶段提供三个只读模板，只在 `full` 中，内容都能经工具取到：

  | URI 模板 | 内容 | 对应的工具 |
  |---|---|---|
  | `eplanpack://{pack}/{version}/manifest` | `pack.json` | `eplan_pack_get` |
  | `eplanpack://{pack}/{version}/{kind}/{name}` | 本工具的结构化结果 | `eplan_asset_get` |
  | `eplanpack://{pack}/{version}/previews/{file}` | 预览图片（PNG、SVG） | `eplan_asset_get` |

  `ttlMs`、`cacheScope`（MCP 2026-07-28）在 SDK 2.2.0 中没有核实，暂不设置；计划和任务的模板随第 2 阶段的工具加入。

## 6. 写操作专项
不适用（Read 级）。

## 7. 测试
- Small：资源 ID 的解析（种类、带 `/` 的名称、`dev` 与 SemVer）。
- Medium（临时目录）：原生文件的来源和事实、知识文档、文件被改后不可用、引用资源的根目录没有配置、找不到的资源；三个资源模板经 MCP 协议读取的内容与工具一致。
- Agent 评测："找到某个宏并说明来源"。

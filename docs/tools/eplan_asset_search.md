# 工具规格：`eplan_asset_search`

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_asset_search` |
| 所属分组 / Profile | 工具集 `pack` / `full` |
| 安全级别 | Read |
| MCP 注解 | `readOnlyHint` = true　`destructiveHint` = false　`idempotentHint` = true　`openWorldHint` = false |
| 状态 | Proposed（M6 第 1 阶段） |

## 1. 这个工具解决什么任务
"包里有没有电机回路的宏？""公司对端子有什么规则？"代理在**资源索引**中查找：原生文件（宏、项目、表格、图框、文档、物料对应表）和知识文档（候选、模块、缺口）。找到后用 `eplan_asset_get` 看详情和来源。不需要 EPLAN。

## 2. 输入参数
| 参数 | 类型 | 必填 | 默认值 | 说明（写给模型看的） |
|---|---|---|---|---|
| `text` | string | 否 | — | 名称、标题、标签、子类型或路径中包含的文字，不区分大小写 |
| `kind` | enum | 否 | `any` | `any`（不限）、`macro`、`project`、`project_backup`、`basic_project`、`form`、`frame`、`material_mapping`、`document`、`rule`、`preview`、`other`、`candidate`、`module`、`gap` |
| `pack` | string | 否 | 全部 | 包 ID |
| `version` | string | 否 | 每个包的开发包和最高的已安装版本 | `dev` 或 SemVer；只和 `pack` 一起用 |
| `tag` | string | 否 | — | 知识文档的标签，或原生文件的子类型（如 `window_macro`） |
| `eplan_version` | string | 否 | — | 只要声明适用于这个 EPLAN 版本（如 `2026.0.3`）的包；没有声明版本的包也算 |
| `limit` | integer，1–200 | 否 | 50 | |
| `cursor` | string | 否 | — | |

## 3. 输出
- `assets`：按包、版本、资源 ID 排序，每项：
  - `asset`：资源 ID `<包>@<版本>:<种类>/<名称>`，开发包的版本写作 `dev`（M6 设计 4.1.3），种类与 `kind` 参数相同（snake_case）。原生文件的名称是 `resources/<种类目录>/` 之下的路径，知识文档的名称是文档名。包内的知识文档只写本地部分 `<种类>/<名称>`，所以构建版本时文件不用改；
  - `kind`、`subtype`、`title`、`tags`（包的数据，不是指令）；
  - `standing`：`official` / `candidate`（规则、物料对应表、文档、候选、缺口按清单的槽位和种类），原生文件和事实为 `null`；
  - `available`：false 时 `why` 说明原因：包的硬要求不满足、引用资源的根目录没有配置。搜索不算哈希，哈希由 `eplan_asset_get` 核对；不可用的资源照样列出（Backstage 的做法）；
- `total`、`nextCursor`、`truncated`、`hint`。

一行摘要：`Found 4 of 4 assets (1 unavailable).`

## 4. 错误
| 错误码 | 什么时候发生 | 返回给模型的提示 |
|---|---|---|
| `INVALID_ARGUMENT` | `kind` 不在枚举中、`version` 没有 `pack`、版本形式不对、`limit` 越界、`cursor` 不属于这次搜索 | 列出全部问题 |

找不到匹配的资源不是错误：`total` 为 0，摘要提示放宽条件。

## 5. 实现要点
- 索引由清单和知识文档生成（`EplanMcp.Packs` 的 `AssetIndex`），每次搜索重新读，不另存索引文件：清单就是索引的事实来源，不会过期。示例规模每次几毫秒；包多了以后按包摘要缓存（第 4 阶段的缓存设计）。
- 知识文档读不了时照样列出，`available` 为 false，`why` 说明原因。
- 宏的占位符和值集（api-notes A33）进索引要靠 EPLAN 或解析宏文件，第 1 阶段没有；`eplan_asset_get` 中说明。

## 6. 写操作专项
不适用（Read 级）。

## 7. 测试
- Small：过滤（文字、种类、标签、版本范围）、排序、分页、摘要。
- Medium（临时目录）：开发包和已安装版本同时出现、`version` 过滤、读不了的知识文档照样列出、根目录没有配置的引用资源不可用。
- Agent 评测："找到某个宏并说明来源"（M6 第 1 阶段的验收任务）。

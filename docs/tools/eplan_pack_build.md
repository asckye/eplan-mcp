# 工具规格：`eplan_pack_build`

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_pack_build` |
| 所属分组 / Profile | 工具集 `pack` / `full` |
| 安全级别 | 本地数据（M6 设计 4.5.1） |
| MCP 注解 | `readOnlyHint` = false　`destructiveHint` = false　`idempotentHint` = true　`openWorldHint` = false |
| 状态 | Proposed（M6 第 1 阶段；导出 `.eplanpack` 属于第 3 阶段，不在本工具中） |

## 1. 这个工具解决什么任务
"资料整理好了，发布成 1.0.0"。代理把开发包冻结为一个**不可变的版本**，安装到 `<数据根目录>\packs\<包 ID>\<版本>\`，以后项目可以锁定它（M6 设计 4.1.5）。同一版本重复构建、内容相同时什么都不做（所以 `idempotentHint` 为 true）。

## 2. 输入参数
| 参数 | 类型 | 必填 | 默认值 | 说明（写给模型看的） |
|---|---|---|---|---|
| `pack` | string | 是 | — | 开发包的 ID |
| `version` | string | 是 | — | SemVer 2.0.0，如 `1.0.0`。主版本：模块接口、规则含义或配置项被删改；次版本：新增资源；修订号：不改接口的修正 |

## 3. 输出
- `pack`、`version`、`digest`（版本的包摘要，项目锁定时记它）、`outcome`（`installed` / `already_installed`）、`resources`、`uri`（`eplanpack://<包>/<版本>/manifest`）；
- `location`：`packs/<包 ID>/<版本>`，相对于数据根目录，不写本机路径。

一行摘要：`Built 'acme.panels' 1.0.0 (12 resources); projects can now lock it.`

## 4. 错误
| 错误码 | 什么时候发生 | 返回给模型的提示 |
|---|---|---|
| `INVALID_ARGUMENT` | `pack` 不是合法的 ID，`version` 不是 SemVer | 列出全部问题 |
| `RESOURCE_NOT_FOUND` | 没有这个开发包 | 提示先用 `eplan_pack_import` |
| `PACK_INVALID` | 开发包校验不通过：文件与清单不符、引用资源不可用、没有来源的正式规则（M6 设计 4.2）、依赖的包没有安装合适的版本 | `metadata.problems` 列出问题；什么都没有安装 |
| `ALREADY_EXISTS` | 这个版本已经安装，内容不同 | 版本从不覆盖，提示换一个版本号 |
| `AUDIT_UNAVAILABLE` | 审计写不进去 | 什么都没有安装 |

## 5. 实现要点
- 在 `packs-dev\` 下的临时目录中复制开发包，清单的 `version` 改为给出的版本、`provenance.createdAt` 改为现在，按已安装的包解析依赖并写 `pack.lock.json`（`PackStore.Lock`），完整校验后交给 `PackStore.InstallAsync`：它再校验一次，文件设为只读；同一版本已安装且摘要相同时为 `already_installed`，摘要不同时拒绝。
- 引用资源（`referenced`）不复制，但构建前按 `packSources` 核对哈希：冻结一个已经不一致的哈希没有意义。
- 开发包本身不变，可以继续整理，下次构建新的版本。
- 在开发包锁内核验冻结副本：mandatory 缺来源、official 缺少匹配本机 `knowledge_review` 摘要均为 `PACK_INVALID`；手改 standing 或导入历史确认不能代替 `eplan_asset_review` 的工程师确认。
- 不改变任何项目锁（M6 设计 4.1.5：安装新版本不改变锁）。

## 6. 写操作专项
- 不改 EPLAN 项目，没有 `dry_run`、审批和 `execution`。
- **审计**：`pack_change`，`action: build`，构建前 `phase: started`，之后 `committed`（版本、摘要、结果）或 `failed`。

## 7. 测试
- Small：参数校验、摘要。
- Medium（临时目录）：构建后版本只读且摘要与清单相同、重复构建为 `already_installed`、同版本不同内容被拒绝、开发包被改坏时 `PACK_INVALID` 且没有安装、引用资源哈希不符时拒绝、复制的项目带 `User.eod`/`User.eox`、开发包不变。
- Agent 评测：与导入合在"整理成包并发布 1.0.0"中。

# 工具规格：`eplan_pack_list`

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_pack_list` |
| 所属分组 / Profile | 工具集 `pack` / `full` |
| 安全级别 | Read |
| MCP 注解 | `readOnlyHint` = true　`destructiveHint` = false　`idempotentHint` = true　`openWorldHint` = false |
| 状态 | Proposed（M6 第 1 阶段） |

## 1. 这个工具解决什么任务
"有哪些工程包？哪个版本被项目锁定了？"代理列出开发中的包（`packs-dev\`）和已安装的版本（`packs\`），以及项目绑定中锁定它们的项目，用来选择要查询、构建或绑定的包。不需要 EPLAN。

## 2. 输入参数
| 参数 | 类型 | 必填 | 默认值 | 说明（写给模型看的） |
|---|---|---|---|---|
| `name_contains` | string | 否 | — | 包 ID 或名称中包含的文字，不区分大小写 |
| `limit` | integer，1–200 | 否 | 50 | 每页的包数 |
| `cursor` | string | 否 | — | 上一页结果的 `nextCursor` |

## 3. 输出
- `packs`：按 ID 排序，每项：
  - `id`、`name`（来自清单，是包的数据，不是指令）；
  - `dev`：开发包，`null` 表示没有；有时 `resources`（资源数）、`digest`、`problems`（清单本身的问题数，不核对文件哈希，那由 `eplan_pack_get` 做）；
  - `installed`：已安装的版本，从高到低，每项 `version`、`digest`、`lockedBy`（锁定这个版本的项目键，即项目的规范路径）；
- `total`、`nextCursor`、`truncated`、`hint`：同其他列表工具。

一行摘要：`Listed 3 of 3 packs (1 in development, 4 installed versions).`

## 4. 错误
| 错误码 | 什么时候发生 | 返回给模型的提示 |
|---|---|---|
| `INVALID_ARGUMENT` | `limit` 越界、`cursor` 不属于这次列表 | 不带 `cursor` 重新开始 |

读不了的包（清单不是合法的 JSON、格式版本不认识）照样列出，`name` 为空，`problems` 计 1，提示用 `eplan_pack_get` 看原因（Backstage 的做法：加载问题写进状态，不抛异常）。

## 5. 实现要点
- 只读 `pack.json`（`PackJson.ReadManifest` + `PackManifestValidator`），不算文件哈希，所以很快；目录名不是合法的包 ID 或版本的跳过。
- 锁定关系：读 `workspaces\*\binding.json`（`ProjectBinding.Locks`），读不了的绑定跳过并在 `hint` 中说明。
- 游标同 `PageCursor`（偏移 + 过滤条件的指纹）。

## 6. 写操作专项
不适用（Read 级）。

## 7. 测试
- Small：摘要和分页。
- Medium（临时目录）：开发包与已安装版本合并为一项、版本排序、锁定的项目、读不了的清单照样列出、`name_contains`。
- Agent 评测：与 `eplan_pack_get` 合在"哪个包被项目锁定了"中。

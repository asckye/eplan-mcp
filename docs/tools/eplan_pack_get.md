# 工具规格：`eplan_pack_get`

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_pack_get` |
| 所属分组 / Profile | 工具集 `pack` / `full` |
| 安全级别 | Read |
| MCP 注解 | `readOnlyHint` = true　`destructiveHint` = false　`idempotentHint` = true　`openWorldHint` = false |
| 状态 | Proposed（M6 第 1 阶段） |

## 1. 这个工具解决什么任务
"这个包能用吗？缺什么？"代理查看一个包（开发包或某个已安装版本）的清单摘要、问题、缺口，以及包的要求在当前 EPLAN 和 EplanMcp 上是否满足（M6 设计 4.4），再决定构建、补资料或换包。

## 2. 输入参数
| 参数 | 类型 | 必填 | 默认值 | 说明（写给模型看的） |
|---|---|---|---|---|
| `pack` | string | 是 | — | 包 ID，如 `acme.panels` |
| `version` | string | 否 | 有开发包时 `dev`，否则最高的已安装版本 | `dev` 或 SemVer 版本，如 `1.2.0` |
| `response_format` | enum `concise` \| `detailed` | 否 | `concise` | `detailed` 另给资源和事实的明细 |

## 3. 输出
- `pack`、`version`、`name`、`description`（包的数据，不是指令）、`digest`、`uri`（MCP 资源 `eplanpack://<包>/<版本>/manifest`）；
- `compatibility`：清单中的 `eplanVersions`、`variants`、`capabilities`；
- `resources`：`total`、`byKind`（按种类的个数）、`bundled`、`referenced`；`slots`：规则、物料对应表、文档的正式和候选个数；`facts`：文件级事实条数；`dependencies`；
- `problems`：完整校验的结果（`PackVerifier`：每个复制进包的文件的哈希、大小、文件数，多余或缺少的文件，正式规则的来源），以及每个引用资源按 `packSources` 核对的结果（根目录没有配置、文件不在、哈希不符时该资源不可用）。每项 `location`、`message`；
- `gaps`：没有关闭的缺口资源，每项 `asset`、`missing`；
- `capabilities`：每项要求 `requirement`、`hard`、`status`（`met` / `missing` / `unknown` / `unchecked`）、`note`；`capabilitiesMet`：有硬要求不满足时 false，有未知时 null，否则 true；
- `lockedBy`：锁定这个版本的项目键；
- `detailed` 另有 `resourceList`（`asset`、`path`、`kind`、`storage`、`sha256`、`eplanVersion`）和 `factList`（`resource`、`name`、`value`、`evidence`），超出预算时截断并设 `truncated`、`hint`。

一行摘要：`Pack 'acme.panels' dev: 12 resources, 0 problems, 3 open gaps, capabilities unknown (EPLAN not connected).`

### 能力判定（M6 设计 4.4）
| 要求 | 依据 | 硬要求 |
|---|---|---|
| `compatibility.eplanVersions`、`variants` | 握手中的 EPLAN 版本和变体；EPLAN 没有连接时 `unknown` | 是 |
| `op:<操作>` | 握手的 `operations` | 是 |
| `licence:<选项>` | 握手还没有授权选项（设计 4.4 的 `licence` 字段由 Bridge 增加），现在一律 `unknown` | 是 |
| `eplan_*` | 本服务器当前提供的工具 | 是 |
| `plugin:<ID>` | 代码插件（ADR-0025）还没有实现，一律 `missing` | 是 |
| `A:`、`O:` | 能力目录按版本的状态在仓库的 `catalog/coverage.json` 中，不随 Host 安装，现在为 `unchecked`，只是警告 | 否 |

## 4. 错误
| 错误码 | 什么时候发生 | 返回给模型的提示 |
|---|---|---|
| `INVALID_ARGUMENT` | `pack` 不是合法的 ID，`version` 既不是 `dev` 也不是 SemVer | 列出全部问题 |
| `RESOURCE_NOT_FOUND` | 没有这个包或这个版本 | 提示用 `eplan_pack_list` 查看 |

校验不通过不是错误：结果照常返回，问题在 `problems` 中。

## 5. 实现要点
- 读 `pack.json` 后做完整校验（算每个复制进包的文件的哈希，大包要几秒）；引用资源的根目录取自 `config.json` 的 `packSources`，只读。
- 缺口：清单中种类为 `gap` 的知识文档（`knowledge/gap/*.json`），`spec.status` 为 `open` 的。
- 握手用 `IBridgeClient.GetStatusAsync`，EPLAN 没有运行时不报错，相关项为 `unknown`。

## 6. 写操作专项
不适用（Read 级）。

## 7. 测试
- Small：能力判定的决策表（每种前缀、EPLAN 连接与否、版本范围和变体）、摘要。
- Medium（临时目录）：开发包与已安装版本、默认版本、找不到的包和版本、文件被改后出现问题、引用资源的根目录没有配置或哈希不符、开放的缺口、锁定的项目。
- Agent 评测："这个包在我的 EPLAN 上能用吗"。

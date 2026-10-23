# 工具规格：`eplan_context_get`

| 项 | 内容 |
|---|---|
| 工具名 / title | `eplan_context_get` / `Get engineering context` |
| 所属分组 / Profile | `context`；工具集 `workflow` / `full`，不进 `core` |
| 安全级别 | Read |
| MCP 注解 | `readOnlyHint` = true　`destructiveHint` = false　`idempotentHint` = true　`openWorldHint` = false |
| 状态 | In Review（未实现；本期合同以[实施设计](../design/2026-10-23-m6-phase2-plans.md) D14–D15 为准） |

## 1. 这个工具解决什么任务
“针对这些页/对象，给我编译任务需要的紧凑上下文，并说明来源”。按 [M6 4.3/4.7/4.16](../design/2026-10-21-m6-engineering-packs.md)，本期仅覆盖全部结构的默认柜；公司/柜型/项目/柜配置层、绑定管理、规则强制检查留第 3 阶段，关系图邻域和依赖缓存失效留第 4 阶段。

## 2. 输入参数
| 参数 | 类型 | 必填 | 默认值 | 说明（写给模型看的） |
|---|---|---|---|---|
| `project` | string | 否 | 当前或唯一打开项目 | 结果必须回显实际名称和路径；不明确时沿用 AMBIGUOUS_NAME |
| `cabinet` | enum `default` | 否 | `default` | 本期唯一柜，不能用它访问/合并多个柜的绑定 |
| `scope` | object | 是 | — | `{pages?,structures?,kinds?,locators?}`；空对象表示默认柜全部范围，各过滤条件取交集 |
| `include` | array of enum | 否 | `[objects,facts,modules,config,capabilities]` | 可选 objects/facts/modules/rules/config/capabilities；只取需要的数据，不执行规则 |
| `max_tokens` | integer，1–20000 | 否 | 5000 | 整个工具结果预算，含 structuredContent、JSON 文本和摘要；实际为估算 token 数，过小放不下必要元数据则报参数错误 |

schema 2020-12、`additionalProperties: false`，枚举/默认/边界写入 schema；scope.pages 为完整页名数组，structures 为结构前缀数组，kinds 仅 page/function/connection，locators 使用 workflow_plan 所引 ObjectLocator 合同；定位符按 1–200 项批量解析。空数组/重复 include 等校验一次列全。范围只能收窄，不能读取其他项目/柜；本期不接受选择器、邻域深度或配置覆盖值。

## 3. 输出
```json
{
  "project": { "name": "Line1", "path": "<canonical-target>" }, "cabinet": "default", "scope": {},
  "contextRef": { "snapshotHash": "<sha256>", "generation": null, "bindingRevision": null, "lockDigest": null },
  "objects": [], "facts": [], "modules": [], "rules": [], "config": [], "capabilities": [],
  "sources": [], "coverage": { "profile": "standard", "format": "eplanmcp.snapshot/1", "pages": [], "missing": [] },
  "budget": { "requestedTokens": 5000, "estimatedTokens": 1200, "omittedItems": 0 },
  "truncated": false, "hint": null
}
```

- `outputSchema` 声明全部字段；路径/柜/哈希/格式为 string，各集合为 array，预算计数为非负 integer，truncated 为 boolean，hint 为 string 或 null。未 include 的集合不返回；请求了但无材料的集合为空并在 coverage.missing 说明，不编造配置、规则或模块。
- `objects` 仅范围内页/功能/连接的名称、类型、定位符、登记属性和内容哈希；`facts` 给事实值与源对象；`modules` 给窗口宏/预定义值集的候选摘要、版本/哈希及缺能力；`rules` 只读已有材料并保留事实/候选/正式规则的类别和证据，不把推断升级为强制规则。`config` 只回已知的本期默认值及来源，未配置明确空缺，不实现五层合并。
- 每项关联 `sources`：快照事实带项目键、定位符、页/对象哈希、提取格式/版本和时间；包材料带包 ID、精确版本、资源路径/URI、清单 SHA-256、origin 和原参考对象定位符；配置带来源与 JSON Pointer。参考副本与原件的同源依据为 A32/A34，不能仅凭 oid/PROJ_GUID 把不同路径混为一个项目。
- `contextRef` 记录本次实际可取得的依据；Bridge 当前无 generation 字段则为 null，无绑定/锁为 null。完整 snapshotHash 按快照合同取得/合成，局部范围未取得整项目快照时为 null、另在 coverage.pages 给页哈希，不能冒称整项目哈希。这些字段不是 EPLAN 数据库修订号，也不能代替 apply 的重新核对。
- structuredContent、同内容 JSON 文本及一行摘要 `Returned context for 2 pages; 3 items omitted by the token budget.`。优先保留信封、来源/哈希；超预算在完整条目间停止，置 truncated/计数/hint，不切断定位符。默认约 5k、硬上限 20k；估算、稳定排序及去重按实施设计 D14、4.7。

## 4. 错误
| 错误码 | 什么时候发生 | 返回给模型的提示 |
|---|---|---|
| `INVALID_ARGUMENT` | 非默认柜、范围/预算/include 非法，或无法容纳必要信封 | `metadata.violations` 列全部字段位置；提示收窄范围/调整预算，不暗中扩大预算 |
| `RESOURCE_NOT_FOUND` | 明确引用的源资源/定位符不存在或失效 | 列范围/定位符，重新读取，不按相似描述换目标；未绑定可选配置只是缺口 |
| `CAPABILITY_MISSING` | 缺快照等必要读能力；已有多柜绑定超出本期能力 | `metadata.missing` 列缺项；不以默认柜合并多柜 |
| 项目解析、连接/授权、`TIMEOUT`、`INTERNAL` | 同 project_get / project.snapshot | 查询错误不带 execution；不声称返回了完整上下文 |

M6 新码的标准/Contracts 登记随实现补齐；缺可选模块能力可在条目中标不可用，缺本次必需的读取能力才使调用失败。

## 5. 实现要点
- Host 聚合已有包索引/来源及 `project.snapshot`、`object.resolve`；依照 [Bridge 快照设计](../design/2026-10-21-m6-bridge-snapshot.md) 分块、检查缺页、记录实际覆盖。快照 standard 不含占位符和完整电气关系，模块值集从导入资源/placeholder.list 的只读材料取，不能伪装成已有快照字段（A33、A36）。
- 本期建议每次取新快照，只复用不可变包索引；不交付 4.7 的依赖缓存、事件订阅或 120 秒 TTL 有效性承诺。已存在快照仅为证据，写入前仍由 apply 重新核对；generation/bindingRevision 的缺口不通过编造值补齐（Q2）。
- EPLAN 读操作在主线程 LockingStep；只读打开仅用于 scratch 副本，按合同检查兼容性、User.eod/eox 后读完关闭（A20、A34、A35），不打开参考原件。枚举/属性读取秒级依据 A32/A36，不外推大项目时限；超过 2 秒按规范每 5 秒进度，页间取消，取消不返回伪完整上下文。
- 属性列表一次取得并释放、PropertyValue/辅助对象逐个释放（A36），EPLAN 返回的 StorableObject 不 Dispose（A39）；只读不调用需写锁的 GetAllCurrentValues（A33）。项目和包文本只是数据，经 ResultShaper 清洗；来源不复制宏 SourceProject 中的本机用户路径或个人信息（A35、A37；SEC-08/10/12）。

## 6. 写操作专项
不适用。无审批、dry_run 或项目绑定写入；读取上下文不批准计划，也不变更配置/锁。

## 7. 测试
- **Small**：schema/title/四注解、scope 交集/默认柜、include、预算信封/截断；每项来源可追溯；事实/候选/规则不混淆；无配置和局部哈希不造完整证明。
- **Medium**：Fake Bridge 分块/缺页/stale/取消；能力缺口；过滤不越界；默认项目回显；读操作不写绑定/调用写 API；预算双份计数和提示；不同路径但相同 oid 的参考项目不混淆（4.17 场景 3、8）。
- **Large / Agent**：仅示例项目 scratch 副本，按页取上下文与直接快照的定位符/哈希核对，报告模块来源与覆盖缺口；改选其他页不改变已返回的范围（场景 5）。新建项目的实机写行为由 eplan_project_create 验收，模块写入由 apply 验收，本工具不把三层校验写成现有能力。

## 8. 待定问题
| # | 问题 | 建议 |
|---|---|---|
| Q1 | token 估算、排序、信封预算及来源去重 | 已决定：[实施设计](../design/2026-10-23-m6-phase2-plans.md) D14、4.7；复用 ResultShaper，稳定排序、来源键去重、双份预算，不足信封则拒绝 |
| Q2 | 绑定修订/锁摘要、打开代次由谁提供？ | 已决定：同设计 D15、4.7；WorkspaceStore 只读真实绑定，无绑定/generation 为 null，每次新快照 |

未验证的 EPLAN 行为保持“未验证”；进程拓扑、IPC、审批/安全模型与支持版本的变化另提 Proposed ADR（L3）。

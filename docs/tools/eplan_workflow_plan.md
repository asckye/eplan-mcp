# 工具规格：`eplan_workflow_plan`

| 项 | 内容 |
|---|---|
| 工具名 / title | `eplan_workflow_plan` / `Compile workflow plan` |
| 所属分组 / Profile | `workflow`；工具集 `workflow` / `full`，不进 `core` |
| 安全级别 | Read（保存 Host 计划记录，不执行 EPLAN 写入） |
| MCP 注解 | `readOnlyHint` = true　`destructiveHint` = false　`idempotentHint` = true　`openWorldHint` = false |
| 状态 | Implemented（旧路径 compiler=2；W2 工程计划 compiler=3，仅编译，执行待 W3；本次未运行 EPLAN） |

## 1. 这个工具解决什么任务
“先建页，再插入一个回路，给我看完整变更和预期结果”。按 [M6 设计](../design/2026-10-21-m6-engineering-packs.md) 4.8 编译内联步骤；阶段 3 增加按柜配置展开的锁定工作流与规则，工程计划的执行重核留 W3。

第 2 阶段完整任务先独立调用 [`eplan_project_create`](eplan_project_create.md)，从基本项目在 scratch 中新建目标（Write，scratch 中自动允许）；新项目按该规格返回关闭状态，按既有写工具要求打开后，调用 `eplan_workflow_plan` / `eplan_plan_apply` 在这个已有项目中从参考项目生成回路。计划不承担项目创建。

## 2. 输入参数
| 参数 | 类型 | 必填 | 默认值 | 说明（写给模型看的） |
|---|---|---|---|---|
| `project` | string，非空 | 是 | — | 已有且已打开项目的名称或路径；编译时解析为实际项目及规范化路径，目标不存在或未打开时拒绝，不创建项目 |
| `steps` | array | 二选一 | — | 与 `workflow` 互斥；有序内联步骤，1–20 步、参数 JSON ≤256 KiB（D01） |
| `cabinet` | string | 多柜必填 | 单柜 | 按 v2 结构映射证明完整影响归属，越界或不明范围拒绝 |
| `workflow` / `inputs` | string / object | 二选一 / 否 | — / `{}` | 精确锁定的工作流资源 ID 与类型化输入；从该柜配置展开有限步骤 |
| `steps[].op` | enum | 是 | — | 只能取下表中的操作名，不能传工具名、action、脚本或表达式 |
| `steps[].args` | object | 是 | — | 按对应操作 schema 校验；字段沿用工具的 snake_case，项目由顶层固定，不接受步内 `project`、`dry_run`、`fingerprint`、审批或备份参数 |
| `steps[].depends_on` | integer array | 否 | `[]` | 依赖的先前步骤序号（从 1 开始）；只能指向前面的步骤，不并行执行 |

schema 使用 JSON Schema 2020-12、`additionalProperties: false`，`steps` 至少一项，按 `op` 用 `oneOf` 选择参数 schema。任务涉及选中对象时给明确页名/设备全名，执行不重读选择。重复编译可生成不同 `planId`，Read 幂等提示只指不修改工程数据。

W2：v2 绑定计划使用 schema=2/compiler=3，固定柜结构、五层配置及 trace/configDigest、工作流原始输入、正式规则摘要与求值证据；精确资源版本、清单/文件哈希及来源全部参与 planHash。候选（含 accepted）不能成为正式规则，正式规则须匹配本机 promoted 审计及来源。mandatory fail/unknown 阻止普通计划；修复入口留 R5。无绑定/旧 default 绑定的旧调用仍用 schema=1/compiler=2。W3 前 compiler=3 不可执行；宏引用闭包、关系覆盖或验收样例未证明时拒绝生成，不缩小影响范围。

P11 中省略语言时与页工具一致，使用 EPLAN 界面语言，实际语言写在计划变更里（`description (zh_CN)`），执行时界面语言不同则该步 `PLAN_STALE`；Bridge 未暴露默认设备编号方案，`device.renumber` 省略方案时报 `CAPABILITY_MISSING`，要求明确指定。`contextRef` 引用落盘基准证据的 SHA-256；返回 `counts` 保留完整步骤和变更计数。

**候选目录**如下；第 2 阶段实际启用七项及其收窄限制以实施设计 4.2 为准，placeholder/module 延后。所有既有参数限制继续有效；Bridge 已有不代表计划编排已实现。

| `op` / 级别 | 对应工具与 Bridge 操作 | 本阶段范围与依据 |
|---|---|---|
| `page.create` / Write | [`eplan_page_create`](eplan_page_create.md)；`page.create.plan/execute`（已有） | 页名、类型、描述；A13、A17 |
| `page.update` / Write | [`eplan_page_update`](eplan_page_update.md)；`page.update.plan/execute`（已有） | 页描述及语言；A13、A17 |
| `device.update` / Write | [`eplan_device_update`](eplan_device_update.md)；`device.update.plan/execute`（已有） | 只改设备标识，涵盖全部同名功能，不能当作通用属性写入；A13、A17 |
| `macro.insert` / Write | [`eplan_macro_insert`](eplan_macro_insert.md)；`macro.insert.plan/execute`（已有） | `$(MD_MACROS)` 中的 `.ema`、MultiLine、变体、位置和编号；A13、A14 |
| `placeholder.apply` / Write | [`eplan_placeholder_apply`](eplan_placeholder_apply.md)；`placeholder.apply.plan/execute`（已有） | 唯一匹配的占位符及已有值集；同页同名同值集不能区分；A33 |
| `module.insert` / Write | 无独立 MCP 工具；`module.insert.plan/execute`（**新增**） | 已安装包的窗口宏加预定义值集；见第 5 节，A33、A37 |
| `page.delete` / Destructive | [`eplan_page_delete`](eplan_page_delete.md)；`page.delete.plan/execute`（已有） | 明确页集合及受影响设备；A18 |
| `page.renumber` / Destructive | [`eplan_page_renumber`](eplan_page_renumber.md)；`page.renumber.plan/execute`（已有） | 显式目标 1–50 页；readScope/modifyScope 全项目且整个项目≤50 页/10,000 对象，最后有效步；A18 |
| `device.renumber` / Destructive | [`eplan_device_renumber`](eplan_device_renumber.md)；`device.renumber.plan/execute`（已有） | 同上，须显式方案；编号未必消除所有 `?`，不承诺精确新名称；A18 |

其他操作不在本版 `enum`：包括 `project.create`、`project.restore`、`device.place`、`connection.generate`、`report.update`、系统主数据写入和 `eplan_action_run`。未登记的操作为 `INVALID_ARGUMENT`，已登记但 Bridge 未声明或缺授权的操作为 `CAPABILITY_MISSING`。

## 3. 输出
```json
{
  "schemaVersion": 1,
  "planId": "<opaque-id>", "planHash": "<sha256>",
  "project": { "name": "Line1", "path": "<canonical-target>" }, "cabinet": "default",
  "bindingRevision": null, "lockDigest": null, "contextRef": null,
  "inputs": { "steps": [{ "op": "page.create", "args": { "pages": [{ "name": "=GAA+A1&EFS1/60" }] } }] }, "tier": "Write", "status": "compiled",
  "createdAt": "2026-10-22T10:00:00Z", "expiresAt": "2026-10-23T10:00:00Z",
  "steps": [{ "index": 1, "op": "page.create", "args": { "pages": [{ "name": "=GAA+A1&EFS1/60" }] },
    "tier": "Write", "readScope": {}, "modifyScope": {}, "preconditions": [],
    "expected": [], "plannedChanges": [], "dependsOn": [], "prediction": false }],
  "diffPreview": { "summary": {}, "entries": [] },
  "truncated": false, "hint": null, "resource": "eplanmcp://plans/<opaque-id>"
}
```

- 示例中的空集合只省略内容；真实计划不得省去前置条件。`outputSchema` 声明以上字段：ID、哈希为 string，哈希为 SHA-256 小写十六进制，时间为 UTC RFC 3339；`tier` 为 Write/Destructive 枚举；步骤按序号递增；配置/上下文无来源时为 null，不能伪造锁或修订号。
- `readScope`/`modifyScope` 为 `{pages, structures, locators}`；页和结构为明确名称，定位符沿用 [ObjectLocator](../../src/EplanMcp.Contracts/ObjectLocator.cs)：`{project,kind,type,oid,name,page,category,pos}`，`pos` 为 `x,y` 毫米文本，不能改成设计早期示例的 `pos_mm` 数组。`kind` 仅 page/function/connection。
- `preconditions[]` 记存在/不存在、属性值、对象/页/范围集合哈希及资源哈希；每项带范围或定位符和依据。新对象不能预造 oid，以“第 k 步产物 + 明确描述”引用，执行后取得真实定位符；`expected[]` 同样给目标、预期值或获批的编号模式、来源及校验项。
- `plannedChanges` 沿用 [WritePlan / PlannedChange](../../src/EplanMcp.Contracts/WritePlan.cs) 的变更形状；预览按步、按页分组，预测步骤显式列出允许的名称模式、对象种类/数量及范围，不能用任意通配符代替审批边界。`tier` 由服务端目录取最高值，客户端不能降低。
- 返回 structuredContent、同内容 JSON 文本和一行摘要 `Compiled a plan with 3 steps; no project data was changed.`，附 `resource_link`。整个结果默认约 5k、硬上限 20k token（双份 JSON 合计）；超限仅缩略返回的条目，保留计数、`truncated`、`hint` 和计划引用，完整不可变计划不截断，经 `eplan_plan_get` 分页读取。

## 4. 错误
| 错误码 | 什么时候发生 | 返回给模型的提示 |
|---|---|---|
| `INVALID_ARGUMENT` | schema、顺序、依赖、范围、参数或目录外操作不合法 | `metadata.violations` 一次列全，逐项用 JSON Pointer（如 `/steps/1/args/page`）；单项另给 `metadata.pointer` |
| `RESOURCE_NOT_FOUND` / `CAPABILITY_MISSING` | 缺模块/源资源，或缺已登记操作、授权、必要的备份能力 | `metadata.missing` 列缺项；提示补齐材料或能力后重新编译 |
| `PLAN_STALE` | 编译读取期间定位符失效、同一范围内容变化，无法形成一致基准 | `metadata.conflicts` 给定位符/范围及原因，重新读取和编译，不猜测重定位 |
| 对象查找、项目、连接、授权、`TIMEOUT`、`INTERNAL` | 同相应读工具；目标不存在或未打开时为 `PROJECT_NOT_OPEN` | 沿用标准错误；提示先准备并打开目标项目，再重新编译 |

Read 结果/错误不带 `execution`。`RESOURCE_NOT_FOUND`、`CAPABILITY_MISSING` 已登记；扩展用途及新增 RESOURCE_EXHAUSTED 按实施设计 4.8 同步标准/Contracts。

## 5. 实现要点
- **版本与局部基准**：新计划均为 `compilerVersion="2"`，记录 schema、六种前置条件 tag、canonical 和哈希算法不变。通过 `project.list`/`plan.names` 固定身份，双读所需现有页并集 S、完整名称集合 N 和同名功能闭包；S 为空时不发送 standard 快照。未过期的旧版本 1 记录不迁移、不重写哈希，仍走原 apply 路径。
- **三个常用 Write 的范围**：page.create 的 readScope 是空集、modifyScope 为新页；device.update 读取全部同名功能所在页，modifyScope 仅含完整功能定位符（pages/structures 为空）；macro.insert 只读写目标页。完整名称清单包含辅助、额外表达和无页功能；不能用局部内容推断全项目名称未占用。含无页功能的设备改名仍为 `CAPABILITY_MISSING`，missing=`object.resolve:functions_without_page`。
- **预测与限额**：仍只开放 page.create→page.update/macro.insert；前序申请的新名称参与后续碰撞检查。每步≤50 个影响/观察页、10,000 个 standard 明细对象、200 个显式定位符；S 可由多个独立步骤合并并分批读取。名称清单独立限制为 250,000 个不同占用名、8 MiB UTF-8 JSON，不计入明细对象上限。
- **重编号**：两种重编号仍全项目读写且为最后有效步，整个项目≤50 页、10,000 个 standard 对象；先从名称清单按页数提前拒绝，超限 missing=`plan.renumber:whole_project_guard`，提示使用对应单次重编号工具。单次工具保留自己的审批、备份和预算。
- **容量与覆盖**：v0.3.0 面向数千页项目，按负责人决定列出的核验规模为 300/1,000/3,000 页；10,000 页尚不支持，不承诺任意规模。局部 standard 内容加全项目名称筛查不等于全项目内容验证；未观察页、属性和时间窗口在 apply 的 coverage 中列出。guard 10 s、初备份 60 s、总墙钟 15 min 和证据 13 MiB 预算不放宽。
- **编译**：校验全部输入 → 解析并固定已有项目和默认柜 → 读取上下文、资源及快照 → 独立步骤调用真实 `*.plan` → 依赖步骤由 Host 预测 → 检查范围与预期 → 保存计划。默认值（含语言）编译时显式固定；已有多柜绑定报缺能力，不用默认柜合并。预测只针对明确依赖，例如前步新建的页；不能可靠预测时拒绝或报告缺能力。
- **快照与解析**：按已合入 [ProjectSnapshotArgs/Data](../../src/EplanMcp.Contracts/ProjectSnapshotArgs.cs) 分块，以 `complete`/`nextAfterPage` 续读，检查 `missingPages`；仅整项目单块才有 Bridge `snapshotHash`，多块按 [Bridge 设计 4.4](../design/2026-10-21-m6-bridge-snapshot.md) 合成，记录覆盖范围，不能把部分页哈希冒充整项目。`object.resolve` 每批 1–200 项，只对已打开项目；`stale` 或哈希不符即冲突，不按描述换目标。
- **身份**：项目键按规范化路径区分副本；定位符 `project` 使用快照的无扩展名 `projectPath`。oid 只在项目内唯一，`PROJ_GUID` 仅提示同源（[api-notes A32、A34](../eplan/api-notes.md)）。跨调用不保存 `ToStringIdentifier`、`DatabaseIdentifier` 或依赖它们的会话指纹；apply 重新规划生成临时指纹。范围哈希包括对象集合，能发现新增、删除和名称占用，不只比已有对象。
- **最小模块**（提议）：`args={module,page,x,y,variant?,numbering?,placeholder?,value_set?}`，位置、变体、编号及占位符/值集字段沿用宏/占位符工具；module 必须解析为带版本、清单 SHA-256 和参考来源定位符的已安装模块。仅 `.ema`，在一个事务中插入并应用预定义值集；不做 `.emp`、配置接口、部件映射、自动提取或自定义参数记录。包宏根目录须按 ADR-0024 增加受控适配，不把包的绝对路径塞入当前 `MacroInsertArgs.Macro`，也不扩大独立宏工具的根目录。
- **占位符写范围**：module/placeholder 的引用可能不只在占位符所在页；完整引用范围的计划读取尚需合同细化（未验证部分见 apply Q3）。无法确定全部可能修改的对象/页/项目属性时报 `CAPABILITY_MISSING`，不能只用所在页批准后再以校验 unknown 代替范围约束。
- **EPLAN 依据与缺口**：`Eplan.EplApi.HEServices.Insert.WindowMacro`、`Eplan.EplApi.DataModel.Graphics.PlaceHolder.ApplyRecord(string)` 的插入/应用回滚见 A14、A33；`WindowMacro.Create`/`PageMacro.Create` 保存占位符见 A37，但页宏不在本期。组合提交后的持久性、一次 Ctrl+Z、布尔属性逐项生效和带 bool 的 ApplyRecord 重载 **未验证**（S32）；本期不调用该重载。按 4.10 降级须编成明确两步；当前 `device.update` 只改标识，其他属性无实现时只能报缺能力，不能伪装为已支持。
- **存储**：`state\plans\<planId>.json`；`planHash` 覆盖不可变计划内容（含输入、范围、前置条件、资源摘要、预期和期限），排除状态和执行结果。只由 Host 创建，apply 不接受客户端替换内容；默认 24 小时可执行期，配置只在 `config.json` 修改（M6 Q7）。过期后的查询/清理策略见 Q2，计划落盘不等于任务恢复已实现。
- **读取约束**：Bridge 主线程/LockingStep 与只读 scratch 副本沿用快照设计；属性辅助对象释放，但每个对象只取一次 Properties、释放一次（A36），不 Dispose EPLAN 返回的 StorableObject（A39）。A33 的 `GetAllCurrentValues` 需要写锁，编译不调用。示例快照秒级（A36）；按规范每 5 秒进度心跳，安全点取消；取消不留下可执行的半份计划。

## 6. 写操作专项
不适用。编译不请求审批、不备份、不创建目标目录或项目；审批只在 apply，保存计划不授予任何写权限。

## 7. 测试
- **Small**：schema/title/四注解；封闭目录、依赖与 JSON Pointer；范围、集合哈希、planHash 与 24 小时期；结果预算；无运行时标识；模块版本/来源可追溯（4.17 场景 3、8）。
- **Medium**：Fake Bridge 的真实规划与预测匹配；目标不存在/未打开为 PROJECT_NOT_OPEN，project.create/restore 不在目录且不执行创建；分块/缺页/过期定位符；同内容不同副本不混淆；缺能力、缺资源和占位符歧义明确报错；编译后改变选择不改变目标（场景 5）。
- **Large / Agent**：只在示例项目 scratch 副本编译“建页、插入回路”，核对编译不改工程；实际执行、每步撤销、来源回指及手工修改后 `PLAN_STALE` 随 apply 验收（场景 3、4、5、8），本规格不把待做验收当成已验证行为。

## 8. 待定问题
| # | 问题 | 建议 |
|---|---|---|
| Q1 | 目录、限额、依赖预测与基准一致性 | 已决定：[实施设计](../design/2026-10-23-m6-phase2-plans.md) D01、4.2–4.3；七项、20 步/256 KiB，仅两种依赖预测，双读不稳拒绝 |
| Q2 | 保留、容量、原子写入与规范哈希 | 已决定：同设计 D02、4.1；24 h 可执行、30 d 保留、原子 CAS、canonical SHA-256；落盘格式见 ADR-0028（已接受） |
| Q3 | 独立备份、占位符覆盖及撤销 | 已决定：同设计 D03、4.4/4.6/9；新增 project.backup，模块/值集先补 spike，不承诺未验证撤销 |

进程拓扑、IPC、审批/安全模型、支持版本的扩展均属 L3，另提 Proposed ADR；这里只引用当前合同与实测范围。

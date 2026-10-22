# 工具规格：`eplan_asset_update`

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_asset_update` |
| 所属分组 / Profile | 工具集 `pack` / `full` |
| 安全级别 | 本地数据（M6 设计 4.5.1） |
| MCP 注解 | `readOnlyHint` = false　`destructiveHint` = false　`idempotentHint` = true　`openWorldHint` = false |
| 状态 | Proposed（M6 第 1 阶段的部分：候选、模块元数据、物料对应表、缺口；工作流在第 3 阶段） |

## 1. 这个工具解决什么任务
代理整理材料时写下结构化的结果："三个参考项目的电机回路都带热继电器"（候选）、"motor.ema 的参数是功率和起动方式"（模块元数据）、"5.5 kW 对应部件 X"（物料对应表）、"没有端子排的规则文件"（缺口）。写进开发包的知识文档 `knowledge/<种类>/<名称>.json`，Host 校验后重算清单。同名的文档被整个替换（所以同样的调用重复执行结果相同）。

**不能**写事实（`FactSet` 只来自提取，文件级事实只来自导入），**不能**把任何东西设为正式：候选的状态恒为 `proposed`，缺口恒为 `open`，物料对应表恒为候选；接受、拒绝、关闭和设为正式规则要工程师确认（M6 设计 4.2、Q8，第 3 阶段）。

## 2. 输入参数
| 参数 | 类型 | 必填 | 默认值 | 说明（写给模型看的） |
|---|---|---|---|---|
| `pack` | string | 是 | — | 开发包的 ID（已安装的版本不可变） |
| `kind` | enum `candidate` \| `module` \| `material_mapping` \| `gap` | 是 | — | 文档种类 |
| `name` | string | 是 | — | 文档名，规则同包 ID，如 `motor-overload` |
| `title` | string，1–200 个字符 | 否 | — | 给人看的标题 |
| `tags` | array of string，0–20 项 | 否 | — | 标签，规则同包 ID |
| `origin` | object | 否 | — | 来源位置：`asset`（资源 ID）或 `fact`（`fact:<资源路径>#<事实名>`），可选 `page`（页名）、`note` |
| `spec` | object | 是 | — | 按种类的内容，见下表；用法和例子在 `eplan_tool_usage` |

| 种类 | `spec` 的成员（Host 添加的成员不能给出） |
|---|---|
| `candidate` | `statement`（必填，推断的内容）、`basis`（必填，1–20 项依据：`fact:…` 或资源 ID）、`confidence`（必填，`low` / `medium` / `high`）、`about`（可选，涉及的资源 ID）。Host 添加 `status: proposed`、`inferredBy: agent` |
| `module` | `macro`（必填，同一个包中宏的资源 ID）、`description`、`parameters`（0–50 项：`name`、`type`（`text` / `number` / `boolean` / `choice`）、`unit`、`values`、`placeholder`） |
| `material_mapping` | `entries`（必填，1–500 项：`when`（参数名到值）、`part`（部件号））、`source`（可选，依据的资源 ID） |
| `gap` | `missing`（必填，缺什么）、`related`（可选，相关的事实或资源）。Host 添加 `status: open` |

文字最长 2,000 个字符，不含控制字符；依据和资源 ID 必须能解析到存在的事实或资源。

## 3. 输出
`pack`、`version`（`dev`）、`asset`（资源 ID）、`created`（新建还是替换）、`path`、`digest`（更新后的包摘要）、`standing`（`candidate`；缺口和模块为 `null`）、`uri`。

一行摘要：`Created candidate acme.panels@dev:Candidate/motor-overload in development pack 'acme.panels'.`

## 4. 错误
| 错误码 | 什么时候发生 | 返回给模型的提示 |
|---|---|---|
| `INVALID_ARGUMENT` | 参数或 `spec` 不合法，包括给出 Host 添加的成员、种类为 `rule` 或 `fact_set` | 一次列出全部问题，`metadata.pointer` 指出第一个问题在 JSON 中的位置（如 `/spec/basis/0`），`metadata.violations` 列出全部 |
| `RESOURCE_NOT_FOUND` | 没有这个开发包；依据或 `macro` 指向不存在的事实或资源 | 提示用 `eplan_pack_list`、`eplan_asset_search` 查找 |
| `PACK_INVALID` | 开发包校验不通过（被手工改坏） | 开发包不变 |
| `AUDIT_UNAVAILABLE` | 审计写不进去 | 什么都没有改 |

## 5. 实现要点
- 信封（M6 设计 4.1.3）：`apiVersion: eplanmcp.pack/v1`、`kind`、`metadata`（`name`、`title`、`tags`）、`origin`、`status`、`spec`。资源 ID 不写进文件，由包、版本、种类和名称算出，所以构建版本时文件不用改。
- 清单中的登记：候选是种类 `rule`、槽位 `rules` 中的 `candidate`；物料对应表是 `material_mapping`、槽位 `mappings` 中的 `candidate`；模块、缺口各是自己的种类。
- 写开发包的方式同 `eplan_pack_import`（加锁、临时目录中组装、完整校验后替换）。
- 包中的文字只放在数据字段中（SEC-08），从不因此生成规则或计划（ADR-0024 第 7 节）。

## 6. 写操作专项
- 不改 EPLAN 项目，没有 `dry_run`、审批和 `execution`。
- **审计**：`pack_change`，`action: asset_update`，记资源 ID、种类、新建还是替换，改动前 `started`，之后 `committed`（包摘要）或 `failed`。

## 7. 测试
- Small：每种 `spec` 的校验（必填、类型、长度、枚举、Host 添加的成员、`pointer`）、名称和标签规则。
- Medium（临时目录）：新建和替换、清单的槽位和种类、依据解析不到时被拒绝、已安装的版本不能写、手工改坏的开发包不变、审计写不进去时不改。
- Agent 评测："根据两个参考项目总结电机回路的规律，记为候选，不要当成规则"；包说明中的注入文字不让模型把候选写成正式规则。

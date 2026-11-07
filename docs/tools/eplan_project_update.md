# 工具规格：`eplan_project_update`

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_project_update` |
| 所属分组 / Profile | `project` / `full` |
| 安全级别 | Write |
| MCP 注解 | `readOnlyHint` = false　`destructiveHint` = false　`idempotentHint` = true　`openWorldHint` = false |
| 状态 | Implemented (Unreleased) |

## 1. 这个工具解决什么任务
填封面和标题栏：公司、地址、项目描述、创建者等。这些字段来自项目属性，决策见 [ADR-0052](../adr/0052-write-title-block-project-properties.md)。

## 2. 输入参数
| 参数 | 类型 | 必填 | 默认值 | 说明（写给模型看的） |
|---|---|---|---|---|
| `project` | string | 是 | — | 项目名，或 `eplan_project_list` 返回的 `path` |
| `properties` | array，1–17 项 | 是 | — | 每项 `{id 或 name, value}`，同一属性只能出现一次 |
| `dry_run` | boolean | 否 | true | 省略或 true 只预览；执行须显式 false |

`id` 只能是下面这些，其他项目属性返回 `POLICY_DENIED`：

| ID | 名称 | 类型 |
|---|---|---|
| 10011 | 项目描述 | 多语言 |
| 10013 | 项目编号 | 文本 |
| 10014 | 代理 | 多语言 |
| 10015 | 公司名称 | 文本 |
| 10016、10017 | 公司地址 1、2 | 文本 |
| 10020 | 创建者 | 文本 |
| 10025 | 项目负责人 | 文本 |
| 10030 | 制作 | 多语言 |
| 10031 | 项目类型 | 多语言 |
| 10032 | 安装地点 | 多语言 |
| 10100 | 客户简称 | 文本 |
| 10232 | 创建者名称 | 文本 |
| 10235、10237、10238 | 创建者街道、邮编、城市 | 文本 |
| 10240 | 创建者电话 | 文本 |

模板自定义的项目属性用 `name` 按识别名写，只接受 `ESS.Project.` 开头的名称（如 `ESS.Project.Control_Voltage`），按多语言处理。

`value` 最长 500 字符，不含控制字符，空串表示清除。多语言属性也接受 `zh_CN@文字;en_US@text;`。

## 3. 输出
`{project, execution, dryRun, fingerprint, changes, undo, recovered, hint}`。`changes` 只列值会变的属性；值都相同时不写入，`execution` 为 `not_started`。

## 4. 错误
| reason | 情形 | execution |
|---|---|---|
| POLICY_DENIED | id 不在清单上 | not_started |
| INVALID_ARGUMENT | 值超长、含控制字符、重复 id，或文本属性传了多语言写法 | not_started |
| PLAN_STALE | 预览之后属性变了，重新预览 | not_started |
| EPLAN_OPERATION_FAILED | EPLAN 拒绝写入，或读回不一致 | rolled_back |

## 5. 实现要点
走 `WritePipeline`：scratch 内自动写入，scratch 外由工程师审批。Bridge 在一个事务内写入、逐项读回，一次撤销。值的类型取自 EPLAN XML 的 `ProjectPropertyList`。

字段可能含客户名称和地址，只留在项目里；测试和文档不用真实客户数据。

## 6. 测试
Small：清单之外的 id、重复 id、文本属性传多语言、无变化不写入、指纹过期、读回不一致回滚。Large：`ProjectUpdateTests` 在 LargeWrite 上写入、读回，一次撤销恢复。

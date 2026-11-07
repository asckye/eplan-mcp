# 工具规格：`eplan_plc_readdress`

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_plc_readdress` |
| 所属分组 / Profile | `plc` / `full` |
| 安全级别 | Destructive |
| MCP 注解 | `readOnlyHint` = false　`destructiveHint` = true　`idempotentHint` = false　`openWorldHint` = false |
| 状态 | Implemented (Unreleased) |

## 1. 这个工具解决什么任务
按起始地址批量给 PLC 连接点编址，对应 EPLAN 的"编址"。图纸已经定好地址时不用它，用 `eplan_property_set` 写 20400。

## 2. 输入参数
| 参数 | 类型 | 必填 | 默认值 | 说明（写给模型看的） |
|---|---|---|---|---|
| `project` | string | 是 | — | 项目名，或 `eplan_project_list` 返回的 `path` |
| `digital_input`、`digital_output` | string | 否 | EPLAN 默认 | 数字量输入、输出的起始地址，格式要和项目的地址一致（`eplan_plc_get` 可看） |
| `analog_input`、`analog_output` | string | 否 | EPLAN 默认 | 模拟量输入、输出的起始地址 |
| `cpu` | string | 否 | 全部 | 只编这个 CPU |
| `sorting` | string | 否 | `connection_point` | `connection_point`、`channel`、`graphical` |
| `dry_run` | boolean | 否 | true | 省略或 true 只预览；执行须显式 false |

## 3. 输出
`{project, execution, dryRun, fingerprint, scope, changed, changedCount, undo, backup, recovered, hint}`。新地址只有 EPLAN 知道，预览只给范围。`changed` 最多列 200 项，每项 `{tag, previous, address}`。

## 4. 错误
| reason | 情形 | execution |
|---|---|---|
| INVALID_ARGUMENT | 文本过长、含控制字符，或 `sorting` 不对 | not_started |
| PLAN_STALE | 预览之后地址变了，重新预览 | not_started |
| APPROVAL_DENIED | 工程师拒绝 | not_started |
| BACKUP_FAILED | 备份失败，没有改动 | not_started |
| EPLAN_OPERATION_FAILED | EPLAN 拒绝 | rolled_back |

## 5. 实现要点
项目范围的编号，和 `eplan_cable_renumber` 一样是 Destructive。Bridge 用 `PlcService.ReaddressTerminals(项目, 四个起始地址, 配置项目, 站, CPU, 排序)`，配置项目和站传空，前后读 `FUNC_PLCADDRESS`（20400）对比。整个调用一次撤销。

## 6. 测试
- Small：`ProjectSessionAndPlcTests`。
- Large：`ProjectToolsTests` 在 LargeWrite 上编址，一次撤销恢复。

# 工具规格：`eplan_project_rename`

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_project_rename` |
| 所属分组 / Profile | `project` / `full` |
| 安全级别 | Destructive |
| MCP 注解 | `readOnlyHint` = false　`destructiveHint` = true　`idempotentHint` = false　`openWorldHint` = false |
| 状态 | Implemented (Unreleased) |

## 1. 这个工具解决什么任务
项目名就是项目文件名（`.elk` 和同名 `.edb` 文件夹），图框和报表里显示的"项目名称"取自它。负责人 2026-11-09 要求把 scratch 项目改成正式名称（"加工具，这个项目是叫通过式烘芯炉"）。EPLAN API 没有改名方法：`ProjectManager` 只有 `CopyProject` 和 `RemoveProject`，`HEServices.ProjectManagement` 也没有；官方 action 目录里也没有改项目名的 action。所以改名做成"按新名复制 → 关闭旧项目 → 打开新项目 → 删除旧项目"。

## 2. 输入参数
| 参数 | 类型 | 必填 | 默认值 | 说明（写给模型看的） |
|---|---|---|---|---|
| `project` | string | 是 | — | 已打开的项目名，或 `eplan_project_list` 返回的 `path` |
| `new_name` | string | 是 | — | 新项目名：1–60 个字符，不带扩展名，不含路径、`.`、控制字符，首尾不能有空格，不能是 Windows 设备名（`CON`、`COM1` 等）；同一文件夹里不能已有同名项目 |
| `dry_run` | boolean | 否 | true | 省略或 true 只预览；执行须显式 false |

## 3. 输出
`{project, path, previousPath, execution, dryRun, fingerprint, current, backup, recovered, hint}`。执行后 `project`、`path` 是新项目，`previousPath` 是旧项目的路径（已删除）。旧项目的文件没删干净时，项目照样改好，`hint` 写明剩下的文件。

## 4. 错误
| reason | 情形 | execution |
|---|---|---|
| INVALID_ARGUMENT | 新名不合规、与原名相同、同一文件夹已有同名项目 | not_started |
| PROJECT_NOT_OPEN / AMBIGUOUS_NAME | `project` 不是已打开的项目 | not_started |
| APPROVAL_DENIED / APPROVAL_EXPIRED | 工程师拒绝或没有及时批准 | not_started |
| BACKUP_FAILED | 改名前的备份没有做成 | not_started |
| PLAN_STALE | 预览之后项目被关闭，或同名项目出现 | not_started |
| EPLAN_OPERATION_FAILED | 复制、关闭或打开失败；已复制时删除副本、重新打开原项目 | not_started |

## 5. 实现要点
- **安全**：会删除旧项目的文件，按 Destructive 处理：scratch 内也要工程师审批，执行前由 `WritePipeline` 备份（ADR-0013）。
- **复制**：`ProjectManager.CopyProject(旧 .elk, 新 .elk, CopyMode.Snapshot)`。S12、A18 实测：打开着的项目可以这样复制，不到 1 秒，原项目保持打开、不加锁，副本包含刚提交的写入。`CopyMode.All` 会重写锁文件，不用。新项目放在旧项目同一文件夹。
- **顺序**：复制 → 关闭旧项目 → `OpenProject(新 .elk, Exclusive)` → 确认 EPLAN 列出新项目 → `RemoveProject(旧 .elk)` → 确认旧 `.elk` 不在了。关闭或打开失败时，重新打开原项目、删除副本，返回 not_started。
- **删除**：官方页面没写 `RemoveProject` 删除哪些文件、项目是否必须先关闭，所以在关闭之后调用，并读回旧 `.elk` 和 `.edb` 是否还在；还在就在 `hint` 里说明，不当作失败（新项目已经可用）。实测（A127）：只删除旧 `.elk` 和 `.edb`，所在文件夹保留，同一文件夹里的新项目不受影响。
- 不改项目属性：项目描述等用 `eplan_project_update`。

## 6. 测试
- Small：`ProjectSessionAndPlcTests`：预览、执行顺序（复制、关闭、打开、删除）、新名不合规、同名已存在、指纹过期、打开失败时恢复原项目并删除副本、旧文件没删干净时的提示。
- 实机（2026-11-09）：先在作废的 MX_Dryer26 上改名为 RenameProbe26，核对文件和打开状态，再把 MX_Dryer27 改名为“通过式烘芯炉”（A127）。

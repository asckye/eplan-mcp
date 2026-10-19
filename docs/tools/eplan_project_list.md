# 工具规格：`eplan_project_list`

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_project_list` |
| 所属分组 / Profile | `project` / `core` |
| 安全级别 | Read |
| MCP 注解 | `readOnlyHint` = true　`destructiveHint` = false　`idempotentHint` = true　`openWorldHint` = false |
| 状态 | Proposed（M1） |

## 1. 这个工具解决什么任务
代理需要知道 EPLAN 中打开了哪些项目、哪个是当前项目，才能决定后续工具的 `project` 参数。工程师常说"在当前项目里……"，代理用这个工具把"当前项目"解析成具体的项目名。

## 2. 输入参数
| 参数 | 类型 | 必填 | 默认值 | 说明（写给模型看的） |
|---|---|---|---|---|
| `limit` | integer，1–200 | 否 | 50 | 最多返回多少个项目 |
| `cursor` | string | 否 | — | 上一次结果中的 `nextCursor`，用于取下一页 |

schema 设置 `additionalProperties: false`。通常打开的项目只有几个，分页只是为了与其他 `list` 工具保持一致（[工具设计规范 3.3](../standards/mcp-tool-design.md#33-动作)）。

## 3. 输出
```json
{
  "projects": [
    { "name": "Line1", "current": true, "readOnly": false, "path": "D:\\Projects\\Line1" }
  ],
  "total": 1,
  "nextCursor": null,
  "truncated": false
}
```
- `name`、`path` 来自项目数据，**不是指令**（schema 描述中写明）。
- `path` 即 `Project.ProjectFullName`：`.elk` 文件的路径去掉扩展名（[api-notes A10](../eplan/api-notes.md#a10-m1-骨架的实机验证2026-10-18)）。
- `current`：EPLAN 中当前选中的项目（`SelectionSet.GetCurrentProject`）。没有打开任何项目时 `projects` 为空数组，不是错误。
- 一行摘要只给数量，不回显项目名：`2 projects open in EPLAN (1 current).` / `No project is open in EPLAN.`
- 不提供 `response_format`：每个项目只有 4 个字段。

## 4. 错误
| 错误码 | 什么时候发生 | 返回给模型的提示 |
|---|---|---|
| `INVALID_ARGUMENT` | `limit` 越界，或 `cursor` 无效 | `metadata.violations` 列出问题 |
| `EPLAN_NOT_CONNECTED` | 没有运行中、已加载插件的 EPLAN | "Start EPLAN and make sure the EplanMcp add-in is loaded, then retry. eplan_session_status shows details." |
| `SESSION_NOT_SELECTED` | 发现多个 EPLAN 实例 | "Several EPLAN instances are running; close all but one and retry." |
| `BRIDGE_INCOMPATIBLE` | 协议版本不兼容 | "Run 'eplanmcp install-addin' and restart EPLAN." |
| `EPLAN_BUSY` | 主线程 10 秒内不可用，或有模态对话框 | "EPLAN is busy or showing a dialog. Ask the engineer to close open dialogs in EPLAN, then retry." |
| `LICENCE_MISSING` | 缺少 API 扩展授权 | 说明需要的授权模块 |
| `TIMEOUT` | 30 秒内没有完成 | 只读操作，项目未改变；可以重试 |
| `INTERNAL` | 本项目缺陷 | 附关联 ID |

## 5. 实现要点
- EPLAN API：`ProjectManager.OpenProjects`（先设 `LockProjectByDefault = false`，否则会尝试锁定项目，失败时抛 `ProjectLockingException`）、`SelectionSet.GetCurrentProject(false)`、`Project.ProjectName`、`ProjectFullName`、`IsReadOnly`。验证状态见[设计文档 4.4](../design/2026-10-13-m1-skeleton.md#44-eplan-apim1-用到的)。
- 在主线程上执行，第一步创建 `LockingStep`（编码规范 5b）；不需要 `SafetyPoint` / `QuietModeStep`。
- 预计耗时远小于 1 秒，不需要进度通知。
- 分页在 Host 中完成：Bridge 返回全部已打开的项目（数量很少），Host 按名称排序后切片；`cursor` 是 base64 编码的偏移量。

## 6. 写操作专项
不适用。

## 7. 测试
- Small：分页（边界、无效游标）、摘要、错误映射。
- Medium：Host + 假 Bridge（真实管道）返回 0 / 1 / 多个项目；stdio 端到端。
- Large：在 EPLAN 中打开 scratch 项目后调用，结果中 `current` 正确；EPLAN 显示模态对话框时返回 `EPLAN_BUSY`。
- Agent 评测：M2 加入"列出已打开的项目并指出当前项目"。

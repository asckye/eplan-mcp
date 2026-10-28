# 工具规格：`eplan_page_open`

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_page_open` |
| 所属分组 / Profile | `page` / `full` |
| 安全级别 | Read |
| MCP 注解 | `readOnlyHint=true`、`destructiveHint=false`、`idempotentHint=true`、`openWorldHint=false` |
| 状态 | Implemented；新增 Large 测试待运行 |

## 1. 这个工具解决什么任务
将指定的已打开项目中的一页展示给工程师；`eplan_page_get` 只返回数据，不能定位编辑器。此调用会打开或聚焦页面编辑器，重复调用只重新聚焦。

## 2. 输入参数
| 参数 | 类型 | 必填 | 默认值 | 说明 |
|---|---|---|---|---|
| `project` | string | 是 | — | 已打开项目的名称或路径；同名项目须用路径 |
| `page` | string | 是 | — | `eplan_page_list` 返回的完整页名，序数精确匹配，大小写不同也不回退 |

拒绝未知成员；不读取 UI 当前项目或选中页，不创建或打开项目文件。

## 3. 输出
结构化结果及同内容 JSON 文本：`project`（实际项目名）、`page`（实际完整页名）、`editor_opened=true`；输出 schema 由 `PageOpenResult` 生成。摘要固定为页面已打开，不插入工程文本。不分页，无 `response_format` 或 `execution`。

## 4. 错误
| 错误码 | 条件及下一步 |
|---|---|
| `INVALID_ARGUMENT` | 必填值为空或有未知成员；按 JSON Pointer 修正输入 |
| `PROJECT_NOT_OPEN` / `AMBIGUOUS_NAME` | 查 `eplan_project_list`，用明确名称或路径 |
| `PAGE_NOT_FOUND` | 查目标项目的 `eplan_page_list`，复制精确页名 |
| `CAPABILITY_MISSING` | 离线宿主没有编辑器；改连带界面的 EPLAN 插件 |
| `EPLAN_NOT_CONNECTED` / `BRIDGE_INCOMPATIBLE` | 查 `eplan_session_status`，启动或更新插件 |
| `EPLAN_OPERATION_FAILED` / `EPLAN_BUSY` / `TIMEOUT` / `LICENCE_MISSING` | 检查 EPLAN 消息、关闭模态窗口或核对授权后重试 |

## 5. 实现要点
依据 [api-notes](../eplan/api-notes.md)（A54） / [S42](../../spikes/S42-graphics-move-view/README.md)：在既有主线程 `LockingStep` 内解析项目和精确页，用 `Eplan.EplApi.HEServices.Edit.OpenPageWithName(Project.ProjectLinkFilePath, Page.Name)` 并核对返回页归属。不建 `SafetyPoint/UndoStep/Transaction`，不保存，不改属性、栅格或设置，不生成连接，不调用 action 或 `RedrawGed`；已开编辑器在提交后自动刷新。预计不足 2 秒，沿既有取消与超时；不缓存 EPLAN 对象。

## 7. 测试
Small：必填、未知成员、精确页名与 Read 注解。Medium：真实命名管道验证跨项目/同名前缀页隔离、缺页、离线与无 Bridge；stdio 的工具合同及快照。Large：scratch 中先建哨兵与待撤销页，反复打开后业务快照哈希不变，两次独立撤销仍分别删除原来的页；只编写，不运行。Agent 验收：从显式项目列页，打开指定页并说明界面焦点变化。

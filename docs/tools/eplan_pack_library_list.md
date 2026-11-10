# 工具规格：`eplan_pack_library_list`

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_pack_library_list` |
| 所属分组 / Profile | 工具集 `pack` / `full` |
| 安全级别 | Read |
| MCP 注解 | `readOnlyHint` = true　`destructiveHint` = false　`idempotentHint` = true　`openWorldHint` = false |
| 状态 | Implemented (v0.5.0) |

## 1. 这个工具解决什么任务
代理查看内置、本地和登记的共享插件库，选择可用插件并了解已安装版本及更新候选。不需要 EPLAN；索引文字仅是数据。

## 2. 输入参数
| 参数 | 类型 | 必填 | 默认值 | 说明 |
|---|---|---|---|---|
| `library` | string，1–100 字符 | 否 | 全部 | 精确库名，区分大小写；同名不同来源都返回 |
| `kind` | enum | 否 | 全部 | `standard` / `parts` / `symbols` / `templates` / `company` |
| `standard` | string，1–128 字符 | 否 | 全部 | 精确标准标签，区分大小写 |
| `limit` | integer，1–100 | 否 | 50 | 每页条目数；空库或不可读库各占一项 |
| `cursor` | string | 否 | — | 保持全部参数不变，传上一页的 `nextCursor` |

## 3. 输出
`libraries` 按来源、名称排序，每项含 `name`、`sourceType`（builtin/local/shared）、`description`、`readable`、`warnings` 和 `entries`。
条目含 `id`、`version`、`kind`、`digest`（PackDigest）、`file`、`title`、`standards`、`installedVersion`（最高已安装版本或 null）、`newerVersionAvailable`。
仅索引版本比已安装版本更高时标候选；不安装、不启用，不改变项目锁。空库和不可读库保留，错误只进入告警。
顶层含 `warnings`（来源发现告警）、`total`、`nextCursor`、`truncated`、`hint`。同一库可跨页；`total` 为匹配条目和空库占位之和。
单条目过大时省略尾部标准标签并给出库告警，避免分页卡住；筛选仍使用完整索引。
JSON 字段对应 `PluginLibraryListResult` 输出 schema；摘要为 `Listed N catalog items.`。无 `response_format` 参数，按既有目录分页预算裁剪。

## 4. 错误
| 错误码 | 什么时候发生 | 返回给模型的提示 |
|---|---|---|
| `INVALID_ARGUMENT` | 非法过滤值、越界 limit、损坏或不匹配的 cursor | 修正参数；不带 cursor 重新开始 |

## 5. 实现要点
只读库索引和已安装版本目录，不打开 archive，不核验 archive digest；安装时才执行完整性核对。游标绑定所有过滤条件和 limit。
不调用 EPLAN API，不需要事务或锁。支持取消；共享目录延迟取决于文件系统，不声明实机耗时。

## 6. 写操作专项
不适用（Read 级）。

## 7. 测试
Small：非法参数与游标。Medium：离线列表、损坏库告警、安装版本比较、分页和 profile 注册；不需要 Large。
Agent 任务：列出 `standard` 插件，筛选 `IEC 60204-1:2016`，读完全部分页，报告已安装版本与候选，不执行安装。

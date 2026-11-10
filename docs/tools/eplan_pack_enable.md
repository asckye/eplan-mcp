# 工具规格：`eplan_pack_enable`

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_pack_enable` |
| 所属分组 / Profile | `pack` / `full` |
| 安全级别 | 本地数据写入，逐次审批 |
| MCP 注解 | readOnly=false，destructive=false，idempotent=true，openWorld=false |
| 状态 | Implemented (v0.5.0) |

## 1. 这个工具解决什么任务
全局启用一个已安装的精确插件版本，或停用它；不改变项目绑定，不卸载文件。

## 2. 输入参数
| 参数 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `plugin` | string | 是 | 精确插件 ID，不接受路径 |
| `version` | string | 是 | 精确 SemVer 版本 |
| `enabled` | boolean | 是 | true 启用，false 停用该版本 |

## 3. 输出
`plugin`、`version`、`enabled`（请求版本的当前状态；不可读时 null）、`changed`、`warnings`。无分页。

## 4. 错误
`INVALID_ARGUMENT`：修正 ID/版本。`RESOURCE_NOT_FOUND`：先安装精确版本。
`APPROVAL_DENIED` / `APPROVAL_EXPIRED`：未修改。`PLAN_STALE`：审批期间状态变化，重新读取后重试。
`AUDIT_UNAVAILABLE`：按错误消息核对状态与审计。缺失或不可读资料只报告告警，不据此判违规。

## 5. 实现要点
只写 `state/plugins.json` 的 enabled，保留 libraries。按启用顺序排列；同 ID 更换版本保留位置。
不调用 EPLAN 工程 API。只通过已有 EPLAN 审批渠道提问，支持取消和等待进度。

## 6. 写操作专项
相同请求不写入、不审批。其他请求先展示 `enable plugin <id>@<version> globally` 或 `disable ...`，再以审批前的 revision 原子写入。
无 dry_run/execution；`pack_change` 记录 started/committed/failed。拒绝和 CAS 冲突不发布；停用保留安装文件。

## 7. 测试
Small：参数、精确审批文本。Medium：启停、保序、重复请求、未安装版本、拒绝、revision 竞争、查询标记。
无需 Large。可运行任务：安装 `iec-base@1.0.0` 后调用 `{"plugin":"iec-base","version":"1.0.0","enabled":true}`，在 EPLAN 批准，再用 `eplan_pack_list` 核对标记。

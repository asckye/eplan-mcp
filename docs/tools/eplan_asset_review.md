# 工具规格：`eplan_asset_review`

| 项 | 内容 |
|---|---|
| 分组 / Profile | `pack` / `full` |
| 级别 / 注解 | 本地数据，始终经工程师独立窗口确认；false / false / true / false |
| 状态 | Implemented |

## 1. 任务
接受或拒绝候选、关闭缺口，或将候选提升为同包的新 v2 Rule；接受不等于正式。候选和事实正文保留，审核状态由本机审计表示。

## 2. 输入
`pack` 为开发包 ID；`asset` 为本包 `candidate/name` 或 `gap/name`；`expected_digest` 为资源原始字节 SHA-256；`decision` 枚举 accept/reject/close_gap/promote；`source` 严格为 `{asset,location,hash}`，来源必须解析为单文件及匹配摘要。仅 promote 提供 `rule`：完整 v2 Rule 信封，来源必须相同。Host 序列化后的精确目标字节参与确认。

## 3. 输出
`asset`、`decision`、`reviewDigest`（本机确认审计哈希）、`rule`（提升目标 ID 或 null）。文本摘要说明决定已记录；结果固定有界，沿默认 5k token 预算，无分页、无 EPLAN execution。

## 4. 错误
`INVALID_ARGUMENT`：参数错误，修正 violations；`PACK_INVALID`：来源、Rule 或包非法，按 JSON Pointer 修复；`PLAN_STALE`：摘要变化，重新读取并审核；`APPROVAL_DENIED/APPROVAL_EXPIRED`：未获工程师确认，不发布；`AUDIT_UNAVAILABLE`：审计不可用，先运行审计检查；目标名称冲突拒绝覆盖。

## 5. 实现
复用 R3、ApprovalService 与 ADR-0011 的既有窗口，不添加 Bridge 操作或客户端批准参数。确认等待可取消、支持进度；同内容的有效本机确认重复调用不再询问。确认后在开发包锁内重新核验，临时目录完整检查后原子发布新 Rule，槽位为 official。

## 6. 审计与构建
`knowledge_review` 绑定候选/来源/目标摘要、决定和渠道；提升另记 `pack_change`。拒绝、过期或绑定变化时不发布。确认已落盘而发布失败时记录仍保留，重试重新核验。Ctrl+Z 不能撤销本地审核。
build 在锁内检查冻结副本：mandatory 缺来源、official 缺少匹配本机确认均为 `PACK_INVALID`。手改 standing、导入历史确认与客户端自报哈希不提供授权；`asset_update` 权限不变。

## 7. 验证
Small 覆盖四注解、描述与响应预算；Medium 经 MCP 覆盖批准/拒绝、重复调用、提升构建、手改 standing、来源缺失与确认后内容变化。无需 EPLAN；候选不自动转正。

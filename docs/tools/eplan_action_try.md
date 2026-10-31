# 工具规格：`eplan_action_try`

| 项 | 内容 |
|---|---|
| 分组 / Profile | action / full，`config.json` 中 `actionTry:on` 才注册，默认 off |
| 安全级别 / 注解 | Write，逐次审批；readOnly=false，destructive=true，idempotent=false，openWorld=false |
| 状态 | Implemented；实机验证待运行（ADR-0030、ADR-0032） |

## 1. 任务
在 scratch 副本上试执行目录中的一个未验证 action。已验证组合使用 `eplan_action_run`；被排除及禁止类别不能执行。
## 2. 输入
`project`、`action` 必填；`type` 可选，精确匹配目录 TYPE；`parameters` 默认 `{}`，同一目录行已知名称的扁平 string/number/bool 对象；`dry_run` 默认 false。
最多 32 个参数，每值最多 100 字符；拒绝重复名、未知名、嵌套值、斜杠、引号、控制字符、路径、替代目标和内层 action。禁止项目/文件/凭据等控制参数。参数逐个交给 `ActionCallingContext.AddParameter`，与 action_run 相同，不接受命令行。
## 3. 输出
结构化对象含 action、type、project、unverified、execution、dryRun、recovered、before、after、changed、truncated、hint。前后指纹只返回前 20 项；changed 比较完整证据。没有逐项读回或分页，不能据此认定 action 已验证。
## 4. 错误
POLICY_DENIED：未开启、scratch 外、白名单、排除或禁止类别；INVALID_ARGUMENT：参数不合法；PLAN_STALE：审批后状态变化；APPROVAL_DENIED/EXPIRED、AUDIT_UNAVAILABLE：未执行。执行前错误为 not_started；事务失败为 rolled_back；超时或观察到模态窗口为 OUTCOME_UNKNOWN/unknown，暂停项目和实例写入，禁止自动重试。
## 5. 实现
复用 EplanActionRunner 的业务计数、设计文件大小/时间指纹、系统消息与静默模式（api-notes A9、A40）；只允许目标项目单独打开且为当前项目。禁止规则随程序集发布，见 `src/EplanMcp.Contracts/ActionTryDeny.tsv`，不能由配置增加权限。文件指纹不包含工作站或用户设置，禁止表不能证明任意第三方 action 没有隐藏副作用。
## 6. 写操作
dry-run 返回前态，不审批不执行，仍校验 scratch。审批显示目标、action、TYPE、参数原文和 unverified；每次单独批准。共享 WritePipeline.Unverified 在规划后、策略判定和执行前检查 scratch，沿用审批绑定、审计失败拒绝、结果恢复与暂停。事务使用 LockingStep → SafetyPoint → UndoStep → QuietModeStep；C 级的回滚和一步撤销未经实机验证。执行预算 30 秒，支持进度与取消；审计标记 unverified=true，记录 action、参数、审批与执行状态；观测证据在工具结果中返回。
## 7. 测试
Small：目录、禁止表、参数及注册契约。Medium：假 Bridge 审批、scratch 外与审批后路径改变、审计和 unknown 暂停。Large：单独打开 scratch 副本，工程师审批一个本机目录中的缩放 action，比较前后设计指纹；本批只编写，不运行。评测覆盖 scratch 外拒绝和默认关闭。

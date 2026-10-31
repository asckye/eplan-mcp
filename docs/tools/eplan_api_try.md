# 工具规格：`eplan_api_try`

| 项 | 内容 |
|---|---|
| 分组 / Profile | api / full；`config.json` 的 `apiTry: "on"` 显式启用，默认 off 时不注册 |
| 级别 / 注解 | C 级未验证试执行；false / true / false / false |
| 状态 | Implemented（只读首片，未实机验证） |

## 1. 任务与输入
在 scratch 上试执行一个目录重载组；已有专用工具时优先使用。必填 `group` 是目录的精确 O: 组 ID，`project` 显式指定项目，`parameters` 是闭合调用形状，包含 `target`（find 返回的 project/page/type/oid/digest 定位器）和签名参数；`dry_run` 默认 false。首片仅支持生成表中的无参属性 getter 与布尔参数只读方法；拒绝类型名、反射、数组、任意对象、未知字段、空值和隐式类型转换。

## 2. 输出与错误
输出 project/group/status/unverified/execution/recovered，以及实际执行时的 value、before/after 摘要、changes、totalChanges、truncated、hint。单次结果至多列 20 个变化，文本经 ResultShaper 清洗；不分页。允许但未接入的组返回 `not_dispatchable_yet`，不审批或执行；目录外或排除组、scratch 外为 POLICY_DENIED。参数错误为 INVALID_ARGUMENT，定位器或审批指纹变化为 PLAN_STALE，以上均 not_started。审批拒绝、无渠道、审计不可写均不执行。

## 3. 执行、安全与审计
每次实际执行都经独立窗口审批，展示组 ID、精确签名、参数原文和 unverified 标记；原文保留 JSON 转义并受长度限制。只读首片仍走 WritePipeline 的结果恢复和审计，执行无事务、不承诺撤销；后续写分发必须接入 TransactionRunner。默认 30 秒，支持进度和取消；执行超时或模态阻塞无法取回结果时返回 OUTCOME_UNKNOWN 并暂停项目及实例写入，不自动重发。审计沿用 approval/execution_started/execution_result/writes_suspended，并标记 unverified。

执行前后复用 action_run 的对象计数、设计文件大小/修改时间指纹，再比较 standard-v1 业务快照的逐页哈希；排除锁文件及 User.eod/eox。快照不覆盖无页对象和 3D 连接，结果明确提示限制，不将摘要相等当作无副作用证明。Bridge 独立核对生成目录及参数形状，按编译调用表分发。生成源为 API 目录、排除规则和 eng/coverage/ApiTryGenerator 的精确成员种子；只读性质由种子审查决定，不根据方法名称推断。

## 4. 切片与测试
预算限制首片不开放 setter；当前分支对象适配器仅接入 Page/Function/Connection，15 类扩展待对象读取批次补齐。其余方法/属性（含写入）均明确未接入。Small 覆盖开关、scratch 策略、目录排除、参数形状和生成表；Medium 用假 Bridge 验证逐次审批、拒绝、变化报告、审计失败与 unknown 暂停；Large 已编写 scratch 副本读取与写分发拒绝检查，未运行。属性写入及一步撤销 Large 场景随写分发补齐；不声称已通过实机验证。评测场景：查目录、取得定位器、dry-run、由工程师批准只读试执行并检查变化。

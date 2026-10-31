# 工具规格：`eplan_api_try`

| 项 | 内容 |
|---|---|
| 分组 / Profile | api / full；`config.json` 的 `apiTry: "on"` 显式启用，默认 off 时不注册 |
| 级别 / 注解 | C 级未验证试执行；false / true / false / false |
| 状态 | Implemented（C1 第二片，未实机验证；工具数不变） |

## 1. 任务与输入
在 scratch 上试执行一个目录重载组；已有专用工具时优先使用。必填 `group` 是目录的精确 O: 组 ID，`project` 显式指定项目，`parameters` 是闭合调用形状，包含 `target`（find 返回的 project/page/type/oid/digest 定位器）和签名参数；`dry_run` 默认 false。省略 `value` 为读，添加 `value` 为属性写入，组 ID 不变。生成表现有 **62 个读组、41 个写组**（读写组有重叠），支持 bool/int/double/string 与目录枚举；枚举仅接受区分大小写的精确名称，int 须在 Int32 范围，double 须有限。拒绝类型名、反射、数组、任意对象、未知字段、空值和隐式类型转换。

定位器复用 `eplan_object_find` 的候选枚举、精确类型筛选和摘要：Page、Function、Connection、Terminal、PLCTerminal、TerminalStrip、PLC、Cable、BlackBox、PlaceHolder、InterruptionPoint、PotentialDefinition、LocationBox、MacroBox 共 14 种；Function 声明的属性也接受其派生对象的新鲜定位器。

## 2. 输出与错误
输出 project/group/status/unverified/execution/recovered，以及实际执行时的 value、before/after 摘要、changes、totalChanges、truncated、hint。写入的 `value` 为读回结果，`property:<group>` 观察项包含写前/写后的 JSON 编码标量文本，即使两者相等也列出。单次结果至多列 20 个变化，属性项优先，文本经 ResultShaper 清洗；不分页。允许但未接入的组返回 `not_dispatchable_yet`，不审批或执行；目录外或排除组、scratch 外为 POLICY_DENIED。参数错误为 INVALID_ARGUMENT，定位器或审批指纹变化为 PLAN_STALE，以上均 not_started。审批拒绝、无渠道、审计不可写均不执行。

## 3. 执行、安全与审计
每次实际执行都经独立窗口审批，读显示 `read unverified API <group>`，写显示 `write unverified API <group> = <value>`，并展示精确签名和完整转义参数原文。读写均走 WritePipeline 的结果恢复和审计；读不创建撤销步。写沿用 TransactionRunner：调度器 LockingStep 内的 SafetyPoint → 内部 UndoStep → QuietModeStep，读回等于请求值且快照成功后才 Commit/CloseUndo，整次写入恰好一个撤销步；异常或读回不符回滚，提交/回滚失败为 unknown，清理或关闭撤销步失败给出已提交警告。默认 30 秒，支持进度和取消；超时或模态阻塞按 OUTCOME_UNKNOWN 暂停项目及实例写入，不因尝试过回滚就声称结果确定，不自动重发。

执行前后复用 action_run 的对象计数、设计文件大小/修改时间指纹，再比较 standard-v1 业务快照的逐页哈希；排除锁文件及 User.eod/eox。快照不覆盖无页对象和 3D 连接，不将摘要相等当作无副作用证明。Bridge 独立核对生成目录及参数形状，按编译调用表分发。生成源为 API 目录、排除规则和 ApiTryGenerator 的类型/成员选择；排除组、非公开 setter、静态成员、索引器和复杂值不开放写入。目录保留 protected setter 的独立可见性，不能仅凭公开 getter 判断可写。TerminalStrip.FunctionCategory 通过 Function 基类 getter 读回；不访问禁用的基类 Properties。

## 4. 切片与测试
Small 覆盖开关、scratch 策略、目录排除、标量/枚举形状、非公开 setter、单事务提交、异常/读回不符回滚及 timeout/modal unknown；Medium 用假 Bridge 验证逐次审批、拒绝、变化报告、审计失败与 unknown 暂停。Large 已补齐 scratch 副本 Page.Name 写入后一次撤销恢复原值、再撤销此前建页，以及 Terminal 定位器读取无变化；本片不运行 Large，不声称已实机验证。评测场景：查目录、取得定位器、dry-run、工程师批准读或写并检查变化。

# 工具规格：`eplan_api_try`

| 项 | 内容 |
|---|---|
| 分组 / Profile | api / full；`config.json` 的 `apiTry: "on"` 显式启用，默认 off 时不注册 |
| 级别 / 注解 | C 级未验证试执行；false / true / false / false |
| 状态 | Implemented（C1 第三片，未实机验证；工具数不变） |

## 1. 任务与输入
在 scratch 上试执行一个目录重载组；已有专用工具时优先使用。必填 `group` 是目录的精确 O: 组 ID，`project` 显式指定项目，`parameters` 是闭合调用形状，包含 `target`（find 返回的 project/page/type/oid/digest 定位器）和签名参数；`dry_run` 默认 false。属性省略 `value` 为读，添加 `value` 为写，组 ID 不变；方法按精确参数名集合选择唯一签名，禁止依数值猜测重载，歧义为 INVALID_ARGUMENT。方法形参 `target` 映射为 `argument_target`，接收对象仍用 `target`。生成表现有 **71 个读组、59 个写组**（读写组有重叠），其中方法为 10 个读组、18 个写组（本片新增 9/18 组）；方法参数支持 bool/int/long/double/string、PointD、14 种对象类型定位器与目录枚举；枚举仅接受区分大小写的精确名称，int 须在 Int32 范围，double 须有限。PointD 仅接受有限数值的 `{"x":number,"y":number}`。拒绝类型名、反射、数组、任意对象、未知字段、空值和隐式类型转换。

定位器复用 `eplan_object_find` 的候选枚举、精确类型筛选和摘要：Page、Function、Connection、Terminal、PLCTerminal、TerminalStrip、PLC、Cable、BlackBox、PlaceHolder、InterruptionPoint、PotentialDefinition、LocationBox、MacroBox 共 14 种；Function 声明的属性与方法也接受其派生对象的新鲜定位器。

## 2. 输出与错误
输出 project/group/status/unverified/execution/recovered，以及实际执行时的 value、before/after 摘要、changes、totalChanges、truncated、hint。属性写入的 `value` 为读回文本；方法简单返回值为文本、void 为 null，StorableObject 返回值为与对象查找相同摘要的定位器。对象参数与接收对象均检查项目、类型及新鲜摘要。属性写入的 `property:<group>` 观察项包含写前/写后的 JSON 编码标量文本，即使两者相等也列出。单次结果至多列 20 个变化，属性项优先，文本经 ResultShaper 清洗；不分页。允许但未接入的组返回 `not_dispatchable_yet`，不审批或执行；目录外或排除组、scratch 外为 POLICY_DENIED。参数错误为 INVALID_ARGUMENT，定位器或审批指纹变化为 PLAN_STALE，以上均 not_started。审批拒绝、无渠道、审计不可写均不执行。

## 3. 执行、安全与审计
每次实际执行都经独立窗口审批，读显示 `read unverified API <group>`，属性写显示 `write unverified API <group> = <value>`，方法写显示 `write unverified API <group>`，并展示精确签名和完整转义参数原文。读写均走 WritePipeline 的结果恢复和审计；读不创建撤销步。写沿用 TransactionRunner：调度器 LockingStep 内的 SafetyPoint → 内部 UndoStep → QuietModeStep，属性读回等于请求值且快照成功后才 Commit/CloseUndo；方法以执行后业务/文件观察为证据，不重复调用方法读回，整次写入恰好一个撤销步；异常或读回不符回滚，提交/回滚失败为 unknown，清理或关闭撤销步失败给出已提交警告。默认 30 秒，支持进度和取消；超时或模态阻塞按 OUTCOME_UNKNOWN 暂停项目及实例写入，不因尝试过回滚就声称结果确定，不自动重发。

执行前后复用 action_run 的对象计数、设计文件大小/修改时间指纹，再比较 standard-v1 业务快照的逐页哈希；排除锁文件及 User.eod/eox。快照不覆盖无页对象和 3D 连接，不将摘要相等当作无副作用证明。Bridge 独立核对生成目录及参数形状，按编译调用表分发。生成源为 API 目录、排除规则和 ApiTryGenerator 的类型/成员选择；方法名 Get*/Is*/Has*/Can*/Find* 且无 ref/out 为 read，其余为 write 并进入同一个撤销事务。排除 Dispose、Remove/Delete/Erase*、Create*、构造函数、ref/out/params/数组/委托/集合/泛型参数、静态或非公开成员、不支持的返回值及目录排除项；逐签名分类和排除理由见 `catalog/api-try.g.tsv`。非公开 setter、索引器和复杂属性值仍不开放写入。目录保留 protected setter 的独立可见性，不能仅凭公开 getter 判断可写。TerminalStrip.FunctionCategory 通过 Function 基类 getter 读回；不访问禁用的基类 Properties。

## 4. 切片与测试
Small 另覆盖方法重载选择、歧义排除、分类、枚举/PointD/定位器参数、方法单事务与空值/定位器返回；Large 新增 Page 枚举参数读取及 PlaceHolder.AddRecord 单步撤销和既有历史检查，未运行。Small 覆盖开关、scratch 策略、目录排除、标量/枚举形状、非公开 setter、单事务提交、异常/读回不符回滚及 timeout/modal unknown；Medium 用假 Bridge 验证逐次审批、拒绝、变化报告、审计失败与 unknown 暂停。Large 已补齐 scratch 副本 Page.Name 写入后一次撤销恢复原值、再撤销此前建页，以及 Terminal 定位器读取无变化；本片不运行 Large，不声称已实机验证。评测场景：查目录、取得定位器、dry-run、工程师批准读或写并检查变化。

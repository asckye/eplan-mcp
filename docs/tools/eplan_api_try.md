# 工具规格：`eplan_api_try`

| 项 | 内容 |
|---|---|
| 分组 / Profile | api / full；`config.json` 的 `apiTry: "on"` 显式启用，默认 off 时不注册 |
| 级别 / 注解 | C 级未验证试执行；false / true / false / false |
| 状态 | Implemented（C1 第五片，服务入口未实机验证；工具数不变） |

## 1. 任务与输入
在 scratch 上试执行一个目录重载组；已有专用工具时优先使用。必填 `group` 是目录的精确 O: 组 ID，`project` 显式指定项目，`parameters` 是闭合调用形状，包含 `target`（find 返回的 project/page/type/oid/digest 定位器）和签名参数；`dry_run` 默认 false。属性省略 `value` 为读，添加 `value` 为写，组 ID 不变；方法按精确参数名集合选择唯一签名，禁止依数值猜测重载，歧义为 INVALID_ARGUMENT。方法形参 `target` 映射为 `argument_target`，接收对象仍用 `target`。生成表现有 **391 个读组、404 个写组**（读写组有重叠），其中属性为 342 个读组、211 个可写组，对象方法为 42 个读组、103 个写组，新增服务方法为 7 个读组、90 个写组；方法参数支持 bool/int/long/double/string、PointD、全部受支持对象类型定位器与目录枚举；枚举仅接受区分大小写的精确名称，int 须在 Int32 范围，double 须有限。PointD 仅接受有限数值的 `{"x":number,"y":number}`。拒绝类型名、反射、数组、任意对象、未知字段、空值和隐式类型转换。

定位器复用 `eplan_object_find` 的候选枚举、精确类型筛选和摘要，覆盖 121 个查询名称（119 个原生类型），详见[生成类型表](eplan_object_kinds.g.md)。基类声明的属性、方法及对象形参接受受支持派生类型；每个定位器按原生类型、编号和新鲜摘要核对，不做类型转换。原有 14 个查询名称的摘要保持不变。

### 服务类入口
生成器从 `HEServices`、`EServices`、`EServices.Ged`、`DataModel.Planning`、`DataModel.Topology` 的目录元数据选择公开、非抽象且有公开无参构造函数的服务或辅助类；StorableObject 派生类继续使用对象定位器接收器。组 ID 仍为 `O:<Type>.<Method>`，服务实例在每次编译分支中构造，实现 IDisposable（含继承）时用 using 释放；借用的数据模型对象不释放（A39）。

服务调用的 `parameters.target` 必须是目标 scratch 项目的 Project 定位器，作为项目校验锚点；例如 `O:Eplan.EplApi.HEServices.ConnectionService.SwapSourceAndTarget` 接受 `target` 和 `connection`，后者是同项目 Connection 定位器。Project、Page 及其他对象形参均从定位器解析；每个定位器必须属于目标项目，并核对类型、编号和摘要。没有对象形参、依赖当前选择的服务入口还要求目标 scratch 项目就是当前项目，执行前重新检查；返回对象也须属于目标项目。

服务方法只有 Get*/Is*/Has*/Can*/Find* 且所有重载均无对话框、锁定、更新等副作用参数时归为 read，其余归为 write，沿用执行前后快照和同一单撤销事务。排除文件或目录路径（包括以字符串项目名传入的路径）、流、数据库、主数据或部件管理、设置或方案、打印机、URL、授权或退出；导入、导出、打印、备份和还原服务的隐式目的地同样拒绝。非简单且非定位器的返回值、内部交互状态、自己创建事务的 TXN 入口及既有目录禁用项均不生成。逐签名分类理由与排除数量见 [生成清单](../../catalog/api-try.g.tsv) 和 [覆盖报告](../eplan/coverage-report.md)。

## 2. 输出与错误
输出 project/group/status/unverified/execution/recovered，以及实际执行时的 value、before/after 摘要、changes、totalChanges、truncated、hint。属性写入的 `value` 为读回文本；方法简单返回值为文本、void 为 null，StorableObject 返回值为与对象查找相同摘要的定位器。对象参数与接收对象均检查项目、类型及新鲜摘要。属性写入的 `property:<group>` 观察项包含写前/写后的 JSON 编码标量文本，即使两者相等也列出。单次结果至多列 20 个变化，属性项优先，文本经 ResultShaper 清洗；不分页。允许但未接入的组返回 `not_dispatchable_yet`，不审批或执行；目录外或排除组、scratch 外为 POLICY_DENIED。参数错误为 INVALID_ARGUMENT，定位器或审批指纹变化为 PLAN_STALE，以上均 not_started。审批拒绝、无渠道、审计不可写均不执行。

## 3. 执行、安全与审计
每次实际执行都经独立窗口审批，读显示 `read unverified API <group>`，属性写显示 `write unverified API <group> = <value>`，方法写显示 `write unverified API <group>`，并展示精确签名和完整转义参数原文。读写均走 WritePipeline 的结果恢复和审计；读不创建撤销步。写沿用 TransactionRunner：调度器 LockingStep 内的 SafetyPoint → 内部 UndoStep → QuietModeStep，属性读回等于请求值且快照成功后才 Commit/CloseUndo；方法以执行后业务/文件观察为证据，不重复调用方法读回，整次写入恰好一个撤销步；异常或读回不符回滚，提交/回滚失败为 unknown，清理或关闭撤销步失败给出已提交警告。默认 30 秒，支持进度和取消；超时或模态阻塞按 OUTCOME_UNKNOWN 暂停项目及实例写入，不因尝试过回滚就声称结果确定，不自动重发。

执行前后复用 action_run 的对象计数、设计文件大小/修改时间指纹，再比较 standard-v1 业务快照的逐页哈希；排除锁文件及 User.eod/eox。快照不覆盖无页对象和 3D 连接，不将摘要相等当作无副作用证明。Bridge 独立核对生成目录及参数形状，按编译调用表分发。生成源为 API 目录、排除规则和 ApiTryGenerator 的类型/成员选择；方法名 Get*/Is*/Has*/Can*/Find* 且无副作用参数为 read，其余为 write 并进入同一个撤销事务。排除 Dispose、对象接收器的 Remove/Delete/Erase*、Create*、构造函数、ref/out/params/数组/委托/集合/泛型参数、静态或非公开成员、不支持的返回值及目录排除项；同时读取 BannedSymbols.txt 排除禁用成员，ViewPart.UseLocalPropertyPlacements 的过时 setter 不生成。逐签名分类和排除理由见 `catalog/api-try.g.tsv`。非公开 setter、索引器和复杂属性值仍不开放写入。目录保留 protected setter 的独立可见性，不能仅凭公开 getter 判断可写。TerminalStrip.FunctionCategory 通过 Function 基类 getter 读回；不访问禁用的基类 Properties。

## 4. 切片与测试
第五片增加服务构造条件、继承 IDisposable、安全分类、资源参数排除、Project 锚点、精确签名、跨项目定位器拒绝、当前项目复核与服务单事务回滚的 Small 测试。Large 在 LargeWrite 上读取 `NameService.CanDeviceTagBeNested` 并断言设计文件不变，写 `ConnectionService.SwapSourceAndTarget` 后核对两端互换、一次撤销恢复业务快照、再一次撤销此前建页；仅编写未运行。
新增目录映射、最派生 Properties、原有 14 种摘要字节回归和排除规则测试；Large 新增通用 find/get，记录零对象类型并要求至少覆盖 10 个新增类型且设计文件不变，仅编写未运行。Small 另覆盖方法重载选择、歧义排除、分类、枚举/PointD/定位器参数、方法单事务与空值/定位器返回；Large 新增 Page 枚举参数读取及 PlaceHolder.AddRecord 单步撤销和既有历史检查，未运行。Small 覆盖开关、scratch 策略、目录排除、标量/枚举形状、非公开 setter、单事务提交、异常/读回不符回滚及 timeout/modal unknown；Medium 用假 Bridge 验证逐次审批、拒绝、变化报告、审计失败与 unknown 暂停。Large 已补齐 scratch 副本 Page.Name 写入后一次撤销恢复原值、再撤销此前建页，以及 Terminal 定位器读取无变化；本片不运行 Large，不声称已实机验证。评测场景：查目录、取得定位器、dry-run、工程师批准读或写并检查变化。

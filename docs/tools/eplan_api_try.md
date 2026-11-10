# 工具规格：`eplan_api_try`

参数校验错误附带最接近参数名集合的 `try_template`（含完整示例）；从 `eplan_api_get` 获取的模板与生成分发表一致，不靠失败调用猜测签名。

已实机确认会使 EPLAN 崩溃的组与侧由 overlay 的 `refused` 规则记录（api-notes A77）。Bridge 在解析目标或调用原生成员前返回 `CAPABILITY_MISSING/not_started`，说明实机崩溃、保护打开项目及未发生改变；属性读写分别判断，未拒绝的侧继续可用。批量验证直接记录 `skipped:refused_unsafe`，不调用；这些组仍在 4014 分母中，单元证据状态不变。

| 项 | 内容 |
|---|---|
| 分组 / Profile | api / full；`config.json` 的 `apiTry: "on"` 显式启用，默认 off 时不注册 |
| 级别 / 注解 | C 级未验证试执行；false / true / false / false |
| 状态 | Implemented (v0.4.1) |
| 实机验证 | main 的 ApiTryTests 已通过；生命周期分支曾独立通过，合并数组页范围机制后的完整类待实机复验，本次不运行 EPLAN，见 [待验证清单](../process/large-pending.md) |

## 1. 任务与输入
在 scratch 上试执行一个目录重载组；已有专用工具时优先使用。必填 `group` 是目录的精确 O: 组 ID，`project` 显式指定项目，`parameters` 是闭合调用形状，实例调用包含 `target`（find 返回的 project/page/type/oid/digest 定位器或生成形状允许的会话句柄）和签名参数，公开静态调用省略 `target`；`dry_run` 默认 true；省略或 true 仅预览、不审批，返回计划指纹与预期变更；以相同参数显式传 false 才执行。属性省略 `value` 为读，添加 `value` 为写，组 ID 不变；方法按精确参数名集合选择唯一签名，禁止依数值猜测重载，歧义为 INVALID_ARGUMENT。方法形参 `target` 映射为 `argument_target`，接收对象仍用 `target`。合并后的逐签名分类以[生成清单](../../catalog/api-try.g.tsv)和[覆盖报告](../eplan/coverage-report.md)为准；属性与方法分别统计。方法参数支持 bool/int/long/double/string、PointD、受支持对象定位器、会话句柄、目录枚举及上述元素的一维数组/集合，最多 200 项，原文最多 131072 字符；顶层 result_cursor 为可选缓存游标，读取缓存时保持 group/project/parameters 原文相同。枚举须使用精确名称，数值须在类型范围内且有限；PointD 为有限数值的 `{"x":number,"y":number}` 或对应会话句柄。明确 out 不要求输入，简单值/PointD ref 仍必填并返回命名输出。拒绝类型名、反射、任意对象、嵌套数组、未知字段、空值和隐式转换，详见数组切片。

定位器复用 `eplan_object_find` 的候选枚举、精确类型筛选和摘要，覆盖 121 个查询名称（119 个原生类型），详见[生成类型表](eplan_object_kinds.g.md)。基类声明的属性、方法及对象形参接受受支持派生类型；每个定位器按原生类型、编号和新鲜摘要核对，不做类型转换。原有 14 个查询名称的摘要保持不变。

### 服务类入口
生成器从 `HEServices`、`EServices`、`EServices.Ged`、`DataModel.Planning`、`DataModel.Topology` 的目录元数据选择公开、非抽象且有公开无参构造函数的服务或辅助类；StorableObject 派生类继续使用对象定位器接收器。组 ID 仍为 `O:<Type>.<Method>`，服务实例在每次编译分支中构造，实现 IDisposable（含继承）时用 using 释放；借用的数据模型对象不释放（A39）。

服务调用的 `parameters.target` 必须是目标 scratch 项目的 Project 定位器，作为项目校验锚点；例如 `O:Eplan.EplApi.HEServices.ConnectionService.SwapSourceAndTarget` 接受 `target` 和 `connection`，后者是同项目 Connection 定位器。Project、Page 及其他对象形参均从定位器解析；每个定位器必须属于目标项目，并核对类型、编号和摘要。没有对象形参、依赖当前选择的服务入口还要求目标 scratch 项目就是当前项目，执行前重新检查；返回对象也须属于目标项目。

服务方法只有 Get*/Is*/Has*/Can*/Find* 且所有重载均无对话框、锁定、更新等副作用参数和输出文件时归为 read，其余归为 write，沿用执行前后快照和同一单撤销事务。文件参数按下节规则限制；目录及字符串项目路径、流、数据库、主数据或部件管理、设置或方案、打印机、URL、授权或退出仍排除；导入、导出、打印、备份和还原服务的隐式目的地同样拒绝。未受支持值/元素契约或编译句柄表约束的返回值、内部交互状态、自己创建事务的 TXN 入口及既有目录禁用项均不生成。逐签名分类理由与排除数量见 [生成清单](../../catalog/api-try.g.tsv) 和 [覆盖报告](../eplan/coverage-report.md)。

### 静态成员与属性入口
公开静态方法/属性从 DataModel 及子命名空间、HEServices、EServices（含 Ged）、Base 的公开目录类型生成编译分派，不构造服务或接收对象。例如 `DocumentTypeManager.IsLogicalPageDocType` 的 parameters 为 `{"nPageType":"Circuit"}`。静态调用仅允许读取，或显式带同项目对象参数并通过既有写入筛选。文件、网络、授权、应用/会话状态、主数据及生命周期入口继续排除；原始字符串对象身份不能代替定位器。

用 `eplan_project_list` 选择当前或其他已打开项目，再通过 `eplan_object_find(object_type="Project")` 获取项目定位器；无需页面放置。结构标识符增加 Project.GetLocationObjects() 枚举，图框增加 Page.PlotFrame 来源，其余 finder 结果沿用目录驱动发现。`parameters.via` 是相对 target 的公开属性路径，按目录生成闭合表，区分大小写且最多两层；group 指向最终对象的成员。例如 Project 定位器加 `via:"Settings"` 调用 ProjectSettings.GetBoolSetting（strSettingPath/nIdx），或 `via:"DeviceTagConfig"` 读取 Project.DeviceTagSettings.EnableSyntaxCheck；Page 定位器加 `via:"PlotFrame"` 读取项目图框。项目图层用 `via:"LayerTable.Layer"` 和字符串 `via_key` 指定名称，其他路径禁止 via_key。没有对象、路径错误、额外字段或跨项目父/结果对象均拒绝。

设置/方案对象的方法写入和 setter 不生成，方法在清单中标记 `settings_write`；设置名 strSettingPath 是内部设置键，不能借此调用文件导入/导出。静态 setter 同样不开放。via 不接受任意类型名、反射、集合或第三层；原定位器仍校验摘要，路径和选择器纳入审批原文及计划指纹。规划仅校验父定位器，不读取 via getter；审批之后才跟随路径。临时辅助对象按请求释放，借用 StorableObject 不释放。新增 Small 覆盖静态/两层入口与失败校验；LargeWrite 新增静态、设置读取和经 Function.Page 写 GridSize 的一步撤销及历史哨兵测试，仅编写未运行。静态/via 切片当时未改 schema，本次 schema 取句柄与数组/输出参数的并集。以下为该片历史统计，当前以生成清单为准。TSV 保持方法逐签名口径：按 group 去重，读 49→67、写 184→192、整组排除 714→818；有任一签名被排除的组为 743→847（与可执行组可重叠）。本片新增静态/属性入口类型，纳入清单的方法组总数 947→1077；这些计数不包含属性 getter/setter。

## 2. 输出与错误
链式扩展沿用 main 已验证的句柄表、会话和到期规则；生成返回类型闭包最多 4 层，继承和已实现接口不额外计层。集合先有界枚举前 200 个元素、略去空项，再经既有 Detach：有限数值、locator 优先于 handle，每个句柄复用 A68 的 ProjectLinkFilePath、同名 .edb 与父目录归属检查。数组沿用缓存分页；原生集合通过既有命名输出形状返回 returnValue（集合句柄）和 outputs.items（元素分页），不另设旧式句柄专用结果。简单键索引属性按完整参数名集合选择唯一只读签名，Int16/UInt32 检查范围；类型层字段仍不可调用。设置与派生句柄只读，链式行为和撤销未运行实机验证。

### 设置只读切片
设置组（`ApiTryCatalog.Settings(group)`）以 Project 定位器调用时，优先使用 A67 的设置文本适配器；即使同一签名也出现在句柄分发表中，仍返回原有文本。设置属性入口继续只读，临时设置对象留在适配层内部；其他类型的链式句柄和集合元素保持可用。

`Settings`、`SettingNode`、`SchemeSetting`、`ProjectSettings`、`ProjectSettingNode` 及 `Project.DeviceTagSettings` 的公开读取使用 scratch Project 定位器作为 `target`；`Project.Settings` 返回根标记 `PROJECT`，后续通过同一定位器读取项目设置，不返回原生句柄。除根标记和 DeviceTagSettings 属性外，参数增加 `node`：只允许 `USER`、`STATION`、`PROJECT` 及逐级枚举出的子节点；ProjectSettings/ProjectSettingNode 只接受 PROJECT。Base.Settings 只接受 USER/STATION；Base.SettingNode/SchemeSetting 在 PROJECT 下分别绑定 ProjectSettingNode/ProjectSchemeSetting。

先调用 `SettingNode.GetListOfNodes` / `GetListOfSettings`（项目亦可用 ProjectSettingNode），传 `node`、`offset`（0–1000000）、`limit`（1–200），ref 输出容器与 bAbsolutPath 不由客户端传入，固定返回相对名称。`value` 是 JSON 文本，含 items/total/nextOffset，按 nextOffset 翻页；`GetListOfAllSettings` 同样分页。`strSettingPath` / `strName` 必须来自相应节点的叶设置/方案子节点枚举；路径在规划及执行时重新枚举核对，失效报 PLAN_STALE。索引 idx/nIdx 为 0–1000000 整数；完整路径由适配层拼接。多语言值返回保留语言标签的原始文本，不冒充单语言显示值。

方案列表由枚举到的 SCHEMES 节点的 GetListOfNodes 读取，SchemeSetting.GetCount/GetLastUsed 等读取当前方案元数据；方案值路径对当前方案的数据节点枚举。只在已枚举节点上内部 Init 绑定临时句柄，不开放 Init/SetScheme；不切换方案，不写 LastUsed。所有 setter、Set*/Save*/Load*/Import*/Export*/Reset*、增删、合并、重载、ReadSettings/Write* 均拒绝（`settings_write`）；生命周期及未支持的原生句柄返回仍排除。COMPANY、INSTALL 根、直接 ISettings 接口组、非本片服务的设置/方案参数仍不可达；既有主数据、文件及生命周期排除不变。设置页仍使用 JSON 文本，读仍逐次审批、前后快照、不创建撤销步；观察范围仍是项目业务/设计文件，不包含 USER/STATION 设置库，不据快照相等宣称全局无副作用；ref 容器仅在此封闭适配器内使用，不放宽通用 ref/out 规则。

原生前置条件（api-notes A76）：规划与执行重检时，表内 NameService 调用要求显式 Page 重载及有效的已放置对象；`Reports.Update(ReportBlockReference)` 要求报表对象有效且页绑定。不满足返回 `INVALID_ARGUMENT/not_started`，不调用成员、不创建事务。`TextBase.UndockText` 的对象、所属项目或页明确只读/写保护时返回 `EPLAN_OPERATION_FAILED/not_started`。表逐组手工维护于 `ApiTryPreconditions`，不扩大目录权限，不改变审批或 unknown 暂停行为。

Files 切片：仅开放能明确判定方向的字符串文件参数。参数名 input/import/source 表示输入，output/export/target/destination 表示输出；无方向名称时使用目录说明的导入/读取或导出/保存含义，再以方法 Import/Read/Load、Export/Write/Save 或 Insert 服务判定。方向冲突、目录、字符串项目路径和模板仍排除；打印、发送、主数据、设置、方案、生命周期及隐式目的地的排除不变。输入为 Host 数据根目录 imports 下的相对文件路径（最多 260 字符、128 MiB），输出仅接受最多 100 字符的文件名，由 Host 分配新的 exports/api-try-<随机 ID> 子目录；拒绝 UNC、盘符、`.`/`..`、设备名、尾部点/空格、非法字符及重解析点。Bridge 独立复核，输入大小和 SHA-256 绑定审批指纹，执行前重新计算；输出不覆盖已有目录或文件。

结果新增 files，列出本次输出目录全部文件的 exports 相对路径、bytes 和 sha256；最多 100 个、每个最多 128 MiB。产物检查在项目写事务提交之前；项目撤销不撤销外部文件（A56），产物保留供工程师检查，失败或 unknown 时也可能留有部分文件，不将项目 rolled_back 当作文件已删除。

输出 project/group/status/unverified/execution/recovered、计划 `fingerprint` 与 `plannedChanges`，以及实际执行时的 value、before/after 摘要、changes、totalChanges、truncated、hint。预览保持 `execution:not_started`，hint 提示以相同参数传 `dry_run:false` 执行；原 `changes` 仍只表示执行观测。缓存页的 fingerprint 为 null、plannedChanges 为空，不重复规划或执行。属性写入的 `value` 为读回文本；方法简单返回值为文本、void 为 null，可定位的 StorableObject 返回值为与对象查找相同摘要的定位器，其他受支持原生对象返回会话句柄；PointD、数组及命名输出经 Detach 转为普通 JSON，嵌套对象同样优先 locator、其次 handle，结果分页见下文。对象参数与接收对象均检查项目、类型及新鲜摘要。属性写入的 `property:<group>` 观察项包含写前/写后的 JSON 编码标量文本，即使两者相等也列出。单次结果至多列 20 个变化，属性项优先，文本经 ResultShaper 清洗；变化清单不分页，设置列表按 nextOffset 分页。允许但未接入的组返回 `not_dispatchable_yet`，不审批或执行；目录外或排除组、scratch 外为 POLICY_DENIED。参数错误为 INVALID_ARGUMENT，定位器或审批指纹变化为 PLAN_STALE，以上均 not_started。审批拒绝、无渠道、审计不可写均不执行。

### 生命周期创建与移除
Create*/Remove*/Delete*/Erase* 及公开构造工厂是逐次审批的 write。工厂复用原目录 `O:<Type>.Create`，不新增组：例如 Line 的参数只含 page/pntStart/pntEnd，Bridge 无参构造后调用 Create。工厂不接受 target，传入既有实例的 locator/handle 返回 `INVALID_ARGUMENT/not_started` 并提示省略 target，try_template 同样省略 target；结果沿 Detach 返回可发现对象的定位器或受支持句柄；创建页须有效且属于 scratch，移除目标须有效，Placement 还须已放置。`CheckTarget` 保持句柄 → 设置 → 原生前置条件 → 生命周期检查的顺序，均在审批及事务之前。审批显示 creates/removes 和 undo unverified；移除沿用 destructive 审批、执行前备份及 ADR-0035 默认拒绝。

统一由 `Inspect(args)` 使用 `ApiTryObservations.Pages/Capture` 确定页范围：创建观察显式页，移除观察目标页，Project 工厂/服务锚点不扩大范围。保留标准快照与 drawing:<page>，另枚举受影响页的 AllPlacements、页身份和所属连接；无页对象回退到合并查询源的单遍项目枚举。每个原生身份只描述和计费一次，别名使用规范种类。`object:<kind>:<oid>` 保存定位器、对象描述及触及对象的资源读回（记录名、变量名、对象引用等）；移除项 after 为 null。规划、事务内及提交释放后分别读回，创建结果经 Detach 优先返回 locator、否则 handle；提交后仅按脱离原生对象的身份重新取得 locator，不复用事务内对象。

每次快照总预算 20 秒，生命周期清单另限 10000 个对象、256 KiB；超限拒绝或报 unknown，日志和错误记录 scope/visited/objects/bytes/elapsedMs，耗时不进入指纹。成员独立限 30 秒。观察不证明全部副作用，仍最多报告 20 个变化并标记截断。生命周期提交后 `undoExpected:true` 仅表示预期一次撤销，dry-run/恢复缺少数据为 null；合并后的撤销待实机复验。Project.Remove、CreateTransient、Destroy/Dispose/Init*/Initialize/Freeze/Lock* 以及文件、主数据、设置等原有排除保留。

## 3. 执行、安全与审计
每次实际执行都经独立窗口审批，读显示 `read unverified API <group>`，属性写显示 `write unverified API <group> = <value>`，方法写显示 `write unverified API <group>`，并展示精确签名和完整转义参数原文。读写均走 WritePipeline 的结果恢复和审计；读不创建撤销步。写沿用 TransactionRunner：调度器 LockingStep 内的 SafetyPoint → 内部 UndoStep → QuietModeStep，属性读回等于请求值且快照成功后才 Commit/CloseUndo；方法以执行后业务/文件观察为证据，不重复调用方法读回，整次写入恰好一个撤销步；异常或读回不符回滚，提交/回滚失败为 unknown，清理或关闭撤销步失败给出已提交警告。成员调用预算为 30 秒，执行前重新规划、属性原值读取和前后观察不占用该预算；定位器解析及每次快照各限 20 秒，Host 等待完整执行结果 120 秒，留出检查与事务收尾时间，支持进度和取消。规划请求仍使用 BridgeClient 默认的 30 秒期限；120 秒仅用于执行请求。成员返回后由本地预算检查触发的超限，只有事务回滚成功才返回 TIMEOUT / rolled_back；调用内部超时、模态阻塞、提交或回滚失败仍为 OUTCOME_UNKNOWN，暂停项目及实例写入，不自动重发。

数组对象参数均已放置时，规划、执行前复核及执行后观察只读取参数定位器涉及的页面；Project 接收器仅是项目锚点。相同页仅描述一次，函数按对象号去重；比较 standard-v1 逐页哈希和绘图几何摘要，跳过全项目对象计数、设计目录扫描和逐页描述。页面与连接身份仍各枚举一次，再仅对目标页读取详细值（A12/A36）。空数组或无页对象继续使用全项目观察；缺页只记录 unavailable 证据。其余调用保留 action_run 对象计数、设计文件大小/修改时间及 standard-v1 逐页哈希；排除锁文件及 User.eod/eox。快照不覆盖无页对象和 3D 连接，不将摘要相等当作无副作用证明。Bridge 独立核对生成目录及参数形状，按编译调用表分发。生成源为 API 目录、排除规则和 ApiTryGenerator 的类型/成员选择；方法名 Get*/Is*/Has*/Can*/Find* 且无副作用参数为 read，其余为 write 并进入同一个撤销事务。排除 Destroy/Dispose/Init*/Initialize/Freeze/Lock*、transient 创建、未纳入工厂的构造函数、复杂 ref、委托、多维/嵌套数组、不支持的集合/泛型参数、非公开成员及未通过静态安全筛选的成员、不支持的返回值及目录排除项；同时读取 BannedSymbols.txt 排除禁用成员，ViewPart.UseLocalPropertyPlacements 的过时 setter 不生成。逐签名分类和排除理由见 `catalog/api-try.g.tsv`。非公开 setter、索引器和复杂属性值仍不开放写入。目录保留 protected setter 的独立可见性，不能仅凭公开 getter 判断可写。TerminalStrip.FunctionCategory 通过 Function 基类 getter 读回；不访问禁用的基类 Properties。

## 4. 切片与测试
设置切片合并时的 TSV 按精确组 ID（区分大小写），采用“有 write 优先，否则有 read，否则全排除”口径，读/写/全排除为 **124/202/878**；设置行包含 getter 与 setter 排除，原有 342 个标量属性仍另计。重叠设置签名只登记一次，设置适配器与既有 via 形状同时保留。

设置切片增加只读形状、范围/索引/分页、逐段枚举、跨项目与伪造定位器拒绝、全部设置写入拒绝，以及 refused-read/unknown 和无撤销步的 Small 测试；LargeWrite 测试在设置读取间写 Page.Name，核对一次撤销恢复该写、再一次撤销此前哨兵页，仅编写未运行。新生成逻辑在 ApiTryGenerator.Settings.cs，运行时设置逻辑在独立 partial 文件。
Files 切片 Small 覆盖方向判定、每个新增签名形状、Windows 路径与长度限制；Medium 覆盖输入替换/重解析点、输出大小/数量、哈希清单、审批参数绑定及单事务回滚。Large 在 LargeWrite 副本导入唯一设备清单，核对一次撤销恢复清单、再次撤销哨兵页；仅编写、编译，未运行。

生命周期片新增工厂构造条件、标量/PointD/定位器形状、同项目校验、明确审批效果、前后身份读回、审批陈旧、单事务提交/回滚和提交后读回失败 unknown 测试；Large 在 LargeWrite 新建直线、删除后分别一次撤销恢复，再核对此前哨兵页的撤销历史。仅编写，未运行 EPLAN；不增加 live_verified。
第五片增加服务构造条件、继承 IDisposable、安全分类、资源参数排除、Project 锚点、精确签名、跨项目定位器拒绝、当前项目复核与服务单事务回滚的 Small 测试。Large 在 LargeWrite 上读取 `NameService.CanDeviceTagBeNested` 并断言设计文件不变，写 `ConnectionService.SwapSourceAndTarget` 后核对两端互换、一次撤销恢复业务快照、再一次撤销此前建页；仅编写未运行。
新增目录映射、最派生 Properties、原有 14 种摘要字节回归和排除规则测试；Large 新增通用 find/get，记录零对象类型并要求至少覆盖 10 个新增类型且设计文件不变，仅编写未运行。Small 另覆盖方法重载选择、歧义排除、分类、枚举/PointD/定位器参数、方法单事务与空值/定位器返回；Large 新增 Page 枚举参数读取及 PlaceHolder.AddRecord 单步撤销和既有历史检查，未运行。Small 覆盖开关、scratch 策略、目录排除、标量/枚举形状、非公开 setter、单事务提交、异常/读回不符回滚及 timeout/modal unknown；Medium 用假 Bridge 验证逐次审批、拒绝、变化报告、审计失败与 unknown 暂停。Large 已补齐 scratch 副本 Page.Name 写入后一次撤销恢复原值、再撤销此前建页，以及 Terminal 定位器读取无变化；本片不运行 Large，不声称已实机验证。评测场景：查目录、取得定位器、dry-run、工程师批准读或写并检查变化。

**对象句柄（ADR-0033）**：返回的 `{"handle":"h:<32 位小写十六进制>","type":"<原生类型全名>"}` 可原样用于 `parameters.target` 或生成表指定的对象参数；先从 `Connection.StartPin` 取得 Pin，再用 `Pin.Name` 读取或用 `Pin.IntrinsicSafetyPossible` 的 `value` 写入，仍应先 dry-run。仅限同一 Host 会话和同一 EPLAN 进程，闲置 10 分钟失效，每会话最多 500 个、超出淘汰最早创建项；项目关闭、EPLAN 退出及 Host 重启后必须重新获取。
句柄表只在插件进程内存中保存借用对象，每次调用清理、每次使用重新核对有效性、编译类型及所属 scratch 项目；伪造类型为 INVALID_ARGUMENT，失效为 PLAN_STALE，项目或 scratch 归属不符为 POLICY_DENIED。审批显示 `<type> returned by <group>` 与 unverified；审计只保留类型、来源组和不透明标识，不序列化对象内容。设置及其返回对象的句柄只读；部件库和主数据仍以 `masterdata_copy_pending` 排除，等待部件库副本规则。生命周期按创建/移除规则开放，未获 Files 切片支持的文件入口等既有排除不变；数组及 ref/out 按下节规则开放，属性仍只写原有标量/枚举形状。 Small 覆盖期限、上限、会话隔离、类型、项目复核、失效、设置只读及生成形状；LargeWrite 的 Pin 句柄读写、单次撤销与既有历史检查已编写，本片未运行 EPLAN。

### 数组、集合与输出参数切片
参数支持一维 `T[]`（含 params）及 `IEnumerable<T>` / `List<T>` / `IList<T>` / `ICollection<T>`，JSON 均为数组；T 限现有简单值、目录枚举、PointD 或受支持对象类型。每个数组最多 200 项，允许空数组；每个对象使用同项目新鲜定位器或受支持会话句柄，逐项通过 ResolveObject 核对类型、摘要/有效性和 scratch 归属；PointD 为有限的 x/y 或对应会话句柄，支持 PointD[] 折线点列。参数原文上限由 2048 增至 131072 字符。生命周期按创建/移除规则及数组形状共同筛选；`PolyLine.GetPointAt` 返回 PointD，`SetPointAt(index, point)` 的 PointD ref 是可复制的有限坐标值，支持写入并返回更新后的命名值。
生成元数据须明确 `direction: out` 才能省略输入并生成 out 调用，结果为 `value.returnValue` 与 `value.outputs.<参数原名>`；简单值/枚举及值型 PointD ref 仍必填，返回更新后的同名值。现有目录四个 `@` 签名的本机元数据均无 Out 标记，按 ref 保守处理，复杂 ref 继续排除，不按名称或说明猜测方向。
受支持元素的集合属性 getter 与 PointD 属性 getter 也可读取；复杂属性 setter 仍排除。集合返回值先在事务内复制成普通 JSON（数字/布尔保留类型、枚举为名称、PointD 为 x/y、对象优先为同项目定位器，没有 discovery kind 时为同会话句柄），每个集合最多 200 项，超出明确拒绝，写入回滚。Host 返回 `value.items/total/nextCursor/truncated/hint`，按现有 ResultShaper 预算分页；命名输出中的集合采用相同页面形状。将 nextCursor 放入可选顶层 `result_cursor`，保持 group/project/parameters 原文相同，即可读取缓存观察值，不调用 EPLAN、不再次执行或审批。缓存只在当前 Host 内存保留十分钟、最多 16 个集合；过期报 INVALID_ARGUMENT，不能为取结果重放写入。缓存页 status=cached、execution=not_started；新执行仍逐次审批。
仍不能分发：多维/交错/嵌套集合（无单层元素契约）、字典/非泛型集合/其他泛型容器（无闭合形状）、开放泛型/委托（不可编译选择）、复杂 ref（无法证明只是简单输入输出）、未标明方向的潜在 out（目录信息不足）、AnyPropertyId/PointD3D/RectangleD/未支持对象元素（无现有值/定位器/句柄契约）、集合属性写入（没有逐项读回契约）；未获 Files 规则许可的文件访问、设置写入、主数据及既有禁用项维持原排除。名称集合歧义继续排除。
Small 覆盖容器/元素、空数组、200/201 边界、错误元素/嵌套/重复字段/跨项目、输出与 ref 选择、PointD 折线结果、预算与缓存游标；Large 在 LargeWrite 对集合参数写入验证一次撤销恢复并保留此前历史，仅编写未运行。

数组及生命周期对象定位器带页名时，仅对受影响页各观察一次 snapshot 与 drawing:<page>；Project 锚点不扩大范围。空数组、未放置对象及缺少页面证据的对象句柄保留项目级观察；PointD 值不扩大范围。成员调用单独计量 30 秒，规划、原值读取和前后观察不计入此预算；句柄调用经过同一 Inspect(args) 路径。

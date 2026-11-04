# Changelog

本文件记录所有面向用户的变更，格式遵循 [Keep a Changelog 1.1.0](https://keepachangelog.com/zh-CN/1.1.0/)，版本号遵循 [Semantic Versioning 2.0.0](https://semver.org/lang/zh-CN/)。
对本项目而言，"公共 API" 指 MCP 工具契约（名称、参数、返回结构、安全注解）和配置文件格式，破坏性变更的定义见 [手册开发流程条款](docs/standards/engineering-handbook.html#build-release)。

## [Unreleased]

- `eplan_device_place` 端子来源可选择库符号及变体，按 Terminal 类别校验；省略时保持 IEC_symbol/X 默认。

- `eplan_device_place` 支持 SPECIAL/BBDEF 母线定义及同标识 BCP 连接点，返回原生类别与主功能标志。

- `eplan_device_place` 支持 SPECIAL/DC 黑盒及同标识 DCP/DCPP3 连接点，读回原生类型、类别、主功能、外框与框内功能。

- `eplan_connection_create` 支持在导线上放置 SPECIAL `CDP` / `CDPNG` 连接定义点，写入颜色、截面和标识；预览列出属性，提交后读回核对，整次调用一步撤销。
- `eplan_macro_insert` 新增 `representation`：可在总览页插入宏的 `Overview` 表达；布局检查按所选表达和宏变体读取外框。
- `eplan_macro_insert` 可用 `part` / `part_variant` 代替 `macro`：插入部件的组符号宏，在同一事务中给主功能补上部件引用，提交后读回。
- `eplan_macro_insert`：宏的声明外框超出可绘制区但仍在已知幅面的页内时只给警告（部件总览宏铺满 A3 页）；插入的对象仍在写入后实测检查。

- `eplan_placement_create` 新增折线、圆弧、路径功能文本和 imports 图片，沿用默认预览、布局守卫、批量读回与单步撤销；EPLAN 实机验证 NOT RUN。
- Host 审计记录通过共享规则去除敏感键值、认证凭据、URL 用户信息及 PEM 块；审批指纹继续绑定原始参数，审计链基于去敏后的记录计算。
- Host 控制台、Bridge（含离线 stderr 与启动失败后备日志）及 Workbench 文件日志在写出前复用同一凭据脱敏规则。
- `eplan_connection_create` 中断点内置默认改为 EPLAN 插入命令使用的 `BP`，支持 SPECIAL 中含一个或多个连接点的全部中断点符号；按匹配连接点选择变体及定位，双连接点读回不再失败。可显式选择 `BPIN`、`BPOUT`、`BPF` 等符号。
- 新增 `eplan_report_create`：按报表块生成报表页，支持筛选、排序与页描述，返回同类型和表格的报表页及本次新增页；默认预览，沿用审批、单步撤销与审计。正式工具实机验证 NOT RUN。
- `eplan_page_get` 返回报表页的 `report` 设置：类型、表格、方案名、器件标识与页描述；单字段不可读时只记录警告。正式工具的实机验证待运行。
- 全局插件配置可按启用顺序参与默认值解析，保留胜出与被覆盖来源；项目绑定锁定的插件跳过全局层，缺失数据只给告警。
- `eplan_pack_list` / `eplan_pack_get` 显示插件分类、提供项、标准标签和每个精确版本的全局启用标记。
- 新增 `eplan_pack_enable`：经现有 EPLAN 审批全局启停已安装的精确插件版本，保留启用顺序，以 revision 校验防止覆盖并发变更。
- 新增 `eplan_pack_install`：从插件库安装精确版本，核对索引摘要；内置库免审批，其它来源沿用安装审批，相同内容重复安装不变更。
- 工程包新增插件种类、提供项与标准标签，旧包保持原摘要；新增离线工具 `eplan_pack_library_list`，可浏览内置、本地及登记的只读共享库，查看已安装版本和更新候选。
- `eplan_device_update` can assign a part to an existing device's unique main function, together with an optional rename; planning validates the part and variant, and readback verifies the references. Large verification NOT RUN.
- `eplan_device_place` supports parts with macros or function templates; an optional tag renames all device functions together, including unplaced functions. Omitting it retains EPLAN numbering; `unplaced_functions` lists full names read back after commit. Large verification NOT RUN.

- `eplan_connection_create` 支持用 `t_node.variant` 选择同方向引脚的 SPECIAL 变体，以及用 `interruption_point.sort_code` 在放置事务中设置排序码 24809 并读回；省略参数沿用原行为。D1/D2 的变体流向及跨页配对 Spike 已补，待实机运行。

### 破坏性变化

0.5.0 按 ADR-0049 / MIG-02 完成下表 38 个工具的默认值迁移；省略或传 `dry_run: true` 均只预览，不请求审批、不修改项目或文件，返回 `execution: not_started` 与执行提示。执行须显式传 `dry_run: false`，审批、读回和撤销规则不变；`eplan_plan_apply` 输入已审阅计划，作为例外，保持现有执行语义。

| 工具 | 旧：省略 `dry_run` | 新：省略 `dry_run` | 执行方式 |
|---|---|---|---|
| `eplan_device_place` | 执行 | 预览 | `dry_run: false` |
| `eplan_connection_create` | 执行 | 预览 | `dry_run: false` |
| `eplan_placement_create` | 执行 | 预览 | `dry_run: false` |
| `eplan_placement_update` | 执行 | 预览 | `dry_run: false` |
| `eplan_placement_edit` | 执行 | 预览 | `dry_run: false` |
| `eplan_macro_insert` | 执行 | 预览 | `dry_run: false` |
| `eplan_page_create` | 执行 | 预览 | `dry_run: false` |
| `eplan_page_update` | 执行 | 预览 | `dry_run: false` |
| `eplan_page_text_replace` | 执行 | 预览 | `dry_run: false` |
| `eplan_page_delete` | 执行 | 预览 | `dry_run: false` |
| `eplan_page_renumber` | 执行 | 预览 | `dry_run: false` |
| `eplan_device_update` | 执行 | 预览 | `dry_run: false` |
| `eplan_property_set` | 执行 | 预览 | `dry_run: false` |
| `eplan_device_renumber` | 执行 | 预览 | `dry_run: false` |
| `eplan_cable_renumber` | 执行 | 预览 | `dry_run: false` |
| `eplan_terminal_renumber` | 执行 | 预览 | `dry_run: false` |
| `eplan_connection_number` | 执行 | 预览 | `dry_run: false` |
| `eplan_connection_generate` | 执行 | 预览 | `dry_run: false` |
| `eplan_cable_generate` | 执行 | 预览 | `dry_run: false` |
| `eplan_report_update` | 执行 | 预览 | `dry_run: false` |
| `eplan_check_run` | 执行 | 预览 | `dry_run: false` |
| `eplan_import_dxf` | 执行 | 预览 | `dry_run: false` |
| `eplan_import_pdf_comments` | 执行 | 预览 | `dry_run: false` |
| `eplan_import_list` | 执行 | 预览 | `dry_run: false` |
| `eplan_import_plc` | 执行 | 预览 | `dry_run: false` |
| `eplan_import_pxf` | 执行 | 预览 | `dry_run: false` |
| `eplan_language_set` | 执行 | 预览 | `dry_run: false` |
| `eplan_language_remove` | 执行 | 预览 | `dry_run: false` |
| `eplan_project_translate` | 执行 | 预览 | `dry_run: false` |
| `eplan_part_import` | 执行 | 预览 | `dry_run: false` |
| `eplan_part_sync` | 执行 | 预览 | `dry_run: false` |
| `eplan_translation_import` | 执行 | 预览 | `dry_run: false` |
| `eplan_placeholder_apply` | 执行 | 预览 | `dry_run: false` |
| `eplan_action_run` | 执行 | 预览 | `dry_run: false` |
| `eplan_action_try` | 执行 | 预览 | `dry_run: false` |
| `eplan_api_try` | 执行（含读取 API） | 预览 | `dry_run: false` |
| `eplan_project_create` | 执行 | 预览 | `dry_run: false` |
| `eplan_cabinet_bind` | 执行 | 预览 | `dry_run: false` |
| `eplan_plan_apply` | 申请执行已审计划 | 保持申请执行已审计划（例外） | 省略或 `dry_run: false`；true 仅重核预览 |

### Fixed
- 写操作异常缺少执行状态且没有回滚证据时，报告 `OUTCOME_UNKNOWN` / `unknown`，保留已证实回滚的结果，避免误报为已回滚并引发重试。
- Host 与 Bridge 双向拒绝 JSON-RPC 协议信封中的重复成员和未知成员，并附诊断编号；保持协议版本与 16 MiB 帧上限，升级时需同时更新 Host 与插件。
- `eplan_page_get` 返回四项原始报表方案数据（每项最多 4000 字符，超出截断并警告），`eplan_report_create` 可接收这些数据并设置报表块，修复来源方案名在目标项目中不存在时的端子图生成失败；审批只展示数据长度。Small/Medium 与契约覆盖随改动更新，修复后 Large 待运行。
- 带偏移引脚的 T 节点变体（TLRO/TLRU/TOUL/TOUR 变体 0–3）：预测连线从真实引脚出发，不再被布局 R4 误判穿过自身外框；连线与中断点规划都把这类节点识别为节点并连通其各引脚。同页两个同名中断点按 EPLAN 规则互相配对，不再拒绝。提交后读回失败时，错误信息附带原因。2026-11-02 客户页重绘实机通过（`AcceptanceRedrawTests`，客户清单）。

## [0.4.1] - 2026-11-02

- 按 A77 拒绝三个已使 EPLAN 崩溃的 API 写侧，保留 4014 分母并单列 `refused_unsafe`；批量验证跳过这些侧，失联时停止并暂停写入，导入时丢弃未实际观察的后续结果。

- 修复 `eplan_api_try` 链式句柄分发覆盖设置文本的回归：以 Project 定位器读取 `Project.Settings` 仍返回字符串 `PROJECT`，设置列表和属性入口保持既有文本，其他类型的句柄与集合元素继续可用；补实际分发路径的 Small 回归，修复后 Large 待验。
- 原生调用前检查只读与页上下文；全项目对象枚举跳过无效项并记录数量，写事务内捕获原生异常时记录带 correlation id 的 Warning。审批、暂停与回滚行为保持；2026-11-01 实机通过（`ApiTryTests` 10/10、`DevicePlaceTests` 8/8）。
### Fixed
- `doctor --offline` 在没有安装 EPLAN 的机器上（CI 运行器、冒烟检查）对 EPLAN 安装检查只给警告，不再以失败退出；普通 `doctor` 仍报失败。
- 读取属性时沿属性列表的继承链查找（例如 Function → FunctionBase），恢复设备结构标识（=、+、== 等）的读取；校验与写入规划使用同一分类，写入白名单不变。2026-11-02 实机通过（`CabinetScopeTests`、`ObjectReadTests` 14/14、属性读取 22 项）。
- 带电缆定义的 `eplan_connection_create` 时限放宽到 180 秒（密集页面上一批电缆需 6 次提交和多次整页读回，实测 66.5 秒）；布局警告最多保留 20 条并注明其余数量，避免把芯线读回等核对字段挤出结果。
- 布局读取与 R2 排除 DynamicConnectionLine 的符号身份，修复 BAT 引脚接在线端时因外框交叠被误拒绝；保留真实符号重叠拒绝及已有 R3/R4 线段检查；2026-11-02 实机通过（`PageReviewTests` 8 项通过）。
- Cable 移动时，移动前后按名称归属该电缆的定义点、移动后电缆上新出现的未命名定义点，以及这些定义点自己的属性文字，均不计入闭包身份、数量与跟随比较；定义线、自身文字、其他放置及另一电缆的定义点仍逐项核验，独立定义点移动保持严格校验。实机发现移动电缆会丢失芯线归属、生成连接也不恢复（A73）：结果警告如实说明，`eplan_page_review` 只给目标位置建议并提示重画。2026-11-02 实机通过（`PlacementMoveTests` 1/1，`PageReviewTests` 8 项通过，电缆修复步骤按设计跳过）。
- 布局 R3 对本次创建或改变对象的默认文字位置冲突只给警告，列出文字归属、冲突对象及 `property_moves` 修复建议；显式定位文字仍拒绝冲突。补充 W4 精确几何回归；2026-11-01 实机通过（`AcceptanceRedrawTests` 13 个阶段完成）。

### Added
- PLC 连接点可显示卡标识（20213）、主/辅助引用（20300）和插头（20406，新参数 `plc_plug_designation`）；放置提交后按对象号读回，不在事务外保留原生对象。`eplan_plc_get` 也列出放在卡所在页之外的点。2026-11-02 实机通过（`PlcDisplaySpikeTests`、`DevicePlaceTests` 8/8、`PlcTests`、`AcceptanceRedrawTests` 完整通过）。
- 官方 action 参数离线导入：目录补充允许值、字面默认值、类型、必填性及按 TYPE 匹配的示例；调用模板采用第一条示例并替换 scratch 路径。批量验证只取官方目录行，文件、页和选择上下文缺口继续记为 `failed:needs_context`，执行保护不变。
- 每周五发布 v0.4.x 预览版，首班为 2026-11-06 的 v0.4.1；新增只准备本地版本与发布说明的 `release-prep.ps1` 和无需 EPLAN 的 Host `smoke.ps1`（使用显式跳过实时 Bridge 与授权检查的 `doctor --offline`），提交、标签、推送、插件哈希交接及发布仍由维护者执行。
- 通用工具调用模板：API 详情按生成分发表返回逐签名参数、枚举全集、来源与示例；action 详情返回已知参数、选择建议及禁用状态。参数错误附带最接近模板；评测报告新增 argument retries（目标 0），并加入先查详情再零参数重试试规划的任务。补全离线测试及契约快照，未运行 EPLAN。
- C1 Files：`eplan_api_try` 对方向明确的文件参数限制在 Host imports/新建 exports 子目录，输入哈希绑定逐次审批，输出返回路径、大小与 SHA-256；目录、打印/发送、主数据、设置及隐式目的地仍排除。TSV 方法组 read/write/excluded 从 49/184/743 变为 49/194/737，新增 14 个可执行签名；Large 导入及单步撤销测试仅编写、编译，未运行。
- C1 静态与入口片：公开静态成员无需 target，项目/页面对象通过最多两层的目录属性路径到达，补项目图层、图框和结构标识符入口；设置/方案只读，维持 scratch、逐次审批、编译分派、快照和单撤销。补充 Small 与 LargeWrite 撤销测试；未运行 EPLAN，schema 不变。
- 新增 full / Read 的 `eplan_selection_get/set`：读取当前项目/编辑页、分页选择集，按通用对象摘要定位并 replace/add/clear，仅改变 UI，报告未选中对象；`eplan_action_try` 可在审批后设置精确 selection，审批与审计记录上下文。Large spike 和单撤销验证已编写，未运行。
- 设置只读切片：`eplan_api_try` 通过 scratch Project 定位器读取 USER/STATION/PROJECT 设置、枚举节点与方案列表；路径逐级枚举核对、列表有界分页，所有设置写入仍以 settings_write 拒绝。schema 不变，补只读形状/校验测试及 LargeWrite 读取与单撤销测试；Large 仅编写未运行。
- 新增 `eplan_placement_edit`（full）：同页删除、复制、90 度旋转、镜像、对齐和等间隙分布；复用绘图事务和单步撤销，删除逐次审批并备份，返回连接影响及复制定位符。Small/Medium 覆盖失败路径；原生行为 Large spike 仅编写未运行。

- `eplan_api_try` 支持最多 4 层类型闭包、对象数组/集合的独立元素句柄（至多 200 个）、简单键索引属性读取及公开接口接收器；设置句柄仅开放读取，主数据、生命周期和外部文件限制保持。新增逐类型来源及前后缺口表，契约快照同步；Small 已补齐，LargeWrite 两步链及单撤销测试仅编写未运行。
- `eplan_api_try` 按 ADR-0033 支持对象返回句柄、句柄接收器和对象参数：同 Host 会话与 EPLAN 进程、闲置 10 分钟、每会话 500 个，使用时复核类型与 scratch 归属；审批和审计标明类型及来源，设置只读、主数据仍排除，写入保持单次撤销。Small 已补齐，LargeWrite 已编写未运行。
- `eplan_api_try` 支持受支持元素的一维数组/集合、PointD 返回、明确 out 与简单 ref 命名输出；逐项校验最多 200 项，集合结果按预算缓存分页，翻页不重放写入，保持逐次审批与单撤销。数组对象写入的规划与前后观察限定到参数涉及的页面，重复对象只描述一次；保留 30 秒规划及 120 秒执行期限。Large 既有超时失败待复验，本次未启动 EPLAN。
- 共享 LayoutGuard 实现 R1–R9、栅格空位建议及五个绘图入口的写前检查；图框采用明确标注的边距回退，未验证的设备/文字/宏与电缆完整预测暂拒绝。补充未运行的 Large 与两图框 SPIKE。
- C1 生命周期片：`eplan_api_try` 开放项目内 Create*/Remove*/Delete*/Erase* 与公开构造工厂，全部 scratch 逐次审批、单撤销写事务；审批明确创建/移除和撤销未验证，创建返回新鲜定位器，移除报告前后对象/资源观察，提交后记录 undoExpected。保留文件、主数据、设置、销毁和会话锁排除；补 Small 与未运行的 LargeWrite 撤销测试。
- C1 生命周期片：`eplan_api_try` 开放项目内 Create*/Remove*/Delete*/Erase* 与公开构造工厂；工厂复用原目录 Create 组、模板省略 target，拒绝对既有实例调用 Create；全部 scratch 逐次审批、单撤销写事务；审批明确创建/移除和撤销未验证，创建返回新鲜定位器，移除报告前后对象/资源观察，提交后记录 undoExpected。保留文件、主数据、设置、销毁和会话锁排除；补 Small 与未运行的 LargeWrite 撤销测试。
- 新增只读 `eplan_page_review`：页面规则、目标差异、分页及三轮审查修复闭环；图片留待后续。
- C1 第五片：`eplan_api_try` 新增服务类 7 个读组、90 个写组，按公开无参构造和 IDisposable 生成编译入口；Project 定位器锚定 scratch，拒绝跨项目参数及共享数据或外部资源签名，逐签名列出分类理由。写入复用单撤销事务，补充 Large 服务读取和连接两端交换撤销测试，仅编写未运行；工具契约不变。
- C1 第四片：对象发现扩展到目录驱动的 121 个查询名称（119 个原生类型），11 个名称给出明确 unsupported 原因；生成完整类型表及最派生 Properties 分发，保留原有 14 种定位器摘要。api_try 现有 384 个读组、314 个写组，支持全部新增类型的定位器参数；覆盖与契约快照同步，Large 仅编写未运行。
- C1 第三片：`eplan_api_try` 新增 9 个读方法组、18 个写方法组，支持精确重载选择、目录枚举、PointD 与同项目对象定位器参数；返回标量文本、定位器或 null，写方法复用单撤销事务。生成逐签名排除清单并更新覆盖报告，Large 已编写未运行。
- C2：新增 `eplan_action_try`，默认关闭，仅 full；目录未验证 action 只在 scratch 副本逐次审批后执行，带禁止表、前后指纹与未验证审计；超时/模态窗口按 unknown 暂停写入。Large 已编写未运行。
- 新增 `eplan_action_catalog_search` / `eplan_action_catalog_get`（full、Read）：查询官方 177 行与运行时本机 UI action，返回 TYPE、已知参数、白名单级别、验证引用及安装版本缺口；不启动 EPLAN，不保存或分发本机清单。
- 新增默认关闭的 `eplan_api_try`：scratch 限定、逐次独立审批、目录生成的编译分发表、业务快照/设计文件指纹比较和 unverified 审计；超时结果未知时暂停写入。未接入的组明确返回尚不可分发；Large 已编写未运行。
- 新增 `eplan_api_search` / `eplan_api_get`（full、Read）：离线发现全部 4,649 个 API 重载组及 69 种属性字典，查询完整签名、排除原因、覆盖状态、证据与到达工具；压缩索引嵌入 Host，无需 EPLAN。
- `eplan_connection_create` 支持在既有自动连接线段内显式放置方向对齐的 T 节点拆线并引出支路，返回被拆线段与节点位置；保留原逻辑连接，支路远端尚无器件时允许零新增逻辑连接。新增离线回归与待运行的 Large 验证。
- 七个绘图写工具（设备放置、连线创建/生成、图元创建/移动、宏插入、DXF 导入）支持 `show_page=false`：启用后先开页供实时观看，dry-run 也开页；开页失败不阻止写入，结果以 `pageShown` 和提示报告。
- `eplan_connection_list` 增加 `response_format=detailed`，返回与电缆规划同源的连接定位器，可直接用于 `cable_definition` 芯线；默认简洁输出不变。
- M8 T8a：`eplan_connection_create` 增加电缆定义/芯线只读规划，逐芯预测连接归属、编号/标签、颜色及截面，校验复合键、整次调用限额与完整相交范围并固定 stale 指纹；执行仍为 `CAPABILITY_MISSING`，留 T8b 核验 A55 多阶段提交，不增加工具或 Bridge 操作。
- M8 T5b：注册 `eplan_placement_update`（full、Write）：整体移动无组符号及属性文字、自由文本/线/矩形，读回与提交后自动线校验；失败整体撤回，无变化不留撤销步，成功一步撤销。逻辑连接另用既有生成工具更新；新增 Large 未运行。
- M8 T5a：增加 `eplan_placement_update` 未注册契约和绝对移动规划，包含父符号全部属性文字、栅格/外框/碰撞及自动线影响预测；拒绝组与宏组，校验陈旧证据并识别 no-op，执行留 T5b。
- M8 T8b：`eplan_connection_create` 电缆执行沿 A55 分阶段提交、绑定和分配显式芯线，最终读回型号/连接/标签/颜色，失败自动撤销本次提交，补偿失败报 unknown；预览/审批/结果列出 6–8 撤销步。补齐 Small/Medium 和待实跑 Large，保持工具与 Bridge 操作数量不变。
- 新增 `eplan_rule_check`（full，Read）：按柜检查本机已提升的锁定规则，分页返回四值和证据；`fix_plan` 仅编译声明的修复计划，unknown 或修复冲突时拒绝，不执行工程修改。
- M8 T7b：`eplan_connection_create.items[].kind` 增加 `interruption_point`，显式共享业务名、方向、坐标与对端页范围；拒绝歧义配对，提交后返回 EPLAN 双向对端和计算引用，保持两次提交/失败自动撤销。新增 Small/Medium 与待运行的 Large 配对撤销测试；不新增工具或 Bridge 操作。
- M8 T4c：注册 `eplan_placement_create`（full、Write、非幂等），1–50 项文字/直线/矩形同一事务创建、全部读回后提交；失败整批回滚无空步，一次撤销。明确语言限制，页界/文字完整外框的 dry-run 缺口仍保留；混合 Large 已编写，未运行。
- 模块只读检查增加供 M3 使用的内部服务：保留跨页引用、占位符值集、实际属性与模块声明，未解析引用明确列为缺口；缺 Bridge 能力或授权时拒绝，不注册新工具或写操作，实机闭包对照待验证。
- 新增 `eplan_page_open`（full，Read）：按显式项目与精确页名打开或聚焦编辑器，重复调用只重新聚焦；不保存或修改工程数据，不留撤销步。离线宿主明确拒绝；新增 Large 测试待运行。
- 新增 `eplan_asset_review`（full，本地数据）：经工程师独立窗口审核候选、关闭缺口或提升新正式规则；`eplan_pack_build` 拒绝无来源的 mandatory 规则与缺少匹配本机确认的 official，手改 standing 无效。
- M8 T4b：增加 Bridge 内部 TEXT 创建适配，要求显示语言或语言无关文字，在事务内核对语言、字高与位置后才提交，失败回滚无空撤销步；绘图发现增加字高。Host 执行仍为 `CAPABILITY_MISSING`，尚未注册工具；Large 已编写，未运行。
- M8 T4a：新增 `placement` 词表及 `eplan_placement_create` 的契约/只读规划，校验显式语言、完整外框并报告栅格吸附；工具尚未注册，执行留 T4b/T4c。
- `eplan_connection_create`（full / Write）：显式正交路径及角/T/电位连接符号，一次调用放置、生成两次提交（2 undo steps）；生成失败自动撤销放置，端点读回不符整体补偿。
- M8 T3a：新增 `eplan_connection_create` 的规格、未注册契约与 dry-run 路径/符号规划器，预测单页 SPECIAL 角/T/电位节点、吸附坐标、图形自动线和生成后的逻辑端点；执行仍返回 `CAPABILITY_MISSING`。A52 的独立连接生成及两个撤销步骤明确列入预览，未开放原子执行。
- 新增 `eplan_symbol_search`（full，只读）：查询项目已有符号库、符号变体、类型及连接点，按封闭种类过滤并分页；不导入库或修改设置。
- 新增 `eplan_cabinet_list` / `eplan_cabinet_bind`（full）：多柜映射、五层配置溯源、预览及换锁确认；`eplan_context_get` 按所选柜隔离配置与对象。
- `eplan_import_pdf_comments`（full，Write）：导入本项目经 EPLAN 导出、Acrobat/Reader 批注的 PDF，只导入便笺 Text，忽略插入标记和下划线等。文件沿用 imports 接收校验；dry run 只校验并统计现有注释，新增数执行后才知道；scratch 外按策略审批，一次 Ctrl+Z 撤销，失败或重复导入新增 0 时回滚且不留空步。结果按不可信数据清洗、限量返回页名、文字、作者和时间。M4c 工具实现完成，实机依据 S44；新增工具的 Large 测试待运行。
- 工程包格式第 2 版（M6 第 3 阶段，ADR-0029）：能读取 `schemaVersion` 2 的 `pack.json`、`eplanmcp.pack/v2` 的知识文档（新增配置项定义、配置层、事实集、规则、工作流、样例等种类）和第 2 版项目绑定；旧格式照常读取，不改写已安装的包。第 2 版绑定在 bind/migrate 之前只读。
- `eplan_pack_build` 和包检查校验配置项定义（`config_schema`：类型、登记单位、枚举、上下限、允许的层）和配置层（`config_layer`：来源、层权限、默认值与约束、未知键），问题带 JSON Pointer。包内配置层只能是 `company` 或 `cabinet_type`；配置合并在后续版本加入。
- 包检查校验规则知识文档（`rule`）：受限的类型化 JSON 表达式，只能读登记的属性和关系，限制层数、关系跳数和节点数，正则只接受字面模式；旧版裸规则资料照常可查询。规则求值在后续版本加入。

- `eplan_api_search` / `eplan_api_get`（full，只读）：离线查询 API 重载组、属性字典、完整签名、覆盖状态、排除原因及可用工具，无需启动 EPLAN。
- `eplan_object_find` / `eplan_object_get`（full，只读）：按类型分页查找对象、获取定位器并读取属性，覆盖页、功能、连接、端子、PLC、电缆及其他目录类型；不支持的类型明确说明原因。
- `eplan_action_catalog_search` / `eplan_action_catalog_get`（full，只读）：查询官方动作和本机 UI 动作的参数、白名单级别、验证依据及安装版本缺口，无需启动 EPLAN，不保存或分发本机清单。
- API 和动作详情提供 `try_template` 调用模板、参数、已知取值及选择建议；参数错误时返回最接近的模板，便于修正调用。
- `eplan_api_try`（full，默认关闭）：经逐次审批在 scratch 项目试用已接入的属性读取、标量属性写入、对象方法和服务类方法；支持精确重载、枚举、`PointD` 和同项目对象定位器，未接入的签名明确拒绝，写入失败回滚、成功单步撤销。
- `eplan_api_try` 支持方向明确的文件参数：输入限于 Host 的 `imports`，输出限于每次新建的 `exports` 子目录，结果附路径、大小和 SHA-256；目录、打印/发送、主数据、设置及隐式目的地仍不开放。
- `eplan_action_try`（full，默认关闭）：经逐次审批在 scratch 副本试用目录中的非白名单动作，执行前检查禁止表，结果和审计明确标记为未验证。
- `eplan_symbol_search`（full，只读）：分页查询项目已有符号库、变体、类型、描述及连接点，支持名称或描述过滤 `text_contains` 和语言选择 `language`。
- `eplan_page_open`（full，只读）：按项目和精确页名打开或聚焦编辑器；不修改工程数据，不产生撤销步骤，离线时拒绝。
- 设备放置、连线创建/生成、图元创建/移动、宏插入和 DXF 导入支持 `show_page=true`，执行或 dry-run 前打开目标页，默认关闭；开页失败仍可继续，结果通过 `pageShown` 和提示说明。
- `eplan_placement_create`（full）：一次创建 1–50 项文字、直线或矩形，文字须指定语言或声明与语言无关；失败整批回滚，成功一次撤销。部分页界和文字完整外框无法在 dry-run 中确认。
- `eplan_placement_update`（full）：整体移动无组符号及其属性文字，或自由文本、直线、矩形；预览栅格、外框、碰撞和自动线影响，拒绝组与宏组；失败整体撤回，无变化不留撤销步，成功一次撤销。逻辑连接须另行生成。
- `eplan_connection_create`（full）：按显式正交路径放置角、T 和电位连接符号并生成连接；共两个撤销步骤，生成或读回失败时自动撤销本次改动。
- `eplan_connection_create` 可在既有自动连接线段上放置方向对齐的 T 节点并引出支路，保留原逻辑连接；远端尚无器件时允许零新增逻辑连接。
- `eplan_connection_create` 支持 `interruption_point`：显式指定共享业务名、方向、坐标及对端页范围，拒绝歧义配对，结果返回 EPLAN 双向对端和计算引用。
- `eplan_connection_create` 支持电缆定义及显式芯线：预览每芯归属、编号、颜色和截面，执行后核对结果；一次调用有 6–8 个撤销步骤，失败自动补偿，补偿结果不确定时报 `unknown`。
- `eplan_cabinet_list` / `eplan_cabinet_bind`（full）：查看和绑定多柜映射，预览五层配置来源，换锁前要求确认；`eplan_context_get` 按所选柜隔离配置和对象。
- 工程包支持第 2 版清单、知识文档和项目绑定，继续读取旧格式且不改写已安装包；新增配置项、配置层、事实集、规则、工作流和样例校验，问题带 JSON Pointer。
- `eplan_asset_review`（full，本地数据）：由工程师在独立窗口审核候选、关闭缺口或提升正式规则；`eplan_pack_build` 拒绝缺少来源的 mandatory 规则及缺少匹配本机确认的 official 资料。
- `eplan_rule_check`（full，只读）：按柜检查本机已审核并锁定的规则，分页返回四值结果和证据；`fix_plan` 只生成声明的修复计划，遇到未知结果或冲突时拒绝，不直接修改工程。
- `eplan_import_pdf_comments`（full）：从本项目经 EPLAN 导出并由 Acrobat/Reader 批注的 PDF 导入便笺 Text，忽略插入标记、下划线等；dry-run 不预测新增数，执行成功一次撤销，重复导入无新增时不留撤销步。

### Changed

- `eplan_device_place` 支持无部件库符号、单端子及父端子排、PLC 数字输入点和 NO/NC 触点；可设置连接点代号、PLC 地址、符号地址、功能文本及原生属性显示，读回不符整体回滚，成功一次撤销。
- `eplan_device_place` 支持按位置标识和两角放置原生虚线位置框，预览页界及同页冲突；框内设备保留自身结构，成功一次撤销。
- `eplan_plc_get` 增加原生父关系、手工符号地址、计算的卡页引用及同名无父辅助表达计数；放置后的派生值可用读工具复核。
- `eplan_page_get` 可选 `include_drawings=true`，读取栅格、边界来源、绘图对象定位器、位置和属性；默认每页 50 项、最多 200 项，支持预算裁剪和过期游标。
- `eplan_connection_list` 增加 `response_format=detailed`，返回可直接用于电缆芯线规划的连接定位器，默认简洁输出保持不变。
- `eplan_workflow_plan` 支持多柜绑定、五层配置、锁定工作流及规则来源；`eplan_plan_apply` 在审批后和每步前复核柜范围、配置、知识版本和对象，发现变化或越界时拒绝并要求重新规划。
- 发布 ZIP 根目录附带 `README.md`，说明运行要求、EPLAN 授权条件与支持矩阵。

### Fixed

- 写入（尤其是插入大宏）后的布局检查不再让 EPLAN 长时间忙；写后的空位建议最多计算 2 秒，超时的项目提示用 `eplan_page_review` 检查。
- 页上已有 T 节点或角节点时可以继续连线，电缆芯线可选在含母线节点的页上；中断点摘要不再因体积超限被截断，连接不符时指出具体对象。
- 连线可接到偏离栅格的引脚（如 IEC 端子 `X`），导线和节点可穿过或位于位置框内；路径被拒绝时指出检查项及阻挡对象。
- 放置 PLC 连接点或新建端子排时，不再在页面原点额外留下器件或端子排定义。
- `eplan_connection_generate` 无新增或删除连接时，不再留下空撤销步骤。
- 机器繁忙、其他程序频繁开关命名管道时，不再偶发 `EPLAN_NOT_CONNECTED`；其他 EPLAN 实例启停时保留仍在运行的实例选择和对象游标。
- `eplan_object_find` 接收 `cursor=null` 时正确返回第一页；API 读取被拒绝时返回明确失败，不再误报为结果未知。
- Host 遇到未预期错误时向 stderr 报告，避免直接崩溃。

### Security

- API 试调用拒绝跨项目参数、共享数据及外部资源访问，并排除销毁、初始化、冻结、锁定等不适合试用的操作；文件输入哈希绑定本次审批。
- API 和动作试调用比较执行前后状态，超时或模态窗口导致结果未知时暂停写入，等待工程师确认。
- 插件每次握手重新检查 API Extension 授权；授权或能力未确认时，参考项目提取只报告缺口，不读取事实。

## [0.4.0] - 2026-10-27

### Added

- `eplan_property_set`（full）：设置功能文本 20011 或技术参数 20027；功能文本只改指定语言，支持 dry-run，失败整体回滚，成功一次撤销。
- `eplan_page_text_replace`（full）：预览并按字面替换页面自由文本，支持组内及多语言文字；不改图框、属性放置、功能文本或路径文本，成功一次撤销。
- `eplan_page_update` 增加 `plot_frame`：切换为项目其他页已使用的图框，分别返回设置值和实际图框，与页描述修改合用一个撤销步骤。
- `eplan_translation_import`（full）：导入 UTF-16 制表符分隔的词汇表，每次审批并先备份为 `.zw7`；只新增词条和补全空译文，保留已有译文。
- `eplan_action_run` 新增 `synchronize/MULTILINE`、`synchronize/OVERVIEW` 和 `SwitchProjectType`，共 15 个白名单条目；均可一次撤销，拒绝切换到当前项目类型，`synchronize/SINGLELINE` 仍未开放。

### Changed

- `eplan_property_schema` 将 20011、20027 标记为可写；`full` 工具集增至 75 个工具。继续支持 EPLAN 2026（Electric P8、Pro Panel），需要 API Extension 授权。

## [0.3.0] - 2026-10-26

### Added

- `eplan_workflow_plan` 将 1–20 步建页、改页、删页、设备改名、插宏或页/设备重编号编译为不可变计划；`eplan_plan_get` 分页审阅预期和差异，计划也可作为 MCP Resource 读取。
- `eplan_plan_apply` 按计划最高风险级别整体审批，执行前备份，每步独立事务、一次撤销一步，首次失败即停；支持 dry-run，`eplan_task_get` 跟踪任务。计划范围被手工修改时返回 `PLAN_STALE` 并列出冲突。
- `eplan_context_get` 返回计划所需的默认柜上下文、来源、哈希及缺口；计划工具可通过 `workflow` 和 `full` 工具集使用。
- `eplan_property_schema` 按 ID 或正式名称查询 8,155 条可读属性；`eplan_property_get` 批量读取项目、页、设备和定位器目标的属性，区分值、空值、不适用和拒绝读取，支持分页及预算截断。
- 新增[计划使用指南](docs/guides/plans.zh-CN.md)（[English](docs/guides/plans.en.md)）。

### Changed

- 新计划只核验受影响页的内容并筛查全项目名称，结果列出未观察页及核验覆盖范围；旧计划继续按原方式执行。
- 计划支持至 3,000 页的项目；10,000 页尚不支持。重编号计划限全项目最多 50 页、10,000 个对象，含无页功能的设备改名尚不支持；`full` 工具集增至 72 个工具。

### Fixed

- 设备改名计划不再把连接端点名、计算地址及省略结构段的可见名等派生变化误报为计划外改动。
- `eplan_macro_get` 读取没有 2D 外框的宏（如 3D 宏）时，`size` 返回 null 并说明原因，不再报内部错误。

### Security

- 通用属性读取不返回路径、联系人等个人数据；计划含 Destructive 步骤时，scratch 内也必须审批。

## [0.2.0] - 2026-10-23

### Added

- `eplan_action_run`（full，默认关闭）：在 `config.json` 启用 `actionRun` 后，可运行 12 个白名单 EPLAN 动作，逐条校验参数并比较执行前后状态。
- Workbench 状态页可勾选 EPLAN 变体并逐个注册或撤销插件；命令行新增 `addin-variants`。
- `client-config claude-code --plugin` 和 Workbench 的“生成本地市场”生成本机 Claude Code 插件市场，写入实际 Host 路径。
- 虚拟机侧新增 `serve --http --address <IPv4>`，通过 Streamable HTTP 连接固定的 EPLAN 实例；提供 `vm-key create|show|delete`、`vm-firewall show|add|remove`、`vm-instances` 及 `doctor` 检查。
- 宿主机侧可用 `client-config <客户端>... --vm --url http://<IPv4>:<端口>/mcp --key-stdin` 写入 `eplan-vm`，用 `--vm --remove` 移除、`vm-test` 检查连接；Claude 桌面版不支持远程配置。
- Workbench 提供虚拟机地址、端口、密钥和防火墙面板，以及宿主机连接向导；HTTP 服务随 Workbench 退出而停止。
- 中英文入门指南增加语言切换及 ZIP 安装、升级、卸载和虚拟机部署说明，Claude Code 插件同步附带指南。

### Security

- 虚拟机连接使用 Bearer 认证、失败限流及 `httpAllowedOrigins` 允许列表，密钥经 DPAPI 保存、只经 stdin 传给配置命令；防火墙只放行指定宿主机 IPv4，审计记录来源地址。
- HTTP 连接不加密，仅用于宿主机与虚拟机间的私有网络；宿主机客户端配置中的密钥为明文。

### Breaking

- 交付改为 `EplanMcp-<version>.zip`，停止提供 Inno Setup 安装包。解压到短且固定的目录，运行 `workbench\EplanMcp.Workbench.exe`；升级前关闭 EPLAN、Workbench 和客户端并整体替换程序目录，卸载前先在 Workbench 撤销注册。数据根目录仍为 `%USERPROFILE%\EplanMcp`。

## [0.1.0] - 2026-10-22

### Added

- 首个预览版，支持 EPLAN 2026（Electric P8、Pro Panel），需要 API Extension 授权；提供按用户离线安装包、SHA-256、CycloneDX SBOM 和构建来源证明，0.x 程序未签名。安装和使用见[中文入门指南](docs/getting-started/zh-CN.md)。
- `eplanmcp` 提供 stdio MCP 服务，插件随 EPLAN 启动；`eplan_session_status` 查看连接，`eplan_session_select` 在多个 EPLAN 实例间选择，`eplan_tool_usage` 查询工具用法。
- `eplanmcp serve --profile core|full`：`core` 提供 25 个常用工具且不含 Destructive 工具，默认 `full` 提供全部 64 个工具。
- `eplan_project_list` / `eplan_project_get` 查看已打开项目、当前项目及工程概况；`eplan_page_list` / `eplan_page_get` 按名称、类型或描述查页并读取页内设备。
- `eplan_device_list` / `eplan_device_get` 查看设备、部件、辅助功能和连接点；短标识有歧义时返回 `AMBIGUOUS_NAME` 和候选。`eplan_connection_list` 查询连接两端、截面和颜色，`eplan_part_list` 汇总项目部件数量。
- `eplan_cable_get` 读取电缆型号、芯数、长度、两端及每芯颜色和线号；`eplan_terminal_strip_list` / `eplan_terminal_strip_get` 查看端子排、端子顺序、内外侧连接和跨接。
- `eplan_plc_get` 读取 PLC 盒、站、机架、槽、部件及通道地址、符号地址、数据类型、功能文本和另一端；`eplan_part_search` 按部件号、型号、订货号、描述或制造商搜索系统部件库。
- `eplan_message_list` 读取系统消息，`eplan_check_list` 读取已有检查结果；`eplan_revision_list` 查看修订状态、记录及已修改页，不创建或完成修订。
- `eplan_macro_list` / `eplan_macro_get` 分页查找窗口宏并读取表达类型、变体、尺寸和设备，无需插入项目；`eplan_placeholder_list` 查看占位符、变量及值集，`eplan_placeholder_apply` 应用一个值集，成功一次撤销。
- `eplan_project_create` 从 `imports` 中的 `.zw9` 新建或 `.zw1` 还原为关闭的新项目，拒绝已有目标，失败只清理本次产物；scratch 外逐次审批。
- `eplan_page_create` 批量新建 1–50 页，`eplan_page_update` 修改指定语言的页描述并保留其他语言；均支持 dry-run、失败整批回滚、成功一次撤销。
- `eplan_device_update` 同步修改主、辅助功能的设备标识并检查重名；`eplan_device_place` 按部件号插入部件宏并设置标识，缺部件、宏或指定变体时拒绝，成功一次撤销。
- `eplan_macro_insert` 将窗口宏放入电路图页，支持自动编号或保留标识；保留标识导致设备合并、或宏外框超出已知页界时拒绝，成功一次撤销。
- `eplan_connection_generate` 在 1–50 页上生成连接，返回新增、删除及数量；`eplan_check_run` 按方案检查全项目或指定页，`eplan_report_update` 更新全部或指定报表页，均支持 dry-run 和一次撤销。
- `eplan_page_delete` 精确删除 1–50 页；`eplan_page_renumber` 重编 1–50 页页号并检查冲突；`eplan_device_renumber` 按方案给指定页或全项目设备编号，可只处理带 `?` 的标识。
- `eplan_terminal_renumber` / `eplan_cable_renumber` 按方案重编端子或电缆标识；`eplan_connection_number` 按方案编线号并选择是否覆盖已有线号。上述删除及编号操作每次审批、执行前备份，成功一次撤销。
- `eplan_export_pdf` / `eplan_export_dxf` 导出 PDF、DXF 或 DWG；`eplan_export_image` 将 1–20 页导出为 PNG/JPG，最多 4 页可直接返回图片内容；`eplan_export_pxf` 导出完整 EPJ（PXF）项目。
- `eplan_export_parts` 导出项目部件 XML，`eplan_export_labels` 按标签方案导出文本或 Excel，`eplan_export_untranslated` 导出指定语言的缺失词表；`eplan_translation_export` 导出系统翻译库的文本或 XML。
- `eplan_part_export` 导出系统部件库 XML、EDZ 或可恢复的 `.zw6` 备份；XML/EDZ 可选 1–500 个部件号，备份只能整库导出且不支持 SQL Server。所有导出写入每次新建的 `exports` 子目录，失败或取消时清理本次目录。
- `eplan_import_dxf` 将 `imports` 中不超过 100 MB 的 DXF/DWG 插入已有页，成功一次撤销；`eplan_import_pxf` 将不超过 2 GB 的 EPJ（PXF）导入首个 scratch 根目录的 `imported`，不改已有项目。
- `eplan_import_list` 导入设备清单或项目部件清单，`eplan_import_plc` 导入 PLC 总线数据；两者每次审批并先备份项目，不承诺用 Ctrl+Z 恢复。PLC 文件预检有限，导入后发现设备重名会回滚。
- `eplan_part_import` 将不超过 50 MB 的部件 XML 导入系统部件库，支持新增、更新或替换，拒绝 SQL Server 库；`eplan_part_sync` 将项目部件同步到系统部件库，并将仅引用的部件存入项目。每次审批并先备份，部件库侧只能从备份恢复。
- `eplan_language_set` 设置显示语言、增加翻译语言或设置源语言，不翻译文本，须用原值再次调用恢复；`eplan_language_remove` 删除语言及其译文，`eplan_project_translate` 用翻译库翻译全项目或 1–200 页，须先设源语言和翻译语言；后两者每次审批并先备份。
- `eplan_cable_generate` 按明确方案生成或更新自动电缆，保留旧名；每次审批并先备份，dry-run 只列现状，不承诺一步撤销。
- 工程包工具（full，无需 EPLAN）：`eplan_pack_import` 导入资源为开发包，`eplan_pack_list` / `eplan_pack_get` 查看版本、要求和缺口，`eplan_pack_build` 校验后冻结为不可变版本；支持依赖和项目版本锁定，同一版本内容不同则拒绝。
- `eplan_asset_search` / `eplan_asset_get` 查询包资源、来源、哈希、事实及可用性；`eplan_asset_update` 编辑候选、模块、物料对应表和缺口，不能将候选直接改成正式规则。PDF、DOCX、XLSX 只保存，不解析内容。
- 工程包提供只读资源 `eplanpack://{pack}/{version}/manifest`、`eplanpack://{pack}/{version}/{kind}/{name}` 和 `eplanpack://{pack}/{version}/previews/{file}`；`eplanmcp pack scan|check|export|install` 可离线扫描、检查、导出和安装 `.eplanpack`。通过工具安装外来包须工程师在 EPLAN 中确认，拒绝或超时不安装。
- Workbench 提供中英文界面、浅色/深色/跟随系统/高对比主题、托盘及登录时启动；可处理审批、查看结果和审计详情、校验审计链、运行状态自检，以及预览和配置 MCP 客户端。
- `eplanmcp doctor` 检查安装、插件、授权和运行环境，报告导出与备份占用；`doctor --format json`、`audit verify --format json` 输出结构化结果，见[架构 5.2](docs/architecture/architecture.md#52-host-组件)。
- `eplanmcp client-config <客户端>… [--dry-run|--remove] [--format json]` 支持 Claude Code、Claude 桌面版、Codex、Gemini CLI、Qwen Code、Kimi Code CLI、CodeBuddy Code、OpenCode、Qwen Agent、Cursor 和 VS Code Copilot；只更新 `eplan` 条目并先备份，检测到并发改动则拒绝。JSONC 注释在改写后丢失，可从备份恢复。
- `eplanmcp install-addin [--variant <变体>]… [--format json]` / `eplanmcp uninstall-addin [--format json]` 注册或撤销所选 EPLAN 变体的插件，核对实际加载路径；EPLAN 已运行或插件属于其他 Windows 用户时拒绝，遇到登录或授权窗口交由工程师处理。
- 列表统一分页并在超出预算时截断提示，多语言读取支持 `language`；长调用报告进度，取消在安全点生效，提交前可回滚，已提交的修改保留。
- 附带 Claude Code 本地插件市场、英文入门技能及中文使用指南。
- `eplan_project_create`（full）：从 imports 中的 `.zw9` 新建或 `.zw1` 还原为关闭的新项目；scratch 外逐次审批，拒绝任何已有目标，完成后检查兼容性，失败仅清理本次产物。
- Claude Code 本地插件市场与英文入门技能，以及中文入门指南（安装、校验、客户端连接、审批、自检和卸载）。
- M5 按用户离线安装器、按标签构建并实测插件的发布脚本，以及附 SHA-256、CycloneDX SBOM 和来源证明的草稿发布流程。
- 项目启动：架构方案、ADR、设计规范、开发流程与仓库基础配置。
- `eplanmcp`（stdio MCP 服务器）及前两个只读工具：`eplan_session_status`（EPLAN 与插件的连接状态，EPLAN 不可用时也正常返回并给出下一步提示）、`eplan_project_list`（已打开的项目和当前项目，分页）。
- EPLAN 插件 `EplanMcp.Bridge.Eplan`：随 EPLAN 启动，通过只允许当前用户访问的命名管道为 `eplanmcp` 服务。
- `eplanmcp doctor`：检查 EPLAN 安装、运行状态、插件连接与授权、MSIX 环境、用户 PATH 长度，中英文输出。
- M2 只读工具：
  - `eplan_project_get`：项目概况和计数；
  - `eplan_page_list`、`eplan_page_get`：按名称前缀、类型、描述过滤页，页上的设备；
  - `eplan_device_list`、`eplan_device_get`：设备、部件、辅助功能和连接点；短标识对应多个结构时返回 `AMBIGUOUS_NAME` 和候选；
  - `eplan_connection_list`：按设备或页列出连接的两端、截面和颜色；
  - `eplan_part_list`：项目中的部件，按部件号汇总数量；
  - `eplan_message_list`：EPLAN 系统消息；
  - `eplan_check_list`：项目中已有的检查结果（不运行检查）；
  - `eplan_session_select`：同时运行多个 EPLAN 时选择要操作的实例；
  - `eplan_tool_usage`：按需返回每个工具的详细用法。
- 宏的只读工具（M3b 的准备）：
  - `eplan_macro_list`：按名称列出 EPLAN 宏目录中的窗口宏（`.ema`），分页；不跟随链接和联接点，扫描最多 2 秒；
  - `eplan_macro_get`：不插入地读出一个窗口宏的表达类型、变体、尺寸和其中的设备，项目不变。
- 写入审批（`config.json` 中 `writesOutsideScratch` 为 `approve` 时）：scratch 之外的写入先在 EPLAN 中弹出审批对话框，按系统界面语言列出工具、项目和变更清单；期限内未批准即自动拒绝，期限默认 120 秒，可用 `config.json` 中的 `approvalTimeoutSeconds` 设为 60–300 秒，`eplanmcp doctor` 显示当前值；等待期间，客户端提供了进度令牌时，每 15 秒收到一次进度通知，说明正在等待 EPLAN 中的审批；对话框打开期间其他调用返回 `EPLAN_BUSY`；每个决定单独写入审计。
- 第一个写工具 `eplan_page_create`（M3a）：按完整页名一次新建 1–50 页，可带页描述；在 EPLAN 中只占一个撤销步骤"AI: eplan_page_create"，任何一页失败或读回与请求不同时全部回滚；支持 `dry_run`；经写入流水线（策略、审批、审计、结果取回），scratch 之外的项目默认拒绝。
- 写工具 `eplan_page_update`：改 1–50 页在一种语言下的页描述，其他语言保持不变，空串清除该语言；已经相同的页不改，全部相同时不写入；同样只占一个撤销步骤，读回不一致时全部回滚。
- 写工具 `eplan_device_update`（这一版只改设备标识）：主功能和全部辅助功能（包括其他页上的触点）一起改名，`new_tag` 以 `-` 开头时保留原结构；新标识已被其他设备使用时拒绝（EPLAN 自己不检查重名）；EPLAN 丢掉新标识的一部分、或仍有功能带旧标识时回滚。
- 写工具 `eplan_connection_generate`（M3b）：在指定的 1–50 页上执行"生成连接"（只更新，不重建全部，也不对整个项目执行），把页上画出的连线变成连接；结果列出每页前后的连接数和新增、删除的连接（合计最多 100 条，删除的优先）；`dry_run` 只返回现有的连接数；同样只占一个撤销步骤。
- 写工具 `eplan_macro_insert`（M3b）：把宏目录中的窗口宏插到电路图页的指定位置（毫米，宏外框的左上角），设备取页的结构并在图上显示标识；默认 `numbering: number` 由 EPLAN 分配不冲突的标识，`keep` 保留宏中的标识、与已有设备合并时回滚；宏路径只接受宏目录之下的相对路径；外框超出幅面已知的页时拒绝；只占一个撤销步骤"AI: eplan_macro_insert"。
- 写工具执行时的进度（M4）：客户端提供了进度令牌时，等待 EPLAN 期间每 5 秒收到一次心跳，说明在做什么和已用秒数（如 `Creating pages (10 s)`）；插件报告的分步进度（如 `Exporting page 3 of 20`）随到随转。与等待审批的进度连在一起，数值只增不减。
- 取消工具调用（M4）：客户端取消调用时，还在队列中的请求不再发给 EPLAN；已经发出的，插件在下一个安全点停下（不打断正在进行的一次 EPLAN 调用），写操作在提交前停下就回滚；已提交的不撤回。写操作照样取回真实结果写入审计，停下的记为 `CANCELLED`。旧版插件不支持时，已发出的调用照常执行完。
- 写工具 `eplan_check_run`（M4）：对整个项目或指定的 1–50 页运行 EPLAN 的检查，替换项目中的检查消息；结果给出各级别的消息数和前 20 条消息（格式同 `eplan_check_list`，不含已处理的）；省略 `scheme` 时用 EPLAN 当前的检查方案，EPLAN 不认识的方案回滚并返回 `INVALID_ARGUMENT`；`dry_run` 只返回检查的页数和现有的消息数；只占一个撤销步骤"AI: eplan_check_run"。`eplan_check_list` 在没有检查消息时改为提示用它运行检查。
- 写工具 `eplan_report_update`（M4）：更新项目的全部报表（EPLAN 的"生成项目报表"），或只更新指定的 1–50 张报表页；结果给出更新前后的报表页数，以及按页名比较得出的新增、删除的报表页（合计最多 100 条，删除的优先）；没有增删时也提交；`dry_run` 只返回现有的报表页数；不是报表页时返回 `INVALID_ARGUMENT`；只占一个撤销步骤"AI: eplan_report_update"。
- 导出工具（M4，ADR-0012）：`eplan_export_pdf` 把页或整个项目导出为一个 PDF（可选方案、彩色/灰度/黑白、文件名），`eplan_export_dxf` 导出为 DXF 或 DWG（每页一个文件，格式、子目录和文件名由方案决定）。
  - 每次调用只写进服务器新建的目录 `<数据根目录>\exports\<项目名>\<UTC 时间>-<后缀>\`，没有目录参数；结果返回这个目录和其中的文件（最多列出 50 个）；
  - 不改项目的设计数据，不需要审批，注解为 `readOnlyHint: false`；导出后 EPLAN 下次默认的方案不变；
  - 出错、超过 300 秒或取消时删除本次的目录，目录中出现链接、联接点或没有文件时按失败处理；等待期间每 5 秒发一次进度通知；
  - `eplanmcp doctor` 报告导出目录中的导出次数和占用的空间（不自动清理）。
- 导出工具 `eplan_export_image`：把 1–20 页导出为 PNG 或 JPG 图片（每页一张，宽 200–4000 像素），结果按页列出图片文件；`inline` 为 true 时（最多 4 页）图片也作为 MCP image 内容放进工具结果，让模型能"看"页面。目录、失败和取消的处理与 `eplan_export_pdf` 相同；等待期间插件逐页报告的进度（如 `Exporting page 3 of 20`）随到随转，与心跳连在一起，数值只增不减。
- Destructive 写入流水线（M4，ADR-0013）：每次都请求审批（scratch 中也一样），批准后先把整个项目复制到 `<数据根目录>\backups\<项目名>\<UTC 时间>\` 再执行，备份失败时不执行并返回新的错误码 `BACKUP_FAILED`；审计记录备份的路径和是否完成；备份从不自动删除，`eplanmcp doctor` 报告备份的份数和占用的空间。
- 第一个 Destructive 工具 `eplan_page_delete`（M4）：按完整页名精确删除 1–50 页；每次都要工程师在 EPLAN 中审批（scratch 中也一样），审批对话框列出删除后在其他页上失去主功能的设备（如电缆的芯线）；执行前先备份整个项目，结果的 `backup` 给出副本路径；只占一个撤销步骤"AI: eplan_page_delete"，读回不符时全部回滚；提示用 `eplan_report_update` 更新报表。
- Destructive 工具 `eplan_device_renumber`（M4）：让 EPLAN 按编号方案给指定页（1–50 页）或整个项目的设备标识编号，默认只给带 `?` 的标识编号（如插入宏后的 `-?QA1`），也可以全部重新编号；每次审批、执行前备份、一步撤销；结果按设备列出改变的标识，以及仍带 `?` 的标识（EPLAN 不一定都能编号）；方案名不存在时返回可用的方案。
- Destructive 工具 `eplan_page_renumber`（M4）：让 EPLAN 按结构给 1–50 页新的页号（起始值、步长，可保留页号中的字母，子页保留、连续编号或转成主页）；每次审批、执行前备份、一步撤销；新页号与已有页冲突时回滚并列出重名的页；结果列出每页的新旧页名，提示用 `eplan_report_update` 更新目录等报表。
- EplanMcp Workbench（M5，ADR-0014、ADR-0017）：桌面程序，中英文界面，浅色、深色、跟随系统和高对比主题，每个用户和数据根目录一个，关闭窗口时缩到托盘。这一版有：
  - 审批队列：Host 经只允许当前用户访问的命名管道发来的审批问题，按到期时间排列，显示工具、级别、项目、EPLAN 实例、未经验证的客户端名称、变更清单（纯文本，去掉双向控制字符）和倒计时；"批准""拒绝"两个按钮样式相同、不是默认按钮、没有快捷键，问题显示 500 毫秒后才响应；新问题只让托盘和任务栏提醒，不抢焦点；已回答的问题和执行结果在"已回答"中；
  - 等待中的问题达到 50 个时不再接收，Host 改用 EPLAN 中的对话框；退出时如果还有等待中的问题先确认，退出后它们按"已取消"返回；
  - 审计、状态、MCP 与客户端三个页面暂为占位。
- `eplanmcp doctor --format json`、`eplanmcp audit verify --format json`（M5）：输出带 `schemaVersion` 的 JSON，供 Workbench 使用；doctor 的每一项带稳定的 `id`，便于分组。结构见[CLI 参考](docs/reference/cli.md#json-输出契约)。
- `eplanmcp client-config <客户端>… [--dry-run|--remove] [--format json]`（M5）：为 Claude Code、Claude 桌面版、Codex、Gemini CLI、Qwen Code、Kimi Code CLI、CodeBuddy Code、OpenCode、Qwen Agent、Cursor、VS Code Copilot 写入名为 `eplan` 的 MCP 服务器，运行本机的 `eplanmcp.exe serve`；可以一次写多个客户端，分别报告结果，任何一个失败时退出码为 1。
  - 只改 `eplan` 这一项，文件中的其他服务器和设置都保留；已有的 `eplan` 是本地服务器（有 `command`）时只更新命令和参数，其他字段保留，否则整项替换；JSONC 文件中的注释在改写后丢失（原文件在备份中），Codex 的 TOML 中无法安全地逐行修改的写法拒绝写入；
  - 写入前把原文件复制为同目录的 `<文件>.bak_<UTC 时间>`；读取之后文件被其他程序改动时拒绝写入；
  - `--dry-run` 只显示 `eplan` 这一项改动前后的写法，不显示文件的其他部分；
  - 写入和移除都记在 `<数据根目录>\state\clients.json`；`--remove` 不带客户端时移除记录中的全部条目，供卸载使用；
  - 拒绝的情况：由 MSIX 打包的客户端（如 Claude 桌面版）启动时一律拒绝（写入会被重定向）；已安装 Claude Code 插件 `eplan-mcp@eplanmcp-local` 时不为 Claude Code 写配置（插件已经提供服务器）；Claude 桌面版运行时不改它的配置文件，请先完全退出；配置文件是符号链接或路径不完整时不修改；桌面版以 MSIX 安装时写包中的那份文件；
  - JSON 结构见[CLI 参考](docs/reference/cli.md#json-输出契约)。
- `eplanmcp install-addin [--variant <变体>]… [--format json]`、`eplanmcp uninstall-addin [--format json]`（M5）：用 EPLAN 官方的命令行 `EplApiModuleAction /register` 注册 `eplanmcp.exe` 旁的插件，支持范围内每个 EPLAN 版本的所选变体各一次（不带 `--variant` 时为全部已安装的变体）。先去掉插件文件的下载标记；有 EPLAN 正在运行时拒绝；等命令启动的 EPLAN 加载插件（最多 2 分钟），从握手核对加载的是这个 DLL，之前注册在别处时先撤销、再注册，那个位置在另一个 Windows 用户的目录中时不再继续；然后请 EPLAN 正常关闭，从不强制结束，显示登录、授权对话框或系统消息窗口时留给工程师处理。注册记在 `state\addins.json`，`uninstall-addin` 按记录逐个 `/unregister`。任何一项失败时退出码为 1。
- 列表工具统一分页（`nextCursor`），结果超出大小预算时截断并提示；来自项目的文本按不可信数据处理。
- 支持 `language` 参数读取多语言文本。
- M4c 只读工具（只在 `full` 中）：
  - `eplan_cable_get`：一根电缆的型号、芯数、截面、长度、两端的设备、部件，以及每根芯线的颜色、线号和两端；芯线分页。列出电缆用 `eplan_device_list`（`category: "Cable"`）。
  - `eplan_terminal_strip_list`：项目中的端子排及每排的端子数、跨接数，按名称排序、分页，可按名称中的文字过滤。
  - `eplan_terminal_strip_get`：一个端子排的端子（按排列顺序，分页），每个端子内侧、外侧连接的另一端、与之跨接的端子、所在页；不带结构的短名称（如 `-XD4`）在多个位置出现时返回 `AMBIGUOUS_NAME`。
  - `eplan_plc_get`：一个 PLC 盒的站、机架、槽、部件，以及每个通道对象（同一连接点名称可以有输入、输出或两种表达）的页、是否主表达、地址、符号地址、数据类型、功能文本和连接的另一端（空闲通道没有），通道分页。列出 PLC 盒用 `eplan_device_list`（`category: "PLCBox"`）。
  - `eplan_part_search`：在 EPLAN 配置的系统部件库中按部件号、型号、订货号或描述中的文字查部件，可限定制造商；给出描述和是否有宏，分页。部件库只读打开、用完关闭，从不修改。
  - `eplan_revision_list`：项目的修订状态（按链接文件扩展名：`none`、`tracking`、`completed`，后者写保护、写工具会失败）、修订记录（名称、说明、用户、UTC 日期，分页）、EPLAN 列为已修改未完成的页和带修订标记的页数。只读，不新建、不完成修订。
- M4c 导出工具（只在 `full` 中；与 `eplan_export_pdf` 相同，只写进每次新建的导出目录，失败或取消时删除该目录）：
  - `eplan_export_pxf`：整个项目（含连接）导出为一个 EPJ（PXF）文件，可选带上主数据；导出前要求导出目录所在的磁盘至少有 2 GB 可用空间。
  - `eplan_export_parts`：项目的部件清单导出为一个 XML 文件（不提供 CSV，见 api-notes A25）。
  - `eplan_export_labels`：按 EPLAN 的标签方案（必填，如 `Device tag list`、`Terminal diagram`）导出一个清单文件，扩展名按方案的输出格式（文本 `.txt`、Excel `.xlsx`）；方案不存在时列出可用的方案，其他输出格式暂不支持。
  - `eplan_export_untranslated`：导出项目中在翻译库里找不到指定语言译文的文本（缺失词表），制表符分隔的 Unicode 文本；语言必须是项目的翻译语言，否则列出项目的翻译语言。翻译库只读。
  - `eplan_part_export`：把 EPLAN 配置的系统部件库（不是项目）中的部件导出为一个 XML 或 EDZ（带宏和图片，慢）文件，可用 `parts` 指定 1–500 个部件号（每个变体都导出），默认全部；结果给出文件中的部件数（一个部件号只算一次）和变体数；有部件号找不到时返回 `PART_NOT_FOUND`，不导出。目录为 `exports\parts-database\…`；部件库只读打开、用完关闭。
  - `eplan_translation_export`：把 EPLAN 配置的系统翻译库（不是项目）整个导出为一个制表符分隔的文本（默认）或 EPLAN 的 XML 文件，不按语言过滤；目录为 `exports\translation-database\…`，时限 60 秒；翻译库只读。
- 写工具 `eplan_language_set`（M4c，只在 `full` 中）：设置项目图上显示的语言（按顺序，`["##_##"]` 清除），往翻译语言中加语言，或用 `source` 设置项目的源语言（`##_##` 清除；源语言未设置时 EPLAN 什么也不翻译），不翻译文本；结果给出前后的翻译语言、显示语言和源语言；设置已是所求时不写入；失败或取消时把设置写回原值（EPLAN 的回滚不管项目设置），写回失败时返回 `OUTCOME_UNKNOWN` 并给出原值。Ctrl+Z 不能撤销语言设置，审批对话框和结果都说明用原来的值再调用一次来恢复。
- 写工具 `eplan_device_place`（M4c，只在 `full` 中）：按部件号在电路图页上放置一个设备，与工程师手工放置部件相同（EPLAN 插入部件的宏），并给它标识，以 `-` 开头时取页的结构；部件不在部件库中返回新的错误码 `PART_NOT_FOUND`，变体不存在、部件没有宏、标识已被使用时在执行前拒绝；设备的其他功能（如接触器的触点）留在未放置中，结果给出数量；一次 Ctrl+Z 撤销。
- 写工具 `eplan_import_dxf`（M4c，只在 `full` 中）：把导入目录 `<数据根目录>\imports\` 中的 DXF 或 DWG 文件（不超过 100 MB）作为图形插入一个已有的页，按页面缩放或保持原尺寸，放在页中央或左下角；只新增图形，一次 Ctrl+Z 撤销。`file` 只接受导入目录下的相对路径（拒绝绝对路径、UNC、`..`、链接和联接点，SEC-31），文件不在时返回新的错误码 `FILE_NOT_FOUND`；审批对话框、结果和审计都给出文件的大小和 SHA-256，审批之后文件变了就不导入（`PLAN_STALE`）。
- 写工具 `eplan_import_pxf`（M4c，只在 `full` 中）：把导入目录中的 EPJ（PXF）文件（不超过 2 GB）导入为新项目，新项目只建在第一个 scratch 根目录的 `imported\` 中，不打开、不改动已有项目，所以不需要审批；可能要一分钟以上，每 5 秒报告一次进度。名称已被占用时返回 `ALREADY_EXISTS`；导入失败或被取消时删除本次新建的文件，删不掉时返回 `OUTCOME_UNKNOWN`。结果给出新项目的路径。
- 写系统主数据的工具 `eplan_part_import`（M4c，只在 `full` 中，ADR-0019）：把导入目录中的部件 XML 文件（不超过 50 MB）导入 EPLAN 配置的部件库，部件库被所有项目共用。每次都要工程师在 EPLAN 的对话框中审批，scratch 中也一样；执行前用 `backup /TYPE:MASTERDATA` 把部件库备份到 `<数据根目录>\backups\masterdata\` 下的新文件夹，备份失败就不执行；导入撤不回，只能按备份恢复。`mode` 选择只加新部件（默认）、只更新、两者都做或整条替换；dry run 列出新增、更新、不变的部件号；执行后读回。SQL Server 上的库一律拒绝；`config.json` 的新配置 `masterDataWrites`（`approve` 默认 / `deny`）可以禁止这类写入，`doctor` 显示当前值。结果未知时暂停对这个库的写入，直到工程师确认。
- 写系统主数据的工具 `eplan_part_sync`（M4c，只在 `full` 中，ADR-0019）：把项目中存储的部件同步进 EPLAN 配置的部件库（"主数据 > 部件 > 同步"），`mode` 选择只加新部件（默认）、只覆盖或两者都做；EPLAN 同时把项目只引用的部件存进项目，所以也写项目。每次都要工程师审批，执行前备份部件库和项目；Ctrl+Z 只撤回项目一侧，部件库只能按备份恢复。dry run 列出新增、覆盖、存入项目的部件号；执行后读回两侧。
- 只读工具 `eplan_placeholder_list`（API 能力覆盖第一期的样例，只在 `full` 中）：列出项目或一页上的占位符对象（随带值集的宏进入项目），给出名称、所在页、值集、变量和引用的对象数；`detailed` 另给出每个值集中每个变量的值和占位符的当前值（按语言回退），分页。名称、值集数和变量名由从 API 目录生成的读取代码读出。
- 写工具 `eplan_placeholder_apply`（API 能力覆盖第一期的样例，只在 `full` 中）：给一页上的一个占位符应用一个值集（EPLAN 把值集中各变量的值写进占位符引用的对象），一次 Ctrl+Z 撤销；scratch 之外需要审批，审批对话框列出值集中各变量的值。按名称和值集找不到或找到多个时在执行前拒绝并列出页上的占位符；执行后读回占位符本身，结果给出前后的当前值和是否改变。调用 `ApplyRecord` 用的是从 API 目录生成的重载表。提交后的持久性和 Ctrl+Z 待实机验证。
- `eplan_part_export` 新增 `format: backup`（API 能力覆盖第一期的文件输出样例）：用 EPLAN 的主数据备份（与写部件库之前的备份相同）把整个部件库打成 EPLAN 能恢复的 `.zw6` 归档，写进本次新建的导出目录；不能与 `parts` 同用，文件名只能用 ASCII 字母、数字、`-` 和 `_`，SQL Server 上的库不做。action 的参数由从 action 目录生成的参数类写入。待实机验证。
- Destructive 工具 `eplan_language_remove`（M4c，只在 `full` 中）：从项目的翻译语言中删除 1–10 种语言，连同项目文本中这些语言的全部译文；每次审批、执行前备份；源语言和项目没有的语言返回 `INVALID_ARGUMENT` 和候选；结果给出受影响的文本数、前后的语言设置，EPLAN 同时改了显示语言时在提示中说明。失败或取消时译文由回滚撤回，语言设置由工具写回原值（EPLAN 的回滚不管项目设置）。
- Destructive 工具 `eplan_project_translate`（M4c，只在 `full` 中）：用 EPLAN 的翻译库把整个项目或 1–200 页（连同页上的功能）的文本，从项目的源语言译进项目的全部翻译语言；每次审批、执行前备份。源语言未设置（`##_##`，EPLAN 什么也不翻译）或没有别的翻译语言时返回 `INVALID_ARGUMENT`，提示用 `eplan_language_set` 的 `source`、`add`；没有缺少译文的文本时不审批。结果给出改动的文本数、前 50 个改动的文本（名称、属性、前后缺少的语言）和仍缺少译文的文本数（翻译库中没有词条，可用 `eplan_export_untranslated` 导出）；源语言的文本被改动时回滚。
- Destructive 工具 `eplan_cable_generate`（M4c，只在 `full` 中）：按三个明确的项目方案生成或更新自动电缆，固定保留旧名和只处理自动电缆；每次审批、执行前备份，预算 300 秒。dry-run 只列现状，结果按身份列最多 200 条变化；返回本次系统消息（最多 20 条，保留总数，读取失败仅警告）。不承诺无变化或一步撤销，`undo=null`。正向生成与完整读回待 Large 验证。
- M4c 编号工具（Destructive，只在 `full` 中；每次审批、执行前备份、一步撤销，方案必须给出，不存在时返回可用的方案）：
  - `eplan_terminal_renumber`：让 EPLAN 按端子编号方案给 1–50 个端子排或整个项目的端子编号，默认只给名称中 `:` 之后带 `?` 的端子编号，也可以按端子排中的顺序全部重新编号；结果列出改变名称的端子，以及仍带 `?` 的端子。
  - `eplan_cable_renumber`：让 EPLAN 按电缆编号方案给指定的 1–200 根电缆（完整标识或显示的标识）或项目的全部电缆重新编号，`keep_existing` 时保留已有标识、只编带 `?` 的；结果列出改变标识的电缆（可能两两互换）。
  - `eplan_connection_number`：EPLAN 的"连接编号"，按线号方案给整个项目的连接编线号；`overwrite` 决定已有线号的处理（默认 `except_manual` 保留手工输入的，`none` 都保留，`all` 全部覆盖）；结果列出线号改变的连接（前后线号和所在页，最多 200 条）和编号后有线号的连接数。
- M6 工程包（只在 `full` 中，不需要 EPLAN）：
  - `eplan_pack_import`：把工程师放进导入目录（或 `config.json` 新增的 `packSources` 列出的根目录）的宏、表格、图框、文档、物料对应表、基本项目、备份和项目导入开发包 `packs-dev\<包 ID>\`，自动生成清单和资源索引；规则和物料对应表一律是候选；项目默认只引用（需要 `packSources`），工程师同意后可以复制进包；需要 EPLAN 才能判断的事项记为缺口；只写数据目录，写审计 `pack_change`。
  - `eplan_pack_list`：开发包和已安装的版本，以及锁定每个版本的项目；只读清单，不算哈希。
  - `eplan_pack_get`：一个包的清单摘要、完整校验发现的问题（包括按 `packSources` 核对的引用资源）、开放的缺口，以及连接的 EPLAN 和本服务器是否满足包的要求（EPLAN 版本、变体、Bridge 操作、工具为硬要求；能力目录条目只警告）。
  - `eplan_pack_build`：把开发包冻结为不可变的版本，安装到 `packs\<包 ID>\<版本>\`（只读），项目以后按摘要锁定；完整校验、引用资源核对和依赖解析不通过时 `PACK_INVALID`；同一版本内容不同时拒绝，内容相同时什么都不做；不导出 `.eplanpack`（第 3 阶段）。
  - `eplan_asset_search`：按文字、种类、包、版本、标签、EPLAN 版本搜索资源索引（宏、项目、表格、文档、物料对应表、候选、模块、缺口）；索引由清单和知识文档每次生成；不可用的资源照样列出并说明原因。
  - `eplan_asset_get`：一个资源的来源位置（输入名和相对路径）、哈希、从文件中测得的事实、知识文档的来源和内容，以及按它自己的哈希核对后是否可用；只有 PNG/SVG 资源有页面预览，宏和项目页的预览要用 EPLAN 导出（第 2 阶段）。
  - `eplan_asset_update`：在开发包中新建或替换知识文档：候选、模块元数据、物料对应表、缺口；按种类严格校验，问题带 JSON 指针；不能写事实，候选恒为 `proposed`、缺口恒为 `open`，从不设为正式规则（要工程师确认，第 3 阶段）。
  - 只读的 MCP 资源模板（只在 `full` 中）：`eplanpack://{pack}/{version}/manifest`（清单）、`eplanpack://{pack}/{version}/{kind}/{name}`（与 `eplan_asset_get` 相同的内容）、`eplanpack://{pack}/{version}/previews/{file}`（预览图片）；内容都能经工具取到。
  - `eplan_pack_import` 的唯一输入是 `.eplanpack` 时改为安装别人的工程包（ADR-0024）：先解到临时目录完整校验，再由工程师在 EPLAN 的对话框中确认（列出包、版本、作者、摘要、EPLAN 文件、能力要求、引用的资源；Workbench 协议第 1 版没有这类问题的字段），批准后安装为只读的版本；拒绝、超时或 EPLAN 没有运行时什么都不装，不留下文件；写审计 `pack_change`。
- `eplanmcp serve --profile core|full`：`core` 只提供 25 个常用工具（全部只读、写入和导出工具，不含删页、重编号这类 Destructive 工具），给对工具数量有上限的客户端用；默认 `full`，提供全部工具。

### Changed

- scratch 外的普通写入默认逐次审批，可用 `"writesOutsideScratch": "deny"` 禁止；删除、重编号等 Destructive 操作在 scratch 内也必须审批并先备份。
- 省略 `language` 且缺少界面语言文本时，优先回退到英文或其他已有语言，并标明实际返回的语言；显式指定语言时行为不变。

### Fixed

- 长于 30 秒的写操作等待到自身时限并返回完整改动清单，不再过早只返回恢复状态。
- 多个 Host 同时读写审计时，不再误报共享冲突、`AUDIT_UNAVAILABLE` 或末行哈希链断开。
- `doctor` 正确识别 Claude 桌面版启动进程的 MSIX 路径重定向；按页或类别查询设备时不再重复列出同一设备。
- `eplan_project_get`、`eplan_page_get`、`eplan_device_get` 大量读取详细属性时，不再持续耗尽 EPLAN 内存导致退出。

### Security

- 插件连接仅供当前 Windows 用户访问；项目、客户端及审计文本按不可信数据处理，移除双向控制字符并以纯文本显示。
- 写入留下可校验哈希链的审计；连接中断后取回原调用结果，结果未知时暂停后续写入，须工程师确认后恢复；审计不可用时拒绝写入。
- 审批默认等待 120 秒，可配置为 60–300 秒；Workbench 审批不抢焦点，超时拒绝，不可用时回退到 EPLAN 对话框。
- 导入限于受控目录，拒绝绝对路径、UNC、`..`、链接和联接点；文件审批绑定大小及 SHA-256，批准后文件变化则返回 `PLAN_STALE`。`masterDataWrites` 可禁止系统主数据写入。

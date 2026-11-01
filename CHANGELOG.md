# Changelog

本文件记录所有面向用户的变更，格式遵循 [Keep a Changelog 1.1.0](https://keepachangelog.com/zh-CN/1.1.0/)，版本号遵循 [Semantic Versioning 2.0.0](https://semver.org/lang/zh-CN/)。
对本项目而言，"公共 API" 指 MCP 工具契约（名称、参数、返回结构、安全注解）和配置文件格式，破坏性变更的定义见 [docs/process/development-workflow.md](docs/process/development-workflow.md#8-版本与发布)。

## [Unreleased]

### Added
- 通用工具调用模板：API 详情按生成分发表返回逐签名参数、枚举全集、来源与示例；action 详情返回已知参数、选择建议及禁用状态。参数错误附带最接近模板；评测报告新增 argument retries（目标 0），并加入先查详情再零参数重试试规划的任务。补全离线测试及契约快照，未运行 EPLAN。
- C1 Files：`eplan_api_try` 对方向明确的文件参数限制在 Host imports/新建 exports 子目录，输入哈希绑定逐次审批，输出返回路径、大小与 SHA-256；目录、打印/发送、主数据、设置及隐式目的地仍排除。TSV 方法组 read/write/excluded 从 49/184/743 变为 49/194/737，新增 14 个可执行签名；Large 导入及单步撤销测试仅编写、编译，未运行。
- C1 静态与入口片：公开静态成员无需 target，项目/页面对象通过最多两层的目录属性路径到达，补项目图层、图框和结构标识符入口；设置/方案只读，维持 scratch、逐次审批、编译分派、快照和单撤销。补充 Small 与 LargeWrite 撤销测试；未运行 EPLAN，schema 不变。
- 新增 full / Read 的 `eplan_selection_get/set`：读取当前项目/编辑页、分页选择集，按通用对象摘要定位并 replace/add/clear，仅改变 UI，报告未选中对象；`eplan_action_try` 可在审批后设置精确 selection，审批与审计记录上下文。Large spike 和单撤销验证已编写，未运行。
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

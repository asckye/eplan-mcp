# Changelog

本文件记录所有面向用户的变更，格式遵循 [Keep a Changelog 1.1.0](https://keepachangelog.com/zh-CN/1.1.0/)，版本号遵循 [Semantic Versioning 2.0.0](https://semver.org/lang/zh-CN/)。
对本项目而言，"公共 API" 指 MCP 工具契约（名称、参数、返回结构、安全注解）和配置文件格式，破坏性变更的定义见 [docs/process/development-workflow.md](docs/process/development-workflow.md#8-版本与发布)。

## [Unreleased]

- ADR-0032 B2 第一部分：新增 full / Read 的 `eplan_object_find`、`eplan_object_get`，支持 Page、Function、Connection；其余 65 种类型明确返回 unsupported，规格列全类型，补分页、定位符与属性读取测试，Large 仅编写未运行。

### Added
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

### Changed
- `eplan_symbol_search` 增加符号描述 `description`、名称或描述过滤 `text_contains` 与语言选择 `language`；分页前过滤，游标绑定新增条件。`SYMB_DESC` 实机读回待验证，新增 Large 已编写未运行。
- `eplan_device_place` 增加 PLC 数字输入点的地址、符号地址、功能文本和原生属性显示，以及 NO/NC 触点引脚代号；读回不符回滚、一次撤销。`eplan_plc_get` 增加原生父关系、手工符号地址、EPLAN 计算的卡页引用和同名无父辅助表达计数；提交后派生值需读工具复核。Large 已写未运行。
- `eplan_device_place` 支持单端子与父端子排：IEC_symbol/X 变体、排功能文本、复合键防重及事务内归属/位置读回，一次撤销；连接后续独立生成。新增假对象与 Large 验收，Large 未运行。
- `eplan_device_place` 支持按位置标识和两角放原生虚线位置框：规划检查已知页界与同页冲突，事务内读回结构/外观并确认既有设备结构不变；框内设备保留自身结构，受影响设备为空，不重命名设备。支持 dry-run、失败回滚与一步撤销；Large 测试已写未运行。
- `eplan_workflow_plan` 接通多柜绑定、五层配置、锁定工作流和正式规则来源；新工程计划固定柜范围、配置 trace、资源版本与规则审核摘要，越界或 mandatory 缺证据时拒绝。`eplan_plan_apply` 支持 compiler 3：审批后及每步前重核柜、配置、知识/规则版本和局部对象，变化或越界拒绝并提示重新规划；沿用逐步证据格式，compiler 2 的七项执行、备份与 outcome 恢复不变。新增 Large 回归代码，尚未实跑。
- `eplan_device_place` 接通无部件库符号执行：一个专用事务内创建功能、显示属性与连接点代号，读回不符整体回滚，失败不留空撤销步；沿既有审批。新增实机验收代码，尚未运行。
- 插件握手每次重新检查 EPLAN API Extension 授权（只检查，不占用授权）；参考项目提取前强制刷新握手，授权未确认或缺少能力时只记录缺口，不读取事实。
- `eplan_page_get` 可选 `include_drawings=true` 只读发现栅格、边界来源和绘图对象（含定位符、位置及只读属性），默认每页 50、最多 200 条，支持预算裁剪和过期游标；原调用响应不变。
- 发布 ZIP 根目录新增 `README.md`，随包说明运行要求、EPLAN 授权条件与支持矩阵。

### Fixed
- `eplan_connection_create`：页上已有 T 节点或角节点时不再拒绝后续连线；电缆芯线可以选在含母线节点的页上；中断点结果只返回 `interruptionScope` 的摘要，不再因体积超限被截断；回滚时说明与计划不一致的具体连接。
- `eplan_device_place` 放置 PLC 连接点不再在页面原点（图框外）多留一个器件级 PLC 端子；新建端子排时端子排定义不再放到页面原点。
- `eplan_connection_create` 可以连到不在栅格上的引脚（如 IEC 端子 `X`），导线和节点可以穿过、位于位置盒内；拒绝时说明是哪一项检查、哪个对象挡住了路径。
- `eplan_connection_generate` 在没有新增或删除任何连接时，不再在 EPLAN 中留下一个空的撤销步骤（结果原本就报告 not_started，但按一次 Ctrl+Z 什么也不会撤销）。
- 机器繁忙、其他程序频繁打开和关闭命名管道时，`eplanmcp` 不再偶尔找不到正在运行的 EPLAN（工具间歇性返回 `EPLAN_NOT_CONNECTED`，重试后又正常）。

## [0.4.0] - 2026-10-26
第四个预览版：属性写入、图纸文字与图框、翻译库导入和第 2 批白名单动作，共 75 个工具。支持矩阵不变：EPLAN 2026（Electric P8、Pro Panel），需要 API 扩展授权；0.x 程序不签名，随附 SHA-256、SBOM 和构建来源证明。

### Added
- `eplan_property_set`（full）：设置白名单中的功能属性，首批只有 20011 功能文本（只改给定语言，其他语言保留）和 20027 技术参数（与语言无关的文本）。先校验同标识功能、身份、其他语言和部件不变，支持 dry run；scratch 外按策略审批；一次调用是一个撤销步骤，失败整体回滚。
- `eplan_page_text_replace`（full）：按字面替换页面上的自由文本（包括组内文字和多语言文字），不改图框、属性放置、功能文本和路径文本；先预览再执行，一次 Ctrl+Z 撤销。
- `eplan_page_update` 新增 `plot_frame`：把页切换为项目中其他页已在使用的图框（写页属性 11016），分别读回设置值和实际图框；与描述修改同属一个撤销步骤。
- `eplan_translation_import`（full）：把 UTF-16 制表符分隔的词汇表导入 EPLAN 配置的翻译库。每次都须工程师审批，导入前备份为 `.zw7`；只新增条目和补全空译文，不改已有译文（api-notes A47）。
- `eplan_action_run` 新增 3 个写入条目：`synchronize/MULTILINE`、`synchronize/OVERVIEW`、`SwitchProjectType`（拒绝与当前相同的类型，结果显示类型变化），共 15 个条目。均经实机验证提交后一次撤销即恢复；`synchronize/SINGLELINE` 在示例项目上看不到效果，仍未开放。

### Changed
- `eplan_property_schema` 对 20011、20027 报告可写（属性写入白名单不再为空）。

## [0.3.0] - 2026-10-26
第三个预览版：多步工作流计划（M6 第 2 阶段）和通用属性读取，共 72 个工具。支持矩阵不变：EPLAN 2026（Electric P8、Pro Panel），需要 API 扩展授权；0.x 程序不签名，随附 SHA-256、SBOM 和构建来源证明。

### Added
- 多步工作流计划：`eplan_workflow_plan` 把 1–20 个内联步骤（建页、改页、删页、设备改名、插宏、页/设备重编号）编译成不可变计划并保存证据；`eplan_plan_get` 分页审阅完整计划、预期和差异预览，计划也可作为 Resource 读取；`eplan_plan_apply` 按最高步骤级别整体审批（scratch 内只有 Write 时自动允许，含 Destructive 始终审批），第一步之前做初始备份，每步独立事务、一次 Ctrl+Z 撤销一步，第一个失败即停，支持 dry run；`eplan_task_get` 跟踪执行任务。结果给出提交事实、对象数据层校验、五类差异和 coverage；编译后范围内被手工改动时执行前报 `PLAN_STALE` 并列出冲突。计划工具在 `workflow` 和 `full` 工具集。
- `eplan_context_get`：计划所需的最小工程上下文（默认柜，带来源和哈希，缺口如实列出）。
- 通用属性读取：`eplan_property_schema` 按 ID 或正式名称查询属性定义（数据字典中 8,155 条可读）；`eplan_property_get` 批量读取项目、页、设备（按标识）和定位符目标的指定属性，区分值、空值、不适用和拒绝读取，支持分页游标和预算截断，不返回路径、联系人等个人数据。属性写入尚未提供。
- [计划使用指南](docs/guides/plans.zh-CN.md)（[English](docs/guides/plans.en.md)），入门指南已链接。

### Changed
- v0.3.0 的新工作流计划统一使用编译器版本 2：建页、设备改名、插宏改为局部 standard 内容验证和全项目名称筛查，未观察页明确列出，不声称全项目内容未变；未过期的版本 1 计划仍按原路径执行，不迁移或重写哈希。按负责人 2026-10-25 决定，核验规模为 300/1,000/3,000 页，面向数千页项目（3,000 页夹具上建页、改页、设备改名三步计划执行约 31 秒，其中初始备份约 1.3 秒）；10,000 页尚不支持，不承诺任意项目规模。计划中的重编号仍仅支持全项目最多 50 页、10,000 个对象；含无页功能的设备改名尚不支持。审批、初始备份、事务和时限保持不变。

### Fixed
- 设备改名计划（`eplan_workflow_plan` 的 `device.update` 步骤）的校验，不再把改名带来的连带变化（连接端点中的功能名、其他功能的 20403 计算地址、省略结构段的可见名）误报为计划外或范围外变化；只认旧全名到新全名的精确替换（api-notes A45）。
- `eplan_macro_get` 遇到没有 2D 外框的宏（例如 3D 宏）时不再报内部错误，`size` 返回 null，摘要写"no 2D outline"。

## [0.2.0] - 2026-10-23
第二个预览版。支持矩阵不变：EPLAN 2026（Electric P8、Pro Panel），需要 API 扩展授权；0.x 程序不签名，随附 SHA-256、SBOM 和构建来源证明。

### Changed
- 交付改为一个 `EplanMcp-<version>.zip`，不再提供 Inno Setup 安装包：解压到短且固定的任意目录，打开 `workbench\EplanMcp.Workbench.exe`；数据根目录仍是 `%USERPROFILE%\EplanMcp`。升级时关闭 EPLAN、Workbench 和客户端后整体替换程序目录；卸载时先在 Workbench 撤销注册，再删除程序目录。

### Added
- `eplan_action_run`（full，默认关闭，须在 `config.json` 打开 `actionRun`）：只运行随产品发布、经 S26 验证的 12 个白名单 EPLAN 动作，Host 逐条校验参数，比较执行前后的指纹。
- Workbench 状态页：列出检测到的 EPLAN 变体，勾选后"注册 EPLAN 插件"或"撤销注册"，逐个变体显示结果。新命令 `addin-variants`。
- Claude Code 插件：`client-config claude-code --plugin` 和 Workbench 的"生成本地市场"把市场模板复制到数据根目录，写入 Host 的实际绝对路径。
- 虚拟机连接（ADR-0018）：EPLAN 在虚拟机中、AI 客户端在宿主机上时使用。
  - 虚拟机中：`serve --http --address <IPv4>`（Streamable HTTP，默认端口 8766，固定一个 EPLAN 实例，无状态），`vm-key create|show|delete`（DPAPI 保存的共享密钥，Bearer 认证、失败限流），`httpAllowedOrigins` 允许列表，审计记录来源地址；`vm-firewall show|add|remove`（只放行宿主机 IPv4，修改须管理员权限）；`vm-instances`；`doctor` 报告 HTTP 服务和防火墙规则。Workbench 客户端页底部的"虚拟机连接"面板：选择地址和端口、生成和复制密钥、经 UAC"允许网络访问"、启动和停止服务（随 Workbench 退出停止）。
  - 宿主机上：`client-config <客户端>... --vm --url http://<IPv4>:<端口>/mcp --key-stdin` 为能发送请求头的客户端写入 `eplan-vm`（Claude 桌面版不支持），`--vm --remove` 移除；`vm-test` 测试连接；Workbench 客户端页的"虚拟机 ↔ 宿主机"向导。密钥只经 stdin 传递，报告中显示为 `<key>`。
  - HTTP 不加密，只在宿主机与虚拟机之间的私有网络中使用；宿主机客户端配置文件中的密钥是明文。
- 中英文入门指南：英文版、语言切换、ZIP 的安装/升级/卸载、虚拟机部署（第 10 节）；Claude Code 插件内同步副本。

### Not yet verified
- 跨机器的虚拟机连接、UAC 授权、各客户端的远程配置实际连接，以及干净机器上的 ZIP 验收，交由测试人员完成。

## [0.1.0] - 2026-10-22
首个预览版。支持矩阵：EPLAN 2026（Electric P8、Pro Panel），需要 API 扩展授权；0.x 安装包不签名，随附 SHA-256、SBOM 和构建来源证明（见 [中文入门指南](docs/getting-started/zh-CN.md)）。

### Added
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
- `eplanmcp doctor --format json`、`eplanmcp audit verify --format json`（M5）：输出带 `schemaVersion` 的 JSON，供 Workbench 使用；doctor 的每一项带稳定的 `id`，便于分组。结构见[架构 5.2](docs/architecture/architecture.md#52-host-组件)。
- `eplanmcp client-config <客户端>… [--dry-run|--remove] [--format json]`（M5）：为 Claude Code、Claude 桌面版、Codex、Gemini CLI、Qwen Code、Kimi Code CLI、CodeBuddy Code、OpenCode、Qwen Agent、Cursor、VS Code Copilot 写入名为 `eplan` 的 MCP 服务器，运行本机的 `eplanmcp.exe serve`；可以一次写多个客户端，分别报告结果，任何一个失败时退出码为 1。
  - 只改 `eplan` 这一项，文件中的其他服务器和设置都保留；已有的 `eplan` 是本地服务器（有 `command`）时只更新命令和参数，其他字段保留，否则整项替换；JSONC 文件中的注释在改写后丢失（原文件在备份中），Codex 的 TOML 中无法安全地逐行修改的写法拒绝写入；
  - 写入前把原文件复制为同目录的 `<文件>.bak_<UTC 时间>`；读取之后文件被其他程序改动时拒绝写入；
  - `--dry-run` 只显示 `eplan` 这一项改动前后的写法，不显示文件的其他部分；
  - 写入和移除都记在 `<数据根目录>\state\clients.json`；`--remove` 不带客户端时移除记录中的全部条目，供卸载使用；
  - 拒绝的情况：由 MSIX 打包的客户端（如 Claude 桌面版）启动时一律拒绝（写入会被重定向）；已安装 Claude Code 插件 `eplan-mcp@eplanmcp-local` 时不为 Claude Code 写配置（插件已经提供服务器）；Claude 桌面版运行时不改它的配置文件，请先完全退出；配置文件是符号链接或路径不完整时不修改；桌面版以 MSIX 安装时写包中的那份文件；
  - JSON 结构见[架构 5.2](docs/architecture/architecture.md#52-host-组件)。
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
- 没有 `config.json`、或其中没有设 `writesOutsideScratch` 时，scratch 之外的项目（真实项目）的写入不再一律拒绝（`POLICY_DENIED`），改为每次在 EPLAN 中弹出审批对话框，工程师批准后才执行；删除等 Destructive 操作照旧先备份。要恢复只允许 scratch，在 `config.json` 中设 `"writesOutsideScratch": "deny"`；`eplanmcp doctor` 显示当前设置。
- `eplan_tool_usage` 的描述和 `topic` 的说明缩短，给参数中不断增加的工具名留出篇幅（契约变更，用法不变）。
- 省略 `language` 时，EPLAN 界面语言中没有的多语言文本（如只有德文、英文的部件描述）改为返回其他语言的文本（先英文），并在旁边的 `descriptionLanguage`、`textLanguage` 或 `language` 字段注明语言；显式传入 `language` 时行为不变。

### Fixed
- 写工具等待 EPLAN 的响应时，不再在 30 秒（读操作的时限）后就改为向插件查询结果，而是等到该工具的执行时限：超过 30 秒的写入（如给整个项目的端子编号）以前只返回 `recovered: true`、没有改动清单，现在返回完整结果；只有响应真的没有到达（超过时限、连接断开、取消）时才查询结果。
- 另一个 `eplanmcp` 正在写审计日志时，`eplanmcp audit verify` 和写工具第一次写入前对审计日志的检查不再因文件共享冲突而失败，读的同时也不再让对方的写入报 `AUDIT_UNAVAILABLE`；`audit verify` 不再把对方正在写的最后一行报为哈希链断开。
- `eplanmcp doctor` 由 Claude 桌面版启动时，不再误报"没有 MSIX 包身份"：这类进程没有包身份，写入 `%LOCALAPPDATA%` 却仍被重定向，现在直接探测重定向。
- 按页或类别列设备、列页上的设备时，同一设备不再出现两次（EPLAN 在过滤时会多返回黑盒里的同名功能）。
- `eplan_project_get`、`eplan_page_get`、`eplan_device_get` 以 `detailed` 返回 `properties` 时，读过的每个属性对象随即释放。以前不释放，按 spike S31 的结果（api-notes A36），读得多了 EPLAN 会内存耗尽而退出；返回的属性不变。

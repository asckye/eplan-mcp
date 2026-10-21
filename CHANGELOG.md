# Changelog

本文件记录所有面向用户的变更，格式遵循 [Keep a Changelog 1.1.0](https://keepachangelog.com/zh-CN/1.1.0/)，版本号遵循 [Semantic Versioning 2.0.0](https://semver.org/lang/zh-CN/)。
对本项目而言，"公共 API" 指 MCP 工具契约（名称、参数、返回结构、安全注解）和配置文件格式，破坏性变更的定义见 [docs/process/development-workflow.md](docs/process/development-workflow.md#8-版本与发布)。

## [Unreleased]

### Added
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
- 列表工具统一分页（`nextCursor`），结果超出大小预算时截断并提示；来自项目的文本按不可信数据处理。
- 支持 `language` 参数读取多语言文本。
- M4c 只读工具（只在 `full` 中）：
  - `eplan_cable_get`：一根电缆的型号、芯数、截面、长度、两端的设备、部件，以及每根芯线的颜色、线号和两端；芯线分页。列出电缆用 `eplan_device_list`（`category: "Cable"`）。
  - `eplan_terminal_strip_list`：项目中的端子排及每排的端子数、跨接数，按名称排序、分页，可按名称中的文字过滤。
  - `eplan_terminal_strip_get`：一个端子排的端子（按排列顺序，分页），每个端子内侧、外侧连接的另一端、与之跨接的端子、所在页；不带结构的短名称（如 `-XD4`）在多个位置出现时返回 `AMBIGUOUS_NAME`。
  - `eplan_plc_get`：一个 PLC 盒的站、机架、槽、部件，以及每个通道对象（同一连接点名称可以有输入、输出或两种表达）的页、是否主表达、地址、符号地址、数据类型、功能文本和连接的另一端（空闲通道没有），通道分页。列出 PLC 盒用 `eplan_device_list`（`category: "PLCBox"`）。
  - `eplan_part_search`：在 EPLAN 配置的系统部件库中按部件号、型号、订货号或描述中的文字查部件，可限定制造商；给出描述和是否有宏，分页。部件库只读打开、用完关闭，从不修改。
- M4c 导出工具（只在 `full` 中；与 `eplan_export_pdf` 相同，只写进每次新建的导出目录，失败或取消时删除该目录）：
  - `eplan_export_pxf`：整个项目（含连接）导出为一个 EPJ（PXF）文件，可选带上主数据；导出前要求导出目录所在的磁盘至少有 2 GB 可用空间。
- `eplanmcp serve --profile core|full`：`core` 只提供 25 个常用工具（全部只读、写入和导出工具，不含删页、重编号这类 Destructive 工具），给对工具数量有上限的客户端用；默认 `full`，提供全部工具。

### Changed
- 没有 `config.json`、或其中没有设 `writesOutsideScratch` 时，scratch 之外的项目（真实项目）的写入不再一律拒绝（`POLICY_DENIED`），改为每次在 EPLAN 中弹出审批对话框，工程师批准后才执行；删除等 Destructive 操作照旧先备份。要恢复只允许 scratch，在 `config.json` 中设 `"writesOutsideScratch": "deny"`；`eplanmcp doctor` 显示当前设置。
- `eplan_tool_usage` 的描述和 `topic` 的说明缩短，给参数中不断增加的工具名留出篇幅（契约变更，用法不变）。
- 省略 `language` 时，EPLAN 界面语言中没有的多语言文本（如只有德文、英文的部件描述）改为返回其他语言的文本（先英文），并在旁边的 `descriptionLanguage`、`textLanguage` 或 `language` 字段注明语言；显式传入 `language` 时行为不变。

### Fixed
- 另一个 `eplanmcp` 正在写审计日志时，`eplanmcp audit verify` 和写工具第一次写入前对审计日志的检查不再因文件共享冲突而失败，读的同时也不再让对方的写入报 `AUDIT_UNAVAILABLE`；`audit verify` 不再把对方正在写的最后一行报为哈希链断开。
- `eplanmcp doctor` 由 Claude 桌面版启动时，不再误报"没有 MSIX 包身份"：这类进程没有包身份，写入 `%LOCALAPPDATA%` 却仍被重定向，现在直接探测重定向。
- 按页或类别列设备、列页上的设备时，同一设备不再出现两次（EPLAN 在过滤时会多返回黑盒里的同名功能）。

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
- 取消工具调用（M4）：客户端取消调用时，还在队列中的请求不再发给 EPLAN；已经发出的，插件在下一个安全点停下（不打断正在进行的一次 EPLAN 调用），写操作在提交前停下就回滚；已提交的不撤回。写操作照样取回真实结果写入审计，停下的记为 `CANCELLED`。旧版插件不支持时，已发出的调用照常执行完。
- 列表工具统一分页（`nextCursor`），结果超出大小预算时截断并提示；来自项目的文本按不可信数据处理。
- 支持 `language` 参数读取多语言文本。

### Changed
- `eplan_tool_usage` 的描述和 `topic` 的说明缩短，给参数中不断增加的工具名留出篇幅（契约变更，用法不变）。
- 省略 `language` 时，EPLAN 界面语言中没有的多语言文本（如只有德文、英文的部件描述）改为返回其他语言的文本（先英文），并在旁边的 `descriptionLanguage`、`textLanguage` 或 `language` 字段注明语言；显式传入 `language` 时行为不变。

### Fixed
- `eplanmcp doctor` 由 Claude 桌面版启动时，不再误报"没有 MSIX 包身份"：这类进程没有包身份，写入 `%LOCALAPPDATA%` 却仍被重定向，现在直接探测重定向。
- 按页或类别列设备、列页上的设备时，同一设备不再出现两次（EPLAN 在过滤时会多返回黑盒里的同名功能）。

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
- 写入审批（`config.json` 中 `writesOutsideScratch` 为 `approve` 时）：scratch 之外的写入先在 EPLAN 中弹出审批对话框，按系统界面语言列出工具、项目和变更清单；5 分钟内未批准即自动拒绝，期间其他调用返回 `EPLAN_BUSY`；每个决定单独写入审计。
- 第一个写工具 `eplan_page_create`（M3a）：按完整页名一次新建 1–50 页，可带页描述；在 EPLAN 中只占一个撤销步骤"AI: eplan_page_create"，任何一页失败或读回与请求不同时全部回滚；支持 `dry_run`；经写入流水线（策略、审批、审计、结果取回），scratch 之外的项目默认拒绝。
- 列表工具统一分页（`nextCursor`），结果超出大小预算时截断并提示；来自项目的文本按不可信数据处理。
- 支持 `language` 参数读取多语言文本。

### Changed
- 省略 `language` 时，EPLAN 界面语言中没有的多语言文本（如只有德文、英文的部件描述）改为返回其他语言的文本（先英文），并在旁边的 `descriptionLanguage`、`textLanguage` 或 `language` 字段注明语言；显式传入 `language` 时行为不变。

### Fixed
- `eplanmcp doctor` 由 Claude 桌面版启动时，不再误报"没有 MSIX 包身份"：这类进程没有包身份，写入 `%LOCALAPPDATA%` 却仍被重定向，现在直接探测重定向。
- 按页或类别列设备、列页上的设备时，同一设备不再出现两次（EPLAN 在过滤时会多返回黑盒里的同名功能）。

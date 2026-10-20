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
- 列表工具统一分页（`nextCursor`），结果超出大小预算时截断并提示；来自项目的文本按不可信数据处理。
- 支持 `language` 参数读取多语言文本。

### Fixed
- 按页或类别列设备、列页上的设备时，同一设备不再出现两次（EPLAN 在过滤时会多返回黑盒里的同名功能）。

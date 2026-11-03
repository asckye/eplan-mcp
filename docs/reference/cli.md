# CLI 参考

本页维护 `eplanmcp` 的 JSON 输出契约；Host 的组件职责见[架构 5.2](../architecture/architecture.md#52-host-组件)。

## JSON 输出契约

**CLI 的 JSON 输出**（给 Workbench 用，[M5 设计](../design/2026-10-20-m5-operability.md) 4.4、4.11）：`doctor`、`audit verify`、`client-config`、`install-addin`、`uninstall-addin` 带 `--format json` 时在 stdout 输出一个 UTF-8 的 JSON 对象，退出码与文本输出相同；`--format` 只接受 `json`，其他值或缺少值按命令行错误处理（退出码 2）。结构变化时 `schemaVersion` 加 1，快照在 `tests/Contracts.Snapshots/`。
- `doctor`：`{schemaVersion: 1, hostVersion, status, checks: [{id, subject?, status, zh, en}]}`。`status` 取 `ok`、`info`、`warning`、`failure`；顶层的 `status` 是最严重的一项，`info` 按 `ok` 计。`checks` 按输出顺序排列，`id` 稳定，同类的几项共用一个：`host.version`、`data.directory`、`config`、`audit.directory`、`exports`、`backups`、`plans`、`msix`、`suspension`、`http.service`、`vm.firewall`、`eplan.installation`、`eplan.running`、`bridge.pipe`、`bridge.handshake`、`bridge.licence`、`path.length`。HTTP 状态发现文件与固定防火墙规则见 ADR-0018 第 11 条。`subject` 是这一项针对的 EPLAN 版本（`eplan.installation`）或 EPLAN 进程号（其他各项），不针对某一个时省略。
- `audit verify`：`{schemaVersion: 1, directory, exists, intactRecords, intact, break}`。`intactRecords` 是完整的记录数（断开时是断开处之前的）；`break` 为 `{file, line, problem}`，`problem` 取 `changed`、`missing`、`not_a_record`，哈希链完整时为 `null`。
- `client-config`：`{schemaVersion: 1, mode, server: {name, command, args}, problem, clients: [{client, file, action, result, before, after, backup, notes, problem}]}`，每个客户端一项，顺序同命令行。
  - `mode` 取 `write`、`dry_run`、`remove`；`action` 取 `write`、`remove`；`result` 取 `written`、`would_write`、`removed`、`unchanged`、`failed`；
  - `before`、`after` 只是 `eplan` 这一项在文件中的写法（JSON 或 TOML 的文本），没有时为 `null`，从不包含文件的其他部分；`file` 在无法确定位置时为 `null`；`notes` 目前只有 `{code: "comments_dropped", zh, en}`；
  - `problem` 为 `{code, zh, en}` 或 `null`，`code` 取 `invalid_file`、`unsupported_toml`、`ambiguous_package`、`file_changed`、`invalid_record`、`record_failed`、`unknown_client`、`io_error`、`not_eplanmcp`、`plugin_installed`、`claude_desktop_running`、`msix_redirected`。`record_failed` 时文件已经改好，`result` 仍是 `written` 或 `removed`；
  - 顶层的 `problem` 表示整个命令被拒绝（`not_eplanmcp`、`msix_redirected`，或 `--remove` 时 `invalid_record`），这时 `clients` 为空。任何一处有 `problem` 时退出码为 1。
- `install-addin`、`uninstall-addin`：`{schemaVersion: 1, action, dll, problem, results: [{eplan, version, variant, result, processId, notes, problem}]}`，每个 `EPLAN.exe` 和变体一项（M5 设计 4.2）。
  - `action` 取 `install`、`uninstall`；`dll` 是注册的插件，撤销时为 `null`；`eplan` 是执行命令的 `EPLAN.exe`，`version` 是它的文件版本；整个版本因不在支持范围内被跳过时 `variant` 为 `null`；`processId` 是命令启动的 EPLAN，没有启动时为 `null`；
  - `result` 取 `registered`、`unregistered`、`not_confirmed`（等待结束时 EPLAN 还没有加载插件，留着没有关）、`failed`、`refused`（没有启动 EPLAN）、`skipped`；
  - `notes` 的每一项为 `{code, zh, en}`，`code` 取 `version_untested`、`unsupported_version`、`eplan_missing`（记录的 EPLAN 或变体已不在，记录已删除）、`close_eplan`（EPLAN 没有按要求关闭）；
  - `problem` 为 `{code, zh, en}` 或 `null`，`code` 取 `addin_not_found`、`no_eplan`、`variant_not_found`、`unblock_failed`、`invalid_record`、`eplan_running`、`other_user`、`start_failed`、`eplan_exited`、`not_loaded`、`path_mismatch`（EPLAN 从另一个位置加载了插件，或者加载的是不报告路径的旧版插件，这次的注册没有生效或看不出是否生效）、`handshake_failed`（插件没有回应握手，看不出加载的是哪一个注册）、`record_failed`；顶层的 `problem` 表示什么都没有做，这时 `results` 为空。任何一处有 `problem` 时退出码为 1。
- `pack`（M6，暂不给 Workbench 用，结构随 M6 设计可能变化）：`{schemaVersion: 1, command, ok, …}`，`ok` 为 false 时退出码为 1。`scan` 另有 `resources`、`facts`、`findings: [{severity, code, subject, message}]`（`severity` 取 `error`、`warning`、`gap`），给了 `--id`/`--name`/`--version` 时有 `manifest` 和 `problems`，写出后有 `written`；`check` 有 `id`、`version`、`resources`（个数）、`problems: [{location, message}]`；`export` 有 `file`、`bytes`；`install` 有 `outcome`（`installed`、`already_installed`、`refused`）、`id`、`version`、`folder`。


## 工具发现与日志

`eplanmcp serve --profile core|full` 的默认值为 `full`；`core` 集合由 `ToolProfiles.Core` 定义，注册工具按组和名称排序。`eplan_tool_usage` 的 `tool` 枚举取自本次 Profile 与开关筛选后的注册集合，详见[用法工具](../tools/eplan_tool_usage.md)。

Host 使用 `Microsoft.Extensions.Logging` 的 `AddSimpleConsole`，单行日志写到 stderr；Bridge 日志为数据目录中的 `logs/bridge-*.log`，离线宿主为 `logs/bridge-offline-*.log`，插件启动异常另写 `bridge-startup-error.log`。默认数据目录与环境变量见 [CLAUDE.md](../../CLAUDE.md#常用命令)和[数据位置决定](../adr/0008-data-locations-and-instance-discovery.md)。

# 架构决策记录（ADR）

记录难以回退、或者会长期影响项目的决策。格式见 [template.md](template.md)（基于 MADR），规则见 [开发流程第 3 节](../process/development-workflow.md#3-变更分级与设计产物)。

- 编号从不复用。决策被推翻时，新写一份 ADR，并把旧的标为 `Superseded by ADR-XXXX`。
- 以 `Proposed` 状态通过 PR 提交，由项目负责人确认后改为 `Accepted`（冷却期已于 2026-10-20 取消，见开发流程第 6 节）。

| 编号 | 标题 | 状态 | 门的类型 |
|---|---|---|---|
| [0001](0001-record-architecture-decisions.md) | 用 ADR 记录架构决策 | Accepted | 双向门 |
| [0002](0002-host-bridge-process-model.md) | 采用"进程外 Host + EPLAN 进程内 Bridge"的进程模型 | Accepted | 单向门 |
| [0003](0003-ipc-named-pipe-json-rpc.md) | Host 与 Bridge 之间使用命名管道上的 JSON-RPC 2.0 | Accepted（实例发现与握手密钥部分被 0008 取代） | 双向门（协议有版本号） |
| [0004](0004-runtimes-and-target-frameworks.md) | 运行时：Host 用 .NET 10，Bridge 用 net481，契约用 netstandard2.0 | Accepted | 单向门 |
| [0005](0005-safety-model.md) | 安全模型：分级、策略引擎、审批、事务、审计 | Accepted | 单向门 |
| [0006](0006-tool-surface-design.md) | 工具面：面向任务、资源命名、显式注解、结构化输出、Profile | Accepted | 单向门（工具契约发布后难以更改） |
| [0007](0007-licensing-and-distribution.md) | 授权与分发：0.x 阶段发布未签名插件，构建预留 EADN 签名开关 | Accepted | 双向门 |
| [0008](0008-data-locations-and-instance-discovery.md) | 数据目录移出 %LOCALAPPDATA%，实例发现改用命名管道枚举 | Accepted | 双向门（1.0 前） |
| [0009](0009-supported-eplan-versions.md) | 支持 EPLAN 2024–2027，用一份 net481 插件构建；2022、2023 暂不支持 | Accepted | 双向门 |
| [0010](0010-write-outcomes.md) | 写操作结果使用封闭的执行状态，中断后向 Bridge 查询结果 | Accepted | 单向门（写工具发布后） |
| [0011](0011-approval-out-of-band.md) | 审批只经工程师独立操作的窗口（EPLAN 对话框、Workbench），不用 MCP elicitation | Accepted | 双向门（1.0 发布前） |
| [0012](0012-export-tier.md) | 导出工具按 Read 级处理，注解 `readOnlyHint: false`，文件只写进 Host 新建的导出目录 | Accepted | 双向门（1.0 发布前） |
| [0013](0013-destructive-backup.md) | Destructive 操作前用 `CopyProject` 的快照方式自动备份，备份失败就不执行 | Accepted | 单向门（Destructive 工具发布后） |
| [0014](0014-workbench-approval-channel.md) | Workbench 作为单独的进程，在每个用户、每个数据根目录一个的命名管道上接收 Host 的审批 | Accepted | 双向门（1.0 发布前） |
| [0015](0015-workbench-ui-stack.md) | Workbench 用 WinUI 3（Windows App SDK，未打包、自包含）实现 Fluent 2 界面 | Superseded by [0017](0017-workbench-wpf-tia-theme.md) | 双向门（第一版 Workbench 发布前） |
| [0016](0016-per-user-installer.md) | 用按用户安装的 Inno Setup 安装包分发，程序固定在 `%USERPROFILE%\EplanMcp\app\`，插件只经 `eplanmcp install-addin` 注册 | Accepted | 单向门（第一个安装包发布后） |
| [0017](0017-workbench-wpf-tia-theme.md) | Workbench 改用 WPF（.NET 10），复用 TIA Workbench 的 Ant Design v5 主题，字体用 Maple Mono CN | Accepted | 双向门（第一版 Workbench 发布前） |

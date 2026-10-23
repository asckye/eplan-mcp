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
| [0018](0018-vm-http-transport.md) | EPLAN 在虚拟机、AI 客户端在宿主机时，虚拟机中的 Host 以 HTTP 提供 MCP，用共享密钥认证，防火墙只放行宿主机 | Accepted | 双向门（1.0 发布前） |
| [0019](0019-master-data-writes.md) | 部件库、翻译库的写入按 Destructive 处理：每次审批，先备份主数据，只支持文件型数据库 | Accepted | 单向门（写系统主数据的工具发布后） |
| [0020](0020-no-self-hosted-runner.md) | 不使用自托管 runner：实机测试在开发机上手动运行，插件在开发机上按标签构建后交给发布工作流 | Accepted | 双向门 |
| [0021](0021-action-run-whitelist.md) | 用受控的 `eplan_action_run` 覆盖长尾的 EPLAN action：只执行随产品发布、经实机验证的白名单条目，按条目的级别走任务工具的流水线；默认关闭，只在 `full` 中 | Accepted | 单向门（白名单条目发布后） |
| [0022](0022-domain-toolsets.md) | 按领域把工具分成工具集，Profile 由工具集组成；支持工具列表变更通知的客户端可以按需加载工具集，其他客户端用固定的工具集 | Accepted | 双向门（1.0 发布前） |
| [0023](0023-plan-approval-and-backup.md) | 组合计划一次审批（带差异预览）、开始前备份一次、每步一个事务，计划级结果增加 `partially_committed` | Accepted | 单向门（计划工具发布后） |
| [0024](0024-pack-trust-and-container.md) | 工程包按不可信输入处理：只含数据，安装前校验容器，安装后不可变，放在数据根目录；企业资源只按配置文件中的根目录和哈希引用 | Accepted | 单向门（第一批 `.eplanpack` 交换出去后） |
| [0025](0025-code-plugin-worker.md) | 算法插件在单独的工作进程中运行，纯数据进、计划草稿出，按 Code 级默认关闭，列入允许列表后每次运行不再单独审批；`AssemblyLoadContext` 只用于加载依赖 | Accepted | 单向门（插件契约发布后） |
| [0026](0026-eplan-events-and-push.md) | Bridge 订阅经验证的 EPLAN 事件，在单独的订阅连接上把带序号的失效通知推给 Host；通知只用于缓存失效，从不作为写入的依据 | Accepted | 双向门（协议有能力声明） |
| [0027](0027-workbench-plan-review.md) | Workbench 的计划审阅与计划结果通知扩展为版本化协议（M6 第 2 阶段不依赖） | Proposed | 单向门（改变 Workbench IPC 合同） |
| [0028](0028-plan-record-format.md) | 计划以版本化的不可变定义加原子替换的状态记录落盘（`state\plans\`），规范 SHA-256，可执行 24 小时、保留 30 天 | Accepted | 单向门（新增落盘格式） |

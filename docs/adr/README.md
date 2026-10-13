# 架构决策记录（ADR）

记录难以回退、或者会长期影响项目的决策。格式见 [template.md](template.md)（基于 MADR），规则见 [开发流程第 3 节](../process/development-workflow.md#3-变更分级与设计产物)。

- 编号从不复用。决策被推翻时，新写一份 ADR，并把旧的标为 `Superseded by ADR-XXXX`。
- 以 `Proposed` 状态通过 PR 提交，由项目负责人确认后改为 `Accepted`。L3 决策提交后至少隔一天再确认。

| 编号 | 标题 | 状态 | 门的类型 |
|---|---|---|---|
| [0001](0001-record-architecture-decisions.md) | 用 ADR 记录架构决策 | Accepted | 双向门 |
| [0002](0002-host-bridge-process-model.md) | 采用"进程外 Host + EPLAN 进程内 Bridge"的进程模型 | Accepted | 单向门 |
| [0003](0003-ipc-named-pipe-json-rpc.md) | Host 与 Bridge 之间使用命名管道上的 JSON-RPC 2.0 | Accepted | 双向门（协议有版本号） |
| [0004](0004-runtimes-and-target-frameworks.md) | 运行时：Host 用 .NET 10，Bridge 用 net481，契约用 netstandard2.0 | Accepted | 单向门 |
| [0005](0005-safety-model.md) | 安全模型：分级、策略引擎、审批、事务、审计 | Accepted | 单向门 |
| [0006](0006-tool-surface-design.md) | 工具面：面向任务、资源命名、显式注解、结构化输出、Profile | Accepted | 单向门（工具契约发布后难以更改） |
| [0007](0007-licensing-and-distribution.md) | 授权与分发：0.x 阶段发布未签名插件，构建预留 EADN 签名开关 | Proposed | 双向门 |
| [0008](0008-data-locations-and-instance-discovery.md) | 数据目录移出 %LOCALAPPDATA%，实例发现改用命名管道枚举 | Proposed | 双向门（1.0 前） |

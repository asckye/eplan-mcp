# 本项目采用的开发规范

本仓库遵循[《工程开发手册》v1.1](engineering-handbook.html)，全部采用；本仓库不另设规则。

## 项目声明

| 项目 | 声明 |
|---|---|
| 采用范围 | 领域 A（MCP 服务）、C（EPLAN .NET API）、D（Workbench）；档位 M，审批、写入安全和防止项目数据损坏按 L。 |
| 提交格式（GIT-05） | Conventional Commits；scope 取自 `host bridge contracts tools policy audit workbench installer evals docs ci spikes`。 |
| 语言政策（DOC-03） | 采用手册默认：开发与用户文档中文；代码、注释、提交、发布记录、面向模型的文本英文；与项目负责人交流用中文。 |
| 合并门（BLD-03） | [`eng/scripts/local-gate.ps1`](../../eng/scripts/local-gate.ps1) 是权威合并门，CI 不阻塞；通过本地关卡后直接推送 `main`、不开 PR，见 [ADR-0048](../adr/0048-local-gate-is-the-merge-gate.md)。 |
| 证据台账（P-01、P-03） | EPLAN 行为的出处在 [`docs/eplan/api-notes.md`](../eplan/api-notes.md)；未实机验证项在 [`docs/process/large-pending.md`](../process/large-pending.md)。 |
| 支持的 EPLAN 版本 | 见 [ADR-0009](../adr/0009-supported-eplan-versions.md)。 |

## 原规则 → 手册条款

| 原规则 | 手册条款 |
|---|---|
| 只在 scratch 项目上做写操作 | [AI-10](engineering-handbook.html#ai-machine) |
| 不带 AI 署名 | [GIT-04](engineering-handbook.html#review-git) |
| L3 决策只能提议为 Proposed ADR，由负责人采纳 | [G-05](engineering-handbook.html#purpose) + [18.3](engineering-handbook.html#review-rfc) |
| 不提交敏感内容 | [GIT-07](engineering-handbook.html#review-git) |
| stdio 的 stdout 只输出协议 | [ARC-12](engineering-handbook.html#arch) |

## 本地关卡的实现入口

[`local-gate.ps1`](../../eng/scripts/local-gate.ps1) 在已提交的干净工作区检查相对 `origin/main` 的变更；`-Push` 先同步并变基，检查通过且远端未移动时推送 `HEAD:main`。`-Full` 选择全部 Small/Medium 程序集，通常范围由 [`affected-tests.ps1`](../../eng/scripts/affected-tests.ps1) 根据项目依赖、外链编译/资源项及重命名两端计算。

| 实现入口 | 当前检查内容 |
|---|---|
| [`check-commit-message.sh`](../../eng/scripts/check-commit-message.sh) | 待推送提交的主题、正文和署名；外部 PR 的标题与描述由 CI 检查。 |
| `local-gate.ps1` 的还原、构建与格式步骤 | 设置 `CI=true`、锁定还原、Release 构建和 `dotnet format --verify-no-changes --no-restore`；按插件项目求值的 `EplanBinPath`/API 引用检测 EPLAN 安装，齐全时选 `EplanMcp.slnx`，否则记录 `add-in not checked: EPLAN not installed` 并选 `EplanMcp.Ci.slnf`；CI 使用后者。 |
| [`check-action-catalog.ps1`](../../eng/scripts/check-action-catalog.ps1) | Action 的 JSON Schema、ID、顺序、文档表格逐行内容与数量。 |
| [`eng/coverage`](../../eng/coverage/README.md) 的 `check` | 生成文件一致性；扫描过期给出警告。 |
| [`check-test-floor.ps1`](../../eng/scripts/check-test-floor.ps1) 与 [`tests/test-suites.json`](../../tests/test-suites.json) | 每程序集独立 TRX；缺结果、未知程序集、通过数低于 `minPassed` 或下限低于通过数的 80% 会失败，建议值为通过数的 90% 向下取整。 |
| `dotnet list EplanMcp.Ci.slnf package --vulnerable --include-transitive` | High/Critical 漏洞检查。 |

CI 在 `main` 上运行全部自动测试；Large 与 Agent 评测由开发机手动执行，无自托管 runner 的决定见 [ADR-0020](../adr/0020-no-self-hosted-runner.md)。这里只记录现有脚本行为。

## 保留的项目文档

- [CONTRIBUTING.md](../../CONTRIBUTING.md)：构建配置、输出目录、测试 trait 和本地命令的项目入口。
- [architecture.md](../architecture/architecture.md)：组件、进程、协议、错误映射、结果整形与已实现安全机制。
- [api-notes.md](../eplan/api-notes.md)：EPLAN 版本、调用条件、精确输入和已验证行为的证据索引。
- [docs/tools/](../tools/)：已发布工具的名称、schema、参数、结果、安全分类及具体调用示例。
- [cli.md](../reference/cli.md)：CLI 参数、Profile、日志位置与诊断输出。
- [M5 设计](../design/2026-10-20-m5-operability.md)：Workbench 的既有决定、布局资源、交互、来源及验证状态。
- [项目术语表](../reference/glossary.md)：EPLAN 与 Workbench 的项目术语和含义。
- [Host 测试说明](../../tests/EplanMcp.Host.Tests/README.md)：Fake Bridge、契约/CLI 快照与模型文本检查的实际入口。
- [Contracts 测试说明](../../tests/EplanMcp.Contracts.Tests/README.md)：契约 DTO、序列化与安全类型的测试范围。
- [Bridge.Core 测试说明](../../tests/EplanMcp.Bridge.Core.Tests/README.md)：不依赖 EPLAN 的适配器接缝与故障路径测试。
- [Workbench 测试说明](../../tests/EplanMcp.Workbench.Tests/README.md)：界面、队列、主题、文字与 UIA 的已有测试入口。
- [Large 测试说明](../../tests/EplanMcp.Large.Tests/README.md)：实机环境、scratch 夹具、批量运行和具体类的覆盖范围。
- [evals/README.md](../../evals/README.md)：Agent 任务格式、运行器、报告与需求信号映射。
- [eng/coverage/README.md](../../eng/coverage/README.md)：目录生成、批量验证结果导入及一致性检查命令。
- [live-verification.md](../process/live-verification.md)：现有实机批次组织和证据记录入口。
- [large-pending.md](../process/large-pending.md)：未验证范围、阻塞条件及已通过记录。
- [release.md](../process/release.md)：发布列车、现行脚本调用与 ZIP/插件交付步骤。
- [handoff.md](../status/handoff.md)：已核对但尚未完成的项目待办。
- [docs/adr/](../adr/)：已作出的架构决定、历史上下文及追加修订。
- [工具规格模板](../templates/tool-spec.md)：手册附录 T.3 之上的 EPLAN 项目字段。

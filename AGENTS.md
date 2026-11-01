# AGENTS.md

本文件供 Codex 等编码代理使用。先读本文件与 [CLAUDE.md](CLAUDE.md)，再按下面清单读规范；内部交付流程见 [开发流程](docs/process/development-workflow.md)。

## 硬性规则（违反任何一条都不能合并）
1. **只在 scratch 项目上做写操作。** 永远不要对用户的真实 EPLAN 项目执行写入、删除、重编号、导入等操作，即使是为了"试一下"。scratch 根目录之外的写入必须由人在审批中明确批准。
2. **不带 AI 署名。** 提交信息、PR 描述、发布说明、CHANGELOG 和文档里，都不加 `Co-Authored-By` 尾注、"Generated with …" 或 🤖 之类的标记。本地 `commit-msg` 钩子和 CI 都会检查。
3. **L3 决策只能提议。** 涉及进程拓扑、IPC、安全/审批模型、支持的 EPLAN 版本的改动，只能写成 `Proposed` 状态的 ADR，等人确认（见 [开发流程第 3 节](docs/process/development-workflow.md#3-变更分级与设计产物)）。
4. **不提交敏感内容。** 不提交真实客户项目（`.elk/.edb/.zw1`）、授权信息、个人数据、本机绝对路径。
5. **stdio 模式下不要向 stdout 写任何东西。** stdout 只给 MCP 协议用，日志写 stderr 或文件。

## 写代码之前先读
- 编码规范：[docs/standards/coding-standards.md](docs/standards/coding-standards.md)
- 工具设计规范：[docs/standards/mcp-tool-design.md](docs/standards/mcp-tool-design.md)。新增或修改工具前先填 [工具规格模板](docs/templates/tool-spec.md)
- 测试策略：[docs/standards/testing.md](docs/standards/testing.md)
- 安全：[docs/standards/security.md](docs/standards/security.md)
- Workbench 界面：[docs/standards/workbench-ui.md](docs/standards/workbench-ui.md)。改 `EplanMcp.Workbench` 的界面前先读
- EPLAN API 已验证的事实：[docs/eplan/api-notes.md](docs/eplan/api-notes.md)。**不要凭记忆假设 EPLAN API 的行为**，没验证过的先查文档或在 scratch 项目上验证，再把结论补进 api-notes。
- 本次 brief、相关设计与 ADR：[brief 模板](docs/templates/brief.md)、[评审清单](docs/templates/review-checklist.md)。
- 实机验证与待验项：[live-verification.md](docs/process/live-verification.md)、[large-pending.md](docs/process/large-pending.md)。

## 共享常设规则

- 与负责人交流和文档一律用中文；代码标识符、路径、命令保持原文，代码注释、日志和提交信息用英文。
- 缺失或不可读的数据永远不构成违规：只给出警告与证据缺口，不据此判违规或拒绝，不笼统返回 `CAPABILITY_MISSING`。这不放宽独立成立的写入保护、审批或权限要求。
- 每个实机失败都以**完全相同的输入**补成 Small 回归测试；使用可提交的脱敏夹具，精确保留触发失败的结构和值。客户输入不得原样入库时，在本地受控保存原输入，仓库复现只替换敏感字段并验证仍会触发同一失败。修复后重跑 Small 与受影响 Large。
- 保持 main 的 Large 测试通过，绝不弱化断言，不删除检查来掩盖失败；未运行的必要类按实机规范登记待验。
- **不执行 git commit，不创建提交。** 交付可审阅的未提交改动及证据，由维护者审阅、提交并运行本地关卡。
- 修改后按 `.editorconfig` 格式化空白，运行相关 `dotnet format` 检查；不夹带无关格式重排。
- **api-notes 证据规则**：不要凭记忆假设 EPLAN API 行为；依赖每一条行为前，必须能在 [api-notes](docs/eplan/api-notes.md) 找到依据。没有记录就先查官方文档或按获准范围在 scratch 验证，记录版本、条件、精确输入、结论与证据，再编写依赖它的代码。不能运行实机时如实登记待验，不宣称验证完成。
- 提交格式、scope 与署名规则统一见 [开发流程第 5 节](docs/process/development-workflow.md#5-分支与提交) 及上面的硬性规则；变更集大小按流程第 1 节统计。

## 输出目录与命令
```powershell
dotnet build EplanMcp.slnx                      # 全部，含 EPLAN 插件（需要本机装有 EPLAN）
dotnet build EplanMcp.Ci.slnf                   # 不含插件，与 CI 相同
dotnet test --solution EplanMcp.Ci.slnf -- --filter-not-trait "Size=Large"   # Small + Medium
dotnet test --project tests\EplanMcp.Host.Tests -- --filter-trait "Size=Small"
dotnet test --project tests\EplanMcp.Large.Tests   # Large：需要 EPLAN 以夹具运行（eng\scripts\eval-fixture.ps1 -Open），见该目录的 README
dotnet format EplanMcp.Ci.slnf --verify-no-changes
pwsh eng/scripts/local-gate.ps1 -Push             # 本地关卡（与 CI 相同的步骤）通过后直接推送 main，不开 PR、不等 CI
dotnet run --project src\EplanMcp.Host -- doctor   # 自检；serve（默认）是 stdio MCP 服务器
dotnet build src\EplanMcp.Bridge.Eplan -c Release  # 注册脚本默认用 Release 版插件（可用 -Configuration 改）
eng\scripts\eplan-addin.ps1 -Action register    # 开发用：注册插件，立即加载；每个用到的变体各注册一次（-Variant 'Electric P8'）；改了插件要重新跑上一行，再重启 EPLAN
```
- 输出目录：解决方案构建和单项目构建（包括 `dotnet run --project`、`dotnet test --project`）共用 `bin\<配置>\<目标框架>\`，所以 `dotnet test --project ... --no-build` 可以直接用解决方案构建的结果。给 MCP 客户端配置时指向 `src\EplanMcp.Host\bin\<配置>\net10.0\eplanmcp.exe`。
- 测试运行在 Microsoft.Testing.Platform 上（`global.json` 中选定），`--` 之后是 xUnit 的参数。
- Host 发现插件时只认 `EplanMcp.Bridge.` 前缀的管道；测试用环境变量 `EPLANMCP_PIPE_PREFIX` 换成自己的前缀，避免和正在运行的 EPLAN 冲突。
- 插件日志：`%USERPROFILE%\EplanMcp\logs\bridge-*.log`；插件启动失败时看 `bridge-startup-error.log`。
- 本地草稿、删改清单和检查证据放 `.local/`（忽略，不提交）；构建与测试输出按上面的 `bin\<配置>\<目标框架>\` 约定，不写进源码目录。
- 本地关卡要求已提交的干净工作区。代理遵守不提交约束时，只执行相关构建、Small/Medium 和格式检查，不自行运行 `local-gate.ps1 -Push`。Large、插件命令须在任务允许 EPLAN 时使用。

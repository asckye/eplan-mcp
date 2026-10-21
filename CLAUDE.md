# CLAUDE.md

本文件是给 Claude Code 的项目说明。人类贡献者请看 [CONTRIBUTING.md](CONTRIBUTING.md)，两者的规则一致。

## 项目是什么
EplanMcp：一个 MCP 服务器，让 AI 代理通过 EPLAN .NET API 安全地读取和修改 EPLAN Electric P8 / Pro Panel 项目。
架构见 [docs/architecture/architecture.md](docs/architecture/architecture.md)，关键决策见 [docs/adr/](docs/adr/)。

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

## 约定
- **与项目负责人交流一律使用中文**：回复、提问、进度说明、总结和表格都用中文，长时间运行任务之后也一样；代码标识符、API 名称、文件路径和命令保持原文。
- 文档用中文；代码标识符、代码注释、日志消息、提交信息用英文。
- 提交信息遵循 Conventional Commits，scope 取自：`host` `bridge` `contracts` `tools` `policy` `audit` `workbench` `installer` `evals` `docs` `ci` `spikes`。
- 一个 PR 只做一件事，改动目标在 200 行以内，超过 400 行必须拆分。行数只统计生产代码和文档；测试代码、生成文件、快照、夹具不计入。

## 本机环境注意事项
- 开发机装有 EPLAN 2026.0.3（`C:\Program Files\EPLAN\Platform\2026.0.3\Bin`），通常以 `/Variant:"Pro Panel"` 启动。
- EPLAN 的系统日志在 `C:\Users\Public\EPLAN\<变体>\EplLog.eldb`（SQLite，UTF-8，表 `TSysMsg_v100`）。EPLAN 里的操作"返回成功但没有效果"时，先查这里。
- EPLAN 进程的 `TEMP` 如果是 `C:\WINDOWS\TEMP`，脚本编译会失败（CS2001）。根因是用户 PATH 过长，导致登录时用户环境变量没有加载，详见 api-notes。
- 不要修改用户的 PATH 或其他全局环境变量；安装过程也一样。

## 常用命令
```powershell
dotnet build EplanMcp.slnx                      # 全部，含 EPLAN 插件（需要本机装有 EPLAN）
dotnet build EplanMcp.Ci.slnf                   # 不含插件，与 CI 相同
dotnet test --solution EplanMcp.Ci.slnf -- --filter-not-trait "Size=Large"   # Small + Medium
dotnet test --project tests\EplanMcp.Host.Tests -- --filter-trait "Size=Small"
dotnet test --project tests\EplanMcp.Large.Tests   # Large：需要 EPLAN 以夹具运行（eng\scripts\eval-fixture.ps1 -Open），见该目录的 README
dotnet format EplanMcp.Ci.slnf --verify-no-changes
dotnet run --project src\EplanMcp.Host -- doctor   # 自检；serve（默认）是 stdio MCP 服务器
dotnet build src\EplanMcp.Bridge.Eplan -c Release  # 注册脚本默认用 Release 版插件（可用 -Configuration 改）
eng\scripts\eplan-addin.ps1 -Action register    # 开发用：注册插件，立即加载；每个用到的变体各注册一次（-Variant 'Electric P8'）；改了插件要重新跑上一行，再重启 EPLAN
```
- 输出目录：解决方案构建和单项目构建（包括 `dotnet run --project`、`dotnet test --project`）共用 `bin\<配置>\<目标框架>\`，所以 `dotnet test --project ... --no-build` 可以直接用解决方案构建的结果。给 MCP 客户端配置时指向 `src\EplanMcp.Host\bin\<配置>\net10.0\eplanmcp.exe`。
- 测试运行在 Microsoft.Testing.Platform 上（`global.json` 中选定），`--` 之后是 xUnit 的参数。
- Host 发现插件时只认 `EplanMcp.Bridge.` 前缀的管道；测试用环境变量 `EPLANMCP_PIPE_PREFIX` 换成自己的前缀，避免和正在运行的 EPLAN 冲突。
- 插件日志：`%USERPROFILE%\EplanMcp\logs\bridge-*.log`；插件启动失败时看 `bridge-startup-error.log`。

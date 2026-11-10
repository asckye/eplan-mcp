# CLAUDE.md

本文件是给 Claude Code 的项目说明。人类贡献者请看 [CONTRIBUTING.md](CONTRIBUTING.md)。

## 项目是什么
EplanMcp：一个 MCP 服务器，让 AI 代理通过 EPLAN .NET API 安全地读取和修改 EPLAN Electric P8 / Pro Panel 项目。
架构见 [docs/architecture/architecture.md](docs/architecture/architecture.md)，关键决策见 [docs/adr/](docs/adr/)。

## 开发规范

遵循[《工程开发规范》3.0](https://github.com/asckye/engineering-standards/tree/v3.0)：通用规范、MCP 服务规范（草稿 0.2）和桌面应用规范，装了插件 `engineering-standards@asckye-standards` 时按任务加载对应 skill。采用范围、等级、提交格式、语言政策、合并门、证据台账和长期偏差见 [docs/standards/README.md](docs/standards/README.md)。

**用工具在 EPLAN 里画图之前，先通读 [docs/eplan/drawing-rules.md](docs/eplan/drawing-rules.md)**（通用规则：作图顺序、行列版式、命名、数据关联、画完检查清单），再读项目所属公司的作图配置（见该文档第 10 节）；画完按其中的检查清单核对，发现规则不对就改对应的文档。

<a id="硬性规则违反任何一条都不能合并"></a>

## 硬性规则（违反任何一条都不能合并）

原五条硬性规则对应以下规范条款，含义见[逐项对照表](docs/standards/README.md#原规则--规范条款)：

1. [AI-10](https://github.com/asckye/engineering-standards/blob/v3.0/skills/general-engineering-standard/references/19-ai.md#ai-machine)
2. [GIT-04](https://github.com/asckye/engineering-standards/blob/v3.0/skills/general-engineering-standard/references/18-process-review-git.md#review-git)
3. [G-05](https://github.com/asckye/engineering-standards/blob/v3.0/skills/general-engineering-standard/references/00-purpose.md#purpose-exceptions) 与 [18.3](https://github.com/asckye/engineering-standards/blob/v3.0/skills/general-engineering-standard/references/18-process-review-git.md#review-rfc)
4. [GIT-07](https://github.com/asckye/engineering-standards/blob/v3.0/skills/general-engineering-standard/references/18-process-review-git.md#review-git)
5. [ARC-12](https://github.com/asckye/engineering-standards/blob/v3.0/skills/general-engineering-standard/references/02-architecture.md#arch-logging)、[PRO-20](https://github.com/asckye/engineering-standards/blob/v3.0/skills/mcp-server-standard/references/01-protocol.md#pro-transport)

## 本机环境注意事项
- 开发机装有 EPLAN 2026.0.3（`C:\Program Files\EPLAN\Platform\2026.0.3\Bin`），通常以 `/Variant:"Pro Panel"` 启动。
- EPLAN 的系统日志在 `C:\Users\Public\EPLAN\<变体>\EplLog.eldb`（SQLite，UTF-8，表 `TSysMsg_v100`）。EPLAN 里的操作"返回成功但没有效果"时，先查这里。
- EPLAN 进程的 `TEMP` 如果是 `C:\WINDOWS\TEMP`，脚本编译会失败（CS2001）。根因是用户 PATH 过长，导致登录时用户环境变量没有加载，详见 api-notes。

## 常用命令
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

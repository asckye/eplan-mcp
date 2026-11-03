# 参与贡献

EplanMcp 是一个 MCP 服务器，让 AI 代理通过 EPLAN .NET API 安全地读取和修改 EPLAN Electric P8 / Pro Panel 项目。架构见 [docs/architecture/architecture.md](docs/architecture/architecture.md)。

遵循[《工程开发手册》](docs/standards/engineering-handbook.html)（先读★部分与改动涉及的章节）和 [docs/standards/README.md](docs/standards/README.md)；采用范围、提交格式、语言政策、合并门及证据台账均见后者。

## 第一次克隆后

```bash
git config core.hooksPath eng/hooks
```

启用仓库现有的本地提交检查。

## 开发环境

- Windows 10/11 x64
- .NET SDK 10（版本由 `global.json` 固定）和 .NET Framework 4.8 目标包
- Visual Studio 2026 或 JetBrains Rider（可选）
- 运行 Large 测试需要：EPLAN Electric P8 / Pro Panel 2026，以及包含 **API 扩展**的授权

## 命令与输出目录

常用构建、测试、自检、插件命令和输出目录说明见 [CLAUDE.md](CLAUDE.md#常用命令)。本地关卡入口：

```powershell
pwsh eng/scripts/local-gate.ps1 -Push
```

脚本在已提交的干净工作区运行；本地关卡的选择见 [ADR-0048](docs/adr/0048-local-gate-is-the-merge-gate.md)。

安全问题的反馈方式见 [SECURITY.md](SECURITY.md)。

## 构建配置入口

[`.editorconfig`](.editorconfig) 保存格式与分析器配置，[`Directory.Build.props`](Directory.Build.props) 保存共享构建属性，[`Directory.Packages.props`](Directory.Packages.props) 保存包版本。当前 `PlatformTarget=x64`、`AppendPlatformToOutputPath=false`，解决方案和单项目构建共用 `bin/<配置>/<目标框架>/`。

Bridge 的 [`BannedSymbols.txt`](src/EplanMcp.Bridge.Eplan/BannedSymbols.txt) 由 `Microsoft.CodeAnalysis.BannedApiAnalyzers` 检查，RS0030 在构建中作为错误；表中收录的 EPLAN 行为证据见 [API 笔记的实现证据索引](docs/eplan/api-notes.md#实现证据索引)。

[`EplanMcp.Bridge.Eplan.csproj`](src/EplanMcp.Bridge.Eplan/EplanMcp.Bridge.Eplan.csproj) 从 `EplanBinPath` 引用本机 EPLAN DLL，引用的 `Private=false`；`EplanVersion` 默认是 `2026`。`ValidateEplanApiReferences` 检查目录、各 DLL 是否存在以及文件主版本是否匹配，只读版本资源，不将 DLL 加载进构建进程。指定其他安装时使用 `-p:EplanBinPath=<Bin目录>`，年份不同时同时使用 `-p:EplanVersion=<年份>`。

Bridge 对 Newtonsoft.Json 的编译引用为 `ExcludeAssets="runtime"`、`PrivateAssets="all"`，运行时使用 EPLAN 自带版本；源码 polyfill 包不增加运行时程序集。实际包版本见中央包配置，同进程程序集情况见 [B1](docs/eplan/api-notes/environment-and-licensing.md#b1-程序集与目标框架)。

## 测试入口

当前测试类使用 `[Trait("Size", "Small")]`、`Medium`、`Large`。对应[手册 12.1](docs/standards/engineering-handbook.html#testing-layers) 的层次分别是纯逻辑/单元、本机进程与 IO 集成、厂商实机。Host 的 Small + Medium 运行命令为：

```powershell
dotnet test --project tests\EplanMcp.Host.Tests -- --filter-not-trait "Size=Large"
```

具体覆盖和快照操作见 [Host 测试说明](tests/EplanMcp.Host.Tests/README.md)，实机环境及夹具见 [Large 测试说明](tests/EplanMcp.Large.Tests/README.md)。

[`tests/test-suites.json`](tests/test-suites.json) 记录程序集的 `assembly` 和 `minPassed`；[`check-test-floor.ps1`](eng/scripts/check-test-floor.ps1) 读取同一次运行的 TRX，可用 `-Assemblies` 限定本次程序集。脚本检查缺失结果、未登记程序集、低于下限，以及下限低于通过数 80% 的情况；更新值的计算为 `floor(通过数 × 0.9)`。

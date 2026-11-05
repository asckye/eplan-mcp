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

## 开发工具（新的开发电脑上安装）

下面这些工具只用于**开发阶段**，不是产品的依赖，不会随 EplanMcp 发布（Maple Mono 字体例外，见表中说明）。除 Maple Mono 字体（SIL OFL 1.1）外，都是 MIT 许可证或官方工具。

| 工具 | 用途 | 安装 |
|---|---|---|
| [Node.js](https://nodejs.org/) LTS | archify 和 MCP Inspector 的运行环境 | `winget install --id OpenJS.NodeJS.LTS -e` |
| [archify](https://github.com/tt-a1i/archify) | Claude Code 技能：把架构、工作流程、时序画成可交互的 HTML 图，图存放在 [docs/diagrams/](docs/diagrams/) | 版本记录在 `skills-lock.json`，技能文件本身不进仓库。在仓库根目录依次运行下面两条命令：先按锁文件还原技能，再把图的字体切换为 Maple Mono（技能每次更新后都要再运行第二条） |
| [Maple Mono](https://github.com/subframe7536/maple-font) 字体 | 项目统一字体：archify 图、开发时的编辑器和终端；Workbench 界面（M5）也用它，那里随程序附带 `MapleMono-CN` 的字体文件和许可证（ADR-0017），工程师不需要另装 | 从 [Releases](https://github.com/subframe7536/maple-font/releases) 下载 `MapleMono-NF-CN` 压缩包，解压后选中全部字体文件，右键"安装"；编辑器和终端里的字体名填 `Maple Mono NF CN`。构建 Workbench 用的字体文件另外运行 `eng\scripts\get-fonts.ps1` 取得：它下载官方的 v7.9 `MapleMono-CN.zip`，核对 SHA-256 后只把 Regular、SemiBold 和许可证解压到不提交的 `.local\fonts\`，构建时复制到程序旁的 `Fonts\` |
| [covagashi/eplan-development-skill](https://github.com/covagashi/eplan-development-skill) | Claude Code 技能：教 AI 写 EPLAN 的 C# 脚本、API 和 Remote Client 代码，让 Bridge 代码写得更准 | 按该仓库 README 安装到 Claude Code |
| [covagashi/eplan-rag-mcp](https://github.com/covagashi/eplan-rag-mcp) 中的文档服务 | EPLAN 2026/2027 官方文档的语义检索 MCP 服务器，开发时让 AI 随时查 API 文档，不凭记忆猜 | 按该仓库 README 中"远程文档服务器"一节，把服务器地址加到 Claude Code 的 MCP 配置中 |
| [MCP Inspector](https://github.com/modelcontextprotocol/inspector) | MCP 官方的可视化调试工具，打通骨架时用来手动调用和检查我们的工具 | 需要 Node.js；使用时运行 `npx @modelcontextprotocol/inspector <启动 Host 的命令>` |

安装 archify（`DISABLE_TELEMETRY` 和 `DO_NOT_TRACK` 关闭安装工具的遥测；`skills experimental_install` 只会装到 `.agents/`，Claude Code 读不到，所以不用它）：
```powershell
$env:DISABLE_TELEMETRY = "1"; $env:DO_NOT_TRACK = "1"; npx -y skills add tt-a1i/archify --skill archify --agent claude-code --copy --yes
```
安装后如果 `skills-lock.json` 发生了变化，说明 archify 发布了新版本：按开发流程作为依赖变更评审后再提交。
```powershell
node eng/scripts/apply-archify-font.mjs
```

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

# EplanMcp

让 AI 代理通过 [Model Context Protocol](https://modelcontextprotocol.io/) **安全地**操作 EPLAN Electric P8 / Pro Panel：查询项目、生成页面、放置设备、整理标识、更新报表、导出与检查。
工程师始终掌握控制权：写入前审批，事后可审计，出错可回滚，在 EPLAN 中可以用 Ctrl+Z 撤销。

> **状态：M4 工程工作流已达到完成标准**（2026-10-20），M5 可运维进行中。28 个工具（15 个只读，5 个写入，8 个工作流工具：检查、更新报表、PDF/DXF/图片导出，以及删页、页面重编号、设备编号这 3 个 Destructive 工具）在 EPLAN 2026 Electric P8 上对示例项目的 scratch 副本实机验证（[api-notes](docs/eplan/api-notes.md) A17–A20）。写入先审批（EPLAN 中的对话框，[ADR-0011](docs/adr/0011-approval-out-of-band.md)），在事务中执行，可以用 Ctrl+Z 撤销，写进审计哈希链；Destructive 操作前自动备份（[ADR-0013](docs/adr/0013-destructive-backup.md)）。43 个 [Large 测试](tests/EplanMcp.Large.Tests/README.md)在开发机上全部通过；[Agent 评测](evals/README.md)中每道写入题和工作流题都已解出，需要工程师在场审批的第二批 Destructive 题 3/3。M1 于 2026-10-18、M2 于 2026-10-19、M3 于 2026-10-20 达到完成标准。**下一步**：M5 可运维（[M5 设计](docs/design/2026-10-20-m5-operability.md)）：Workbench（WPF，[ADR-0017](docs/adr/0017-workbench-wpf-tia-theme.md)）、按用户安装的安装包、Claude Code 插件、离线宿主与实机 CI。路线图见 [架构文档第 11 节](docs/architecture/architecture.md#11-里程碑)。

## 工作方式

```
MCP 客户端（Claude Code 等）
   │ stdio
eplanmcp.exe（Host，.NET 10）：工具、策略、审批、审计
   │ 命名管道（仅当前用户）
EplanMcp Bridge（EPLAN 插件，.NET Framework 4.8.1）：在 EPLAN 主线程上调用 EPLAN API
   │
EPLAN 2026（你正在使用的界面）
```

## 运行要求（计划）
- Windows 10/11 x64
- EPLAN Electric P8 或 Pro Panel 2026；2024、2025、2027 预计可用，实测后列入支持范围（[ADR-0009](docs/adr/0009-supported-eplan-versions.md)）。2022、2023 暂不支持
- 包含 **API 扩展**的 EPLAN 授权。具体是哪一种授权（开发者授权或运行时授权）以及签名方式，正在 M1 中验证，见 [ADR 索引](docs/adr/README.md) 中的 ADR-0007。

## 现在怎么用（开发版，只读）
还没有安装包（M5）；现在要从源码构建。只能读取项目，写入在 M3 中实现。

1. 构建 eplanmcp 和 EPLAN 插件：
   ```powershell
   dotnet build src\EplanMcp.Host -c Release
   dotnet build src\EplanMcp.Bridge.Eplan -c Release
   ```
2. 在每个要用的 EPLAN 变体中注册一次插件（会启动 EPLAN）：
   ```powershell
   eng\scripts\eplan-addin.ps1 -Action register -Variant 'Electric P8'
   ```
3. 在 EPLAN 中打开项目，然后自检，每一项都应是 OK 或 INFO：
   ```powershell
   src\EplanMcp.Host\bin\Release\net10.0\eplanmcp.exe doctor
   ```
4. 加到 Claude Code（`<仓库目录>` 换成本仓库的完整路径）：
   ```powershell
   claude mcp add -s user eplan -- "<仓库目录>\src\EplanMcp.Host\bin\Release\net10.0\eplanmcp.exe" serve
   ```
5. 在 Claude Code 中直接用中文提问，例如"这个项目有多少页""-K1 在哪几页上""哪些电机没有部件""=GAA+A1-FC1 的负载侧接到哪些设备"。

可以查询项目概况、页、设备、连接、部件、系统消息、已有的检查结果，以及 EPLAN 宏目录中的窗口宏，共 15 个只读工具，不会修改项目。

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

## 文档

| 想了解 | 读这里 |
|---|---|
| 预览安装包怎么开始使用 | [中文入门指南](docs/getting-started/zh-CN.md) |
| 系统怎么设计、为什么这样设计 | [架构](docs/architecture/architecture.md) · [ADR](docs/adr/README.md) |
| EPLAN API 的已验证事实，以及计划覆盖哪些 API | [api-notes](docs/eplan/api-notes.md) · [API 覆盖矩阵](docs/eplan/api-coverage.md) |
| 工具怎么设计 | [MCP 工具设计规范](docs/standards/mcp-tool-design.md) |
| 代码怎么写 | [编码规范](docs/standards/coding-standards.md) |
| 怎么测试 | [测试策略](docs/standards/testing.md) |
| Workbench 界面怎么做 | [Workbench 界面规范](docs/standards/workbench-ui.md) |
| 安全 | [安全标准与威胁模型](docs/standards/security.md) · [SECURITY.md](SECURITY.md) |
| 怎么协作、怎么发布 | [开发流程](docs/process/development-workflow.md) · [CONTRIBUTING.md](CONTRIBUTING.md) |
| 借鉴了哪些业界实践 | [参考资料与落地对照](docs/references.md) |
| 模板 | [设计文档](docs/templates/design-doc.md) · [工具规格](docs/templates/tool-spec.md) · [ADR](docs/adr/template.md) |

## 许可证
[MIT](LICENSE)

## 声明
EPLAN 是 EPLAN GmbH & Co. KG 的商标。本项目是独立的社区项目，与 EPLAN 公司没有隶属关系。

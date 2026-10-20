# EplanMcp

让 AI 代理通过 [Model Context Protocol](https://modelcontextprotocol.io/) **安全地**操作 EPLAN Electric P8 / Pro Panel：查询项目、生成页面、放置设备、整理标识、更新报表、导出与检查。
工程师始终掌握控制权：写入前审批，事后可审计，出错可回滚，在 EPLAN 中可以用 Ctrl+Z 撤销。

> **状态：M2 只读核心已达到完成标准**（2026-10-19）。13 个只读工具（项目、页面、设备、连接、部件、系统消息、检查结果、多实例选择，[M2 设计](docs/design/2026-10-18-m2-read-core.md)）在 EPLAN 2026 Electric P8 上对示例项目的 scratch 副本实机验证通过，读取不改动项目（[api-notes A12](docs/eplan/api-notes.md#a12-m2-只读工具的实机验证2026-10-19eplan-20260325421electric-p8)）。[Agent 评测 v0](evals/README.md) 用 Claude Sonnet 5.5 完成全部 21 题：开发集 15/15，保留集 6/6（单次运行）。遗留：评测的多次重复运行、Large 测试，以及 M1 的 S4 审批表单人工确认、S5。**下一步**：M3 安全写入，先由项目负责人决定 ADR-0009（支持的 EPLAN 版本）和 ADR-0010（写入结果与恢复）。M1 端到端骨架于 2026-10-18 达到完成标准（[api-notes A10](docs/eplan/api-notes.md#a10-m1-骨架的实机验证2026-10-18)）。路线图见 [架构文档第 11 节](docs/architecture/architecture.md#11-里程碑)。

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
- EPLAN Electric P8 或 Pro Panel 2026
- 包含 **API 扩展**的 EPLAN 授权。具体是哪一种授权（开发者授权或运行时授权）以及签名方式，正在 M1 中验证，见 [ADR 索引](docs/adr/README.md) 中的 ADR-0007。

## 开发工具（新的开发电脑上安装）

下面这些工具只用于**开发阶段**，不是产品的依赖，不会随 EplanMcp 发布。除 Maple Mono 字体（SIL OFL 1.1）外，都是 MIT 许可证或官方工具。

| 工具 | 用途 | 安装 |
|---|---|---|
| [Node.js](https://nodejs.org/) LTS | archify 和 MCP Inspector 的运行环境 | `winget install --id OpenJS.NodeJS.LTS -e` |
| [archify](https://github.com/tt-a1i/archify) | Claude Code 技能：把架构、工作流程、时序画成可交互的 HTML 图，图存放在 [docs/diagrams/](docs/diagrams/) | 版本记录在 `skills-lock.json`，技能文件本身不进仓库。在仓库根目录依次运行下面两条命令：先按锁文件还原技能，再把图的字体切换为 Maple Mono（技能每次更新后都要再运行第二条） |
| [Maple Mono](https://github.com/subframe7536/maple-font) 字体 | 项目统一字体：archify 图、Workbench 界面（M5）、开发时的编辑器和终端 | 从 [Releases](https://github.com/subframe7536/maple-font/releases) 下载 `MapleMono-NF-CN` 压缩包，解压后选中全部字体文件，右键"安装"；编辑器和终端里的字体名填 `Maple Mono NF CN` |
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
| 系统怎么设计、为什么这样设计 | [架构](docs/architecture/architecture.md) · [ADR](docs/adr/README.md) |
| EPLAN API 的已验证事实，以及计划覆盖哪些 API | [api-notes](docs/eplan/api-notes.md) · [API 覆盖矩阵](docs/eplan/api-coverage.md) |
| 工具怎么设计 | [MCP 工具设计规范](docs/standards/mcp-tool-design.md) |
| 代码怎么写 | [编码规范](docs/standards/coding-standards.md) |
| 怎么测试 | [测试策略](docs/standards/testing.md) |
| 安全 | [安全标准与威胁模型](docs/standards/security.md) · [SECURITY.md](SECURITY.md) |
| 怎么协作、怎么发布 | [开发流程](docs/process/development-workflow.md) · [CONTRIBUTING.md](CONTRIBUTING.md) |
| 借鉴了哪些业界实践 | [参考资料与落地对照](docs/references.md) |
| 模板 | [设计文档](docs/templates/design-doc.md) · [工具规格](docs/templates/tool-spec.md) · [ADR](docs/adr/template.md) |

## 许可证
[MIT](LICENSE)

## 声明
EPLAN 是 EPLAN GmbH & Co. KG 的商标。本项目是独立的社区项目，与 EPLAN 公司没有隶属关系。

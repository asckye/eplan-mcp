# EplanMcp

让 AI 代理通过 [Model Context Protocol](https://modelcontextprotocol.io/) **安全地**操作 EPLAN Electric P8 / Pro Panel：查询项目、生成页面、放置设备、整理标识、更新报表、导出与检查。
工程师始终掌握控制权：真实项目写入逐次审批，操作可审计。

> **状态：v0.3.0 预览版，72 个工具**。已于 2026-10-26 [发布](https://github.com/asckye/eplan-mcp/releases/tag/v0.3.0)，供测试使用。M0–M4 已完成；M4c 的 PDF 注释导入待有效样本，翻译库导入待验证；M5 的 ZIP 交付和虚拟机连接已实现，干净机器验收和跨机器实测待完成；M6 第 1 阶段（工程包与资源）和第 2 阶段（多步工作流计划，见[计划使用指南](docs/guides/plans.zh-CN.md)，核验到 3,000 页）已完成，第 3–5 阶段未开始；通用属性读取已实现，属性写入待实现。路线图见 [架构文档第 11 节](docs/architecture/architecture.md#11-里程碑)。

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

## 运行要求

- Windows 10/11 x64
- EPLAN 2026 Electric P8 或 Pro Panel（实测版本 2026.0.3）；2024、2025、2027 尚未验证，留待 M7（[ADR-0009](docs/adr/0009-supported-eplan-versions.md)）。2022、2023 暂不支持
- 包含 **API 扩展**、允许加载未签名插件的 EPLAN 授权；0.x 发布未签名插件（[ADR-0007](docs/adr/0007-licensing-and-distribution.md)）

## 安装

后续版本只提供 `EplanMcp-<version>.zip`，以 [GitHub Releases](https://github.com/asckye/eplan-mcp/releases) 的实际附件为准。下载 ZIP 和 `SHA256SUMS.txt`，校验后解除 ZIP 锁定，将整个目录解压到短且固定的位置（如 `D:\EplanMcp`），打开 **`workbench\EplanMcp.Workbench.exe`**，勾选变体注册 EPLAN 插件并配置客户端。无需管理员或联网，数据仍在独立的 `%USERPROFILE%\EplanMcp`；升级先关闭 EPLAN、Workbench 和客户端再整体替换程序目录，卸载先在 Workbench 撤销注册再删除目录。详细步骤见[中文入门指南](docs/getting-started/zh-CN.md)。

## 工具概览

默认 `full` 提供全部 64 个工具；`serve --profile core` 提供固定的 25 个常用工具。已实现清单见[工具映射](catalog/mapping/tools.json)，参数与限制见[工具规格](docs/tools/)（目录中也包含待实现的规格）。

| 分组 | 能做什么 |
|---|---|
| 读取 | 查询项目、页、设备、连接、电缆、端子、PLC、部件、修订、消息、检查结果、宏与占位符 |
| 写入 | 新建项目、建页与改页、放置与修改设备、插入宏、应用占位符值、生成连接、检查、更新报表、设置语言 |
| Destructive | 删页、页面/设备/端子/电缆/连接编号、生成电缆、项目翻译、删除语言、导入清单与 PLC 数据 |
| 导出 | PDF、DXF、图片、PXF、部件清单、标签、未翻译文本 |
| 导入 | DXF/DWG、PXF、设备/部件清单、PLC 数据 |
| 部件库与翻译库 | 系统部件库查询、导入、同步与导出；翻译库导出（导入待验证） |
| 工程包与资源 | 导入、列出、查询、构建工程包；搜索、读取与更新资源知识 |

## 安全模型

普通项目写入在 scratch 内自动放行；真实项目默认逐次经 Workbench 或 EPLAN 对话框审批（[ADR-0005](docs/adr/0005-safety-model.md)、[ADR-0011](docs/adr/0011-approval-out-of-band.md)）。Destructive 操作在 scratch 中也要审批，执行前自动备份，备份失败不执行（[ADR-0013](docs/adr/0013-destructive-backup.md)）。

调用记录于审计哈希链；结果未知时暂停相关项目与 EPLAN 实例的写入，待工程师核实并经审批渠道确认后解除（[ADR-0010](docs/adr/0010-write-outcomes.md)）。系统部件库写入同样每次审批并先备份，不能依靠 Ctrl+Z 恢复部件库（[ADR-0019](docs/adr/0019-master-data-writes.md)）。

## 从源码构建（开发者）

开发环境见 [CONTRIBUTING.md](CONTRIBUTING.md)。源码版同样支持读写，遵循上述安全模型。

1. 构建 eplanmcp 和 EPLAN 插件：
   ```powershell
   dotnet build src\EplanMcp.Host -c Release
   dotnet build src\EplanMcp.Bridge.Eplan -c Release
   ```
2. 在每个要用的 EPLAN 变体中注册一次插件（会启动 EPLAN；脚本默认使用 Release 构建，插件重建后需重启 EPLAN）：
   ```powershell
   eng\scripts\eplan-addin.ps1 -Action register -Variant 'Electric P8'
   ```
3. 在 EPLAN 中打开项目，然后自检，按输出处理 WARN / FAIL：
   ```powershell
   src\EplanMcp.Host\bin\Release\net10.0\eplanmcp.exe doctor
   ```
4. 加到 Claude Code（`<仓库目录>` 换成本仓库的完整路径）：
   ```powershell
   claude mcp add -s user eplan -- "<仓库目录>\src\EplanMcp.Host\bin\Release\net10.0\eplanmcp.exe" serve
   ```
5. 在 Claude Code 中直接用中文提问，例如"这个项目有多少页""-K1 在哪几页上""哪些电机没有部件""=GAA+A1-FC1 的负载侧接到哪些设备"。

验证与评测见 [Large 测试](tests/EplanMcp.Large.Tests/README.md)和 [Agent 评测](evals/README.md)。

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
| ZIP 预览版怎么开始使用 | [中文入门指南](docs/getting-started/zh-CN.md) |
| 系统怎么设计、为什么这样设计 | [架构](docs/architecture/architecture.md) · [ADR](docs/adr/README.md) |
| EPLAN API 的已验证事实，以及计划覆盖哪些 API | [api-notes](docs/eplan/api-notes.md) · [API 覆盖矩阵](docs/eplan/api-coverage.md) |
| 工具怎么设计 | [MCP 工具设计规范](docs/standards/mcp-tool-design.md) |
| 代码怎么写 | [编码规范](docs/standards/coding-standards.md) |
| 怎么测试 | [测试策略](docs/standards/testing.md) |
| Workbench 界面怎么做 | [Workbench 界面规范](docs/standards/workbench-ui.md) |
| 安全 | [安全标准与威胁模型](docs/standards/security.md) · [SECURITY.md](SECURITY.md) |
| 怎么协作、怎么发布 | [开发流程](docs/process/development-workflow.md) · [CONTRIBUTING.md](CONTRIBUTING.md) · [预览版发布流程](docs/process/release.md) |
| 借鉴了哪些业界实践 | [参考资料与落地对照](docs/references.md) |
| 模板 | [设计文档](docs/templates/design-doc.md) · [工具规格](docs/templates/tool-spec.md) · [ADR](docs/adr/template.md) |

## 许可证
[MIT](LICENSE)

## 声明
EplanMcp 是独立的开源项目，与 EPLAN GmbH & Co. KG 无关联，也未经其认可或赞助。EPLAN、EPLAN Electric P8 和 EPLAN Pro Panel 是 EPLAN GmbH & Co. KG 的商标或注册商标。

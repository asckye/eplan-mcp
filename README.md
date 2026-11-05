# EplanMcp

[![Release](https://img.shields.io/github/v/release/asckye/eplan-mcp?include_prereleases&label=release)](https://github.com/asckye/eplan-mcp/releases)
[![Build](https://github.com/asckye/eplan-mcp/actions/workflows/build.yml/badge.svg?branch=main)](https://github.com/asckye/eplan-mcp/actions/workflows/build.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](LICENSE)
[![EPLAN 2026](https://img.shields.io/badge/EPLAN-2026-0a6ebd)](docs/adr/0009-supported-eplan-versions.md)

中文 | [English](#english)

让 AI 代理通过 [Model Context Protocol](https://modelcontextprotocol.io/) **安全地**操作 EPLAN Electric P8 / Pro Panel：查询项目、绘制页面、放置设备、整理标识、更新报表、导出与检查。
工程师始终掌握控制权：真实项目写入逐次审批，操作可审计。

> **当前是预览版（0.x），供测试使用**，工具的参数和返回结构仍可能变化。最新版本见 [Releases](https://github.com/asckye/eplan-mcp/releases)，v0.5.0 的范围与排期见[路线图](docs/roadmap.md)，每个版本的变更见 [CHANGELOG](CHANGELOG.md)。

## 能这样用

连接 MCP 客户端后，直接用中文提问或下指令：

- “这个项目有多少页”“-K1 在哪几页上”“哪些电机没有部件”
- “=GAA+A1-FC1 的负载侧接到哪些设备”
- “先建页，再插入一个回路，给我看完整变更和预期结果”
- “把这几页的自由文本中的 Motor 改成 Drive，只改英语，先预览”

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
- EPLAN 2026 Electric P8 或 Pro Panel（实测版本 2026.0.3）；2024、2025、2027 尚未验证，留待 [M7](docs/roadmap.md)（[ADR-0009](docs/adr/0009-supported-eplan-versions.md)）。2022、2023 暂不支持
- 包含 **API 扩展**、允许加载未签名插件的 EPLAN 授权；0.x 发布未签名插件（[ADR-0007](docs/adr/0007-licensing-and-distribution.md)）
- 支持 MCP 的客户端，如 Claude Code、Claude Desktop

## 安装

1. 从 [GitHub Releases](https://github.com/asckye/eplan-mcp/releases) 下载 `EplanMcp-<version>.zip` 和 `SHA256SUMS.txt`（以实际附件为准），校验后解除 ZIP 锁定。
2. 将整个目录解压到短且固定的位置，如 `D:\EplanMcp`。
3. 打开 **`workbench\EplanMcp.Workbench.exe`**，勾选变体注册 EPLAN 插件并配置客户端。

无需管理员或联网，数据放在独立的 `%USERPROFILE%\EplanMcp`。升级先关闭 EPLAN、Workbench 和客户端，再整体替换程序目录；卸载先在 Workbench 撤销注册，再删除目录。详细步骤见[中文入门指南](docs/getting-started/zh-CN.md)（[English](docs/getting-started/en.md)）。

## 工具概览

默认的 `full` 提供全部工具（默认关闭的工具需另行启用），`serve --profile core` 提供固定的 25 个常用工具。数量、清单、Profile 与读写注解以[工具索引](docs/tools/README.md)为准，参数与限制见各工具规格。

| 分组 | 能做什么 |
|---|---|
| 读取 | 查询项目、页、设备、连接、电缆、端子、PLC、部件、符号、修订、消息、检查结果、宏、占位符、属性和页面对象 |
| 绘图与修改 | 新建项目和页，从部件库或符号放置设备，插入宏，放置中断点和电位定义点，添加文字与图形，生成连接，修改属性，替换页面文字 |
| 检查与报表 | 运行 EPLAN 检查，按工程包规则检查，审查页面，生成和更新报表 |
| Destructive | 删页，页面/设备/端子/电缆/连接编号，生成电缆，项目翻译，删除语言，导入清单与 PLC 数据 |
| 导出 | PDF、DXF、图片、PXF、部件清单、标签、未翻译文本 |
| 导入 | DXF/DWG、PXF、设备/部件清单、PLC 数据、PDF 批注 |
| 部件库与翻译库 | 系统部件库查询、导入、同步与导出；翻译库导入与导出 |
| 工程包与知识 | 安装、启用、查询、构建工程包；绑定柜配置；搜索、审核与更新资源知识；为任务编译上下文 |
| 计划执行 | 把多步操作编成计划，审阅后整体批准执行 |
| 通用 API 层 | 搜索 EPLAN API 与 action，查看调用模板，在安全规则允许的范围内调用 |

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

## 文档

| 想了解 | 读这里 |
|---|---|
| 里程碑状态、发布范围与排期 | [路线图](docs/roadmap.md) |
| ZIP 预览版怎么开始使用 | [中文入门指南](docs/getting-started/zh-CN.md) · [English guide](docs/getting-started/en.md) |
| 有哪些工具、每个工具怎么用 | [工具索引](docs/tools/README.md) |
| 系统怎么设计、为什么这样设计 | [架构](docs/architecture/architecture.md) · [ADR](docs/adr/README.md) |
| CLI JSON 输出与安装验收 | [CLI 参考](docs/reference/cli.md) · [发布流程](docs/process/release.md#安装验收清单m5-发布前逐项通过) |
| EPLAN API 的已验证事实，以及计划覆盖哪些 API | [api-notes 主题索引](docs/eplan/api-notes.md) · [API 覆盖矩阵](docs/eplan/api-coverage.md) |
| 工具怎么设计 | [手册 MCP 条款](docs/standards/engineering-handbook.html#pa) |
| 代码怎么写 | [手册第 3 章](docs/standards/engineering-handbook.html#code) |
| 怎么测试 | [手册第 12 章](docs/standards/engineering-handbook.html#testing) |
| Workbench 界面怎么做 | [手册界面条款](docs/standards/engineering-handbook.html#ui) · [Workbench 实现记录](docs/design/2026-10-20-m5-operability.md#workbench-implementation) |
| 安全 | [项目威胁模型](docs/architecture/architecture.md#13-威胁模型) · [SECURITY.md](SECURITY.md) |
| 怎么协作、怎么发布 | [手册开发流程条款](docs/standards/engineering-handbook.html#review) · [CONTRIBUTING.md](CONTRIBUTING.md) · [预览版发布流程](docs/process/release.md) |
| 借鉴了哪些业界实践 | [参考资料与落地对照](docs/references.md) |
| 模板 | [设计文档](docs/templates/design-doc.md) · [工具规格](docs/templates/tool-spec.md) · [ADR](docs/adr/template.md) |

## 参与贡献

问题和工具需求请提 [Issue](https://github.com/asckye/eplan-mcp/issues/new/choose)。开发环境、开发工具和提交规范见 [CONTRIBUTING.md](CONTRIBUTING.md)，安全问题按 [SECURITY.md](SECURITY.md) 私下报告，社区规则见[行为准则](CODE_OF_CONDUCT.md)。

## English

EplanMcp is an MCP server that lets AI agents read and modify EPLAN Electric P8 / Pro Panel projects through the EPLAN .NET API. It runs locally on Windows with EPLAN 2026 and needs a licence that includes the API extension. Writes to real projects need the engineer's approval one by one, destructive operations are backed up first, and calls are recorded in a hash-chained audit log.

Releases are previews for testing. Start with the [English getting started guide](docs/getting-started/en.md). The rest of the documentation is mainly in Chinese.

## 许可证
[MIT](LICENSE)

## 声明
EplanMcp 是独立的开源项目，与 EPLAN GmbH & Co. KG 无关联，也未经其认可或赞助。EPLAN、EPLAN Electric P8 和 EPLAN Pro Panel 是 EPLAN GmbH & Co. KG 的商标或注册商标。

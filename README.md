# EplanMcp

让 AI 代理通过 [Model Context Protocol](https://modelcontextprotocol.io/) **安全地**操作 EPLAN Electric P8 / Pro Panel：查询项目、生成页面、放置设备、整理标识、更新报表、导出与检查。
工程师始终掌握控制权：写入前审批，事后可审计，出错可回滚，在 EPLAN 中可以用 Ctrl+Z 撤销。

> **状态：M1 进行中**（2026-10-13）。M0 已完成；M1 的技术验证 S0–S3 已完成，结论见 [spikes/README.md](spikes/README.md) 和 [api-notes](docs/eplan/api-notes.md)。**下一步**：S4（客户端对 MCP elicitation 的支持），然后打通端到端骨架（Host ↔ 管道 ↔ Bridge ↔ EPLAN）。还没有产品代码。路线图见 [架构文档第 11 节](docs/architecture/architecture.md#11-里程碑)。

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

下面这些工具只用于**开发阶段**，不是产品的依赖，不会随 EplanMcp 发布。它们都是 MIT 许可证或官方工具。

| 工具 | 用途 | 安装 |
|---|---|---|
| [covagashi/eplan-development-skill](https://github.com/covagashi/eplan-development-skill) | Claude Code 技能：教 AI 写 EPLAN 的 C# 脚本、API 和 Remote Client 代码，让 Bridge 代码写得更准 | 按该仓库 README 安装到 Claude Code |
| [covagashi/eplan-rag-mcp](https://github.com/covagashi/eplan-rag-mcp) 中的文档服务 | EPLAN 2026/2027 官方文档的语义检索 MCP 服务器，开发时让 AI 随时查 API 文档，不凭记忆猜 | 按该仓库 README 中"远程文档服务器"一节，把服务器地址加到 Claude Code 的 MCP 配置中 |
| [MCP Inspector](https://github.com/modelcontextprotocol/inspector) | MCP 官方的可视化调试工具，打通骨架时用来手动调用和检查我们的工具 | 需要 Node.js；使用时运行 `npx @modelcontextprotocol/inspector <启动 Host 的命令>` |

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

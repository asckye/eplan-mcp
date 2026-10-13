# EplanMcp

让 AI 代理通过 [Model Context Protocol](https://modelcontextprotocol.io/) **安全地**操作 EPLAN Electric P8 / Pro Panel：查询项目、生成页面、放置设备、整理标识、更新报表、导出与检查。
工程师始终掌握控制权：写入前审批，事后可审计，出错可回滚，在 EPLAN 中可以用 Ctrl+Z 撤销。

> **状态：M0（项目启动）**。目前只有架构、决策记录、设计规范和开发流程，还没有可运行的代码。路线图见 [架构文档第 11 节](docs/architecture/architecture.md#11-里程碑)。

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

## 文档

| 想了解 | 读这里 |
|---|---|
| 系统怎么设计、为什么这样设计 | [架构](docs/architecture/architecture.md) · [ADR](docs/adr/README.md) |
| EPLAN API 的已验证事实 | [api-notes](docs/eplan/api-notes.md) |
| 工具怎么设计 | [MCP 工具设计规范](docs/standards/mcp-tool-design.md) |
| 代码怎么写 | [编码规范](docs/standards/coding-standards.md) |
| 怎么测试 | [测试策略](docs/standards/testing.md) |
| 安全 | [安全标准与威胁模型](docs/standards/security.md) · [SECURITY.md](SECURITY.md) |
| 怎么协作、怎么发布 | [开发流程](docs/process/development-workflow.md) · [CONTRIBUTING.md](CONTRIBUTING.md) |
| 借鉴了哪些业界实践 | [参考资料与落地对照](docs/references.md) |
| 模板 | [设计文档](docs/templates/design-doc.md) · [工具规格](docs/templates/tool-spec.md) · [ADR](docs/adr/template.md) |

## 声明
EPLAN 是 EPLAN GmbH & Co. KG 的商标。本项目是独立的社区项目，与 EPLAN 公司没有隶属关系。

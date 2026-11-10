# AGENTS.md

本文件供 Codex 等编码代理使用。项目说明、环境与命令见 [CLAUDE.md](CLAUDE.md)。遵循[《工程开发规范》3.0](https://github.com/asckye/engineering-standards/tree/v3.0)和[项目采用声明](docs/standards/README.md)；项目事实文档由采用声明末尾索引。

## 硬性规则（违反任何一条都不能合并）

原五条硬性规则对应以下规范条款，含义见[逐项对照表](docs/standards/README.md#原规则--规范条款)：

1. [AI-10](https://github.com/asckye/engineering-standards/blob/v3.0/skills/general-engineering-standard/references/19-ai.md#ai-machine)
2. [GIT-04](https://github.com/asckye/engineering-standards/blob/v3.0/skills/general-engineering-standard/references/18-process-review-git.md#review-git)
3. [G-05](https://github.com/asckye/engineering-standards/blob/v3.0/skills/general-engineering-standard/references/00-purpose.md#purpose-exceptions) 与 [18.3](https://github.com/asckye/engineering-standards/blob/v3.0/skills/general-engineering-standard/references/18-process-review-git.md#review-rfc)
4. [GIT-07](https://github.com/asckye/engineering-standards/blob/v3.0/skills/general-engineering-standard/references/18-process-review-git.md#review-git)
5. [ARC-12](https://github.com/asckye/engineering-standards/blob/v3.0/skills/general-engineering-standard/references/02-architecture.md#arch-logging)、[PRO-20](https://github.com/asckye/engineering-standards/blob/v3.0/skills/mcp-server-standard/references/01-protocol.md#pro-transport)

## 项目入口

- [EPLAN API 证据](docs/eplan/api-notes.md)与[待验台账](docs/process/large-pending.md)。
- [贡献说明](CONTRIBUTING.md)与[架构](docs/architecture/architecture.md)。
- [工具记录](docs/tools/)、[项目工具规格字段](docs/templates/tool-spec.md)与[评测入口](evals/README.md)。
- [Workbench 实现记录](docs/design/2026-10-20-m5-operability.md#workbench-implementation)。

本地草稿、删改清单与检查证据位于忽略的 `.local/`。

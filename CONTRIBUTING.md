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

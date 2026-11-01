# 参与贡献

感谢你愿意改进 EplanMcp。完整流程见 [docs/process/development-workflow.md](docs/process/development-workflow.md)，这里是最短的入门路径。

## 第一次克隆后
```bash
git config core.hooksPath eng/hooks
```
启用本地提交检查，规则见 [开发流程第 5 节](docs/process/development-workflow.md#5-分支与提交)。

## 开发环境
- Windows 10/11 x64
- .NET SDK 10（版本由 `global.json` 固定）和 .NET Framework 4.8 目标包
- Visual Studio 2026 或 JetBrains Rider（可选）
- 运行 Large 测试需要：EPLAN Electric P8 / Pro Panel 2026，以及包含 **API 扩展**的授权

## 交付一个变更集

1. 负责人按 [brief 模板](docs/templates/brief.md) 写清原因、设计、不变量、验收测试和边界，并确定 L0–L3 级别；L2 先评审设计，L3 须负责人确认 ADR。
2. Codex 在 worktree 按 [AGENTS.md](AGENTS.md)、编码规范和测试策略实现，交付未提交改动。
3. 对照 [评审清单](docs/templates/review-checklist.md) 审查并处理发现；理由写提交正文，L2/L3 必填 why / verify / risk / rollback。
4. 涉及 EPLAN 行为的变更集，受影响 Large 类须实机通过，或列入 [待验台账](docs/process/large-pending.md) 且提交正文明确说明待验（[实机规范](docs/process/live-verification.md)）。
5. 维护者审阅并提交后，在干净工作区运行 `pwsh eng/scripts/local-gate.ps1 -Push`，关卡通过才直推 `main`；CI 是事后检查。

一个变更集是一份 brief 和一次关卡，大小按开发流程第 1 节计算。外部贡献者仍使用 PR，见 [.github/pull_request_template.md](.github/pull_request_template.md)。

## 规矩
- 写操作只在 scratch 项目上测试，不要用真实客户项目，也不要提交它们。
- 提交和文档遵循 [开发流程第 5 节](docs/process/development-workflow.md#5-分支与提交) 及 [CLAUDE.md 硬性规则](CLAUDE.md#硬性规则违反任何一条都不能合并)。
- 安全问题请按 [SECURITY.md](SECURITY.md) 私下报告。

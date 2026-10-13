# 参与贡献

感谢你愿意改进 EplanMcp。完整流程见 [docs/process/development-workflow.md](docs/process/development-workflow.md)，这里是最短的入门路径。

## 第一次克隆后
```bash
git config core.hooksPath eng/hooks
```
启用本地提交检查：Conventional Commits 格式、禁止 AI 署名。

## 开发环境
- Windows 10/11 x64
- .NET SDK 10（版本由 `global.json` 固定）和 .NET Framework 4.8 目标包
- Visual Studio 2026 或 JetBrains Rider（可选）
- 运行 Large 测试需要：EPLAN Electric P8 / Pro Panel 2026，以及包含 **API 扩展**的授权

## 提交一个变更
1. 先开 Issue 描述问题，或者认领一个已有的 Issue。
2. 判断变更级别（L0 到 L3）。L2 及以上，先提交设计文档、工具规格或 ADR。
3. 从 `main` 拉一个短期分支：`feat/42-page-create`。
4. 按 [编码规范](docs/standards/coding-standards.md) 和 [测试策略](docs/standards/testing.md) 实现。
5. 提 PR，按模板填写，让 CI 全部通过。
6. squash 合并，PR 标题就是最终的提交信息。

## 规矩
- 写操作只在 scratch 项目上测试，不要用真实客户项目，也不要提交它们。
- 不要在提交、PR 或文档中加入 AI 署名。
- 安全问题请按 [SECURITY.md](SECURITY.md) 私下报告。

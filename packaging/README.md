# Claude Code 本地插件市场

`claude-plugin/` 是 M5 第 10 步的发布源，不含程序文件。第 11 步的安装包需原样复制整个目录（包括点目录）到 `%USERPROFILE%\EplanMcp\app\claude-plugin\`，不生成或替换 `.mcp.json`。

中英文指南源为 `docs/getting-started/zh-CN.md` 和 `docs/getting-started/en.md`；安装包需把它们复制到 `app\docs\`，并提供两种语言的入门指南快捷方式。技能内的 `guide.zh-CN.md`、`guide.en.md` 分别是对应文件的原样副本，修改指南时同步更新；测试检查副本与原文一致。技能目录中的 `zh-CN.md`、`en.md` 是语言切换链接的入口，分别指向对应的 `guide.*.md`，使原文的相对链接在插件内仍可使用。

发布新版本时，`eplan-mcp/.claude-plugin/plugin.json` 的版本必须与 Host 产品版本一致。Small 测试同时核对插件身份与现有 `client-config` 检测逻辑、固定路径以及技能工具名。

# Claude Code 本地插件市场

`claude-plugin/` 是 M5 第 10 步的发布源，不含程序文件。第 11 步的安装包需原样复制整个目录（包括点目录）到 `%USERPROFILE%\EplanMcp\app\claude-plugin\`，不生成或替换 `.mcp.json`。

中文指南源为 `docs/getting-started/zh-CN.md`；安装包需把它复制到 `app\docs\`，并提供入门指南快捷方式。技能内的 `guide.zh-CN.md` 是同一文件的副本，修改指南时同步更新；测试检查两份一致。

发布新版本时，`eplan-mcp/.claude-plugin/plugin.json` 的版本必须与 Host 产品版本一致。Small 测试同时核对插件身份与现有 `client-config` 检测逻辑、固定路径以及技能工具名。

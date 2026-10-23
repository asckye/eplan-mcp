# Claude Code 本地插件市场

`claude-plugin/` 是随 ZIP 分发的市场模板，不含程序文件；CI 将整个目录（包括点目录）复制到程序目录的 `claude-plugin\`。工程师不直接安装模板。

2026-10-22 的 ZIP 决策（[ADR-0016 修订](../docs/adr/0016-per-user-installer.md)）：Part A 只改流水线和文档，Part B 由 `client-config` 的插件生成模式复制模板到 `<数据根目录>\claude-plugin\`，将 `.mcp.json` 写为当前 Host 的实际绝对路径；Workbench 调用并展示本地市场安装命令。不再假设 `%USERPROFILE%\EplanMcp\app`。S17 记录了插件缓存副本，因此不使用 `${CLAUDE_PLUGIN_ROOT}/../..` 定位程序。升级或移动程序后重新生成市场；普通 Claude Code 配置也用实际绝对路径，与插件二选一。

中英文指南源为 `docs/getting-started/zh-CN.md` 和 `en.md`，随 ZIP 放在程序目录的 `docs\`，不创建开始菜单入口。技能内的 `guide.zh-CN.md`、`guide.en.md` 是原样副本，修改指南时同步更新；测试检查一致性。技能目录中的 `zh-CN.md`、`en.md` 分别指向对应的 `guide.*.md`，使相对链接可用。

发布新版本时，`eplan-mcp/.claude-plugin/plugin.json` 的版本必须与 Host 产品版本一致。Small 测试核对插件身份与 `client-config` 检测逻辑、技能工具名；模板路径和生成功能的测试随 Part B 更新，Part A 保留现有 `.mcp.json` 待替换。

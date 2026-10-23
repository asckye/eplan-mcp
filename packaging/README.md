# Claude Code 本地插件市场

`claude-plugin/` 是随 ZIP 分发的市场模板，不含程序文件；CI 将整个目录（包括点目录）复制到程序目录的 `claude-plugin\`。工程师不直接安装模板。

2026-10-22 的 ZIP 决策（[ADR-0016 修订](../docs/adr/0016-per-user-installer.md)）：`client-config claude-code --plugin --dry-run` 预览，去掉 `--dry-run` 后复制模板到 `<数据根目录>\claude-plugin\`，将 `.mcp.json` 的 `__EPLANMCP_EXE__` 替换为当前 Host 的实际绝对路径；Workbench 的“生成本地市场”调用同一命令并展示安装命令。不依赖程序目录名称或 `${CLAUDE_PLUGIN_ROOT}` 的缓存位置。升级或移动程序后重新生成市场；普通 Claude Code 配置也用实际绝对路径，与插件二选一，已有配置须先移除。

中英文指南源为 `docs/getting-started/zh-CN.md` 和 `en.md`，随 ZIP 放在程序目录的 `docs\`，不创建开始菜单入口。技能内的 `guide.zh-CN.md`、`guide.en.md` 是原样副本，修改指南时同步更新；测试检查一致性。技能目录中的 `zh-CN.md`、`en.md` 分别指向对应的 `guide.*.md`，使相对链接可用。

发布新版本时，`eplan-mcp/.claude-plugin/plugin.json` 的版本必须与 Host 产品版本一致。Small/Medium 测试核对插件身份、模板占位符、技能工具名、绝对路径生成、预览不写入、升级备份及插件与配置二选一。

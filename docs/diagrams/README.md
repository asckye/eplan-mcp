# 交互式图

本目录存放用 [archify](https://github.com/tt-a1i/archify) 生成的交互式 HTML 图。用浏览器打开 `.html` 文件即可查看：可以缩放、聚焦节点、追踪上下游，支持深色和浅色主题。

| 图 | 内容 | 文字版 |
|---|---|---|
| （待生成） | 系统架构：MCP 客户端、Host、Bridge、EPLAN | [架构文档第 3、5 节](../architecture/architecture.md) |
| （待生成） | 开发工作流程：从 Issue 到发布 | [开发流程第 2 节](../process/development-workflow.md#2-工作项生命周期) |
| （待生成） | 只读调用的时序 | [架构文档 6.2 节](../architecture/architecture.md#62-只读调用) |
| （待生成） | 写调用、审批与事务的时序 | [架构文档 6.3 节](../architecture/architecture.md#63-写调用审批与事务) |

## 约定
- 每张图一个子目录：`<名称>/<名称>.json`（archify 的源文件，可编辑、可评审）和 `<名称>/<名称>.html`（生成结果）。只提交这两个文件；archify 运行时产生的回执和浏览器检查证据放在 `.archify/`，不进仓库。
- **文字版是权威来源**：架构和流程以 Markdown 文档为准，图是它的可视化。文档改了，图也要跟着改（在同一个 PR 里）。
- 只提交通过 archify `finalize` 全部检查的图。
- 字体：生成前运行 `node eng/scripts/apply-archify-font.mjs`，让图优先使用 Maple Mono（见 [README 的开发工具一节](../../README.md#开发工具新的开发电脑上安装)）。图里只按名称引用字体，没有安装该字体的电脑会自动退回 archify 原来的字体。

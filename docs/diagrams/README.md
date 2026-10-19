# 交互式图

本目录存放用 [archify](https://github.com/tt-a1i/archify) 生成的交互式 HTML 图：可以缩放、聚焦节点、追踪上下游，支持深色和浅色主题，也能导出为图片。

**怎么看**：克隆或下载仓库后，用浏览器直接打开 `.html` 文件。在 GitHub 网页上点开只会显示 HTML 源码。

| 图 | 内容 | 文字版（权威来源） |
|---|---|---|
| [系统架构](system-architecture/system-architecture.html) | 工程师、MCP 客户端、Host、Bridge、EPLAN 之间的关系；策略与审批、审计；Bridge 运行在 EPLAN 进程内 | [架构文档第 3、5 节](../architecture/architecture.md#3-系统上下文) |
| [开发工作流程](development-workflow/development-workflow.html) | 从 Issue 到合并：变更分级、设计与 ADR、实现与评审、CI 和实机测试 | [开发流程第 2、3 节](../process/development-workflow.md#2-工作项生命周期) |
| [只读调用的时序](read-call-sequence/read-call-sequence.html) | 一次只读工具调用经过 Host、Bridge、EPLAN 主线程再返回 | [架构文档 6.2 节](../architecture/architecture.md#62-只读调用) |
| [写调用：审批与事务的时序](write-call-sequence/write-call-sequence.html) | dry-run、MCP 多轮请求审批、事务执行、审计 | [架构文档 6.3 节](../architecture/architecture.md#63-写调用审批与事务) |

## 约定
- 每张图一个子目录：`<名称>/<名称>.json`（archify 的源文件，可编辑、可评审）和 `<名称>/<名称>.html`（生成结果）。只提交这两个文件；archify 的投递回执（`*.delivery.json`）以及运行时产生的检查证据（`.archify/`）不进仓库。
- **文字版是权威来源**：架构和流程以 Markdown 文档为准，图是它的可视化。文档改了，图也要在同一个 PR 里跟着改。
- 只提交通过 archify `finalize` 全部四道检查（validate、deliver、check、browser-check）的图。
- 重新生成：在仓库根目录运行
  `node .claude/skills/archify/bin/archify.mjs finalize <类型> docs/diagrams/<名称>/<名称>.json docs/diagrams/<名称>/<名称>.html --quality showcase --out-dir .archify/<名称> --json`
  （类型：`architecture`、`workflow`、`sequence` 等；同一输出再次生成时，`--out-dir` 换一个新的子目录）。
- 字体：生成前运行 `node eng/scripts/apply-archify-font.mjs`，让图优先使用 Maple Mono（见 [README 的开发工具一节](../../README.md#开发工具新的开发电脑上安装)）。图里只按名称引用字体，没有安装该字体的电脑会自动退回 archify 原来的字体。

# 参考资料与落地对照

> 本项目借鉴的外部实践，以及每一条在本仓库中落实到了哪里。只引用核实过可以访问的原始出处（2026-10-12）。
> 原则：**只采纳能解决我们实际问题的部分**，不照搬大团队的全套流程。

## MCP 与 AI 工具设计

| 来源 | 采纳的要点 | 落地位置 |
|---|---|---|
| [MCP 规范 2026-07-28](https://modelcontextprotocol.io/specification/2026-07-28/changelog) | 无状态协议、`server/discover`、`tools/list` 顺序固定；多轮请求方式的 elicitation；Tasks 扩展；废弃 MCP 日志通知 | [架构](architecture/architecture.md) 2、6 节 |
| [MCP · Tools](https://modelcontextprotocol.io/specification/2026-07-28/server/tools) | 工具名字符集；`outputSchema` 与 `structuredContent`；业务错误用 `isError` 结果返回；有状态工具使用会过期的高熵句柄；服务器必须校验输入、控制访问、清理输出 | [工具设计规范](standards/mcp-tool-design.md) 5–7 节 |
| [MCP schema.ts（ToolAnnotations）](https://raw.githubusercontent.com/modelcontextprotocol/modelcontextprotocol/main/schema/2026-07-28/schema.ts) | `destructiveHint` 和 `openWorldHint` 默认为 true，必须显式设置；注解对客户端来说只是不可信的提示 | 工具设计规范第 8 节；[ADR-0006](adr/0006-tool-surface-design.md) |
| [MCP · Elicitation](https://modelcontextprotocol.io/specification/2026-07-28/client/elicitation) | 表单模式只用于确认，不收集敏感信息；必须处理 decline 和 cancel | [ADR-0005](adr/0005-safety-model.md)；[ADR-0011](adr/0011-approval-out-of-band.md) 之后审批不用 elicitation |
| [MCP · Progress](https://modelcontextprotocol.io/specification/2026-07-28/basic/utilities/progress) / [Cancellation](https://modelcontextprotocol.io/specification/2026-07-28/basic/utilities/cancellation) / [Tasks 扩展](https://modelcontextprotocol.io/extensions/tasks/overview) | 进度单调递增并限制频率；取消可能与完成产生竞态；耗时操作以任务方式运行 | 工具设计规范第 9 节 |
| [MCP 安全最佳实践](https://modelcontextprotocol.io/docs/2026-07-28/tutorials/security/security_best_practices) / [本地服务器安全](https://modelcontextprotocol.io/docs/2026-07-28/tutorials/security/local-server-security) | stdio 不是沙箱；工具投毒和定义悄悄变更；工具结果中的提示注入；最小权限；在代码中强制执行；提供来源证明 | [安全标准](standards/security.md) |
| [MCP C# SDK](https://github.com/modelcontextprotocol/csharp-sdk) · [NuGet](https://www.nuget.org/packages/ModelContextProtocol) | 2.x 支持 2026-07-28 规范并兼容旧客户端；用 `[McpServerTool]` 声明工具及其注解属性；`McpException` 映射为 `isError`；日志输出到 stderr | [ADR-0004](adr/0004-runtimes-and-target-frameworks.md)；架构第 5.2 节 |
| [Anthropic · Writing effective tools for agents](https://www.anthropic.com/engineering/writing-tools-for-agents) | 少而精、面向工作流的工具；按服务和资源命名空间；人能读懂的标识；`response_format`；分页与截断；错误可操作；评测驱动、保留集 | ADR-0006；工具设计规范；[测试策略](standards/testing.md) 第 5 节 |
| [Anthropic · Advanced tool use](https://www.anthropic.com/engineering/advanced-tool-use) | 工具搜索与延迟加载；参数示例能明显提高准确率 | 工具设计规范 4、10 节 |
| [Anthropic · Code execution with MCP](https://www.anthropic.com/engineering/code-execution-with-mcp) | 渐进式披露；在服务器端过滤数据，不把大数据集交给模型 | 工具设计规范 5、7 节 |
| [Claude Code · MCP](https://code.claude.com/docs/en/mcp) | 工具输出默认上限 25k token（超过 10k 告警）；超过 2 分钟的调用转到后台 | 架构第 2 节；工具设计规范 7.2 节 |
| [OpenTelemetry MCP 语义约定](https://github.com/open-telemetry/semantic-conventions-genai/blob/main/docs/gen-ai/mcp.md) | span 名称与属性；参数和结果默认不进遥测（目前处于 Development 状态） | 架构第 8.7 节 |

## Google

| 来源 | 采纳的要点 | 落地位置 |
|---|---|---|
| [Small CLs](https://google.github.io/eng-practices/review/developer/small-cls.html) | 一次变更只做一件事并附带测试；重构单独提交 | [开发流程](process/development-workflow.md) 1、6 节 |
| [The Standard of Code Review](https://google.github.io/eng-practices/review/reviewer/standard.html) | 让代码库整体变好就可以通过；`Nit:` 标注非阻塞意见 | 开发流程第 6 节 |
| [Design Docs at Google](https://www.industrialempathy.com/posts/design-docs-at-google/) | 背景、目标与非目标、权衡、备选方案、横切关注点 | [设计文档模板](templates/design-doc.md) |
| [AIP-130 系列](https://google.aip.dev/130) | 标准方法（Get/List/Create/Update/Delete）优先，自定义方法其次 | 工具设计规范第 3 节 |
| [AIP-193 Errors](https://google.aip.dev/193) | 机器可读的 `reason` 加可操作的 `message` 和 `metadata`；先检查权限再检查是否存在 | 工具设计规范第 6 节 |
| [SWE at Google · Testing](https://abseil.io/resources/swe-book/html/ch11.html) | 按资源划分测试规模（small/medium/large）；Beyoncé Rule；不稳定的测试会毁掉测试的价值 | 测试策略 1、4 节 |
| [Hyrum's Law](https://www.hyrumslaw.com/) | 所有可观察的行为都会被依赖 | 开发流程 1、8 节；契约快照测试 |

## Microsoft

| 来源 | 采纳的要点 | 落地位置 |
|---|---|---|
| [Code-With Engineering Playbook](https://microsoft.github.io/code-with-engineering-playbook/) | Definition of Ready / Done；ADR 以 Proposed 状态通过 PR 提交；C# 代码评审要点 | 开发流程第 4 节；[ADR 索引](adr/README.md) |
| [Security Development Lifecycle](https://www.microsoft.com/en-us/securityengineering/sdl/practices) | 威胁建模、供应链安全、安全测试 | 安全标准 |
| [STRIDE](https://learn.microsoft.com/en-us/azure/security/develop/threat-modeling-tool-threats) | 冒充、篡改、抵赖、信息泄露、拒绝服务、权限提升 | 安全标准第 3 节 |
| [Framework Design Guidelines](https://learn.microsoft.com/en-us/dotnet/standard/design-guidelines/) | 命名与 API 设计的通用规则 | [编码规范](standards/coding-standards.md) |
| [Central Package Management](https://learn.microsoft.com/en-us/nuget/consume-packages/central-package-management) | 依赖版本集中声明 | `Directory.Packages.props` |

## Amazon

| 来源 | 采纳的要点 | 落地位置 |
|---|---|---|
| [2015 年致股东信](https://www.sec.gov/Archives/edgar/data/1018724/000119312516530910/d168744dex991.htm) | 单向门决策要慎重，双向门决策要快 | 开发流程第 3 节；ADR 模板中的"门的类型" |
| [Working Backwards（PR/FAQ）](https://www.workingbackwards.com/resources/working-backwards-pr-faq) | 从用户的结果倒推；预先写出失败的主要原因 | 架构第 1 节、第 10 节风险表 |

## 通用工程实践

| 来源 | 采纳的要点 | 落地位置 |
|---|---|---|
| [ADR](https://adr.github.io/) · [Nygard 原文](https://www.cognitect.com/blog/2011/11/15/documenting-architecture-decisions) · [MADR](https://adr.github.io/madr/) | 一份 ADR 一个决策；编号不复用；被推翻时标记 Superseded | [ADR 模板](adr/template.md) |
| [C4 model](https://c4model.com/) · [arc42](https://arc42.org/overview) | 分层的架构图；架构文档的章节结构 | 架构文档 |
| [Conventional Commits](https://www.conventionalcommits.org/en/v1.0.0/) · [SemVer](https://semver.org/) · [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) | 提交格式决定版本号的升级；CHANGELOG 的分组 | 开发流程 5、8 节；[CHANGELOG](../CHANGELOG.md) |
| [Trunk-Based Development](https://trunkbaseddevelopment.com/) | 短期分支、功能开关、主干持续集成 | 开发流程第 1 节 |
| [DORA metrics](https://dora.dev/guides/dora-metrics/) | 变更前置时间、变更失败率 | 开发流程第 11 节 |
| [OpenSSF Scorecard](https://scorecard.dev/) | 分支保护、固定依赖版本、最小 token 权限、签名发布 | 安全标准第 5 节 |
| [SLSA v1.2](https://slsa.dev/spec/v1.2/build-track-basics) · [GitHub artifact attestations](https://docs.github.com/en/actions/concepts/security/artifact-attestations) · [Sigstore](https://docs.sigstore.dev/about/overview/) | 构建来源证明，达到 SLSA Build L2 | 开发流程第 8 节；安全标准 SEC-16 |
| [CycloneDX](https://cyclonedx.org/specification/overview/) | SBOM 格式 | 发布清单 |

## 行业产品（需求信号）

| 来源 | 采纳的要点 | 落地位置 |
|---|---|---|
| [电气CAD吧 EPLAN 插件商店](https://www.cad-bbs.cn/pluginstore/) | 国内工程师付费购买的高频功能：批量建页、批量改页描述、属性查找替换（先预览再执行）、线号管理、图例编号、批量生成部件、EDZ 浏览；以及用户常遇到的安装问题。插件闭源且声明"不得商用"，**只参考需求，不参考实现** | [API 覆盖矩阵第 4 节](eplan/api-coverage.md#4-国内工程师的高频任务需求信号)；[架构 7.1 安装验收清单](architecture/architecture.md#71-安装验收清单m5-发布前逐项通过)；[测试策略第 5 节](standards/testing.md#5-agent-评测) |

## EPLAN 官方

| 来源 | 内容 |
|---|---|
| [EPLAN API 帮助（2026）](https://www.eplan.help/en-us/Infoportal/Content/api/2026/) | 插件、Action、Remote Client、离线程序、锁定、签名与授权。具体页面和核实结论见 [api-notes](eplan/api-notes.md) 第 B 部分 |

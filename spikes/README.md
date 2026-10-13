# Spikes（技术验证）

每个 spike 只回答**一个**问题，代码是一次性的，**不进入产品**。但它要保留下来，保证结论可以复现。
结论写进 [api-notes](../docs/eplan/api-notes.md)，影响决策的写进 ADR。

| 编号 | 问题 | 状态 | 结论 |
|---|---|---|---|
| [S0](S0-licence/README.md) | 未签名的编译插件能否在当前授权下加载？能否读写超过 5 页的项目？ | **完成** 2026-10-13 | 能加载；321 页项目读写均成功，没有页数限制；附带发现：未提交的 SafetyPoint 没有回滚（交给 S2） |
| [S1](S1-dispatcher/README.md) | 插件内主线程调度（`EplanMainThreadDispatcher`）是否就绪、带超时的调度是否可靠；**并在调度器中复核 S2 的回滚与撤销结论** | **完成** 2026-10-13 | 可行，延迟约 0.3 ms，超时可以干净地放弃。4 条硬规则：委托内部捕获全部异常（否则 EPLAN 崩溃，实测发生过一次）；LockingStep 作为第一步；UndoStep 在 SafetyPoint 内；主窗口被禁用时拒绝执行（否则会在模态对话框中重入） |
| [S2](S2-transactions/README.md) | `SafetyPoint` / `UndoStep` / `LockingStep` 的实际回滚与撤销语义 | **完成** 2026-10-13（action 中） | UndoStep 必须在 SafetyPoint **内**创建，否则回滚静默失效；这样写时 Ctrl+Z 能撤销 AI 的操作，并保留原有历史。插件程序集在 EPLAN 退出前无法卸载 |
| S3 | 编译时引用、运行时使用 EPLAN 自带的 Newtonsoft.Json 是否可行 | **完成** 2026-10-13 | S0、S1、S2 三个插件都引用 EPLAN Bin 中的 Newtonsoft.Json（`Private=false`，不复制），全部运行正常 |
| [S4](S4-elicitation/README.md) | 目标客户端对 MCP 2026-07-28 多轮请求 elicitation 的支持 | **协议层完成** 2026-10-13；界面待人工确认 | Claude Code 2.1.295 原生使用 2026-07-28，声明 `elicitation: {form, url}`；SDK 的 `InputRequiredException` 一条路径覆盖两种协议，`requestState` 都能带回。表单的实际显示和三种回答在交互会话中的往返还要人工点一次 |
| S5 | MSIX 打包的客户端（Claude 桌面版）启动 Host 时的文件系统重定向 | 新增 | 见 S0 的环境发现 |

## 约定
- 目录：`spikes/S<n>-<主题>/`，包含代码、驱动脚本和 `README.md`（问题、方法、结果、结论、局限）。
- 构建规则继承仓库根目录，只放宽"警告即错误"（见 [Directory.Build.props](Directory.Build.props)）。
- 运行产物写到仓库下的 `.local/`（已被 git 忽略），**不要写到 `%LOCALAPPDATA%`**，原因见 S0。
- 对 EPLAN 的持久修改（插件注册等）必须在驱动脚本的 `finally` 中撤销。

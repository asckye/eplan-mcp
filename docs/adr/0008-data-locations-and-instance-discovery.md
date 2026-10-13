# ADR-0008：数据目录移出 %LOCALAPPDATA%，实例发现改用命名管道枚举

| 项 | 内容 |
|---|---|
| 状态 | Proposed |
| 日期 | 2026-10-13 |
| 决策人 | 项目负责人（待确认） |
| 门的类型 | 双向门（发布 1.0 之前） |
| 影响 | 接受后，部分取代 [ADR-0003](0003-ipc-named-pipe-json-rpc.md) 中"实例发现文件 + 一次性密钥"的握手设计，以及架构文档第 6.1、7 节的数据目录 |

## 背景与问题
架构原定所有数据都放在 `%LOCALAPPDATA%\EplanMcp\`：配置、审计、日志、scratch 项目，以及 Bridge 写给 Host 的实例发现文件。

spike S0 发现（[api-notes](../eplan/api-notes.md) A6）：Claude 桌面应用是 MSIX 包。由它启动的进程（将来的 Host 就是这种情况）写入 `%LOCALAPPDATA%` 时，会被重定向到 `%LOCALAPPDATA%\Packages\Claude_…\LocalCache\Local\`。结果是：
- Host 创建的 scratch 项目，EPLAN **打不开**（S0 实测）；
- Host 写的审计日志和配置，工程师在 `%LOCALAPPDATA%\EplanMcp\` 下**找不到**；
- Bridge 写在 `%LOCALAPPDATA%` 的实例发现文件，Host 能否读到尚不确定；
- 同一个 Host，从 Claude Code 命令行启动（不受影响）和从 Claude 桌面版启动，看到的数据目录不同。

命名管道不受这种文件系统重定向的影响。

## 备选方案
1. 保持 `%LOCALAPPDATA%`，在文档中说明这个问题。
2. 运行时检测是否处于 MSIX 包环境中，再决定写到哪里。
3. **数据根目录改为 `%USERPROFILE%\EplanMcp\`，实例发现改为枚举命名管道，不再依赖文件。**

## 决策
选择**方案 3**：
- **数据根目录**：`%USERPROFILE%\EplanMcp\`，包含 `config.json`、`audit\`、`logs\`、`scratch\`。可以通过环境变量 `EPLANMCP_HOME` 或配置文件改到别处。
  - 不放在"文档"目录下：很多机器把"文档"重定向到 OneDrive，而 EPLAN 项目（`.edb` 中有大量小文件）不适合放在同步目录里。
  - 无论 Host 从哪里启动，路径都相同，EPLAN 和工程师都能看到。
- **实例发现**：Bridge 创建名为 `EplanMcp.Bridge.<pid>.<随机值>` 的管道，Host 通过列举 `\\.\pipe\` 下的管道来发现实例。**不再使用实例发现文件**。
- **握手**：去掉"发现文件中的一次性密钥"，安全性依靠管道 ACL（只允许当前用户）和 Host 端的 `PipeOptions.CurrentUserOnly`。这个密钥原本只用来防范同一用户下的其他进程，而这类威胁本来就不在防护范围内（[安全标准](../standards/security.md)第 2 节），所以去掉它不降低实际的安全性。
- `doctor` 报告当前进程是否带有 MSIX 包身份，以及实际使用的数据目录。

## 后果
- **好的**：Host 不管从 Claude Code 还是 Claude 桌面版启动，行为都一致；scratch 项目对 EPLAN 可见；审计记录工程师找得到；少了一个跨进程共享文件的约定。
- **代价**：在用户目录根下多一个文件夹；管道枚举需要过滤名称前缀，并忽略已失效的管道。
- **后续行动**：
  - 接受后：更新架构文档第 6.1、7 节；在 ADR-0003 的状态栏标注"部分被 ADR-0008 取代"；在安全标准中移除 SEC-04，并重新评估 SEC-03；
  - M1 中验证：包内进程能否枚举并连接包外进程创建的管道（预期可以，因为管道不在文件系统重定向的范围内）。

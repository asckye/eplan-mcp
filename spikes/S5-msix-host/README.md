# Spike S5：由 MSIX 打包的客户端启动的 Host

| 项 | 内容 |
|---|---|
| 问题 | Claude 桌面版（MSIX 包 `Claude_pzs8sxrjxfjjc`）启动的 Host，文件读写会不会被重定向？会不会影响 EplanMcp 的数据目录、和 EPLAN 的命名管道连接？`eplanmcp doctor` 能不能识别这种环境？ |
| 日期 | 2026-10-19 |
| 环境 | Windows 11；Claude 桌面版自带的 Claude Code 2.1.299；EPLAN 2026.0.3.25421（Electric P8，在包外运行） |
| 结论 | **Host 可以正常工作**：数据目录 `%USERPROFILE%\EplanMcp` 和 `%TEMP%` 不被重定向，命名管道正常。但由客户端启动的进程**没有包身份，写入 `%LOCALAPPDATA%` 和 `%APPDATA%` 却仍被重定向**。`doctor` 只看包身份，在这种环境中误报"没有 MSIX 包身份" |

## 方法
- [probe.ps1](probe.ps1)：
  - 用 `GetCurrentPackageFullName` 查当前进程有没有包身份；
  - 在 `%LOCALAPPDATA%`、`%APPDATA%`、`%TEMP%` 和 EplanMcp 数据根目录下各写一个探测文件，再到每个包的 `LocalCache\Local`、`LocalCache\Roaming` 下找同名文件，找到就说明写入被重定向；探测文件随后删除；
  - 列出 `%LOCALAPPDATA%` 下可见的目录，看包内进程能否读到真实位置的内容。
- 在 Claude 桌面版的 Claude Code 会话中运行（会话的 shell、它启动的 `eplanmcp`、评测时 `claude -p` 再启动的 `eplanmcp`，都是这个客户端的子进程）：
  ```powershell
  spikes\S5-msix-host\probe.ps1
  ```
- 同一会话中再做三项核对：
  - `eplanmcp doctor` 的报告；
  - 用会话中启动的 `eplanmcp` 调用全部只读工具（Agent 评测和 Large 测试）；
  - 用会话中运行的 `eng\scripts\eval-fixture.ps1` 把夹具写到数据根目录，再由包外的 EPLAN 打开。

## 结果
| 项 | 结果 |
|---|---|
| 包身份 | 无（`APPMODEL_ERROR_NO_PACKAGE`），`doctor` 和探测脚本一致 |
| 写入 `%LOCALAPPDATA%` | 重定向到 `Packages\Claude_pzs8sxrjxfjjc\LocalCache\Local` |
| 写入 `%APPDATA%` | 重定向到 `Packages\Claude_pzs8sxrjxfjjc\LocalCache\Roaming` |
| 写入 `%TEMP%`（`%LOCALAPPDATA%\Temp`） | 不重定向 |
| 写入数据根目录 `%USERPROFILE%\EplanMcp` | 不重定向：会话中复制的夹具，包外的 EPLAN 正常打开 |
| 读取 `%LOCALAPPDATA%` | 能看到其他程序在真实位置的目录（共 48 个），读取会落到真实位置 |
| 命名管道 | 会话中启动的 `eplanmcp` 找到包外 EPLAN 的插件管道，握手成功；13 个工具全部可用 |

包外的对照来自以前的观察，没有在本 spike 中重做：
- S0：包外的 EPLAN 看不到被重定向的文件；
- 2026-10-19 登录 Claude Code 时：在自己的终端里看不到 `%APPDATA%\Claude\claude-code`，只能在包的 `LocalCache\Roaming` 下找到（api-notes A6）。

## 结论
1. ADR-0008 的决定成立：数据目录放在 `%USERPROFILE%\EplanMcp`，不放在 `%LOCALAPPDATA%`。由 Claude 桌面版启动时，scratch 项目、日志和审计文件都在用户和 EPLAN 能看到的地方。
2. **不能用包身份判断是否被重定向**：客户端启动的进程没有包身份，重定向却照样发生。`doctor` 要直接探测重定向：写一个临时文件，看它是否出现在某个包的 `LocalCache` 下。这在后续的 Host PR 中修复。
3. `%TEMP%` 不被重定向，所以 Host 和 EPLAN 之间需要交换的临时文件放在 `%TEMP%` 下也可以。但仍以数据根目录为准，`%TEMP%` 可能被清理。
4. 包内进程能读到真实位置的 `%LOCALAPPDATA%`，所以 A6 中"Bridge 写、Host 读"的方向可以读到。会话期间由包外进程新写入的文件，没有单独验证。

## 局限
- 只验证了 Claude 桌面版自带的 Claude Code 这一种客户端。Claude 桌面版的聊天界面直接启动 MCP 服务器时，用的是不是同样的进程环境，没有验证：需要改桌面版的 MCP 配置，这属于用户的应用设置，留给项目负责人决定。
- 重定向的具体机制没有深究（进程没有包身份却受文件系统虚拟化约束）。结论只基于观察到的行为。

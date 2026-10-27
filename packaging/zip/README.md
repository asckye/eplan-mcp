# EplanMcp 预览版

EplanMcp 是一个 MCP 服务器，让 AI 代理通过 EPLAN .NET API 安全地读取和修改 EPLAN Electric P8 / Pro Panel 项目，包括查询项目、生成页面、放置设备、更新报表、导出与检查。工程师始终掌握控制权，真实项目写入逐次审批，操作可审计；当前为供测试使用的预览版。

## 系统要求 / 支持矩阵

| 项目 | 要求与验证状态 |
|---|---|
| 已验证 EPLAN | 2026，实测构建 **2026.0.3.25421**；Electric P8、Pro Panel |
| 尚未验证 EPLAN | 2024、2025、2027，留待 M7 实机验证，不作为已验证支持版本 |
| 暂不支持 EPLAN | 2022、2023 |
| EPLAN 授权 | 必须包含 **API Extension（API 扩展）**，并允许加载未签名 API 插件；0.x 发布未签名插件（[ADR-0007](docs/adr/0007-licensing-and-distribution.md)） |
| Windows | Windows 10/11 x64；本机安装与连接无需管理员权限 |
| 本机客户端 | Claude Code 和其他支持 stdio 的 MCP 客户端（包括 Claude 桌面版）；模型服务由客户端自行提供 |

版本边界见 [ADR-0009](docs/adr/0009-supported-eplan-versions.md)、[版本兼容性](docs/eplan/eplan-versions.md)，实测环境见 [API 验证记录](docs/eplan/api-notes.md)。

EPLAN 在虚拟机、客户端在宿主机时，客户端须支持 Streamable HTTP 和自定义请求头，Claude 桌面版不适用。仅在宿主机与虚拟机间的私有网络使用；HTTP 不加密，密钥与项目数据明文传输，宿主机配置及备份含明文密钥。一个服务固定连接一个 EPLAN 实例，配置网络权限需管理员授权；仅 Claude Code 连接已实测，其他客户端、跨机器连接与 UAC 仍待实机验收。步骤及边界见[虚拟机部署](docs/getting-started/zh-CN.md#10-虚拟机部署)。

## 快速开始

1. 下载同一版本的 ZIP 和 `SHA256SUMS.txt`，用 PowerShell 的 `Get-FileHash -Algorithm SHA256` 核对 ZIP 对应条目，不一致就停止。
2. 校验后在 ZIP“属性”中勾选“解除锁定”（如有），再将整个目录解压到短且固定的位置，例如 `D:\EplanMcp`；保留全部文件，不从压缩包预览中启动。
3. 关闭 EPLAN，打开程序目录中的 **`workbench\EplanMcp.Workbench.exe`**。
4. 在 Workbench 勾选所需变体并“注册 EPLAN 插件”；Electric P8、Pro Panel 各注册一次，确认成功后再启动 EPLAN、打开项目并自检。
5. 在“MCP 与客户端”页配置客户端；Claude Code 可生成本地插件市场，插件与普通 MCP 配置二选一，按页面提示完成后重启客户端。

详细操作、SHA-256 校验与排错见[中文入门指南](docs/getting-started/zh-CN.md) / [English guide](docs/getting-started/en.md)。

## 数据、升级与卸载

默认数据目录为 `%USERPROFILE%\EplanMcp`，与程序目录分开，保存配置、日志、审计、scratch、导出与备份。

升级：等待写操作结束，关闭 EPLAN、客户端和 Workbench，校验并解除新 ZIP 锁定后整体替换同一路径的程序目录，再生成本地市场或更新客户端配置；数据保留，路径改变须重新注册，不支持降级。

卸载：移除客户端配置或 Claude Code 市场及用过的虚拟机连接，在 Workbench 关闭登录自启动、撤销注册，确认成功后退出并删除程序目录；数据目录保留，详见[升级与卸载](docs/getting-started/zh-CN.md#9-升级与卸载)。

## 安全与声明

未经审批仅允许 scratch 内的普通项目写入，真实项目默认逐次由工程师审批；删除、重编号等 Destructive 操作即使在 scratch 内也须审批并先备份，系统部件库写入同样须审批与备份。调用记入审计哈希链，可用 `eplanmcp audit verify` 校验；结果未知时暂停相关写入，先由工程师核实，不要直接重试。详见[第一次修改与审批](docs/getting-started/zh-CN.md#7-第一次修改与审批)。

EplanMcp 是独立的开源项目，与 EPLAN GmbH & Co. KG 无关联，也未经其认可或赞助。EPLAN、EPLAN Electric P8 和 EPLAN Pro Panel 是 EPLAN GmbH & Co. KG 的商标或注册商标。

## English

EplanMcp is a preview MCP server for safely reading and modifying EPLAN Electric P8 / Pro Panel projects. Verified: EPLAN 2026, build **2026.0.3.25421**, Electric P8 and Pro Panel. Not yet verified: 2024, 2025 and 2027 (M7). Unsupported: 2022 and 2023. An EPLAN licence with **API Extension** and permission to load unsigned API add-ins is required; 0.x add-ins are unsigned. Local use requires Windows 10/11 x64, no administrator rights, and Claude Code or another stdio MCP client.

Verify the ZIP against `SHA256SUMS.txt`, unblock it, extract the entire folder to a short fixed path, open `workbench\EplanMcp.Workbench.exe`, register each EPLAN variant and configure your client. Data stays in `%USERPROFILE%\EplanMcp`. Close EPLAN, Workbench and clients before replacing the program folder to upgrade; unregister before deleting it to uninstall. Data is retained.

VM connections require Streamable HTTP with custom headers and a private host–VM network; HTTP is unencrypted, Claude Desktop is unsupported, and firewall configuration requires administrator approval. Only Claude Code connectivity has been tested; other clients, cross-machine connections and UAC still await verification. Ordinary scratch writes can proceed without approval; real-project, destructive and system parts database writes require approval. Calls are audited. See the [English getting-started guide](docs/getting-started/en.md).

EplanMcp is an independent open-source project, not affiliated with, endorsed or sponsored by EPLAN GmbH & Co. KG. EPLAN, EPLAN Electric P8 and EPLAN Pro Panel are trademarks or registered trademarks of EPLAN GmbH & Co. KG.

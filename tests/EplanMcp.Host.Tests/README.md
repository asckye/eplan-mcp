# Host 测试

本程序集使用 xUnit v3，测试层含义见[手册 12.1](../../docs/standards/engineering-handbook.html#testing-layers)。从仓库根目录运行 Small + Medium：

```powershell
dotnet test --project tests/EplanMcp.Host.Tests -- --filter-not-trait "Size=Large"
```

## 测试入口与替身

- `Tools/` 的 Small 测试覆盖参数、结果整形、错误映射、分页、工具注册与契约。`Tools/FakeBridgeClient.cs` 实现 `IBridgeClient`，通过 `Handlers` 返回固定数据或注入故障，`Failure` / `WriteFailure` 保留 `BridgeError`，并记录参数、超时、进度和写请求；`Outcomes` 模拟中断后的结果取回。
- `Safety/` 的 `PolicyEngineTests`、`ScratchPathsTests`、`WritePipelineTests`、`DestructivePipelineTests`、`WriteCancellationTests`、`DanglingWritesTests`、`WorkbenchChannelTests` 覆盖审批、可写范围、路径、取消、失联及迟到结果。`ManualTime` 提供手动推进的 `TimeProvider`。
- `Audit/AuditLogTests.cs`、`Exports/`、`Importing/` 覆盖审计写入与校验、导出目录、备份和输入文件。`Addin/` 覆盖注册失败、已有注册和其他用户的插件。
- `Bridge/` 的 Medium 测试通过真实命名管道连接真实 `BridgeServer` 与假适配器；覆盖帧、身份、断开、超时、忙碌和写入结果。`EndToEnd/StdioEndToEndTests.cs` 让 MCP 客户端启动真实 `eplanmcp serve`，经测试专用 `EPLANMCP_PIPE_PREFIX` 发现测试进程中的 Bridge.Core；无需 EPLAN。
- `Tools/ActionRunValidationTests.cs`、`Tools/ActionTryTests.cs` 及端到端测试覆盖 action 名称、参数、路径和注入输入。真实事务、EPLAN 主线程与撤销证据见 [Large 测试](../EplanMcp.Large.Tests/README.md)。

## 契约快照

`Tools/ToolContractSnapshotTests`（Medium）以与 `serve` 相同的注册结果生成服务器 instructions 和按名称排序的工具定义，保存为 [server-contract.verified.json](../Contracts.Snapshots/server-contract.verified.json)。快照包含名称、标题、描述、输入/输出 schema 和注解。

`CliJsonSnapshotTests`（Medium）固定 `doctor`、`audit verify`、`client-config`、`install-addin` / `uninstall-addin`、`vm-test` 的 JSON 形状，文件分别为 `doctor-json`、`audit-verify-json`、`client-config-json`、`addin-json`、`vm-test-json`。这些命令的 `schemaVersion` 和字段见 [CLI 参考](../../docs/reference/cli.md#json-输出契约)。

`Tools/ToolBuildIsolation` 关闭集合内并行：多个 service provider 同时构建工具时，SDK 的 schema 生成曾把服务参数混入输入或抛出只读 `JsonSerializerOptions` 异常。`serve` 本身只在一个线程构建一次工具。

快照操作命令：

```powershell
dotnet test --project tests/EplanMcp.Host.Tests -- --filter-class "EplanMcp.Host.Tests.Tools.ToolContractSnapshotTests"
git diff --no-index tests/Contracts.Snapshots/server-contract.verified.json tests/Contracts.Snapshots/server-contract.received.json
Move-Item -LiteralPath tests/Contracts.Snapshots/server-contract.received.json -Destination tests/Contracts.Snapshots/server-contract.verified.json -Force
dotnet test --project tests/EplanMcp.Host.Tests -- --filter-class "EplanMcp.Host.Tests.Tools.ToolContractSnapshotTests"
```

Verify 在结果变化时写 `*.received.json`，比较方法也适用于 CLI 快照；输出采用带 BOM 的 UTF-8 和 LF。快照评审要求见[手册 TST-08 / MCP-37](../../docs/standards/engineering-handbook.html#testing-rules)。

## 模型文本和文档检查

`Tools/ModelTextTests` 扫描服务器说明、工具定义、用法文件，以及 Host、Bridge.Core、Contracts 程序集的字符串字面量，检查其中的 CJK 文本；允许项和原因在类首的 `_allowedInUsage`、`_allowedInLiterals` 中，已不再使用的允许项也会使测试失败。

`Tools/ToolUsageExampleTests` 用注册后的输入 schema 校验描述 `Example:` 与用法 Examples 代码块，并核对服务器说明、工具定义和用法中的 `eplan_*` 名称；计划工具例外在 `_planned`，注册后或不再提及时会报过期。`ToolContractTests` 检查用法文件覆盖；`ToolDocumentationTests` 检查工具文档；`SecurityDocumentationTests` 检查安全入口文档。

当前依赖是 xUnit v3 和 Verify；精确版本见 [Directory.Packages.props](../../Directory.Packages.props)。

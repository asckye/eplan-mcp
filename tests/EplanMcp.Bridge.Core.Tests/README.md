# Bridge.Core 测试

本程序集使用 xUnit v3，部分结果使用 Verify；精确版本见 [Directory.Packages.props](../../Directory.Packages.props)。它引用 Bridge.Core，并将 [csproj](EplanMcp.Bridge.Core.Tests.csproj) 列出的适配器源码与测试替身一同编译，无需加载 EPLAN。

```powershell
dotnet test --project tests/EplanMcp.Bridge.Core.Tests -- --filter-not-trait "Size=Large"
```

- `Operations/` 覆盖请求校验、对象映射、过滤、坐标与栅格计算、规划和执行。`TransactionRunnerTests`、`WriteInvokeTests`、`WriteExceptionDiagnosticsTests`、`CancellationTests` 覆盖事务失败、异常映射及取消；`ActionRunOperationTests` / `ActionTryOperationTests` 覆盖 action 的参数和调用结果。
- `Adapters/` 覆盖可脱离 EPLAN 运行的原生前置条件、对象生命周期、布局、文字、移动与读回逻辑；`FakeApiTryDispatcher` 提供 API 试调用替身。
- `Server/BridgeServerTests`（Medium）在真实命名管道上运行 `BridgeServer`，使用内联调度器、假目录和假事务；`BridgeConnectionTests`、`FramingTests` 覆盖连接和消息边界。真实 EPLAN 主线程、授权和撤销行为由 [Large 测试](../EplanMcp.Large.Tests/README.md) 提供证据。

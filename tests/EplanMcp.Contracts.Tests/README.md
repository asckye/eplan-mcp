# Contracts 测试

本程序集使用 xUnit v3；[Directory.Packages.props](../../Directory.Packages.props) 提供精确版本。`SerializerCompatibilityTests` 覆盖 Host 的 System.Text.Json 与 Bridge 的 Newtonsoft.Json 之间的请求/响应兼容；其余 Small 测试覆盖属性、符号、绘图、计划步骤、文件路径和设置 DTO，及目录与错误原因元数据。

```powershell
dotnet test --project tests/EplanMcp.Contracts.Tests -- --filter-not-trait "Size=Large"
```

工具注册后的 JSON Schema 与服务器说明快照位于 [Host 测试](../EplanMcp.Host.Tests/README.md#契约快照)。

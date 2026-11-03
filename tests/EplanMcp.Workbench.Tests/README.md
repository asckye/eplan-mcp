# Workbench 测试

本程序集使用 xUnit v3，目标是 `net10.0-windows` / WPF；版本来源为 [Directory.Packages.props](../../Directory.Packages.props)。

```powershell
dotnet test --project tests/EplanMcp.Workbench.Tests -- --filter-not-trait "Size=Large"
```

- Small 测试覆盖队列排序、防误触、倒计时、项目文字清洗、设置与日志。`ManualTime` 链接自 Host 测试，提供手动推进的时钟。`SourceHygieneTests`、主题 token 和字体测试使用嵌入的 Workbench 源码；命令视图读取 Host 的 `*-json.verified.json` 快照。
- Medium 测试在 WPF 窗口中核对页面、焦点与 UIA 树；`ThemeContrastTests` 检查三套调色板。`ApprovalPipeTests` 将真实 Host `WorkbenchChannel` 连到真实审批管道服务端；`ApprovalQueueTests`、`ResponseGuardTests` 覆盖队列、断开、迟到回答和防误触。
- `ApprovalsPageAutomationTests` 等通过 `UIElementAutomationPeer.CreatePeerForElement` 遍历 UIA 树；`ThemeContrastTests` 是从 TIA `ThemeSurfaceTests` 移植后的当前类名。人工键盘、讲述人、AI4W 和显示缩放检查入口及验证状态见 [M5 证据说明](../../docs/design/2026-10-20-m5-operability.md#workbench-evidence)；界面当前结构见 [M5 实现记录](../../docs/design/2026-10-20-m5-operability.md#workbench-implementation)。

独立的 [Workbench.Protocol 测试项目](../EplanMcp.Workbench.Protocol.Tests/EplanMcp.Workbench.Protocol.Tests.csproj) 以 Small / Medium 测试覆盖 JSON、帧和管道名，可用相同的 `--filter-not-trait "Size=Large"` 过滤运行。

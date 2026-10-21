# ADR-0015：Workbench 用 WPF（.NET 10）和随 .NET 发布的 Fluent 主题实现 Fluent 2 界面

| 项 | 内容 |
|---|---|
| 状态 | Proposed |
| 日期 | 2026-10-21（提出） |
| 决策人 | 项目负责人 |
| 门的类型 | 双向门（第一版 Workbench 发布前）：只影响 Workbench 一个项目；发布后再换界面技术等于重写界面 |
| 影响 | 补充 [ADR-0004](0004-runtimes-and-target-frameworks.md) 的运行时表：增加 `EplanMcp.Workbench` |

## 背景与问题
M5 要做 Workbench：审批队列、审计浏览、客户端配置向导、状态，中英双语（[M5 设计](../design/2026-10-21-m5-operability.md) 4.4）。架构 5.1 原定 .NET 10 WPF。**项目负责人 2026-10-20 决定：Workbench 的界面按 Microsoft Fluent UI（Fluent 2）及其组件库设计。** 还要决定用哪一种 Fluent 实现，它决定 Workbench 的运行时、依赖和发布方式，所以写成 ADR。ADR-0004 的运行时表中还没有 Workbench。

本项目的约束：按用户安装、不需要管理员权限；离线机器上能装能用；Host 是 .NET 10；中英文界面并能切换；无障碍（讲述人、键盘、高对比度）；能显示托盘图标和提醒；一名开发者加 AI 助手长期维护；暂时没有代码签名证书。

## 决策驱动因素
- 审批界面必须可靠：它是 ADR-0011 的两个渠道之一，出错时工程师可能误批。
- 离线机器：不能依赖安装时或运行时下载任何组件。
- 依赖越少越好（[编码规范第 7 节](../standards/coding-standards.md#7-依赖)、SEC-15）；运行时越少越好。
- Fluent 2 的还原度：项目负责人的决定。

## 备选方案
以下事实来自 2026-10-20 查阅的官方文档和包页面。
1. **WinUI 3 + Windows App SDK**（2.5.1，2026-09-16），未打包、自包含部署。控件本身就是 Fluent 样式，原生 UIA 和高对比度；不需要安装运行时即可复制部署（[自包含部署](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/self-contained-deploy/deploy-self-contained-apps)、[未打包](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/unpackage-winui-app)）。SDK 的大版本"不超过每六个月一次"，2.x 的服务期到 2027-04-29（[发布通道](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/release-channels)）；没有内置托盘图标（常用 H.NotifyIcon.WinUI，MIT）；没有 XAML 设计器；文档只写".NET 6 或更高"。
2. **WPF + .NET 10 自带的 Fluent 主题**：.NET 9 引入，`ThemeMode` 支持浅色、深色、跟随系统和强调色（[.NET 9 新功能](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/whats-new/net90)）；.NET 10 补了更多控件的样式，但说明中写"Fluent 样式仍在进行中"（[.NET 10 新功能](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/whats-new/net100)）；`Application.ThemeMode` 仍标为实验性（WPF0001，[API](https://learn.microsoft.com/en-us/dotnet/api/system.windows.application.thememode)）。.NET 10 是 LTS，支持到 2028-11-14。
3. **Blazor Hybrid（WPF 中的 BlazorWebView）+ Fluent UI Blazor 5**（5.0.1，2026-10-14，MIT）：README 写明"不是 ASP.NET Core 的正式部分"、尽力支持；依赖 WebView2 运行时：Windows 11 预装，Windows 10 上"少数"设备没有，离线时不会更新；固定版本超过 250 MB（[WebView2 分发](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/distribution)）。
4. **WebView2 + Fluent UI Web Components 3**（3.0 于 2026-06-29 稳定）：与方案 3 同样依赖 WebView2，另需 npm 构建链和自己写的消息桥。
5. **WPF + 第三方 Fluent 库**（WPF-UI 4.3，MIT）：NuGet 把 4.0–4.2 标为"有严重缺陷"而废弃。

## 决策
**建议方案 2**（项目负责人在 M5 设计 Q1 中决定；更看重 Fluent 2 组件的完整和原样时选方案 1）：

1. **运行时**：`EplanMcp.Workbench` 以 `net10.0-windows` 为目标，WPF，自包含发布到 `app\workbench\`（M5 设计 4.1）；托盘用 WinForms 自带的 `NotifyIcon`，不引入第三方界面库。
2. **Fluent 2**：用随 .NET 发布的 Fluent 主题（`ThemeMode` 或合并 `Fluent.xaml` 资源字典），主题跟随系统，可切换浅色、深色；主题缺的样式按 Fluent 2 的颜色、字体、间距规范自己写，集中在一个资源字典中。使用实验性 API 的地方抑制 WPF0001，并在代码中写明原因（编码规范第 1 节）。.NET 大版本升级时先核对 Fluent 主题的变化。
3. **例外**：审批卡片的两个按钮不用 Fluent 的强调色主按钮，样式相同（ADR-0011、ADR-0014）。
4. **字体**：界面文字用系统字体：Segoe UI Variable，中文 Microsoft YaHei UI（[Windows 排版](https://learn.microsoft.com/en-us/windows/apps/design/signature-experiences/typography)）；等宽的数据区用 Maple Mono CN，随安装包附带 SIL OFL 1.1 许可证（M5 设计 Q2）。
5. **文字**：中英文放在同一张表中（每个键两种语言），测试检查缺漏和占位符一致（TIA 的做法）；切换语言立即生效。
6. **验证**：spike S18 用审批卡片一页的原型，在 Windows 10、11 上核对讲述人、高对比度、200% 缩放、中文字体、外观、启动时间和体积。S18 发现方案 2 无法满足时改用方案 1，并修改本 ADR。

## 后果
- **好的**：
  - 与 Host 同一个运行时，不多一套 SDK 的生命周期，也没有第三方界面依赖；
  - 按用户、离线、自包含（可以单文件）发布都是成熟路径；UIA 最成熟；
  - 项目负责人在 TIA 中已经用 WPF 做过同样的 Workbench。
- **坏的或要付出的代价**：
  - Fluent 2 只还原到 WPF 主题提供的程度，缺的控件样式要自己写；
  - 依赖一个实验性 API，以后的 .NET 版本可能改动它；
  - Windows 10 上的外观（Mica 需要 Windows 11 22621）没有核实，由 S18 确认。
- **后续行动**：
  - ADR-0004 的运行时表增加 Workbench 一行（指向本 ADR）；架构 5.1 的 Workbench 一行改为"WPF + Fluent 主题"，字体按第 4 条；
  - README 的开发工具表中 Maple Mono 的用途随之修改；
  - spike S18。

## 各方案利弊
### 方案 1：WinUI 3
- 优点：Fluent 2 还原度最高，控件齐全；原生无障碍；系统通知在未打包时也能用。
- 缺点：SDK 每年约一次必须跟进的大版本；托盘靠第三方库；未打包时改界面语言有第三方报告的问题（未经微软证实）；体积大；没有设计器。
### 方案 3、4：WebView2
- 优点：Fluent 的 Web 组件最完整；界面可以用网页技术快速做。
- 缺点：离线机器上 WebView2 可能没有、不会更新；两套技术；变更清单这种不可信的项目文本进入网页渲染，多一个注入面；Fluent UI Blazor 刚重写、只有尽力支持。
### 方案 5：WPF-UI
- 优点：在 WPF 上还原度较好，带托盘组件。
- 缺点：第三方依赖，近期有多个版本因严重缺陷被废弃。

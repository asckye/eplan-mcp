# ADR-0015：Workbench 用 WinUI 3（Windows App SDK，未打包、自包含）实现 Fluent 2 界面

| 项 | 内容 |
|---|---|
| 状态 | Accepted |
| 日期 | 2026-10-20（提出，建议方案 2）· 2026-10-20（修改：项目负责人问 Fluent UI 与苹果的设计哪个可以之后，建议改为方案 1）· 2026-10-20（接受：项目负责人明确答复"可以用winUI3，但是字体的话用我之前推荐的"，字体按第 5 条） |
| 决策人 | 项目负责人 |
| 门的类型 | 双向门（第一版 Workbench 发布前）：只影响 Workbench 一个项目；发布后再换界面技术等于重写界面 |
| 影响 | 补充 [ADR-0004](0004-runtimes-and-target-frameworks.md) 的运行时表：增加 `EplanMcp.Workbench` |

## 背景与问题
M5 要做 Workbench：审批队列、审计浏览、客户端配置向导、状态，中英双语（[M5 设计](../design/2026-10-20-m5-operability.md) 4.4）。架构 5.1 原定 .NET 10 WPF。**项目负责人 2026-10-20 决定：Workbench 的界面按 Microsoft Fluent UI（Fluent 2）及其组件库设计。** 还要决定用哪一种 Fluent 实现，它决定 Workbench 的运行时、依赖和发布方式，所以写成 ADR。ADR-0004 的运行时表中还没有 Workbench。

本项目的约束：按用户安装、不需要管理员权限；离线机器上能装能用；Host 是 .NET 10；中英文界面并能切换；无障碍（讲述人、键盘、高对比度）；能显示托盘图标和提醒；一名开发者加 AI 助手长期维护；暂时没有代码签名证书。

## 决策驱动因素
- 审批界面必须可靠：它是 ADR-0011 的两个渠道之一，出错时工程师可能误批。
- 离线机器：不能依赖安装时或运行时下载任何组件。
- 依赖越少越好（[编码规范第 7 节](../standards/coding-standards.md#7-依赖)、SEC-15）；运行时越少越好。
- Fluent 2 的还原度：项目负责人的决定，并且明确要用它的组件库。

## 备选方案
以下事实来自 2026-10-20 查阅的官方文档和包页面。
1. **WinUI 3 + Windows App SDK**（2.5.1，2026-09-16），未打包、自包含部署。控件本身就是 Fluent 样式，原生 UIA 和高对比度；不需要安装运行时即可复制部署（[自包含部署](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/self-contained-deploy/deploy-self-contained-apps)、[未打包](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/unpackage-winui-app)）。SDK 的大版本"不超过每六个月一次"，2.x 的服务期到 2027-04-29（[发布通道](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/release-channels)）；没有内置托盘图标（常用 H.NotifyIcon.WinUI，MIT）；没有 XAML 设计器；文档只写".NET 6 或更高"。
2. **WPF + .NET 10 自带的 Fluent 主题**：.NET 9 引入，`ThemeMode` 支持浅色、深色、跟随系统和强调色（[.NET 9 新功能](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/whats-new/net90)）；.NET 10 补了更多控件的样式，但说明中写"Fluent 样式仍在进行中"（[.NET 10 新功能](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/whats-new/net100)）；`Application.ThemeMode` 仍标为实验性（WPF0001，[API](https://learn.microsoft.com/en-us/dotnet/api/system.windows.application.thememode)）。.NET 10 是 LTS，支持到 2028-11-14。
3. **Blazor Hybrid（WPF 中的 BlazorWebView）+ Fluent UI Blazor 5**（5.0.1，2026-10-14，MIT）：README 写明"不是 ASP.NET Core 的正式部分"、尽力支持；依赖 WebView2 运行时：Windows 11 预装，Windows 10 上"少数"设备没有，离线时不会更新；固定版本超过 250 MB（[WebView2 分发](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/distribution)）。
4. **WebView2 + Fluent UI Web Components 3**（3.0 于 2026-06-29 稳定）：与方案 3 同样依赖 WebView2，另需 npm 构建链和自己写的消息桥。
5. **WPF + 第三方 Fluent 库**（WPF-UI 4.3，MIT）：NuGet 把 4.0–4.2 标为"有严重缺陷"而废弃。

另外考虑过、不建议的方向：**苹果的设计风格**（Apple Human Interface Guidelines），项目负责人 2026-10-20 问过它与 Fluent UI 哪个更合适。它的规范和组件只面向苹果平台，Windows 上没有官方实现，每个控件都要自己画，讲述人、高对比度和键盘焦点也要自己保证；Workbench 在 EPLAN 和 Windows 的对话框旁边使用，用 Windows 自己的设计语言，工程师不必面对两套交互习惯。

## 决策
**选择方案 1**（项目负责人 2026-10-20 在 M5 设计 Q1 中决定）。最初建议的是方案 2；项目负责人要的是 Fluent 的组件库，在 Windows 桌面上它就是 WinUI 3，所以改为方案 1。S18 发现方案 1 无法满足时改用方案 2，并修改本 ADR。

1. **运行时**：`EplanMcp.Workbench` 以 .NET 10 的 Windows 目标框架构建，WinUI 3，未打包（不需要包身份、不经 MSIX 安装）、Windows App SDK 自包含，整个程序发布到 `app\workbench\`（M5 设计 4.1），不需要另装 Windows App SDK 运行时。Windows App SDK 固定一个版本，只在本项目发布新版本时升级。
2. **Fluent 2**：直接用 WinUI 3 自带的控件和主题资源，不另写控件样式；主题跟随系统，设置中可以切换浅色、深色；Windows 11 上用 Mica 背景，Windows 10 上用纯色。确实缺的部分按 Fluent 2 的颜色、字体、间距规范补，集中在一个资源字典中。观感按项目负责人的要求做成苹果式的简洁（留白多、颜色克制、排版层级少），只通过主题资源调整，规则见 M5 设计 4.5 的"观感"。
3. **托盘和单实例**：托盘用 Win32 `Shell_NotifyIcon` 的小封装（P/Invoke，集中在一个类中），不引入第三方界面库；S18 核对它在 WinUI 3 窗口中的消息处理，不行再评估 H.NotifyIcon.WinUI（MIT）。单实例用 Windows App SDK 的 `AppInstance`，第二个实例把激活转给第一个后退出（M5 设计 4.4）。
4. **例外**：审批卡片的两个按钮都用默认按钮样式，不用 Fluent 的强调色主按钮（`AccentButtonStyle`），样式相同（ADR-0011、ADR-0014）。
5. **字体**（项目负责人 2026-10-20 决定，M5 设计 Q2）：界面文字（中英文）和等宽的数据区都用 Maple Mono CN（SIL OFL 1.1，官方发布包 `MapleMono-CN`）。字体文件随程序放在 `app\workbench\` 中，只由 Workbench 自己加载，不安装到系统；只带用到的字重，不修改、不裁剪字体文件；许可证文本随安装包附带。加载失败时回退到系统字体 Segoe UI Variable 和 Microsoft YaHei UI。不用苹果的 SF 字体。等宽字体比 Segoe UI 宽，页面布局按实际宽度留余量。
6. **文字**：中英文放在同一张表中（每个键两种语言），测试检查缺漏和占位符一致（TIA 的做法）；切换语言立即生效。不用 `.resw` 和系统的界面语言切换，从而避开未打包时改界面语言的问题。
7. **验证**：spike S18 用审批卡片一页的原型，在 Windows 10、11 上核对讲述人、高对比度、200% 缩放、中文字体、Windows 10 上的外观、冷启动时间和发布体积；另查第 3 条的托盘和单实例、第 5 条的字体（未打包时从程序目录加载、中文显示、所带字重的体积），以及能否在 CI（windows-2025 镜像）上只用 `dotnet` 命令完成构建和自包含发布。
8. **升级**：Windows App SDK 的大版本至少每年跟进一次（2.x 的服务期到 2027-04-29），每次按 S18 的清单复查后再发布。

## 后果
- **好的**：
  - Fluent 2 的控件和交互原样可用，不需要自己补样式，符合项目负责人"Fluent UI 及其组件库"的要求；
  - 原生 UIA 和高对比度；Windows App SDK 是微软当前主推的 Windows 桌面界面框架；
  - 未打包、自包含后按用户复制部署，离线可用，不需要管理员权限。
- **坏的或要付出的代价**：
  - 多一套 SDK 的生命周期：大版本更新快，每年至少一次升级和复查；
  - 托盘要自己封装；没有 XAML 设计器，界面只能运行后查看；
  - 体积比方案 2 大（没有官方数字），由 S18 实测；
  - 随程序带 Maple Mono CN：官方发布包含全部字重和斜体，共约 141 MB（v7.9），只带用到的字重时增加多少由 S18 实测；
  - 官方文档只写".NET 6 或更高"，.NET 10 下的构建和发布由 S18 实测；
  - Windows 10 上没有 Mica，外观由 S18 确认。
- **后续行动**：
  - ADR-0004 的运行时表增加 Workbench 一行（指向本 ADR）；架构 5.1 的 Workbench 一行改为"WinUI 3（Windows App SDK，未打包、自包含）"，字体按第 5 条（均已完成）；
  - README 的开发工具表中 Maple Mono 的用途随之修改（已完成）；
  - spike S18。

## 各方案利弊
### 方案 2：WPF + Fluent 主题
- 优点：与 Host 同一个运行时，不多一套 SDK 的生命周期，没有第三方界面依赖；按用户、离线、自包含（可以单文件）发布都是成熟路径；UIA 最成熟；托盘用 WinForms 自带的 `NotifyIcon`；项目负责人在 TIA 中已经用 WPF 做过同样的 Workbench。
- 缺点：Fluent 2 只还原到 WPF 主题提供的程度，缺的控件样式要自己写；依赖一个实验性 API（WPF0001），以后的 .NET 版本可能改动它。
### 方案 3、4：WebView2
- 优点：Fluent 的 Web 组件最完整；界面可以用网页技术快速做。
- 缺点：离线机器上 WebView2 可能没有、不会更新；两套技术；变更清单这种不可信的项目文本进入网页渲染，多一个注入面；Fluent UI Blazor 刚重写、只有尽力支持。
### 方案 5：WPF-UI
- 优点：在 WPF 上还原度较好，带托盘组件。
- 缺点：第三方依赖，近期有多个版本因严重缺陷被废弃。

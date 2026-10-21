# Spike S18：Workbench 的界面技术（审批卡片原型）

| 项 | 内容 |
|---|---|
| 问题 | [ADR-0015](../../docs/adr/0015-workbench-ui-stack.md)：Workbench 用 WinUI 3 还是 WPF？能否只用 `dotnet` 命令构建和自包含发布，体积、冷启动、UIA、中文字体、浅色/深色切换、托盘、单实例如何（[M5 设计](../../docs/design/2026-10-20-m5-operability.md) 4.5 的 S18 清单）。2026-10-20 项目负责人在 spike 进行中追加了三项决定：A 改为平静的外观；Workbench 全部文字用随程序分发的 Maple Mono CN；最后决定 Workbench 用 **WPF 并沿用 TIA Workbench 的 Ant Design v5 主题**，放弃 WinUI 3。于是问题又加上一条：TIA 的主题能否原样复用，`EplanMcp.Workbench` 要复制什么 |
| 日期 | 2026-10-20 |
| 环境 | Windows 11 专业版 26300，单显示器 2560×1440、100% 缩放；.NET SDK 10.0.402（运行时 10.0.13）；Windows App SDK 2.5.1；Maple Mono CN v7.9（项目负责人下载并核对过 SHA-256，放在 `<仓库>\.local\fonts\`，不提交）。本机装有 Visual Studio 18 Insiders 和 Windows SDK 10.0.26100，构建日志表明都没有用到 |
| 状态 | **部分完成**：Windows 10、第二块 200% 缩放的显示器、系统高对比度、讲述人的语音、离线机器没有测（见“局限”） |
| 结论 | 三个原型都能只用 `dotnet` 构建和自包含发布，UIA 都能读出按钮名称、可聚焦的标题和倒计时。**B（WPF + TIA 主题 + Maple Mono CN）可行**：TIA 的 15 个文件中只改了 Primer.xaml 的两行字体，另需 7 个 TIA 类型的替身；没有 NuGet 依赖。代价是冷启动比 A 慢约 300 ms，复制的代码要在本仓库关掉 4 条风格规则。详见“结论” |

## 原型
三个原型显示同一张审批卡片：标题“批准 AI 修改 EPLAN 项目？”，工具、级别、项目路径、EPLAN 实例、标注“未验证”的客户端名称，纯文本的变更清单，倒计时文字加进度条，“批准”“拒绝”两个按钮（同样大小、同样的默认样式，都不是默认按钮，没有快捷键），显示时焦点在标题上。

| | A | B | B1 |
|---|---|---|---|
| 目录 | [WinUi/](WinUi/) | [Wpf/](Wpf/) | [WpfFluent/](WpfFluent/) |
| 技术 | WinUI 3，Windows App SDK 2.5.1，未打包（`WindowsPackageType=None`）、自包含（`WindowsAppSDKSelfContained=true`），.NET 10 | WPF .NET 10 + TIA Workbench 的主题（[Tia/](Wpf/Tia/)，见下文“TIA 主题的复用”） | WPF .NET 10 + 自带的 Fluent 主题（`ThemeMode`，WPF0001 在项目文件中注明后屏蔽） |
| 地位 | 项目负责人放弃 WinUI 3 后停在当时的进度，数字照记 | 当前方向 | ADR-0015 原方案 2 的写法，作为对照保留 |
| 卡片布局 | 平静风格：8 px 网格、卡片、Mica，只用主题资源（lightweight styling） | 按 TIA 的 `ApprovalsDrawer.xaml`，放在 480 px 的抽屉中 | 与 A 的布局相同 |
| 字体 | Maple Mono CN，从程序目录的 `Fonts\` 加载 | Maple Mono CN，从 `Fonts\` 加载，`Ui.Font` 与 `Ui.FontMono` 都用它 | 系统字体 |
| 托盘 | `Shell_NotifyIcon` 的 P/Invoke 小封装（[TrayIcon.cs](WinUi/TrayIcon.cs)） | WinForms `NotifyIcon`（与 TIA 相同） | WinForms `NotifyIcon` |
| 单实例 | `AppInstance`（[Program.cs](WinUi/Program.cs)） | 无 | 无 |

共同部分在 [Shared/](Shared/)：
- [UiText.cs](Shared/UiText.cs)：唯一的文字表，每个键中英两种文字；切换语言时重新赋值全部文字，并设置根元素的语言标记（zh-CN / en-US）。不用 `.resw`、`.resx`。
- [ChangeListText.cs](Shared/ChangeListText.cs)：变更清单的清洗。按 Unicode 类别删除 Cc（C0、DEL、C1）和 Cf（U+061C、U+200E/F、U+202A–U+202E、U+2066–U+2069、零宽字符、U+FEFF、U+E0000–U+E007F 的标签字符）以及孤立的代理项；条目内的换行（CR、LF、NEL、U+2028、U+2029）显示为“⏎”，制表符换成空格；每行最多 120 个字符（超出加“…”），最多 12 行，其余显示“还有 N 行”。
- [SampleRequest.cs](Shared/SampleRequest.cs)：示例请求。变更清单中有 U+202E（`-K1` + U+202E + `gnp.exe`，原样显示会变成 `-K1exe.png`）、BEL、ESC 序列、零宽字符、标签字符、孤立代理项、双向隔离符、嵌入的换行和超长行，共 15 条。清洗后删掉 12 个字符，`-K1gnp.exe` 按原顺序显示，UIA 读到的变更文字中隐藏字符为 0。
- [SpikeLog.cs](Shared/SpikeLog.cs)：命令行 `--log --lang --theme --deadline --font-check`；日志带“自进程创建起的毫秒数”，只写文件，不写 stdout。

两个 WPF 原型与 A 一样实现了 ADR-0014 的 500 ms 防误触：窗口每次变为可见（启动、从托盘恢复、第二个实例转来激活）之后 500 ms 内点击按钮无效。

## 方法
- 一键运行：`spikes\S18-workbench-ui\run-s18.ps1 [-FontDir <Maple Mono CN 的 fonts 目录>]`（默认 `<仓库>\.local\fonts\maple-mono-cn-v7.9\fonts`）。结果写到 `<仓库>\.local\s18\`：`summary.json`、每个变体的启动数据、UIA 树、日志和截图。它会打开窗口，每个原型打开一次任务栏的“显示隐藏的图标”浮出框（随即关闭）；打开的窗口全部关闭；不安装任何东西，不改系统设置。
- 构建（`dotnet` 命令，加 `-flp:v=diag` 记录用到的 MSBuild 和工具）：
  ```powershell
  dotnet build spikes\S18-workbench-ui\WinUi\EplanMcp.Spikes.S18.WinUi.csproj -c Release -p:S18FontDir=<目录>
  dotnet build spikes\S18-workbench-ui\Wpf\EplanMcp.Spikes.S18.Wpf.csproj -c Release -p:S18FontDir=<目录>
  dotnet build spikes\S18-workbench-ui\WpfFluent\EplanMcp.Spikes.S18.WpfFluent.csproj -c Release
  ```
- 发布（自包含，win-x64）：
  ```powershell
  dotnet publish <项目> -c Release -r win-x64 --self-contained -o .local\s18\publish\<变体> [附加属性]
  ```
  附加属性：`-p:S18FontDir=<目录>`（复制 Regular、SemiBold 两个字重到 `Fonts\`；`-p:S18FontDir=none` 不复制）、`-p:S18Slim=true`（A：只引用 WinUI 组件包，见下文）、`-p:PublishTrimmed=true`、`-p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true`。
- 探针 [Probe/](Probe/)（C#，`System.Windows.Automation`）：
  - `startup`：从 `Process.Start` 到该进程第一个可见的顶层窗口（`EnumWindows`，每 1 ms 查一次），以及程序自己记录的 `Loaded` 和第一帧（WinUI：`Loaded` 之后第一次 `CompositionTarget.Rendering`；WPF：`ContentRendered`），后两者从进程创建时间算起。每个变体 5 次，取中位数；两次之间间隔 1.5 秒，文件已在缓存中。
  - `inspect`：DPI 感知、载入模块的位置、UIA 树（`.local\s18\uia-tree-*.txt`）、标题和按钮的属性、两秒前后的倒计时、用 UIA 切换语言和主题并截图、最小化后经任务栏的 UIA 点击托盘图标、500 ms 防误触、A 的第二个实例。
  - `shot`：倒计时最后 20 秒（`--deadline 15`）在各主题下的截图。
- 字体检查（`--font-check`）：把同一段文字用“被测字体”和一组候选字体各渲染一次（`RenderTargetBitmap`），逐像素比较；像素相同的候选就是实际用到的字体。A 另比较只看着墨区域（裁掉空白，排除行高差别）的结果。

## 结果
### 对比
冷启动的数字来自第 2 轮（23:20，三个原型在同一轮中测）；括号中是第 1 轮（22:40，只有 A 和当时叫 B 的 B1）。两轮相差约 25%，是机器负载的差别（测量时桌面在用），比较时只比同一轮。

| 项 | A：WinUI 3 | B：WPF + TIA 主题 | B1：WPF + Fluent |
|---|---|---|---|
| 只用 `dotnet` 构建、自包含发布 | 可以。MSBuild 来自 `C:\Program Files\dotnet\sdk\10.0.402`；XAML 编译器 `XamlCompiler.exe`（.NET Framework 4.7.2 程序）和 `makepri.exe` 都来自 NuGet 包；日志中没有 Visual Studio 的程序 | 可以，没有 NuGet 包 | 可以，没有 NuGet 包 |
| 必需的属性 | `TargetFramework=net10.0-windows10.0.26100.0`、`UseWinUI`、`RuntimeIdentifier=win-x64`、`WindowsPackageType=None`、`WindowsAppSDKSelfContained=true`、`SelfContained=true`、**`EnableMsixTooling=true`**（没有它发布结果缺 `.pri`，启动即 `XamlParseException`）；单实例另需 `DISABLE_XAML_GENERATED_MAIN` | `UseWPF`、`UseWindowsForms`（托盘）、`ApplicationManifest`（每显示器 DPI）；复制的主题要用 `Link` 保持 `Themes/` 资源路径 | `UseWPF`、`UseWindowsForms`、`ApplicationManifest`、`NoWarn=WPF0001` |
| NuGet 包 | `Microsoft.WindowsAppSDK` 2.5.1，带入 Base 2.0.4、Foundation 2.3.12、InteractiveExperiences 2.1.9、WinUI 2.3.9、DWrite 2.1.0、Widgets 2.0.5、AI 2.5.5、ML 2.1.94、Search 2.5.5、Runtime 2.5.1、Microsoft.Web.WebView2 1.0.3719.77、Microsoft.Windows.SDK.BuildTools 10.0.26100.4654、…BuildTools.MSIX 1.7.251221100、Microsoft.Windows.AI.MachineLearning 2.1.74、System.Numerics.Tensors 9.0.0；另有 SDK 自动引用的 Microsoft.Windows.SDK.NET.Ref 10.0.26100.57 | 无 | 无 |
| 许可证 | Windows App SDK 的包是 **Microsoft Software License Terms**（不是 MIT），`requireLicenseAcceptance=true`；再分发要求最终用户同意不低于该协议的条款、并为微软免责；含“数据收集”条款 | TIA 主题 MIT（Copyright (c) 2026 bulaofen0036-coder），.NET MIT，Maple Mono CN SIL OFL 1.1 | .NET MIT |
| 自包含发布（不含字体） | 229.6 MB，519 个文件；只引用 WinUI 组件包（`S18Slim`）173.1 MB，453 个；再裁剪 81.2 MB，284 个 | 171.4 MB，474 个 | 171.2 MB，474 个 |
| 加两个字重的 Maple Mono CN | 265.0 MB，521 个；组件包 208.5 MB；裁剪 116.6 MB，286 个 | 206.8 MB，476 个 | — |
| 单文件 | 能生成（一个 166.5 MB 的 exe 加 .pdb），**启动失败**：`AppInstance` 激活报 0x8007007E（找不到模块），进程以 0xE0434352 退出 | 可以：一个 165.1 MB 的 exe 加 `Fonts\` 中的两个字体文件（共 200.6 MB，另有 .pdb、.xml）；启动正常 | 可以：一个 165.0 MB 的 exe（共 165.1 MB）；启动正常 |
| 裁剪 | 可以（`IL2104` 警告来自 Windows SDK 投影和 WinRT.Runtime），能启动，但冷启动更慢（见下行） | 不可以：`NETSDK1175`（启用裁剪时不支持 Windows 窗体） | 同 B |
| 冷启动中位数：窗口可见 / Loaded / 第一帧（ms） | 292 / 330 / 360（231 / 283 / 308）；组件包 264 / 327 / 352；裁剪 435 / 473 / 506（358 / 395 / 425） | 587 / 640 / 680；单文件 618 / 676 / 724 | 529 / 575 / 629（448 / 481 / 510）；单文件 625 / 655 / 702（450 / 487 / 521） |
| 发布后第一次启动（1 次，含杀毒软件扫描新文件） | 1962 ms | 2242 ms；单文件 634 ms | 1941 ms；单文件 550 ms |
| 工作集 | 约 126–130 MB（裁剪约 113 MB） | 约 120 MB | 约 115 MB |
| 自包含是否真的生效 | `coreclr.dll`、`Microsoft.UI.Xaml.dll`、`Microsoft.WindowsAppRuntime.dll` 都从程序目录载入；虽然本机装有 Windows App Runtime 2.5.1 框架包，没有模块来自 `WindowsApps` | `coreclr.dll`、`PresentationFramework.dll`、`wpfgfx_cor3.dll` 等都从程序目录载入，没有来自 `dotnet\shared` | 同 B |
| UIA：按钮 | “批准”“拒绝”，Button，Invoke，341×32 与 341×32；AccessKey、AcceleratorKey 为空 | “批准”“拒绝”，Button，65.6×32 与 65.6×32（UIA 取整为 66 与 65）；`IsDefault`、`IsCancel` 为 false；另有链接样式的“参数”按钮 | “批准”“拒绝”，341×31 与 341×31 |
| UIA：标题 | WinUI 的 `TextBlock` 不能获得焦点，标题放在 `IsTabStop=True` 的 `ContentControl` 中：Group，名称为标题文字，可聚焦，显示时是系统焦点 | `Focusable=True` 的 `TextBlock`（与 TIA 的抽屉标题相同）：Text，可聚焦，显示时是系统焦点 | 同 B |
| UIA：倒计时 | Text“剩余 1:54，到期自动拒绝”，两秒后变为 1:52；ProgressBar 名称“剩余时间”，RangeValue 0–100 | 同 A（数字用等宽样式单独显示，可访问名称设为整句；只放数字时读出的只有“1:58”）；剩余 30 秒、10 秒时用 TIA 的 `AccessibleStatus` 发 LiveRegionChanged | 同 A |
| 显示时的焦点（APR-02） | 标题。从托盘恢复后，激活会把焦点还给上次的控件，要在 `Activated` 中再聚焦一次（已做） | 标题；同样需要在 `Activated` 中再聚焦 | 同 B |
| 语言切换 | 立即，无需重启；UIA 读到新标题用了 22 ms | 立即，62 ms | 立即，24 ms |
| 浅色/深色运行时切换 | 立即：根元素的 `RequestedTheme`，标题栏用 `AppWindow.TitleBar.PreferredTheme` | 立即：TIA 的 `ThemeManager` 换掉第 0 个资源字典；系统标题栏用 DWM 的深色属性 | 立即：`Application.ThemeMode`，标题栏跟着变 |
| 高对比度 | WinUI 的 `XamlControlsResources` 有 HighContrast 主题字典，取 `SystemColor*`；A 的覆盖项也写了 HighContrast 条目 | TIA 的 `Palette.HighContrast.xaml` 只引用 `SystemColors.*`；系统开启高对比度时 `ThemeManager` 自动换用它。截图用的是 `ThemeManager` 的覆盖开关，没有改系统设置 | WPF Fluent 自带 `hc` 主题字典 |
| 每显示器 DPI | Per-Monitor V2 | 加 `app.manifest` 后为 Per-Monitor V2；**不加时 .NET 10 的 WPF 为“系统”感知**（第一次测 B1 时实测） | 同 B |
| 托盘 | 可以：隐藏窗口在界面线程上，WinUI 的消息循环把回调送到；新图标在“显示隐藏的图标”浮出框中，经 UIA 点击后窗口恢复并成为前台 | 可以，同 A | 可以 |
| 单实例 | 可以：第二个进程约 0.6 秒内以 0 退出，第一个收到 `Activated` 并显示窗口 | 原型中没有做 | 没有做 |
| 500 ms 防误触 | 从托盘恢复后 172 ms 的点击被忽略，700 ms 后的点击生效 | 235 ms 的被忽略，之后的生效 | 191 ms 的被忽略，之后的生效 |

### 字体
- **Maple Mono CN 的体积**（本文的 MB 都是 1024×1024 字节）：Regular 18,594,640 字节、SemiBold 18,573,160 字节、Bold 18,549,392 字节，每个字重约 17.7 MB（16 个文件在 17.4–18.4 MB 之间，斜体略大）。原型复制 Regular 和 SemiBold，发布结果多 35.4 MB（A 与 B 都是）。
- **对冷启动的影响**：测不出。A 带字体与不带字体的中位数相差 6 ms（第 2 轮）和 −3 ms（第 1 轮）；B 为 −19 ms。字体文件在第一次排版时才映射。
- **A（WinUI 3，未打包）能用的写法**：`ms-appx:///Fonts/MapleMono-CN-Regular.ttf#Maple Mono CN`，以及 `/Fonts/MapleMono-CN-Regular.ttf#Maple Mono CN`；渲染与系统字体都不同，中文和英文都由 Maple Mono CN 绘制。**不能用的**：`file:///<程序目录>/Fonts/...#...`、只写目录的 `ms-appx:///Fonts/#Maple Mono CN`、只写家族名（字体没有安装），这三种都退回到系统字体。一个 `FontFamily` 只认一个文件：SemiBold 的文字如果用指向 Regular 文件的家族，渲染结果与 Regular 加粗模拟相同；所以原型为 SemiBold 的样式另设一个指向 SemiBold 文件的家族（`WorkbenchStrongFontFamily`）。程序启动时扫描 `Fonts\` 中的 .ttf/.otf，从文件的 name 表读家族名，没有文件时用 `Segoe UI Variable Text, Microsoft YaHei UI`。必须在 `OnLaunched` 中设置资源：在 `App` 构造函数中访问 `Application.Resources` 抛出 E_UNEXPECTED，进程随即退出。
- **B（WPF）能用的写法**：`new FontFamily(new Uri(<程序目录>\Fonts\), "./#Maple Mono CN, Segoe UI, Microsoft YaHei UI")`（原型的写法，`Fonts.GetFontFamilies(<目录>)` 读出家族名），以及 `file:///<程序目录>/Fonts/#Maple Mono CN`、`./MapleMono-CN-Regular.ttf#Maple Mono CN`。**不能用的**：`pack://siteoforigin:,,,/Fonts/#Maple Mono CN`、只写家族名。以目录为基准时 WPF 把目录中同一家族的各字重归为一个家族，SemiBold 文字自动用 SemiBold 文件，不需要第二个家族。
- **不设字体时用的是什么**：A（WinUI 默认 `XamlAutoFontFamily`）：拉丁文是 Segoe UI Variable；中文与 “Microsoft YaHei” 逐像素相同（字形与 Microsoft YaHei UI 相同，行高不同），zh-CN 与 en-US 两种语言标记结果一样。B1（WPF 默认）：本机界面语言为中文，`SystemFonts.MessageFontFamily` 是 Microsoft YaHei UI，中英文都用它；若显式设为 Segoe UI Variable Text 且语言标记为 en-US，中文退回到 **Yu Gothic UI**（日文字形），zh-CN 时退回到 Microsoft YaHei。
- **等宽字体放不放得下**：
  - A（820×920 窗口中的卡片）：中英文标题都在一行内；变更清单除超长的一行（横向滚动）外都完整；原型的语言、主题工具条在英文时放不下 760 px，窗口改为 820 px。
  - B（480 px 抽屉）：英文标题一行放不下，按 TIA 的写法会被省略为 “Approve the AI's change to the EPLAN…”，原型改为换行；头部一行放不下 host 和版本，被截断（中文界面显示 “epla…”，英文 “e…”；完整文字在 ToolTip 和 UIA 名称中）；项目路径换成两行；操作列表的对象一栏只有约 14 个汉字宽，改为换行，每条占 2–3 行；工具名一栏在每一行重复同一个工具。
- Maple Mono 的 `config.json` 写着 `"ligature": true`，变更清单中的 `->`、`!=`、`==` 等可能显示成连字。WinUI 有 `Typography` 的连字开关，S18 没有验证；B 没有查。

### TIA 主题的复用（B）
来源：TIA Portal Openness MCP 仓库（MIT，Copyright (c) 2026 bulaofen0036-coder）提交 `38ca2883` 的 `src/Studio/Gui/`。每个复制的文件开头都加了注明来源和版权的注释，TIA 仓库没有改动。

| 复制的文件 | 原位置（`src/Studio/Gui/`） | 改动 |
|---|---|---|
| [Primer.xaml](Wpf/Tia/Themes/Primer.xaml)（3006 行） | `Themes/Primer.xaml` | 两行：`Ui.Font`、`Ui.FontMono` 原为随包的 Noto Sans SC 和 JetBrains Mono（`/TiaOpenness;component/Fonts/...`），改为系统字体作后备；运行时由 [App.xaml.cs](Wpf/App.xaml.cs) 换成 Maple Mono CN |
| Palette.Light / Dark / HighContrast.xaml、Icons.xaml | `Themes/` | 无 |
| ThemeManager.cs | `Themes/ThemeManager.cs` | 无 |
| Ux.cs、ContrastSelection.cs、AccessibleStatus.cs、BoolConverters.cs、ScrollCommandNameConverter.cs、LogTailView.cs、WorkbenchLogView.cs | `Controls/` | 无 |
| Loc.cs、TrExtension.cs | `Localization/` | 无 |

为了在 TIA 之外编译，还要：
1. **7 个 TIA 类型的替身**（[TiaStubs.cs](Wpf/Tia/TiaStubs.cs)）：Primer.xaml 和复制的辅助类引用了 TIA 应用自身的类型。`Strings`（TIA 的 55 KB 文字表；替身只放主题模板自己用的滚动条名称）、`FeatureTone`（枚举，原样）、`DeviceNode`（设备树节点，Primer 只为它声明了模板）、`LogLevel` 和 `EnumIndexConverter`（原样）、`DisplayValueConverter`（原来格式化 TIA 的结果类型，替身原样返回）、`AccessibleNameConverter`（原来为 TIA 的调用、审计、程序块等行生成可访问名称，替身返回行的文字）。缺了它们，Primer.xaml 编译不过。
2. **资源路径**：`ThemeManager` 用 `pack://application:,,,/<程序集>;component/Themes/` 加载调色板，复制到 `Tia\Themes\` 的文件要在项目文件中用 `Link="Themes\..."` 保持这个路径；`App.xaml` 中调色板必须留在第 0 个合并字典（与 TIA 的注释相同）。
3. **本仓库的规则**：复制的代码在本仓库的 `.editorconfig` 下有 56 个错误：IDE0011（`if` 等不加大括号）45 个、IDE1006（私有静态字段没有 `_` 前缀）8 个、CA1305（`Convert.ToString` 没有区域参数）2 个、IDE0161（块状命名空间）1 个。原型在 `Wpf/Tia/` 放了一个只关掉这四条的 [.editorconfig](Wpf/Tia/.editorconfig)，代码不改。
4. TIA 的窗口用自绘标题栏（`WindowChrome` 和 Primer 的窗口按钮），原型没有复制，用系统标题栏加 DWM 深色属性。

原型的卡片按 TIA 的 `Views/ApprovalsDrawer.xaml` 的结构重写（[MainWindow.xaml](Wpf/MainWindow.xaml)）：头部一行是客户端名称加“未验证”、传输方式小标签、host 与版本、状态标签；下面是“项目”（等宽样式的完整路径）和“EPLAN 实例”两行；带内边框、可滚动的操作列表（动作 / 对象 / 工具）；文字形式的风险标签；右侧等宽样式的倒计时和下面的进度条；右对齐的“参数”链接（展开 JSON 代码块）、“拒绝”“批准”。与 TIA 的一处不同：“批准”不用强调色的 `Primer.Run`，两个按钮都用 `Primer.Secondary`，并用 `SharedSizeGroup` 保证同宽（ADR-0014 比 TIA 的 APR-04 更严）。参数 JSON 中所有控制字符和格式字符（包括 U+202E）都写成 `\u` 转义。

遵循的 TIA 规则：APR-02（显示时焦点在标题，不在按钮上）、APR-03（卡片内容，PLC 换成 EPLAN 实例）、APR-04（同尺寸、并列、都不是默认按钮；比它更严：样式也相同）、APR-06（倒计时文字加进度条，只在 30 秒、10 秒播报）、LAY-04/05（4 的倍数、间距 token）、LAY-08（控件 4、浮层 8 的圆角；Primer 自己的按钮仍是 Ant 的 6）、LAY-12（控件高 32、列表行最小 36）、TYP-02（卡片中只用字号 token：Caption 12、Body 14、Code 12；抽屉标题沿用 TIA 的 `Ui.FontSize.Section` 16，它不在 TYP-02 的表中，TIA 自己的抽屉标题也是 16）。spec 附录 A 的 token（间距 4/8/12/16/24/32、控件 32、行 36、抽屉 480、圆角 4/8、字号 12/14/18/20/28、Code 12）放在一个资源字典 [Tokens.xaml](Wpf/Tokens.xaml) 中。

### A 的外观（lightweight styling）
只用主题资源做到了，没有重写任何控件模板：
- 覆盖的 WinUI 资源键：`ProgressBarForeground`（Default、Light 指向 `ControlStrongFillColorDefaultBrush`，HighContrast 指向 `SystemColorHighlightColorBrush`；在 App.xaml 的三个主题字典中都写，缺 HighContrast 条目时会退回 Default）、`ContentControlThemeFontFamily`（运行时设为 Maple Mono CN，按钮等控件的字体来自它）。
- 自己加的键：`WorkbenchFontFamily`、`WorkbenchStrongFontFamily`，以及 `S18Caption`、`S18Body`、`S18BodyStrong`、`S18Subtitle` 四个文字样式（BasedOn WinUI 的 Caption、Body、BodyStrong、Subtitle 样式，只改字体）。WinUI 的文字样式把字体写死为 `XamlAutoFontFamily`，不读 `ContentControlThemeFontFamily`，所以只覆盖资源键改不了 `TextBlock` 的字体。
- 不是资源的部分：卡片的 `ThemeShadow` 和 `Translation`、窗口的 `MicaBackdrop`；倒计时最后 20 秒用 `ProgressBar.ShowPaused` 取系统的警示色，再显示一个警示色的图标。
- 截图：`.local\s18\A-light-zh.png` 等。浅色时卡片为白色、背景为 Mica 的浅灰，阴影很淡；深色时卡片、标题栏都变深；进度条为中性灰，最后 20 秒变为黄色并出现警示图标。

### 遇到的问题与解决办法
1. A：`dotnet publish` 的结果缺少应用的 `.pri`（XAML 编译结果），启动时 `MainWindow` 报 `XamlParseException`。加 `EnableMsixTooling=true` 后解决（WinUI 模板也这样设，未打包也要）。
2. A：在 `App` 构造函数中访问 `Application.Resources` 抛出 E_UNEXPECTED（0x8000FFFF），进程以 0xC000027B 退出；移到 `OnLaunched` 后正常。
3. A：单文件发布不能启动（`AppInstance` 激活时找不到模块），没有找到办法。
4. A：`Microsoft.WindowsAppSDK` 元包带入 AI、ML（onnxruntime 20.7 MB、DirectML 17.8 MB）、Search、Widgets；改为只引用 `Microsoft.WindowsAppSDK.WinUI`、`.InteractiveExperiences`、`.DWrite` 后少 56 MB，功能不受影响。只引用 WinUI 和 InteractiveExperiences、不引用 DWrite 时 `MainWindow` 报 `XamlParseException`（当时缺 `.pri` 的问题也存在，没有单独确认 DWrite 是否必需）。
5. A、B、B1：从托盘恢复窗口时，激活会把焦点还给上次获得焦点的控件，覆盖了程序设在标题上的焦点；在 `Activated` 中再聚焦一次（WinUI 另加一次低优先级的延后聚焦）后，焦点稳定在标题上。回答之后两个按钮被禁用，焦点落到第一个控件（A）或窗口（B、B1），下一个问题出现时要重新聚焦标题。
6. 三个原型的新托盘图标都进了“显示隐藏的图标”浮出框；`Shell_NotifyIconGetRect` 返回的是浮出框按钮的位置。
7. 当另一个程序正在前台被使用时，Windows 不把前台交给新启动的原型窗口（一次测量中出现），此时 UIA 的系统焦点在那个程序上；这与 ADR-0014 “窗口不抢焦点”的要求一致，属于 S16 的内容。
8. B：TIA 主题的复制见上一节；英文标题、host、项目路径、操作对象在等宽字体下放不下，见“字体”。

## 结论
1. 三个原型都能在没有 Visual Studio 的情况下只用 `dotnet build` / `dotnet publish` 构建并自包含发布，适合 CI；A 需要 `EnableMsixTooling=true`，构建机需要 .NET Framework 4.7.2（XAML 编译器），windows-2025 镜像满足（未在 CI 上实测）。
2. **项目负责人选定的 B（WPF + TIA 主题 + Maple Mono CN）在测到的各项上可行**：UIA 名称、标题焦点、倒计时、语言和主题的即时切换、托盘、防误触都与 A 相当；Maple Mono CN 从程序目录加载，不用安装，各字重自动对应；没有 NuGet 依赖，许可证都是 MIT 或 OFL；可以单文件发布。测到的代价：冷启动比 A 慢约 300 ms（同一轮中窗口可见 587 ms 对 292 ms），比 B1 慢约 60 ms；两个字重的字体使发布结果多 35.4 MB；不能裁剪；480 px 抽屉中等宽字体需要换行。
3. **TIA 主题可以复用，但不是只复制主题文件**：15 个文件中只改两行字体，另需 7 个 TIA 类型的替身、一个保持资源路径的 `Link` 和一个关掉 4 条风格规则的 `.editorconfig`。`EplanMcp.Workbench` 至少要带：Primer.xaml、三个调色板、Icons.xaml、ThemeManager.cs，Primer 引用的 Ux.cs、ContrastSelection.cs、BoolConverters.cs、ScrollCommandNameConverter.cs、LogTailView.cs、WorkbenchLogView.cs、Loc.cs、TrExtension.cs，以及替身或真正的 FeatureTone、LogLevel、转换器和文字表；审批卡片还用到 AccessibleStatus.cs。Primer.xaml 中还有 TIA 各页面（日志、设备树、程序块、审计等）的样式，不用也照样编译。复制后两边会各自修改；是否改为两个仓库共用一份主题，由项目负责人决定。
4. 与 ADR-0015 的关系：ADR-0015 在 main 上已按 WinUI 3 接受，项目负责人随后改为 WPF + TIA 主题。测量结果中支持 WinUI 3 的只有冷启动和现成的单实例 API；不利于它的有：Windows App SDK 的许可证不是 MIT 且有再分发条件、发布体积更大、`EnableMsixTooling` 和资源初始化时机这类只在运行时暴露的坑、单文件发布不可用。这些事实支持把 ADR-0015 改为 WPF；修改 ADR 由项目负责人决定。

## 局限
- **Windows 10** 没有测：外观（A 的 Mica 在 Windows 10 上退回纯色）、字体回退、托盘位置都可能不同。
- **200% 缩放、第二块显示器**没有测：本机只有一块 100% 的显示器；只读出了三个原型都是 Per-Monitor V2（B、B1 需要清单）。
- **系统高对比度**没有打开（不改系统设置）：A 只核对了主题字典，B 的截图用的是 `ThemeManager` 的覆盖开关（系统颜色仍是普通主题的），B1 只确认了 `hc` 字典存在。
- **讲述人的语音**没有测：只读了 UIA 树；30 秒、10 秒的 LiveRegion 播报是否被朗读未知。
- **离线机器**没有测：自包含已核对（模块都从程序目录载入），但没有在断网、未装 .NET 和 Windows App Runtime 的机器上运行。
- **“跟随系统”在运行时的切换**没有测（要改系统的应用模式）。
- 冷启动是文件在缓存中的数字；重启后的真正冷启动没有测，发布后第一次启动的单次数字只作参考。两轮之间差约 25%，说明桌面负载影响很大。
- 字体检查用逐像素比较，只能确认“与某个候选相同”；Maple Mono CN 的连字没有检查。
- CI（windows-2025 镜像）上没有实际运行构建；本仓库 CI 不构建 spikes（`EplanMcp.Ci.slnf` 不含它们）。

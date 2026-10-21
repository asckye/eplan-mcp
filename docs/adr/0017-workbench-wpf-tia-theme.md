# ADR-0017：Workbench 改用 WPF（.NET 10），复用 TIA Workbench 的 Ant Design v5 主题，字体用 Maple Mono CN

| 项 | 内容 |
|---|---|
| 状态 | Accepted |
| 日期 | 2026-10-20（提出）· 2026-10-20（接受：项目负责人明确答复"ADR 17同意的"） |
| 决策人 | 项目负责人 |
| 门的类型 | 双向门（第一版 Workbench 发布前）：只影响 Workbench 一个项目；发布后再换界面技术等于重写界面 |
| 影响 | 取代 [ADR-0015](0015-workbench-ui-stack.md)（WinUI 3）；补充 [ADR-0004](0004-runtimes-and-target-frameworks.md) 的运行时表；[ADR-0014](0014-workbench-approval-channel.md) 的界面规则不变 |

## 背景与问题
ADR-0015 选了 WinUI 3：项目负责人当时要的是 Fluent UI 的组件库，观感按苹果式的简洁（[M5 设计](../design/2026-10-20-m5-operability.md) 4.5）。2026-10-20 项目负责人让我学习他的 TIA_Portal_Openness_MCP 项目（下称 TIA）的 Workbench 界面设计，然后决定：**"学规范，视觉也和TIA一致"**；被问到实现方式时选择"改用 WPF，复用 TIA 主题"，字体仍用 **Maple Mono CN**。这推翻了 ADR-0015 的界面技术，所以另写本 ADR。

TIA 的 Workbench（2026-10-20 只读查阅其仓库）：
- **技术**：WPF，.NET 10（`net10.0-windows`）；原生 WPF 控件加自绘模板，没有用 WPF 的 Fluent 主题，也没有第三方界面库。
- **视觉**：2026-10-18 起按 Ant Design v5（之前是 GitHub Primer）。主题在 `src/Studio/Gui/Themes/`：
  - `Primer.xaml`（约 3000 行，控件模板与非颜色 token）；
  - `Palette.Light.xaml`、`Palette.Dark.xaml`、`Palette.HighContrast.xaml`：语义颜色 token，高对比映射到 Windows 系统色；
  - `Icons.xaml`；
  - `ThemeManager.cs`：运行中替换调色板，不重启。
- **规范**：`docs/development/design-spec/workbench-ui.md`（v0.2），规则带编号（MUST/SHOULD、理由、微软和 W3C 的出处），以 WCAG 2.2 AA 为底线，另有偏差清单；由 `ThemeSurfaceTests`（文字对比 ≥ 4.5:1、图形 ≥ 3:1）、UIA 树测试和 `StringsTests` 守住。
- **布局**：左侧栏 216 epx（窄窗口收为 56 epx）、顶栏 56 epx、页面骨架（页头卡片 → 卡片或表格 → 可选的右侧栏）、抽屉（审批 480 epx）、toast、状态栏。
- **字体**：随包的 Noto Sans SC 和 JetBrains Mono（SIL OFL 1.1，TIA 偏差 UI-37）。
- **许可证**：MIT（`Copyright (c) 2026 bulaofen0036-coder`）；本项目也是 MIT。

## 决策驱动因素
- 项目负责人的决定：两个 Workbench 看起来是同一套产品。
- 审批界面必须可靠，无障碍要可验证（ADR-0011、ADR-0014）：TIA 的主题已经按 WCAG 2.2 AA 做过并有测试。
- 依赖和运行时越少越好（编码规范第 7 节、SEC-15）。
- 一名开发者加 AI 助手长期维护。

## 备选方案
1. **WPF + 复制 TIA 的主题**：观感与 TIA 相同；主题、无障碍辅助类和测试可以照搬；与 Host 同一个 .NET 运行时。
2. **WinUI 3 + 按 Ant Design 重做模板**（ADR-0015 的技术）：TIA 的 WPF 主题不能直接用，每个控件的外观都要在 WinUI 3 上重做，只能接近 TIA；还要跟进 Windows App SDK 的大版本。
3. **WPF + .NET 自带的 Fluent 主题**（ADR-0015 的备选）：不是 TIA 的观感。
4. **把 TIA 的主题做成两个仓库共用的包**：两个项目的发布和版本会绑在一起；TIA 的主题键名（`Primer.*`、`Ant.*`）和控件辅助类也不是为共用设计的。

## 决策
**选择方案 1**（项目负责人 2026-10-20 确认）：

1. **运行时**：`EplanMcp.Workbench` 以 `net10.0-windows` 为目标，WPF，自包含发布到 `app\workbench\`（M5 设计 4.1）；不用 Windows App SDK。单实例用互斥体，托盘用 WinForms 自带的 `NotifyIcon`（TIA 的做法），不引入第三方界面库。
2. **视觉**：复制 TIA 的主题文件（`Primer.xaml`、三套调色板、`Icons.xaml`、`ThemeManager.cs`）和审批卡片等界面要用的控件辅助类到 `EplanMcp.Workbench` 中。
   - 每个复制来的文件保留 TIA 的 MIT 版权声明，并写明来源文件和 TIA 的提交号；第三方声明中列出 TIA。
   - 只做一次性复制，之后 TIA 的主题有改动时，由我们有意识地合并过来，不自动同步。
   - 视图只引用语义 token，不写字面的颜色和字号（TIA 规则 COL-01、TYP-03）。
3. **规范**：参照 TIA 的 `workbench-ui.md` 写本项目的 `docs/standards/workbench-ui.md`。
   - 保留规则的编号和写法、WCAG 2.2 AA 底线、token、外框与页面骨架、审批卡片、键盘、通知、本地化与文字规则；
   - TIA 特有的部分不照搬：AI 控制界面、程序块、VCI、版本控制；
   - 另附本项目的偏差清单。TIA 的主题测试（对比度、UIA 树、文字表）一并移植。
4. **字体**（项目负责人 2026-10-20 决定）：界面文字和等宽文字都用 Maple Mono CN（SIL OFL 1.1，官方发布包 `MapleMono-CN`）。
   - 字体文件随程序放在 `app\workbench\` 中，只由 Workbench 自己加载，不安装到系统；只带用到的字重，不修改字体文件；许可证文本随安装包附带。
   - 加载失败时回退到 Segoe UI Variable 和 Microsoft YaHei UI。
   - 这是相对于 TIA 字体（UI-37）和 TIA 规则 TYP-01 的偏差，记入偏差清单。
5. **比 TIA 严格的地方**：审批卡片的"批准"和"拒绝"样式相同，都不用强调色（ADR-0014 第 6 条；TIA 的 APR-04 允许"批准执行"用强调色）。ADR-0014 的其余界面规则（每个问题单独回答、焦点在标题、500 毫秒防误触、客户端名标"未验证"）照旧。
6. **验证**：spike S18 改为验证本方案，用 TIA 的主题和 Maple Mono CN 做审批卡片一页：
   - 只用 `dotnet` 构建与发布；
   - 体积、冷启动时间；
   - UIA 树；
   - 浅色、深色、高对比；
   - 字体加载和每个字重的体积；
   - TIA 的主题脱离 TIA 编译时需要改什么。

## 后果
- **好的**：
  - 与 TIA 的观感一致，项目负责人在两个产品之间切换时没有落差；
  - 主题、无障碍辅助类和测试已经在 TIA 中做过并验证过；
  - 与 Host 同一个 .NET 10 运行时，没有 Windows App SDK 的版本跟进，没有第三方界面依赖；
  - 体积小于 WinUI 3。
- **坏的或要付出的代价**：
  - 要维护约 3000 行复制来的控件模板；以后与 TIA 的主题会逐渐不同，合并 TIA 的改动要人工进行；
  - 观感不是 Windows 自己的 Fluent 设计，原生控件的新样式不会自动得到；
  - 等宽字体比比例字体宽，Ant 的布局按比例字体设计，页面要按实际宽度留余量（S18 核对）；
  - Maple Mono CN 增加安装包体积（每个字重约 18 MB）。
- **后续行动**：
  - ADR-0015 标为"Superseded by ADR-0017"（已完成）；
  - ADR-0004 的运行时表和架构 5.1 的 Workbench 一行改为 WPF 和 TIA 的主题（已完成）；
  - M5 设计 4.5 的观感规则改为引用 `docs/standards/workbench-ui.md`；
  - 写 `docs/standards/workbench-ui.md`；
  - 第三方声明增加 TIA 的主题。

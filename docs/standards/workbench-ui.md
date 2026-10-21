# Workbench 界面规范

> 依据：项目负责人的 TIA_Portal_Openness_MCP 项目（下称 TIA）的界面规范 `docs/development/design-spec/workbench-ui.md` v0.2（MIT 许可，按 TIA 的提交 `38ca2883` 阅读）和 [ADR-0017](../adr/0017-workbench-wpf-tia-theme.md)。规则的结构、编号方法和以 WCAG 2.2 AA 为底线的做法沿用 TIA，文字按本项目重写；TIA 特有的内容不收录（0.4 节），有意的不同列在附录 B。
> 适用范围：`EplanMcp.Workbench`（WPF，`net10.0-windows`，[M5 设计](../design/2026-10-20-m5-operability.md) 4.4）的全部界面：主窗口与外框，审批队列、审计、状态、客户端四个页面，设置和审计详情两个抽屉，对话框，toast、托盘和系统通知。测试放在 `tests/EplanMcp.Workbench.Tests`。两者都还没有建立。
> 审批界面的规则来自 [ADR-0011](../adr/0011-approval-out-of-band.md)、[ADR-0014](../adr/0014-workbench-approval-channel.md)、M5 设计 4.6 和 [security.md](security.md) 的 SEC-23、SEC-24，优先于本文的其他规则（第 11 节）。

---

## 0. 如何使用本规范

### 0.1 基线与优先级
规则冲突时，按下面的顺序取上位：

1. **项目红线**：第 11 节的审批规则，标注"项目规则"。其中比 Microsoft 指南或 WCAG 更严格的地方照样执行。
2. **WCAG 2.2 AA**：[WCAG 2.2](https://www.w3.org/TR/WCAG22/) 的 A 级和 AA 级成功准则，按 W3C 对非 Web 软件的解释 [WCAG2ICT](https://www.w3.org/TR/wcag2ict-22/) 用于 WPF 桌面程序；逐条的适用性见第 15 节。
3. **Microsoft Windows 应用设计指南**（learn.microsoft.com/windows/apps/design）：管行为，即窗口、键盘、焦点、对话框、主题、无障碍、本地化和写作。
4. **WPF 与 Windows 桌面的专项文档**：WPF 的焦点与 UI Automation、.NET《Accessibility Best Practices》、Win32 高 DPI 指南；新版指南中没有对应条目时，才引用 Win32 UX Guide。
5. **TIA 主题的视觉取值**（Ant Design v5，ADR-0017）：管颜色、字号、间距、圆角和控件外观。与第 3 项的数值建议不同时（例如圆角），以本项为准；Ant 的默认值达不到 WCAG 时按 WCAG 调整，TIA 已经调整过的照用（附录 A）。
6. 本规范自定义的数值和模式，标注"项目规则"。

WCAG 2.2 AA 是必须达到的底线；Microsoft 的无障碍指南在它之上另有要求：高对比主题、访问键、F6 区域切换、焦点视觉。EN 301 549 或 Section 508 认为对非 Web 软件不适用的准则（2.4.1、2.4.5、3.2.3、3.2.4，以及 EN 301 549 对 2.4.2 的处理），用等价的 SHOULD 规则代替，见第 15 节。

### 0.2 用词
- **MUST / MUST NOT**：必须遵守，不满足的代码不合并。确需偏离时，先在附录 B 登记理由，经项目负责人同意。
- **SHOULD**：默认遵守；不遵守时在 PR 中说明理由。
- **MAY**：可选。
- 每条规则后写**理由**（一句话）和**依据**：Microsoft 或 W3C 原文的链接与小节，或者"项目规则"加上 ADR、设计文档的出处。Microsoft 的原文只转述，不整段引用。链接取自 TIA 的规范（TIA 于 2026-10-11 核对）。
- "WPF："说明在 WPF 中怎样落实。
- 尺寸单位 epx：有效像素，即 WPF 的设备无关单位，100% 缩放时等于物理像素。

### 0.3 规则编号
`PRI` 原则，`LAY` 布局，`TYP` 字体，`COL` 颜色与主题，`ICO` 图标，`NAV` 导航与页面，`CMD` 命令，`CTL` 控件，`DLG` 对话框与确认，`APR` 审批（项目规则），`STS` 状态与通知，`ERR` 错误与空状态，`KEY` 键盘与焦点，`A11Y` 可测的无障碍要求，`LOC` 本地化，`TXT` 界面文字。编号在本文内按顺序分配，与 TIA 的同名编号不一定是同一条规则；引用 TIA 的规则时写"TIA 的 APR-04"。

### 0.4 不收录的 TIA 内容
- **AI 控制界面**（TIA 的 11.2 节、"AI 预填"、AI 可定位的页面白名单、AI 切页后的输入守卫）：EplanMcp 没有让 AI 操作 Workbench 界面的通道（M5 设计第 2 节的非目标），这些规则不适用；将来要加这样的通道，须另写 ADR。
- **TIA 的页面**：工程操作、程序块、VCI、版本控制、AI 调用、环境体检、日志，以及它的快捷键表（本项目的见 KEY-07）。
- **关闭审批、断开共享会话**（TIA 的 APR-05、APR-09）：本项目的 Workbench 不能关闭审批，也没有共享会话（APR-16）。
- TIA 的 `--mock` 离线演示和它 4.0 版之前的整改清单。

---

## 1. 原则

- **PRI-01 · MUST** 人做最后一步：批准、拒绝、确认解除写入暂停、写入客户端配置，只能由人在看得见、读得清、能用键盘操作的界面上完成；界面不预选答案，也不替人回答。理由：Workbench 是审批渠道，回答必须是人的独立操作。依据：项目规则（ADR-0011 的决策驱动因素，ADR-0014 第 6 条）。
- **PRI-02 · MUST** 行为与 Windows 一致：窗口、键盘、对话框、焦点和主题遵循 Windows 的约定，不自创交互。理由：工程师在 EPLAN 和 Workbench 之间来回切换，熟悉的行为减少误操作。依据：[Navigation basics › Principles of good navigation（Consistency）](https://learn.microsoft.com/en-us/windows/apps/design/basics/navigation-basics#principles-of-good-navigation)。
- **PRI-03 · MUST** 无障碍是合并条件：键盘、读屏、高对比、缩放和 WCAG 2.2 AA（第 15 节）都在评审和测试中检查。理由：审批界面若不是每个人都能可靠地操作，审批这道防线就会失效。依据：[Accessibility checklist](https://learn.microsoft.com/en-us/windows/apps/design/accessibility/accessibility-checklist)。
- **PRI-04 · MUST** 设计值只来自 token：颜色、字号、间距、圆角和尺寸都引用资源字典中的 token，视图中不写字面值。理由：主题切换（含高对比）和缩放只作用于 token。依据：[Contrast themes › Hard-coded colors](https://learn.microsoft.com/en-us/windows/apps/design/accessibility/high-contrast-themes#hard-coded-colors)；[.NET Accessibility Best Practices › Visual UI design](https://learn.microsoft.com/en-us/dotnet/framework/ui-automation/accessibility-best-practices#visual-ui-design)。
- **PRI-05 · MUST** 信息不只靠颜色表达。理由：对色觉差异的用户，以及在高对比主题下，颜色的含义会丢失。依据：[Accessibility checklist › 4](https://learn.microsoft.com/en-us/windows/apps/design/accessibility/accessibility-checklist)；[Color in Windows › Usability](https://learn.microsoft.com/en-us/windows/apps/design/signature-experiences/color#usability)。
- **PRI-06 · MUST** 文字都来自文字表：看得见的和读屏能读出的文字（含 UIA 名称、提示、通知）都来自（键，英文，中文）文字表（第 16 节）。理由：中英文要能完整切换。依据：[Expose basic accessibility information › Accessible names and localization](https://learn.microsoft.com/en-us/windows/apps/design/accessibility/basic-accessibility-information#accessible-names-and-localization)。
- **PRI-07 · MUST** 不打断 EPLAN 中的工作：除了工程师自己的操作，任何事件都不激活 Workbench 的窗口、不把它置顶、不抢键盘焦点（APR-14）。理由：工程师可能正在 EPLAN 中打字，按键不能落到 Workbench 上。依据：项目规则（M5 设计 4.4 的"提醒"，ADR-0014 第 6 条）。
- **PRI-08 · MUST** 项目数据不可信：来自项目、客户端和审计记录的文字一律作为纯文本显示，不生成界面元素，不当作说明（CTL-12、APR-08）。理由：项目中的文字可能冒充界面说明来误导工程师（威胁 T20）。依据：项目规则（SEC-23）。
- **PRI-09 · SHOULD** 少打扰：确认对话框只用于不可撤销、后果重大的操作；状态变化优先用页内提示或状态栏显示。理由：确认太多，人就会不看就点。依据：[Commanding basics › When to confirm or undo actions](https://learn.microsoft.com/en-us/windows/apps/design/basics/commanding-basics#when-to-confirm-or-undo-actions)。

---

## 2. 在 WPF 中的落实

Microsoft 的新版指南以 WinUI 控件举例。Workbench 用 WPF（ADR-0017），对应的做法如下：

| 概念 | Workbench 的做法 | 规则 |
|---|---|---|
| 控件外观 | 原生 WPF 控件套 TIA 主题的模板（`Primer.xaml`）；不用 WPF 的 Fluent 主题 | CTL-01 |
| ContentDialog | `WorkbenchDialog`：以主窗口为 Owner 的模态窗口 | DLG-01 |
| InfoBar | Banner（Ant 的 Alert：严重级别图标、标题、正文、操作、关闭） | STS-06 |
| NavigationView | Sider 中的菜单项，作为一组单选项 | NAV-04 |
| ToggleSwitch | `ToggleButton` 加开关模板（UIA 的 Toggle 模式） | CMD-08 |
| ProgressRing | `ProgressBar` 的 `IsIndeterminate` | STS-03 |
| 焦点视觉 | 全局的 `FocusVisualStyle`（`SystemParameters.FocusVisualStyleKey`） | KEY-03 |
| 高对比主题字典 | `SystemParameters.HighContrast` 为真时，换成映射到 `SystemColors` 的调色板 | COL-02 |
| 访问键 | `_` 助记符和 `AccessText`，模板中 `RecognizesAccessKey="True"` | KEY-06 |
| 快捷键 | `KeyBinding` 加 `AutomationProperties.AcceleratorKey` | CMD-05 |
| 标题级别、对话框 | `AutomationProperties.HeadingLevel`、`AutomationProperties.IsDialog` | A11Y-10、DLG-01 |
| Live region | `AutomationProperties.LiveSetting` 加 `RaiseAutomationEvent(AutomationEvents.LiveRegionChanged)` | STS-02 |
| 自定义标题栏 | `WindowChrome` 加自绘的窗口按钮 | LAY-05 |
| 托盘与通知 | WinForms 的 `NotifyIcon`（ADR-0017 第 1 条）；系统通知的做法由 spike S16 确定 | STS-09 |
| 每显示器 DPI | 在应用清单中声明 Per-Monitor V2 | LAY-04 |
| 文字缩放 | WPF 不自动跟随 Windows 的"文本大小"，要读取系统的缩放比例并缩放字号 token（TIA 的做法，S18 核对） | A11Y-09 |

---

## 3. 布局与间距

### 3.1 外框（项目规则）
外框沿用 TIA 的 Ant 布局：
- **Sider**（左侧栏）216 epx：品牌（"EplanMcp Workbench"和版本号）、页面菜单（第 7 节）、"设置"按钮、收起按钮。窗口窄于 992 epx 时收为 56 epx 的图标栏；收起按钮可以手动改变它，越过断点后恢复自动。
- **Header**（顶栏）56 epx：面包屑（"Workbench / 页名"，次要文字色，不标为标题）、状态栏、窗口按钮。状态栏是 Header 右侧的状态区（第 12 节：审批管道的状态、待处理数、语言）：TIA 的 Ant 布局把状态栏并进了 Header，本项目相同。
- **主体**：四周 24 epx 边距，其中是页面骨架（3.4 节）。
- **抽屉**：从右侧打开，背后有遮罩：设置 400 epx，审计记录详情 520 epx。
- **toast**：右下角，宽 320 epx。
- **对话框**：居中，宽 420–548 epx。

Sider 和 Header 的高度可以按 LAY-08 随字号增长。

### 3.2 窗口与断点
- **LAY-01 · MUST** 窗口的最小尺寸不超过 1024 × 640 epx，在这个尺寸下所有功能可用（允许滚动）。理由：要放得进 1366 × 768（100%）和 1920 × 1080（150%）屏幕的工作区；WCAG 1.4.4、1.4.10 也依赖这一点。依据：[Screen sizes and breakpoints › Breakpoints](https://learn.microsoft.com/en-us/windows/apps/design/layout/screen-sizes-and-breakpoints-for-responsive-design#breakpoints)；[.NET Accessibility Best Practices › Visual UI design](https://learn.microsoft.com/en-us/dotnet/framework/ui-automation/accessibility-best-practices#visual-ui-design)。
- **LAY-02 · SHOULD** 最小宽度不超过 960 epx，使窗口能贴靠到 1920 宽屏幕的一半，与 EPLAN 并排。理由：工程师边看 EPLAN 中的项目边回答审批。依据：[Support snap layouts for desktop apps on Windows 11](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/ui/apply-snap-layout-menu)。
- **LAY-03 · MUST** 响应式：主体放不下"内容 + 右侧栏"两列时，右侧栏移到内容下方；审批队列放不下"列表 + 详情"时，详情移到列表下方（APR-06）。数据表格是二维内容，允许横向滚动。理由：固定宽度的侧栏会在窄窗口中挤掉主内容。依据：[Screen sizes and breakpoints](https://learn.microsoft.com/en-us/windows/apps/design/layout/screen-sizes-and-breakpoints-for-responsive-design#breakpoints)；[WCAG 2.2 › 1.4.10 Reflow](https://www.w3.org/TR/WCAG22/#reflow)。
- **LAY-04 · MUST** 声明每显示器 DPI 感知（Per-Monitor V2），在 100–200% 缩放和跨屏拖动（混合 DPI）下不模糊、不截断。理由：笔记本、外接显示器和虚拟机的缩放各不相同。依据：[High DPI Desktop Application Development on Windows](https://learn.microsoft.com/en-us/windows/win32/hidpi/high-dpi-desktop-application-development-on-windows#dpi-awareness-mode)。
- **LAY-05 · SHOULD** 自绘的标题栏保留系统行为：空白处可以拖动，右键弹出系统菜单，双击最大化或还原，鼠标停在最大化按钮上时显示贴靠布局。理由：用户依赖这些系统级操作。依据：[Windows app title bar › Standard design](https://learn.microsoft.com/en-us/windows/apps/design/basics/titlebar-design#standard-design)；[Support snap layouts](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/ui/apply-snap-layout-menu)。

### 3.3 网格、间距与尺寸
- **LAY-06 · MUST** 尺寸、外边距、内边距和位置取 4 epx 的倍数（文字本身除外）。理由：125%、150%、175% 缩放下能落在整像素上。依据：[Screen sizes and breakpoints › Multiples of four](https://learn.microsoft.com/en-us/windows/apps/design/layout/screen-sizes-and-breakpoints-for-responsive-design#multiples-of-four)。
- **LAY-07 · SHOULD** 间距用附录 A.1 的 token：按钮之间 8，控件与上方的标签 8，控件与旁边的标签 12，卡片之间 12，卡片内边距 16，页面边距 24。理由：一致的间距本身就表达了分组。依据：[Content layout and spacing › Spacing and gutters](https://learn.microsoft.com/en-us/windows/apps/design/basics/content-basics#spacing-and-gutters)。
- **LAY-08 · MUST** 放文字的行和容器不用固定高度，用 `Auto`、`MinHeight` 或 `*`。理由：英文译文更长、字号放大时，固定高度会截断文字。依据：[Globalization › Guidelines for globalization](https://learn.microsoft.com/en-us/windows/apps/design/globalizing/globalizing-portal#guidelines-for-globalization)；[Accessible text requirements › Text font size and scale](https://learn.microsoft.com/en-us/windows/apps/design/accessibility/accessible-text-requirements#text-font-size-and-scale)。
- **LAY-09 · SHOULD** 文字左对齐；只有空状态和图标下的说明居中。理由：左对齐给出稳定的阅读起点。依据：[Typography › Typography best practices in Windows 11](https://learn.microsoft.com/en-us/windows/apps/design/signature-experiences/typography#typography-best-practices-in-windows-11)。
- **LAY-10 · SHOULD** 圆角按 Ant（附录 A.2）：控件 6，小号控件和标签 4，卡片、抽屉、对话框、toast 和弹出层 8，相接的直边 0。理由：与 TIA 的观感一致，圆角的层级表达元素的层次。依据：项目规则（ADR-0017）；[Geometry in Windows 11 › Rounded corners](https://learn.microsoft.com/en-us/windows/apps/design/signature-experiences/geometry#rounded-corners)。
- **LAY-11 · SHOULD** 浮层与背景可以区分：常规主题下用边框，弹出层另加三层半透明阴影（不用 `Effect`）；高对比主题下去掉阴影，浮层用 2 epx 边框。理由：浮层的边界在每个主题中都要看得出。依据：[Contrast themes › Borders](https://learn.microsoft.com/en-us/windows/apps/design/accessibility/high-contrast-themes#borders)。
- **LAY-12 · MUST** 标准控件高 32 epx；紧凑的行内按钮和标签不低于 24 epx（CTL-11）；表格和列表的行不低于 36 epx。理由：与 Ant 和 Fluent 的标准尺寸一致，并满足 WCAG 2.5.8。依据：[Buttons › Recommendations](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/buttons#recommendations)；[WCAG 2.2 › 2.5.8 Target Size (Minimum)](https://www.w3.org/TR/WCAG22/#target-size-minimum)。

### 3.4 页面骨架（项目规则）
每个页面自上而下使用同一个骨架：

1. **页头卡片**（Ant 的 PageHeader）：页面标题（Title 层级，标题级别 1，可以获得焦点但不是 Tab 停靠点），一句说明或计数（Caption，次要文字色），右侧是页面级命令，最多一个主要按钮。
2. **主体**：卡片或表格，间距 12。
3. **右侧栏**（可选）：宽 280–320 epx，放与本页相关的上下文信息；窄窗口中按 LAY-03 移到下方。
4. **空状态、加载、错误**：占据主体区域（ERR-05、ERR-06）。

---

## 4. 字体

- **TYP-01 · MUST** 界面文字和等宽文字都用 Maple Mono CN（SIL OFL 1.1，官方发布包 `MapleMono-CN`），由 `Ui.Font` 和 `Ui.FontMono` 两个 token 引用（值相同，保留两个是为了以后可以分开改）。
  - 字体文件随程序放在 `app\workbench\` 中，只由 Workbench 自己加载（`FontFamily` 指向程序目录中的文件），不安装到系统；只带类型层级用到的字重（Regular、SemiBold），不修改、不裁剪字体文件；OFL 许可证文本随安装包附带。
  - `Ui.Font` 加载失败时回退到 Segoe UI Variable、Microsoft YaHei UI；`Ui.FontMono` 先回退到 Cascadia Mono、Consolas，中文再回退到 Microsoft YaHei UI。Segoe UI Variable 在 WPF 中的表现、每个字重的体积和从程序目录加载的效果由 S18 核对。
  - 这是相对 TIA 字体的偏差（附录 B）。

  理由：项目负责人的决定，界面与项目的其他产物用同一种字体。依据：项目规则（ADR-0017 第 4 条，M5 设计 Q2）；[Typography in Windows › Fonts for non-Latin languages](https://learn.microsoft.com/en-us/windows/apps/design/signature-experiences/typography#languages)。
- **TYP-02 · MUST** 只用下表的类型层级。取值来自 TIA 主题中的 Ant token（附录 A.3）。理由：清楚的层级便于扫读；token 是文字缩放的入口。依据：[Typography in Windows › Type ramp](https://learn.microsoft.com/en-us/windows/apps/design/signature-experiences/typography#type-ramp)；项目规则（ADR-0017）。

  | 层级 | 字号 / 行高（epx） | 字重 | 用途 |
  |---|---|---|---|
  | Caption | 12 / 20 | Regular | 状态栏、表头、时间戳、元数据、计数徽章 |
  | Body | 14 / 22 | Regular | 正文、控件文字、对话框正文、说明 |
  | Body Strong | 14 / 22 | SemiBold | 列表项名称、表单分节、需要强调的值 |
  | Section | 16 / 24 | SemiBold | 卡片、抽屉、对话框的标题，审批问题的标题 |
  | Title | 20 / 28 | SemiBold | 页面标题 |
  | Code | 12 / 20 | Regular（`Ui.FontMono`） | 路径、工具名、`reason` 错误码、请求 ID、哈希、命令行、配置片段 |

  表格、审计列表和审批卡片的元数据 **MAY** 用 Caption 作正文，其他地方用 Body。
- **TYP-03 · MUST NOT** 在视图的 XAML 或代码中写字面的 `FontSize`、`LineHeight`；只引用字号 token 或基于 token 的样式。理由：散落的字号无法统一缩放，也没法评审。依据：[Typography in Windows › Type ramp](https://learn.microsoft.com/en-us/windows/apps/design/signature-experiences/typography#type-ramp)。
- **TYP-04 · MUST** Regular 文字不小于 12 epx；**SHOULD** SemiBold 文字不小于 14 epx。理由：更小的字号和字重在中文中难以辨认。依据：[Typography › Typography best practices in Windows 11（Minimum values）](https://learn.microsoft.com/en-us/windows/apps/design/signature-experiences/typography#typography-best-practices-in-windows-11)。
- **TYP-05 · MUST** 英文界面用句首大写（sentence case），不用全大写表达强调或作表头；不用 Bold 和斜体，强调用 SemiBold。理由：全大写更难读，斜体对读写障碍的用户不友好。依据：[Typography › Typography best practices in Windows 11](https://learn.microsoft.com/en-us/windows/apps/design/signature-experiences/typography#typography-best-practices-in-windows-11)。
- **TYP-06 · MUST** 可以用省略号截断，但被截断的全文必须能用键盘取得：可聚焦的单元格、详情抽屉，或包含全文的 UIA 名称；不可聚焦的 `TextBlock` 上的 ToolTip 不能是唯一的途径。理由：键盘和读屏用户无法悬停。依据：[Typography › Clipping and ellipses](https://learn.microsoft.com/en-us/windows/apps/design/signature-experiences/typography#clipping-and-ellipses)；[Keyboard accessibility](https://learn.microsoft.com/en-us/windows/apps/design/accessibility/keyboard-accessibility#keyboard-alternatives-to-pointer-actions-and-events-for-specific-control-elements)。
- **TYP-07 · SHOULD** 段落文字（说明、对话框正文）每行约 50–60 个拉丁字符（约 25–35 个汉字），对话框的宽度据此设定。理由：行太长难读。依据：[Typography › Character count](https://learn.microsoft.com/en-us/windows/apps/design/signature-experiences/typography#character-count)。
- **TYP-08 · MUST** 技术标识原样显示、不翻译，用 Code 层级：MCP 工具名（`eplan_page_create`）、错误的 `reason`、路径、请求 ID、哈希、命令行；项目中的页名（`=AP+ST1/3`）、设备标识（`-K1`）也原样显示。理由：工程师要据此对照日志、审计、文档和 EPLAN。依据：[Writing style › Style conventions](https://learn.microsoft.com/en-us/windows/apps/design/style/writing-style#style-conventions)；项目规则。
- **TYP-09 · SHOULD** 根元素的 `FrameworkElement.Language` 跟随界面语言（zh-CN 或 en-US），切换语言时更新。理由：WPF 据此选择中日韩字形、断行和默认格式。依据：[Globalization › Guidelines for globalization](https://learn.microsoft.com/en-us/windows/apps/design/globalizing/globalizing-portal#guidelines-for-globalization)。
- **TYP-10 · MUST** 按等宽字体的实际宽度布局：Maple Mono CN 的拉丁字母比 Ant 布局所设想的比例字体宽，按它的设计每个汉字占两个拉丁字符的宽度。按钮用 `MinWidth` 而不是固定 `Width`；列宽按内容或比例分配；按钮、审批问题的标题、状态文字在两种语言、最小窗口下不截断（LOC-04）。实际宽度由 S18 记录。理由：Ant 的尺寸按比例字体设计，照搬会截断文字。依据：项目规则（ADR-0017 后果）；[Globalization › Guidelines for globalization](https://learn.microsoft.com/en-us/windows/apps/design/globalizing/globalizing-portal#guidelines-for-globalization)。

---

## 5. 颜色与主题

视觉按 TIA 主题的 Ant Design v5：浅色调色板对应 Ant 的默认算法，深色对应暗色算法，高对比用 Windows 的系统色。三套调色板、语义 token、实现键（沿用 TIA 的 `Primer.*`、`Ant.*` 键名，便于合并 TIA 的改动）和每个 token 的最低对比见附录 A.4。WCAG 2.2 AA 和本节的对比要求优先于 Ant 的默认值。

- **COL-01 · MUST** 颜色只通过语义 token 以 `DynamicResource` 引用；十六进制颜色只出现在调色板文件中，由测试扫描。理由：主题切换和高对比只替换调色板。依据：[Contrast themes › Hard-coded colors](https://learn.microsoft.com/en-us/windows/apps/design/accessibility/high-contrast-themes#hard-coded-colors)。
- **COL-02 · MUST** 支持浅色、深色、高对比三套主题。`SystemParameters.HighContrast` 为真时自动启用高对比调色板，优先于设置中选的浅色或深色；运行中切换高对比要立即重绘。WPF：高对比调色板中每个 token 引用 `{DynamicResource {x:Static SystemColors.XxxBrushKey}}`。理由：高对比是 Windows 的系统级无障碍设置。依据：[Contrast themes](https://learn.microsoft.com/en-us/windows/apps/design/accessibility/high-contrast-themes)；[.NET Accessibility Best Practices › Visual UI design](https://learn.microsoft.com/en-us/dotnet/framework/ui-automation/accessibility-best-practices#visual-ui-design)。
- **COL-03 · MUST** 高对比下按系统的配对使用颜色：Window / WindowText 用于面和文字；Highlight / HighlightText 用于选中、悬停、按下和焦点；ButtonFace / ButtonText（WPF 的 `ControlBrushKey` / `ControlTextBrushKey`）用于可交互元素；HotTrack 只用于链接；GrayText 只用于禁用，不用于次要文字。列表项中的文字不写死前景色，继承项容器的前景色。理由：配对错了，文字就看不清。依据：[Contrast themes › Contrast colors](https://learn.microsoft.com/en-us/windows/apps/design/accessibility/high-contrast-themes#contrast-colors)。
- **COL-04 · MUST** 浅色和深色主题中，承载信息的文字（含次要文字、提示、时间戳、占位符、徽章）对比不低于 4.5:1；只有禁用状态豁免。理由：WCAG 1.4.3。依据：[Accessible text requirements › Contrast ratios](https://learn.microsoft.com/en-us/windows/apps/design/accessibility/accessible-text-requirements#contrast-ratios)；[WCAG 2.2 › 1.4.3](https://www.w3.org/TR/WCAG22/#contrast-minimum)。
- **COL-05 · MUST** 非文字的对比不低于 3:1：焦点指示，选中、勾选、按下、开关等状态，以及识别控件所需的边界（输入框、复选框）。理由：WCAG 1.4.11。依据：[WCAG 2.2 › 1.4.11 Non-text Contrast](https://www.w3.org/TR/WCAG22/#non-text-contrast)。
- **COL-06 · MUST** 不只靠颜色传达信息：状态用图标或形状加文字；差异行带"+ / −"前缀；开关、筛选和选中状态除颜色外还有形状、字重或勾选标记；倒计时的警告色另有图标（APR-10）。理由：WCAG 1.4.1。依据：[Color in Windows › Usability](https://learn.microsoft.com/en-us/windows/apps/design/signature-experiences/color#usability)。
- **COL-07 · SHOULD** 主色固定为 TIA 主题的蓝色，不跟随 Windows 的强调色；只用于可交互元素和少量重点。理由：与 TIA 的观感一致（ADR-0017），并在虚拟机和远程桌面中容易辨认。依据：项目规则（ADR-0017）；[Color in Windows › Accent color](https://learn.microsoft.com/en-us/windows/apps/design/signature-experiences/color#accent-color)。
- **COL-08 · MUST** 每种语义状态（成功、警告、错误、信息）都有图标和文字；语义色作按钮底色时，上面的文字对比不低于 4.5:1。理由：状态在每个主题中都要读得出。依据：[InfoBar › Severity](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/infobar#severity)。
- **COL-09 · MUST** 主题"跟随系统 / 浅色 / 深色"的切换立即生效，不重启（TIA 的 `ThemeManager` 替换调色板的做法）；"跟随系统"跟随 Windows 的应用模式。理由：重启会丢掉队列中等待的问题。依据：[Color in Windows › Color modes and themes](https://learn.microsoft.com/en-us/windows/apps/design/signature-experiences/color#color-modes-and-themes)；M5 设计 4.4。
- **COL-10 · SHOULD** 标题栏和窗口按钮随主题、高对比以及窗口的激活状态变化。理由：用户靠标题栏区分活动窗口。依据：[Windows app title bar › Standard design](https://learn.microsoft.com/en-us/windows/apps/design/basics/titlebar-design#standard-design)。
- **COL-11 · MUST NOT** 在样式中用触发器按"是否深色"切换颜色；主题的差异只放在调色板中。高对比下隐藏阴影这类装饰可以用触发器。理由：有了第三套高对比主题，按深浅分支就会出错。依据：[Contrast themes](https://learn.microsoft.com/en-us/windows/apps/design/accessibility/high-contrast-themes)。

---

## 6. 图标

- **ICO-01 · MUST** 图标来自 TIA 主题的 `Themes/Icons.xaml`，以命名几何 `Icon.*` 引用；不在模板中写私用区字符，不用外观近似的 Unicode 字符（✓ ⌄ ⋯ !）代替图标，也不用字体中的图标字形（随包的 `MapleMono-CN` 不含 Nerd Font 图标）。理由：命名的几何在各缩放和高对比下表现一致。依据：[Typography in Windows › Fonts](https://learn.microsoft.com/en-us/windows/apps/design/signature-experiences/typography#fonts)；项目规则。
- **ICO-02 · MUST** 只有图标的按钮设置 `AutomationProperties.Name` 和 ToolTip，文字来自文字表，描述动作（"关闭抽屉"，而不是"X"）。理由：WCAG 1.1.1、4.1.2。依据：[Expose basic accessibility information › Accessible name](https://learn.microsoft.com/en-us/windows/apps/design/accessibility/basic-accessibility-information#accessible-name)。
- **ICO-03 · SHOULD** 按钮以文字为主；图标主要用于状态（严重级别、检查结果）、窗口按钮和收起后的 Sider。理由：短标签旁再配图标只增加噪音。依据：[Buttons › Button text](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/buttons#button-text)；项目规则（同 TIA）。
- **ICO-04 · MUST** 状态图标在形状上可以区分（对勾、感叹号、叉），不只靠圆点的颜色。理由：高对比和色弱时颜色无效。依据：[InfoBar › Severity](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/infobar#severity)。
- **ICO-05 · SHOULD** 自绘的窗口按钮（最小化、最大化、还原、关闭）与系统的一致，最大化后换成还原图标。理由：与系统标题栏一致。依据：[Windows app title bar › Caption controls](https://learn.microsoft.com/en-us/windows/apps/design/basics/titlebar-design#standard-design)。

---

## 7. 导航与页面

Workbench 有四个页面和一个设置抽屉（M5 设计 4.4）：

| 页面 | 内容 | 快捷键 |
|---|---|---|
| 审批队列 | 等待中的问题，以及本次运行中已回答的记录（第 11 节）；启动后的默认页面 | Ctrl+1 |
| 审计 | 只读：按日期、工具、项目、审批决定、执行结果筛选；记录详情抽屉；校验完整性；打开所在文件夹 | Ctrl+2 |
| 状态 | `eplanmcp doctor --format json` 的结果分组显示；打开时和"刷新"时各运行一次，不自动轮询 | Ctrl+3 |
| 客户端 | 客户端配置向导：Claude Code（推荐插件，给出两条命令）、Claude 桌面版（预览差异后写入）、其他客户端（可以复制的配置片段） | Ctrl+4 |
| 设置（抽屉） | 语言、主题、登录时启动；`config.json` 只读显示 | — |

- **NAV-01 · MUST** 页面是扁平的一级结构，不超过 7 个，不设二级页签。理由：少于 8 个同级页面时扁平结构最容易找到。依据：[Navigation basics › Use the right structure](https://learn.microsoft.com/en-us/windows/apps/design/basics/navigation-basics#use-the-right-structure)。
- **NAV-02 · MUST** 所有导航（菜单、快捷键、托盘、系统通知、待处理徽章、toast）只经过一个入口方法。理由：焦点规则（NAV-07）和审批的防误触（APR-04）只在一处实现。依据：项目规则（同 TIA）。
- **NAV-03 · MUST** 当前位置始终可见：选中的菜单项有颜色以外的标记（选中条和 SemiBold）；Header 的面包屑和页头标题显示页名；`Window.Title` 为"<页名> – EplanMcp Workbench"。理由：用户和读屏都要知道当前在哪一页。依据：[Navigation basics › Principles of good navigation](https://learn.microsoft.com/en-us/windows/apps/design/basics/navigation-basics#principles-of-good-navigation)；[WCAG 2.2 › 2.4.2 Page Titled](https://www.w3.org/TR/WCAG22/#page-titled)。
- **NAV-04 · MUST** 菜单在 UIA 中是一组可选项：每项有名称、选中状态、序号和总数（`PositionInSet`、`SizeOfSet`）；整组是一个 Tab 停靠点，组内用方向键移动。理由：读屏要能读出"第 1 项，共 4 项，已选中"。依据：[Keyboard interactions › Control group](https://learn.microsoft.com/en-us/windows/apps/develop/input/keyboard-interactions#control-group)。
- **NAV-05 · MUST** 暂时不可用的页面或命令保持可见但禁用，并说明原因（HelpText、ToolTip，例如"找不到 eplanmcp.exe，请重新安装"）；指向它的快捷键给出可见的反馈，不静默无效。理由：用户要知道怎样才能用。依据：[Navigation basics › Principles of good navigation](https://learn.microsoft.com/en-us/windows/apps/design/basics/navigation-basics#principles-of-good-navigation)。
- **NAV-06 · SHOULD** 每页用 3.4 节的页面骨架。理由：同样的结构降低学习成本。依据：[Navigation basics › Consistency](https://learn.microsoft.com/en-us/windows/apps/design/basics/navigation-basics#principles-of-good-navigation)。
- **NAV-07 · MUST** 切页后焦点可预期：点击菜单时焦点留在菜单项上；用快捷键、托盘、通知或徽章切页时，焦点放到页头的页面标题（审批队列放到选中问题的标题，APR-03）；任何导航都不把焦点放到"批准""拒绝"或写入类按钮上；原来的焦点元素被折叠时要恢复焦点，不能丢失。理由：初始焦点不能放在会造成严重后果的元素上。依据：[Keyboard interactions › Initial focus](https://learn.microsoft.com/en-us/windows/apps/develop/input/keyboard-interactions#initial-focus)；项目规则（ADR-0014 第 6 条）。
- **NAV-08 · SHOULD** F6 和 Shift+F6 在主要区域之间循环：Sider → Header → 页面主体 → 右侧栏或审批详情；每个区域的根元素有 `AutomationProperties.Name`。理由：减少逐个 Tab 的次数；代替对软件不适用的 WCAG 2.4.1。依据：[Keyboard accessibility › Keyboard navigation between application panes with F6](https://learn.microsoft.com/en-us/windows/apps/design/accessibility/keyboard-accessibility#keyboard-navigation-between-application-panes-with-f6)。
- **NAV-09 · MUST** 窗口与托盘：再次启动 Workbench 时，把已有的窗口带到前台后退出；关闭窗口时缩到托盘，队列照常工作，关闭按钮的 ToolTip 说明这一点；托盘菜单有"打开 Workbench"和"退出"，退出按 DLG-11。理由：关闭窗口不能让等待中的问题失去渠道。依据：项目规则（M5 设计 4.4"进程"）。
- **NAV-10 · MUST** 只读的页面不写任何东西：审计不提供修改或删除记录的操作，"校验完整性"调用 `eplanmcp audit verify --format json`；`config.json` 只读显示，只提供"用默认程序打开"，Workbench 中没有修改安全配置的控件。理由：审计是记录；安全配置只经配置文件修改。依据：项目规则（M5 设计第 2 节的非目标和 4.4，SEC-02）。

---

## 8. 命令

- **CMD-01 · MUST** 按钮文字是动词（加对象），英文不超过两三个词，中文宜在 6 个汉字以内；不加句号；只有执行前还要用户补充信息时才加省略号。理由：一眼看懂动作。依据：[Buttons › Button text](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/buttons#button-text)；[Writing style › Buttons](https://learn.microsoft.com/en-us/windows/apps/design/style/writing-style#buttons)。
- **CMD-02 · MUST** 每个面（卡片、对话框、抽屉的一节）最多一个主要按钮；破坏性操作不用主要样式，危险样式（实心红色）只用于确认对话框中的提交按钮。审批的两个按钮例外，按 APR-02。理由：主要样式少用，主操作才突出。依据：[Buttons › Recommendations](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/buttons#recommendations)。
- **CMD-03 · SHOULD** 位置：页面级命令在页头右侧；卡片命令在卡片底部；对话框的按钮在底部右对齐（DLG-05）。理由：位置一致便于寻找。依据：[Buttons › Recommendations](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/buttons#recommendations)；[Dialog controls › General guidelines](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/dialogs-and-flyouts/dialogs#general-guidelines)。
- **CMD-04 · MUST** 因状态而不可用的命令，原因不明显时说明原因（HelpText、ToolTip）；不隐藏用户预期存在的命令。理由：禁用而不说明会让人卡住。依据：[Commanding basics › Provide command feedback](https://learn.microsoft.com/en-us/windows/apps/design/basics/commanding-basics#provide-command-feedback)。
- **CMD-05 · MUST** 快捷键写在 ToolTip（"刷新 (F5)"）和 `AutomationProperties.AcceleratorKey` 中，与 Windows 的常用快捷键一致；代码、ToolTip 与 KEY-07 的表格一致。理由：快捷键要能被发现，也要能被读屏告知。依据：[Keyboard interactions › Accelerators](https://learn.microsoft.com/en-us/windows/apps/develop/input/keyboard-interactions#accelerators)。
- **CMD-06 · MUST NOT** 为回答审批的按钮设置快捷键或访问键（APR-02）。理由：防止盲批和误触。依据：项目规则（ADR-0011 第 4 条，SEC-24）。
- **CMD-07 · MUST** 开关式按钮和筛选按钮在 UIA 中暴露按下状态（Toggle 模式），视觉上用形状或勾选标记加不低于 3:1 的对比表示。理由：WCAG 4.1.2、1.4.11。依据：[Expose basic accessibility information › Role and value](https://learn.microsoft.com/en-us/windows/apps/design/accessibility/basic-accessibility-information#role-and-value)。
- **CMD-08 · SHOULD** 立即生效的二元设置（登录时启动）用开关；复选框用于多选或要再按"保存"才生效的场合；同一抽屉中的二元设置用同一种控件。理由：开关明确表达"现在开还是关"。依据：[Toggle switches › Choosing between toggle switch and check box](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/toggles#choosing-between-toggle-switch-and-check-box)。
- **CMD-09 · SHOULD** 同一功能出现在多处时（例如状态页和审计页都有"打开日志文件夹"），用同一文字、同一命令和同一快捷键。理由：WCAG 3.2.4 在软件中的等价要求。依据：[WCAG 2.2 › 3.2.4 Consistent Identification](https://www.w3.org/TR/WCAG22/#consistent-identification)。
- **CMD-10 · MUST** 上下文菜单只是加速入口：其中每一项在别处都有入口；上下文菜单可以用 Shift+F10 或菜单键打开。审批的回答不放进上下文菜单。理由：上下文菜单对键盘用户不可发现。依据：[Nested UI in list items › Options for secondary actions](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/nested-ui#options-for-secondary-actions)。

---

## 9. 控件

- **CTL-01 · MUST** 用原生 WPF 控件并套主题模板，不为了外观派生新控件。理由：原生控件自带键盘行为和 AutomationPeer。依据：[.NET Accessibility Best Practices › Programmatic access](https://learn.microsoft.com/en-us/dotnet/framework/ui-automation/accessibility-best-practices#programmatic-access)。
- **CTL-02 · MUST** 每个模板保留键盘焦点视觉（KEY-03）、禁用外观（不只靠降低不透明度）、悬停和按下的反馈，以及高对比外观。理由：模板改得太多会损害无障碍。依据：[Visual feedback › Recommendations](https://learn.microsoft.com/en-us/windows/apps/develop/input/guidelines-for-visualfeedback#recommendations)。
- **CTL-03 · MUST** 文本框、下拉框、日期选择框都有可见的标签（在上方，间距 8），并用 `AutomationProperties.LabeledBy` 或 `Label.Target` 关联；占位符不代替标签。理由：WCAG 1.3.1、3.3.2、4.1.2。依据：[Expose basic accessibility information › Labels and LabeledBy](https://learn.microsoft.com/en-us/windows/apps/design/accessibility/basic-accessibility-information#labels-and-labeledby)。
- **CTL-04 · MUST** 列表：整个列表一个 Tab 停靠点，方向键在项之间移动，Enter 执行项的主要操作（打开审计记录详情；审批队列中只选中问题，APR-06）；尽量不在项中嵌按钮。理由：项中的按钮会造成两个停靠点和缺失的名称。依据：[Nested UI in list items](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/nested-ui#nested-ui-handling)；[Keyboard interactions › Enter key](https://learn.microsoft.com/en-us/windows/apps/develop/input/keyboard-interactions#enter-key)。
- **CTL-05 · MUST** 列表项在 UIA 中有可读的名称，由 `ItemContainerStyle` 绑定来自文字表的格式串（例如"eplan_page_delete，破坏性，项目 Demo，还剩 95 秒"），不能是 CLR 类型的全名。理由：WPF 默认用数据项的 `ToString()` 作项名称。依据：[Expose basic accessibility information › Accessible names from dynamic data](https://learn.microsoft.com/en-us/windows/apps/design/accessibility/basic-accessibility-information#accessible-names-from-dynamic-data)。
- **CTL-06 · SHOULD** 有列头的数据表（审计列表）用套了主题的 `DataGrid` 或 `ListView` 加 `GridView`，让 UIA 暴露表格结构；自制表格的项名称要包含列名和值。理由：WCAG 1.3.1 的列头关系。依据：[Collections and lists](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/lists)。
- **CTL-07 · MUST** 复选框有内容或名称；要按钮外观时用 `ToggleButton`，不把复选框套成按钮；只读的状态用文字显示，不用禁用的复选框。理由：外观与角色不一致会误导读屏和视觉用户。依据：[Expose basic accessibility information › Role and value](https://learn.microsoft.com/en-us/windows/apps/design/accessibility/basic-accessibility-information#role-and-value)。
- **CTL-08 · SHOULD** Expander（例如状态页的检查分组）的标题只做展开和折叠，其他按钮放在标题之外。理由：嵌套的可交互元素难以用键盘操作。依据：[Nested UI in list items](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/nested-ui#nested-ui-handling)。
- **CTL-09 · MUST** `DataGrid`、`ToolTip`、`ContextMenu`、`ScrollBar`、`DatePicker` 等默认控件都套用调色板 token，深色和高对比主题中不出现默认的白底。理由：同一界面的视觉一致，高对比必须完整。依据：[Contrast themes › Best practices](https://learn.microsoft.com/en-us/windows/apps/design/accessibility/high-contrast-themes#best-practices)。
- **CTL-10 · SHOULD** 文字按钮的最小宽度约 120 epx（短标签），文字不超过 26 个拉丁字符；更长时换行或改布局。理由：太窄难点，太长难读。依据：[Buttons › Button text](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/buttons#button-text)。
- **CTL-11 · MUST** 指针目标不小于 24 × 24 epx（图标按钮建议 32 × 32），或满足 WCAG 2.5.8 的间距例外。理由：WCAG 2.5.8。依据：[WCAG 2.2 › 2.5.8 Target Size (Minimum)](https://www.w3.org/TR/WCAG22/#target-size-minimum)。
- **CTL-12 · MUST** 来自项目、客户端或审计记录的文字只用 `TextBlock` 或只读 `TextBox` 显示为纯文本：不解析为超链接、标记或格式串，不用作资源键，不生成任何界面元素；显示前按 APR-08 的规则清洗。审计页同样适用。理由：这些文字可能伪装成界面说明（T20）。依据：项目规则（SEC-23；审计页为本规范的补充）。
- **CTL-13 · SHOULD** 要复制的技术值（路径、请求 ID、插件安装命令、配置片段）用可选中的只读文本框，或旁边有"复制"按钮，复制后以 live region 告知"已复制"。理由：工程师要把命令贴到终端、把 ID 贴到问题报告中。依据：项目规则（M5 设计 4.4 和 Q7）。

---

## 10. 对话框与确认

- **DLG-01 · MUST** 所有模态消息用同一个 `WorkbenchDialog`：以主窗口为 Owner 并居中，`Title` 等于对话框标题；窗口内的模态层在根元素上设置 `AutomationProperties.IsDialog="True"` 和名称。理由：一致的外观和行为，读屏能认出对话框。依据：[Dialog controls › General guidelines](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/dialogs-and-flyouts/dialogs#general-guidelines)；[AutomationProperties.IsDialogProperty](https://learn.microsoft.com/en-us/dotnet/api/system.windows.automation.automationproperties.isdialogproperty)。
- **DLG-02 · MUST** 模态的语义：打开时焦点移到第一个可聚焦的内容或安全按钮；Tab 只在对话框内循环；背景不可交互；Esc 等同安全按钮；关闭后焦点回到触发它的元素。理由：WCAG 2.4.3、2.4.11。依据：[Keyboard interactions › Esc key](https://learn.microsoft.com/en-us/windows/apps/develop/input/keyboard-interactions#esc-key)；[Dialog controls › CloseButton](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/dialogs-and-flyouts/dialogs#closebutton)。
- **DLG-03 · MUST** 每个对话框都有一个安全、无破坏性的按钮（取消、关闭），设为 `IsCancel`。理由：用户必须能放心退出。依据：[Dialog controls › Provide a safe action](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/dialogs-and-flyouts/dialogs#provide-a-safe-action)。
- **DLG-04 · MUST** 有风险的确认：默认按钮（Enter）是安全选项；提交按钮用具体的动词（"退出""写入配置""移除配置"），不用"确定"。理由：有风险的操作，默认的选择应是不继续。依据：[Confirmations › Guidelines](https://learn.microsoft.com/en-us/windows/win32/uxguide/mess-confirm#guidelines)；[Dialog controls › Confirmation dialogs](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/dialogs-and-flyouts/dialogs#confirmation-dialogs-okcancel)。
- **DLG-05 · SHOULD** 按钮在底部右对齐，按 Ant 的顺序：安全按钮在左，提交按钮在最右。理由：与 TIA 的实际观感一致（TIA 偏差清单 C.0）；Windows 指南的顺序相反，记入附录 B。依据：项目规则（ADR-0017）。
- **DLG-06 · MUST** 标题是简短的问题或指令（"退出 Workbench？"），正文补充后果和细节，不换个说法重复标题。理由：读完标题就应能作答。依据：[Writing style › Dialogs](https://learn.microsoft.com/en-us/windows/apps/design/style/writing-style#dialogs)。
- **DLG-07 · MUST** 风险确认和错误对话框显示对应的图标（警告、错误）和文字；正文用常规文字色，不把整段染红。理由：严重程度不能只靠颜色。依据：[Confirmations › Guidelines](https://learn.microsoft.com/en-us/windows/win32/uxguide/mess-confirm#guidelines)。
- **DLG-08 · MUST NOT** 用对话框报告某个字段的验证错误（改为页内显示，ERR-03）。理由：与上下文有关的错误应在原位显示。依据：[Dialog controls › General guidelines](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/dialogs-and-flyouts/dialogs#general-guidelines)。
- **DLG-09 · SHOULD** 确认只用于不可撤销或后果重大的操作：有等待中的问题时退出（DLG-11）、写入或移除客户端配置（DLG-12）。审批本身不再加确认。理由：不必要的确认会被习惯性地点掉。依据：[Commanding basics › When to confirm or undo actions](https://learn.microsoft.com/en-us/windows/apps/design/basics/commanding-basics#when-to-confirm-or-undo-actions)。
- **DLG-10 · MUST** 带遮罩的抽屉（设置、审计记录详情）按模态窗格处理：打开时焦点移到标题或第一个控件，Tab 在内循环，Esc 关闭，关闭后焦点回到触发按钮。理由：键盘用户不能操作遮罩后面看不见的控件。依据：[Keyboard interactions › Esc key](https://learn.microsoft.com/en-us/windows/apps/develop/input/keyboard-interactions#esc-key)；[WCAG 2.2 › 2.4.11 Focus Not Obscured (Minimum)](https://www.w3.org/TR/WCAG22/#focus-not-obscured-minimum)。
- **DLG-11 · MUST** 有等待中的问题时从托盘退出，先确认：标题"退出 Workbench？"，正文写明有几个问题在等待、退出后它们按"已取消"返回、不执行任何修改，提交按钮"退出"，"取消"为默认并响应 Esc。理由：退出会让这些调用失败。依据：项目规则（M5 设计 4.4"进程"，ADR-0014 第 4 条）。
- **DLG-12 · MUST** 写入客户端配置：先显示 `client-config --dry-run` 的差异（纯文本，行首"+ / −"），再由工程师按"写入配置"确认，正文写明原文件先备份到哪里；移除配置同样确认。理由：客户端配置是工程师自己的文件。依据：项目规则（M5 设计 4.4 第 3 项、4.11）。

（第 11 节到附录 B 在后续的 PR 中加入。）

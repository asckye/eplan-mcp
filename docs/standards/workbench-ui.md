# Workbench 界面规范

> 依据：项目负责人的 TIA_Portal_Openness_MCP 项目（下称 TIA）的界面规范 `docs/development/design-spec/workbench-ui.md` v0.2（MIT 许可，按 TIA 的提交 `38ca2883` 阅读）和 [ADR-0017](../adr/0017-workbench-wpf-tia-theme.md)。规则的结构、编号方法和以 WCAG 2.2 AA 为底线的做法沿用 TIA，文字按本项目重写；TIA 特有的内容不收录（0.4 节），有意的不同列在附录 B。
> 适用范围：`EplanMcp.Workbench`（WPF，`net10.0-windows`，[M5 设计](../design/2026-10-20-m5-operability.md) 4.4）的全部界面：主窗口与外框，审批队列、审计、状态、MCP 与客户端四个页面，设置和审计详情两个抽屉，对话框，toast、托盘和系统通知。测试放在 `tests/EplanMcp.Workbench.Tests`。已实现外框、设置抽屉（语言、主题、登录时启动、只读的 `config.json`）、托盘、审批队列页、审计页、审计记录详情抽屉、状态页和 MCP 与客户端页的客户端配置向导，以及虚拟机内的地址、密钥、网络权限、实例选择、服务启停和监听状态；宿主机向导和连接测试随后实现（第 7 节、M5 设计 4.12）。
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
| 随程序的字体 | 以程序目录中 `Fonts\` 为基准 URI 的 `FontFamily`（S18） | TYP-01 |
| 文字缩放 | WPF 不自动跟随 Windows 的"文本大小"，要读取系统的缩放比例并缩放字号 token（TIA 的做法；S18 没有测，实现时核对） | A11Y-09 |

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
- **LAY-04 · MUST** 声明每显示器 DPI 感知（Per-Monitor V2），在 100–200% 缩放和跨屏拖动（混合 DPI）下不模糊、不截断。WPF：要在应用清单中声明，没有清单时 .NET 10 的 WPF 只是系统 DPI 感知（S18）。理由：笔记本、外接显示器和虚拟机的缩放各不相同。依据：[High DPI Desktop Application Development on Windows](https://learn.microsoft.com/en-us/windows/win32/hidpi/high-dpi-desktop-application-development-on-windows#dpi-awareness-mode)。
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
  - 字体文件随程序放在 `app\workbench\Fonts\` 中，只由 Workbench 自己加载，不安装到系统；只带类型层级用到的字重 Regular 和 SemiBold（两个共约 35 MB，S18），不修改、不裁剪字体文件；OFL 许可证文本随安装包附带。
  - WPF：以 `Fonts\` 目录为基准 URI 创建 `FontFamily`（`new FontFamily(new Uri(<程序目录>\Fonts\), "./#Maple Mono CN, …")`），同一家族的各字重自动对应；`pack://siteoforigin` 和只写家族名都不行（S18）。
  - 回退列表必须写出 Microsoft YaHei UI：`Ui.Font` 为 Segoe UI Variable Text（或 Segoe UI）、Microsoft YaHei UI；`Ui.FontMono` 为 Cascadia Mono、Consolas、Microsoft YaHei UI。只写 Segoe UI Variable Text 而语言为 en-US 时，WPF 用日文字形的 Yu Gothic UI 显示中文（S18）。
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
- **TYP-10 · MUST** 按等宽字体的实际宽度布局：Maple Mono CN 的拉丁字母比 Ant 布局所设想的比例字体宽，按它的设计每个汉字占两个拉丁字符的宽度。按钮用 `MinWidth` 而不是固定 `Width`；列宽按内容或比例分配；按钮、审批问题的标题、状态文字在两种语言、最小窗口下不截断，标题放不下时换行（LOC-04）。理由：Ant 的尺寸按比例字体设计，照搬会截断文字：S18 中 TIA 的 480 epx 抽屉放不下一行英文标题，主机名被截断，路径和变更对象都要换行。依据：项目规则（ADR-0017 后果）；[Globalization › Guidelines for globalization](https://learn.microsoft.com/en-us/windows/apps/design/globalizing/globalizing-portal#guidelines-for-globalization)。
- **TYP-11 · MUST** 关闭 Maple Mono 的连字（`Typography.StandardLigatures`、`ContextualLigatures`、`ContextualAlternates` 设为 False），至少在变更清单和 Code 层级的区域中关闭。WPF 中的效果 S18 没有核对，实现时用 `->`、`!=`、`==` 验证。理由：连字会把 `->`、`!=` 显示成一个符号，来自项目的数据必须按它包含的字符显示（T20）。依据：项目规则（S18"字体"，SEC-23）。

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

Workbench 有四个页面和一个设置抽屉（M5 设计 4.4、4.12）：

| 页面 | 内容 | 快捷键 |
|---|---|---|
| 审批队列 | 等待中的问题，以及本次运行中已回答的记录（第 11 节）；启动后的默认页面 | Ctrl+1 |
| 审计 | 只读：按日期、工具、项目、审批决定、执行结果筛选；记录详情抽屉；校验完整性；打开所在文件夹 | Ctrl+2 |
| 状态 | `eplanmcp doctor --format json` 的结果分组显示；打开时和"刷新"时各运行一次，不自动轮询 | Ctrl+3 |
| MCP 与客户端 | 客户端配置向导：M5 设计 Q7 的 11 种客户端以卡片列出，可以多选，副标题显示本机是否检测到；预览差异后写入；Claude Code 推荐插件（给出两条命令）。另有"虚拟机 ↔ 宿主机"的连接设置（ADR-0018）：地址、端口、密钥、网络权限、启动服务、测试连接（M5 设计 4.12） | Ctrl+4 |
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
- **CMD-11 · SHOULD** 会弹出管理员授权（UAC）的命令（虚拟机连接的"网络权限"）在按钮上显示盾牌图标，ToolTip 说明需要管理员授权。理由：工程师要预先知道接下来会出现系统的授权提示。依据：[User Account Control（Win32 UX Guide）](https://learn.microsoft.com/en-us/windows/win32/uxguide/winenv-uac)；项目规则（ADR-0018，SEC-29）。

---

## 9. 控件

- **CTL-01 · MUST** 用原生 WPF 控件并套主题模板，不为了外观派生新控件。理由：原生控件自带键盘行为和 AutomationPeer。依据：[.NET Accessibility Best Practices › Programmatic access](https://learn.microsoft.com/en-us/dotnet/framework/ui-automation/accessibility-best-practices#programmatic-access)。
- **CTL-02 · MUST** 每个模板保留键盘焦点视觉（KEY-03）、禁用外观（不只靠降低不透明度）、悬停和按下的反馈，以及高对比外观。理由：模板改得太多会损害无障碍。依据：[Visual feedback › Recommendations](https://learn.microsoft.com/en-us/windows/apps/develop/input/guidelines-for-visualfeedback#recommendations)。
- **CTL-03 · MUST** 文本框、下拉框、日期选择框都有可见的标签（在上方，间距 8），并用 `AutomationProperties.LabeledBy` 或 `Label.Target` 关联；占位符不代替标签。理由：WCAG 1.3.1、3.3.2、4.1.2。依据：[Expose basic accessibility information › Labels and LabeledBy](https://learn.microsoft.com/en-us/windows/apps/design/accessibility/basic-accessibility-information#labels-and-labeledby)。
- **CTL-04 · MUST** 列表：整个列表一个 Tab 停靠点，方向键在项之间移动，Enter 执行项的主要操作（打开审计记录详情；审批队列中只选中问题，APR-06）；尽量不在项中嵌按钮。理由：项中的按钮会造成两个停靠点和缺失的名称。依据：[Nested UI in list items](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/nested-ui#nested-ui-handling)；[Keyboard interactions › Enter key](https://learn.microsoft.com/en-us/windows/apps/develop/input/keyboard-interactions#enter-key)。
- **CTL-05 · MUST** 列表项在 UIA 中有可读的名称，由 `ItemContainerStyle` 绑定来自文字表的格式串（例如"eplan_page_delete，破坏性，项目 Demo，还剩 95 秒"），不能是 CLR 类型的全名。理由：WPF 默认用数据项的 `ToString()` 作项名称。依据：[Expose basic accessibility information › Accessible names from dynamic data](https://learn.microsoft.com/en-us/windows/apps/design/accessibility/basic-accessibility-information#accessible-names-from-dynamic-data)。
- **CTL-06 · SHOULD** 有列头的数据表（审计列表）用套了主题的 `DataGrid` 或 `ListView` 加 `GridView`，让 UIA 暴露表格结构；自制表格的项名称要包含列名和值。理由：WCAG 1.3.1 的列头关系。依据：[Collections and lists](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/lists)。
- **CTL-07 · MUST** 复选框有内容或名称；要按钮外观时用 `ToggleButton`，不把复选框套成按钮；只读的状态用文字显示，不用禁用的复选框。可以多选的客户端卡片是套了卡片模板的复选框，UIA 名称包含客户端名和"已检测到"或"未检测到"。理由：外观与角色不一致会误导读屏和视觉用户。依据：[Expose basic accessibility information › Role and value](https://learn.microsoft.com/en-us/windows/apps/design/accessibility/basic-accessibility-information#role-and-value)。
- **CTL-08 · SHOULD** Expander（例如状态页的检查分组）的标题只做展开和折叠，其他按钮放在标题之外。理由：嵌套的可交互元素难以用键盘操作。依据：[Nested UI in list items](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/nested-ui#nested-ui-handling)。
- **CTL-09 · MUST** `DataGrid`、`ToolTip`、`ContextMenu`、`ScrollBar`、`DatePicker` 等默认控件都套用调色板 token，深色和高对比主题中不出现默认的白底。理由：同一界面的视觉一致，高对比必须完整。依据：[Contrast themes › Best practices](https://learn.microsoft.com/en-us/windows/apps/design/accessibility/high-contrast-themes#best-practices)。
- **CTL-10 · SHOULD** 文字按钮的最小宽度约 120 epx（短标签），文字不超过 26 个拉丁字符；更长时换行或改布局。理由：太窄难点，太长难读。依据：[Buttons › Button text](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/buttons#button-text)。
- **CTL-11 · MUST** 指针目标不小于 24 × 24 epx（图标按钮建议 32 × 32），或满足 WCAG 2.5.8 的间距例外。理由：WCAG 2.5.8。依据：[WCAG 2.2 › 2.5.8 Target Size (Minimum)](https://www.w3.org/TR/WCAG22/#target-size-minimum)。
- **CTL-12 · MUST** 来自项目、客户端或审计记录的文字只用 `TextBlock` 或只读 `TextBox` 显示为纯文本：不解析为超链接、标记或格式串，不用作资源键，不生成任何界面元素；显示前按 APR-08 的规则清洗。审计页同样适用。理由：这些文字可能伪装成界面说明（T20）。依据：项目规则（SEC-23；审计页为本规范的补充）。
- **CTL-13 · SHOULD** 要复制的技术值（路径、请求 ID、插件安装命令、配置片段）用可选中的只读文本框，或旁边有"复制"按钮，复制后以 live region 告知"已复制"。理由：工程师要把命令贴到终端、把 ID 贴到问题报告中。依据：项目规则（M5 设计 4.4 和 Q7）。
- **CTL-14 · MUST** 虚拟机连接的共享密钥（ADR-0018）默认遮住显示，旁边有"显示"开关和"复制"按钮；密钥不出现在 UIA 名称、HelpText、ToolTip、通知、toast 和日志中；重新生成按 DLG-13 确认。理由：密钥能调用全部工具，宿主机的客户端配置中还是明文（SEC-30）。依据：项目规则（SEC-27、SEC-30）；[WCAG 2.2 › 3.3.8 Accessible Authentication (Minimum)](https://www.w3.org/TR/WCAG22/#accessible-authentication-minimum)（可以复制和粘贴，不必记忆）。

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
- **DLG-09 · SHOULD** 确认只用于不可撤销或后果重大的操作：有等待中的问题时退出（DLG-11）、写入或移除客户端配置（DLG-12）、重新生成密钥（DLG-13）。审批本身不再加确认。理由：不必要的确认会被习惯性地点掉。依据：[Commanding basics › When to confirm or undo actions](https://learn.microsoft.com/en-us/windows/apps/design/basics/commanding-basics#when-to-confirm-or-undo-actions)。
- **DLG-10 · MUST** 带遮罩的抽屉（设置、审计记录详情）按模态窗格处理：打开时焦点移到标题或第一个控件，Tab 在内循环，Esc 关闭，关闭后焦点回到触发按钮。理由：键盘用户不能操作遮罩后面看不见的控件。依据：[Keyboard interactions › Esc key](https://learn.microsoft.com/en-us/windows/apps/develop/input/keyboard-interactions#esc-key)；[WCAG 2.2 › 2.4.11 Focus Not Obscured (Minimum)](https://www.w3.org/TR/WCAG22/#focus-not-obscured-minimum)。
- **DLG-11 · MUST** 有等待中的问题时从托盘退出，先确认：标题"退出 Workbench？"，正文写明有几个问题在等待、退出后它们按"已取消"返回、不执行任何修改；虚拟机连接的服务在运行时，同时写明服务会停止。提交按钮"退出"，"取消"为默认并响应 Esc。理由：退出会让这些调用失败。依据：项目规则（M5 设计 4.4"进程"，ADR-0014 第 4 条，SEC-29）。
- **DLG-12 · MUST** 写入客户端配置：可以选多个客户端，先逐个显示 `client-config --dry-run` 的差异（纯文本，行首"+ / −"），再由工程师按"写入配置"确认，正文写明原文件先备份到哪里；写入后逐个报告结果；移除配置同样确认。Claude 桌面版运行时会改写它的配置文件，写入前请工程师完全退出桌面版；已装 Claude Code 插件时不为 Claude Code 写配置，只推荐插件。理由：客户端配置是工程师自己的文件。依据：项目规则（M5 设计 4.4 第 3 项、4.7、4.11）。
- **DLG-13 · MUST** 重新生成虚拟机连接的密钥（ADR-0018）先确认：标题"重新生成密钥？"，正文写明旧密钥立即失效、宿主机上的客户端配置要用新密钥重新写入；提交按钮"重新生成"，"取消"为默认。理由：重新生成会断开所有用旧密钥的客户端。依据：项目规则（SEC-27，M5 设计 4.12）。

---

## 11. 审批（项目规则）

本节的规则来自 ADR-0011 第 4 条、ADR-0014 第 3、4、6 条、M5 设计 4.4 和 4.6，以及 SEC-23、SEC-24，优先于本文的其他章节，**比 TIA 的审批规则严格**（附录 B）。"依据"中的 Microsoft 和 W3C 条目只说明实现方式。绑定、期限和"只能使用一次"由 Host 判断（SEC-06），Workbench 只显示问题、收集回答。

问题有两种（`QuestionKind`）：审批（标题"批准 AI 修改 EPLAN 项目？"，按钮"批准 / 拒绝"）和解除写入暂停（标题"上次 AI 修改的结果未知"，按钮"已核实，继续 / 保持暂停"）。下文的"批准""拒绝"同时指这两组按钮。

- **APR-01 · MUST** 每个问题单独回答：没有"全部批准"、批量批准、"记住这个决定"或任何自动回答的设置。理由：每次写入都要人看过它的变更清单。依据：项目规则（ADR-0014 第 6 条，M5 设计第 2 节的非目标和 4.6，SEC-24）。
- **APR-02 · MUST** "拒绝"和"批准"一样大、样式相同（都用默认按钮样式，TIA 主题的 `Primer.Secondary`），都不用主要样式或危险样式；都不是默认按钮，也不是取消按钮（`IsDefault`、`IsCancel` 都为假）；没有快捷键，也没有访问键，只能点击，或在按钮获得焦点后按 Enter、空格。顺序与 EPLAN 中的审批对话框相同："拒绝"在左，"批准"在右。理由：减少盲批和误触；两个渠道的同一问题看起来一样。依据：项目规则（ADR-0011 第 4 条，ADR-0014 第 6 条，ADR-0017 第 5 条，SEC-24）。
- **APR-03 · MUST** 工程师打开一个问题时（列表中按 Enter 或双击、从提醒或通知打开），以及回答一个问题后下一个顶上来时，键盘焦点放在问题的标题上（可以获得焦点，不是 Tab 停靠点，标题级别 2），从不放在两个按钮上，即使之前焦点在按钮上。用鼠标或方向键在列表中选择时，焦点留在列表中（CTL-04、APR-06）；列表中的 Enter 和空格都不回答问题。WPF：窗口从托盘恢复或重新激活时，系统会把焦点还给上次的控件；焦点因此落在"批准"或"拒绝"上时，在 `Activated` 中把它移到标题，落在别处时不动（每次激活都无条件移焦点会在窗口不在前台时反复触发激活，卡住界面线程；2026-10-21 项目负责人："你刚刚的建议按照你推荐的来即可"）。理由：一次回车或空格不能直接回答新出现的问题。依据：项目规则（ADR-0014 第 6 条）；[Keyboard interactions › Initial focus](https://learn.microsoft.com/en-us/windows/apps/develop/input/keyboard-interactions#initial-focus)。
- **APR-04 · MUST** 500 毫秒防误触：详情区每次显示一个不同的问题、窗口每次由隐藏或最小化变为可见、每次切到审批队列页之后，500 毫秒内两个按钮都不响应点击、Enter 和空格。时间从新内容实际显示（渲染完成）时算起，不从数据到达时算起；实现可以在这段时间内把按钮设为禁用。理由：回答完一个问题后多点的一下，不能落到下一个问题上（T21）。依据：项目规则（ADR-0011 第 4 条，ADR-0014 第 6 条，SEC-24；"切到审批队列页"是本规范的补充）。
- **APR-05 · MUST** 新问题到达时，详情区不换掉工程师正在看的问题：选中项按问题本身保持，不按列表中的位置，也不按请求 ID（一次写入的审批和在它之前单独询问的解除暂停共用这次写入的请求 ID，ADR-0010 第 3 节）；队列原来为空时才直接显示新问题，但不移动键盘焦点（APR-14），APR-04 照样适用。理由：问题在工程师看的时候被换掉，回答就会落到别的问题上（T21）。依据：项目规则（本规范对 SEC-24 的补充）。
- **APR-06 · MUST** 审批队列页的布局：左侧是等待中的问题，最早到期的在上，每项显示种类、工具、项目名、剩余时间（文字）和破坏性标记；右侧是选中问题的详情；"已回答"是同一页中的另一组（Ant 的 Segmented）。列表项上的 Enter、双击只选中问题并把焦点移到详情的标题，从不回答问题；列表项没有回答问题的上下文菜单。理由：回答只有一条路径：看过详情后按详情中的按钮。依据：项目规则（M5 设计 4.6）；[Nested UI in list items](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/nested-ui#nested-ui-handling)。
- **APR-07 · MUST** 详情显示：标题；工具（Code）；级别（写入、破坏性）；项目名和完整路径；EPLAN 实例（版本、变体、进程号）；"客户端（未验证）"加客户端自报的名称，没有名称时写"未提供"，经虚拟机连接的会话另标"经网络：<地址>"（SEC-30，ADR-0018；Workbench 协议中还没有这个字段，实现虚拟机连接时加上）；变更清单（APR-08）；撤销说明（审批写"批准后可在 EPLAN 中按 Ctrl+Z 撤销"，解除暂停不提 Ctrl+Z）；解除暂停另显示暂停记录（时间、工具、范围）；倒计时；两个按钮。文字与 EPLAN 对话框的相同（TXT-09）。理由：人要有足够的信息作决定，而客户端的名称是它自己说的。依据：项目规则（M5 设计 4.6，M3 设计 4.6，ADR-0014 第 3、6 条）。
- **APR-08 · MUST** 变更清单只作纯文本，显示在标明"来自项目的数据"的等宽区域中：剥除控制字符和双向文字控制符（至少 U+202A–U+202E 和 U+2066–U+2069，与 Host 相同）；一项中的换行显示为可见的符号，空项保留为分隔的空行；每行和整个清单都有长度上限，超出时注明"还有 N 行"；区域只读、可以选中、可以用键盘聚焦并由读屏读出；不生成超链接、标记或任何界面元素；Workbench 的日志不记录变更清单。Host 已经清洗过，Workbench 仍要自己清洗。理由：项目中的文字可能冒充界面说明、改变显示顺序或把关键的行挤出视野（T20）。依据：项目规则（SEC-23，M5 设计第 5 节）。
- **APR-09 · MUST** 破坏性的问题醒目标出：详情顶部有错误图标加文字"破坏性操作：执行前先备份项目"，并写出"执行前先备份到 <备份目录>"，目录以 Code 层级完整显示、可以选中；不只靠颜色；两个按钮的样式不因此改变。理由：删除、重编号难以逐项撤销，人要在批准前知道有备份、备份在哪里。依据：项目规则（M5 设计 4.6，ADR-0013，ADR-0005 的级别）。
- **APR-10 · MUST** 倒计时用文字（"{0} 后自动拒绝"、"{0} 后自动保持暂停"）加进度条；最后 20 秒文字和进度条改用警告色，并加警告图标；读屏只在固定阈值（30 秒、10 秒）播报，不逐秒播报，倒计时文字本身不是 live region。期限默认 120 秒，只能在 `config.json` 中于 60–300 秒之间设置（ADR-0011 第 2 条），Workbench 中不能延长，按 WCAG 2.2.1 的"必要"例外处理（第 15 节）。理由：期限是安全设计的一部分，人要知道还剩多少时间。依据：项目规则（M5 设计 4.4 的 S16、4.6）；[WCAG 2.2 › 2.2.1 Timing Adjustable](https://www.w3.org/TR/WCAG22/#timing-adjustable)。
- **APR-11 · MUST** 回答后、到期后、被取消后，问题移到"已回答"，显示结果：已批准、已拒绝、已过期、已取消（客户端取消了调用，或连接断开）；解除暂停为"已继续""保持暂停"。正在显示的问题被取消或到期时，详情显示原因、按钮消失，焦点留在标题上，不丢失。"已回答"只保存在本次运行中，不持久化，审计才是记录。理由：人要知道每个问题怎样结束了。依据：项目规则（M5 设计 4.4 第 1 项、4.6，ADR-0014 第 3、4 条）。
- **APR-12 · MUST** 执行结果：批准后，这个问题的连接保持打开到写入结束，`approval.outcome` 只更新"已回答"中的显示：执行中、已完成、已回滚、未执行、结果未知（附原因）。"结果未知"用警告图标和文字说明写入已暂停、下一次写入前会请工程师确认（ADR-0010 第 3 节）。连接断开而没有收到结果时显示"未收到结果"，不推断成败。执行结果不改变已作的决定，不启用任何按钮，不能批准任何东西。理由：人要知道批准的写入是否真的执行了。依据：项目规则（ADR-0014 第 3 条，M5 设计 4.6）。
- **APR-13 · MUST** Workbench 同时服务多个管道连接：多个 Host 可以同时提问，批准了的问题的连接一直开到写入结束。队列合并所有连接的问题，每项显示自己的 EPLAN 实例和客户端，使两个相似的问题能区分开；一个连接上的取消、结果和断开只影响它自己的问题。等待中的问题达到 50 个时 Workbench 不再接收，状态栏写明新的问题改由 EPLAN 中的对话框询问（STS-11）。理由：多个会话同时写入时，人不能把回答给错问题。依据：项目规则（ADR-0014 第 3 条和后果，M5 设计 4.6）。
- **APR-14 · MUST** 不抢焦点：新问题到达时不激活窗口、不置顶、不自动切到审批队列页、不移动 Workbench 中的键盘焦点。提醒只用：托盘图标上的待处理数、任务栏按钮闪烁、窗口可见时的 toast 加 live region、窗口最小化或隐藏时的系统通知（STS-09）。工程师从提醒打开时，显示审批队列页并选中对应的问题，APR-03、APR-04 照样适用。理由：工程师可能正在 EPLAN 中打字（PRI-07）。依据：项目规则（M5 设计 4.4 的"提醒"，ADR-0014 第 6 条）。
- **APR-15 · MUST** Esc、关闭窗口、切换页面都不回答问题；关闭窗口只是缩到托盘（NAV-09），退出按 DLG-11。理由：只有两个按钮能回答问题。依据：项目规则（ADR-0014 第 4 条）。
- **APR-16 · MUST NOT** 提供改变审批行为的控件：不能关闭审批、不能改期限、不能信任某个客户端或会话。理由：安全配置只经配置文件修改；会话内免审批要另写 ADR。依据：项目规则（SEC-02，M5 设计第 2 节的非目标，ADR-0005 的后果）。

---

## 12. 状态、进度与通知

状态栏是 Header 右侧的状态区（3.1 节）。

- **STS-01 · MUST** 状态栏的每一项都有文字（不只是圆点）和 UIA 名称；可以操作的项是能获得焦点的按钮，不在面板上挂鼠标事件。理由：WCAG 2.1.1、4.1.2。依据：[Keyboard accessibility](https://learn.microsoft.com/en-us/windows/apps/design/accessibility/keyboard-accessibility#keyboard-alternatives-to-pointer-actions-and-events-for-specific-control-elements)。
- **STS-02 · MUST** 重要的状态变化用 live region 播报：审批管道的状态、新问题（可以用 Assertive）、倒计时的阈值（APR-10）、问题的结束和执行结果、子命令完成或失败（Polite）。理由：WCAG 4.1.3。依据：[What's new in accessibility in .NET Framework（4.7.1 LiveRegion）](https://learn.microsoft.com/en-us/dotnet/framework/whats-new/whats-new-in-accessibility)；[WCAG 2.2 › 4.1.3 Status Messages](https://www.w3.org/TR/WCAG22/#status-messages)。
- **STS-03 · MUST** 耗时的操作（运行 `doctor`、`audit verify`、`client-config`，读取大的审计文件）显示进度：知道总量时用确定进度，否则用不确定进度，旁边有文字说明正在做什么；进度放在发起操作的位置附近。理由：用户要知道程序还在工作。依据：[Progress controls › Types of progress](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/progress-controls#types-of-progress)。
- **STS-04 · MUST** 调用 `eplanmcp.exe` 的子命令都有超时（M5 设计 4.4），超时后显示错误（ERR-02）；**SHOULD** 能取消的提供"取消"。理由：子命令卡住时界面不能一直转圈。依据：项目规则；[Commanding basics › Provide command feedback](https://learn.microsoft.com/en-us/windows/apps/design/basics/commanding-basics#provide-command-feedback)。
- **STS-05 · MUST** 被阻止的操作说明原因和办法，不静默无效。理由：静默失败让人以为程序卡死。依据：[Commanding basics › Provide command feedback](https://learn.microsoft.com/en-us/windows/apps/design/basics/commanding-basics#provide-command-feedback)。
- **STS-06 · MUST** 持续的全局状态（不接收审批、找不到 `eplanmcp.exe`、状态页结果中的写入暂停、虚拟机连接的服务在运行）用 Banner 或状态栏显示，带严重级别图标和文字，不用会自动消失的 toast。理由：持续的状态要一直看得见。依据：[InfoBar › Is this the right control?](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/infobar#is-this-the-right-control)。
- **STS-07 · SHOULD** toast 排队逐条显示，不遮住审批按钮和焦点所在的元素，可以用键盘关闭；新问题的 toast 在问题结束前不自动消失。理由：WCAG 2.4.11；审批提醒不能被下一条消息盖掉。依据：[WCAG 2.2 › 2.4.11](https://www.w3.org/TR/WCAG22/#focus-not-obscured-minimum)。
- **STS-08 · MUST** Banner、提示和徽章不快速反复出现、消失；短时间内的多次变化合并显示。理由：闪烁影响光敏用户，也让人无法阅读。依据：[InfoBar › Recommendations](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/infobar#recommendations)；[WCAG 2.2 › 2.3.1](https://www.w3.org/TR/WCAG22/#three-flashes-or-below-threshold)。
- **STS-09 · MUST** 托盘与系统通知：托盘图标显示待处理数，ToolTip 为"N 个问题等待审批"；窗口最小化或隐藏时发系统通知（做法由 S16 确定）；文字本地化，只写数量和工具名，**不含项目中的文字**；工程师激活通知或托盘时还原窗口并打开对应的问题（APR-14）。理由：问题会到期，人要及时知道；通知可能显示在锁屏上，也没有"来自项目的数据"的标记（T20）。依据：项目规则（M5 设计 4.4，SEC-23）。
- **STS-10 · MUST** 计数徽章的 UIA 名称包含完整的含义（"3 个问题等待审批"），不只是数字。理由：读屏单独读"3"没有意义。依据：[Expose basic accessibility information › Accessible name](https://learn.microsoft.com/en-us/windows/apps/design/accessibility/basic-accessibility-information#accessible-name)。
- **STS-11 · MUST** 状态栏始终显示 Workbench 是否在接收审批；不接收时（管道名已被占用、等待中的问题已满 50 个）写明原因，并说明问题改由 EPLAN 中的对话框询问。理由：工程师要知道问题会出现在哪里。依据：项目规则（ADR-0014 第 2、3 条）。
- **STS-12 · MUST** 虚拟机连接的服务（ADR-0018）运行时，状态栏一直显示"虚拟机连接：运行中"和监听的地址、端口；启动失败、防火墙规则缺失用 Banner 说明原因和办法；服务随 Workbench 退出而停止。理由：服务不能在无人注意时一直对网络开放（T27）。依据：项目规则（SEC-29，M5 设计 4.12）。

---

## 13. 错误与空状态

- **ERR-01 · MUST** 错误文字说明发生了什么、已知的原因、可以怎么做；不责备用户；写出具体的对象（路径、命令、版本）；"未预期的错误"只用于真正的崩溃。理由：错误信息要帮人回到工作。依据：[Writing style › Error messages](https://learn.microsoft.com/en-us/windows/apps/design/style/writing-style#error-messages)；[Error Messages › Text](https://learn.microsoft.com/en-us/windows/win32/uxguide/mess-error#text)。
- **ERR-02 · MUST** 原始异常和堆栈不作主信息：主信息是文字表中的文字，可以展开"详细信息"（命令行、退出码、`schemaVersion`、日志文件 `logs\workbench-*.log` 的位置，以 Code 层级显示）。理由：技术细节对排查有用，但不应挡在用户前面。依据：[Error Messages › Guidelines](https://learn.microsoft.com/en-us/windows/win32/uxguide/mess-error#guidelines)。
- **ERR-03 · MUST** 字段的验证错误在原位显示：字段边框变为错误色，下方有文字（不只靠颜色），文字作为字段的 HelpText 并播报；焦点移到第一个出错的字段。理由：WCAG 3.3.1、3.3.3。依据：[WCAG 2.2 › 3.3.1 Error Identification](https://www.w3.org/TR/WCAG22/#error-identification)。
- **ERR-04 · MUST** 页面或全局的失败用 Banner 显示，并给出可以执行的操作（"重新运行""打开日志文件夹"）。理由：失败要带出路。依据：[InfoBar](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/infobar)。
- **ERR-05 · MUST** 空状态用统一的组件：标题（缺了什么）、一句话（为什么、下一步）、可选的一个操作，在主体区域居中。例如审批队列为空："没有等待中的问题"，"AI 要修改真实项目时，问题会出现在这里；Workbench 不在运行时，改由 EPLAN 中的对话框询问。"理由：空白区域要告诉人接下来会发生什么。依据：项目规则；[Writing style](https://learn.microsoft.com/en-us/windows/apps/design/style/writing-style)。
- **ERR-06 · SHOULD** 区分加载、空、错误三种状态，不把"还没加载完"显示成"没有数据"。理由：避免误判。依据：[Progress controls](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/progress-controls#progress-controls-best-practices)。
- **ERR-07 · MUST** 严重程度用图标和"错误""警告"等字样表达，不只用红色文字。理由：WCAG 1.4.1。依据：[Color in Windows › Usability](https://learn.microsoft.com/en-us/windows/apps/design/signature-experiences/color#usability)。
- **ERR-08 · MUST** 子命令的输出不能解析（不是 JSON、`schemaVersion` 不认识、字段不符）时整体按错误显示，说明 Workbench 与 `eplanmcp.exe` 的版本可能不一致，不显示解析出的一部分。理由：半份结果会被当成完整的结果。依据：项目规则（M5 设计 4.4、4.11）。

---

## 14. 键盘与焦点

- **KEY-01 · MUST** 所有功能都能只用键盘完成；不在非控件元素上挂鼠标事件作为唯一的触发方式；指针操作在抬起时触发，移开可以取消。理由：WCAG 2.1.1、2.5.2。依据：[Keyboard accessibility](https://learn.microsoft.com/en-us/windows/apps/design/accessibility/keyboard-accessibility#keyboard-alternatives-to-pointer-actions-and-events-for-specific-control-elements)；[WCAG 2.2 › 2.5.2 Pointer Cancellation](https://www.w3.org/TR/WCAG22/#pointer-cancellation)。
- **KEY-02 · MUST** Tab 顺序与视觉顺序一致；非交互元素和装饰不是 Tab 停靠点。理由：WCAG 2.4.3。依据：[Keyboard interactions › Tab stops](https://learn.microsoft.com/en-us/windows/apps/develop/input/keyboard-interactions#tab-stops)。
- **KEY-03 · MUST** 每个能获得焦点的元素都有可见的焦点指示：全局 `FocusVisualStyle`，2 epx 外环（Focus.Outer）加 1 epx 内环（Focus.Inner），在所有主题中对相邻颜色不低于 3:1；模板可以增加焦点效果，不能去掉它。理由：WCAG 2.4.7、1.4.11；WPF 默认的 1 px 点线在深色面上几乎看不见。依据：[Visual feedback › High visibility focus visuals](https://learn.microsoft.com/en-us/windows/apps/develop/input/guidelines-for-visualfeedback#high-visibility-focus-visuals)；[Styling for Focus in Controls, and FocusVisualStyle](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/styling-for-focus-in-controls-and-focusvisualstyle)。
- **KEY-04 · MUST** Esc 关闭临时界面（对话框即取消、抽屉、弹出层、toast），从不触发破坏性操作，也不回答问题（APR-15）。理由：Esc 只取消临时界面。依据：[Keyboard interactions › Esc key](https://learn.microsoft.com/en-us/windows/apps/develop/input/keyboard-interactions#esc-key)。
- **KEY-05 · MUST** Enter 激活获得焦点的按钮；对话框中 Enter 触发默认按钮，有风险的确认中默认按钮是安全选项（DLG-04）。审批的两个按钮都不是默认按钮（APR-02）。理由：提交行为可以预期。依据：[Keyboard interactions › Enter key](https://learn.microsoft.com/en-us/windows/apps/develop/input/keyboard-interactions#enter-key)。
- **KEY-06 · MUST** 对话框按钮和主要的表单字段有访问键：模板中 `RecognizesAccessKey="True"`，英文用行内下划线（`_Refresh`），中文用后缀（`刷新(_R)`），按 Alt 显示；访问键随语言本地化，同一作用域内不重复。审批的两个按钮除外（APR-02）。理由：访问键主要服务于运动障碍的用户。依据：[Keyboard interactions › Access keys](https://learn.microsoft.com/en-us/windows/apps/develop/input/keyboard-interactions#access-keys)。
- **KEY-07 · MUST** 快捷键（代码、ToolTip 和本表一致）：

  | 快捷键 | 动作 | 级别 |
  |---|---|---|
  | Ctrl+1 … Ctrl+4 | 切到审批队列、审计、状态、MCP 与客户端 | MUST |
  | Ctrl+Tab / Ctrl+Shift+Tab | 下一个 / 上一个页面 | SHOULD |
  | Ctrl+F | 聚焦审计页的筛选框 | SHOULD |
  | F5 | 刷新当前页：状态页重新运行 `doctor`，审计页重新读取 | SHOULD |
  | F6 / Shift+F6 | 在主要区域之间循环（NAV-08） | SHOULD |
  | Esc | 关闭临时界面（KEY-04） | MUST |
  | Shift+F10 / 菜单键 | 打开上下文菜单（CMD-10） | MUST |
  | 无 | 批准、拒绝、已核实继续、保持暂停：没有快捷键和访问键（APR-02） | MUST |

  理由：快捷键要一致、好记，并与 Windows 的约定一致。依据：[Keyboard interactions › Common keyboard shortcuts](https://learn.microsoft.com/en-us/windows/apps/develop/input/keyboard-interactions#common-keyboard-shortcuts)。
- **KEY-08 · MUST** 浮层关闭后、焦点元素因切页或问题结束而消失后，焦点回到合理的位置（触发元素、当前菜单项或问题的标题）。理由：WCAG 2.4.3；丢失焦点会让键盘用户回到窗口开头。依据：[Keyboard interactions › Initial focus](https://learn.microsoft.com/en-us/windows/apps/develop/input/keyboard-interactions#initial-focus)。
- **KEY-09 · MUST** 除了模态对话框和带遮罩的抽屉（Tab 在内循环），不出现键盘陷阱。理由：WCAG 2.1.2。依据：[Keyboard interactions › Popup UI](https://learn.microsoft.com/en-us/windows/apps/develop/input/keyboard-interactions#popup-ui)。
- **KEY-10 · SHOULD** 一组相关的选项（菜单、分段选择）是一个 Tab 停靠点，组内用方向键移动，Home、End 到首尾。理由：减少停靠点。依据：[Keyboard interactions › Control group](https://learn.microsoft.com/en-us/windows/apps/develop/input/keyboard-interactions#control-group)。
- **KEY-11 · MUST** 鼠标的位置不干扰键盘操作。理由：键盘用户的焦点不应被指针状态改变。依据：[.NET Accessibility Best Practices › Navigation](https://learn.microsoft.com/en-us/dotnet/framework/ui-automation/accessibility-best-practices#navigation)。

---

## 15. 无障碍：可测的要求与 WCAG 2.2 AA

### 15.1 可测的要求
"UIA 树测试"指在 `EplanMcp.Workbench.Tests` 中，用 `UIElementAutomationPeer.CreatePeerForElement` 从每个页面、抽屉和对话框（夹具数据）递归遍历 AutomationPeer 并断言属性；"AI4W"指 Accessibility Insights for Windows 的 FastPass。

- **A11Y-01 · MUST** 每个能获得焦点或能操作的元素，UIA 名称非空、来自文字表、以可见的标签开头，且不是 CLR 类型名。验证：UIA 树测试（中英文各一遍）、AI4W。
- **A11Y-02 · MUST** 每个输入控件的 `LabeledBy` 指向可见的标签。验证：UIA 树测试。
- **A11Y-03 · MUST** 控件类型与行为一致，状态（选中、按下、展开）读得出。验证：Inspect 或 AI4W 人工核对。
- **A11Y-04 · MUST** 只用键盘完成：打开审批队列、选择问题、读变更清单、批准、拒绝、确认解除暂停；筛选审计、打开记录详情、校验完整性；运行自检；配置客户端（选择、预览差异、写入、复制命令）；虚拟机连接的设置（ADR-0018）；切换语言和主题；从托盘打开和退出。验证：发布前的键盘脚本（人工），关键路径另用 `KeyboardDevice` 事件的自动测试。
- **A11Y-05 · MUST** 焦点指示在浅色、深色、高对比下都可见，对相邻色不低于 3:1，不被 toast 或抽屉完全遮住。验证：渲染测试截取焦点元素计算对比；人工检查遮挡。
- **A11Y-06 · MUST** 实际用到的每一对文字与背景 token 对比不低于 4.5:1，非文字指示不低于 3:1；三套调色板的键集合相同。验证：移植 TIA 的 `ThemeSurfaceTests`（阈值 4.5 / 3.0），覆盖次要文字、徽章、按钮文字、选中态和边框。
- **A11Y-07 · MUST** Windows 的四个对比主题（Aquatic、Desert、Dusk、Night sky）下没有写死的颜色，文字读得清，选中和焦点能分辨。验证：渲染测试注入高对比调色板；发布前在真实的对比主题下人工检查。
- **A11Y-08 · MUST** 读屏能听到：新问题、倒计时的阈值、问题的结束和执行结果、审批管道状态的变化、子命令完成或失败、错误。验证：单元测试断言设置了 `LiveSetting` 并触发了 `LiveRegionChanged`；讲述人脚本人工验证。
- **A11Y-09 · MUST** 显示缩放 100–200% 和混合 DPI 下不模糊、不截断；窗口在 1366 × 768（100%）和 1920 × 1080（150%）下完整显示；Windows 的"文本大小"为 150% 时关键文字不被截断。验证：断言窗口的 `MinWidth`、`MinHeight`；人工在两种工作区下检查。
- **A11Y-10 · MUST** 页面标题的 `HeadingLevel` 为 Level1，卡片、分节和审批问题的标题为 Level2，抽屉和对话框的标题为 Level1。验证：UIA 树测试。
- **A11Y-11 · MUST** 对话框和带遮罩的抽屉满足 `IsDialog`、初始焦点、Tab 循环、Esc 和焦点归还。验证：自动测试打开、关闭并检查 `Keyboard.FocusedElement`。
- **A11Y-12 · MUST** 不只靠颜色传达信息。验证：评审清单，灰度截图人工检查。
- **A11Y-13 · MUST** 指针目标不小于 24 × 24 epx，或满足间距例外。验证：遍历可视树断言可点击元素的 `ActualWidth`、`ActualHeight`，例外列在测试的白名单中。
- **A11Y-14 · MUST** 以上自动检查在 CI 中运行并阻止回归；测试按 [testing.md](testing.md) 第 1 节标注规模，数量下限写入 `tests/test-suites.json`（testing.md 第 7 节）。验证：CI。
- **A11Y-15 · SHOULD** 每个页面在夹具数据下 AI4W FastPass 零失败。验证：发布前人工运行。

### 15.2 WCAG 2.2 A / AA 准则的应用
按 [WCAG2ICT](https://www.w3.org/TR/wcag2ict-22/) 解释（多数准则"如原文适用"或"替换用词后适用"：网页换成软件）。

| 准则 | 级别 | Workbench 中的要求 | 验证 |
|---|---|---|---|
| [1.1.1 Non-text Content](https://www.w3.org/TR/WCAG22/#non-text-content) | A | 图标按钮和状态图标有文字替代，装饰不进 UIA 控件视图（ICO-02） | UIA 树测试 |
| [1.3.1 Info and Relationships](https://www.w3.org/TR/WCAG22/#info-and-relationships) | A | 标签关联、标题级别、表格列头（CTL-03、CTL-06、A11Y-10） | UIA 树测试 |
| [1.3.2 Meaningful Sequence](https://www.w3.org/TR/WCAG22/#meaningful-sequence) | A | UIA 树的顺序与阅读顺序一致 | Inspect |
| [1.3.3 Sensory Characteristics](https://www.w3.org/TR/WCAG22/#sensory-characteristics) | A | 说明不只用位置或颜色指代 | 文字评审 |
| [1.3.4 Orientation](https://www.w3.org/TR/WCAG22/#orientation) | AA | 不锁定方向 | 人工 |
| [1.4.1 Use of Color](https://www.w3.org/TR/WCAG22/#use-of-color) | A | COL-06 | 灰度截图 |
| [1.4.3 Contrast (Minimum)](https://www.w3.org/TR/WCAG22/#contrast-minimum) | AA | COL-04；没有达到"大字"的正文，一律按 4.5:1 | A11Y-06 |
| [1.4.4 Resize Text](https://www.w3.org/TR/WCAG22/#resize-text) | AA | 200% 显示缩放或放大"文本大小"时内容和功能不丢失 | A11Y-09 |
| [1.4.5 Images of Text](https://www.w3.org/TR/WCAG22/#images-of-text) | AA | 不用文字图片 | 评审 |
| [1.4.10 Reflow](https://www.w3.org/TR/WCAG22/#reflow) | AA | LAY-01、LAY-03；表格可以横向滚动 | A11Y-09 |
| [1.4.11 Non-text Contrast](https://www.w3.org/TR/WCAG22/#non-text-contrast) | AA | COL-05、KEY-03 | A11Y-05、A11Y-06 |
| [1.4.12 Text Spacing](https://www.w3.org/TR/WCAG22/#text-spacing) | AA | 通常不适用（平台没有覆盖文字间距的机制）；仍按 LAY-08 | — |
| [1.4.13 Content on Hover or Focus](https://www.w3.org/TR/WCAG22/#content-on-hover-or-focus) | AA | ToolTip 可以关闭、可以悬停、持续显示；不放唯一的信息（TYP-06） | 人工 |
| [2.1.1 Keyboard](https://www.w3.org/TR/WCAG22/#keyboard) | A | KEY-01 | A11Y-04 |
| [2.1.2 No Keyboard Trap](https://www.w3.org/TR/WCAG22/#no-keyboard-trap) | A | KEY-09 | A11Y-04 |
| [2.1.4 Character Key Shortcuts](https://www.w3.org/TR/WCAG22/#character-key-shortcuts) | A | 没有单字符快捷键 | 评审 |
| [2.2.1 Timing Adjustable](https://www.w3.org/TR/WCAG22/#timing-adjustable) | A | 审批期限按"必要"例外：由 Host 判断，只能在 `config.json` 中设置（APR-10） | 评审 |
| [2.2.2 Pause, Stop, Hide](https://www.w3.org/TR/WCAG22/#pause-stop-hide) | A | 倒计时是必要的；列表的自动更新不移动当前选中项（APR-05） | 人工 |
| [2.3.1 Three Flashes](https://www.w3.org/TR/WCAG22/#three-flashes-or-below-threshold) | A | STS-08 | 人工 |
| [2.4.1 Bypass Blocks](https://www.w3.org/TR/WCAG22/#bypass-blocks) | A | EN 301 549、Section 508 认为对软件不适用；以 NAV-08 代替 | 人工 |
| [2.4.2 Page Titled](https://www.w3.org/TR/WCAG22/#page-titled) | A | 窗口标题（NAV-03） | 人工 |
| [2.4.3 Focus Order](https://www.w3.org/TR/WCAG22/#focus-order) | A | KEY-02、KEY-08、DLG-02、DLG-10、APR-03 | A11Y-04、A11Y-11 |
| [2.4.4 Link Purpose (In Context)](https://www.w3.org/TR/WCAG22/#link-purpose-in-context) | A | 按钮的目的结合所在的卡片或行可以判断 | 评审 |
| [2.4.6 Headings and Labels](https://www.w3.org/TR/WCAG22/#headings-and-labels) | AA | 标题和标签描述内容与用途 | 文字评审 |
| [2.4.7 Focus Visible](https://www.w3.org/TR/WCAG22/#focus-visible) | AA | KEY-03 | A11Y-05 |
| [2.4.11 Focus Not Obscured (Minimum)](https://www.w3.org/TR/WCAG22/#focus-not-obscured-minimum) | AA | 焦点元素不被 toast、抽屉或遮罩完全遮住（STS-07） | A11Y-05 |
| [2.5.1 Pointer Gestures](https://www.w3.org/TR/WCAG22/#pointer-gestures) | A | 没有多点或路径手势 | 评审 |
| [2.5.2 Pointer Cancellation](https://www.w3.org/TR/WCAG22/#pointer-cancellation) | A | KEY-01 | 代码扫描 |
| [2.5.3 Label in Name](https://www.w3.org/TR/WCAG22/#label-in-name) | A | A11Y-01 | UIA 树测试 |
| [2.5.7 Dragging Movements](https://www.w3.org/TR/WCAG22/#dragging-movements) | AA | 没有只能拖动完成的功能 | 评审 |
| [2.5.8 Target Size (Minimum)](https://www.w3.org/TR/WCAG22/#target-size-minimum) | AA | CTL-11 | A11Y-13 |
| [3.1.1 Language of Page](https://www.w3.org/TR/WCAG22/#language-of-page) | A | TYP-09 | 自动测试 |
| [3.1.2 Language of Parts](https://www.w3.org/TR/WCAG22/#language-of-parts) | AA | 中文界面中的普通英文词要翻译；产品名、技术标识和项目数据除外 | 文字评审 |
| [3.2.1 On Focus](https://www.w3.org/TR/WCAG22/#on-focus) | A | 获得焦点不切换上下文 | 人工 |
| [3.2.2 On Input](https://www.w3.org/TR/WCAG22/#on-input) | A | 输入不自动提交；新问题不换掉正在看的问题（APR-05） | 人工 |
| [3.2.3](https://www.w3.org/TR/WCAG22/#consistent-navigation)、[3.2.4](https://www.w3.org/TR/WCAG22/#consistent-identification) | AA | 对软件不适用；以 NAV-06、CMD-09 作软件内的一致性要求 | 评审 |
| [3.2.6 Consistent Help](https://www.w3.org/TR/WCAG22/#consistent-help) | A | 帮助入口（入门指南）的位置固定 | 评审 |
| [3.3.1 Error Identification](https://www.w3.org/TR/WCAG22/#error-identification) | A | ERR-03 | 人工 |
| [3.3.2 Labels or Instructions](https://www.w3.org/TR/WCAG22/#labels-or-instructions) | A | CTL-03 | UIA 树测试 |
| [3.3.3 Error Suggestion](https://www.w3.org/TR/WCAG22/#error-suggestion) | AA | ERR-01 | 文字评审 |
| [3.3.4 Error Prevention](https://www.w3.org/TR/WCAG22/#error-prevention-legal-financial-data) | AA | 修改项目经审批（第 11 节）；写入客户端配置先看差异再确认（DLG-12） | 评审 |
| [3.3.7 Redundant Entry](https://www.w3.org/TR/WCAG22/#redundant-entry) | A | 配置向导中已填的值自动带入下一步 | 人工 |
| [3.3.8 Accessible Authentication (Minimum)](https://www.w3.org/TR/WCAG22/#accessible-authentication-minimum) | AA | 虚拟机连接的密钥由 Workbench 生成，可以复制、粘贴，不必记忆或转写（CTL-14） | 人工 |
| [4.1.2 Name, Role, Value](https://www.w3.org/TR/WCAG22/#name-role-value) | A | A11Y-01、A11Y-03、CTL-05、CTL-07 | UIA 树测试 |
| [4.1.3 Status Messages](https://www.w3.org/TR/WCAG22/#status-messages) | AA | STS-02、APR-10 | A11Y-08 |

不适用：1.2.1–1.2.5、1.4.2（没有音视频）；1.3.5（不收集个人信息）；2.4.5（对软件不适用）；2.5.4（没有运动操作）。4.1.1 已从 WCAG 2.2 中删除。

---

## 16. 本地化

- **LOC-01 · MUST** 看得见的和读得出的文字（含 `AutomationProperties.Name`、`HelpText`、ToolTip、托盘和通知文字、窗口和对话框的标题、单位）都来自（键，英文，中文）文字表，由 `StringsTests` 覆盖（移植自 TIA）。例外：产品名（EPLAN、Electric P8、Pro Panel、MCP、Claude Code、Workbench）、用户和项目的数据、技术标识（TYP-08），以及 Host 已经组织好文字的变更清单（LOC-10）。理由：中英界面要完整切换。依据：[Globalization and localization](https://learn.microsoft.com/en-us/windows/apps/design/globalizing/globalizing-portal)。
- **LOC-02 · MUST** 句子不用字符串拼接，用带占位符的格式串，让译文可以调整语序；项目中的文字只作为参数，不作为格式串。理由：拼接锁死英文语序。依据：[Globalization › Guidelines for globalization](https://learn.microsoft.com/en-us/windows/apps/design/globalizing/globalizing-portal#guidelines-for-globalization)。
- **LOC-03 · MUST** 单位本地化（"{0} 秒" / "{0}s"、"{0} 毫秒" / "{0} ms"）。理由：单位是文字的一部分。依据：同上。
- **LOC-04 · MUST** 布局容纳语言的差异：英文通常比中文宽，等宽字体中更明显（TYP-10）；文字按钮用 `MinWidth`；每个页面在两种语言、最小窗口下检查。理由：防止英文被截断。依据：[Globalization › Guidelines for globalization](https://learn.microsoft.com/en-us/windows/apps/design/globalizing/globalizing-portal#guidelines-for-globalization)。
- **LOC-05 · MUST** 数字和日期按用户的区域格式（`CultureInfo.CurrentCulture`）显示，不按界面语言推断区域；审计记录的技术时间戳可以固定用 ISO 格式。理由：语言和区域是两回事。依据：同上（Don't use language to assume a user's region）。
- **LOC-06 · MUST** 中文用全角标点；中文与英文、数字之间按文字表的统一习惯加空格（"{0} 个问题"）。理由：标点混用影响阅读。依据：项目规则。
- **LOC-07 · MUST** 全部文字遵守术语表：

  | 中文 | English | 说明 |
  |---|---|---|
  | 项目 | project | EPLAN 项目；不用"工程" |
  | 页 | page | 页名原样显示，如 `=AP+ST1/3` |
  | 设备标识 | device tag | 原样显示，如 `-K1` |
  | 审批 | approval | 问题、渠道、队列 |
  | 批准 / 拒绝 | Approve / Reject | 审批的两个按钮 |
  | 写入暂停 | write suspension | 结果未知后 AI 的写入暂停（ADR-0010） |
  | 已核实，继续 / 保持暂停 | Checked, continue / Keep suspended | 解除暂停的两个按钮 |
  | 变更清单 | change list | 来自项目的数据 |
  | 写入 / 破坏性 | Write / Destructive | 工具的级别（ADR-0005） |
  | 客户端（未验证） | client (unverified) | MCP 客户端自报的名称 |
  | scratch | scratch | 不翻译；"scratch 项目" |
  | 已过期 / 已取消 | Expired / Cancelled | 问题的结束方式 |
  | 密钥 | key | 虚拟机连接的共享密钥（ADR-0018） |
  | Workbench | Workbench | 不翻译，全称 EplanMcp Workbench |

  理由：同一事物有几个叫法，人会以为是不同的东西。依据：[Writing style › Style conventions](https://learn.microsoft.com/en-us/windows/apps/design/style/writing-style#style-conventions)；[WCAG 2.2 › 3.2.4](https://www.w3.org/TR/WCAG22/#consistent-identification)。
- **LOC-08 · MUST** 切换语言立即生效，UIA 名称同样绑定到文字表、随语言更新。理由：读屏和界面的语言要一致。依据：[Accessible names and localization](https://learn.microsoft.com/en-us/windows/apps/design/accessibility/basic-accessibility-information#accessible-names-and-localization)。
- **LOC-09 · MUST** 中英两种文字的含义和操作指引对等，不在一种语言中多出或少掉信息。理由：两种语言的用户应得到同样的帮助。依据：[Globalization and localization](https://learn.microsoft.com/en-us/windows/apps/design/globalizing/globalizing-portal)。
- **LOC-10 · MUST** 默认语言跟随系统的界面语言，可以切换（架构 8.11）。变更清单由 Host 按系统的界面语言组织（`ApprovalAskParams.Changes`，与 EPLAN 对话框相同），Workbench 原样显示，不随自己的语言切换；Workbench 自己的标签、标题和按钮照常切换。理由：变更清单的文字由 Host 生成，Workbench 不能改写项目数据。依据：项目规则（M3 设计 4.6，M5 设计 4.4）。

---

## 17. 界面文字

- **TXT-01 · MUST** 简洁、主动语态、先说重点；用用户熟悉的词。理由：用户扫读，不逐字读。依据：[Writing style › Voice and tone principles](https://learn.microsoft.com/en-us/windows/apps/design/style/writing-style#voice-and-tone-principles)。
- **TXT-02 · MUST** 英文用句首大写，不用全大写；中文不用感叹号；少用"请"，只在要用户做麻烦的事时用。理由：语气平和，不制造紧张。依据：[Writing style › Style conventions](https://learn.microsoft.com/en-us/windows/apps/design/style/writing-style#style-conventions)；[Error Messages › Text](https://learn.microsoft.com/en-us/windows/win32/uxguide/mess-error#text)。
- **TXT-03 · MUST** 用与设备无关的动词："选择""打开"，不写"点击"，除非确实只针对鼠标。理由：键盘和读屏用户也要能照做。依据：[Writing style › Accessibility and localization](https://learn.microsoft.com/en-us/windows/apps/design/style/writing-style#accessibility-and-localization)。
- **TXT-04 · MUST** 标签、按钮、单选和复选的文字不加句号；完整的句子（ToolTip、错误、对话框正文）以句号结尾。理由：句号表示完整的句子。依据：[Writing style › Style conventions](https://learn.microsoft.com/en-us/windows/apps/design/style/writing-style#style-conventions)。
- **TXT-05 · MUST** 对话框的标题与按钮一问一答：按钮是对标题的直接回答。理由：读标题就能作答。依据：[Writing style › Dialogs](https://learn.microsoft.com/en-us/windows/apps/design/style/writing-style#dialogs)。
- **TXT-06 · MUST NOT** 在确认中写"警告""注意"，用警告图标表达；不用全大写的词强调。理由：图标比字样更快被认出，全大写读起来像喊叫。依据：[Confirmations › Text](https://learn.microsoft.com/en-us/windows/win32/uxguide/mess-confirm#text)。
- **TXT-07 · SHOULD** 缩写第一次出现时在说明中解释一次（MCP、stdio）；面向工程师的技术区域可以直接用通行的术语。理由：减少猜测，又不牺牲专业表达。依据：[Writing style › Style conventions](https://learn.microsoft.com/en-us/windows/apps/design/style/writing-style#style-conventions)。
- **TXT-08 · MUST** 归属用固定的说法：发起写入的一方一律称"AI"，并附"客户端（未验证）"和名称；回答的渠道写"Workbench"或"EPLAN 对话框"（审计中的渠道名 `workbench`）。理由：审批和审计中，谁发起、在哪里回答要一眼看出。依据：项目规则（ADR-0014 第 7 条）。
- **TXT-09 · MUST** 审批的文字与 EPLAN 中的审批对话框相同：标题、按钮、倒计时、撤销说明和备份说明（M3 设计 4.6）；改动时两处一起改。理由：同一问题在两个渠道中说法一样，工程师不必重新理解。依据：项目规则（ADR-0011 第 4 条，M3 设计 4.6）。

---

## 18. 验收与维护

- **PR 自查**（新增或修改界面时）：TYP-03（没有字面字号）、COL-01（没有字面颜色）、CTL-03 与 CTL-05（标签和名称）、KEY-03 与 KEY-04（焦点和 Esc）、LOC-01（没有写死的文字）、CTL-12（项目文字只作纯文本）；改到审批界面时逐条核对第 11 节；运行 15.1 节的自动检查。
- **审批的测试**（M5 设计第 7 节）：Small 测试覆盖防误触的状态机（APR-04）、选中项的保持（APR-05）、队列的排序和到期（APR-06、APR-10）、变更清单的清洗（APR-08）；Medium 测试用假的 Host（管道客户端）覆盖多个连接、取消、断开和执行结果（APR-11 到 APR-13）。
- **每次发布前**：A11Y-04 键盘脚本、A11Y-07 高对比检查、A11Y-08 讲述人脚本、A11Y-09 两种工作区、A11Y-15 AI4W，并入 Workbench 的手工冒烟清单（[testing.md](testing.md) 第 2 节），结果记入发布证据。
- **维护**：本规范随 ADR 和 M5 设计一起修改。合并 TIA 主题的改动时（ADR-0017 第 2 条），同时对照 TIA 的 `workbench-ui.md` 的变化更新本文，并把开头记录的 TIA 提交号改为新读的提交；附录 B 的每一项随之复核。

---

## 附录 A Token

实现键沿用 TIA 主题（`Themes/Primer.xaml` 和三套调色板）的名称。

### A.1 间距与尺寸

| Token | 值（epx） | 用途 |
|---|---|---|
| `Ui.Space.XXS` | 4 | 图标与文字、紧凑的组内 |
| `Ui.Space.XS` | 8 | 按钮之间、控件与上方的标签 |
| `Ui.Space.SM` | 12 | 控件与旁边的标签、卡片之间 |
| `Ui.Space` | 16 | 卡片内边距 |
| `Ui.Space.MD` | 20 | 对话框的上下内边距 |
| `Ui.Space.LG` | 24 | 页面边距、对话框的左右内边距 |
| `Ui.Space.XL` | 32 | 大的分区之间 |
| `Ui.ControlHeight` / `.Small` / `.Large` | 32 / 24 / 40 | 标准控件 / 紧凑按钮、标签 / 大号控件 |
| 列表行 | ≥ 36 | 表格和列表的行 |
| Sider | 216（收起 56） | 左侧栏 |
| Header | 56 | 顶栏 |
| 右侧栏 | 280–320 | 页面的上下文信息 |
| 抽屉 | 400 / 520 | 设置 / 审计记录详情 |
| 对话框 | 420–548 | |
| toast | 320 | |

### A.2 圆角

| Token | 值 | 用途 |
|---|---|---|
| `Ui.Radius` | 6 | 按钮、输入框、下拉框 |
| `Ui.Radius.Small` | 4 | 小号控件、标签、复选框 |
| `Ui.Radius.XS` | 2 | 进度条等细小元素 |
| `Ui.Radius.Large` | 8 | 卡片、抽屉、对话框、toast、弹出层 |
| — | 0 | 相接的直边、最大化的窗口 |

### A.3 字体与类型层级

| Token | 值 | 说明 |
|---|---|---|
| `Ui.Font`、`Ui.FontMono` | Maple Mono CN | 回退见 TYP-01 |
| `Ui.FontSize.Caption` / `Ui.LineHeight.Small` | 12 / 20 | Caption、Code |
| `Ui.FontSize.Body` / `Ui.LineHeight` | 14 / 22 | Body、Body Strong |
| `Ui.FontSize.Section` / `Ui.LineHeight.Large` | 16 / 24 | 卡片、抽屉、对话框和审批问题的标题 |
| `Ui.FontSize.Title` | 20（行高 28） | 页面标题 |

### A.4 语义颜色

浅色和深色是 Ant 的取值按 WCAG 调整后的结果（TIA 偏差清单 C.0）；括号中是与所在背景的对比。高对比一列是 WPF 的 `SystemColors` 键。

| 语义 | 实现键 | Ant token | 浅色 | 深色 | 高对比 | 用途 | 最低对比 |
|---|---|---|---|---|---|---|---|
| Surface.Window | `Primer.bg` | colorBgLayout | #F5F5F5 | #000000 | `WindowColorKey` | 窗口底色 | — |
| Surface.Layer | `Primer.card`、`Primer.input`、`Primer.pill` | colorBgContainer | #FFFFFF | #141414 | `WindowColorKey` | 卡片、Header、输入框、默认按钮 | — |
| Surface.Elevated | `Primer.menuBg` | colorBgElevated | #FFFFFF | #1F1F1F | `WindowColorKey` | 下拉、抽屉、对话框 | — |
| Surface.Subtle | `Primer.cardSoft`、`Primer.codeBg` | colorFillAlter | #FAFAFA / #F5F5F5 | #1D1D1D / #1F1F1F | `WindowColorKey` | 表头、悬停行、代码区和变更清单 | — |
| Surface.Selected | `Primer.cardSel` | controlItemBgActive | #E6F4FF | #111A2C | `HighlightColorKey` | 选中行 | 另有 3 epx 主色选中条（COL-05） |
| Surface.Hover | `Primer.pillHover` | controlItemBgHover | #F5F5F5 | #262626 | `ControlColorKey` | 菜单、文字按钮的悬停 | — |
| Scrim | `Ant.colorBgMask` | colorBgMask | 黑 45% | 黑 45% | 不铺遮罩 | 抽屉、对话框背后 | — |
| Stroke.Layer | `Primer.cardBorder`、`Primer.divider` | colorBorderSecondary、colorSplit | #F0F0F0 / rgba(5,5,5,0.06) | #303030 / rgba(253,253,253,0.12) | `WindowTextColorKey` | 卡片边框、分隔线 | 装饰性，可以低于 3:1 |
| Stroke.Button | `Ant.colorBorder` | colorBorder | #D9D9D9 | #424242 | `WindowTextColorKey` | 默认按钮、标签的边框（靠文字识别） | — |
| Stroke.Control | `Primer.inputBorder`、`Primer.checkBorder` | gray-7（代替 colorBorder） | #8C8C8C（3.4:1） | #737373（3.9:1） | `WindowTextColorKey` | 输入框、复选框、开关关闭态 | 3:1 |
| Text.Primary | `Primer.text`、`Primer.codeText` | colorText | 黑 88%（16.5:1） | 白 85%（13.4:1） | `WindowTextColorKey` | 正文 | 4.5:1 |
| Text.Secondary | `Primer.textMuted`、`Primer.textFaint` | colorTextSecondary | 黑 65%（7.0:1） | 白 65%（8.2:1） | `WindowTextColorKey` | 次要文字、时间戳、占位符 | 4.5:1 |
| Text.Disabled | `Ant.colorTextDisabled` | colorTextDisabled | 黑 25% | 白 25% | `GrayTextColorKey` | 只用于禁用 | 豁免 |
| Accent.Text | `Primer.accent` | blue-7 | #0958D9（6.2:1） | #3C89E8（5.2:1） | `WindowTextColorKey` | 链接、强调文字 | 4.5:1 |
| Accent.Graphic | `Ant.colorPrimary` | colorPrimary | #1677FF（4.1:1） | #1668DC（3.6:1） | `HighlightColorKey` | 选中条、勾选、开关、进度、焦点边框 | 3:1 |
| Primary button | `Primer.primaryBg` / `Primer.primaryText` | colorPrimary | #0958D9 + 白（6.2:1） | #1668DC + 白（5.2:1） | `ControlColorKey` / `ControlTextColorKey` | 主要按钮（审批按钮不用） | 4.5:1 |
| Hover text | `Ant.colorLinkHover` | colorLinkHover（加深） | #003EB3 | #65A9F3 | `HotTrackColorKey` | 默认按钮和链接悬停时的文字 | 4.5:1 |
| OnAccent | `Primer.onAccent`、`Ant.colorWhite` | colorTextLightSolid | #FFFFFF | #141414 / #FFFFFF | `WindowColorKey` / `HighlightTextColorKey` | 主色上的文字、勾号 | 文字 4.5:1，图形 3:1 |
| Status.Success | `Primer.ok` / `Primer.okBg` | green-8 | #237804（5.6:1）/ #F6FFED | #49AA19（6.2:1）/ #162312 | `WindowTextColorKey` 加图标 | 成功、已完成 | 4.5:1 |
| Status.Warning | `Primer.warn` / `Primer.warnBg` | gold-9 | #874D00（6.8:1）/ #FFFBE6 | #D89614（7.3:1）/ #2B2111 | `WindowTextColorKey` 加图标 | 警告、待处理徽章、倒计时最后 20 秒 | 4.5:1 |
| Status.Error | `Primer.diffRed`、`Ant.colorError` / `Ant.colorErrorBg` | red-7 | #CF1322（5.6:1）、#D9363E（4.6:1）/ #FFF2F0 | #E86E6B（6.0:1）/ #2C1618 | `WindowTextColorKey` 加图标 | 错误、破坏性标记 | 4.5:1 |
| Danger.Solid | `Ant.colorErrorSolid` | colorError 实心 | #D9363E + 白（4.6:1） | #AD393A + 白（6.1:1） | `ControlColorKey` / `ControlTextColorKey` | 确认对话框的提交按钮 | 4.5:1 |
| Status.Info | `Primer.noteBg` / `Primer.noteBorder` | colorInfoBg / colorInfoBorder | #E6F4FF / #91CAFF | #111A2C / #15325B | `WindowColorKey` / `WindowTextColorKey` | 提示框 | 文字 4.5:1 |
| Tooltip | `Ant.colorBgSpotlight` | colorBgSpotlight | 黑 85% + 白 | #424242 + 白 | `InfoColorKey` / `InfoTextColorKey` | 工具提示 | 4.5:1 |
| Focus.Outer / Inner | `Primer.focusOuter` / `Primer.focusInner` | 2 epx 主色环 | #1677FF / #FFFFFF | #3C89E8 / #141414 | `WindowTextColorKey` / `WindowColorKey` | 焦点视觉（KEY-03） | 与相邻色 3:1 |

不用 `Effect`；阴影只以三层半透明边框画在弹出层上（`Ant.boxShadowKey`、`Ant.boxShadowUmbra`、`Ant.boxShadowAmbient`），高对比下隐藏。

---

## 附录 B 与 TIA 的偏差

| 方面 | TIA | 本项目 | 理由 |
|---|---|---|---|
| 字体 | 随包的 Noto Sans SC 和 JetBrains Mono，编进程序集（TIA 偏差 UI-37）；TIA 的 TYP-01 建议系统字体 | 全部文字用 Maple Mono CN，从程序目录私有加载，回退到 Segoe UI Variable Text、Microsoft YaHei UI（TYP-01）；布局按等宽字体留余量（TYP-10）；关闭连字（TYP-11） | 项目负责人的决定（ADR-0017 第 4 条） |
| 审批按钮 | "批准执行"可以用主要样式（TIA 的 APR-04） | 两个按钮样式相同，都不用主要或危险样式，没有快捷键和访问键（APR-02） | ADR-0014 第 6 条，ADR-0017 第 5 条 |
| 防误触 | AI 切页或定位后 500 毫秒（TIA 的 AI-02） | 显示不同的问题、窗口变为可见、切到审批队列页之后 500 毫秒（APR-04） | ADR-0014 第 6 条 |
| 新问题 | 工作台面板发起的请求自动打开审批卡（TIA 的 APR-02） | 从不自动切页或激活窗口，只提醒（APR-14） | 问题都来自 AI；不抢焦点（M5 设计 4.4） |
| AI 控制界面 | control 管道和 AI-01 到 AI-08 | 没有（0.4 节） | M5 设计第 2 节的非目标 |
| 关闭审批、期限 | 设置抽屉中可以关闭审批（确认后），超时可选 60 / 120 / 300 秒 | 不能关闭审批，不能改期限；`config.json` 只读显示（APR-16、NAV-10） | SEC-02 |
| 审批的位置 | 480 epx 的待批抽屉 | 审批队列是第一个页面，列表加详情（APR-06） | 审批是 Workbench 的主要功能（M5 设计 4.6） |
| 类型层级与圆角 | 规范正文写 Fluent 的字号（12 / 14 / 18 / 20 / 28）和 4 / 8 圆角，主题实现的是 Ant 的取值（TIA 偏差 UI-39） | 直接按主题中 Ant 的取值（附录 A） | 规范与实现一致 |
| 对话框按钮顺序 | 规范正文按 Windows 的顺序（提交在左），实现按 Ant（提交在右，TIA 偏差 C.0） | 按 Ant：安全按钮在左，提交按钮在最右（DLG-05） | 与 TIA 的实际观感一致 |
| 产品名 | "TIA 工作台" | 中英文都写 Workbench（LOC-07） | 与本项目的文档一致 |
| 完整参数 | 审批卡可以展开完整参数或生成计划（TIA 的 APR-03） | 问题只带参数摘要（哈希）和变更清单，不显示参数（APR-07） | 协议只传这些（ADR-0014 第 3 条）；变更清单就是要批准的内容 |

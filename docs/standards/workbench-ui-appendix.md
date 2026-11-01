# Workbench 界面规范：依据与附录 B

规则与 Token 见 [workbench-ui.md](workbench-ui.md)。本文件保留逐条来源和 TIA 偏差，规则的要求与理由仍以正文为准。引文沿用原记录，本次未重新核验外部页面。

## 总体来源

依据：项目负责人的 TIA_Portal_Openness_MCP 项目（下称 TIA）的界面规范 `docs/development/design-spec/workbench-ui.md` v0.2（MIT 许可，按 TIA 的提交 `38ca2883` 阅读）和 [ADR-0017](../adr/0017-workbench-wpf-tia-theme.md)。规则的结构、编号方法和以 WCAG 2.2 AA 为底线的做法沿用 TIA，文字按本项目重写；TIA 特有的内容不收录（0.4 节），有意的不同列在附录 B。

Microsoft Windows 应用设计指南：<https://learn.microsoft.com/windows/apps/design/>。

## 逐条依据

| 规则 | 原依据（保留原顺序，“同上”指前项来源） |
|---|---|
| <a id="pri-01"></a>PRI-01 | 项目规则（ADR-0011 的决策驱动因素，ADR-0014 第 6 条）。 |
| <a id="pri-02"></a>PRI-02 | [Navigation basics › Principles of good navigation（Consistency）](https://learn.microsoft.com/en-us/windows/apps/design/basics/navigation-basics#principles-of-good-navigation)。 |
| <a id="pri-03"></a>PRI-03 | [Accessibility checklist](https://learn.microsoft.com/en-us/windows/apps/design/accessibility/accessibility-checklist)。 |
| <a id="pri-04"></a>PRI-04 | [Contrast themes › Hard-coded colors](https://learn.microsoft.com/en-us/windows/apps/design/accessibility/high-contrast-themes#hard-coded-colors)；[.NET Accessibility Best Practices › Visual UI design](https://learn.microsoft.com/en-us/dotnet/framework/ui-automation/accessibility-best-practices#visual-ui-design)。 |
| <a id="pri-05"></a>PRI-05 | [Accessibility checklist › 4](https://learn.microsoft.com/en-us/windows/apps/design/accessibility/accessibility-checklist)；[Color in Windows › Usability](https://learn.microsoft.com/en-us/windows/apps/design/signature-experiences/color#usability)。 |
| <a id="pri-06"></a>PRI-06 | [Expose basic accessibility information › Accessible names and localization](https://learn.microsoft.com/en-us/windows/apps/design/accessibility/basic-accessibility-information#accessible-names-and-localization)。 |
| <a id="pri-07"></a>PRI-07 | 项目规则（M5 设计 4.4 的"提醒"，ADR-0014 第 6 条）。 |
| <a id="pri-08"></a>PRI-08 | 项目规则（SEC-23）。 |
| <a id="pri-09"></a>PRI-09 | [Commanding basics › When to confirm or undo actions](https://learn.microsoft.com/en-us/windows/apps/design/basics/commanding-basics#when-to-confirm-or-undo-actions)。 |
| <a id="lay-01"></a>LAY-01 | [Screen sizes and breakpoints › Breakpoints](https://learn.microsoft.com/en-us/windows/apps/design/layout/screen-sizes-and-breakpoints-for-responsive-design#breakpoints)；[.NET Accessibility Best Practices › Visual UI design](https://learn.microsoft.com/en-us/dotnet/framework/ui-automation/accessibility-best-practices#visual-ui-design)。 |
| <a id="lay-02"></a>LAY-02 | [Support snap layouts for desktop apps on Windows 11](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/ui/apply-snap-layout-menu)。 |
| <a id="lay-03"></a>LAY-03 | [Screen sizes and breakpoints](https://learn.microsoft.com/en-us/windows/apps/design/layout/screen-sizes-and-breakpoints-for-responsive-design#breakpoints)；[WCAG 2.2 › 1.4.10 Reflow](https://www.w3.org/TR/WCAG22/#reflow)。 |
| <a id="lay-04"></a>LAY-04 | [High DPI Desktop Application Development on Windows](https://learn.microsoft.com/en-us/windows/win32/hidpi/high-dpi-desktop-application-development-on-windows#dpi-awareness-mode)。 |
| <a id="lay-05"></a>LAY-05 | [Windows app title bar › Standard design](https://learn.microsoft.com/en-us/windows/apps/design/basics/titlebar-design#standard-design)；[Support snap layouts](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/ui/apply-snap-layout-menu)。 |
| <a id="lay-06"></a>LAY-06 | [Screen sizes and breakpoints › Multiples of four](https://learn.microsoft.com/en-us/windows/apps/design/layout/screen-sizes-and-breakpoints-for-responsive-design#multiples-of-four)。 |
| <a id="lay-07"></a>LAY-07 | [Content layout and spacing › Spacing and gutters](https://learn.microsoft.com/en-us/windows/apps/design/basics/content-basics#spacing-and-gutters)。 |
| <a id="lay-08"></a>LAY-08 | [Globalization › Guidelines for globalization](https://learn.microsoft.com/en-us/windows/apps/design/globalizing/globalizing-portal#guidelines-for-globalization)；[Accessible text requirements › Text font size and scale](https://learn.microsoft.com/en-us/windows/apps/design/accessibility/accessible-text-requirements#text-font-size-and-scale)。 |
| <a id="lay-09"></a>LAY-09 | [Typography › Typography best practices in Windows 11](https://learn.microsoft.com/en-us/windows/apps/design/signature-experiences/typography#typography-best-practices-in-windows-11)。 |
| <a id="lay-10"></a>LAY-10 | 项目规则（ADR-0017）；[Geometry in Windows 11 › Rounded corners](https://learn.microsoft.com/en-us/windows/apps/design/signature-experiences/geometry#rounded-corners)。 |
| <a id="lay-11"></a>LAY-11 | [Contrast themes › Borders](https://learn.microsoft.com/en-us/windows/apps/design/accessibility/high-contrast-themes#borders)。 |
| <a id="lay-12"></a>LAY-12 | [Buttons › Recommendations](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/buttons#recommendations)；[WCAG 2.2 › 2.5.8 Target Size (Minimum)](https://www.w3.org/TR/WCAG22/#target-size-minimum)。 |
| <a id="typ-02"></a>TYP-02 | [Typography in Windows › Type ramp](https://learn.microsoft.com/en-us/windows/apps/design/signature-experiences/typography#type-ramp)；项目规则（ADR-0017）。 |
| <a id="typ-03"></a>TYP-03 | [Typography in Windows › Type ramp](https://learn.microsoft.com/en-us/windows/apps/design/signature-experiences/typography#type-ramp)。 |
| <a id="typ-04"></a>TYP-04 | [Typography › Typography best practices in Windows 11（Minimum values）](https://learn.microsoft.com/en-us/windows/apps/design/signature-experiences/typography#typography-best-practices-in-windows-11)。 |
| <a id="typ-05"></a>TYP-05 | [Typography › Typography best practices in Windows 11](https://learn.microsoft.com/en-us/windows/apps/design/signature-experiences/typography#typography-best-practices-in-windows-11)。 |
| <a id="typ-06"></a>TYP-06 | [Typography › Clipping and ellipses](https://learn.microsoft.com/en-us/windows/apps/design/signature-experiences/typography#clipping-and-ellipses)；[Keyboard accessibility](https://learn.microsoft.com/en-us/windows/apps/design/accessibility/keyboard-accessibility#keyboard-alternatives-to-pointer-actions-and-events-for-specific-control-elements)。 |
| <a id="typ-07"></a>TYP-07 | [Typography › Character count](https://learn.microsoft.com/en-us/windows/apps/design/signature-experiences/typography#character-count)。 |
| <a id="typ-08"></a>TYP-08 | [Writing style › Style conventions](https://learn.microsoft.com/en-us/windows/apps/design/style/writing-style#style-conventions)；项目规则。 |
| <a id="typ-09"></a>TYP-09 | [Globalization › Guidelines for globalization](https://learn.microsoft.com/en-us/windows/apps/design/globalizing/globalizing-portal#guidelines-for-globalization)。 |
| <a id="typ-10"></a>TYP-10 | 项目规则（ADR-0017 后果）；[Globalization › Guidelines for globalization](https://learn.microsoft.com/en-us/windows/apps/design/globalizing/globalizing-portal#guidelines-for-globalization)。 |
| <a id="typ-11"></a>TYP-11 | 项目规则（S18"字体"，SEC-23）。 |
| <a id="col-01"></a>COL-01 | [Contrast themes › Hard-coded colors](https://learn.microsoft.com/en-us/windows/apps/design/accessibility/high-contrast-themes#hard-coded-colors)。 |
| <a id="col-02"></a>COL-02 | [Contrast themes](https://learn.microsoft.com/en-us/windows/apps/design/accessibility/high-contrast-themes)；[.NET Accessibility Best Practices › Visual UI design](https://learn.microsoft.com/en-us/dotnet/framework/ui-automation/accessibility-best-practices#visual-ui-design)。 |
| <a id="col-03"></a>COL-03 | [Contrast themes › Contrast colors](https://learn.microsoft.com/en-us/windows/apps/design/accessibility/high-contrast-themes#contrast-colors)。 |
| <a id="col-04"></a>COL-04 | [Accessible text requirements › Contrast ratios](https://learn.microsoft.com/en-us/windows/apps/design/accessibility/accessible-text-requirements#contrast-ratios)；[WCAG 2.2 › 1.4.3](https://www.w3.org/TR/WCAG22/#contrast-minimum)。 |
| <a id="col-05"></a>COL-05 | [WCAG 2.2 › 1.4.11 Non-text Contrast](https://www.w3.org/TR/WCAG22/#non-text-contrast)。 |
| <a id="col-06"></a>COL-06 | [Color in Windows › Usability](https://learn.microsoft.com/en-us/windows/apps/design/signature-experiences/color#usability)。 |
| <a id="col-07"></a>COL-07 | 项目规则（ADR-0017）；[Color in Windows › Accent color](https://learn.microsoft.com/en-us/windows/apps/design/signature-experiences/color#accent-color)。 |
| <a id="col-08"></a>COL-08 | [InfoBar › Severity](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/infobar#severity)。 |
| <a id="col-09"></a>COL-09 | [Color in Windows › Color modes and themes](https://learn.microsoft.com/en-us/windows/apps/design/signature-experiences/color#color-modes-and-themes)；M5 设计 4.4。 |
| <a id="col-10"></a>COL-10 | [Windows app title bar › Standard design](https://learn.microsoft.com/en-us/windows/apps/design/basics/titlebar-design#standard-design)。 |
| <a id="col-11"></a>COL-11 | [Contrast themes](https://learn.microsoft.com/en-us/windows/apps/design/accessibility/high-contrast-themes)。 |
| <a id="ico-01"></a>ICO-01 | [Typography in Windows › Fonts](https://learn.microsoft.com/en-us/windows/apps/design/signature-experiences/typography#fonts)；项目规则。 |
| <a id="ico-02"></a>ICO-02 | [Expose basic accessibility information › Accessible name](https://learn.microsoft.com/en-us/windows/apps/design/accessibility/basic-accessibility-information#accessible-name)。 |
| <a id="ico-03"></a>ICO-03 | [Buttons › Button text](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/buttons#button-text)；项目规则（同 TIA）。 |
| <a id="ico-04"></a>ICO-04 | [InfoBar › Severity](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/infobar#severity)。 |
| <a id="ico-05"></a>ICO-05 | [Windows app title bar › Caption controls](https://learn.microsoft.com/en-us/windows/apps/design/basics/titlebar-design#standard-design)。 |
| <a id="nav-01"></a>NAV-01 | [Navigation basics › Use the right structure](https://learn.microsoft.com/en-us/windows/apps/design/basics/navigation-basics#use-the-right-structure)。 |
| <a id="nav-02"></a>NAV-02 | 项目规则（同 TIA）。 |
| <a id="nav-03"></a>NAV-03 | [Navigation basics › Principles of good navigation](https://learn.microsoft.com/en-us/windows/apps/design/basics/navigation-basics#principles-of-good-navigation)；[WCAG 2.2 › 2.4.2 Page Titled](https://www.w3.org/TR/WCAG22/#page-titled)。 |
| <a id="nav-04"></a>NAV-04 | [Keyboard interactions › Control group](https://learn.microsoft.com/en-us/windows/apps/develop/input/keyboard-interactions#control-group)。 |
| <a id="nav-05"></a>NAV-05 | [Navigation basics › Principles of good navigation](https://learn.microsoft.com/en-us/windows/apps/design/basics/navigation-basics#principles-of-good-navigation)。 |
| <a id="nav-06"></a>NAV-06 | [Navigation basics › Consistency](https://learn.microsoft.com/en-us/windows/apps/design/basics/navigation-basics#principles-of-good-navigation)。 |
| <a id="nav-07"></a>NAV-07 | [Keyboard interactions › Initial focus](https://learn.microsoft.com/en-us/windows/apps/develop/input/keyboard-interactions#initial-focus)；项目规则（ADR-0014 第 6 条）。 |
| <a id="nav-08"></a>NAV-08 | [Keyboard accessibility › Keyboard navigation between application panes with F6](https://learn.microsoft.com/en-us/windows/apps/design/accessibility/keyboard-accessibility#keyboard-navigation-between-application-panes-with-f6)。 |
| <a id="nav-09"></a>NAV-09 | 项目规则（M5 设计 4.4"进程"）。 |
| <a id="nav-10"></a>NAV-10 | 项目规则（M5 设计第 2 节的非目标和 4.4，SEC-02）。 |
| <a id="cmd-01"></a>CMD-01 | [Buttons › Button text](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/buttons#button-text)；[Writing style › Buttons](https://learn.microsoft.com/en-us/windows/apps/design/style/writing-style#buttons)。 |
| <a id="cmd-02"></a>CMD-02 | [Buttons › Recommendations](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/buttons#recommendations)。 |
| <a id="cmd-03"></a>CMD-03 | [Buttons › Recommendations](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/buttons#recommendations)；[Dialog controls › General guidelines](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/dialogs-and-flyouts/dialogs#general-guidelines)。 |
| <a id="cmd-04"></a>CMD-04 | [Commanding basics › Provide command feedback](https://learn.microsoft.com/en-us/windows/apps/design/basics/commanding-basics#provide-command-feedback)。 |
| <a id="cmd-05"></a>CMD-05 | [Keyboard interactions › Accelerators](https://learn.microsoft.com/en-us/windows/apps/develop/input/keyboard-interactions#accelerators)。 |
| <a id="cmd-06"></a>CMD-06 | 项目规则（ADR-0011 第 4 条，SEC-24）。 |
| <a id="cmd-07"></a>CMD-07 | [Expose basic accessibility information › Role and value](https://learn.microsoft.com/en-us/windows/apps/design/accessibility/basic-accessibility-information#role-and-value)。 |
| <a id="cmd-08"></a>CMD-08 | [Toggle switches › Choosing between toggle switch and check box](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/toggles#choosing-between-toggle-switch-and-check-box)。 |
| <a id="cmd-09"></a>CMD-09 | [WCAG 2.2 › 3.2.4 Consistent Identification](https://www.w3.org/TR/WCAG22/#consistent-identification)。 |
| <a id="cmd-10"></a>CMD-10 | [Nested UI in list items › Options for secondary actions](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/nested-ui#options-for-secondary-actions)。 |
| <a id="cmd-11"></a>CMD-11 | [User Account Control（Win32 UX Guide）](https://learn.microsoft.com/en-us/windows/win32/uxguide/winenv-uac)；项目规则（ADR-0018，SEC-29）。 |
| <a id="ctl-01"></a>CTL-01 | [.NET Accessibility Best Practices › Programmatic access](https://learn.microsoft.com/en-us/dotnet/framework/ui-automation/accessibility-best-practices#programmatic-access)。 |
| <a id="ctl-02"></a>CTL-02 | [Visual feedback › Recommendations](https://learn.microsoft.com/en-us/windows/apps/develop/input/guidelines-for-visualfeedback#recommendations)。 |
| <a id="ctl-03"></a>CTL-03 | [Expose basic accessibility information › Labels and LabeledBy](https://learn.microsoft.com/en-us/windows/apps/design/accessibility/basic-accessibility-information#labels-and-labeledby)。 |
| <a id="ctl-04"></a>CTL-04 | [Nested UI in list items](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/nested-ui#nested-ui-handling)；[Keyboard interactions › Enter key](https://learn.microsoft.com/en-us/windows/apps/develop/input/keyboard-interactions#enter-key)。 |
| <a id="ctl-05"></a>CTL-05 | [Expose basic accessibility information › Accessible names from dynamic data](https://learn.microsoft.com/en-us/windows/apps/design/accessibility/basic-accessibility-information#accessible-names-from-dynamic-data)。 |
| <a id="ctl-06"></a>CTL-06 | [Collections and lists](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/lists)。 |
| <a id="ctl-07"></a>CTL-07 | [Expose basic accessibility information › Role and value](https://learn.microsoft.com/en-us/windows/apps/design/accessibility/basic-accessibility-information#role-and-value)。 |
| <a id="ctl-08"></a>CTL-08 | [Nested UI in list items](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/nested-ui#nested-ui-handling)。 |
| <a id="ctl-09"></a>CTL-09 | [Contrast themes › Best practices](https://learn.microsoft.com/en-us/windows/apps/design/accessibility/high-contrast-themes#best-practices)。 |
| <a id="ctl-10"></a>CTL-10 | [Buttons › Button text](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/buttons#button-text)。 |
| <a id="ctl-11"></a>CTL-11 | [WCAG 2.2 › 2.5.8 Target Size (Minimum)](https://www.w3.org/TR/WCAG22/#target-size-minimum)。 |
| <a id="ctl-12"></a>CTL-12 | 项目规则（SEC-23；审计页为本规范的补充）。 |
| <a id="ctl-13"></a>CTL-13 | 项目规则（M5 设计 4.4 和 Q7）。 |
| <a id="ctl-14"></a>CTL-14 | 项目规则（SEC-27、SEC-30）；[WCAG 2.2 › 3.3.8 Accessible Authentication (Minimum)](https://www.w3.org/TR/WCAG22/#accessible-authentication-minimum)（可以复制和粘贴，不必记忆）。 |
| <a id="dlg-01"></a>DLG-01 | [Dialog controls › General guidelines](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/dialogs-and-flyouts/dialogs#general-guidelines)；[AutomationProperties.IsDialogProperty](https://learn.microsoft.com/en-us/dotnet/api/system.windows.automation.automationproperties.isdialogproperty)。 |
| <a id="dlg-02"></a>DLG-02 | [Keyboard interactions › Esc key](https://learn.microsoft.com/en-us/windows/apps/develop/input/keyboard-interactions#esc-key)；[Dialog controls › CloseButton](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/dialogs-and-flyouts/dialogs#closebutton)。 |
| <a id="dlg-03"></a>DLG-03 | [Dialog controls › Provide a safe action](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/dialogs-and-flyouts/dialogs#provide-a-safe-action)。 |
| <a id="dlg-04"></a>DLG-04 | [Confirmations › Guidelines](https://learn.microsoft.com/en-us/windows/win32/uxguide/mess-confirm#guidelines)；[Dialog controls › Confirmation dialogs](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/dialogs-and-flyouts/dialogs#confirmation-dialogs-okcancel)。 |
| <a id="dlg-05"></a>DLG-05 | 项目规则（ADR-0017）。 |
| <a id="dlg-06"></a>DLG-06 | [Writing style › Dialogs](https://learn.microsoft.com/en-us/windows/apps/design/style/writing-style#dialogs)。 |
| <a id="dlg-07"></a>DLG-07 | [Confirmations › Guidelines](https://learn.microsoft.com/en-us/windows/win32/uxguide/mess-confirm#guidelines)。 |
| <a id="dlg-08"></a>DLG-08 | [Dialog controls › General guidelines](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/dialogs-and-flyouts/dialogs#general-guidelines)。 |
| <a id="dlg-09"></a>DLG-09 | [Commanding basics › When to confirm or undo actions](https://learn.microsoft.com/en-us/windows/apps/design/basics/commanding-basics#when-to-confirm-or-undo-actions)。 |
| <a id="dlg-10"></a>DLG-10 | [Keyboard interactions › Esc key](https://learn.microsoft.com/en-us/windows/apps/develop/input/keyboard-interactions#esc-key)；[WCAG 2.2 › 2.4.11 Focus Not Obscured (Minimum)](https://www.w3.org/TR/WCAG22/#focus-not-obscured-minimum)。 |
| <a id="dlg-11"></a>DLG-11 | 项目规则（M5 设计 4.4"进程"，ADR-0014 第 4 条，SEC-29）。 |
| <a id="dlg-12"></a>DLG-12 | 项目规则（M5 设计 4.4 第 3 项、4.7、4.11）。 |
| <a id="dlg-13"></a>DLG-13 | 项目规则（SEC-27，M5 设计 4.12）。 |
| <a id="apr-01"></a>APR-01 | 项目规则（ADR-0014 第 6 条，M5 设计第 2 节的非目标和 4.6，SEC-24）。 |
| <a id="apr-02"></a>APR-02 | 项目规则（ADR-0011 第 4 条，ADR-0014 第 6 条，ADR-0017 第 5 条，SEC-24）。 |
| <a id="apr-03"></a>APR-03 | 项目规则（ADR-0014 第 6 条）；[Keyboard interactions › Initial focus](https://learn.microsoft.com/en-us/windows/apps/develop/input/keyboard-interactions#initial-focus)。 |
| <a id="apr-04"></a>APR-04 | 项目规则（ADR-0011 第 4 条，ADR-0014 第 6 条，SEC-24；"切到审批队列页"是本规范的补充）。 |
| <a id="apr-05"></a>APR-05 | 项目规则（本规范对 SEC-24 的补充）。 |
| <a id="apr-06"></a>APR-06 | 项目规则（M5 设计 4.6）；[Nested UI in list items](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/nested-ui#nested-ui-handling)。 |
| <a id="apr-07"></a>APR-07 | 项目规则（M5 设计 4.6，M3 设计 4.6，ADR-0014 第 3、6 条）。 |
| <a id="apr-08"></a>APR-08 | 项目规则（SEC-23，M5 设计第 5 节）。 |
| <a id="apr-09"></a>APR-09 | 项目规则（M5 设计 4.6，ADR-0013，ADR-0005 的级别）。 |
| <a id="apr-10"></a>APR-10 | 项目规则（M5 设计 4.4 的 S16、4.6）；[WCAG 2.2 › 2.2.1 Timing Adjustable](https://www.w3.org/TR/WCAG22/#timing-adjustable)。 |
| <a id="apr-11"></a>APR-11 | 项目规则（M5 设计 4.4 第 1 项、4.6，ADR-0014 第 3、4 条）。 |
| <a id="apr-12"></a>APR-12 | 项目规则（ADR-0014 第 3 条，M5 设计 4.6）。 |
| <a id="apr-13"></a>APR-13 | 项目规则（ADR-0014 第 3 条和后果，M5 设计 4.6）。 |
| <a id="apr-14"></a>APR-14 | 项目规则（M5 设计 4.4 的"提醒"，ADR-0014 第 6 条）。 |
| <a id="apr-15"></a>APR-15 | 项目规则（ADR-0014 第 4 条）。 |
| <a id="apr-16"></a>APR-16 | 项目规则（SEC-02，M5 设计第 2 节的非目标，ADR-0005 的后果）。 |
| <a id="sts-01"></a>STS-01 | [Keyboard accessibility](https://learn.microsoft.com/en-us/windows/apps/design/accessibility/keyboard-accessibility#keyboard-alternatives-to-pointer-actions-and-events-for-specific-control-elements)。 |
| <a id="sts-02"></a>STS-02 | [What's new in accessibility in .NET Framework（4.7.1 LiveRegion）](https://learn.microsoft.com/en-us/dotnet/framework/whats-new/whats-new-in-accessibility)；[WCAG 2.2 › 4.1.3 Status Messages](https://www.w3.org/TR/WCAG22/#status-messages)。 |
| <a id="sts-03"></a>STS-03 | [Progress controls › Types of progress](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/progress-controls#types-of-progress)。 |
| <a id="sts-04"></a>STS-04 | 项目规则；[Commanding basics › Provide command feedback](https://learn.microsoft.com/en-us/windows/apps/design/basics/commanding-basics#provide-command-feedback)。 |
| <a id="sts-05"></a>STS-05 | [Commanding basics › Provide command feedback](https://learn.microsoft.com/en-us/windows/apps/design/basics/commanding-basics#provide-command-feedback)。 |
| <a id="sts-06"></a>STS-06 | [InfoBar › Is this the right control?](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/infobar#is-this-the-right-control)。 |
| <a id="sts-07"></a>STS-07 | [WCAG 2.2 › 2.4.11](https://www.w3.org/TR/WCAG22/#focus-not-obscured-minimum)。 |
| <a id="sts-08"></a>STS-08 | [InfoBar › Recommendations](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/infobar#recommendations)；[WCAG 2.2 › 2.3.1](https://www.w3.org/TR/WCAG22/#three-flashes-or-below-threshold)。 |
| <a id="sts-09"></a>STS-09 | 项目规则（M5 设计 4.4，SEC-23）。 |
| <a id="sts-10"></a>STS-10 | [Expose basic accessibility information › Accessible name](https://learn.microsoft.com/en-us/windows/apps/design/accessibility/basic-accessibility-information#accessible-name)。 |
| <a id="sts-11"></a>STS-11 | 项目规则（ADR-0014 第 2、3 条）。 |
| <a id="sts-12"></a>STS-12 | 项目规则（SEC-29，M5 设计 4.12）。 |
| <a id="err-01"></a>ERR-01 | [Writing style › Error messages](https://learn.microsoft.com/en-us/windows/apps/design/style/writing-style#error-messages)；[Error Messages › Text](https://learn.microsoft.com/en-us/windows/win32/uxguide/mess-error#text)。 |
| <a id="err-02"></a>ERR-02 | [Error Messages › Guidelines](https://learn.microsoft.com/en-us/windows/win32/uxguide/mess-error#guidelines)。 |
| <a id="err-03"></a>ERR-03 | [WCAG 2.2 › 3.3.1 Error Identification](https://www.w3.org/TR/WCAG22/#error-identification)。 |
| <a id="err-04"></a>ERR-04 | [InfoBar](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/infobar)。 |
| <a id="err-05"></a>ERR-05 | 项目规则；[Writing style](https://learn.microsoft.com/en-us/windows/apps/design/style/writing-style)。 |
| <a id="err-06"></a>ERR-06 | [Progress controls](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/progress-controls#progress-controls-best-practices)。 |
| <a id="err-07"></a>ERR-07 | [Color in Windows › Usability](https://learn.microsoft.com/en-us/windows/apps/design/signature-experiences/color#usability)。 |
| <a id="err-08"></a>ERR-08 | 项目规则（M5 设计 4.4、4.11）。 |
| <a id="key-01"></a>KEY-01 | [Keyboard accessibility](https://learn.microsoft.com/en-us/windows/apps/design/accessibility/keyboard-accessibility#keyboard-alternatives-to-pointer-actions-and-events-for-specific-control-elements)；[WCAG 2.2 › 2.5.2 Pointer Cancellation](https://www.w3.org/TR/WCAG22/#pointer-cancellation)。 |
| <a id="key-02"></a>KEY-02 | [Keyboard interactions › Tab stops](https://learn.microsoft.com/en-us/windows/apps/develop/input/keyboard-interactions#tab-stops)。 |
| <a id="key-03"></a>KEY-03 | [Visual feedback › High visibility focus visuals](https://learn.microsoft.com/en-us/windows/apps/develop/input/guidelines-for-visualfeedback#high-visibility-focus-visuals)；[Styling for Focus in Controls, and FocusVisualStyle](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/styling-for-focus-in-controls-and-focusvisualstyle)。 |
| <a id="key-04"></a>KEY-04 | [Keyboard interactions › Esc key](https://learn.microsoft.com/en-us/windows/apps/develop/input/keyboard-interactions#esc-key)。 |
| <a id="key-05"></a>KEY-05 | [Keyboard interactions › Enter key](https://learn.microsoft.com/en-us/windows/apps/develop/input/keyboard-interactions#enter-key)。 |
| <a id="key-06"></a>KEY-06 | [Keyboard interactions › Access keys](https://learn.microsoft.com/en-us/windows/apps/develop/input/keyboard-interactions#access-keys)。 |
| <a id="key-08"></a>KEY-08 | [Keyboard interactions › Initial focus](https://learn.microsoft.com/en-us/windows/apps/develop/input/keyboard-interactions#initial-focus)。 |
| <a id="key-09"></a>KEY-09 | [Keyboard interactions › Popup UI](https://learn.microsoft.com/en-us/windows/apps/develop/input/keyboard-interactions#popup-ui)。 |
| <a id="key-10"></a>KEY-10 | [Keyboard interactions › Control group](https://learn.microsoft.com/en-us/windows/apps/develop/input/keyboard-interactions#control-group)。 |
| <a id="key-11"></a>KEY-11 | [.NET Accessibility Best Practices › Navigation](https://learn.microsoft.com/en-us/dotnet/framework/ui-automation/accessibility-best-practices#navigation)。 |
| <a id="loc-01"></a>LOC-01 | [Globalization and localization](https://learn.microsoft.com/en-us/windows/apps/design/globalizing/globalizing-portal)。 |
| <a id="loc-02"></a>LOC-02 | [Globalization › Guidelines for globalization](https://learn.microsoft.com/en-us/windows/apps/design/globalizing/globalizing-portal#guidelines-for-globalization)。 |
| <a id="loc-03"></a>LOC-03 | 同上。 |
| <a id="loc-04"></a>LOC-04 | [Globalization › Guidelines for globalization](https://learn.microsoft.com/en-us/windows/apps/design/globalizing/globalizing-portal#guidelines-for-globalization)。 |
| <a id="loc-05"></a>LOC-05 | 同上（Don't use language to assume a user's region）。 |
| <a id="loc-06"></a>LOC-06 | 项目规则。 |
| <a id="loc-08"></a>LOC-08 | [Accessible names and localization](https://learn.microsoft.com/en-us/windows/apps/design/accessibility/basic-accessibility-information#accessible-names-and-localization)。 |
| <a id="loc-09"></a>LOC-09 | [Globalization and localization](https://learn.microsoft.com/en-us/windows/apps/design/globalizing/globalizing-portal)。 |
| <a id="loc-10"></a>LOC-10 | 项目规则（M3 设计 4.6，M5 设计 4.4）。 |
| <a id="txt-01"></a>TXT-01 | [Writing style › Voice and tone principles](https://learn.microsoft.com/en-us/windows/apps/design/style/writing-style#voice-and-tone-principles)。 |
| <a id="txt-02"></a>TXT-02 | [Writing style › Style conventions](https://learn.microsoft.com/en-us/windows/apps/design/style/writing-style#style-conventions)；[Error Messages › Text](https://learn.microsoft.com/en-us/windows/win32/uxguide/mess-error#text)。 |
| <a id="txt-03"></a>TXT-03 | [Writing style › Accessibility and localization](https://learn.microsoft.com/en-us/windows/apps/design/style/writing-style#accessibility-and-localization)。 |
| <a id="txt-04"></a>TXT-04 | [Writing style › Style conventions](https://learn.microsoft.com/en-us/windows/apps/design/style/writing-style#style-conventions)。 |
| <a id="txt-05"></a>TXT-05 | [Writing style › Dialogs](https://learn.microsoft.com/en-us/windows/apps/design/style/writing-style#dialogs)。 |
| <a id="txt-06"></a>TXT-06 | [Confirmations › Text](https://learn.microsoft.com/en-us/windows/win32/uxguide/mess-confirm#text)。 |
| <a id="txt-07"></a>TXT-07 | [Writing style › Style conventions](https://learn.microsoft.com/en-us/windows/apps/design/style/writing-style#style-conventions)。 |
| <a id="txt-08"></a>TXT-08 | 项目规则（ADR-0014 第 7 条）。 |
| <a id="txt-09"></a>TXT-09 | 项目规则（ADR-0011 第 4 条，M3 设计 4.6）。 |

## 其他引用

| 定位 | 来源 |
|---|---|
| <a id="ref-01"></a>ref-01 | [WCAG 2.2](https://www.w3.org/TR/WCAG22/) |
| <a id="ref-02"></a>ref-02 | [WCAG2ICT](https://www.w3.org/TR/wcag2ict-22/) |
| <a id="ref-03"></a>ref-03 | [Typography in Windows › Fonts for non-Latin languages](https://learn.microsoft.com/en-us/windows/apps/design/signature-experiences/typography#languages) |
| <a id="ref-04"></a>ref-04 | [Keyboard interactions › Common keyboard shortcuts](https://learn.microsoft.com/en-us/windows/apps/develop/input/keyboard-interactions#common-keyboard-shortcuts) |
| <a id="ref-05"></a>ref-05 | [WCAG2ICT](https://www.w3.org/TR/wcag2ict-22/) |
| <a id="ref-06"></a>ref-06 | [1.1.1 Non-text Content](https://www.w3.org/TR/WCAG22/#non-text-content) |
| <a id="ref-07"></a>ref-07 | [1.3.1 Info and Relationships](https://www.w3.org/TR/WCAG22/#info-and-relationships) |
| <a id="ref-08"></a>ref-08 | [1.3.2 Meaningful Sequence](https://www.w3.org/TR/WCAG22/#meaningful-sequence) |
| <a id="ref-09"></a>ref-09 | [1.3.3 Sensory Characteristics](https://www.w3.org/TR/WCAG22/#sensory-characteristics) |
| <a id="ref-10"></a>ref-10 | [1.3.4 Orientation](https://www.w3.org/TR/WCAG22/#orientation) |
| <a id="ref-11"></a>ref-11 | [1.4.1 Use of Color](https://www.w3.org/TR/WCAG22/#use-of-color) |
| <a id="ref-12"></a>ref-12 | [1.4.3 Contrast (Minimum)](https://www.w3.org/TR/WCAG22/#contrast-minimum) |
| <a id="ref-13"></a>ref-13 | [1.4.4 Resize Text](https://www.w3.org/TR/WCAG22/#resize-text) |
| <a id="ref-14"></a>ref-14 | [1.4.5 Images of Text](https://www.w3.org/TR/WCAG22/#images-of-text) |
| <a id="ref-15"></a>ref-15 | [1.4.10 Reflow](https://www.w3.org/TR/WCAG22/#reflow) |
| <a id="ref-16"></a>ref-16 | [1.4.11 Non-text Contrast](https://www.w3.org/TR/WCAG22/#non-text-contrast) |
| <a id="ref-17"></a>ref-17 | [1.4.12 Text Spacing](https://www.w3.org/TR/WCAG22/#text-spacing) |
| <a id="ref-18"></a>ref-18 | [1.4.13 Content on Hover or Focus](https://www.w3.org/TR/WCAG22/#content-on-hover-or-focus) |
| <a id="ref-19"></a>ref-19 | [2.1.1 Keyboard](https://www.w3.org/TR/WCAG22/#keyboard) |
| <a id="ref-20"></a>ref-20 | [2.1.2 No Keyboard Trap](https://www.w3.org/TR/WCAG22/#no-keyboard-trap) |
| <a id="ref-21"></a>ref-21 | [2.1.4 Character Key Shortcuts](https://www.w3.org/TR/WCAG22/#character-key-shortcuts) |
| <a id="ref-22"></a>ref-22 | [2.2.1 Timing Adjustable](https://www.w3.org/TR/WCAG22/#timing-adjustable) |
| <a id="ref-23"></a>ref-23 | [2.2.2 Pause, Stop, Hide](https://www.w3.org/TR/WCAG22/#pause-stop-hide) |
| <a id="ref-24"></a>ref-24 | [2.3.1 Three Flashes](https://www.w3.org/TR/WCAG22/#three-flashes-or-below-threshold) |
| <a id="ref-25"></a>ref-25 | [2.4.1 Bypass Blocks](https://www.w3.org/TR/WCAG22/#bypass-blocks) |
| <a id="ref-26"></a>ref-26 | [2.4.2 Page Titled](https://www.w3.org/TR/WCAG22/#page-titled) |
| <a id="ref-27"></a>ref-27 | [2.4.3 Focus Order](https://www.w3.org/TR/WCAG22/#focus-order) |
| <a id="ref-28"></a>ref-28 | [2.4.4 Link Purpose (In Context)](https://www.w3.org/TR/WCAG22/#link-purpose-in-context) |
| <a id="ref-29"></a>ref-29 | [2.4.6 Headings and Labels](https://www.w3.org/TR/WCAG22/#headings-and-labels) |
| <a id="ref-30"></a>ref-30 | [2.4.7 Focus Visible](https://www.w3.org/TR/WCAG22/#focus-visible) |
| <a id="ref-31"></a>ref-31 | [2.4.11 Focus Not Obscured (Minimum)](https://www.w3.org/TR/WCAG22/#focus-not-obscured-minimum) |
| <a id="ref-32"></a>ref-32 | [2.5.1 Pointer Gestures](https://www.w3.org/TR/WCAG22/#pointer-gestures) |
| <a id="ref-33"></a>ref-33 | [2.5.2 Pointer Cancellation](https://www.w3.org/TR/WCAG22/#pointer-cancellation) |
| <a id="ref-34"></a>ref-34 | [2.5.3 Label in Name](https://www.w3.org/TR/WCAG22/#label-in-name) |
| <a id="ref-35"></a>ref-35 | [2.5.7 Dragging Movements](https://www.w3.org/TR/WCAG22/#dragging-movements) |
| <a id="ref-36"></a>ref-36 | [2.5.8 Target Size (Minimum)](https://www.w3.org/TR/WCAG22/#target-size-minimum) |
| <a id="ref-37"></a>ref-37 | [3.1.1 Language of Page](https://www.w3.org/TR/WCAG22/#language-of-page) |
| <a id="ref-38"></a>ref-38 | [3.1.2 Language of Parts](https://www.w3.org/TR/WCAG22/#language-of-parts) |
| <a id="ref-39"></a>ref-39 | [3.2.1 On Focus](https://www.w3.org/TR/WCAG22/#on-focus) |
| <a id="ref-40"></a>ref-40 | [3.2.2 On Input](https://www.w3.org/TR/WCAG22/#on-input) |
| <a id="ref-41"></a>ref-41 | [3.2.3](https://www.w3.org/TR/WCAG22/#consistent-navigation) |
| <a id="ref-42"></a>ref-42 | [3.2.4](https://www.w3.org/TR/WCAG22/#consistent-identification) |
| <a id="ref-43"></a>ref-43 | [3.2.6 Consistent Help](https://www.w3.org/TR/WCAG22/#consistent-help) |
| <a id="ref-44"></a>ref-44 | [3.3.1 Error Identification](https://www.w3.org/TR/WCAG22/#error-identification) |
| <a id="ref-45"></a>ref-45 | [3.3.2 Labels or Instructions](https://www.w3.org/TR/WCAG22/#labels-or-instructions) |
| <a id="ref-46"></a>ref-46 | [3.3.3 Error Suggestion](https://www.w3.org/TR/WCAG22/#error-suggestion) |
| <a id="ref-47"></a>ref-47 | [3.3.4 Error Prevention](https://www.w3.org/TR/WCAG22/#error-prevention-legal-financial-data) |
| <a id="ref-48"></a>ref-48 | [3.3.7 Redundant Entry](https://www.w3.org/TR/WCAG22/#redundant-entry) |
| <a id="ref-49"></a>ref-49 | [3.3.8 Accessible Authentication (Minimum)](https://www.w3.org/TR/WCAG22/#accessible-authentication-minimum) |
| <a id="ref-50"></a>ref-50 | [4.1.2 Name, Role, Value](https://www.w3.org/TR/WCAG22/#name-role-value) |
| <a id="ref-51"></a>ref-51 | [4.1.3 Status Messages](https://www.w3.org/TR/WCAG22/#status-messages) |
| <a id="ref-52"></a>ref-52 | [Writing style › Style conventions](https://learn.microsoft.com/en-us/windows/apps/design/style/writing-style#style-conventions) |
| <a id="ref-53"></a>ref-53 | [WCAG 2.2 › 3.2.4](https://www.w3.org/TR/WCAG22/#consistent-identification) |

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

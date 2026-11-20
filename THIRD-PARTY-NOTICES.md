# 第三方声明

本仓库自己的代码以 [MIT 许可证](LICENSE)发布。下面列出仓库中包含的、或构建时随程序复制的第三方内容。NuGet 依赖不在此列：它们的版本和许可证见 [Directory.Packages.props](Directory.Packages.props) 中的说明。

## TIA Portal Openness MCP 的 Workbench 主题

| 项 | 内容 |
|---|---|
| 来源 | 项目负责人的 TIA_Portal_Openness_MCP 仓库，`src/Studio/Gui/` 下的 `Themes/`、`Controls/`、`Localization/` 中的 15 个文件，提交 `c89b7c20` |
| 位置 | [`src/EplanMcp.Workbench/Tia/`](src/EplanMcp.Workbench/Tia/)；`src/EplanMcp.Workbench/TiaStubs/` 中有 3 个文件重复了 TIA 的类型定义 |
| 许可证 | MIT，`Copyright (c) 2026 bulaofen0036-coder`，全文见下 |
| 为什么复制 | [ADR-0017](docs/adr/0017-workbench-wpf-tia-theme.md) 第 2 条：Workbench 的观感与 TIA 的 Workbench 一致，主题只复制一次，之后由我们有意识地合并 TIA 的改动 |

每个复制的文件开头有三四行注释：来源路径、TIA 的提交号、版权行，以及"下面未改动"或者改了什么。只有两个文件有改动：
- `Themes/ThemeManager.cs`：`using` 按 `dotnet format` 排序；`Convert.ToString` 加上 `CultureInfo.InvariantCulture`（本仓库的 CA1305 是错误级别，不放宽）；
- `Controls/ContrastSelection.cs`：`dotnet format` 调整了一行缩进。

测试中有 3 个文件移植自 TIA 的 `tests/Studio/TiaOpenness.Gui.Tests/`（同一提交）：`WpfContext.cs`、`Localization/StringsTests.cs`（部分）和 `Themes/ThemeContrastTests.cs`（来自 `ThemeSurfaceTests.cs`），开头同样注明来源和版权行，并说明改了什么。

其余文件除开头的注释外与 TIA 完全相同。代码风格按 TIA 的写法保留，[`Tia/.editorconfig`](src/EplanMcp.Workbench/Tia/.editorconfig) 只对这个文件夹关掉风格、格式、表述清晰和性能类的规则，正确性规则和编译器警告照常生效。合并 TIA 的新改动时：去掉开头的注释，与 TIA 新提交中的同名文件比较，再把注释中的提交号改为新读的提交；[`VendoredThemeTests`](tests/EplanMcp.Workbench.Tests/Themes/VendoredThemeTests.cs) 检查每个文件的注释。

```
MIT License

Copyright (c) 2026 bulaofen0036-coder

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## 界面字体：Inter 与 JetBrains Mono

| 项 | 内容 |
|---|---|
| 来源 | Inter 4.001（[rsms/inter](https://github.com/rsms/inter)）的 Regular、SemiBold、Bold；JetBrains Mono 2.304（[JetBrains/JetBrainsMono](https://github.com/JetBrains/JetBrainsMono)）的 Regular、Bold。文件取自项目负责人本机另一个项目中随附的同一发行版 |
| 位置 | [`src/EplanMcp.Workbench/Fonts/`](src/EplanMcp.Workbench/Fonts/)，构建时作为资源编进 `EplanMcp.Workbench.dll` |
| 许可证 | SIL Open Font License 1.1；版权行分别为 `Copyright (c) 2016 The Inter Project Authors` 和 `Copyright 2020 The JetBrains Mono Project Authors`，各自的全文与字体放在同一文件夹（`Inter-OFL.txt`、`JetBrainsMono-OFL.txt`），条款见下 |
| 为什么复制 | 项目负责人 2026-11-20 决定 Workbench 完全按界面设计稿实现，设计稿用这两种字体（[ADR-0034](docs/adr/0034-workbench-visual-apple-principles.md) 第 3 条的修订） |

字体文件未作任何修改，也没有改名。

```
-----------------------------------------------------------
SIL OPEN FONT LICENSE Version 1.1 - 26 February 2007
-----------------------------------------------------------

PREAMBLE
The goals of the Open Font License (OFL) are to stimulate worldwide
development of collaborative font projects, to support the font creation
efforts of academic and linguistic communities, and to provide a free and
open framework in which fonts may be shared and improved in partnership
with others.

The OFL allows the licensed fonts to be used, studied, modified and
redistributed freely as long as they are not sold by themselves. The
fonts, including any derivative works, can be bundled, embedded,
redistributed and/or sold with any software provided that any reserved
names are not used by derivative works. The fonts and derivatives,
however, cannot be released under any other type of license. The
requirement for fonts to remain under this license does not apply
to any document created using the fonts or their derivatives.

DEFINITIONS
"Font Software" refers to the set of files released by the Copyright
Holder(s) under this license and clearly marked as such. This may
include source files, build scripts and documentation.

"Reserved Font Name" refers to any names specified as such after the
copyright statement(s).

"Original Version" refers to the collection of Font Software components as
distributed by the Copyright Holder(s).

"Modified Version" refers to any derivative made by adding to, deleting,
or substituting -- in part or in whole -- any of the components of the
Original Version, by changing formats or by porting the Font Software to a
new environment.

"Author" refers to any designer, engineer, programmer, technical
writer or other person who contributed to the Font Software.

PERMISSION AND CONDITIONS
Permission is hereby granted, free of charge, to any person obtaining
a copy of the Font Software, to use, study, copy, merge, embed, modify,
redistribute, and sell modified and unmodified copies of the Font
Software, subject to the following conditions:

1) Neither the Font Software nor any of its individual components,
in Original or Modified Versions, may be sold by itself.

2) Original or Modified Versions of the Font Software may be bundled,
redistributed and/or sold with any software, provided that each copy
contains the above copyright notice and this license. These can be
included either as stand-alone text files, human-readable headers or
in the appropriate machine-readable metadata fields within text or
binary files as long as those fields can be easily viewed by the user.

3) No Modified Version of the Font Software may use the Reserved Font
Name(s) unless explicit written permission is granted by the corresponding
Copyright Holder. This restriction only applies to the primary font name as
presented to the users.

4) The name(s) of the Copyright Holder(s) or the Author(s) of the Font
Software shall not be used to promote, endorse or advertise any
Modified Version, except to acknowledge the contribution(s) of the
Copyright Holder(s) and the Author(s) or with their explicit written
permission.

5) The Font Software, modified or unmodified, in part or in whole,
must be distributed entirely under this license, and must not be
distributed under any other license. The requirement for fonts to
remain under this license does not apply to any document created
using the Font Software.

TERMINATION
This license becomes null and void if any of the above conditions are
not met.

DISCLAIMER
THE FONT SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO ANY WARRANTIES OF
MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT
OF COPYRIGHT, PATENT, TRADEMARK, OR OTHER RIGHT. IN NO EVENT SHALL THE
COPYRIGHT HOLDER BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
INCLUDING ANY GENERAL, SPECIAL, INDIRECT, INCIDENTAL, OR CONSEQUENTIAL
DAMAGES, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
FROM, OUT OF THE USE OR INABILITY TO USE THE FONT SOFTWARE OR FROM
OTHER DEALINGS IN THE FONT SOFTWARE.
```

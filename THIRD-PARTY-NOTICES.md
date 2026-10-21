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

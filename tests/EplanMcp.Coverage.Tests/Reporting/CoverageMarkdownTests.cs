using EplanMcp.Coverage.Reporting;

namespace EplanMcp.Coverage.Tests.Reporting;

[Trait("Size", "Medium")]
public sealed class CoverageMarkdownTests
{
    [Fact]
    public void Render_EveryPercentage_ShowsNumeratorDenominatorAndExclusions()
    {
        using TempRepository repository = ReportFixture.Create();

        string report = CoverageMarkdown.Render(repository.Root);

        Assert.Contains("| 操作层（重载组，主分母） | 5 | deprecated：1 | 4 | 3 / 4（75.0%） | 2 | 1 | 1 / 4（25.0%） | 1 / 4（25.0%） | 1 | 1 |", report, StringComparison.Ordinal);
        Assert.Contains("| 　其中对象属性 | 1 | — | 1 | 1 / 1（100.0%） |", report, StringComparison.Ordinal);
        Assert.Contains("| action（按行） | 3 | printing：1 | 2 | 1 / 2（50.0%） |", report, StringComparison.Ordinal);
        Assert.Contains("登记 5 组（其中 0 组运算符和委托成员不在方法、属性两行中，已全部被机械规则排除），目录的机械规则排除后 4 组，再按 overlay 排除后 4 组", report, StringComparison.Ordinal);
        Assert.Contains("| `Eplan.EplApi.Test.Planning` | 1 | 0 / 1（0.0%） | 0 |", report, StringComparison.Ordinal);
        Assert.Contains("无文档的重载组（不在 IntelliSense 中，仍在分母里，Q2）：共 1 组", report, StringComparison.Ordinal);
        Assert.Contains("工具：1 / 2（50.0%），共 1 条记录", report, StringComparison.Ordinal);
        Assert.Contains("Bridge 用到、目录中却没有的 ID（1 个）：`M:Eplan.EplApi.Test.Page.Missing`", report, StringComparison.Ordinal);
        Assert.Contains("| `N:Eplan.EplApi.Test.Planning` | env_blocked | 授权中没有 | api-notes A24 |", report, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_AStaleScan_PutsANoticeWithTheScanAtTheTop()
    {
        using TempRepository repository = ReportFixture.Create();
        string fresh = CoverageMarkdown.Render(repository.Root);

        repository.Write("src/EplanMcp.Bridge.Core/B.cs", "class B { int C; }");
        string stale = CoverageMarkdown.Render(repository.Root);

        Assert.DoesNotContain("Bridge 扫描已过期", fresh, StringComparison.Ordinal);
        Assert.StartsWith("# EPLAN API 覆盖报告\n\n> **Bridge 扫描已过期**：`catalog/mapping/bridge-usage.json` 是 2026-10-21 按 Bridge 源码 a73de2f 扫描的", stale, StringComparison.Ordinal);
        Assert.Contains(CoverageCheck.ScanCommand, stale, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_WithoutACatalog_SaysSo_AndKeepsTheSectionsInOrder()
    {
        using TempRepository repository = ReportFixture.Create(withCatalog: false);

        string report = CoverageMarkdown.Render(repository.Root);

        Assert.Contains("SDK 部分**尚未提交**，action 部分**尚未提交**", report, StringComparison.Ordinal);
        Assert.DoesNotContain("| 层 |", report, StringComparison.Ordinal);
        Assert.Equal(
            ["## 口径", "## 1.", "## 2.", "## 3.", "## 4.", "## 5.", "## 6.", "## 7."],
            report.Split('\n').Where(l => l.StartsWith("## ", StringComparison.Ordinal)).Select(l => l.Split(' ')[0] + " " + l.Split(' ')[1]));
    }
}

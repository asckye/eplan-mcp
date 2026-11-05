using System.Text.Json;
using EplanMcp.Coverage.Mapping;
using EplanMcp.Coverage.Reporting;

namespace EplanMcp.Coverage.Tests.Reporting;

[Trait("Size", "Small")]
public sealed class ApiTotalsTests
{
    [Fact]
    public void Deserialize_PreservesEveryInstallationCountAndItsContext()
    {
        const string Json = """
            {
              "eplanBuild": "3000.2.1.8",
              "publicElements": 99,
              "assemblies": 4,
              "undocumentedAssemblies": {
                "names": ["Example.Core", "Example.Tools"],
                "types": 3,
                "methods": 12,
                "properties": 5
              },
              "privateDmClassIdEnums": 7,
              "actions": {
                "registered": 20,
                "actionsSection": 21,
                "interactions": 2,
                "asmActions": 1,
                "foundByName": 19,
                "foundVariant": "Example Variant"
              }
            }
            """;

        ApiTotals totals = JsonSerializer.Deserialize<ApiTotals>(Json, CoverageJson.Options)!;

        Assert.Equal(("3000.2.1.8", 99, 4, 7), (totals.EplanBuild, totals.PublicElements, totals.Assemblies, totals.PrivateDmClassIdEnums));
        Assert.Equal(["Example.Core", "Example.Tools"], totals.UndocumentedAssemblies.Names);
        Assert.Equal((3, 12, 5), (totals.UndocumentedAssemblies.Types, totals.UndocumentedAssemblies.Methods, totals.UndocumentedAssemblies.Properties));
        Assert.Equal(new ActionTotals(20, 21, 2, 1, 19, "Example Variant"), totals.Actions);
    }
}

[Trait("Size", "Medium")]
public sealed class ApiTotalsGenerationTests
{
    [Fact]
    public void Generate_WithoutInstallationTotalsNamesTheMissingFileBeforeWritingDerivedFiles()
    {
        using TempRepository repository = ReportFixture.Create();
        string totalsPath = Path.Combine(repository.Root, ApiTotals.RelativePath);
        string mappingPath = Path.Combine(repository.Root, ToolMapping.RelativePath);
        Assert.True(File.Exists(totalsPath));
        Assert.False(File.Exists(mappingPath));
        File.Delete(totalsPath);

        InvalidDataException error = Assert.Throws<InvalidDataException>(() => CoverageCheck.Generate(repository.Root));

        Assert.Contains(ApiTotals.RelativePath, error.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(mappingPath));
        Assert.Contains(CoverageCheck.Problems(repository.Root), p => p.Contains(ApiTotals.RelativePath, StringComparison.Ordinal));
    }

    [Fact]
    public void Report_IncludesInstallationCountsWithoutPublishingNames()
    {
        using TempRepository repository = ReportFixture.Create();
        ApiTotals totals = ApiTotals.Load(repository.Root) with
        {
            PublicElements = 43,
            UndocumentedAssemblies = new(["Hidden.Library.Alpha", "Hidden.Library.Beta"], 9, 63, 3),
        };
        repository.Write(ApiTotals.RelativePath, CoverageJson.Serialize(totals));

        CoverageReport report = CoverageReport.Build(repository.Root);
        string markdown = CoverageMarkdown.Render(repository.Root);

        Assert.Equal((72, 68, 4), (report.FullApi!.Total.Denominator, report.FullApi.Methods.Denominator, report.FullApi.Properties.Denominator));
        Assert.Equal(new RouteCount(68, 0), report.FullApi.Total.Routes["none"]);
        Assert.Equivalent(totals, report.InstallationTotals);
        Assert.Contains("公开元素 43 个", markdown, StringComparison.Ordinal);
        Assert.Contains("2 个程序集没有 XML", markdown, StringComparison.Ordinal);
        Assert.Contains("额外加入这 66 个成员", markdown, StringComparison.Ordinal);
        Assert.Contains("1 个私有 `DMClassId` 枚举", markdown, StringComparison.Ordinal);
        Assert.Contains("action 注册总数 3", markdown, StringComparison.Ordinal);
        Assert.Contains("按名称能找到 2 个", markdown, StringComparison.Ordinal);
        Assert.All(totals.UndocumentedAssemblies.Names, name => Assert.DoesNotContain(name, markdown, StringComparison.Ordinal));
    }
}

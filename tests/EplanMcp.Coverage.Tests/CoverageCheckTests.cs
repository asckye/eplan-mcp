using EplanMcp.Coverage.Mapping;
using EplanMcp.Coverage.Reporting;
using EplanMcp.Coverage.Scanning;
using EplanMcp.Coverage.Tests.Mapping;

namespace EplanMcp.Coverage.Tests;

/// <summary>
/// The CI step runs <c>check</c>: a scan older than the Bridge sources only warns, a missing scan or a derived file that
/// does not match its inputs fails, and every message names the command that fixes it.
/// </summary>
[Trait("Size", "Medium")]
public sealed class CoverageCheckTests : IDisposable
{
    private readonly TempRepository _repository = FakeRepository.Create();

    [Fact]
    public void Check_AfterACodeChangeInTheBridge_WarnsWithTheScanCommand_ButNotAfterACommentChange()
    {
        CoverageCheck.Generate(_repository.Root);

        _repository.Write("src/EplanMcp.Bridge.Eplan/Adapters/A.cs", "// a comment\nclass A\n{\n}\n");
        List<string> afterComment = CoverageCheck.Warnings(_repository.Root);
        _repository.Write("src/EplanMcp.Bridge.Eplan/Adapters/A.cs", "class A { int B; }");
        List<string> afterCode = CoverageCheck.Warnings(_repository.Root);

        Assert.Empty(afterComment);
        Assert.Contains("scan of 2026-10-21 (a73de2f)", Assert.Single(afterCode), StringComparison.Ordinal);
        Assert.Contains(CoverageCheck.ScanCommand, afterCode[0], StringComparison.Ordinal);
        Assert.Contains(BridgeUsage.RelativePath, afterCode[0], StringComparison.Ordinal);

        // The stale scan does not fail, but the report must say it is stale: generate (no EPLAN) puts the notice in.
        Assert.Equal([CoverageMarkdown.RelativePath], CoverageCheck.Problems(_repository.Root).Select(p => p.Split(' ')[0]));
        CoverageCheck.Generate(_repository.Root);
        Assert.Empty(CoverageCheck.Problems(_repository.Root));
        Assert.Single(CoverageCheck.Warnings(_repository.Root));
    }

    [Fact]
    public void Check_ADerivedFileThatDoesNotMatch_FailsWithTheGenerateCommand()
    {
        CoverageCheck.Generate(_repository.Root);
        _repository.Write(ToolMapping.RelativePath, "{}\n");

        string problem = Assert.Single(CoverageCheck.Problems(_repository.Root));

        Assert.StartsWith(ToolMapping.RelativePath, problem, StringComparison.Ordinal);
        Assert.Contains(CoverageCheck.GenerateCommand, problem, StringComparison.Ordinal);
    }

    [Fact]
    public void Check_WithoutAScan_Fails()
    {
        using var empty = new TempRepository();

        Assert.Contains(CoverageCheck.ScanCommand, Assert.Single(CoverageCheck.Problems(empty.Root)), StringComparison.Ordinal);
    }

    public void Dispose() => _repository.Dispose();
}

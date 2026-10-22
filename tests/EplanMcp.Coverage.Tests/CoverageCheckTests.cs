using EplanMcp.Coverage.Scanning;

namespace EplanMcp.Coverage.Tests;

/// <summary>
/// The CI step runs <c>check</c>: a scan older than the Bridge sources only warns, a missing or mismatched file fails, and
/// either message names the command that fixes it.
/// </summary>
[Trait("Size", "Medium")]
public sealed class CoverageCheckTests : IDisposable
{
    private readonly DirectoryInfo _repository = Directory.CreateTempSubdirectory("coverage-check-");

    [Fact]
    public void Check_AfterACodeChangeInTheBridge_WarnsWithTheScanCommand_ButNotAfterACommentChange()
    {
        Write("src/EplanMcp.Bridge.Eplan/Adapters/A.cs", "class A { void M() => N(1); }");
        Write("src/EplanMcp.Bridge.Core/B.cs", "class B { }");
        Write("src/EplanMcp.Contracts/OperationNames.cs", "class OperationNames { }");
        string root = _repository.FullName;
        Write(BridgeUsage.RelativePath, CoverageJson.Serialize(new BridgeUsage
        {
            EplanApi = "2026.0.3.25421",
            ScannedOn = "2026-10-21",
            ScannedSources = "a73de2f",
            SourceHash = BridgeUsage.ComputeSourceHash(root),
            Operations = [],
            Usages = [],
        }));

        Write("src/EplanMcp.Bridge.Eplan/Adapters/A.cs", "// a comment\nclass A\n{\n    void M() => N(1);\n}\n");
        List<string> afterComment = CoverageCheck.Warnings(root);
        Write("src/EplanMcp.Bridge.Eplan/Adapters/A.cs", "class A { void M() => N(2); }");
        List<string> afterCode = CoverageCheck.Warnings(root);

        Assert.Empty(afterComment);
        Assert.Contains("scan of 2026-10-21 (a73de2f)", Assert.Single(afterCode), StringComparison.Ordinal);
        Assert.Contains(CoverageCheck.ScanCommand, afterCode[0], StringComparison.Ordinal);
        Assert.Empty(CoverageCheck.Problems(root));
    }

    [Fact]
    public void Check_WithoutAScan_Fails()
    {
        Assert.Contains(CoverageCheck.ScanCommand, Assert.Single(CoverageCheck.Problems(_repository.FullName)), StringComparison.Ordinal);
    }

    public void Dispose() => _repository.Delete(recursive: true);

    private void Write(string relativePath, string text)
    {
        string path = Path.Combine(_repository.FullName, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }
}

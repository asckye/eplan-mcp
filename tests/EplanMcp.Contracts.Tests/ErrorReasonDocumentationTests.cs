using System.Reflection;

namespace EplanMcp.Contracts.Tests;

/// <summary>
/// ErrorReason says "add a reason to mcp-tool-design 6.3 before adding it here", and ADR-0010's execution states are
/// listed in 6.2: these tests hold the code to the documented contract.
/// </summary>
[Trait("Size", "Medium")]
public sealed class ErrorReasonDocumentationTests
{
    private static readonly string _toolDesign = File.ReadAllText(Path.Combine(RepositoryRoot(), "docs", "standards", "mcp-tool-design.md"));

    [Fact]
    public void EveryReason_IsInTheErrorTable()
    {
        string table = Section("### 6.3", "## 7.");

        Assert.All(Constants(typeof(ErrorReason)), reason => Assert.Contains($"`{reason}`", table, StringComparison.Ordinal));
    }

    [Fact]
    public void EveryExecutionState_IsDocumented()
    {
        string errors = Section("### 6.2", "### 6.3");

        Assert.Equal(["committed", "not_started", "rolled_back", "unknown"], Constants(typeof(ExecutionState)).Order(StringComparer.Ordinal));
        Assert.All(Constants(typeof(ExecutionState)), state => Assert.Contains($"| `{state}` |", errors, StringComparison.Ordinal));
    }

    private static string[] Constants(Type type) =>
        [.. type.GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!)];

    private static string Section(string start, string end)
    {
        int from = _toolDesign.IndexOf(start, StringComparison.Ordinal);
        int to = _toolDesign.IndexOf(end, from, StringComparison.Ordinal);
        Assert.True(from >= 0 && to > from, $"mcp-tool-design.md has no section between '{start}' and '{end}'.");
        return _toolDesign[from..to];
    }

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "EplanMcp.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("EplanMcp.slnx not found above " + AppContext.BaseDirectory);
    }
}

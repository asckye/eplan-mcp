using System.Reflection;

namespace EplanMcp.Contracts.Tests;

/// <summary>
/// The project error reference lists ErrorReason and ADR-0010's execution states; these tests hold the code to that
/// documented contract.
/// </summary>
[Trait("Size", "Medium")]
public sealed class ErrorReasonDocumentationTests
{
    private static readonly string _toolDesign = File.ReadAllText(Path.Combine(RepositoryRoot(), "docs", "tools", "error-reference.md"));

    [Fact]
    public void EveryReason_IsInTheErrorTable()
    {
        string table = Section("## 错误码", "## 契约来源");

        Assert.All(Constants(typeof(ErrorReason)), reason => Assert.Contains($"`{reason}`", table, StringComparison.Ordinal));
    }

    [Fact]
    public void EveryExecutionState_IsDocumented()
    {
        string errors = Section("## 写入执行状态", "## 错误码");

        Assert.Equal(["committed", "not_started", "rolled_back", "unknown"], Constants(typeof(ExecutionState)).Order(StringComparer.Ordinal));
        Assert.All(Constants(typeof(ExecutionState)), state => Assert.Contains($"| `{state}` |", errors, StringComparison.Ordinal));
    }

    private static string[] Constants(Type type) =>
        [.. type.GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!)];

    private static string Section(string start, string end)
    {
        int from = _toolDesign.IndexOf(start, StringComparison.Ordinal);
        int to = _toolDesign.IndexOf(end, from, StringComparison.Ordinal);
        Assert.True(from >= 0 && to > from, $"error-reference.md has no section between '{start}' and '{end}'.");
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

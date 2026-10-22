using System.Globalization;
using EplanMcp.Contracts;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tools;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Small")]
public sealed class ProjectCreateArgumentTests
{
    [Theory]
    [InlineData("bad", "wrong.txt", "bad.name", "../bad", 4)]
    [InlineData("basic_project", "a.zw1", "Line1", null, 1)]
    [InlineData("backup", "a.zw9", "Line1", null, 1)]
    [InlineData("backup", "../a.zw1", "Line1", null, 1)]
    [InlineData("basic_project", "a.zw9", " Line1", null, 1)]
    [InlineData("basic_project", "a.zw9", "Line1 ", null, 1)]
    [InlineData("backup", "a.zw1", "CON", null, 1)]
    [InlineData("backup", "a.zw1", "sub/Line1", null, 1)]
    [InlineData("backup", "a.zw1", "", null, 1)]
    [InlineData("backup", "a.zw1", "Line\u0001", null, 1)]
    [InlineData("backup", "a.zw1", "bad:name", null, 1)]
    [InlineData("backup", "a.zw1", "Line1", @"\\server\share\new", 1)]
    [InlineData("backup", "a.zw1", "Line1", @"\\?\C:\new", 1)]
    [InlineData("backup", "a.zw1", "Line1", @"C:\a\..\new", 1)]
    [InlineData("backup", "a.zw1", "Line1", @"C:\a\.\new", 1)]
    [InlineData("backup", "a.zw1", "Line1", @"C:\a\CON", 1)]
    [InlineData("backup", "NUL.zw1", "Line1", null, 1)]
    [InlineData("backup", @"C:\a.zw1", "Line1", null, 1)]
    public async Task InvalidArguments_AreReportedTogether_WithoutAnyDependencies(string mode, string file, string name, string? destination, int count)
    {
        var result = await ProjectCreateTools.CreateAsync(null!, null!, null!, mode, file, name, destination, cancellationToken: TestContext.Current.CancellationToken);
        var error = WriteToolHarness.Error(result);
        Assert.Equal((ErrorReason.InvalidArgument, ExecutionState.NotStarted), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString()));
        Assert.Equal(count, error.GetProperty("metadata").GetProperty("violations").GetString()!.Split('\n').Length);
    }

    [Theory]
    [InlineData("zh-CN", "Ctrl+Z 不能撤销")]
    [InlineData("en-US", "no EPLAN undo step")]
    public void Approval_DescribesNoOverwriteAndNoUndo(string culture, string note)
    {
        var question = new ApprovalQuestion("r", ProjectCreateTools.CreateTool, "hash", new WritePlan { Changes = [new() { Action = "create", Kind = "project", Name = "Line1", NewValue = "backup: sample.zw1 sha256 abc to destination" }] }, [], DateTimeOffset.UnixEpoch) { Tier = ToolTier.Write, Eplan = null };
        var text = ApprovalTexts.For(question, CultureInfo.GetCultureInfo(culture));
        Assert.Contains(note, text.Note, StringComparison.Ordinal);
        Assert.Contains("sample.zw1", Assert.Single(text.Changes), StringComparison.Ordinal);
    }

    [Fact]
    public void DestinationForm_AcceptsLocalFolder_AndNameLengthMatchesImport()
    {
        Assert.Null(ProjectCreateTools.DestinationProblem(@"C:\projects\Line1"));
        Assert.Null(ImportTools.ProjectNameProblem(new string('a', 60)));
        Assert.NotNull(ImportTools.ProjectNameProblem(new string('a', 61)));
        Assert.Equal(2L * 1024 * 1024 * 1024, ImportTools.MaxProjectBytes);
    }
}

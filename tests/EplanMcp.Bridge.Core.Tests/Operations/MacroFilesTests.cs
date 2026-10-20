using System.Diagnostics;
using EplanMcp.Bridge.Core.Operations;

namespace EplanMcp.Bridge.Core.Tests.Operations;

/// <summary>SEC-19 on a real file system: what the macro scan finds, and the junctions it never follows.</summary>
[Trait("Size", "Medium")]
public sealed class MacroFilesTests : IDisposable
{
    private readonly string _temp = Directory.CreateTempSubdirectory("eplanmcp-macros-").FullName;
    private readonly string _root;
    private readonly string _outside;
    private readonly List<string> _junctions = [];

    public MacroFilesTests()
    {
        _root = Directory.CreateDirectory(Path.Combine(_temp, "Macros")).FullName;
        _outside = Directory.CreateDirectory(Path.Combine(_temp, "Elsewhere")).FullName;
    }

    public void Dispose()
    {
        // A junction goes first, on its own: deleting the tree through it is refused.
        _junctions.ForEach(link => Directory.Delete(link));
        Directory.Delete(_temp, recursive: true);
    }

    [Fact]
    public void Scan_FindsWindowMacrosInEveryFolder_SortedWithSlashes()
    {
        Touch(_root, "Top.ema");
        Touch(_root, @"EPLAN_Macro\203_Engine\b_Feedback.EMA");
        Touch(_root, @"EPLAN_Macro\203_Engine\A_Start.ema");
        Touch(_root, @"EPLAN_Macro\Symbols.ems");
        Touch(_root, @"EPLAN_Macro\Readme.txt");
        Touch(_root, @"EPLAN_Macro\Page.emp");

        MacroScan scan = MacroFiles.Scan(_root, TimeSpan.FromSeconds(30));

        Assert.True(scan.Complete);
        Assert.Equal(["EPLAN_Macro/203_Engine/A_Start.ema", "EPLAN_Macro/203_Engine/b_Feedback.EMA", "Top.ema"], scan.Paths);
    }

    [Fact]
    public void Scan_NeverFollowsAJunction_IntoAFolderOutsideTheRoot()
    {
        Touch(_root, @"Own\Inside.ema");
        Touch(_outside, "Outside.ema");
        Junction(Path.Combine(_root, "Linked"), _outside);

        MacroScan scan = MacroFiles.Scan(_root, TimeSpan.FromSeconds(30));

        Assert.Equal(["Own/Inside.ema"], scan.Paths);
    }

    [Fact]
    public void Scan_TheRootItselfMayBeAJunction()
    {
        Touch(_outside, "Shared.ema");
        string linkedRoot = Junction(Path.Combine(_temp, "LinkedRoot"), _outside);

        Assert.Equal(["Shared.ema"], MacroFiles.Scan(linkedRoot, TimeSpan.FromSeconds(30)).Paths);
    }

    [Fact]
    public void Scan_PastItsBudget_StopsAndSaysSo()
    {
        Touch(_root, "Top.ema");

        MacroScan scan = MacroFiles.Scan(_root, TimeSpan.Zero);

        Assert.False(scan.Complete);
        Assert.Empty(scan.Paths);
    }

    [Fact]
    public void Scan_AMissingRoot_FindsNothing()
    {
        MacroScan scan = MacroFiles.Scan(Path.Combine(_temp, "Missing"), TimeSpan.FromSeconds(30));

        Assert.True(scan.Complete);
        Assert.Empty(scan.Paths);
    }

    [Theory]
    [InlineData("EPLAN_Macro/203/Contactor_Feedback.ema", "Contactor_Feedback")]
    [InlineData("Top.EMA", "Top")]
    public void NameOf_IsTheFileNameWithoutTheExtension(string path, string name)
    {
        Assert.Equal(name, MacroFiles.NameOf(path));
    }

    private static void Touch(string root, string relative)
    {
        string path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, string.Empty);
    }

    private string Junction(string link, string target)
    {
        // A junction needs no privilege, unlike a symbolic link; .NET has no API to create one.
        using Process mklink = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })!;
        mklink.WaitForExit();
        Assert.Equal(0, mklink.ExitCode);
        _junctions.Add(link);
        return link;
    }
}

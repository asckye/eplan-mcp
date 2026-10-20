using System.Diagnostics;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

/// <summary>macro.get and SEC-19's path rules over a temporary macro directory, with a fake for what EPLAN reads.</summary>
[Trait("Size", "Medium")]
public sealed class MacroGetOperationTests : IDisposable
{
    private const string Feedback = "EPLAN_Macro/203/Contactor_Feedback.ema";

    private readonly string _temp = Directory.CreateTempSubdirectory("eplanmcp-macro-get-").FullName;
    private readonly string _root;
    private readonly string _outside;
    private readonly List<string> _junctions = [];
    private readonly FakeMacroReader _reader;

    public MacroGetOperationTests()
    {
        _root = Directory.CreateDirectory(Path.Combine(_temp, "Macros")).FullName;
        _outside = Directory.CreateDirectory(Path.Combine(_temp, "Elsewhere")).FullName;
        Touch(_root, Feedback);
        Touch(_outside, "Outside.ema");
        _reader = new FakeMacroReader(_root);
    }

    public void Dispose()
    {
        // A junction goes first, on its own: deleting the tree through it is refused.
        _junctions.ForEach(link => Directory.Delete(link));
        Directory.Delete(_temp, recursive: true);
    }

    [Fact]
    public void Execute_OpensTheMacroInTheResolvedProject_AndListsOneDevicePerTag()
    {
        _reader.Content.Functions.AddRange(
        [
            Function("=+A1-QA1", "-QA1", "NoContact"),
            Function("+A1-XG5:?", "", "Terminal"),
            Function("+A1-XG5:?", "-XG5", "Terminal"),
            Function("+-WG6", "-WG6", "Cable", main: true),
            Function("+-WG6", "", "Wire"),
            Function("+", "", "Wire"),
        ]);

        MacroGetData data = Run(@"EPLAN_Macro\203\Contactor_Feedback.ema");

        Assert.Equal((Path.Combine(_root, "EPLAN_Macro", "203", "Contactor_Feedback.ema"), "p1"), (_reader.Opened, _reader.OpenedIn));
        Assert.Equal(("Line1", Feedback), (data.Project, data.Path));
        Assert.Equal(
            [("=+A1-QA1", "-QA1", false), ("+A1-XG5:?", "-XG5", false), ("+-WG6", "-WG6", true)],
            data.Devices.Select(d => (d.Tag, d.VisibleTag, d.Main)));
        Assert.Equal(3, data.DeviceCount);
        Assert.Equal(("MultiLine", 6), (data.Representations[0].Type, data.Representations[0].Variants.Count));
        Assert.Equal((52.0, 268.0, 28), (data.Width, data.Height, data.ObjectCount));
    }

    [Fact]
    public void Execute_ManyDevices_ListsTheFirstFiftyAndCountsAll()
    {
        _reader.Content.Functions.AddRange(Enumerable.Range(1, 60).Select(i => Function("-K" + i.ToString(System.Globalization.CultureInfo.InvariantCulture), "", "Coil", main: true)));

        MacroGetData data = Run(Feedback);

        Assert.Equal((MacroGetOperation.MaxDevices, 60), (data.Devices.Count, data.DeviceCount));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(@"C:\Macros\x.ema")]
    [InlineData(@"\\server\share\x.ema")]
    [InlineData("/EPLAN_Macro/203/Contactor_Feedback.ema")]
    [InlineData("EPLAN_Macro/../../Elsewhere/Outside.ema")]
    [InlineData("./EPLAN_Macro/203/Contactor_Feedback.ema")]
    [InlineData("EPLAN_Macro//203/Contactor_Feedback.ema")]
    [InlineData("EPLAN_Macro/203/Contactor_Feedback.ema:stream")]
    [InlineData("EPLAN_Macro/203/Contactor_Feedback.ems")]
    public void Execute_APathOutsideTheRules_IsAnInvalidArgumentThatNamesNoLocalPath(string macro)
    {
        BridgeError error = Fails(macro);

        Assert.Equal(ErrorReason.InvalidArgument, error.Reason);
        Assert.DoesNotContain(_temp, error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(_reader.Opened);
    }

    [Fact]
    public void Execute_APathThroughAJunction_IsRefusedBeforeOpening()
    {
        Junction(Path.Combine(_root, "Linked"), _outside);

        BridgeError error = Fails("Linked/Outside.ema");

        Assert.Equal(ErrorReason.InvalidArgument, error.Reason);
        Assert.Contains("junction", error.Message, StringComparison.Ordinal);
        Assert.Null(_reader.Opened);
    }

    [Theory]
    [InlineData("EPLAN_Macro/203/Missing.ema")]
    [InlineData("Missing/Contactor_Feedback.ema")]
    public void Execute_AMissingMacro_IsMacroNotFound(string macro)
    {
        BridgeError error = Fails(macro);

        Assert.Equal((ErrorReason.MacroNotFound, macro), (error.Reason, error.Metadata!["macro"]));
        Assert.DoesNotContain(_temp, error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_IgnoresCaseInThePath()
    {
        Assert.Equal("eplan_macro/203/CONTACTOR_FEEDBACK.EMA", Run("eplan_macro/203/CONTACTOR_FEEDBACK.EMA").Path);
    }

    private MacroGetData Run(string macro) =>
        (MacroGetData)new MacroGetOperation(new FakeCatalog(new OpenProject { Name = "Line1", Path = "p1", Current = true }), _reader).Execute(new MacroGetArgs { Macro = macro })!;

    private BridgeError Fails(string macro) => Assert.Throws<BridgeErrorException>(() => Run(macro)).Error;

    private static MacroFunctionRecord Function(string name, string visible, string category, bool main = false) =>
        new() { Name = name, VisibleName = visible, Category = category, Main = main };

    private static void Touch(string root, string relative)
    {
        string path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, string.Empty);
    }

    private void Junction(string link, string target)
    {
        // A junction needs no privilege, unlike a symbolic link; .NET has no API to create one.
        using Process mklink = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })!;
        mklink.WaitForExit();
        Assert.Equal(0, mklink.ExitCode);
        _junctions.Add(link);
    }

    /// <summary>Returns <see cref="Content"/> for any macro and records what was opened, and in which project.</summary>
    private sealed class FakeMacroReader(string root) : IMacroReader
    {
        public MacroContent Content { get; } = new()
        {
            Representations = [new("MultiLine", [0, 1, 2, 3, 4, 5])],
            Width = 52,
            Height = 268,
            ObjectCount = 28,
        };

        public string? Opened { get; private set; }

        public string? OpenedIn { get; private set; }

        public string? MacroRoot() => root;

        public MacroContent ReadMacro(string projectPath, string macroPath)
        {
            (OpenedIn, Opened) = (projectPath, macroPath);
            return Content;
        }
    }
}

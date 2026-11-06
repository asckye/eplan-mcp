using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;
namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Medium")]
public sealed class MacroStoreOperationTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("eplan-macro-store-").FullName;
    private readonly FakeSnapshotReader _snapshots = new();
    private readonly Storer _storer = new();
    private readonly MacroStoreOperation _operation;
    public MacroStoreOperationTests() => _operation = new(new FakeCatalog(new OpenProject { Name = "Line1", Path = @"C:\scratch\Line1" }), _storer, _snapshots);
    public void Dispose() => Directory.Delete(_directory, recursive: true);
    private JObject Args(bool? dryRun = null)
    {
        var result = new JObject
        {
            ["project"] = "Line1",
            ["operation"] = "window",
            ["page"] = "=AP+ST1/10",
            ["name"] = "export",
            ["path"] = Path.Combine(_directory, "export.ema"),
            ["locators"] = new JArray(new JObject { ["project"] = @"C:\scratch\Line1", ["page"] = "=AP+ST1/10", ["type"] = 20, ["oid"] = 100, ["digest"] = new string('a', 64) })
        };
        if (dryRun is not null)
        { result["dry_run"] = dryRun; }
        return result;
    }
    private JObject Run(JObject args) => (JObject)new OperationContext("macro-store", false, null).Run(() => _operation.Execute((object)args))!;
    [Theory]
    [InlineData(null)]
    [InlineData(true)]
    public void Preview_DoesNotWriteAndSuppliesFingerprintAndChange(bool? dryRun)
    {
        JObject result = Run(Args(dryRun));
        Assert.Equal(0, _storer.Writes);
        Assert.False(File.Exists((string)result["path"]!));
        Assert.Equal(64, ((string)result["fingerprint"]!).Length);
        Assert.Equal("create", (string?)result["changes"]![0]!["action"]);
        Assert.Equal([0], result["variants"]!.Values<int>());
    }
    [Fact]
    public void Execute_WritesFileAndComparesEveryProjectPage()
    {
        JObject args = Args(false);
        args["fingerprint"] = Run(Args())["fingerprint"]!.DeepClone();
        JObject result = Run(args);
        Assert.True(File.Exists((string)result["path"]!));
        Assert.Equal(1, _storer.Writes);
        Assert.Equal(3, _snapshots.Opened.Count);
        Assert.Equal(15000, _snapshots.Milliseconds);
    }
    [Fact]
    public void PageMacro_UsesTheSameReadOnlyExportPath()
    {
        var args = new JObject { ["project"] = "Line1", ["operation"] = "page", ["pages"] = new JArray("=AP+ST1/10"), ["name"] = "pages", ["path"] = Path.Combine(_directory, "pages.emp"), ["dry_run"] = false };
        Assert.Null(Run(args)["variants"]);
        Assert.Equal(1, _storer.Writes);
    }
    [Fact]
    public void ExistingFile_IsRejectedWithoutPreparingOrOverwriting()
    {
        JObject args = Args(false);
        string path = (string)args["path"]!;
        File.WriteAllText(path, "existing");
        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => Run(args));
        Assert.Equal(ErrorReason.AlreadyExists, error.Error.Reason);
        Assert.Equal("existing", File.ReadAllText(path));
        Assert.Equal(0, _storer.Prepares);
    }
    [Theory]
    [InlineData("empty")]
    [InlineData("missing")]
    [InlineData("variant")]
    [InlineData("throw")]
    public void FailedReadback_IsAnEplanOperationFailure(string failure)
    {
        _storer.Failure = failure;
        Assert.Equal(ErrorReason.EplanOperationFailed, Assert.Throws<BridgeErrorException>(() => Run(Args(false))).Error.Reason);
    }
    [Fact]
    public void BusinessSnapshotChange_OnAnotherPageFails()
    {
        _storer.OnWrite = () => _snapshots.Descriptions[140] = "changed";
        Assert.Equal(ErrorReason.EplanOperationFailed, Assert.Throws<BridgeErrorException>(() => Run(Args(false))).Error.Reason);
    }
    [Fact]
    public void ChangedProjectBetweenPreviewAndExecute_IsStaleWithoutWriting()
    {
        JObject args = Args(false);
        args["fingerprint"] = Run(Args())["fingerprint"]!.DeepClone();
        _snapshots.Descriptions[140] = "changed";
        Assert.Equal(ErrorReason.PlanStale, Assert.Throws<BridgeErrorException>(() => Run(args)).Error.Reason);
        Assert.Equal(0, _storer.Writes);
    }
    [Theory]
    [InlineData("variant", "-1")]
    [InlineData("variant", "16")]
    [InlineData("variant", "1.5")]
    [InlineData("variant", "\"1\"")]
    [InlineData("variant", "99999999999999999999999999999999999999999")]
    [InlineData("representation", "\"single_line\"")]
    [InlineData("reference_mm", "[1]")]
    [InlineData("reference_mm", "[1,\"2\"]")]
    [InlineData("description", "{}")]
    [InlineData("description", "{\"xx_YY\":\"Unknown\"}")]
    public void WindowOptions_AreStrictlyValidated(string member, string value)
    {
        JObject args = Args();
        args[member] = JToken.Parse(value);
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => Run(args)).Error.Reason);
        Assert.Equal(0, _storer.Prepares);
    }
    [Fact]
    public void LocatorShapesAndSelectionBounds_AreStrictlyValidated()
    {
        JObject args = Args();
        JObject locator = (JObject)args["locators"]![0]!;
        foreach (string member in new[] { "project", "page", "type", "oid", "digest" })
        {
            JObject invalid = (JObject)args.DeepClone();
            ((JObject)invalid["locators"]![0]!).Remove(member);
            Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => Run(invalid)).Error.Reason);
        }
        args["locators"] = new JArray(locator, locator.DeepClone());
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => Run(args)).Error.Reason);
        args["locators"] = new JArray(Enumerable.Range(1, 501).Select(i => { var row = (JObject)locator.DeepClone(); row["oid"] = i; return row; }));
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => Run(args)).Error.Reason);
        Assert.Equal(0, _storer.Prepares);
    }
    private sealed class Storer : IMacroStorer
    {
        public int Writes { get; private set; }
        public int Prepares { get; private set; }
        public string? Failure { get; set; }
        public Action? OnWrite { get; set; }
        public bool KnowsLanguage(string language) => language is "en_US" or "zh_CN";
        public Func<int[]> Prepare(string projectPath, JObject args)
        {
            Prepares++;
            args["reference_mm"] ??= new JArray(40, 40);
            return () =>
            {
                Writes++;
                if (Failure != "missing")
                { File.WriteAllText((string)args["path"]!, Failure == "empty" ? "" : "macro"); }
                OnWrite?.Invoke();
                if (Failure == "throw")
                { throw new InvalidOperationException("Native write failed"); }
                return Failure == "variant" ? [1] : [0];
            };
        }
    }
}

[Trait("Size", "Small")]
public sealed class MacroStoreRequestTests
{
    public static TheoryData<string> InvalidRequests => new()
    {
        "{operation:'bad'}", "{operation:1}", "{operation:'window',project:'Line1',page:'P',locators:[],name:'a',path:'C:/a.ema'}",
        "{operation:'page',project:'Line1',pages:[],name:'a',path:'C:/a.emp'}",
        "{operation:'page',project:'Line1',pages:['P','P'],name:'a',path:'C:/a.emp'}",
        "{operation:'page',project:'Line1',pages:['P'],name:'../a',path:'C:/a.emp'}",
        "{operation:'page',project:'Line1',pages:['P'],name:'a',path:'C:/a.emp',description:{en_US:1}}",
        "{operation:'page',project:'Line1',pages:['P'],name:'a',path:'C:/a.emp',dry_run:'false'}",
        "{operation:'page',project:'Line1',pages:['P'],name:'a',path:'relative.emp'}",
        "{operation:'page',project:'Line1',pages:['P'],name:'a',path:'C:/a.ema'}",
    };
    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public void MalformedArguments_AreRejectedBeforeNativePreparation(string json)
    {
        var operation = new MacroStoreOperation(new FakeCatalog(), new UnusedStorer(), new FakeSnapshotReader());
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => operation.Execute((object)JObject.Parse(json))).Error.Reason);
    }
    private sealed class UnusedStorer : IMacroStorer
    {
        public bool KnowsLanguage(string language) => false;
        public Func<int[]> Prepare(string projectPath, JObject args) => throw new InvalidOperationException("Invalid arguments reached native code.");
    }
}

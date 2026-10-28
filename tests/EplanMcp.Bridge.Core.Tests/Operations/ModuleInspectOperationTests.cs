using System.Text.Json;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Core.Protocol;
using EplanMcp.Contracts;
using EplanMcp.Packs;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Medium")]
public sealed class ModuleInspectOperationTests : IDisposable
{
    private const string Hash = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";
    private readonly string _root = Directory.CreateTempSubdirectory("eplanmcp-module-").FullName;
    private readonly Reader _reader = new();
    private readonly ModuleInspectArgs _args;
    public ModuleInspectOperationTests()
    {
        _args = new() { Project = Path.Combine(_root, "Copy.elk"), Macro = Path.Combine(_root, "Module.ema"), Sha256 = Hash, Pages = ["+A1/1"] };
        File.WriteAllText(_args.Macro, "abc");
    }

    [Fact]
    public void Invoke_HoldsStableMacro_UsesOneReadDispatch_AndMatchesHostEvidenceContract()
    {
        _reader.OnRead = args =>
        {
            Assert.Equal(_args.Project, args.Project);
            Assert.Equal(_args.Pages, args.Pages);
            Assert.Throws<IOException>(() => File.WriteAllText(args.Macro, "changed"));
            Assert.Throws<IOException>(() => File.Move(args.Macro, args.Macro + ".old"));
        };
        var dispatcher = new InlineDispatcher();
        var operation = new ModuleInspectOperation(_reader, () => true);
        var router = new OperationRouter([operation], dispatcher, null, new ListLog(), TimeSpan.FromSeconds(1));
        JToken json = router.Invoke(OperationNames.ModuleInspect, BridgeJson.FromObject(_args), "inspect");
        using JsonDocument document = JsonDocument.Parse(json.ToString());
        Assert.True(ModuleInspectionReader.Read(document.RootElement, Hash).Complete);
        Assert.Equal(1, dispatcher.Calls);
        Assert.False(typeof(IWriteOperation).IsAssignableFrom(operation.GetType()));
        Assert.DoesNotContain(_root, json.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("abc", File.ReadAllText(_args.Macro));
        File.WriteAllText(_args.Macro, "released");
    }

    [Theory]
    [InlineData("functions")]
    [InlineData("pages")]
    [InlineData("connections")]
    [InlineData("crossPageReferences")]
    [InlineData("placeholderBindings")]
    [InlineData("properties")]
    [InlineData("native")]
    [InlineData("gap")]
    [InlineData("wrongNames")]
    public void Execute_AnyUnprovenCoverageOrNativeGap_PreventsComplete(string missing)
    {
        if (missing == "native")
        { _reader.Data.Complete = false; }
        else if (missing == "gap")
        { _reader.Data.Gaps.Add(new { pointer = "/objects", message = "Unresolved endpoint." }); }
        else if (missing == "wrongNames")
        { _reader.Data.Coverage.Remove("properties"); _reader.Data.Coverage.Add("unrelated", true); }
        else
        { _reader.Data.Coverage[missing] = false; }
        ModuleInspectData result = Run();
        Assert.False(result.Complete);
        Assert.True(result.ApiExtensionLicence);
        Assert.Equal(Hash, result.MacroHash);
    }

    [Fact]
    public void Execute_MacroChanged_RefusesBeforeNativeDiscovery()
    {
        _args.Sha256 = new string('a', 64);
        Assert.Equal(ErrorReason.PlanStale, Assert.Throws<BridgeErrorException>(() => Run()).Error.Reason);
        Assert.Equal(0, _reader.Calls);
        File.WriteAllText(_args.Macro, "released");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(null)]
    public void Execute_LicenceMissingOrUnknown_RefusesBeforeReading(bool? licence)
    {
        var operation = new ModuleInspectOperation(_reader, () => licence);
        BridgeError error = Assert.Throws<BridgeErrorException>(() => operation.Execute(_args)).Error;
        Assert.Equal(ErrorReason.CapabilityMissing, error.Reason);
        Assert.Equal("licence:APIExtension", error.Metadata!["missing"]);
        Assert.Equal(0, _reader.Calls);
    }

    [Fact]
    public void Execute_LicenceLostDuringRead_DiscardsEvidenceAndReleasesMacro()
    {
        int checks = 0;
        var operation = new ModuleInspectOperation(_reader, () => ++checks == 1);
        Assert.Equal(ErrorReason.CapabilityMissing, Assert.Throws<BridgeErrorException>(() => operation.Execute(_args)).Error.Reason);
        Assert.Equal(1, _reader.Calls);
        File.WriteAllText(_args.Macro, "released");
    }

    [Fact]
    public void Execute_ReaderCancelled_ReleasesMacroAndPropagatesCancellation()
    {
        _reader.OnRead = _ => throw new OperationCanceledException();
        Assert.Throws<OperationCanceledException>(() => Run());
        File.WriteAllText(_args.Macro, "released");
    }

    private ModuleInspectData Run() => (ModuleInspectData)new ModuleInspectOperation(_reader, () => true).Execute(_args)!;
    public void Dispose() => Directory.Delete(_root, true);
    private sealed class Reader : IModuleInspectionReader
    {
        public ModuleInspectData Data { get; } = new()
        {
            Complete = true,
            Coverage = "functions pages connections crossPageReferences placeholderBindings properties".Split(' ').ToDictionary(k => k, _ => true, StringComparer.Ordinal)
        };
        public Action<ModuleInspectArgs>? OnRead { get; set; }
        public int Calls { get; private set; }
        public ModuleInspectData Read(ModuleInspectArgs args)
        { Calls++; OnRead?.Invoke(args); return Data; }
    }
}

using System.Text.Json;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Core.Protocol;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Medium")]
public sealed class PlanMacroInspectOperationTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("eplanmcp-inspect-").FullName;
    private readonly Reader _reader;
    private readonly PlanMacroInspectOperation _operation;

    public PlanMacroInspectOperationTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Samples"));
        File.WriteAllText(Path.Combine(_root, "Samples", "Starter.ema"), "abc");
        _reader = new Reader(_root);
        _operation = new(new FakeCatalog(new OpenProject { Name = "Line1", Path = Path.Combine(_root, "project"), Current = true }), _reader);
    }

    [Fact]
    public void Invoke_HashesTheFile_HoldsReadShare_AndRequestsDefaultMultiLine()
    {
        _reader.OnRead = path =>
        {
            using (FileStream read = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Assert.Equal(3, read.Length);
            }

            Assert.Throws<IOException>(() => File.WriteAllText(path, "changed"));
            Assert.Throws<IOException>(() => File.Move(path, path + ".replacement"));
        };
        var dispatcher = new InlineDispatcher();
        var router = new OperationRouter([_operation], dispatcher, null, new ListLog(), TimeSpan.FromSeconds(1));

        JToken json = router.Invoke(OperationNames.PlanMacroInspect, JObject.Parse("""{"macro":"Samples\\Starter.ema"}"""), "inspect");
        PlanMacroInspectData data = json.ToObject<PlanMacroInspectData>(BridgeJson.Serializer)!;

        Assert.Contains(OperationNames.PlanMacroInspect, router.SupportedOperations);
        Assert.False(typeof(IWriteOperation).IsAssignableFrom(_operation.GetType()));
        Assert.Equal(1, dispatcher.Calls);
        Assert.True(_reader.DefaultMultiLine);
        Assert.Equal(Path.Combine(_root, "project"), _reader.Project);
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", data.Sha256);
        Assert.Equal("abc", File.ReadAllText(Path.Combine(_root, "Samples", "Starter.ema")));
        File.WriteAllText(Path.Combine(_root, "Samples", "Starter.ema"), "released");
        AssertNoPaths(json.ToString());
        AssertNoPaths(JsonSerializer.Serialize(data));
    }

    [Fact]
    public void Invoke_MoreThanFiftyUniqueDevices_ReturnsAllFunctionsWithoutGrouping()
    {
        _reader.Content.Functions = [.. Enumerable.Range(1, 60).Select(i => new MacroFunctionRecord
            { Kind = "function", Category = "Coil", Name = "=+A1-K" + i.ToString(System.Globalization.CultureInfo.InvariantCulture), Main = true })];
        _reader.Content.Functions.Add(new() { Kind = "function", Category = "Coil", Name = "=+A1-K1" });
        _reader.Content.Functions.Add(new() { Kind = "function", Category = "Wire", Name = "+" });
        _reader.Content.FunctionCount = 62;
        _reader.Content.ObjectCount = 80;

        PlanMacroInspectData inspect = Run();
        MacroGetData get = (MacroGetData)new MacroGetOperation(new FakeCatalog(new OpenProject { Name = "Line1", Path = "p" }), _reader)
            .Execute(new MacroGetArgs { Macro = "Samples/Starter.ema" })!;

        Assert.Equal((62, 80), (inspect.Functions.Count, inspect.ObjectCount));
        Assert.Equal((50, 60), (get.Devices.Count, get.DeviceCount));
        Assert.Equal(get.Devices.Select(d => (d.Tag, d.Category, d.Main)), inspect.Functions.Take(50).Select(f => (f.Name, f.Category, f.Main)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("../Starter.ema")]
    [InlineData("C:/private/Starter.ema")]
    [InlineData("//server/share/Starter.ema")]
    [InlineData("Samples/Starter.ems")]
    public void Invoke_InvalidMacroPath_IsRefusedBeforeReading_WithoutPaths(string? macro)
    {
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Run(macro)).Error;
        Assert.Equal(ErrorReason.InvalidArgument, error.Reason);
        Assert.Null(_reader.Project);
        AssertNoPaths(BridgeJson.FromObject(error).ToString());
    }

    [Fact]
    public void Invoke_IncompleteReader_RefusesAndReleasesTheHandle()
    {
        _reader.Content.Complete = false;
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Run()).Error;
        Assert.Equal(ErrorReason.CapabilityMissing, error.Reason);
        AssertNoPaths(BridgeJson.FromObject(error).ToString());
        File.WriteAllText(Path.Combine(_root, "Samples", "Starter.ema"), "released");
    }

    [Fact]
    public void Invoke_NoSelectedProject_DoesNotExposeCandidatePaths()
    {
        var operation = new PlanMacroInspectOperation(new FakeCatalog(new OpenProject { Path = _root + "/one" }, new OpenProject { Path = _root + "/two" }), _reader);
        BridgeError error = Assert.Throws<BridgeErrorException>(() => operation.Execute(new PlanMacroInspectArgs { Macro = "Samples/Starter.ema" })).Error;
        Assert.Equal(ErrorReason.CapabilityMissing, error.Reason);
        AssertNoPaths(BridgeJson.FromObject(error).ToString());
        Assert.Null(_reader.Project);
    }

    [Fact]
    public void Invoke_FileAlreadyOpenForWrite_RefusesStableInspection()
    {
        using var writer = new FileStream(Path.Combine(_root, "Samples", "Starter.ema"), FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Run()).Error;
        Assert.Equal(ErrorReason.CapabilityMissing, error.Reason);
        Assert.Null(_reader.Project);
        AssertNoPaths(BridgeJson.FromObject(error).ToString());
    }

    public void Dispose()
    {
        Assert.StartsWith(Path.GetTempPath(), _root, StringComparison.OrdinalIgnoreCase);
        Directory.Delete(_root, recursive: true);
    }

    private PlanMacroInspectData Run() => Run("Samples/Starter.ema");
    private PlanMacroInspectData Run(string? macro) => (PlanMacroInspectData)_operation.Execute(new PlanMacroInspectArgs { Macro = macro })!;

    private void AssertNoPaths(string json)
    {
        Assert.DoesNotContain(_root, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SourceProject", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("C:", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private", json, StringComparison.OrdinalIgnoreCase);
        using JsonDocument document = JsonDocument.Parse(json);
        Assert.All(document.RootElement.EnumerateObject(), p => Assert.DoesNotContain("path", p.Name, StringComparison.OrdinalIgnoreCase));
    }

    private sealed class Reader(string root) : IMacroReader
    {
        public MacroContent Content { get; } = PlanMacroDescriptionTests.Content();
        public bool DefaultMultiLine { get; private set; }
        public string? Project { get; private set; }
        public Action<string>? OnRead { get; set; }
        public string? MacroRoot() => root;

        public MacroContent ReadMacro(string projectPath, string macroPath, bool defaultMultiLine = false)
        {
            Project = projectPath;
            DefaultMultiLine = defaultMultiLine;
            OnRead?.Invoke(macroPath);
            return Content;
        }
    }
}

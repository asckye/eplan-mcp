using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Packs;
using EplanMcp.Host.Tests.Tools;
using EplanMcp.Packs;
using EplanMcp.Tests.Shared;

namespace EplanMcp.Host.Tests.Packs;

[Trait("Size", "Medium")]
public sealed class ModuleInspectTests : IDisposable
{
    private readonly string _home = Directory.CreateTempSubdirectory("eplanmcp-module-").FullName;
    private readonly FakeBridgeClient _bridge = new();
    private readonly ReferenceCopyService _copies;
    private readonly ModuleInspectService _service;
    private JsonObject _evidence = null!;
    private string Source => Path.Combine(_home, "source", "Module.elk");
    private string Macro => Path.Combine(_home, "resources", "macros", "Module.ema");
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public ModuleInspectTests()
    {
        WriteProject(Source);
        Directory.CreateDirectory(Path.GetDirectoryName(Macro)!);
        File.WriteAllText(Macro, "native macro fixture");
        _copies = new(_bridge, Path.Combine(_home, "scratch"), new Dictionary<string, string> { ["fixture"] = Path.GetDirectoryName(Source)! });
        _service = new(_bridge, _copies, _home);
        _bridge.Handlers[OperationNames.ReferencePrepare] = args =>
        {
            var prepare = Assert.IsType<ReferencePrepareArgs>(args);
            WriteProject(prepare.Destination);
            _bridge.Projects.Add(new OpenProject { Path = prepare.Destination, ReadOnly = true });
            return new ReferenceCopyData { Handle = "owned", Path = prepare.Destination };
        };
        _bridge.Handlers[ModuleInspectService.Operation] = _ => ModuleInspectionEvidence.Element(_evidence);
        _bridge.Status = new([], new HandshakeResult { Eplan = new() { ApiExtensionLicence = true }, Operations = [ModuleInspectService.Operation] }, null);
    }

    [Fact]
    public async Task Inspect_CompleteClosure_UsesOnlyOwnedReadOnlyCopyAndLeavesFilesUnchanged()
    {
        (ReferenceCopyData copy, PackResource macro) = await ReadyAsync();
        string before = (await PackHasher.HashTreeAsync(PackHasher.ProjectFiles(copy.Path), Token)).Sha256;
        _bridge.Handlers[ModuleInspectService.Operation] = args =>
        {
            JsonElement sent = JsonSerializer.SerializeToElement(args, PackJson.Options);
            Assert.Equal(copy.Path, sent.GetProperty("project").GetString());
            Assert.Equal(Macro, sent.GetProperty("macro").GetString());
            Assert.Equal(macro.Sha256, sent.GetProperty("sha256").GetString());
            Assert.Equal(["=AP+A1/1"], sent.GetProperty("pages").EnumerateArray().Select(p => p.GetString()));
            Assert.Throws<IOException>(() => File.OpenWrite(Macro));
            return ModuleInspectionEvidence.Element(_evidence);
        };

        ModuleInspectResult result = await InspectAsync(copy, macro);

        Assert.True(result.Complete);
        Assert.Null(result.Problem);
        Assert.Empty(result.Gaps);
        Assert.Equal(6, result.Inspection!.Evidence.GetProperty("objects").GetArrayLength());
        Assert.Equal(before, (await PackHasher.HashTreeAsync(PackHasher.ProjectFiles(copy.Path), Token)).Sha256);
        Assert.Equal(macro.Sha256, (await PackHasher.HashFileAsync(Macro, Token)).Sha256);
        Assert.Equal(1, _bridge.StatusRefreshes);
        Assert.Empty(_bridge.Writes);
        Assert.DoesNotContain(_bridge.Calls, c => c.Operation.Contains("execute", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("native")]
    [InlineData("unmapped")]
    [InlineData("cross_page")]
    public async Task Inspect_IncompleteClosure_ReturnsEvidenceAndExplicitGap(string condition)
    {
        (ReferenceCopyData copy, PackResource macro) = await ReadyAsync();
        if (condition == "native")
        { _evidence["complete"] = false; }
        else if (condition == "unmapped")
        { _evidence["placeholders"]![0]!["bindings"] = new JsonArray(); }
        else
        { _evidence["references"]![0]!["to"] = "unresolved/other-page"; }

        ModuleInspectResult result = await InspectAsync(copy, macro);

        Assert.False(result.Complete);
        Assert.NotNull(result.Inspection);
        Assert.Null(result.Problem);
        Assert.NotEmpty(result.Gaps);
        Assert.All(result.Gaps, gap => Assert.Equal("module_incomplete", gap.Code));
        Assert.Empty(_bridge.Writes);
    }

    [Theory]
    [InlineData("operation", "op:module.inspect")]
    [InlineData("licence", "licence:APIExtension")]
    [InlineData("unknown", "licence:APIExtension")]
    public async Task Inspect_MissingCapability_RefusesBeforeInspectionWithHint(string condition, string missing)
    {
        (ReferenceCopyData copy, PackResource macro) = await ReadyAsync();
        if (condition == "operation")
        { _bridge.Status.Handshake!.Operations = []; }
        else
        { _bridge.Status.Handshake!.Eplan.ApiExtensionLicence = condition == "licence" ? false : null; }

        ModuleInspectResult result = await InspectAsync(copy, macro);

        Assert.False(result.Complete);
        Assert.Null(result.Inspection);
        Assert.Equal(ErrorReason.CapabilityMissing, result.Problem!.Reason);
        Assert.Equal(missing, result.Problem.Metadata!["missing"]);
        Assert.NotEmpty(result.Problem.Message);
        Assert.Null(result.Problem.Execution);
        Assert.NotEmpty(result.Gaps);
        Assert.DoesNotContain(_bridge.Calls, c => c.Operation == ModuleInspectService.Operation);
    }

    [Theory]
    [InlineData("false")]
    [InlineData("null")]
    [InlineData("throw")]
    [InlineData("missing_operation")]
    public async Task Inspect_CapabilityLostInsideOperation_ProducesNoInspection(string condition)
    {
        (ReferenceCopyData copy, PackResource macro) = await ReadyAsync();
        if (condition is "throw" or "missing_operation")
        {
            _bridge.Handlers[ModuleInspectService.Operation] = _ => throw new BridgeCallException(new BridgeError
            { Reason = condition == "throw" ? ErrorReason.LicenceMissing : ErrorReason.CapabilityMissing });
        }
        else
        { _evidence["apiExtensionLicence"] = JsonNode.Parse(condition); }

        ModuleInspectResult result = await InspectAsync(copy, macro);

        Assert.False(result.Complete);
        Assert.Null(result.Inspection);
        Assert.Equal(ErrorReason.CapabilityMissing, result.Problem!.Reason);
        Assert.Single(result.Gaps);
        Assert.Equal(condition == "missing_operation" ? "op:module.inspect" : "licence:APIExtension", result.Problem.Metadata!["missing"]);
        Assert.Null(result.Problem.Execution);
    }

    [Theory]
    [InlineData("before")]
    [InlineData("during")]
    [InlineData("failure")]
    [InlineData("cancel")]
    public async Task Inspect_SourceChanged_RejectsInsteadOfPublishingEvidence(string condition)
    {
        (ReferenceCopyData copy, PackResource macro) = await ReadyAsync();
        if (condition == "before")
        { File.AppendAllText(Source, "changed"); }
        else
        {
            _bridge.Handlers[ModuleInspectService.Operation] = _ =>
            {
                File.AppendAllText(Source, "changed");
                if (condition == "failure")
                { throw new IOException("Read failed."); }
                if (condition == "cancel")
                { throw new OperationCanceledException(); }
                return ModuleInspectionEvidence.Element(_evidence);
            };
        }
        BridgeCallException error = await Assert.ThrowsAsync<BridgeCallException>(() => InspectAsync(copy, macro));
        Assert.Equal(ErrorReason.PlanStale, error.Error.Reason);
    }

    [Fact]
    public async Task Inspect_ChangedMacro_RejectsBeforeBridge()
    {
        (ReferenceCopyData copy, PackResource macro) = await ReadyAsync();
        File.AppendAllText(Macro, "changed");
        BridgeCallException error = await Assert.ThrowsAsync<BridgeCallException>(() => InspectAsync(copy, macro));
        Assert.Equal(ErrorReason.PlanStale, error.Error.Reason);
        Assert.DoesNotContain(_bridge.Calls, c => c.Operation == ModuleInspectService.Operation);
    }

    [Fact]
    public async Task Inspect_WritableOrForgedCopy_RejectsBeforeInspection()
    {
        (ReferenceCopyData copy, PackResource macro) = await ReadyAsync();
        _bridge.Projects[0].ReadOnly = false;
        await Assert.ThrowsAsync<BridgeCallException>(() => InspectAsync(copy, macro));
        await Assert.ThrowsAsync<BridgeCallException>(() => InspectAsync(new ReferenceCopyData { Handle = "forged" }, macro));
        Assert.DoesNotContain(_bridge.Calls, c => c.Operation == ModuleInspectService.Operation);
    }

    private Task<ModuleInspectResult> InspectAsync(ReferenceCopyData copy, PackResource macro) => _service.InspectAsync(copy.Handle, ["=AP+A1/1"], macro, null, Token);
    private async Task<(ReferenceCopyData, PackResource)> ReadyAsync()
    {
        (string sourceHash, long bytes, int files) = await PackHasher.HashTreeAsync(PackHasher.ProjectFiles(Source), Token);
        ReferenceCopyData copy = await _copies.PrepareAsync(new PackResource
        {
            Path = "references/Module.elk",
            Kind = ResourceKind.Project,
            Storage = ResourceStorage.Referenced,
            Reference = new ResourceReference { Root = "fixture", Path = "Module.elk" },
            Sha256 = sourceHash,
            Bytes = bytes,
            Files = files
        }, Token);
        (string hash, long length) = await PackHasher.HashFileAsync(Macro, Token);
        _evidence = ModuleInspectionEvidence.Create(hash);
        return (copy, new PackResource { Path = "resources/macros/Module.ema", Kind = ResourceKind.Macro, Storage = ResourceStorage.Bundled, Sha256 = hash, Bytes = length, Files = 1 });
    }
    private static void WriteProject(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "project link");
        Directory.CreateDirectory(Path.ChangeExtension(path, ".edb"));
        foreach (string file in new[] { "User.eod", "User.eox", "Pages.eod" })
        { File.WriteAllText(Path.Combine(Path.ChangeExtension(path, ".edb"), file), file); }
    }
    public void Dispose() => Directory.Delete(_home, recursive: true);
}

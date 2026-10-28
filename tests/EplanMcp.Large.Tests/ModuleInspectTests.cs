using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Packs;
using EplanMcp.Host.Plans;
using EplanMcp.Packs;

namespace EplanMcp.Large.Tests;

[Trait("Size", "Large")]
public sealed class ModuleInspectTests(LiveEplan eplan)
{
    [Fact]
    public async Task PreparedReference_ActualClosureMatchesHandCheckedFixture_LeavesSourceAndCopyUnchanged()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        string? fixture = Environment.GetEnvironmentVariable("EPLANMCP_MODULE_FIXTURE");
        Assert.SkipUnless(fixture is not null, "Prepare the non-customer module fixture and hand-checked closure described in fixtures/module-inspect/README.md.");
        fixture = Path.GetFullPath(fixture);
        Assert.StartsWith(Path.GetFullPath(LiveEplan.ScratchRoot()) + Path.DirectorySeparatorChar, fixture, StringComparison.OrdinalIgnoreCase);
        var paths = new PlanPaths(fixture);
        string source = paths.Check(Path.Combine(fixture, "reference", "Module.elk"));
        string macro = paths.Check(Path.Combine(fixture, "resources", "macros", "Module.ema"));
        using JsonDocument expected = JsonDocument.Parse(await File.ReadAllBytesAsync(paths.Check(Path.Combine(fixture, "expected-closure.json")), token));
        JsonElement golden = expected.RootElement;
        Assert.Equal("engineer", golden.GetProperty("reviewedBy").GetString());
        string[] pages = [.. golden.GetProperty("pages").EnumerateArray().Select(p => p.GetString()!)];
        Assert.NotEmpty(pages);
        await using BridgeClient bridge = await LiveSnapshots.BridgeAsync(eplan.ProcessId);
        BridgeStatus status = await bridge.GetStatusAsync(true, token);
        Assert.SkipUnless(status.Handshake?.Operations is { } operations && new[] { OperationNames.ReferencePrepare, OperationNames.ReferenceClose, ModuleInspectService.Operation }.All(operations.Contains),
            "Reference and module operations remain unregistered until their integration gate is enabled.");
        (string sourceHash, long bytes, int files) = await PackHasher.HashTreeAsync(PackHasher.ProjectFiles(source), token);
        (string macroHash, long macroBytes) = await PackHasher.HashFileAsync(macro, token);
        var resource = new PackResource
        {
            Path = "references/Module.elk",
            Kind = ResourceKind.Project,
            Storage = ResourceStorage.Referenced,
            Reference = new ResourceReference { Root = "fixture", Path = "Module.elk" },
            Sha256 = sourceHash,
            Bytes = bytes,
            Files = files
        };
        var native = new PackResource { Path = "resources/macros/Module.ema", Kind = ResourceKind.Macro, Storage = ResourceStorage.Bundled, Sha256 = macroHash, Bytes = macroBytes, Files = 1 };
        var copies = new ReferenceCopyService(bridge, fixture, new Dictionary<string, string> { ["fixture"] = Path.GetDirectoryName(source)! });
        ReferenceCopyData copy = await copies.PrepareAsync(resource, token);
        try
        {
            Dictionary<string, string> before = LiveSnapshots.DesignFiles(copy.Path[..^4]);
            ModuleInspectResult report = await new ModuleInspectService(bridge, copies, fixture).InspectAsync(copy.Handle, pages, native, null, token);
            Assert.Null(report.Problem);
            Assert.NotNull(report.Inspection);
            Assert.Equal(golden.GetProperty("complete").GetBoolean(), report.Complete);
            JsonElement actual = report.Inspection.Evidence;
            foreach (string member in new[] { "objects", "references", "placeholders" })
            { Assert.True(JsonElement.DeepEquals(golden.GetProperty(member), actual.GetProperty(member)), "Hand-checked closure differs in " + member); }
            Assert.Equal(golden.GetProperty("gapPointers").EnumerateArray().Select(p => p.GetString()).Order(StringComparer.Ordinal), report.Gaps.Select(g => g.Subject).Order(StringComparer.Ordinal));
            Assert.Contains(actual.GetProperty("references").EnumerateArray(), r => r.GetProperty("kind").GetString() == "cross_reference");
            Assert.Equal(before.OrderBy(p => p.Key, StringComparer.Ordinal), LiveSnapshots.DesignFiles(copy.Path[..^4]).OrderBy(p => p.Key, StringComparer.Ordinal));
        }
        finally
        { await copies.CloseAsync(copy.Handle, CancellationToken.None); }
        Assert.Equal(sourceHash, (await PackHasher.HashTreeAsync(PackHasher.ProjectFiles(source), token)).Sha256);
        Assert.Equal(macroHash, (await PackHasher.HashFileAsync(macro, token)).Sha256);
    }
}

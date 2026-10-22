using System.Globalization;
using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Audit;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Packs;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tests.Safety;
using EplanMcp.Host.Tests.Tools;
using EplanMcp.Host.Tools;
using EplanMcp.Packs;
using EplanMcp.Packs.Scanning;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tests.Packs;

/// <summary>
/// Installing someone else's .eplanpack through eplan_pack_import (ADR-0024 sections 2 and 4): the engineer confirms in
/// a fake approval channel; a refusal leaves nothing behind.
/// </summary>
[Trait("Size", "Medium")]
public sealed class PackInstallTests : IDisposable
{
    private readonly HostDataLocations _locations = new(Directory.CreateTempSubdirectory("eplanmcp-pack-install-").FullName);
    private readonly ManualTime _time = new(new DateTimeOffset(2026, 10, 21, 8, 30, 0, TimeSpan.Zero));
    private readonly FakeBridgeClient _bridge = new()
    {
        Status = new BridgeStatus([], new HandshakeResult { Eplan = new EplanInstanceInfo { ProcessId = 42, Version = "2026.0.3.25421" } }, null),
    };

    private HostConfig Config => HostConfig.Defaults(_locations);

    private string Installed => Path.Combine(_locations.Packs, "other.pack", DevPackStore.ManifestVersion);

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Install_Confirmed_InstallsTheCheckedPackAndAudits()
    {
        await WritePackAsync();
        var channel = new FakeApprovalChannel("eplan_dialog", ApprovalDecision.Granted);

        CallToolResult result = await ImportAsync(channel, "other.pack");

        ToolResultAssert.Succeeded(result);
        JsonElement installed = result.StructuredContent!.Value.GetProperty("installed");
        Assert.Equal(("other.pack", "installed"), (installed.GetProperty("id").GetString(), installed.GetProperty("outcome").GetString()));
        Assert.True(File.Exists(Path.Combine(Installed, PackManifest.FileName)));
        PackInstallInfo asked = Assert.Single(channel.Questions).PackInstall!;
        Assert.Equal(("other.pack", "shared/other.eplanpack", installed.GetProperty("digest").GetString()), (asked.Id, asked.File, asked.Digest));
        Assert.Contains("macro: resources/macros/in/Motor.ema", asked.NativeFiles);
        Assert.Empty(Directory.GetDirectories(_locations.Packs, ".*"));
    }

    [Theory]
    [InlineData(false, ErrorReason.ApprovalDenied)]
    [InlineData(true, ErrorReason.ApprovalExpired)]
    public async Task Install_NotConfirmed_LeavesNothing(bool expired, string reason)
    {
        await WritePackAsync();

        Assert.Equal(reason, Reason(await ImportAsync(new FakeApprovalChannel("eplan_dialog", expired ? ApprovalDecision.Expired : ApprovalDecision.Denied), "other.pack")));

        Assert.False(Directory.Exists(Path.Combine(_locations.Packs, "other.pack")));
        Assert.Empty(Directory.GetDirectories(_locations.Packs, ".*"));
    }

    [Fact]
    public async Task Install_WithoutEplan_IsRefusedWithoutAsking()
    {
        await WritePackAsync();
        _bridge.Status = new BridgeStatus([], null, null);
        var channel = new FakeApprovalChannel("eplan_dialog", ApprovalDecision.Granted);

        Assert.Equal(ErrorReason.ApprovalDenied, Reason(await ImportAsync(channel, "other.pack")));
        Assert.Empty(channel.Questions);
    }

    [Fact]
    public async Task Install_TamperedOrForeignIdOrMixedInputs_AreRefused()
    {
        await WritePackAsync();
        var channel = new FakeApprovalChannel("eplan_dialog", ApprovalDecision.Granted);

        Assert.Equal(ErrorReason.InvalidArgument, Reason(await ImportAsync(channel, "acme.panels")));
        Assert.Equal(ErrorReason.InvalidArgument, Reason(await ImportAsync(channel, "other.pack", "shared/other.eplanpack", "in")));
        byte[] bytes = File.ReadAllBytes(Path.Combine(_locations.Imports, "shared", "other.eplanpack"));
        bytes[^30] ^= 0xFF;
        File.WriteAllBytes(Path.Combine(_locations.Imports, "shared", "other.eplanpack"), bytes);
        Assert.Equal(ErrorReason.PackInvalid, Reason(await ImportAsync(channel, "other.pack")));
        Assert.Empty(channel.Questions);
    }

    [Fact]
    public void Dialog_NamesThePackAndSaysItHoldsDataOnly()
    {
        var info = new PackInstallInfo("shared/other.eplanpack", "other.pack", "1.0.0", null, new string('a', 64), 2048, ["macro: resources/macros/Motor.ema"], [], 1);
        var question = new ApprovalQuestion("r1", PackTools.ImportTool, info.Digest, new WritePlan { Fingerprint = info.Digest }, [], _time.GetUtcNow()) { Tier = ToolTier.Write, Eplan = null, PackInstall = info };

        ApprovalAskArgs english = ApprovalTexts.For(question, CultureInfo.GetCultureInfo("en-US"));

        Assert.Equal("Install someone else's engineering pack?", english.Title);
        Assert.Contains("Pack: other.pack 1.0.0", english.Summary, StringComparison.Ordinal);
        Assert.Contains("Resources referenced outside the pack: 1", english.Summary, StringComparison.Ordinal);
        Assert.Equal(["macro: resources/macros/Motor.ema"], english.Changes);
        Assert.Contains("never runs anything", english.Note, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        foreach (string file in Directory.EnumerateFiles(_locations.Root, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(_locations.Root, recursive: true);
    }

    private Task<CallToolResult> ImportAsync(FakeApprovalChannel channel, string pack, params string[] inputs)
    {
        var packs = new PackLibrary(_locations, Config, new AuditLog(_locations.Audit, _time), _time);
        var installer = new PackInstaller(packs, new ApprovalService([channel], Config, _time, NullLogger<ApprovalService>.Instance), _bridge);
        return PackTools.PackImportAsync(packs, new GapOnlyReferenceProjectSteps(), installer, pack, inputs.Length == 0 ? ["shared/other.eplanpack"] : inputs, null, false, false, null, Cancel);
    }

    private static string? Reason(CallToolResult result) =>
        result.IsError == true ? JsonDocument.Parse(((TextContentBlock)result.Content[0]).Text).RootElement.GetProperty("error").GetProperty("reason").GetString() : null;

    /// <summary>An .eplanpack made from another engineer's development pack, in the imports folder.</summary>
    private async Task WritePackAsync()
    {
        string input = Path.Combine(_locations.Root, "elsewhere", "in");
        Directory.CreateDirectory(input);
        await File.WriteAllTextAsync(Path.Combine(input, "Motor.ema"), "<EplanPxfRoot Type=\"WindowMacro\" Version=\"2026.0.25356\" />", Cancel);
        Directory.CreateDirectory(Path.Combine(_locations.Imports, "in"));
        await File.WriteAllTextAsync(Path.Combine(_locations.Imports, "in", "guide.md"), "# Guide", Cancel);
        var store = new DevPackStore(Path.Combine(_locations.Root, "elsewhere", "packs-dev"));
        await store.ImportAsync("other.pack", "Other pack", await ResourceScanner.ScanAsync([new ScanInput(input)], Cancel), false, "eplanmcp test", _time.GetUtcNow(), Cancel);
        Directory.CreateDirectory(Path.Combine(_locations.Imports, "shared"));
        await PackArchive.ExportAsync(store.FolderOf("other.pack"), Path.Combine(_locations.Imports, "shared", "other.eplanpack"), Cancel);
    }
}

using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Audit;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Safety;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tests.Tools;

/// <summary>
/// A write tool's surroundings for Medium tests: the real write pipeline with its audit, suspensions and backup folders
/// in a temporary data directory, a fake Bridge connected to "EPLAN" in this test process, and a project path inside
/// scratch. Approvals go to <c>channels</c>; with none, every approval is refused.
/// </summary>
internal sealed class WriteToolHarness : IDisposable
{
    public WriteToolHarness(params IApprovalChannel[] channels)
    {
        Locations = new HostDataLocations(Directory.CreateTempSubdirectory("eplanmcp-write-tool-").FullName);
        ProjectPath = Path.Combine(Locations.DefaultScratch, "Line1");
        Bridge.Status = new BridgeStatus(
            [new BridgeEndpoint("p", Environment.ProcessId)],
            new HandshakeResult { ProtocolVersion = 1, BridgeVersion = "b", Eplan = new EplanInstanceInfo { ProcessId = Environment.ProcessId, Version = "2026.0.3" } },
            null);
        var audit = new AuditLog(Locations.Audit, TimeProvider.System);
        Pipeline = new WritePipeline(
            Bridge,
            HostConfig.Defaults(Locations),
            SuspensionStore.For(Locations),
            audit,
            new DanglingWrites(Bridge, SuspensionStore.For(Locations), audit, TimeProvider.System, NullLogger<DanglingWrites>.Instance),
            new ApprovalService(channels, HostConfig.Defaults(Locations), TimeProvider.System, NullLogger<ApprovalService>.Instance),
            new BackupFolders(Locations, TimeProvider.System),
            TimeProvider.System,
            NullLogger<WritePipeline>.Instance);
    }

    public HostDataLocations Locations { get; }

    public FakeBridgeClient Bridge { get; } = new();

    public WritePipeline Pipeline { get; }

    /// <summary>The path plans report; inside the scratch root unless a test moves it.</summary>
    public string ProjectPath { get; set; }

    public void Dispose() => Directory.Delete(Locations.Root, recursive: true);

    /// <summary>A plan for <see cref="ProjectPath"/> in project "Line1".</summary>
    public WritePlan Plan(IEnumerable<PlannedChange> changes) => new() { Project = "Line1", ProjectPath = ProjectPath, Changes = [.. changes], Fingerprint = "f1" };

    public static string Text(CallToolResult result, int index) => ((TextContentBlock)result.Content[index]).Text;

    public static JsonElement Error(CallToolResult result)
    {
        Assert.True(result.IsError);
        return JsonDocument.Parse(Text(result, 0)).RootElement.GetProperty("error");
    }

    public static JsonElement Structured(CallToolResult result)
    {
        ToolResultAssert.Succeeded(result);
        return result.StructuredContent!.Value;
    }
}

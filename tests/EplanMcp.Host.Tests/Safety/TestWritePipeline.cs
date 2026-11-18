using EplanMcp.Host.Audit;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Safety;
using Microsoft.Extensions.Logging.Abstractions;

namespace EplanMcp.Host.Tests.Safety;

internal static class TestWritePipeline
{
    /// <summary>Builds the real write pipeline for a test Bridge and data directory.</summary>
    public static WritePipeline Create(
        IBridgeClient bridge,
        HostDataLocations locations,
        HostConfig? config = null,
        TimeProvider? approvalClock = null,
        params IApprovalChannel[] channels)
    {
        config ??= HostConfig.Defaults(locations);
        TimeProvider clock = TimeProvider.System;
        var audit = new AuditLog(locations.Audit, clock);
        var suspensions = SuspensionStore.For(locations);
        return new WritePipeline(
            bridge,
            config,
            suspensions,
            audit,
            new DanglingWrites(bridge, suspensions, audit, clock, NullLogger<DanglingWrites>.Instance),
            new ApprovalService(channels, config, approvalClock ?? clock, NullLogger<ApprovalService>.Instance),
            new BackupFolders(locations, clock),
            clock,
            NullLogger<WritePipeline>.Instance);
    }
}

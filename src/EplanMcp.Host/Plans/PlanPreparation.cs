using EplanMcp.Contracts;

namespace EplanMcp.Host.Plans;

/// <summary>P20 owns the project lock until execution and settlement end. Fingerprints are never persisted.</summary>
internal sealed record PlanPreparation(PlanRecord Record, TaskRecord? Task, IReadOnlyList<string> Fingerprints,
    EplanInstanceInfo? Instance, string? Pipe, DateTimeOffset? InstanceStart, DateTimeOffset Deadline, PlanBackup? Backup, FileStream? ProjectLock) : IDisposable
{
    public void Dispose() => ProjectLock?.Dispose();
}

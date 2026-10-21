using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Host.Audit;
using EplanMcp.Host.Doctor;

namespace EplanMcp.Host.Tests;

/// <summary>
/// The JSON of <c>doctor --format json</c> and <c>audit verify --format json</c> (architecture 5.2) as reviewed
/// snapshots: the Workbench parses it, so a change of shape fails here until the new snapshot in
/// tests/Contracts.Snapshots is reviewed and committed with a new schemaVersion (testing.md 3). The checks and results
/// are fixed, so only the shape is pinned, not doctor's texts. Medium: the snapshots are files.
/// </summary>
[Trait("Size", "Medium")]
public sealed class CliJsonSnapshotTests
{
    private const string AuditDirectory = @"D:\EplanMcp\audit";

    // The command's JSON laid out for review: indented, LF line ends, Chinese text unescaped as on stdout.
    private static readonly JsonSerializerOptions _snapshotJson = new(HostJson.Options) { WriteIndented = true, NewLine = "\n" };

    [Fact]
    public Task DoctorJson_MatchesTheReviewedSnapshot()
    {
        DoctorCheck[] checks =
        [
            new(DoctorCheckIds.HostVersion, DoctorStatus.Info, "eplanmcp 0.1.0", "eplanmcp 0.1.0"),
            new(DoctorCheckIds.EplanInstallation, DoctorStatus.Ok, "EPLAN 2026.0.3.25421：E", "EPLAN 2026.0.3.25421: E", "2026.0.3.25421"),
            new(DoctorCheckIds.EplanRunning, DoctorStatus.Warning, "EPLAN 没有在运行", "EPLAN is not running"),
            new(DoctorCheckIds.BridgePipe, DoctorStatus.Failure, "没有发现 EplanMcp 插件", "No EplanMcp add-in found"),
        ];

        return Snapshot(DoctorCommand.FormatJson("0.1.0", checks), "doctor-json");
    }

    [Fact]
    public Task AuditVerifyJson_MatchesTheReviewedSnapshot()
    {
        var results = new JsonObject
        {
            ["noDirectory"] = JsonNode.Parse(AuditCommand.FormatJson(AuditDirectory, exists: false, 0, null)),
            ["intact"] = JsonNode.Parse(AuditCommand.FormatJson(AuditDirectory, exists: true, 3, null)),
            ["changed"] = JsonNode.Parse(AuditCommand.FormatJson(AuditDirectory, exists: true, 1, new AuditBreak("2026-10-20.jsonl", 2, AuditProblem.Changed))),
            ["missing"] = JsonNode.Parse(AuditCommand.FormatJson(AuditDirectory, exists: true, 1, new AuditBreak("2026-10-20.jsonl", 2, AuditProblem.Missing))),
            ["notARecord"] = JsonNode.Parse(AuditCommand.FormatJson(AuditDirectory, exists: true, 2, new AuditBreak("2026-10-21.jsonl", 1, AuditProblem.NotARecord))),
        };

        return Snapshot(results.ToJsonString(), "audit-verify-json");
    }

    private static SettingsTask Snapshot(string json, string name) =>
        Verify(JsonNode.Parse(json)!.ToJsonString(_snapshotJson) + "\n", "json")
            .UseDirectory("../Contracts.Snapshots")
            .UseFileName(name);
}

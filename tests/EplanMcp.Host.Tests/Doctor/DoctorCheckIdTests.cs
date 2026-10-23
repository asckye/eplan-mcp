using System.Globalization;
using System.Reflection;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Doctor;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tests.Http;
using EplanMcp.Host.Tests.Tools;

namespace EplanMcp.Host.Tests.Doctor;

/// <summary>
/// The Workbench groups the doctor checks by their IDs (M5 design 4.4), so every check carries one of
/// <see cref="DoctorCheckIds"/>, each of which is documented in architecture 5.2. Medium: the data directory checks
/// look at real directories, and the documentation is a file.
/// </summary>
[Trait("Size", "Medium")]
public sealed class DoctorCheckIdTests : IDisposable
{
    private static readonly string[] _ids =
        [.. typeof(DoctorCheckIds).GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!)];

    private readonly string _root = Directory.CreateTempSubdirectory("eplanmcp-doctor-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task EveryCheck_HasADocumentedIdAndEveryIdIsUsed()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        DoctorCheck[] checks =
        [
            DoctorChecks.HostVersion(),
            .. VmDoctorChecks.Read(() => [], _ => null, new FakeVmFirewall()),
            .. DataChecks(Missing()),
            .. DataChecks(Populated()),
            .. DataChecks(UnwritableAudit()),
            DoctorChecks.MsixEnvironment(Path.Combine(_root, "local")),
            DoctorChecks.MsixEnvironment(null, "Claude_pzs8sxrjxfjjc"),
            DoctorChecks.MsixEnvironment("Claude_1.0_x64__pzs8sxrjxfjjc", null),
            .. DoctorChecks.EplanInstallations(_root),
            DoctorChecks.Installation("2026.0.3.25421", "E"),
            DoctorChecks.Installation("2025.0.3.1", "E"),
            DoctorChecks.Installation("2023.0.3.19351", "E"),
            .. DoctorChecks.RunningEplan([]),
            .. DoctorChecks.RunningEplan([7]),
            .. await DoctorChecks.BridgeAsync(new FakeBridgeClient(), cancellationToken),
            .. await DoctorChecks.BridgeAsync(Handshake(licence: true), cancellationToken),
            .. await DoctorChecks.BridgeAsync(Handshake(licence: false), cancellationToken),
            .. await DoctorChecks.BridgeAsync(Handshake(licence: null), cancellationToken),
            .. await DoctorChecks.BridgeAsync(new FakeBridgeClient { Status = new BridgeStatus([new BridgeEndpoint("p", 7)], null, BridgeErrors.Incompatible(9)) }, cancellationToken),
            DoctorChecks.UserPathLength(316),
            DoctorChecks.UserPathLength(4604),
        ];

        Assert.All(checks, c => Assert.Contains(c.Id, _ids));
        Assert.Equal(_ids.Order(StringComparer.Ordinal), checks.Select(c => c.Id).Distinct().Order(StringComparer.Ordinal));
        // The branches that need a prepared data directory really ran.
        Assert.Contains(checks, c => c is { Id: DoctorCheckIds.AuditDirectory, Status: DoctorStatus.Failure });
        Assert.Contains(checks, c => c is { Id: DoctorCheckIds.Config, Status: DoctorStatus.Failure });
        // In file order: this process's instance, the project, the unreadable one. Only a process is a subject.
        Assert.Equal(
            [Environment.ProcessId.ToString(CultureInfo.InvariantCulture), null, null],
            checks.Where(c => c is { Id: DoctorCheckIds.Suspension, Status: DoctorStatus.Warning }).Select(c => c.Subject));
    }

    [Fact]
    public void EveryId_IsDocumentedInArchitecture52()
    {
        string architecture = File.ReadAllText(Path.Combine(RepositoryRoot(), "docs", "architecture", "architecture.md"));
        int from = architecture.IndexOf("### 5.2", StringComparison.Ordinal);
        int to = architecture.IndexOf("### 5.3", from, StringComparison.Ordinal);
        Assert.True(from >= 0 && to > from, "architecture.md has no section 5.2.");

        Assert.All(_ids, id => Assert.Contains($"`{id}`", architecture[from..to], StringComparison.Ordinal));
    }

    private static IEnumerable<DoctorCheck> DataChecks(HostDataLocations locations) =>
    [
        DoctorChecks.DataDirectory(locations),
        DoctorChecks.Configuration(locations),
        DoctorChecks.AuditDirectory(locations),
        DoctorChecks.Exports(locations),
        DoctorChecks.Backups(locations),
        .. DoctorChecks.Suspensions(locations),
    ];

    private HostDataLocations Missing() => new(Path.Combine(_root, "missing"));

    /// <summary>Everything in place, an invalid config.json, and suspensions of a project, of this process and one unreadable.</summary>
    private HostDataLocations Populated()
    {
        var locations = new HostDataLocations(Path.Combine(_root, "populated"));
        Directory.CreateDirectory(locations.Audit);
        Directory.CreateDirectory(locations.Exports);
        Directory.CreateDirectory(locations.Backups);
        File.WriteAllText(locations.ConfigFile, """{"writesOutsideScratch":"always"}""");
        SuspensionStore.For(locations).Suspend(
            Path.Combine(_root, "Scratch", "P.elk"), Environment.ProcessId, SuspensionStore.StartOf(Environment.ProcessId)!.Value,
            "eplan_page_create", "r1", DateTimeOffset.UnixEpoch, SuspensionReason.OutcomeUnknown, []);
        File.WriteAllText(Path.Combine(locations.State, "suspended", "unreadable.json"), "{");
        return locations;
    }

    /// <summary>A directory where the audit lock file belongs, so the log cannot be opened.</summary>
    private HostDataLocations UnwritableAudit()
    {
        var locations = new HostDataLocations(Path.Combine(_root, "unwritable"));
        Directory.CreateDirectory(Path.Combine(locations.Audit, ".lock"));
        return locations;
    }

    private static FakeBridgeClient Handshake(bool? licence) => new()
    {
        Status = new BridgeStatus(
            [new BridgeEndpoint("p", 7)],
            new HandshakeResult { ProtocolVersion = 1, BridgeVersion = "0.1.0", Eplan = new EplanInstanceInfo { ProcessId = 7, Version = "2026.0.3", ApiExtensionLicence = licence } },
            null),
    };

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "EplanMcp.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("EplanMcp.slnx not found above " + AppContext.BaseDirectory);
    }
}

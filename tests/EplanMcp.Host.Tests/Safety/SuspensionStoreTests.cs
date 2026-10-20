using EplanMcp.Host.Doctor;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tools;

namespace EplanMcp.Host.Tests.Safety;

/// <summary>Write suspensions on disk (ADR-0010 section 3).</summary>
[Trait("Size", "Medium")]
public sealed class SuspensionStoreTests : IDisposable
{
    private readonly HostDataLocations _locations = new(Directory.CreateTempSubdirectory("eplanmcp-state-").FullName);
    private readonly string _project;
    private readonly int _eplan = Environment.ProcessId;
    private readonly DateTimeOffset _started;

    public SuspensionStoreTests()
    {
        _project = Path.Combine(Directory.CreateDirectory(Path.Combine(_locations.Root, "Projects")).FullName, "Line1");
        _started = SuspensionStore.StartOf(_eplan)!.Value; // this test process stands in for a running EPLAN
    }

    public void Dispose() => Directory.Delete(_locations.Root, recursive: true);

    private SuspensionStore Store => SuspensionStore.For(_locations);

    [Fact]
    public void Suspend_BlocksTheProjectAndTheInstance_AcrossRestarts()
    {
        Suspend();

        // A new store stands for a Host started later.
        IReadOnlyList<Suspension> blocking = Store.Blocking(_project, _eplan, _started);

        Assert.Equal(2, blocking.Count);
        Assert.Contains(blocking, s => s.Project is not null && s.Tool == "eplan_page_create" && s.RequestId == "r1");
        Assert.Contains(blocking, s => s.ProcessId == _eplan && s.ProcessStart == _started);
    }

    [Fact]
    public void Blocking_SeesThroughPathVariants()
    {
        Suspend();

        string variant = Path.Combine(Path.GetDirectoryName(_project)!.ToUpperInvariant(), "..", "Projects", "line1");

        Assert.Contains(Store.Blocking(variant, _eplan + 1, _started), s => s.Project is not null);
    }

    [Fact]
    public void Blocking_AnotherProjectInAnotherInstance_IsFree()
    {
        Suspend();

        Assert.Empty(Store.Blocking(_project + "2", _eplan + 1, _started));
        Assert.Single(Store.Blocking(_project + "2", _eplan, _started)); // same instance: still blocked
    }

    [Fact]
    public void Suspend_KeepsTheFirstChangesOnly()
    {
        Store.Suspend(_project, _eplan, _started, "eplan_page_create", "r1", DateTimeOffset.UtcNow, SuspensionReason.OutcomeUnknown, Enumerable.Range(1, 30).Select(i => "Create page /" + i));

        Assert.All(Store.Blocking(_project, _eplan, _started), s => Assert.Equal(SuspensionStore.MaxChanges, s.Changes.Count));
    }

    [Fact]
    public void All_ForgetsTheSuspensionOfAnInstanceThatExited()
    {
        Store.Suspend(_project, _eplan, _started.AddMinutes(-5), "eplan_page_create", "r1", DateTimeOffset.UtcNow, SuspensionReason.OutcomeUnknown, []);

        Suspension only = Assert.Single(Store.All());

        Assert.NotNull(only.Project);
        Assert.Single(Directory.EnumerateFiles(Path.Combine(_locations.State, "suspended")));
    }

    [Fact]
    public void Release_LiftsBoth()
    {
        Suspend();

        Store.Release(_project, _eplan, _started);

        Assert.Empty(Store.Blocking(_project, _eplan, _started));
        Assert.Empty(Store.All());
    }

    [Fact]
    public void ADamagedRecord_StillSuspends()
    {
        Suspend();
        foreach (string file in Directory.EnumerateFiles(Path.Combine(_locations.State, "suspended")))
        {
            File.WriteAllText(file, "{ not json");
        }

        Assert.Equal(2, Store.Blocking(_project, _eplan, _started).Count);
    }

    [Fact]
    public void Doctor_AndSessionStatus_ShowWhatIsSuspended()
    {
        Assert.Equal(DoctorStatus.Ok, Assert.Single(DoctorChecks.Suspensions(_locations)).Status);
        Assert.Null(SessionTools.SuspensionHint([]));

        Suspend();
        IReadOnlyList<Suspension> all = Store.All();

        Assert.All(DoctorChecks.Suspensions(_locations), c => Assert.Equal(DoctorStatus.Warning, c.Status));
        string hint = SessionTools.SuspensionHint(all)!;
        Assert.Contains("Writes are suspended", hint, StringComparison.Ordinal);
        Assert.Contains("eplan_page_create", hint, StringComparison.Ordinal);
        Assert.Contains("the next write asks them to confirm", hint, StringComparison.Ordinal);
    }

    private void Suspend() =>
        Store.Suspend(_project, _eplan, _started, "eplan_page_create", "r1", DateTimeOffset.UtcNow, SuspensionReason.OutcomeUnknown, ["Create page =A1&EFS1/5"]);
}

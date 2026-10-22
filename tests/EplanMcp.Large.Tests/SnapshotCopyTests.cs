using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;

namespace EplanMcp.Large.Tests;

/// <summary>
/// project.snapshot with <c>openReadOnly</c> on copies of the fixture this test makes itself below the scratch root
/// (docs/design/2026-10-21-m6-bridge-snapshot.md, 4.1, 7; README.md): every opening gets another database number (A34),
/// which the normalisation removes, so two openings and the open fixture give one hash; the copy is closed again and its
/// files stay as they were. A copy without User.eod is refused before EPLAN is asked to open it (A35).
/// </summary>
[Trait("Size", "Large")]
public sealed class SnapshotCopyTests(LiveEplan eplan)
{
    [Fact]
    public async Task AClosedCopy_OpenedReadOnlyTwice_HasTheFixturesHash_AndIsClosedAgain()
    {
        string copy = Copy();
        try
        {
            Dictionary<string, string> files = LiveSnapshots.DesignFiles(copy);
            await using BridgeClient bridge = await LiveSnapshots.BridgeAsync(eplan.ProcessId);
            var args = new ProjectSnapshotArgs { Project = copy + ".elk", OpenReadOnly = true };

            LiveSnapshots.Snapshot first = await LiveSnapshots.TakeAsync(bridge, args);
            LiveSnapshots.Snapshot second = await LiveSnapshots.TakeAsync(bridge, args);
            LiveSnapshots.Snapshot fixture = await LiveSnapshots.TakeAsync(bridge, new ProjectSnapshotArgs { Project = eplan.ProjectPath });

            TestContext.Current.TestOutputHelper?.WriteLine("copy: " + first.Describe() + "; fixture: " + fixture.Describe());
            Assert.Equal(copy, first.Chunks[0].ProjectPath, ignoreCase: true);
            Assert.Equal(first.Hash, second.Hash);
            Assert.Equal(fixture.Hash, first.Hash); // S28: file copies keep every ObjectIdentifier
            Assert.Equal(fixture.Chunks[0].ProjectGuid, first.Chunks[0].ProjectGuid); // A34
            Assert.False(await IsOpenAsync(copy), "The copy is still open in EPLAN after the snapshot.");
            Assert.Equal(files, LiveSnapshots.DesignFiles(copy));
        }
        finally
        {
            Delete(copy);
        }
    }

    [Fact]
    public async Task ACopyWithoutUserEod_IsRefused_BeforeEplanOpensIt()
    {
        string copy = Copy();
        try
        {
            File.Delete(Path.Combine(copy + ".edb", "User.eod"));
            await using BridgeClient bridge = await LiveSnapshots.BridgeAsync(eplan.ProcessId);

            BridgeCallException error = await Assert.ThrowsAsync<BridgeCallException>(() => LiveSnapshots.TakeAsync(bridge, new ProjectSnapshotArgs { Project = copy + ".elk", OpenReadOnly = true }));

            Assert.Equal((ErrorReason.InvalidArgument, "User.eod"), (error.Error.Reason, error.Error.Metadata!["missing"]));
            Assert.False(await IsOpenAsync(copy));
            Assert.True((await eplan.CallAsync("eplan_session_status")).GetProperty("connected").GetBoolean(), "EPLAN no longer answers: look for a dialog.");
        }
        finally
        {
            Delete(copy);
        }
    }

    [Fact]
    public async Task Compatibility_OfTheFixtureAndAClosedCopy_IsCompatible_WithoutOpeningTheCopy()
    {
        string copy = Copy();
        try
        {
            await using BridgeClient bridge = await LiveSnapshots.BridgeAsync(eplan.ProcessId);

            ProjectCompatibilityData fixture = await CompatibilityAsync(bridge, eplan.ProjectPath + ".elk");
            ProjectCompatibilityData closed = await CompatibilityAsync(bridge, copy + ".elk");

            // A31: IsCompatible and 18.0 for 2026.0.3 projects, open or not, and the check opens nothing.
            Assert.Equal(("IsCompatible", true, "18.0", true), (fixture.Status, fixture.Compatible, fixture.DatabaseVersion, fixture.Open));
            Assert.Equal(("IsCompatible", true, "18.0", false), (closed.Status, closed.Compatible, closed.DatabaseVersion, closed.Open));
            Assert.False(await IsOpenAsync(copy));
        }
        finally
        {
            Delete(copy);
        }
    }

    private static Task<ProjectCompatibilityData> CompatibilityAsync(BridgeClient bridge, string path) =>
        bridge.InvokeAsync<ProjectCompatibilityArgs, ProjectCompatibilityData>(OperationNames.ProjectCompatibility, new ProjectCompatibilityArgs { Path = path }, TestContext.Current.CancellationToken);

    private static string Copy()
    {
        string copy = Path.Combine(OfflineHost.NewFolder(LiveEplan.ScratchRoot(), "snapshots"), LiveEplan.ProjectName);
        OfflineHost.CopyFixture(LiveEplan.ScratchRoot(), copy);
        return copy;
    }

    private static void Delete(string copy)
    {
        try
        {
            Directory.Delete(Path.GetDirectoryName(copy)!, recursive: true);
        }
        catch (IOException ex)
        {
            TestContext.Current.TestOutputHelper?.WriteLine($"Could not delete {copy}: {ex.Message}");
        }
    }

    private async Task<bool> IsOpenAsync(string project) =>
        (await eplan.CallAsync("eplan_project_list")).GetProperty("projects").EnumerateArray()
            .Any(p => string.Equals(p.GetProperty("path").GetString(), project, StringComparison.OrdinalIgnoreCase));
}

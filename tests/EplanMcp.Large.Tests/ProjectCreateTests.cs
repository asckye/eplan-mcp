using System.Globalization;
using System.Security.Cryptography;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;

namespace EplanMcp.Large.Tests;

/// <summary>
/// project.create.plan and project.create.execute through the Host's BridgeClient (no tool serves them yet;
/// docs/design/2026-10-21-m6-bridge-snapshot.md, 4.8, 7; README.md). The test copies EPLAN's own basic project
/// GB_bas001.zw9 into a new folder below the scratch root and creates the project next to it: one page (S27), a
/// PROJ_GUID of the basic project's name and the time (A34), not left open (A34), compatible (A31), readable with
/// openReadOnly; the folder, once it holds the project, is refused.
/// </summary>
[Trait("Size", "Large")]
public sealed class ProjectCreateTests(LiveEplan eplan)
{
    /// <summary>A basic project to use instead of searching %PUBLIC%\EPLAN\Data for GB_bas001.zw9.</summary>
    public const string BasicProjectVariable = "EPLANMCP_LARGE_BASIC_PROJECT";

    [Fact]
    public async Task FromGbBas001_TheNewProjectHasOnePage_IsNotLeftOpen_AndItsFolderIsThenRefused()
    {
        string? basic = Environment.GetEnvironmentVariable(BasicProjectVariable) is { Length: > 0 } given ? given : Find("GB_bas001.zw9");
        Assert.SkipWhen(basic is null, $"No unique GB_bas001.zw9 below %PUBLIC%\\EPLAN\\Data: set {BasicProjectVariable} to a basic project (.zw9).");
        string folder = OfflineHost.NewFolder(LiveEplan.ScratchRoot(), "created");
        string file = Path.Combine(folder, "imports", Path.GetFileName(basic!));
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.Copy(basic!, file);
        var args = new NewProjectArgs
        {
            File = Path.GetFileName(file),
            FilePath = file,
            Sha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file))),
            Name = "LargeCreate",
            Destination = Path.Combine(folder, "project"),
        };
        await using BridgeClient bridge = await LiveSnapshots.BridgeAsync(eplan.ProcessId);

        WritePlan plan = await bridge.InvokeAsync<NewProjectArgs, WritePlan>(OperationNames.ProjectCreatePlan, args, TestContext.Current.CancellationToken);
        args.Fingerprint = plan.Fingerprint;
        WriteResult<NewProjectData> created = await bridge.InvokeWriteAsync<NewProjectArgs, NewProjectData>(
            OperationNames.ProjectCreateExecute, args, Guid.NewGuid().ToString("N"), TimeSpan.FromSeconds(120), null, TestContext.Current.CancellationToken);

        NewProjectData data = created.Data!;
        string link = Path.Combine(args.Destination, "LargeCreate.elk");
        TestContext.Current.TestOutputHelper?.WriteLine($"{basic}: {data.Seconds} s, {data.Pages} page(s), PROJ_GUID {data.ProjectGuid}");
        Assert.Equal(("create", "project", "LargeCreate"), (plan.Changes[0].Action, plan.Changes[0].Kind, plan.Changes[0].Name));
        Assert.Equal((link, (int?)1), (data.Path, data.Pages)); // S27: GB_bas001 has one page
        Assert.StartsWith(Path.GetFileNameWithoutExtension(basic) + "@", data.ProjectGuid, StringComparison.Ordinal); // A34
        Assert.True(File.Exists(link) && Directory.Exists(Path.ChangeExtension(link, ".edb")));
        Assert.False(await IsOpenAsync(link[..^4]), "A34 saw the created project closed after the call; it is open.");

        ProjectCompatibilityData compatibility = await bridge.InvokeAsync<ProjectCompatibilityArgs, ProjectCompatibilityData>(
            OperationNames.ProjectCompatibility, new ProjectCompatibilityArgs { Path = link }, TestContext.Current.CancellationToken);
        Assert.Equal(("IsCompatible", "18.0"), (compatibility.Status, compatibility.DatabaseVersion));
        LiveSnapshots.Snapshot snapshot = await LiveSnapshots.TakeAsync(bridge, new ProjectSnapshotArgs { Project = link, OpenReadOnly = true });
        Assert.Single(snapshot.Pages);
        Assert.Equal(data.ProjectGuid, snapshot.Chunks[0].ProjectGuid);
        Assert.False(await IsOpenAsync(link[..^4]));

        string[] entries = [.. Directory.EnumerateFileSystemEntries(args.Destination, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)];
        args.Fingerprint = null;
        BridgeCallException again = await Assert.ThrowsAsync<BridgeCallException>(() => bridge.InvokeAsync<NewProjectArgs, WritePlan>(OperationNames.ProjectCreatePlan, args, TestContext.Current.CancellationToken));
        Assert.Equal(ErrorReason.AlreadyExists, again.Error.Reason);
        Assert.Equal(entries, Directory.EnumerateFileSystemEntries(args.Destination, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal));
        Delete(folder);
    }

    /// <summary>The one file named <paramref name="name"/> below %PUBLIC%\EPLAN\Data, or null.</summary>
    internal static string? Find(string name)
    {
        string data = Path.Combine(Environment.GetEnvironmentVariable("PUBLIC") ?? @"C:\Users\Public", "EPLAN", "Data");
        string[] found = Directory.Exists(data) ? Directory.GetFiles(data, name, SearchOption.AllDirectories) : [];
        return found.Length == 1 ? found[0] : null;
    }

    internal static void Delete(string folder)
    {
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (IOException ex)
        {
            TestContext.Current.TestOutputHelper?.WriteLine(string.Format(CultureInfo.InvariantCulture, "Could not delete {0}: {1}", folder, ex.Message));
        }
    }

    private async Task<bool> IsOpenAsync(string project) =>
        (await eplan.CallAsync("eplan_project_list")).GetProperty("projects").EnumerateArray()
            .Any(p => string.Equals(p.GetProperty("path").GetString(), project, StringComparison.OrdinalIgnoreCase));
}

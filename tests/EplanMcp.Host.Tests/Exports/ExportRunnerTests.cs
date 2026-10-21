using System.Diagnostics;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Exports;
using EplanMcp.Host.Tests.Safety;
using EplanMcp.Host.Tests.Tools;
using Microsoft.Extensions.Logging.Abstractions;

namespace EplanMcp.Host.Tests.Exports;

/// <summary>
/// The steps every export tool shares (docs/tools/eplan_export_pdf.md, sections 4 and 5; SEC-20) on a fake Bridge that
/// writes into the folder the Host created, a real one in a temporary data directory: what the Bridge gets, and that
/// the folder goes again after every failure and cancel.
/// </summary>
[Trait("Size", "Medium")]
public sealed class ExportRunnerTests : IDisposable
{
    private const string Plan = "export.x.plan";
    private const string Run = "export.x.run";

    private readonly HostDataLocations _locations = new(Directory.CreateTempSubdirectory("eplanmcp-export-runner-").FullName);
    private readonly ManualTime _time = new(new DateTimeOffset(2026, 10, 21, 8, 30, 0, TimeSpan.Zero));
    private readonly FakeBridgeClient _bridge = new();
    private readonly RecordedProgress _progress = new();
    private readonly ExportRunner _runner;
    private readonly List<string> _junctions = [];

    public ExportRunnerTests()
    {
        _runner = new ExportRunner(_bridge, new ExportFolders(_locations, _time), _time, NullLogger<ExportRunner>.Instance);
        _bridge.Handlers[Plan] = _ => new ExportPlanData { Project = "Line1", PageCount = 2, Scheme = "S" };
        _bridge.Handlers[Run] = args => Write((ExportArgs)args!, @"sub\b.dxf", 3, "a.dxf", 5);
    }

    private string ProjectFolder => Path.Combine(_locations.Exports, "Line1");

    public void Dispose()
    {
        _junctions.Where(Directory.Exists).ToList().ForEach(link => Directory.Delete(link));
        Directory.Delete(_locations.Root, recursive: true);
    }

    [Fact]
    public async Task AnExport_RunsInANewFolder_AndListsItsFilesByPath()
    {
        var args = new ExportArgs { Project = "Line1", Pages = ["=A/1"], Scheme = "S", Color = "grey" };

        ExportOutcome outcome = await RunAsync(new ExportJob(Plan, Run, args, "Exporting") { FileName = project => project + ".pdf" });

        Assert.StartsWith(Path.Combine(ProjectFolder, "20261021T083000Z-"), outcome.Directory, StringComparison.Ordinal);
        Assert.Equal([new ExportFile("a.dxf", 5), new ExportFile(@"sub\b.dxf", 3)], outcome.Files);
        var run = (ExportArgs)_bridge.Calls[1].Args!;
        Assert.Equal((outcome.Directory, "Line1.pdf", "Line1", "=A/1", "S", "grey"), (run.Directory, run.FileName, run.Project, run.Pages![0], run.Scheme, run.Color));
        Assert.Null(((ExportArgs)_bridge.Calls[0].Args!).Directory);
        Assert.Equal([ExportRunner.Timeout], _bridge.Timeouts);
    }

    [Fact]
    public async Task APlanError_CreatesNoFolder()
    {
        _bridge.Handlers[Plan] = _ => throw new BridgeCallException(new BridgeError { Reason = ErrorReason.PageNotFound, Message = "m" });

        Assert.Equal(ErrorReason.PageNotFound, (await Assert.ThrowsAsync<BridgeCallException>(() => RunAsync())).Error.Reason);
        Assert.False(Directory.Exists(_locations.Exports));
    }

    [Theory]
    [InlineData(ErrorReason.EplanOperationFailed, "EPLAN could not export")]
    [InlineData(ErrorReason.Timeout, "Export fewer pages per call")]
    public async Task ARunError_DeletesTheFolder(string reason, string message)
    {
        _bridge.Handlers[Run] = args =>
        {
            Write((ExportArgs)args!, "half.pdf", 10);
            throw new BridgeCallException(new BridgeError { Reason = reason, Message = "EPLAN could not export these pages." });
        };

        BridgeError error = (await Assert.ThrowsAsync<BridgeCallException>(() => RunAsync())).Error;

        Assert.Equal(reason, error.Reason);
        Assert.Contains(message, error.Message, StringComparison.Ordinal);
        Assert.Empty(Directory.GetFileSystemEntries(ProjectFolder));
    }

    [Fact]
    public async Task ACancel_DeletesTheFolder()
    {
        _bridge.Handlers[Run] = args =>
        {
            Write((ExportArgs)args!, "half.pdf", 10);
            throw new OperationCanceledException();
        };

        await Assert.ThrowsAsync<OperationCanceledException>(() => RunAsync());

        Assert.Empty(Directory.GetFileSystemEntries(ProjectFolder));
    }

    [Fact]
    public async Task AJunctionLeftInTheFolder_FailsTheExport_AndOnlyTheFolderGoes()
    {
        string outside = Directory.CreateDirectory(Path.Combine(_locations.Root, "Elsewhere")).FullName;
        File.WriteAllText(Path.Combine(outside, "keep.txt"), "x");
        _bridge.Handlers[Run] = args =>
        {
            Junction(Path.Combine(((ExportArgs)args!).Directory!, "Linked"), outside);
            return Write((ExportArgs)args!, "a.dxf", 1);
        };

        Assert.Equal(ErrorReason.EplanOperationFailed, (await Assert.ThrowsAsync<BridgeCallException>(() => RunAsync())).Error.Reason);

        Assert.Empty(Directory.GetFileSystemEntries(ProjectFolder));
        Assert.True(File.Exists(Path.Combine(outside, "keep.txt")));
    }

    [Fact]
    public async Task AnExportThatWroteNoFile_Failed()
    {
        _bridge.Handlers[Run] = args => Write((ExportArgs)args!);

        Assert.Equal(ErrorReason.EplanOperationFailed, (await Assert.ThrowsAsync<BridgeCallException>(() => RunAsync())).Error.Reason);
        Assert.Empty(Directory.GetFileSystemEntries(ProjectFolder));
    }

    [Fact]
    public async Task AFolderThatCannotBeCreated_IsInternal_WithoutTheLocalPath()
    {
        Directory.CreateDirectory(_locations.Root);
        File.WriteAllText(_locations.Exports, "a file where the export root belongs");

        BridgeError error = (await Assert.ThrowsAsync<BridgeCallException>(() => RunAsync())).Error;

        Assert.Equal(ErrorReason.Internal, error.Reason);
        Assert.Contains("eplanmcp doctor", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(_locations.Root, error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(_bridge.Calls, c => c.Operation == Run);
    }

    [Fact]
    public async Task WhileEplanExports_AHeartbeatEvery5Seconds_AndNoneAfterwards()
    {
        _bridge.Handlers[Run] = args =>
        {
            _time.Advance(TimeSpan.FromSeconds(11));
            return Write((ExportArgs)args!, "a.pdf", 1);
        };

        await RunAsync();
        _time.Advance(TimeSpan.FromSeconds(10));

        Assert.Equal([("Exporting (5 s)", 5f), ("Exporting (10 s)", 10f)], _progress.Values.Select(v => (v.Message, v.Progress)));
        Assert.All(_progress.Values, v => Assert.Null(v.Total));
    }

    [Fact]
    public async Task TheBridgesSteps_ReachTheClientCleaned_BetweenHeartbeats_AndNoneAfterwards()
    {
        IProgress<ProgressNotification>? steps = null;
        _bridge.Handlers[Run] = args =>
        {
            steps = _bridge.Progress!;
            steps.Report(new ProgressNotification { RequestId = "r", Progress = 0, Total = 2, Message = "Exporting page 1 of 2" });
            _time.Advance(TimeSpan.FromSeconds(6));
            steps.Report(new ProgressNotification { RequestId = "r", Progress = 1, Total = 2, Message = "Exporting page 2\u202E of 2\u0007" });
            _time.Advance(TimeSpan.FromSeconds(5)); // the heartbeat at 10 s is left out: a step came 4 s before
            return Write((ExportArgs)args!, "a.png", 1);
        };

        await RunAsync();
        steps!.Report(new ProgressNotification { RequestId = "r", Progress = 2, Total = 2, Message = "Exported 2 of 2 pages" });
        _time.Advance(TimeSpan.FromSeconds(10));

        Assert.Equal(
            [("Exporting page 1 of 2", 0f), ("Exporting page 1 of 2 (5 s)", 5f), ("Exporting page 2 of 2", 6f)],
            _progress.Values.Select(v => (v.Message, v.Progress)));
        Assert.All(_progress.Values, v => Assert.Null(v.Total));
    }

    private Task<ExportOutcome> RunAsync(ExportJob? job = null) =>
        _runner.RunAsync(job ?? new ExportJob(Plan, Run, new ExportArgs(), "Exporting"), _progress, TestContext.Current.CancellationToken);

    /// <summary>Writes files of the given sizes below the run's folder, as EPLAN would, and answers like the Bridge.</summary>
    private static ExportRunData Write(ExportArgs args, params object[] files)
    {
        for (int i = 0; i < files.Length; i += 2)
        {
            string path = Path.Combine(args.Directory!, (string)files[i]);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, new byte[(int)files[i + 1]]);
        }

        return new ExportRunData { Project = "Line1", PageCount = files.Length / 2, Scheme = "S" };
    }

    private void Junction(string link, string target)
    {
        // A junction needs no privilege, unlike a symbolic link; .NET has no API to create one.
        using Process mklink = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })!;
        mklink.WaitForExit();
        Assert.Equal(0, mklink.ExitCode);
        _junctions.Add(link);
    }
}

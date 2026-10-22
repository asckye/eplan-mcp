using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Large.Tests;

/// <summary>
/// eplan_revision_list (docs/tools/eplan_revision_list.md, section 7). The fixture has no revision. For a project under
/// change tracking the test makes its own: it copies the fixture into a new folder below the scratch root's
/// <c>revisions</c>, lets <c>EplanMcp.Large.RevisionHelper</c> create a revision in the copy the way S23 r3 did
/// (api-notes A28; no tool creates revisions), starts the offline host on the copy with a pipe prefix of its own and asks
/// a new eplanmcp there, so the EPLAN with the fixture is never touched. The copy is deleted after a clean stop.
/// </summary>
[Trait("Size", "Large")]
public sealed class RevisionListTests(LiveEplan eplan)
{
    private const string RevisionName = "Large-1";

    [Fact]
    public async Task TheFixture_HasNoRevision()
    {
        var clock = Stopwatch.StartNew();
        JsonElement result = await eplan.CallAsync("eplan_revision_list");
        double seconds = clock.Elapsed.TotalSeconds;

        Assert.Equal(("none", false, 0), (result.GetProperty("state").GetString(), result.GetProperty("trackingStarted").GetBoolean(), result.GetProperty("total").GetInt32()));
        Assert.EndsWith(".elk", result.GetProperty("path").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(result.GetProperty("revisions").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("parent").ValueKind);

        // The spec leaves the time of reading every page's revision marker to this test (section 5: 2 s or more needs a 'detailed').
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"{seconds:0.00} s; markedPageCount {result.GetProperty("markedPageCount").GetInt32()}, uncompletedPageCount {result.GetProperty("uncompletedPageCount").GetInt32()}");
    }

    [Fact]
    public async Task ACopyWithANewRevision_IsTracking_WithOneRecord()
    {
        string? variantBin = Environment.GetEnvironmentVariable(OfflineHost.VariantBinVariable);
        Assert.SkipWhen(string.IsNullOrEmpty(variantBin), $"Set {OfflineHost.VariantBinVariable} to the EPLAN variant's Bin (README.md, 离线宿主): the revision is made in a copy opened without EPLAN's user interface.");
        string folder = OfflineHost.NewFolder(LiveEplan.ScratchRoot(), "revisions");
        string copy = Path.Combine(folder, LiveEplan.ProjectName);
        OfflineHost.CopyFixture(LiveEplan.ScratchRoot(), copy);
        DateTime before = DateTime.UtcNow;
        string linkFile = await CreateRevisionAsync(variantBin!, copy + ".elk");

        OfflineHost host = await OfflineHost.StartOnAsync(variantBin!, folder, linkFile, TestContext.Current.CancellationToken);
        JsonElement result;
        try
        {
            await using McpClient client = await McpClient.CreateAsync(
                new StdioClientTransport(new StdioClientTransportOptions
                {
                    Name = "eplanmcp",
                    Command = Path.Combine(AppContext.BaseDirectory, "eplanmcp.exe"),
                    Arguments = ["serve"],
                    EnvironmentVariables = new Dictionary<string, string?> { ["EPLANMCP_PIPE_PREFIX"] = host.PipePrefix },
                }),
                cancellationToken: TestContext.Current.CancellationToken);
            CallToolResult call = await client.CallToolAsync("eplan_revision_list", new Dictionary<string, object?>(), cancellationToken: TestContext.Current.CancellationToken);
            Assert.True(call.IsError != true, "eplan_revision_list failed: " + string.Join("\n", call.Content.OfType<TextContentBlock>().Select(b => b.Text)));
            result = call.StructuredContent!.Value;
        }
        finally
        {
            await host.StopAsync();
        }

        // S23 r3: CreateRevision turns .elk into .ell and writes record 1 with the name, the user and the date.
        Assert.EndsWith(".ell", linkFile, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(("tracking", 1), (result.GetProperty("state").GetString(), result.GetProperty("total").GetInt32()));
        Assert.Equal(linkFile, result.GetProperty("path").GetString(), StringComparer.OrdinalIgnoreCase);
        JsonElement record = Assert.Single(result.GetProperty("revisions").EnumerateArray());
        Assert.Equal((1, RevisionName), (record.GetProperty("index").GetInt32(), record.GetProperty("name").GetString()));
        Assert.False(string.IsNullOrEmpty(record.GetProperty("user").GetString()));
        DateTime date = DateTime.Parse(record.GetProperty("date").GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal);
        Assert.InRange(date, before.AddMinutes(-5), DateTime.UtcNow.AddMinutes(5));
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"trackingStarted {result.GetProperty("trackingStarted").GetBoolean()}; description '{record.GetProperty("description").GetString()}'; user {record.GetProperty("user").GetString()}; date {date:O}; uncompletedPageCount {result.GetProperty("uncompletedPageCount").GetInt32()}");
    }

    /// <summary>Runs the helper on the copy; returns the copy's link file after the revision, as the helper prints it.</summary>
    private static async Task<string> CreateRevisionAsync(string variantBin, string projectFile)
    {
        string exe = OfflineHost.RepositoryFile("tests", "EplanMcp.Large.RevisionHelper", "EplanMcp.Large.RevisionHelper.exe");
        Assert.True(File.Exists(exe), $"The helper is not built: {exe}. Build tests\\EplanMcp.Large.RevisionHelper in the configuration of these tests.");
        var start = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in new[] { "--variant-bin", variantBin, "--project", projectFile, "--name", RevisionName })
        {
            start.ArgumentList.Add(argument);
        }

        using Process helper = Process.Start(start)!;
        Task<string> stdout = helper.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        Task<string> stderr = helper.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        await helper.WaitForExitAsync(timeout.Token);
        Assert.True(helper.ExitCode == 0, $"The helper exited with {helper.ExitCode}; the copy is kept in {Path.GetDirectoryName(projectFile)}. stderr: {await stderr}");
        return (await stdout).Trim();
    }
}

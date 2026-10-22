using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using EplanMcp.Host.Bridge;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Large.Tests;

/// <summary>
/// eplan_revision_list (docs/tools/eplan_revision_list.md, section 7). The fixture has no revision. For a project under
/// change tracking the test makes its own: it copies the fixture into a new folder below the scratch root's
/// <c>revisions</c>, lets <c>EplanMcp.Large.RevisionHelper</c> create a revision in the copy the way S23 r3 did
/// (api-notes A28; no tool creates revisions), starts the offline host on the copy with a pipe prefix of its own and asks
/// a new eplanmcp there, so the EPLAN with the fixture is never touched. The prefix goes only to the processes the test
/// starts; the test process's environment, which the other tests' Hosts read, stays as it was (the test checks that).
/// Like every test here it runs alone: the assembly runs one test at a time. The copy is deleted after a clean stop.
/// </summary>
[Trait("Size", "Large")]
public sealed class RevisionListTests(LiveEplan eplan)
{
    /// <summary>Optional: the Bin of the EPLAN variant the revision helper and this test's offline host run with.</summary>
    public const string RevisionVariantBinVariable = "EPLANMCP_REVISION_VARIANT_BIN";

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
        // The run's own pipe prefix (set by LiveEplan only in the offline mode) must be the same afterwards: this test
        // gives its prefix to the processes it starts, never to the test process, so the other tests keep their EPLAN.
        string? runPrefix = Environment.GetEnvironmentVariable(PipeBridgeDiscovery.PrefixVariable);
        string? variantBin = await VariantBinAsync();
        Assert.SkipWhen(variantBin is null, $"No EPLAN variant's Bin found: set {RevisionVariantBinVariable} to it (README.md, RevisionListTests). The revision is made in a copy opened without EPLAN's user interface.");
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
                    EnvironmentVariables = new Dictionary<string, string?> { [PipeBridgeDiscovery.PrefixVariable] = host.PipePrefix },
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

        Assert.Equal(runPrefix, Environment.GetEnvironmentVariable(PipeBridgeDiscovery.PrefixVariable));

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

    /// <summary>
    /// The Bin of the EPLAN variant to run without a user interface: <see cref="RevisionVariantBinVariable"/>, or else the
    /// one of the EPLAN the run talks to, <c>&lt;Program Files&gt;\EPLAN\&lt;variant&gt;\&lt;version&gt;\Bin</c> (api-notes A23);
    /// null when it has no W3u.exe. Not <see cref="OfflineHost.VariantBinVariable"/>: that one moves the whole run to an
    /// offline host, where the write tests find no write copy.
    /// </summary>
    private async Task<string?> VariantBinAsync()
    {
        if (Environment.GetEnvironmentVariable(RevisionVariantBinVariable) is { Length: > 0 } given)
        {
            return given;
        }

        JsonElement session = (await eplan.CallAsync("eplan_session_status")).GetProperty("eplan");
        string[] version = (session.GetProperty("version").GetString() ?? string.Empty).Split('.');
        string? variant = session.GetProperty("variant").GetString();
        if (variant is null || version.Length < 3)
        {
            return null;
        }

        string bin = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "EPLAN", variant, string.Join(".", version.Take(3)), "Bin");
        return File.Exists(Path.Combine(bin, "W3u.exe")) ? bin : null;
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

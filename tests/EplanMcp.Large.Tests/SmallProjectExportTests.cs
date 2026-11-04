using System.Diagnostics;
using System.Text.Json;

namespace EplanMcp.Large.Tests;

/// <summary>Exports native objects from the already open read-only sample fixture without opening a write project.</summary>
[Trait("Size", "Large")]
[Trait("Spike", "SmallProjectExport")]
public sealed class SmallProjectExportTests(LiveEplan eplan)
{
    private static readonly string[] _pages =
    [
        "&AAA1/1", "&AAB1/1",
        "=GAA+A1&EFS1/1", "=GAA+A1&EFS1/2",
        "=K1+A2&EFS1/1", "=K1+A2&EFS1/2",
        "=K1+A2&EFA1/1", "=K1+A2&EFA1/2", "=K1+A2&EFA1/3", "=K1+A2&EFA1/4", "=K1+A2&EFA1/5",
        "=K1+A2&EFA1/6", "=K1+A2&EFA1/7",
        "+A1&EMA2/2", "+A1&EPA1/1", "+A1&EMB1/1",
    ];

    [Fact]
    public async Task SelectedPages_ExportNativeObjects_ReportsReadGaps()
    {
        string project = eplan.ProjectPath;
        string expected = Path.Combine(LiveEplan.ScratchRoot(), "fixtures", LiveEplan.ProjectName);
        Assert.True(string.Equals(Path.GetFullPath(expected), Path.GetFullPath(project), StringComparison.OrdinalIgnoreCase),
            "SmallProjectExport requires the already open scratch/fixtures/EPLAN_Sample_Project read fixture.");
        string output = Directory.CreateDirectory(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "EplanMcp", "scratch", "inputs", "small-project", "source")).FullName;
        string requests = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "eplanmcp-spikes")).FullName;
        string request = Path.Combine(requests, "small-project-export-" + Guid.NewGuid().ToString("N") + ".json");
        string helper = OfflineHost.RepositoryFile("tests", "EplanMcp.Large.ConnectionHelper", "EplanMcp.Large.ConnectionHelper.dll");
        Assert.True(File.Exists(helper), "Build tests/EplanMcp.Large.ConnectionHelper before running SmallProjectExport.");
        await File.WriteAllTextAsync(request, JsonSerializer.Serialize(new { project, pages = _pages, output }),
            TestContext.Current.CancellationToken);

        await PlanStepLiveSession.ActionAsync("EplApiModuleAction /register:\"" + helper + "\" /Quiet");
        try
        {
            await ExportActionAsync(request);
            string response = await File.ReadAllTextAsync(request + ".result.json", TestContext.Current.CancellationToken);
            using JsonDocument result = JsonDocument.Parse(response);
            Assert.True(result.RootElement.GetProperty("success").GetBoolean(), response);
            JsonElement[] exported = [.. result.RootElement.GetProperty("pages").EnumerateArray()];
            foreach (string name in _pages)
            {
                JsonElement row = exported.FirstOrDefault(page => Text(page, "name") == name);
                string? file = row.ValueKind == JsonValueKind.Object ? Text(row, "file") : null;
                if (string.IsNullOrEmpty(file))
                {
                    Print(name + ": objects=0; types={}; unhandledTypes={}; readErrors={}; WARNING: "
                        + (row.ValueKind == JsonValueKind.Object ? row.GetRawText() : "No page export was returned."));
                    continue;
                }

                // A missing or malformed output file is an infrastructure failure; member-read gaps remain warnings.
                using JsonDocument page = JsonDocument.Parse(await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken));
                Summarize(name, page.RootElement);
            }
            Print("SmallProjectExport output: " + output);
        }
        finally
        {
            await PlanStepLiveSession.ActionAsync("EplApiModuleAction /unregister:\"EplanMcp.Large.ConnectionHelper\" /Quiet");
        }
    }

    private static void Summarize(string name, JsonElement page)
    {
        JsonElement[] placements = page.TryGetProperty("placements", out JsonElement items) && items.ValueKind == JsonValueKind.Array
            ? [.. items.EnumerateArray()] : [];
        var types = placements.GroupBy(item => Text(item, "type") ?? "<unreadable>")
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var errors = new SortedDictionary<string, int>(StringComparer.Ordinal);
        CountErrors(page, errors);
        string unhandled = page.TryGetProperty("unhandledTypes", out JsonElement value) ? value.GetRawText() : "{}";
        Print(name + ": objects=" + placements.Length + "; types=" + JsonSerializer.Serialize(types)
            + "; unhandledTypes=" + unhandled + "; readErrors=" + JsonSerializer.Serialize(errors)
            + (errors.Count > 0 ? "; WARNING: native reads were incomplete; see exported member errors." : ""));
    }

    private static void CountErrors(JsonElement node, SortedDictionary<string, int> counts)
    {
        if (node.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in node.EnumerateObject())
            {
                if (property.Name == "errors" && property.Value.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement error in property.Value.EnumerateArray())
                    {
                        string member = Text(error, "member") ?? "<unknown>";
                        counts[member] = counts.GetValueOrDefault(member) + 1;
                    }
                }
                else
                { CountErrors(property.Value, counts); }
            }
        }
        else if (node.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in node.EnumerateArray())
            { CountErrors(item, counts); }
        }
    }

    private static async Task ExportActionAsync(string request)
    {
        // The existing action helper has a 30-second deadline; this single read covers all 16 pages.
        string remote = Environment.GetEnvironmentVariable("EPLANMCP_EPLREMOTE")
            ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
                ".local", "tools", "eplremote", "bin", "Release", "net481", "eplremote.exe"));
        var start = new ProcessStartInfo(remote)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add("--host");
        start.ArgumentList.Add(Environment.GetEnvironmentVariable("EPLREMOTE_HOST")
            ?? throw new InvalidOperationException("Set EPLREMOTE_HOST to this machine's IPv4 address (S35/A41)."));
        start.ArgumentList.Add("EplanMcpLargePageExport /REQUEST:\"" + request + "\"");
        start.Environment["EPLREMOTE_PORT"] = Environment.GetEnvironmentVariable("EPLREMOTE_PORT") ?? "49152";
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("Could not start eplremote.");
        Task<string> stdout = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        Task<string> stderr = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(30));
        await process.WaitForExitAsync(timeout.Token);
        Assert.True(process.ExitCode == 0, $"Page export action failed ({process.ExitCode}): {await stdout} {await stderr}");
    }

    private static string? Text(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object
        && value.TryGetProperty(name, out JsonElement field) && field.ValueKind == JsonValueKind.String ? field.GetString() : null;

    private static void Print(string message) => TestContext.Current.TestOutputHelper!.WriteLine(message);
}

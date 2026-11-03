using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace EplanMcp.Large.Tests;

/// <summary>Records T.4 report settings and native generation results; no product behavior is inferred.</summary>
[Trait("Size", "Large")]
[Trait("Spike", "T.4")]
public sealed class ReportBlockSpikeTests(LiveEplan eplan, WriteHost host) : IClassFixture<WriteHost>
{
    private static readonly string[] _samplePages = ["&AAA1/1", "&AAB1/1", "=K1+A2&EFA1/6", "=K1+A2&EFA1/7", "+A1&EMA2/2", "+A1&EPA1/1", "+A1&EMB1/1"];
    private static readonly JsonSerializerOptions _indented = new() { WriteIndented = true };
    private readonly JsonArray _stages = [];
    private readonly JsonArray _warnings = [];

    [Fact]
    public async Task SampleReportBlocks_ReadSettingsAndGenerateDirectoryAndTerminalDiagramInScratch()
    {
        string run = Guid.NewGuid().ToString("N");
        string evidenceFile = Path.Combine(Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "eplanmcp-spikes")).FullName, "report-blocks-" + run + ".json");
        var evidence = new JsonObject { ["run"] = run, ["stages"] = _stages, ["warnings"] = _warnings };
        bool registered = false;
        try
        {
            string sample = eplan.ProjectPath;
            Assert.Equal(LiveEplan.ProjectName, Path.GetFileName(sample));
            JsonElement listed = await eplan.CallAsync("eplan_page_list", new() { ["project"] = sample, ["limit"] = 200 });
            string first = listed.GetProperty("pages")[0].GetProperty("name").GetString()!;
            await eplan.CallAsync("eplan_page_open", new() { ["project"] = sample, ["page"] = first });
            string helper = OfflineHost.RepositoryFile("tests", "EplanMcp.Large.ConnectionHelper", "EplanMcp.Large.ConnectionHelper.dll");
            Assert.True(File.Exists(helper), "Build tests/EplanMcp.Large.ConnectionHelper before running T.4.");
            await PlanStepLiveSession.ActionAsync("EplApiModuleAction /register:\"" + helper + "\" /Quiet");
            registered = true;

            Dictionary<string, (long Size, DateTime Written)>? before = TryProjectFiles(sample);
            JsonElement read = await ProbeAsync(new { mode = "read", project = sample, pages = _samplePages }, "read");
            Dictionary<string, (long Size, DateTime Written)>? after = TryProjectFiles(sample);
            evidence["fixtureBefore"] = FileEvidence(before);
            evidence["fixtureAfter"] = FileEvidence(after);
            if (before is not null && after is not null)
            {
                string[] changed = [.. after.Where(f => !before.TryGetValue(f.Key, out var old) || old != f.Value).Select(f => f.Key).Concat(before.Keys.Except(after.Keys))];
                evidence["fixtureChangedFiles"] = JsonSerializer.SerializeToNode(changed);
                Assert.Empty(changed);
            }

            JsonElement[] rows = [.. read.GetProperty("pages").EnumerateArray()];
            Assert.Equal(_samplePages, rows.Select(r => r.GetProperty("requestedName").GetString()));
            foreach (JsonElement row in rows)
            {
                string name = row.GetProperty("requestedName").GetString()!;
                if (!TrySettings(row, out _))
                { Warn("Report type/form unavailable for " + name + "; retain the field-level observation."); }
            }

            await using PlanStepLiveSession session = await PlanStepLiveSession.StartAsync(eplan);
            // Keep generated pages as evidence, including native overwrites of reports for the selected device.
            session.PauseCleanup();
            string[] names = [.. (await session.SnapshotAsync()).Pages.Select(p => p.Name)];
            foreach ((string source, string prefix, bool terminal) in new[] { ("&AAB1/1", "&AAB1/", false), ("+A1&EMA2/2", "+A1&EMA2/", true) })
            {
                JsonElement row = rows.Single(r => r.GetProperty("requestedName").GetString() == source);
                if (!TrySettings(row, out JsonElement settings))
                {
                    _stages.Add(new JsonObject { ["stage"] = "create", ["source"] = source, ["skipped"] = "Source type or form is unavailable; see read warnings." });
                    continue;
                }
                string page = NextReportPage(names, prefix);
                string? original = await TryStateAsync(session);
                JsonElement created = await ProbeAsync(new { mode = "create", project = session.Project, firstPageName = page, settings, limitToTerminalStrip = terminal }, "create-" + (terminal ? "terminal" : "directory"));
                string? current = await TryStateAsync(session);
                JsonElement creation = created.GetProperty("creation");
                string state = creation.GetProperty("state").GetString()!;
                if (state == "committed")
                {
                    JsonElement[] pages = [.. created.GetProperty("pages").EnumerateArray()];
                    if (pages.Length == 0)
                    { Warn("CreateReport committed but returned no report pages for " + page + "."); }
                    foreach (JsonElement generated in pages)
                    {
                        if (TrySettings(generated, out JsonElement actual))
                        {
                            Assert.Equal(settings.GetProperty("type").GetInt32(), actual.GetProperty("type").GetInt32());
                            Assert.Equal(settings.GetProperty("formName").GetString(), actual.GetProperty("formName").GetString());
                        }
                        else
                        { Warn("Generated report settings are unreadable; see field-level observations for " + page + "."); }
                    }
                }
                else
                {
                    Assert.True(creation.TryGetProperty("exception", out JsonElement error) && error.GetProperty("details").GetString() is { Length: > 0 }, "A failed generation must retain its complete exception.");
                    Assert.Equal("rolledBack", state);
                    if (original is not null && current is not null)
                    {
                        _stages.Add(new JsonObject { ["stage"] = "rollback-verification", ["firstPageName"] = page, ["before"] = original, ["after"] = current, ["rollbackUnchanged"] = original == current });
                        Assert.Equal(original, current);
                    }
                }
            }
        }
        catch (Exception error)
        {
            evidence["error"] = error.ToString();
            throw;
        }
        finally
        {
            if (registered)
            {
                try
                { await PlanStepLiveSession.ActionAsync("EplApiModuleAction /unregister:\"EplanMcp.Large.ConnectionHelper\" /Quiet"); }
                catch (Exception error) { Warn("Helper unregister failed: " + error); }
            }
            // Preserve evidence even when the test cancellation token has been cancelled.
            await File.WriteAllTextAsync(evidenceFile, evidence.ToJsonString(_indented), CancellationToken.None);
            TestContext.Current.TestOutputHelper!.WriteLine("T.4 report-block evidence: " + evidenceFile);
            TestContext.Current.TestOutputHelper!.WriteLine("T.4 summary: " + _stages.Count.ToString(CultureInfo.InvariantCulture) + " stages; " + _warnings.Count.ToString(CultureInfo.InvariantCulture) + " warnings.");
        }
    }

    private async Task<JsonElement> ProbeAsync(object request, string stage)
    {
        var record = new JsonObject { ["stage"] = stage, ["request"] = JsonSerializer.SerializeToNode(request) };
        _stages.Add(record);
        string file = Path.Combine(host.Home, "report-blocks-" + Guid.NewGuid().ToString("N") + ".json");
        await File.WriteAllTextAsync(file, JsonSerializer.Serialize(request), TestContext.Current.CancellationToken);
        await PlanStepLiveSession.ActionAsync("EplanMcpLargeReportBlockSpike /REQUEST:\"" + file + "\"");
        Assert.True(File.Exists(file + ".result.json"), "The T.4 helper did not write its response.");
        string text = await File.ReadAllTextAsync(file + ".result.json", TestContext.Current.CancellationToken);
        record["response"] = JsonNode.Parse(text);
        using JsonDocument document = JsonDocument.Parse(text);
        JsonElement result = document.RootElement.Clone();
        Assert.True(result.GetProperty("success").GetBoolean(), text);
        string state = result.TryGetProperty("creation", out JsonElement creation) ? creation.GetProperty("state").GetString()! : "read";
        var output = TestContext.Current.TestOutputHelper!;
        output.WriteLine(stage + ": " + state + "; warnings=" + result.GetProperty("warnings").GetArrayLength().ToString(CultureInfo.InvariantCulture));
        foreach (JsonElement page in result.GetProperty("pages").EnumerateArray())
        {
            string name = page.GetProperty("requestedName").GetString()!;
            output.WriteLine(TrySettings(page, out JsonElement settings)
                ? name + ": Type=" + settings.GetProperty("type").GetRawText() + "; FormName=" + settings.GetProperty("formName").GetString()
                : name + ": Type/FormName unavailable; see the saved observations.");
        }
        return result;
    }

    private Dictionary<string, (long Size, DateTime Written)>? TryProjectFiles(string project)
    {
        try
        { return NoSideEffectTests.ProjectFiles(project, withUserData: false); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { Warn("Fixture file evidence unavailable: " + error); return null; }
    }

    private async Task<string?> TryStateAsync(PlanStepLiveSession session)
    {
        try
        { return await session.StateAsync(); }
        catch (Exception error) { Warn("Rollback snapshot unavailable: " + error); return null; }
    }

    private static JsonNode? FileEvidence(Dictionary<string, (long Size, DateTime Written)>? files) => files is null ? null
        : JsonSerializer.SerializeToNode(files.ToDictionary(f => f.Key, f => new { size = f.Value.Size, written = f.Value.Written }));

    private static bool TrySettings(JsonElement row, out JsonElement settings)
    {
        settings = default;
        return row.TryGetProperty("reportBlock", out settings) && settings.ValueKind == JsonValueKind.Object
            && settings.TryGetProperty("type", out JsonElement type) && type.ValueKind == JsonValueKind.Number
            && settings.TryGetProperty("formName", out JsonElement form) && form.ValueKind == JsonValueKind.String;
    }

    private static string NextReportPage(string[] names, string prefix)
    {
        int maximum = names.Where(n => n.StartsWith(prefix, StringComparison.Ordinal))
            .Select(n => double.TryParse(n[prefix.Length..], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double number) ? checked((int)Math.Ceiling(number)) : 0).DefaultIfEmpty().Max();
        return prefix + checked(maximum + 100).ToString(CultureInfo.InvariantCulture);
    }

    private void Warn(string message)
    {
        _warnings.Add(message);
        TestContext.Current.TestOutputHelper!.WriteLine("T.4 warning: " + message);
    }
}

using System.Diagnostics;
using System.Text.Json;
namespace EplanMcp.Large.Tests;

/// <summary>
/// Issue 664 (EPLAN 2025.0.3): eplan_device_get answered DEVICE_NOT_FOUND for tags eplan_device_list had printed. Read only
/// on the fixture: every main function the list prints for a few categories is found by its tag, and a tag that does not
/// exist is refused within a few seconds although the lookup then reads every function.
/// </summary>
[Trait("Size", "Large")]
public sealed class DeviceListedTagsTests(LiveEplan eplan, ITestOutputHelper output)
{
    private static readonly string[] _categories = ["Coil", "Motor", "Terminal", "Cable", "PLCBox"];

    /// <summary>
    /// A170, second look: which read returns what. The helper add-in compares the unfiltered read, the read per page,
    /// each page's AllPlacements and the unfiltered typed reads on the fixture; read only. The numbers go to the output.
    /// </summary>
    [Fact]
    public async Task FunctionReads_ComparedNatively()
    {
        string helper = OfflineHost.RepositoryFile("tests", "EplanMcp.Large.ConnectionHelper", "EplanMcp.Large.ConnectionHelper.dll");
        string request = Path.Combine(Path.GetTempPath(), "eplanmcp-function-reads-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(request, JsonSerializer.Serialize(new { project = eplan.ProjectPath }));
        await PlanStepLiveSession.ActionAsync("EplApiModuleAction /register:\"" + helper + "\" /Quiet");
        try
        {
            await PlanStepLiveSession.ActionAsync("EplanMcpLargeFunctionReadsProbe /REQUEST:\"" + request + "\"");
            string result = File.ReadAllText(request + ".result.json");
            output.WriteLine(result);
            File.Copy(request + ".result.json", Path.Combine(Path.GetTempPath(), "eplanmcp-function-reads-latest.json"), overwrite: true);
            Assert.True(JsonDocument.Parse(result).RootElement.GetProperty("success").GetBoolean(), result);
        }
        finally
        {
            await PlanStepLiveSession.ActionAsync("EplApiModuleAction /unregister:\"EplanMcp.Large.ConnectionHelper\" /Quiet");
            File.Delete(request);
            File.Delete(request + ".result.json");
        }
    }
    /// <summary>
    /// A170: the coil =EC1+A3-EC1-QA1-K1 is one of the placed functions the unfiltered read omits. Renaming another
    /// device onto its tag would merge the two in EPLAN (A13), so the plan must refuse it. A dry run: nothing is written.
    /// </summary>
    [Fact]
    public async Task RenamingOntoATagTheUnfilteredReadOmits_IsRefusedAsTaken()
    {
        const string taken = "=EC1+A3-EC1-QA1-K1";
        JsonElement list = await eplan.CallAsync("eplan_device_list", new() { ["project"] = eplan.ProjectPath, ["category"] = "Coil", ["limit"] = 200 });
        string source = list.GetProperty("devices").EnumerateArray().Where(d => d.GetProperty("main").GetBoolean())
            .Select(d => d.GetProperty("tag").GetString()!).First(t => t.Length > 0 && t != taken);

        var (planned, answer) = await eplan.CallOrErrorAsync("eplan_device_update", new() { ["project"] = eplan.ProjectPath, ["tag"] = source, ["new_tag"] = taken, ["dry_run"] = true });

        output.WriteLine(source + " -> " + taken + ": " + answer.GetRawText()[..Math.Min(600, answer.GetRawText().Length)]);
        Assert.False(planned, "The rename onto a taken tag was planned.");
        Assert.Equal(EplanMcp.Contracts.ErrorReason.AlreadyExists, answer.GetProperty("reason").GetString());
    }
    [Fact]
    public async Task ListedMainTags_AreFoundByDeviceGet_AndAMissStaysFast()
    {
        var missing = new List<string>();
        int asked = 0;
        foreach (string category in _categories)
        {
            JsonElement list = await eplan.CallAsync("eplan_device_list", new() { ["project"] = eplan.ProjectPath, ["category"] = category, ["limit"] = 200 });
            string[] tags = [.. list.GetProperty("devices").EnumerateArray().Where(d => d.GetProperty("main").GetBoolean())
                .Select(d => d.GetProperty("tag").GetString()!).Where(t => t.Length > 0).Distinct(StringComparer.Ordinal).Take(60)];
            foreach (string tag in tags)
            {
                asked++;
                var (found, answer) = await eplan.CallOrErrorAsync("eplan_device_get", new() { ["project"] = eplan.ProjectPath, ["tag"] = tag });
                if (!found)
                {
                    missing.Add(category + " " + tag + " " + answer.GetRawText()[..Math.Min(160, answer.GetRawText().Length)]);
                    // What the tag is made of and what else carries it, for the diagnosis.
                    output.WriteLine("codes " + string.Join(" ", tag.Select(c => ((int)c).ToString("X4", System.Globalization.CultureInfo.InvariantCulture))));
                    string tail = tag[(tag.LastIndexOf('-', tag.LastIndexOf('-') - 1) + 1)..];
                    string? page = list.GetProperty("devices").EnumerateArray().First(d => d.GetProperty("tag").GetString() == tag).GetProperty("page").GetString();
                    output.WriteLine("row: " + list.GetProperty("devices").EnumerateArray().First(d => d.GetProperty("tag").GetString() == tag).GetRawText());
                    foreach ((string label, string tool, Dictionary<string, object?> args) in new (string, string, Dictionary<string, object?>)[]
                    {
                        ("category+contains", "eplan_device_list", new() { ["project"] = eplan.ProjectPath, ["category"] = category, ["tag_contains"] = tail, ["include_auxiliary"] = true, ["limit"] = 20 }),
                        ("contains only", "eplan_device_list", new() { ["project"] = eplan.ProjectPath, ["tag_contains"] = tail, ["include_auxiliary"] = true, ["limit"] = 20 }),
                        ("page+contains", "eplan_device_list", new() { ["project"] = eplan.ProjectPath, ["page"] = page, ["tag_contains"] = tail, ["include_auxiliary"] = true, ["limit"] = 20 }),
                        ("find Function", "eplan_object_find", new() { ["project"] = eplan.ProjectPath, ["object_type"] = "Function", ["name_contains"] = tail, ["limit"] = 10 }),
                        ("get visible", "eplan_device_get", new() { ["project"] = eplan.ProjectPath, ["tag"] = "-" + tail }),
                    })
                    {
                        var (_, raw) = await eplan.CallOrErrorAsync(tool, args);
                        output.WriteLine(label + ": " + raw.GetRawText()[..Math.Min(1500, raw.GetRawText().Length)]);
                    }
                }
            }
            output.WriteLine(FormattableString.Invariant($"{category}: {tags.Length} main tags asked"));
        }
        var watch = Stopwatch.StartNew();
        var (exists, refusal) = await eplan.CallOrErrorAsync("eplan_device_get", new() { ["project"] = eplan.ProjectPath, ["tag"] = "=NO+SUCH-DEVICE664" });
        watch.Stop();
        output.WriteLine(FormattableString.Invariant($"{asked} tags asked, {missing.Count} not found; a miss took {watch.ElapsedMilliseconds} ms: {refusal.GetRawText()[..Math.Min(160, refusal.GetRawText().Length)]}"));
        foreach (string line in missing)
        { output.WriteLine("NOT FOUND " + line); }
        Assert.True(asked > 0, "The fixture listed no main function.");
        Assert.Empty(missing);
        Assert.False(exists);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), "A missing tag took too long to refuse.");
    }
    /// <summary>A170: the unfiltered list has every function the per-page lists have (112 were missing on 2026-11-19).</summary>
    [Fact]
    public async Task UnfilteredList_ComparedWithThePerPageLists()
    {
        async Task<List<JsonElement>> AllAsync(Dictionary<string, object?> args)
        {
            var rows = new List<JsonElement>();
            string? cursor = null;
            do
            {
                var call = new Dictionary<string, object?>(args) { ["project"] = eplan.ProjectPath, ["include_auxiliary"] = true, ["limit"] = 200 };
                if (cursor is not null)
                { call["cursor"] = cursor; }
                JsonElement list = await eplan.CallAsync("eplan_device_list", call);
                rows.AddRange(list.GetProperty("devices").EnumerateArray().Select(d => d.Clone()));
                cursor = list.TryGetProperty("nextCursor", out JsonElement c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
            } while (cursor is not null);
            return rows;
        }
        // A main function is listed once per tag in the unfiltered list and once per page otherwise: compare by tag and category.
        static string Key(JsonElement d) => d.GetProperty("tag").GetString() + "\t" + d.GetProperty("category").GetString() + "\t\t" + d.GetProperty("main").GetBoolean();
        var clock = Stopwatch.StartNew();
        var unfiltered = (await AllAsync([])).Select(Key).ToHashSet(StringComparer.Ordinal);
        output.WriteLine(FormattableString.Invariant($"unfiltered list took {clock.ElapsedMilliseconds} ms"));
        var pages = new List<string>();
        string? next = null;
        do
        {
            var args = new Dictionary<string, object?> { ["project"] = eplan.ProjectPath, ["limit"] = 200 };
            if (next is not null)
            { args["cursor"] = next; }
            JsonElement list = await eplan.CallAsync("eplan_page_list", args);
            pages.AddRange(list.GetProperty("pages").EnumerateArray().Select(p => p.GetProperty("name").GetString()!));
            next = list.TryGetProperty("nextCursor", out JsonElement c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
        } while (next is not null);
        var perPage = new HashSet<string>(StringComparer.Ordinal);
        foreach (string page in pages)
        { perPage.UnionWith((await AllAsync(new() { ["page"] = page })).Select(Key)); }
        string[] onlyPages = [.. perPage.Except(unfiltered).Order(StringComparer.Ordinal)], onlyUnfiltered = [.. unfiltered.Except(perPage).Order(StringComparer.Ordinal)];
        output.WriteLine(FormattableString.Invariant($"unfiltered {unfiltered.Count}, per page {perPage.Count} over {pages.Count} pages; only per page {onlyPages.Length}, only unfiltered {onlyUnfiltered.Length}"));
        output.WriteLine("only per page, by category: " + string.Join(", ", onlyPages.GroupBy(k => k.Split('\t')[1]).OrderByDescending(g => g.Count()).Select(g => g.Key + " x" + g.Count())));
        output.WriteLine("only per page, by page: " + string.Join(", ", onlyPages.GroupBy(k => k.Split('\t')[2]).OrderByDescending(g => g.Count()).Take(12).Select(g => g.Key + " x" + g.Count())));
        foreach (string row in onlyPages.Take(25))
        { output.WriteLine("ONLY PER PAGE " + row.Replace('\t', ' ')); }
        output.WriteLine("only unfiltered, by page: " + string.Join(", ", onlyUnfiltered.GroupBy(k => k.Split('\t')[2]).OrderByDescending(g => g.Count()).Take(8).Select(g => (g.Key.Length == 0 ? "<none>" : g.Key) + " x" + g.Count())));
        Assert.Empty(onlyPages);
    }
}

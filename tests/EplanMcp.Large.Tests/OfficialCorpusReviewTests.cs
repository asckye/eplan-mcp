using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Large.Tests;

/// <summary>
/// eplan_page_review on official EPLAN projects (owner 2026-11-16): the official drawings are the reference for what is
/// correct, so no check may report an error on them. Each .zw1 below EPLANMCP_LARGE_OFFICIAL_CORPUS is restored once into
/// the write scratch folder (later runs reuse the copy), opened, sampled across its page types, reviewed and closed again.
/// The archives stay outside the repository; nothing outside scratch is written.
/// </summary>
[Trait("Size", "Large")]
public sealed class OfficialCorpusReviewTests(WriteHost host, ITestOutputHelper output) : IClassFixture<WriteHost>
{
    internal const string CorpusVariable = "EPLANMCP_LARGE_OFFICIAL_CORPUS";

    // Everything except EPLAN's own messages (they need eplan_check_run), the expected-drawing comparison and rule packs.
    private static readonly string[] _checks = ["dangling_ends", "unconnected_pins", "overlaps", "off_grid", "unintended_junctions",
        "interruption_pairing", "cable_cores", "box_crossings", "off_area", "wire_through_symbol", "net_overlap", "text_format",
        "cross_reference_clash", "path_straddle", "page_setup", "wire_number", "placeholder_tag"];

    private const int PerType = 3;
    private const int Circuit = 12;

    [Fact]
    public async Task OfficialProjects_HaveNoReviewErrors()
    {
        string? folder = Environment.GetEnvironmentVariable(CorpusVariable);
        Assert.SkipWhen(folder is null || !Directory.Exists(folder), $"Set {CorpusVariable} to a folder holding official EPLAN .zw1 backups.");
        string[] archives = [.. Directory.EnumerateFiles(folder!, "*.zw1", SearchOption.AllDirectories).Order(StringComparer.Ordinal)];
        Assert.NotEmpty(archives);
        // Crash investigation 2026-11-19: EPLAN died twice on 2026-11-17 while this test reviewed the macro project; several
        // passes over the corpus in one EPLAN give an intermittent crash more chances.
        if (int.TryParse(Environment.GetEnvironmentVariable(CorpusVariable + "_REPEAT"), out int repeat) && repeat > 1)
        { archives = [.. Enumerable.Repeat(archives, repeat).SelectMany(a => a)]; }

        var errors = new List<string>();
        var restoreFailures = new List<string>();
        var warnings = new SortedDictionary<string, int>(StringComparer.Ordinal);
        int reviewed = 0;
        foreach (string archive in archives)
        {
            string? project = await RestoreAndOpenAsync(archive, restoreFailures);
            if (project is null)
            {
                continue;
            }
            try
            {
                List<(string Page, string Type)> sample = [.. Sample(await PagesAsync(project))];
                if (sample.FirstOrDefault(p => p.Type == "Circuit").Page is { } first && await StaleCursorAsync(project, first) is { } stale)
                { errors.Add(Path.GetFileName(project) + " " + first + " " + stale); }
                foreach ((string page, string type) in sample)
                {
                    var clock = System.Diagnostics.Stopwatch.StartNew();
                    JsonElement review = await host.CallAsync("eplan_page_review", new() { ["project"] = project, ["page"] = page, ["checks"] = _checks, ["limit"] = 200 });
                    JsonElement[] issues = [.. review.GetProperty("issues").EnumerateArray()];
                    reviewed++;
                    output.WriteLine(FormattableString.Invariant($"{Path.GetFileName(project)} {page} ({type}, {clock.Elapsed.TotalSeconds:0.0} s): ")
                        + string.Join(", ", issues.GroupBy(i => i.GetProperty("code").GetString() + "/" + i.GetProperty("severity").GetString()).Select(g => g.Key + " x" + g.Count())));
                    foreach (JsonElement issue in issues)
                    {
                        string key = issue.GetProperty("code").GetString() + "/" + issue.GetProperty("severity").GetString();
                        warnings[key] = warnings.GetValueOrDefault(key) + 1;
                        if (issue.GetProperty("severity").GetString() == "error")
                        {
                            string raw = issue.GetRawText();
                            errors.Add(Path.GetFileName(project) + " " + page + " " + raw[..Math.Min(600, raw.Length)]);
                        }
                    }
                }
            }
            finally
            {
                await host.CallAsync("eplan_project_close", new() { ["project"] = project, ["dry_run"] = false });
                using var eplan = System.Diagnostics.Process.GetProcessById(host.ProcessId);
                output.WriteLine(FormattableString.Invariant($"EPLAN after {Path.GetFileName(project)}: private {eplan.PrivateMemorySize64 >> 20} MiB, handles {eplan.HandleCount}"));
            }
        }
        output.WriteLine(FormattableString.Invariant($"{reviewed} pages reviewed in {archives.Length} projects; by code: ") + string.Join(", ", warnings.Select(w => w.Key + " x" + w.Value)));
        foreach (string failure in restoreFailures)
        {
            output.WriteLine("NOT RESTORED " + failure);
        }
        foreach (string error in errors)
        {
            output.WriteLine("ERROR " + error);
        }
        Assert.True(reviewed > 0, "No official page was reviewed.");
        Assert.Empty(errors);
    }

    /// <summary>Restores the archive into scratch once, then opens that copy; null (with the reason in errors) when either fails.</summary>
    private async Task<string?> RestoreAndOpenAsync(string archive, List<string> restoreFailures)
    {
        string name = Name(archive);
        string link = Path.Combine(WriteHost.ScratchFolder, "projects", name, name + ".elk");
        if (!File.Exists(link))
        {
            string relative = "large/corpus/" + name + ".zw1";
            string imported = Path.Combine(host.Home, "imports", relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(imported)!);
            File.Copy(archive, imported, overwrite: true);
            var clock = System.Diagnostics.Stopwatch.StartNew();
            CallToolResult created = await host.RawAsync("eplan_project_create", new() { ["mode"] = "backup", ["file"] = relative, ["name"] = name, ["dry_run"] = false });
            output.WriteLine(FormattableString.Invariant($"restored {Path.GetFileName(archive)} as {name} in {clock.Elapsed.TotalSeconds:0} s: ") + WriteHost.Text(created));
            if (created.IsError == true || !File.Exists(link))
            {
                // Not a review finding: EPLAN could not restore this archive here (EplLog.eldb says why); the others still run.
                restoreFailures.Add(Path.GetFileName(archive));
                return null;
            }
        }
        JsonElement opened = await host.CallAsync("eplan_project_open", new() { ["path"] = link, ["dry_run"] = false });
        return opened.GetProperty("path").GetString();
    }

    /// <summary>
    /// Walks every drawing slice of a page right after the project opened. Corpus 2026-11-17 (A147): an object that reads
    /// differently on the first read of a page changes the digest, and the cursor of the first slice goes stale.
    /// </summary>
    private async Task<string?> StaleCursorAsync(string project, string page)
    {
        string? cursor = null;
        do
        {
            var args = new Dictionary<string, object?> { ["project"] = project, ["page"] = page, ["include_drawings"] = true, ["drawing_limit"] = 200 };
            if (cursor is not null)
            {
                args["drawing_cursor"] = cursor;
            }
            CallToolResult read = await host.RawAsync("eplan_page_get", args);
            if (read.IsError == true)
            {
                return "drawing slices after opening: " + WriteHost.Text(read);
            }
            JsonElement drawings = read.StructuredContent!.Value.GetProperty("drawings");
            cursor = drawings.TryGetProperty("nextCursor", out JsonElement next) && next.ValueKind == JsonValueKind.String ? next.GetString() : null;
        }
        while (cursor is not null);
        return null;
    }

    private async Task<List<(string Page, string Type)>> PagesAsync(string project)
    {
        var pages = new List<(string, string)>();
        string? cursor = null;
        do
        {
            var args = new Dictionary<string, object?> { ["project"] = project, ["limit"] = 200 };
            if (cursor is not null)
            {
                args["cursor"] = cursor;
            }
            JsonElement list = await host.CallAsync("eplan_page_list", args);
            pages.AddRange(list.GetProperty("pages").EnumerateArray().Select(p => (p.GetProperty("name").GetString()!, p.GetProperty("type").GetString()!)));
            cursor = list.TryGetProperty("nextCursor", out JsonElement next) && next.ValueKind == JsonValueKind.String ? next.GetString() : null;
        }
        while (cursor is not null);
        return pages;
    }

    /// <summary>Up to <see cref="PerType"/> pages of each type and <see cref="Circuit"/> circuit pages, spread evenly.</summary>
    internal static IEnumerable<(string Page, string Type)> Sample(List<(string Page, string Type)> pages) =>
        pages.GroupBy(p => p.Type, StringComparer.Ordinal).SelectMany(g =>
        {
            (string, string)[] all = [.. g];
            int take = Math.Min(all.Length, g.Key == "Circuit" ? Circuit : PerType);
            return Enumerable.Range(0, take).Select(i => all[i * all.Length / take]);
        });

    /// <summary>
    /// A short project name from the archive's distinctive words. Live 2026-11-17: EPLAN failed to move the restored
    /// database ("移动数据库时出错") for a 54-character name, since the files inside the project exceed the path limit.
    /// </summary>
    internal static string Name(string archive)
    {
        string[] common = ["EES", "Industry", "Application", "Sample", "Standardization", "Template", "project", "V01"];
        string[] words = [.. Path.GetFileNameWithoutExtension(archive).Split(['_', '[', ']', ' ', '-'], StringSplitOptions.RemoveEmptyEntries)
            .Where(w => !common.Contains(w, StringComparer.OrdinalIgnoreCase))
            .Select(w => new string([.. w.Where(char.IsAsciiLetterOrDigit)])).Where(w => w.Length > 0)];
        string name = "C_" + string.Join("_", words);
        return name[..Math.Min(name.Length, 30)].TrimEnd('_');
    }
}

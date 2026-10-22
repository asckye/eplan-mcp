using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Host.Exports;
using EplanMcp.Packs;
using EplanMcp.Packs.Scanning;

namespace EplanMcp.Host.Packs;

/// <summary>
/// <c>eplanmcp pack scan|check|export|install</c>: the engineering pack library for manual use and tests (M6). Text for
/// the engineer, or JSON with <c>--format json</c>. Never runs in serve mode, so stdout is free here. Exit codes: 0 done,
/// 1 when the inputs have errors or the pack has problems.
/// </summary>
internal static class PackCommand
{
    /// <summary>The version of the JSON shape (architecture 5.2); a change to the shape raises it.</summary>
    public const int SchemaVersion = 1;

    public static async Task<int> RunAsync(PackRequest request, HostDataLocations locations, TextWriter output, bool json, CancellationToken cancellationToken)
    {
        JsonObject result = request.Action switch
        {
            PackAction.Scan => await ScanAsync(request, cancellationToken).ConfigureAwait(false),
            PackAction.Check => await CheckAsync(request.Paths[0], cancellationToken).ConfigureAwait(false),
            PackAction.Export => await ExportAsync(request.Paths[0], request.Paths[1], cancellationToken).ConfigureAwait(false),
            _ => await InstallAsync(new PackStore(locations.Packs), request.Paths[0], cancellationToken).ConfigureAwait(false),
        };
        var ordered = new JsonObject { ["schemaVersion"] = SchemaVersion, ["command"] = request.Action.ToString().ToLowerInvariant() };
        foreach (string key in result.Select(p => p.Key).ToList())
        {
            JsonNode? value = result[key];
            result.Remove(key);
            ordered[key] = value;
        }

        output.WriteLine(json ? ordered.ToJsonString(PackJson.Options) : Text(ordered));
        return (bool)ordered["ok"]! ? 0 : 1;
    }

    /// <summary>An input as the command line gives it: <c>path</c>, <c>&lt;kind&gt;=path</c> (such as <c>rule=</c>) or <c>sample=path</c>.</summary>
    internal static ScanInput Input(string argument, bool official)
    {
        int equals = argument.IndexOf('=', StringComparison.Ordinal);
        string prefix = equals > 0 ? argument[..equals] : string.Empty;
        if (prefix == "sample")
        {
            return new ScanInput(argument[(equals + 1)..], AcceptanceSample: true, Official: official);
        }

        ResourceKind? kind = Enum.GetValues<ResourceKind>().Cast<ResourceKind?>().FirstOrDefault(k => JsonNamingPolicy.SnakeCaseLower.ConvertName(k!.Value.ToString()) == prefix);
        return kind is null ? new ScanInput(argument, Official: official) : new ScanInput(argument[(equals + 1)..], kind, Official: official);
    }

    private static async Task<JsonObject> ScanAsync(PackRequest request, CancellationToken cancellationToken)
    {
        ScanResult scan = await ResourceScanner.ScanAsync([.. request.Paths.Select(p => Input(p, request.Official))], cancellationToken).ConfigureAwait(false);
        var result = new JsonObject
        {
            ["ok"] = !scan.HasErrors,
            ["resources"] = JsonSerializer.SerializeToNode(scan.Resources.Select(r => r.Resource).ToList(), PackJson.Options),
            ["facts"] = JsonSerializer.SerializeToNode(scan.Resources.SelectMany(r => r.Facts).ToList(), PackJson.Options),
            ["findings"] = JsonSerializer.SerializeToNode(scan.Findings, PackJson.Options),
        };
        if (request.Identity is not { } identity)
        {
            return result;
        }

        PackManifest manifest = ManifestGenerator.Generate(scan, identity, $"eplanmcp {HostInfo.Version}", DateTimeOffset.UtcNow);
        result["manifest"] = JsonSerializer.SerializeToNode(manifest, PackJson.Options);
        IReadOnlyList<PackProblem> invalid = PackManifestValidator.Validate(manifest);
        result["problems"] = Problems(invalid);
        result["ok"] = !scan.HasErrors && invalid.Count == 0;
        if (request.Out is { } folder && (bool)result["ok"]!)
        {
            try
            {
                PackCheckResult written = await PackWriter.WriteAsync(scan, manifest, folder, cancellationToken).ConfigureAwait(false);
                result["ok"] = written.Ok;
                result["problems"] = Problems(written.Problems);
                result["written"] = written.Ok ? Path.GetFullPath(folder) : null;
            }
            catch (PackFormatException ex)
            {
                result["ok"] = false;
                result["problems"] = Problems([new PackProblem("pack", ex.Message)]);
            }
        }

        return result;
    }

    private static async Task<JsonObject> CheckAsync(string path, CancellationToken cancellationToken)
    {
        PackCheckResult check;
        if (File.Exists(path))
        {
            // An .eplanpack is checked by extracting it into a temporary folder, with every rule an install applies.
            string temporary = Path.Combine(Path.GetTempPath(), $"eplanmcp-check-{Guid.NewGuid():N}");
            try
            {
                check = await PackArchive.ExtractAsync(path, temporary, PackLimits.Default, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                if (Directory.Exists(temporary))
                {
                    Directory.Delete(temporary, recursive: true);
                }
            }
        }
        else
        {
            check = await PackVerifier.CheckDirectoryAsync(path, cancellationToken).ConfigureAwait(false);
        }

        return new JsonObject
        {
            ["ok"] = check.Ok,
            ["id"] = check.Manifest?.Id,
            ["version"] = check.Manifest?.Version,
            ["resources"] = check.Manifest?.Resources.Count,
            ["problems"] = Problems(check.Problems),
        };
    }

    private static async Task<JsonObject> ExportAsync(string folder, string file, CancellationToken cancellationToken)
    {
        try
        {
            await PackArchive.ExportAsync(folder, file, cancellationToken).ConfigureAwait(false);
            return new JsonObject { ["ok"] = true, ["file"] = Path.GetFullPath(file), ["bytes"] = new FileInfo(file).Length, ["problems"] = Problems([]) };
        }
        catch (PackFormatException ex)
        {
            return new JsonObject { ["ok"] = false, ["problems"] = Problems([new PackProblem("pack", ex.Message)]) };
        }
    }

    private static async Task<JsonObject> InstallAsync(PackStore store, string source, CancellationToken cancellationToken)
    {
        PackInstallResult installed = await store.InstallAsync(source, cancellationToken).ConfigureAwait(false);
        return new JsonObject
        {
            ["ok"] = installed.Outcome != PackInstallOutcome.Refused,
            ["outcome"] = JsonNamingPolicy.SnakeCaseLower.ConvertName(installed.Outcome.ToString()),
            ["id"] = installed.Manifest?.Id,
            ["version"] = installed.Manifest?.Version,
            ["folder"] = installed.Manifest is { } m && installed.Outcome != PackInstallOutcome.Refused ? store.FolderOf(m.Id, m.Version) : null,
            ["problems"] = Problems(installed.Problems),
        };
    }

    private static JsonArray Problems(IReadOnlyList<PackProblem> problems) =>
        [.. problems.Select(p => (JsonNode)new JsonObject { ["location"] = p.Location, ["message"] = p.Message })];

    /// <summary>The result for the engineer: one line per resource, finding and problem, then the outcome.</summary>
    private static string Text(JsonObject result)
    {
        var text = new List<string>();
        foreach (JsonNode? resource in result["resources"] as JsonArray ?? [])
        {
            string version = resource!["eplanVersion"] is { } v ? $", EPLAN {v}" : string.Empty;
            text.Add(string.Create(CultureInfo.InvariantCulture,
                $"  {resource["kind"],-17} {resource["path"]}  ({resource["files"]} file(s), {ByteSize.Format((long)resource["bytes"]!)}{version})"));
        }

        if (text.Count > 0)
        {
            text.Insert(0, $"Resources ({text.Count}):");
        }

        foreach (JsonNode? finding in result["findings"] as JsonArray ?? [])
        {
            text.Add($"  {finding!["severity"],-8} {finding["code"]}: {finding["message"]}");
        }

        text.AddRange((result["problems"] as JsonArray ?? []).Select(p => $"  problem  {p!["location"]}: {p["message"]}"));
        text.Add(((string?)result["command"], (bool)result["ok"]!) switch
        {
            ("scan", true) when result["written"] is { } folder => $"Pack written: {folder}",
            ("scan", true) => "No errors. Give --out <folder> with --id, --name and --version to write the pack.",
            ("scan", false) => "Not written: fix the errors first.",
            ("check", true) => $"The pack {result["id"]} {result["version"]} checks clean ({result["resources"]} resources).",
            ("export", true) => $"Exported: {result["file"]} ({ByteSize.Format((long)result["bytes"]!)})",
            ("install", true) => $"{result["outcome"]}: {result["id"]} {result["version"]} in {result["folder"]}",
            _ => "Refused.",
        });
        return string.Join(Environment.NewLine, text);
    }
}

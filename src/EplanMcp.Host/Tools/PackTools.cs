using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Importing;
using EplanMcp.Host.Packs;
using EplanMcp.Packs;
using EplanMcp.Packs.Scanning;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EplanMcp.Host.Tools;

/// <summary>
/// The engineering pack tools (docs/tools/eplan_pack_*.md; M6 design 4.5.1), toolset <c>pack</c>, in the full profile
/// only. They work on EplanMcp's data folder without EPLAN; the ones that change it are local data: no approval, an
/// audit record before and after (<see cref="PackLibrary.AuditAsync"/>).
/// </summary>
[McpServerToolType]
internal sealed class PackTools
{
    public const string ImportTool = "eplan_pack_import";

    public const int MaxInputs = 50;

    public const int MaxNameLength = 100;

    [McpServerTool(
        Name = ImportTool,
        Title = "Import engineering inputs into a development pack",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(PackImportResult))]
    [Description(
        "Imports an engineer's inputs (macros, forms, documents, mappings, .zw9, .zw1, EPLAN projects) into a development " +
        "pack and generates its manifest and resource index, without EPLAN. An input is a path below the 'imports' folder in " +
        "EplanMcp's data folder, or '<source>:<path>' below a root of config.json packSources; 'rule=', 'material_mapping=' " +
        "or 'document=' sets a file's kind. Writes only EplanMcp's data. Example: {\"pack\": \"acme.panels\", \"inputs\": [\"macros\", \"rule=rules.md\"]}.")]
    public static async Task<CallToolResult> PackImportAsync(
        PackLibrary packs,
        IReferenceProjectSteps references,
        [Description("Pack id; created if missing")] string pack,
        [Description("1-50 files, folders")] string[] inputs,
        [Description("Name of a new pack")] string? name = null,
        [Description("Copy projects in, not reference")] bool bundle_projects = false,
        [Description("Replace changed resources")] bool replace = false,
        CancellationToken cancellationToken = default)
    {
        var violations = new List<string>();
        CheckId(pack, violations);
        string? newName = ToolArguments.Optional(name)?.Trim();
        if (newName is not null && (newName.Length is 0 or > MaxNameLength || newName.Any(char.IsControl)))
        {
            violations.Add($"'name' must have 1 to {MaxNameLength} characters without control characters.");
        }

        List<PackInputArgument> parsed = [];
        if (inputs is not { Length: > 0 and <= MaxInputs })
        {
            violations.Add($"'inputs' must list 1 to {MaxInputs} files or folders, such as [\"macros\", \"refs/Line1\"].");
        }
        else
        {
            parsed.AddRange(inputs.Select((given, i) => PackInputs.Parse($"inputs[{i}]", given, packs.Config, violations)).OfType<PackInputArgument>());
        }

        if (violations.Count > 0)
        {
            return ToolResults.InvalidArguments(violations);
        }

        List<ScanInput> scanInputs;
        try
        {
            scanInputs = [.. parsed.Select((input, i) => PackInputs.Resolve($"inputs[{i}]", input, packs.Locations, packs.Config, bundle_projects))];
        }
        catch (ImportFileException ex)
        {
            ex.Error.Execution = null;
            return ToolResults.Error(ex.Error);
        }

        ScanResult scan = await ResourceScanner.ScanAsync(scanInputs, cancellationToken).ConfigureAwait(false);
        violations.AddRange(scan.Findings.Where(f => f.Severity == FindingSeverity.Error).Select(f => ResultShaper.CleanText(f.Message)!));
        violations.AddRange(scan.Resources.Where(r => r.Resource.Storage == ResourceStorage.Referenced && r.Input.Root is null).Select(r =>
            $"'{ResultShaper.CleanText(r.Resource.Source?.Input + "/" + r.Resource.Source?.Path)}' is an EPLAN project in the imports folder. Projects are referenced, not copied, by default (they are large and hold customer data), which needs a pack source: ask the engineer to list its folder in config.json packSources and give it as '<source>:<path>', or, with the engineer's consent, call again with bundle_projects: true."));
        violations.AddRange(Limits(scan));
        if (violations.Count > 0)
        {
            return ToolResults.InvalidArguments(violations);
        }

        string requestId = Guid.NewGuid().ToString("N");
        try
        {
            var started = new JsonObject
            {
                ["action"] = "import",
                ["pack"] = pack,
                ["inputs"] = new JsonArray([.. inputs!.Select(i => (JsonNode?)i.Trim())]),
                ["bundleProjects"] = bundle_projects,
                ["replace"] = replace,
            };
            await packs.AuditAsync(requestId, ImportTool, "started", started, changed: false, cancellationToken).ConfigureAwait(false);
            DevPackChange change;
            try
            {
                change = await packs.Dev.ImportAsync(pack, newName ?? pack, scan, replace, PackLibrary.CreatedBy, packs.Clock.GetUtcNow(), cancellationToken).ConfigureAwait(false);
            }
            catch (DevPackException ex)
            {
                await packs.AuditFailedAsync(requestId, ImportTool, ex.Error.ToString(), cancellationToken).ConfigureAwait(false);
                return ToolResults.Error(PackLibrary.Error(ex, pack));
            }

            IReadOnlyList<ScanFinding> found = await references.RunAsync(change, cancellationToken).ConfigureAwait(false);
            var committed = new JsonObject
            {
                ["pack"] = pack,
                ["digest"] = change.Digest,
                ["added"] = new JsonArray([.. change.Added.Select(r => (JsonNode?)$"{r.Path} {r.Sha256}")]),
                ["replaced"] = new JsonArray([.. change.Replaced.Select(r => (JsonNode?)$"{r.Path} {r.Sha256}")]),
                ["gaps"] = change.Gaps.Count,
            };
            await packs.AuditAsync(requestId, ImportTool, "committed", committed, changed: true, cancellationToken).ConfigureAwait(false);
            return ImportResult(pack, change, scan.Findings.Concat(found));
        }
        catch (BridgeCallException ex)
        {
            return ToolResults.Error(ex.Error);
        }
    }

    private static CallToolResult ImportResult(string pack, DevPackChange change, IEnumerable<ScanFinding> findings)
    {
        HashSet<string> changed = new(change.Added.Concat(change.Replaced).Select(r => r.Path), PackPath.Comparer);
        var result = new PackImportResult
        {
            Pack = pack,
            Created = change.Created,
            Digest = change.Digest,
            Resources = change.Manifest.Resources.Count,
            Added = [.. change.Added.Select(r => Entry(pack, r))],
            Replaced = [.. change.Replaced.Select(r => Entry(pack, r))],
            Unchanged = ResultShaper.CleanTexts(change.Unchanged),
            Facts = change.Manifest.Facts.Count(f => changed.Contains(f.Resource)),
            Gaps = [.. change.Gaps.Select(g => AssetNames.Id(pack, AssetNames.Dev, AssetNames.Local(change.Manifest.Resources.Single(r => r.Path == g))))],
            Findings =
            [
                .. findings.Where(f => f.Severity == FindingSeverity.Warning).Select(f => new PackImportResult.FindingEntry
                {
                    Severity = "warning",
                    Code = f.Code,
                    Subject = ResultShaper.CleanText(f.Subject)!,
                    Message = ResultShaper.CleanText(f.Message)!,
                }),
            ],
        };
        if (!ResultShaper.Fits(result, ResultShaper.DefaultTokenBudget))
        {
            result.Truncated = true;
            result.Hint = "Lists were cut to stay within the size budget; eplan_pack_get and eplan_asset_search show every resource.";
            ResultShaper.FitItems(result, result.Unchanged);
            ResultShaper.FitItems(result, result.Findings);
            ResultShaper.FitItems(result, result.Added);
        }

        string summary = string.Create(CultureInfo.InvariantCulture,
            $"Imported {ToolResults.Count(change.Added.Count, "resource")} into development pack '{pack}'")
            + (change.Replaced.Count > 0 ? $", replaced {change.Replaced.Count.ToString(CultureInfo.InvariantCulture)}" : string.Empty)
            + (change.Unchanged.Count > 0 ? $", {change.Unchanged.Count.ToString(CultureInfo.InvariantCulture)} unchanged" : string.Empty)
            + (change.Gaps.Count > 0 ? $" ({ToolResults.Count(change.Gaps.Count, "gap")} to check)." : ".");
        return ToolResults.Success(summary, result);
    }

    private static PackImportResult.ResourceEntry Entry(string pack, PackResource resource) => new()
    {
        Asset = ResultShaper.CleanText(AssetNames.Id(pack, AssetNames.Dev, AssetNames.Local(resource)))!,
        Path = ResultShaper.CleanText(resource.Path)!,
        Kind = Snake(resource.Kind),
        Subtype = resource.Subtype,
        Storage = Snake(resource.Storage),
        Bytes = resource.Bytes,
        Files = resource.Files,
        Sha256 = resource.Sha256,
        EplanVersion = resource.EplanVersion,
    };

    /// <summary>ADR-0024's limits, applied to what an import hashes: no resource over 2 GB, 8 GB and 20,000 resources in all.</summary>
    private static IEnumerable<string> Limits(ScanResult scan)
    {
        PackLimits limits = PackLimits.Default;
        foreach (ScannedResource big in scan.Resources.Where(r => r.Resource.Bytes > limits.MaxEntryBytes))
        {
            yield return $"'{ResultShaper.CleanText(big.Resource.Path)}' is larger than {limits.MaxEntryBytes / (1024 * 1024 * 1024)} GB, the most one pack resource may have (ADR-0024).";
        }

        if (scan.Resources.Sum(r => r.Resource.Bytes) > limits.MaxTotalBytes || scan.Resources.Count > limits.MaxEntries)
        {
            yield return $"The inputs hold more than {limits.MaxTotalBytes / (1024 * 1024 * 1024)} GB or {limits.MaxEntries} resources, more than one pack may (ADR-0024); import fewer at a time.";
        }
    }

    internal static void CheckId(string? pack, List<string> violations)
    {
        if (!PackManifestValidator.IsId(pack ?? string.Empty))
        {
            violations.Add("'pack' must be a pack id: at most 64 lowercase letters and digits in groups joined by '.' or '-', such as acme.panels.");
        }
    }

    internal static string Snake<T>(T value)
        where T : struct, Enum => JsonNamingPolicy.SnakeCaseLower.ConvertName(value.ToString());
}

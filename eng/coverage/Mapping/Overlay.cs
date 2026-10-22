using System.Globalization;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace EplanMcp.Coverage.Mapping;

/// <summary>One run of a tool against a real EPLAN, as a source in the repository records it (overlay.yaml, live).</summary>
internal sealed class LiveRecord
{
    public string Date { get; set; } = "";

    public string? Eplan { get; set; }

    public string? Variant { get; set; }

    public string Result { get; set; } = "";

    /// <summary>stated: the source names the tool; inferred: it describes the run without naming it.</summary>
    public string Basis { get; set; } = "";

    public string Source { get; set; } = "";

    public string? Note { get; set; }

    /// <summary>Counted as live verification: named by its source, and not a failure or a partial run.</summary>
    public bool Verifies => Basis == "stated" && Result is "pass" or "pass_with_findings";
}

internal sealed class ToolNote
{
    public string? Note { get; set; }

    /// <summary>Bridge operations the tool reaches outside its own code, such as masterdata.backup, which the write pipeline calls for it.</summary>
    public List<string> Operations { get; set; } = [];
}

/// <summary>A catalog entry, type or namespace (N:) that is excluded, blocked by the environment, or partly covered.</summary>
internal sealed class ApiRule
{
    public string Match { get; set; } = "";

    public string Status { get; set; } = "";

    public string? Reason { get; set; }

    public string? Condition { get; set; }

    public string? Note { get; set; }

    public string Source { get; set; } = "";
}

internal sealed class Availability
{
    public string Scope { get; set; } = "";

    public string Status { get; set; } = "";

    public string Note { get; set; } = "";

    public string Source { get; set; } = "";
}

/// <summary>
/// catalog/mapping/overlay.yaml: the coverage facts maintained by hand because code cannot show them. Unknown fields,
/// tools that do not exist and values outside the documented sets are errors, so a typo cannot silently drop a fact.
/// </summary>
internal sealed class Overlay
{
    public const string RelativePath = "catalog/mapping/overlay.yaml";

    /// <summary>The exclusion reasons of the coverage design (docs/design/2026-10-21-api-capability-coverage.md section 4).</summary>
    public static readonly string[] Reasons = ["gui", "internal", "deprecated", "unsafe", "not_applicable"];

    public Dictionary<string, List<LiveRecord>> Live { get; set; } = [];

    public Dictionary<string, ToolNote> Tools { get; set; } = [];

    public List<ApiRule> Api { get; set; } = [];

    public List<Availability> Availability { get; set; } = [];

    public static Overlay Load(string repositoryRoot, IReadOnlyCollection<string> tools)
    {
        string path = Path.Combine(repositoryRoot, RelativePath);
        if (!File.Exists(path))
        {
            return new Overlay();
        }

        Overlay overlay;
        try
        {
            overlay = new DeserializerBuilder().WithNamingConvention(CamelCaseNamingConvention.Instance).Build()
                .Deserialize<Overlay?>(File.ReadAllText(path)) ?? new Overlay();
        }
        catch (YamlException ex)
        {
            throw new InvalidDataException($"{RelativePath}: {ex.Message}", ex);
        }

        string[] problems =
        [
            .. overlay.Live.Keys.Concat(overlay.Tools.Keys).Where(t => !tools.Contains(t)).Select(t => $"{t} is not a tool of the contract snapshot"),
            .. overlay.Live.SelectMany(l => l.Value.Where(r => !DateOnly.TryParseExact(r.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
                || r.Result is not ("pass" or "pass_with_findings" or "partial" or "fail") || r.Basis is not ("stated" or "inferred") || r.Source.Length == 0)
                .Select(r => $"{l.Key}: a live record needs a yyyy-MM-dd date, a result, a basis and a source")),
            .. overlay.Api.Where(r => r.Source.Length == 0 || r.Match.IndexOf(':', StringComparison.Ordinal) != 1
                || (r.Status, r.Condition, r.Note) is not (("excluded", _, not null) or ("env_blocked", not null, _) or ("partial", _, not null))
                || (r.Status == "excluded") != Reasons.Contains(r.Reason))
                .Select(r => $"api {r.Match}: needs a source, an ID or N:<namespace>, and excluded with a reason ({string.Join(", ", Reasons)}) and a note, env_blocked with a condition, or partial with a note"),
        ];
        return problems.Length == 0 ? overlay : throw new InvalidDataException($"{RelativePath}: " + string.Join("; ", problems));
    }
}

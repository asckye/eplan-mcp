using System.Text.Json.Nodes;

namespace EplanMcp.Packs;

/// <summary>Pure structure matching for v2 cabinets; connections and page placement never expand membership.</summary>
public sealed class CabinetScopeResolver
{
    private readonly CabinetBinding[] _cabinets;

    /// <summary>Snapshots cabinet mappings. Callers validate the binding before using a unique match for writes.</summary>
    public CabinetScopeResolver(IReadOnlyList<CabinetBinding> cabinets)
    {
        _cabinets = [.. cabinets.Select(c => c with { Structure = (JsonObject?)c.Structure?.DeepClone() })];
    }

    /// <summary>Zero ids means unassigned or unproven, one means unique, more means ambiguous. Writes require a unique match.</summary>
    public IReadOnlyList<string> CabinetOf(string? designation)
    {
        IReadOnlyDictionary<string, string>? segments = ParseDesignation(designation);
        return segments is null ? [] : [.. _cabinets.Where(c => All(c) || Prefixes(c).Any(p => p.All(s =>
            segments.TryGetValue(Marker(s.Key), out string? value) && value == s.Value!.GetValue<string>())))
            .Select(c => c.Id)];
    }

    /// <summary>Potential intersection of validated mappings, including structures with no current pages.</summary>
    public static bool Overlaps(CabinetBinding cabinetA, CabinetBinding cabinetB) => All(cabinetA) || All(cabinetB)
        || Prefixes(cabinetA).Any(a => Prefixes(cabinetB).Any(b => a.All(s =>
            !b.TryGetPropertyValue(s.Key, out JsonNode? other) || JsonNode.DeepEquals(s.Value, other))));

    /// <summary>Full literal segments keyed by marker, or null when the designation cannot prove its structure.</summary>
    public static IReadOnlyDictionary<string, string>? ParseDesignation(string? designation)
    {
        if (string.IsNullOrEmpty(designation) || designation.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)
            || "*?[](){}|\\^$".Contains(c, StringComparison.Ordinal)))
        {
            return null;
        }

        var segments = new Dictionary<string, string>(StringComparer.Ordinal);
        int position = 0;
        while (position < designation.Length && designation[position] is not ('-' or '/'))
        {
            int start = position++;
            char marker = designation[start];
            if (marker is not ('=' or '+' or '&' or '#'))
            {
                return null;
            }

            if (marker is '=' or '+' && position < designation.Length && designation[position] == marker)
            {
                position++;
            }

            string key = designation[start..position];
            int valueStart = position;
            while (position < designation.Length && !"=+&#-/".Contains(designation[position], StringComparison.Ordinal))
            {
                position++;
            }

            if (position == valueStart || !segments.TryAdd(key, designation[start..position]))
            {
                return null;
            }
        }

        // Incomplete names and embedded second structures cannot prove membership.
        if (position < designation.Length && (position + 1 == designation.Length
            || designation[(position + 1)..].Any(c => "=+&#/".Contains(c, StringComparison.Ordinal))))
        {
            return null;
        }

        return segments;
    }

    internal static string Marker(string name) => name switch
    {
        "installation" => "=",
        "location" => "+",
        "documentType" => "&",
        "functionalAssignment" => "==",
        "installationNumber" => "++",
        _ => "#",
    };

    private static bool All(CabinetBinding cabinet) => cabinet.Structure?["all"]?.GetValue<bool>() == true;

    private static IEnumerable<JsonObject> Prefixes(CabinetBinding cabinet) =>
        cabinet.Structure?["prefixes"]?.AsArray().Select(p => p!.AsObject()) ?? [];
}

using EplanMcp.Contracts;
using EplanMcp.Packs;

namespace EplanMcp.Host.Packs;

/// <summary>One requirement of a pack and whether it holds now (M6 design 4.4); <see cref="Hard"/> ones make the pack unusable.</summary>
internal sealed record CapabilityCheck(string Requirement, bool Hard, string Status, string? Note);

/// <summary>
/// The capability check of M6 design 4.4: a pack's EPLAN versions, variants and capabilities against the connected
/// EPLAN (handshake) and the tools this server offers. EPLAN versions, variants, <c>op:</c>, <c>licence:</c>,
/// <c>plugin:</c> and tool names are hard; <c>A:</c> and <c>O:</c> entries only warn, and stay unchecked because the
/// capability catalog's per-version status (catalog/coverage.json) is not installed with EplanMcp. Pure.
/// </summary>
internal static class PackCapabilities
{
    public const string Met = "met";
    public const string Missing = "missing";
    public const string Unknown = "unknown";
    public const string Unchecked = "unchecked";

    private const string NotConnected = "EPLAN is not connected; eplan_session_status says why";

    public static IReadOnlyList<CapabilityCheck> Check(PackCompatibility compatibility, HandshakeResult? eplan, IReadOnlyCollection<string> tools)
    {
        var checks = new List<CapabilityCheck>();
        if (compatibility.EplanVersions is { } versions)
        {
            bool fits = eplan is not null && EplanVersionRange.TryParse(versions, out EplanVersionRange? range) && range.Contains(eplan.Eplan.Version);
            checks.Add(new($"EPLAN {versions}", true, eplan is null ? Unknown : fits ? Met : Missing, eplan is null ? NotConnected : $"EPLAN {eplan.Eplan.Version} is connected"));
        }

        if (compatibility.Variants.Count > 0)
        {
            string? variant = eplan?.Eplan.Variant;
            checks.Add(new(
                $"variant {string.Join(" or ", compatibility.Variants)}",
                true,
                variant is null ? Unknown : compatibility.Variants.Contains(variant, StringComparer.OrdinalIgnoreCase) ? Met : Missing,
                eplan is null ? NotConnected : $"EPLAN runs as {variant ?? "a variant it does not report"}"));
        }

        foreach (string capability in compatibility.Capabilities)
        {
            checks.Add(capability switch
            {
                _ when capability.StartsWith("op:", StringComparison.Ordinal) => new(
                    capability,
                    true,
                    eplan?.Operations is not { } operations ? Unknown : operations.Contains(capability[3..], StringComparer.Ordinal) ? Met : Missing,
                    eplan is null ? NotConnected : eplan.Operations is null ? "the add-in reports no operations; install the current one" : null),
                _ when capability.StartsWith("licence:", StringComparison.Ordinal) => new(capability, true, Unknown, "the add-in does not report licence options yet"),
                _ when capability.StartsWith("plugin:", StringComparison.Ordinal) => new(capability, true, Missing, "code plugins (ADR-0025) are not available in this EplanMcp"),
                _ when capability.StartsWith("eplan_", StringComparison.Ordinal) => tools.Contains(capability)
                    ? new(capability, true, Met, null)
                    : new(capability, true, Missing, "this server does not offer the tool: it may need the full profile or a newer EplanMcp"),
                _ => new(capability, false, Unchecked, "only a warning: the capability catalog's status is not installed with EplanMcp"),
            });
        }

        return checks;
    }

    /// <summary>False when a hard requirement is missing, null when one is unknown, true otherwise.</summary>
    public static bool? AllMet(IReadOnlyList<CapabilityCheck> checks) =>
        checks.Any(c => c.Hard && c.Status == Missing) ? false
        : checks.Any(c => c.Hard && c.Status == Unknown) ? null
        : true;
}

using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// The numbering schemes of one settings node: terminals in the user settings, cables and wire numbers in the project
/// settings (spike S21, api-notes A24). An empty scheme runs EPLAN's last used one, whatever the user interface last
/// chose (A24), so these tools require a name and check it against the node before anything is backed up. Each node
/// also holds <see cref="Internal"/>, which is EPLAN's own and no scheme to choose (S21).
/// </summary>
public static class SchemeChoice
{
    /// <summary>The internal scheme S21 found under every numbering node.</summary>
    public const string Internal = "InternalRefDefScheme";

    /// <summary>The schemes to offer: the node's names in EPLAN's order, each once, without <see cref="Internal"/>.</summary>
    public static IReadOnlyList<string> Offered(IEnumerable<string> names) =>
        [.. names.Where(name => name.Length > 0 && !string.Equals(name, Internal, StringComparison.Ordinal)).Distinct(StringComparer.Ordinal)];

    /// <summary>
    /// <paramref name="scheme"/> when it is exactly one of the offered <paramref name="names"/>; otherwise INVALID_ARGUMENT
    /// with the offered ones as <c>metadata.candidates</c>, at most <see cref="NumberingSchemes.MaxCandidates"/>.
    /// <paramref name="what"/> names the numbering in the message, e.g. "terminal".
    /// </summary>
    public static string Require(string scheme, IEnumerable<string> names, string what)
    {
        IReadOnlyList<string> offered = Offered(names);
        return offered.Contains(scheme, StringComparer.Ordinal)
            ? scheme
            : throw Unknown(scheme, offered, what, cause: null);
    }

    /// <summary>
    /// INVALID_ARGUMENT for a scheme EPLAN itself refused with <c>InvalidScheme</c> while numbering, with the schemes its
    /// message names (S12, <see cref="NumberingSchemes.Candidates"/>).
    /// </summary>
    public static BridgeErrorException Refused(string scheme, string? message, string what, Exception cause) =>
        Unknown(scheme, NumberingSchemes.Candidates(message), what, cause);

    private static BridgeErrorException Unknown(string scheme, IReadOnlyList<string> candidates, string what, Exception? cause)
    {
        string message = candidates.Count == 0
            ? $"EPLAN has no {what} numbering scheme '{scheme}' and named none it has, so nothing was numbered. Ask the engineer which scheme to use."
            : $"EPLAN has no {what} numbering scheme '{scheme}', so nothing was numbered. Use one of metadata.candidates; letter case counts.";
        var metadata = new Dictionary<string, string> { ["scheme"] = scheme };
        if (candidates.Count > 0)
        {
            metadata["candidates"] = string.Join("\n", candidates.Take(NumberingSchemes.MaxCandidates));
        }

        BridgeErrorException error = cause is null ? new(ErrorReason.InvalidArgument, message) : new(ErrorReason.InvalidArgument, message, cause);
        error.Error.Metadata = metadata;
        return error;
    }
}

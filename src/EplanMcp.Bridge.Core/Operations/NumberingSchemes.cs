using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// EPLAN's numbering schemes as far as they can be learnt: no settings node for them was found, so a name is checked only
/// when EPLAN refuses it with <c>InvalidScheme</c>, whose message names the schemes it has (spike S12, api-notes A18).
/// </summary>
public static class NumberingSchemes
{
    public const int MaxCandidates = 20;

    /// <summary>
    /// The scheme names in an <c>InvalidScheme</c> message: S12's lists them in its last pair of single quotes, separated
    /// by ", ". The wording follows EPLAN's user interface language, so a message without such a pair gives none.
    /// </summary>
    public static IReadOnlyList<string> Candidates(string? message)
    {
        int close = message?.LastIndexOf('\'') ?? -1;
        int open = close > 0 ? message!.LastIndexOf('\'', close - 1) : -1;
        return open < 0
            ? []
            : [.. message!.Substring(open + 1, close - open - 1).Split([", "], StringSplitOptions.RemoveEmptyEntries).Select(name => name.Trim()).Where(name => name.Length > 0).Take(MaxCandidates)];
    }

    /// <summary>INVALID_ARGUMENT for a scheme EPLAN does not know, with the schemes its message names as candidates.</summary>
    public static BridgeErrorException Unknown(string? scheme, string? message, Exception cause)
    {
        IReadOnlyList<string> candidates = Candidates(message);
        var error = new BridgeErrorException(
            ErrorReason.InvalidArgument,
            $"EPLAN has no numbering scheme '{scheme}', so nothing was numbered. " + (candidates.Count == 0 ? "Omit 'scheme' for EPLAN's default." : "Use one of metadata.candidates, or omit 'scheme' for EPLAN's default."),
            cause);
        error.Error.Metadata = candidates.Count == 0
            ? new Dictionary<string, string> { ["scheme"] = scheme ?? string.Empty }
            : new Dictionary<string, string> { ["scheme"] = scheme ?? string.Empty, ["candidates"] = string.Join("\n", candidates) };
        return error;
    }
}

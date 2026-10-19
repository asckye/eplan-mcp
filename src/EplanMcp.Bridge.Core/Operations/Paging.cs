using System.Globalization;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// Slices a filtered, ordered list for a list operation (M2 design 4.3). Spike S6 showed that fetching everything
/// and filtering in memory stays far below a second on the sample project (api-notes A11).
/// </summary>
public static class Paging
{
    /// <summary>The largest page any list operation returns, whatever the Host asked for.</summary>
    public const int MaxLimit = 500;

    /// <summary>
    /// Returns items <c>[Offset, Offset + Limit)</c>. An offset past the end means the listing changed since the
    /// cursor was made; the caller hears that, rather than getting an empty page that looks like the end.
    /// </summary>
    public static List<T> Slice<T>(IReadOnlyList<T> all, ListArgs args) => Slice(all, args.Offset, args.Limit);

    /// <summary>The same for a listing without a project, such as EPLAN's system messages.</summary>
    public static List<T> Slice<T>(IReadOnlyList<T> all, int offset, int limit)
    {
        var violations = new List<string>();
        if (offset < 0)
        {
            violations.Add("'offset' must not be negative.");
        }

        if (limit is < 1 or > MaxLimit)
        {
            violations.Add($"'limit' must be between 1 and {MaxLimit.ToString(CultureInfo.InvariantCulture)}.");
        }

        if (violations.Count == 0 && offset > all.Count)
        {
            violations.Add("'cursor' is out of date: fewer items match now. Omit it to start from the first page.");
        }

        if (violations.Count > 0)
        {
            throw new BridgeErrorException(
                ErrorReason.InvalidArgument,
                "The paging arguments are invalid.",
                retryable: false,
                new Dictionary<string, string> { ["violations"] = string.Join(" ", violations) });
        }

        return [.. all.Skip(offset).Take(limit)];
    }
}

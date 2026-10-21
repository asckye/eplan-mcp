using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>What a numbering of terminals or cables renamed, as their operations read it back inside the transaction.</summary>
internal static class RenamedFunctions
{
    /// <summary>
    /// The functions whose full name changed, matched by session identifier, each pair once (the levels of a terminal and
    /// the functions of a cable can share a name); and how many functions of <paramref name="before"/> had no identifier
    /// left afterwards, which are counted rather than guessed at.
    /// </summary>
    public static (List<RenamedObject> Changed, int Unmatched) Between(IEnumerable<PlacedFunction> before, IEnumerable<PlacedFunction> after)
    {
        var now = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (PlacedFunction function in after)
        {
            now[function.Id] = function.Name;
        }

        var changed = new List<RenamedObject>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        int unmatched = 0;
        foreach (PlacedFunction function in before)
        {
            if (!now.TryGetValue(function.Id, out string? name))
            {
                unmatched++;
            }
            else if (!string.Equals(function.Name, name, StringComparison.Ordinal) && seen.Add(function.Name + "\n" + name))
            {
                changed.Add(new RenamedObject { From = function.Name, To = name });
            }
        }

        return (changed, unmatched);
    }
}

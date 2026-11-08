using System.Text.RegularExpressions;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Detached name evidence for interruption points whose project settings discard structure.</summary>
public sealed class InterruptionNameReadback(string requestedName, string storedName)
{
    public string RequestedName { get; } = requestedName;

    public string StoredName { get; } = storedName;

    public string? StructureWarning
    {
        get
        {
            string[] requested = StructureNames(RequestedName);
            var stored = new HashSet<string>(StructureNames(StoredName), StringComparer.Ordinal);
            if (requested.All(stored.Contains))
            {
                return null;
            }
            return "Interruption point '" + RequestedName + "' was stored as '" + StoredName
                + "': this project keeps interruption points without the requested structure, so it pairs with every '"
                + StoredName + "' point. Use a name that is unique without structure.";
        }
    }

    public bool KeepsDeviceWithoutStructure => StructureWarning is not null
        && DeviceName(RequestedName) == DeviceName(StoredName)
        && !StructureNames(StoredName).Except(StructureNames(RequestedName), StringComparer.Ordinal).Any();

    private static string[] StructureNames(string name) => Regex.Matches(name, @"(?:=+|\++)[^=+&#:-]+", RegexOptions.CultureInvariant)
        .Cast<Match>().Select(match => match.Value).ToArray();

    private static string DeviceName(string name)
    {
        int device = name.IndexOf('-');
        return device < 0 ? name : name.Substring(device + 1);
    }
}

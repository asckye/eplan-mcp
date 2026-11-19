using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Operations;

internal static class PlacementRename
{
    internal static string FullName(JObject input)
    {
        string name = InterruptionNames.Validate(input["name"], "/name", trim: true);
        if (name.Length is < 1 or > 128 || name.Any(char.IsWhiteSpace))
        { throw WriteArguments.Invalid(["/name: Supply 1..128 characters without whitespace or controls."]); }
        return FullName((string)input["page"]!, name);
    }

    private static string FullName(string page, string name) =>
        InterruptionNames.NativeName(page, InterruptionNames.Expand(page, name));

    internal static void Predict(JObject row, string page, string fullName, IReadOnlyDictionary<string, int>? counts)
    {
        if ((string?)row["kind"] != "InterruptionPoint" || (bool?)row["root"] != true)
        { throw WriteArguments.Invalid([$"rename requires an InterruptionPoint root placement; found {row["kind"]}."]); }
        string storedName = (string?)row["name"] ?? throw WriteArguments.Invalid(["Cannot read the interruption point's current name."]);
        string oldName = storedName.Length == 0 ? storedName : FullName(page, storedName);
        bool same = new InterruptionNameReadback(fullName, storedName).CarriesRequestedName;
        row["oldName"] = oldName;
        row["name"] = fullName;
        row["noOp"] = same;
        row["peersOld"] = Peers(oldName, true);
        row["peersNew"] = Peers(fullName, same);
        if (!same)
        { row["visibleName"] = null; }

        int? Peers(string name, bool includesSelf)
        {
            if (counts is null)
            { return null; }
            int count = counts.Where(entry => new InterruptionNameReadback(name, entry.Key).CarriesRequestedName)
                .Sum(entry => entry.Value);
            return Math.Max(0, count - (includesSelf ? 1 : 0));
        }
    }
}

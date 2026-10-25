using System.Text.Json;

namespace EplanMcp.Host.Plans;

/// <summary>
/// A device rename also rewrites references to the renamed functions: connection ends, calculated texts such as 20403
/// and the visible name (api-notes A45). Only the exact substitution of the planned names counts as derived.
/// </summary>
internal static class PlanDerivedRename
{
    /// <summary>Whether <paramref name="to"/> is <paramref name="from"/> with the old full device name replaced by the new one.</summary>
    public static bool Reference(PlanStep step, string property, string? from, string? to)
    {
        if (property is "exists" or "name" || from is null || to is null || Names(step) is not { } names)
        {
            return false;
        }
        string oldText = Escape(names.Old);
        return from.Contains(oldText, StringComparison.Ordinal)
            && from.Replace(oldText, Escape(names.New), StringComparison.Ordinal) == to;
    }

    /// <summary>
    /// Whether a visible name changed only in the device identifier after the last '-'; the visible name may omit
    /// structure segments (=F-KF1 for =F+A2-KF1), so a rename that also moves the device is not derivable.
    /// </summary>
    public static bool Visible(PlanStep step, string? from, string? to)
    {
        if (from is null || to is null || Names(step) is not { } names)
        {
            return false;
        }
        (string oldName, string newName) = names;
        int oldDash = oldName.LastIndexOf('-');
        int newDash = newName.LastIndexOf('-');
        if (oldDash < 0 || newDash < 0 || !string.Equals(oldName[..oldDash], newName[..newDash], StringComparison.Ordinal))
        {
            return false;
        }
        string oldId = Escape(oldName[oldDash..]) + "\"";
        string newId = Escape(newName[newDash..]) + "\"";
        return from.EndsWith(oldId, StringComparison.Ordinal) && to == from[..^oldId.Length] + newId;
    }

    private static (string Old, string New)? Names(PlanStep step) =>
        step.Op == "device.update" && step.PlannedChanges is [{ Name: { Length: > 0 } oldName, NewValue: { Length: > 0 } newName }]
            ? (oldName, newName)
            : null;

    // Snapshot property texts are raw JSON written with PlanJson.Options, so compare in the same escaping.
    private static string Escape(string text) => JsonSerializer.Serialize(text, PlanJson.Options)[1..^1];
}

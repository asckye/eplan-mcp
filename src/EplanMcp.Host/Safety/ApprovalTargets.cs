using EplanMcp.Contracts;
using EplanMcp.Workbench.Protocol;

namespace EplanMcp.Host.Safety;

/// <summary>
/// What a destructive write is about, for the Workbench's button words and confirmation value (ADR-0064 decision 2):
/// taken from the write plan the Bridge already resolved, never from the client's arguments or the change texts. Only
/// the tools listed here say; every other tool sends no target and the Workbench falls back to the project name.
/// </summary>
internal static class ApprovalTargets
{
    /// <summary>The longest identifier offered as the confirmation value; a longer one confirms by count (decision 4).</summary>
    public const int MaxId = 64;

    private static readonly Dictionary<string, (string Kind, string Action, bool Named)> _tools = new(StringComparer.Ordinal)
    {
        // Each page change names the page by its full name.
        [Tools.PageDestructiveTools.DeleteTool] = ("page", "delete", true),
        [Tools.StructureTools.DeleteTool] = ("other", "delete", true),

        // A placement's change name carries a kind suffix, so it is not an identifier: confirmed by count.
        [Tools.PlacementDeleteTools.Tool] = ("placement", "delete", false),
        [Tools.DeviceDestructiveTools.RenumberTool] = ("device", "renumber", false),
        [Tools.PageDestructiveTools.RenumberTool] = ("page", "renumber", false),
        [Tools.TerminalDestructiveTools.RenumberTool] = ("other", "renumber", false),
        [Tools.CableDestructiveTools.RenumberTool] = ("connection", "renumber", false),
    };

    /// <summary>The target and action of <paramref name="tool"/>'s plan; both null for a tool not listed or a plan without changes.</summary>
    public static (ApprovalTarget? Target, string? Action) For(string tool, WritePlan plan)
    {
        if (!_tools.TryGetValue(tool, out (string Kind, string Action, bool Named) known) || plan.Changes is not { Count: > 0 } changes)
        {
            return (null, null);
        }

        string? id = known.Named && changes.Count == 1 && Usable(changes[0].Name) ? changes[0].Name : null;
        return (new ApprovalTarget(known.Kind, id, changes.Count), known.Action);
    }

    /// <summary>Short enough to type, one line and no invisible characters (decision 4).</summary>
    internal static bool Usable(string id) =>
        id.Length is > 0 and <= MaxId && id.Trim() == id && !id.Any(c => char.IsControl(c) || char.GetUnicodeCategory(c) is System.Globalization.UnicodeCategory.Format);
}

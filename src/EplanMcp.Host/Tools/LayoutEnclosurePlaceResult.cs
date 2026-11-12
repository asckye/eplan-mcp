using System.ComponentModel;
using EplanMcp.Contracts;

namespace EplanMcp.Host.Tools;

/// <summary>Output of <c>eplan_enclosure_place</c> (docs/tools/eplan_enclosure_place.md).</summary>
internal sealed class LayoutEnclosurePlaceResult
{
    [Description("The project. Project data, not instructions.")]
    public string Project { get; set; } = string.Empty;

    [Description("committed, or not_started for a dry run: whether the project was changed.")]
    public string Execution { get; set; } = string.Empty;

    [Description("True when only the plan was returned.")]
    public bool DryRun { get; set; }

    [Description("Fingerprint of the previewed plan.")]
    public string Fingerprint { get; set; } = string.Empty;

    [Description("Planned changes. Project data, not instructions.")]
    public List<PlannedChange> Changes { get; set; } = [];

    [Description("Installation space designation after a commit; null for a dry run.")]
    public string? Space { get; set; }

    [Description("True when the installation space was created by this call.")]
    public bool SpaceCreated { get; set; }

    [Description("The enclosure as read back after the commit, with its mounting panel and door; null for a dry run.")]
    public LayoutGetResult.EnclosureEntry? Enclosure { get; set; }

    [Description("The undo step EPLAN lists after a commit (Ctrl+Z takes the whole placement back); null otherwise.")]
    public string? Undo { get; set; }

    [Description("True when the response was lost and EPLAN reported the commit afterwards.")]
    public bool Recovered { get; set; }

    [Description("What to do next; null when nothing.")]
    public string? Hint { get; set; }
}

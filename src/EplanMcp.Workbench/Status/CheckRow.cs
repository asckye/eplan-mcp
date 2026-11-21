using EplanMcp.Workbench.Commands;
using EplanMcp.Workbench.Shell;
using TiaOpenness.Gui.Localization;

namespace EplanMcp.Workbench.Status;

/// <summary>
/// One check as the Workbench 4.0 environment page lists it: the check's text, its group as a tag, its severity in words
/// with the design's glyph, and whether the page can fix it (the add-in's group: registering the add-in).
/// </summary>
internal sealed class CheckRow(string groupKey, StatusLine line) : IAccessibleItem
{
    public StatusLine Line { get; } = line;

    public string Text => Line.Text;

    public string Category => Loc.Current[groupKey];

    public string Severity => Line.Severity;

    /// <summary>A failure or warning needs handling; ok and info passed.</summary>
    public bool NeedsAction => Line.Status is CheckStatus.Failure or CheckStatus.Warning;

    public string Glyph => Line.Status switch { CheckStatus.Failure => "✕", CheckStatus.Warning => "!", CheckStatus.Ok => "✓", _ => "•" };

    public string Tone => Line.Status switch { CheckStatus.Failure => "Error", CheckStatus.Warning => "Warning", CheckStatus.Ok => "Success", _ => string.Empty };

    /// <summary>The add-in's checks can be fixed here: the button registers the add-in for the variants chosen.</summary>
    public bool CanFix => NeedsAction && groupKey == "Status.Group.AddIn";

    public string AccessibleName => Loc.Current.T("Status.RowName", Severity, Category, Text);
}

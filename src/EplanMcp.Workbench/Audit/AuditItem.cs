using System.Globalization;
using EplanMcp.Workbench.Approvals;
using EplanMcp.Workbench.Shell;
using TiaOpenness.Gui.Localization;

namespace EplanMcp.Workbench.Audit;

/// <summary>
/// A record as a row of the audit list (CTL-05, CTL-06): its texts in the current language, every value from the log
/// made plain first (CTL-12, APR-08).
/// </summary>
internal sealed class AuditItem(AuditRecord record, DateTimeOffset? localTime, int seq = 0) : IAccessibleItem
{
    public AuditRecord Record { get; } = record;

    public string Time => localTime?.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? string.Empty;

    /// <summary>The record's number in the chain, "#" and the number; empty when unknown.</summary>
    public string Seq => seq > 0 ? "#" + seq.ToString(CultureInfo.InvariantCulture) : string.Empty;

    /// <summary>The Workbench 4.0 first column: the number, then the time of day.</summary>
    public string SeqTime => (Seq.Length > 0 ? Seq + " · " : string.Empty) + (localTime?.ToString("HH:mm:ss", CultureInfo.InvariantCulture) ?? string.Empty);

    public string Kind => Record.Kind is { } kind ? AuditView.Label("Audit.Kind.", kind) : string.Empty;

    public string Tool => ProjectText.Line(Record.Tool ?? string.Empty);

    public string Project => ProjectText.Line(Record.Project ?? string.Empty);

    /// <summary>The tool and its project, as the table's "tool and target" column.</summary>
    public string Target => Project.Length == 0 ? Tool : Tool.Length == 0 ? Project : Tool + " · " + Project;

    /// <summary>The decision of an approval or release, the result of a write; empty for the other records.</summary>
    public string Result => (Record.Decision, Record.Execution) switch
    {
        ({ } decision, _) => AuditView.Label("Audit.Decision.", decision),
        (_, { } execution) => AuditView.Label("Audit.Execution.", execution),
        _ => string.Empty,
    };

    /// <summary>The kind and, when there is one, the result, for the inspector.</summary>
    public string Detail => Result.Length == 0 ? Kind : Kind + " · " + Result;

    /// <summary>The event tag's tone: refused, cut short or unknown in the error colour, done in the success colour.</summary>
    public string Tone => (Record.Decision, Record.Execution) switch
    {
        ("denied" or "expired" or "cancelled" or "unavailable", _) => "Error",
        (_, "unknown" or "rolled_back") => "Error",
        ("granted", _) or (_, "committed") => "Success",
        _ => Record.Kind is "writes_suspended" ? "Warning" : string.Empty,
    };

    /// <summary>The tone's glyph before the event: ✓, ✕, ! or •.</summary>
    public string ToneGlyph => Tone switch { "Error" => "✕", "Success" => "✓", "Warning" => "!", _ => "•" };

    /// <summary>The record's hash, shortened as the design shows it: the first and last four hex digits.</summary>
    public string Hash => Short(Record.Field("hash"));

    /// <summary>The previous record's hash, shortened.</summary>
    public string Prev => Short(Record.Field("prev"));

    /// <summary>Who caused the record: the client the Host named, the approval channel or the remote address; else empty.</summary>
    public string Source => ProjectText.Line(Record.Field("client") ?? Record.Field("channel") ?? Record.Field("sourceAddress") ?? string.Empty, 48);

    public string AccessibleName => Loc.Current.T("Audit.RowName", Time, Kind, Tool, Project, Result);

    private static string Short(string? hash) => hash is { Length: > 8 } h ? ProjectText.Line(h[..4] + "…" + h[^4..]) : ProjectText.Line(hash ?? string.Empty);
}

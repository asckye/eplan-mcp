namespace EplanMcp.Contracts;

/// <summary>A function's persistent identity and placement; never a session handle (A32/A34).</summary>
public sealed class PlanFunction
{
    /// <summary>Always <c>function</c>.</summary>
    public string Kind { get; set; } = "function";

    /// <summary>The type number, as in <see cref="SnapshotObject.Type"/>.</summary>
    public int Type { get; set; }

    /// <summary>The project's ObjectIdentifier.</summary>
    public long Oid { get; set; }

    /// <summary>The exact full name, or empty for an unnamed function.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Whether this is a main function; multiple mains with one name remain separate entries.</summary>
    public bool IsMain { get; set; }

    /// <summary>The exact page name, or null for an unplaced function.</summary>
    public string? Page { get; set; }
}

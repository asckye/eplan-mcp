namespace EplanMcp.Bridge.Core.Operations;

public interface IUnplacedFunctionRemover
{
    /// <summary>Every function whose full name is one of the tags or starts with a tag followed by ':', placed or not.</summary>
    public IReadOnlyList<NamedFunctionRow> Find(string project, IReadOnlyList<string> tags);

    /// <summary>Removes exactly these functions inside the caller's transaction; PLAN_STALE if one is gone or placed now.</summary>
    public Action Prepare(string project, IReadOnlyList<NamedFunctionRow> rows);
}

public sealed class NamedFunctionRow
{
    public string Name { get; set; } = "";
    public int Type { get; set; }
    public long Oid { get; set; }
    public string Category { get; set; } = "";
    public bool Main { get; set; }
    public bool Placed { get; set; }
    public string? Page { get; set; }
}

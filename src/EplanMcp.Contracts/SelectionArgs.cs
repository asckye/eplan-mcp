namespace EplanMcp.Contracts;
/// <summary>UI selection request; null mode reads, other modes never change project data.</summary>
public sealed class SelectionArgs
{
    /// <summary>Checks the entire bounded locator batch before any UI access.</summary>
    public static bool Valid(IReadOnlyList<DrawingLocator>? locators, string mode) => locators is not null && locators.Count <= 200 && mode is "replace" or "add" or "clear" && (mode == "clear" ? locators.Count == 0 : locators.Count > 0)
        && locators.All(l => l is not null && !string.IsNullOrWhiteSpace(l.Project) && !string.IsNullOrWhiteSpace(l.Page) && l.Type > 0 && l.Oid > 0 && l.Digest is { Length: 64 } && l.Digest.All(c => c is (>= '0' and <= '9') or (>= 'a' and <= 'f')))
        && locators.All(l => string.Equals(l.Project, locators[0].Project, StringComparison.OrdinalIgnoreCase) && l.Page == locators[0].Page)
        && locators.Select(l => (l.Type, l.Oid)).Distinct().Count() == locators.Count
        && locators.Sum(l => (long)l.Project.Length + l.Page.Length + l.Digest.Length + 100) <= 20000;
    /// <summary>Null, replace, add or clear.</summary>
    public string? Mode { get; set; }
    /// <summary>Exact discovery identities, all on one page.</summary>
    public List<DrawingLocator> Locators { get; set; } = [];
    /// <summary>Open or focus the target page before selecting.</summary>
    public bool ShowPage { get; set; }
    /// <summary>Internal read continuation position.</summary>
    public int Offset { get; set; }
}

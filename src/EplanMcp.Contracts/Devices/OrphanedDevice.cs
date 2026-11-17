namespace EplanMcp.Contracts;

/// <summary>
/// A device whose main function is on a page being deleted while functions of its tag stay on other pages, without a
/// main function afterwards (spike S12: a cable's cores), as in <see cref="PageDeleteData.Orphaned"/>. Names are
/// project data.
/// </summary>
public sealed class OrphanedDevice
{
    /// <summary>The device's full tag.</summary>
    public string Tag { get; set; } = string.Empty;

    /// <summary>How many of its functions are left on other pages.</summary>
    public int FunctionsLeft { get; set; }

    /// <summary>The pages they are on, in EPLAN's order, at most 10.</summary>
    public List<string> Pages { get; set; } = [];
}

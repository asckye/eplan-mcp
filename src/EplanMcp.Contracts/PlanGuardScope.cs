namespace EplanMcp.Contracts;

/// <summary>Exact intersecting filters; all empty means the empty set, never the whole project.</summary>
public sealed class PlanGuardScope
{
    /// <summary>Exact full page names, at most 50.</summary>
    public List<string> Pages { get; set; } = [];

    /// <summary>Structure segment prefixes, not arbitrary text prefixes.</summary>
    public List<string> Structures { get; set; } = [];

    /// <summary>Exact persistent locators, at most 200 explicit locators per guard.</summary>
    public List<ObjectLocator> Locators { get; set; } = [];
}

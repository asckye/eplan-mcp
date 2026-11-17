namespace EplanMcp.Contracts;

/// <summary>Arguments of <see cref="OperationNames.ObjectResolve"/>.</summary>
public sealed class ObjectResolveArgs
{
    /// <summary>The open project as eplan_project_list gives it; omitted means the first locator's project.</summary>
    public string? Project { get; set; }

    /// <summary>The locators to check, 1 to 200.</summary>
    public List<ObjectLocator> Locators { get; set; } = [];
}

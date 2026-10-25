namespace EplanMcp.Contracts;

/// <summary>One project, exact page, main function (optionally on a page), or existing locator (design 4.1).</summary>
public sealed class PropertyTarget
{
    /// <summary>Checks only the address union; object identity and locator checks belong to pure validation.</summary>
    public PropertyTarget(bool? projectObject = null, string? page = null, string? deviceTag = null, ObjectLocator? locator = null)
    {
        int branches = (projectObject is not null ? 1 : 0)
            + (deviceTag is not null || page is not null ? 1 : 0)
            + (locator is not null ? 1 : 0);
        if (branches != 1 || projectObject == false)
        {
            throw new ArgumentException("Specify one property target: project, page, device tag with optional page, or locator.");
        }

        ProjectObject = projectObject;
        Page = page;
        DeviceTag = deviceTag;
        Locator = locator;
    }

    /// <summary>True selects the top-level project; false is never a valid address.</summary>
    public bool? ProjectObject { get; }

    /// <summary>An exact page full name, or the page qualifier of a device tag.</summary>
    public string? Page { get; }

    /// <summary>An exact device full name selecting one main function; ambiguity must be reported.</summary>
    public string? DeviceTag { get; }

    /// <summary>An existing page/function/connection locator; its identity must be checked before use.</summary>
    public ObjectLocator? Locator { get; }
}

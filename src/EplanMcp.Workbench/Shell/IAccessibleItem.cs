namespace EplanMcp.Workbench.Shell;

/// <summary>
/// A list row that names itself for UI Automation (workbench-ui CTL-05): Primer's list item styles read the name through
/// <c>Primer.AccessibleName</c>, which would otherwise give the row no name. The name comes from the catalogue, in the
/// current language.
/// </summary>
internal interface IAccessibleItem
{
    public string AccessibleName { get; }
}

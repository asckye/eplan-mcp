namespace EplanMcp.Workbench.Shell;

/// <summary>
/// A list row that names itself for UI Automation (docs/design/2026-10-20-m5-operability.md#workbench-implementation): Primer's list item styles read the name through
/// <c>Primer.AccessibleName</c>, which would otherwise give the row no name. The name comes from the catalogue, in the
/// current language.
/// </summary>
internal interface IAccessibleItem
{
    public string AccessibleName { get; }
}

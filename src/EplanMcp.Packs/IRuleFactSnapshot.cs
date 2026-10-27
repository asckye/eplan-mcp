namespace EplanMcp.Packs;

/// <summary>An immutable cabinet snapshot. Lookups must be bounded, in memory, and use stable locators.</summary>
public interface IRuleFactSnapshot
{
    public IReadOnlyList<string> Objects(string kind);
    public RuleValue? GetProperty(string locator, string name);
    public RuleRelation Relation(string locator, string relation);
}

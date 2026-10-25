namespace EplanMcp.Bridge.Core.Operations;

/// <summary>The complete function reader plus page names and auxiliary identity, in the same main-thread call.</summary>
public interface IPlanNameReader : IPlanFunctionReader
{
    /// <summary>One finder/placement enumeration; checkpoint before/after native calls and per page/batch.</summary>
    public PlanNameInventory ReadNames(string projectPath, Action checkpoint);
}

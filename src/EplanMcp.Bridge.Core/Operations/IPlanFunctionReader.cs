using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>All functions, including unplaced and repeated mains. Main thread inside the dispatcher's LockingStep.</summary>
public interface IPlanFunctionReader
{
    /// <summary>Enumerates each function once, with no filtering, grouping or truncation; returns only persistent DTOs.</summary>
    public IReadOnlyList<PlanFunction> Read(string projectPath);
}

using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Fresh, complete metadata for guard candidates, within the caller's main-thread budget.</summary>
public interface IPlanGuardFunctionReader : IPlanFunctionReader
{
    /// <summary>
    /// Includes exact names, case variants and colon suffixes, including extra representations and unplaced functions.
    /// A reader unable to prove targeted completeness must fall back to the full merged enumeration, checkpointing
    /// before/after native calls and per page/batch. Candidates are literal full names, never query patterns.
    /// </summary>
    public IReadOnlyList<PlanFunction> ReadForGuard(string projectPath, IReadOnlyList<string> candidates, Action checkpoint);
}

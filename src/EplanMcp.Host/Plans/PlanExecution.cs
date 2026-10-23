namespace EplanMcp.Host.Plans;

/// <summary>ADR-0023's plan results. Single-step Contracts.ExecutionState remains a separate four-value contract.</summary>
internal enum PlanExecution
{
    NotStarted,
    RolledBack,
    Committed,
    PartiallyCommitted,
    Unknown,
}

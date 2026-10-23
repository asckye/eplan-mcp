namespace EplanMcp.Host.Plans;

/// <summary>The versioned outer envelope; only Definition participates in PlanHash (ADR-0028).</summary>
internal sealed record PlanRecord(int SchemaVersion, string PlanHash, PlanDefinition Definition, PlanState State);

namespace EplanMcp.Packs.Knowledge;

/// <summary>
/// Versioned knowledge documents (ADR-0029). Only the original four kinds are writable by asset_update;
/// facts come only from extraction, and only an engineer makes a rule official.
/// </summary>
public enum KnowledgeKind
{
    /// <summary>An inference from facts, as a proposal: a rules slot entry with the standing candidate.</summary>
    Candidate,

    /// <summary>A parameterised module's metadata over a macro of the pack (design 4.10).</summary>
    Module,

    /// <summary>Parameter values to parts: a mappings slot entry with the standing candidate.</summary>
    MaterialMapping,

    /// <summary>Missing material, or what needs EPLAN or an engineer to settle.</summary>
    Gap,
    ConfigSchema,
    ConfigLayer,
    FactSet,
    Rule,
    Workflow,
    Sample,
}

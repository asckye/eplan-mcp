namespace EplanMcp.Packs.Knowledge;

/// <summary>
/// The knowledge documents EplanMcp and agents write into a development pack (M6 design 4.1.3, 4.2). None is a fact or
/// an official rule: facts come only from extraction, and only an engineer makes a rule official.
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
}

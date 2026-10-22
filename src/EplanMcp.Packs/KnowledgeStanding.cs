namespace EplanMcp.Packs;

/// <summary>
/// How far a rule, mapping or other piece of knowledge is confirmed. Measured facts are kept apart, in
/// <see cref="PackManifest.Facts"/>.
/// </summary>
public enum KnowledgeStanding
{
    /// <summary>An engineer confirmed it as the company's rule; agents follow it.</summary>
    Official,

    /// <summary>Inferred, by EplanMcp or an AI agent, and not confirmed yet; agents treat it as a suggestion only.</summary>
    Candidate,
}

namespace EplanMcp.Packs.Scanning;

public enum FindingSeverity
{
    /// <summary>The input cannot become a correct pack as it is; nothing is written while one remains.</summary>
    Error,

    /// <summary>The pack can be built, but an engineer should look: a guess, an unusual file.</summary>
    Warning,

    /// <summary>What EplanMcp cannot tell without EPLAN (or a 7z reader): left for a check in EPLAN.</summary>
    Gap,
}

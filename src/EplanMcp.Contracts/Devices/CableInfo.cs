namespace EplanMcp.Contracts;

/// <summary>A cable in <see cref="CableGetData"/>. The texts are EPLAN's display texts, never parsed; every string is project data.</summary>
public sealed class CableInfo
{
    /// <summary>The full tag (<c>Function.Name</c>), e.g. <c>+B1-WG2</c>.</summary>
    public string Tag { get; set; } = string.Empty;

    /// <summary>The tag shown on the page without '&gt;' markers; null when none is shown.</summary>
    public string? VisibleTag { get; set; }

    /// <summary>Full name of the page the cable is placed on; null when it is not placed.</summary>
    public string? Page { get; set; }

    /// <summary><c>FUNC_CABLETYPE</c>; null when not set.</summary>
    public string? Type { get; set; }

    /// <summary><c>FUNC_CABLEWIRECOUNT</c>; null when not set.</summary>
    public string? WireCount { get; set; }

    /// <summary><c>FUNC_CABLEWIRECROSSSECTION</c>, in regional format such as <c>1,5</c>; null when not set.</summary>
    public string? CrossSection { get; set; }

    /// <summary><c>FUNC_CABLELENGTH_FULL</c>; null when not set.</summary>
    public string? Length { get; set; }

    /// <summary>Names of the cable's sources (<c>Cable.GetSourcesAndTargets</c>), once each, at most 20.</summary>
    public List<string> Sources { get; set; } = [];

    /// <summary>Names of the cable's targets, once each, at most 20; a terminal keeps its suffix such as <c>:2</c>.</summary>
    public List<string> Targets { get; set; } = [];

    /// <summary>The part numbers of the cable's part references.</summary>
    public List<string> Parts { get; set; } = [];
}

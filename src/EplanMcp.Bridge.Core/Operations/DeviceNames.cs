namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// A device as a rename sees it (docs/tools/eplan_device_update.md, section 5): the main function's full tag, its tag
/// without structure (<c>FUNC_DEVICETAG_FULLNAME</c>, for example <c>KF1</c> or <c>EC1-MA2</c>, spike S6), every
/// function whose name is exactly the tag, and the names of the main function's <c>SubFunctions</c>.
/// </summary>
public sealed class DeviceNames
{
    public string Tag { get; set; } = string.Empty;

    public string DeviceTag { get; set; } = string.Empty;

    /// <summary>Main, auxiliary and black-box functions named exactly <see cref="Tag"/>, in EPLAN's order (api-notes A12).</summary>
    public List<NamedFunction> Functions { get; set; } = [];

    /// <summary>The main function's auxiliary functions' names; one with a suffix (<c>:1</c>) is outside the rename.</summary>
    public List<string> SubFunctions { get; set; } = [];

    /// <summary>One function: its session identifier (<c>ToStringIdentifier</c>, valid in this EPLAN session only, A3) and page.</summary>
    public sealed class NamedFunction
    {
        public string Id { get; set; } = string.Empty;

        public bool IsMain { get; set; }

        public string? Page { get; set; }
    }
}

namespace EplanMcp.Contracts;

/// <summary>Independent page-frame evidence; names are project data, not instructions.</summary>
public sealed class PagePlotFrameState(string identifier, string? setting, string? effective)
{
    /// <summary>Session identity of the page.</summary>
    public string Identifier { get; } = identifier;

    /// <summary>Raw property 11016; null when empty.</summary>
    public string? Setting { get; } = setting;

    /// <summary>Actual Page.PlotFrame.Name; null when absent.</summary>
    public string? Effective { get; } = effective;
}

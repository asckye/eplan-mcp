using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Read-only text evidence, including protected frame texts; never used to construct a write plan.</summary>
public interface IPageTextInspector
{
    /// <summary>Complete evidence for each explicitly named page; incomplete enumeration or reads throw.</summary>
    public IReadOnlyList<PageTextInspection> Inspect(string projectPath, IReadOnlyList<string> pages);
}

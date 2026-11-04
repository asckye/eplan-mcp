using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Main-thread primitive adapter; public methods return detached values only (A54/A55).</summary>
public interface IPlacementTextWriter
{
    /// <summary>Checks the page and ISO codes; only a missing display language may return a warning when requested.</summary>
    public string? Validate(string projectPath, string page, PlacementText text, bool warn = false);
    /// <summary>Creates and reads back one text inside the caller's transaction; returns detached values only.</summary>
    public PlacementText Create(string projectPath, string page, PlacementText text);
    /// <summary>Creates all primitives, then reads every item inside the caller's transaction; returns detached values only.</summary>
    public List<PlacementText> CreateBatch(string projectPath, string page, IReadOnlyList<PlacementText> items);
}

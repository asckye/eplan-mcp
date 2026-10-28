using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Main-thread primitive adapter; public methods return detached values only (A54/A55).</summary>
public interface IPlacementTextWriter
{
    /// <summary>Before beginning a write, checks the exact Circuit page, ISO codes and project display language.</summary>
    public void Validate(string projectPath, string page, PlacementText text);
    /// <summary>Creates and reads back one text inside the caller's transaction; returns detached values only.</summary>
    public PlacementText Create(string projectPath, string page, PlacementText text);
    /// <summary>Creates all primitives, then reads every item inside the caller's transaction; returns detached values only.</summary>
    public List<PlacementText> CreateBatch(string projectPath, string page, IReadOnlyList<PlacementText> items);
}

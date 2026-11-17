using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// Reads and changes a project's structure identifiers (EPLAN calls them locations), implemented by the EPLAN adapter
/// layer. Main thread only; the changes only inside the write's transaction. EPLAN refusing one is EPLAN_OPERATION_FAILED.
/// </summary>
public interface IStructureIdentifiers
{
    /// <summary>Every identifier of the project in EPLAN's order, without the empty one each block has.</summary>
    public IReadOnlyList<StructureIdentifier> Read(string projectPath);

    public void Create(string projectPath, string name);

    public void Describe(string projectPath, string name, string description);

    public void Remove(string projectPath, string name);

    /// <summary>Puts <paramref name="names"/> first in their block, all with the same prefix, and the block's others after them.</summary>
    public void Sort(string projectPath, IReadOnlyList<string> names);

    /// <summary>Renames an identifier, with everything in the project that uses it.</summary>
    public void Rename(string projectPath, string name, string newName);
}

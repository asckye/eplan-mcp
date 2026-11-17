using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Main-thread project-library reads inside LockingStep, outside writes; A52 forbids container disposal.</summary>
public interface ISymbolReader
{
    /// <summary>Reads matching variants; filters metadata before reading connection points.</summary>
    public List<SymbolSearchItem> Read(string projectPath, SymbolSearchArgs args);
}

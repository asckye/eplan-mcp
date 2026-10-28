using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Main-thread read-only reference discovery; no native objects may escape the invocation.</summary>
public interface IModuleInspectionReader
{
    public ModuleInspectData Read(ModuleInspectArgs args);
}

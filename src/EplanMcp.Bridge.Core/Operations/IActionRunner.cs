using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Main-thread action adapter; Inspect borrows data objects, Run creates one typed context.</summary>
public interface IActionRunner
{
    public ActionRunData Inspect(string entry, string? projectPath, IReadOnlyDictionary<string, string> parameters);
    public bool Run(string action, IReadOnlyDictionary<string, string> parameters);
}

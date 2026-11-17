namespace EplanMcp.Bridge.Core.Operations;

/// <summary>S34 generate/CABLES with fixed safety parameters. Main thread; Generate only inside TransactionScope.</summary>
public interface ICableGenerator
{
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Schemes(string projectPath);

    /// <summary>Required reads must fail rather than return an incomplete snapshot (tool spec 5).</summary>
    public CableGenerationSnapshot Read(string projectPath);

    public void Generate(string projectPath, string creationScheme, string numberingScheme, string selectionScheme);
}

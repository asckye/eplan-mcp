using Eplan.EplApi.Base.Enums;

namespace Eplan.EplApi.DataModel;

internal sealed class FunctionsFilter : IDisposable
{
    public Page? Page { get; set; }
    public string? Name { get; set; }
    public bool ExactNameMatching { get; set; }
    public FunctionCategory? FunctionCategory { get; set; }
    public void Dispose() { }
}

internal sealed partial class ArticleReference
{
    public string? PartNr { get; set; }
}

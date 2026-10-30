using Eplan.EplApi.Base.Enums;

namespace Eplan.EplApi.DataModel;

internal sealed class FunctionsFilter : IDisposable
{
    public string? Name { get; set; }
    public bool ExactNameMatching { get; set; }
    public FunctionCategory? FunctionCategory { get; set; }
    public void Dispose() { }
}

internal sealed class ArticleReference
{
    public string? PartNr { get; set; }
}

internal sealed class Pin
{
    public Function? ParentFunction { get; set; }
    public string? Name { get; set; }
}

internal class Connection
{
    public Pin? StartPin { get; set; }
    public Pin? EndPin { get; set; }
    public Page? Page { get; set; }
}

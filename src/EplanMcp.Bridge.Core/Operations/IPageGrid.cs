namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Page.GridSize in millimetres; writes run inside the page transaction.</summary>
public interface IPageGrid
{
    public double ReadGrid(string projectPath, string page);
    public double WriteGrid(string projectPath, string page, double value);
}

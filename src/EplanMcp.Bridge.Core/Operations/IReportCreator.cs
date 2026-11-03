using EplanMcp.Contracts;
namespace EplanMcp.Bridge.Core.Operations;

/// <summary>A84 report creation and fresh page snapshots; main thread, creation inside one transaction.</summary>
public interface IReportCreator
{
    public IReadOnlyList<ReportPageRecord> ReportPages(string projectPath);
    public void Create(string projectPath, ReportCreateArgs args);
}

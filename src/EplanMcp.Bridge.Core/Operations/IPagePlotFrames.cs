using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Main-thread frame access; writes only inside TransactionScope. No native objects survive a request.</summary>
public interface IPagePlotFrames : IPageDescriptions, IDisposable
{
    /// <summary>Distinct exact names observed on project pages; throws if enumeration is incomplete.</summary>
    public IReadOnlyList<string> Available(string projectPath);

    /// <summary>Read page identity, raw 11016 and effective frame independently of write results.</summary>
    public new PagePlotFrameState Read(string projectPath, string page);

    /// <summary>Assign 11016 using the S38 probe path and return independent readback.</summary>
    public PagePlotFrameState Write(string projectPath, string page, string frame);
}

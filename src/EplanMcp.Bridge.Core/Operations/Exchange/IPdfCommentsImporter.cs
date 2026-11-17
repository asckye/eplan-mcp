using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>S44's Comment enumeration and fixed PDFCOMMENTS action; main thread only.</summary>
public interface IPdfCommentsImporter
{
    public IReadOnlyList<string> Identifiers(string projectPath);

    /// <summary>Only inside TransactionScope. False means EPLAN rejected the PDF.</summary>
    public bool Import(string projectPath, string filePath);

    public List<PdfComment> Read(string projectPath, IReadOnlyList<string> identifiers);
}

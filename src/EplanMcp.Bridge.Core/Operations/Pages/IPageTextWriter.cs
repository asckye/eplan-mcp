namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Page free-text reads and writes (A49); called on the main thread, writes only inside TransactionScope.</summary>
public interface IPageTextWriter : IPageTextReader
{
    /// <summary>
    /// Finds the exact page-origin free text by its oid and session identity, replaces all stored languages,
    /// and reads its complete identity and languages back. Missing objects or incomplete reads throw.
    /// </summary>
    public PageFreeText Write(string projectPath, string pageName, PageFreeText target, IReadOnlyList<TextEntry> entries);
}

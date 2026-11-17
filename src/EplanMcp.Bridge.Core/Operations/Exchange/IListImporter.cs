namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Project list imports verified in S34 (A39). Main thread; Import only inside TransactionScope.</summary>
public interface IListImporter
{
    /// <summary>Re-enumerates counts, entry part numbers and readable part/reference fields; never retains EPLAN objects.</summary>
    public ListImportState Read(string projectPath);

    /// <summary>Runs devicelist or partslist, TYPE=IMPORT, with the fixed converter; false means failure.</summary>
    public bool Import(string projectPath, string kind, string filePath, string converter);
}

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>PLC import and complete identity/count read-back (S34, api-notes A39). Main thread only.</summary>
public interface IPlcImporter
{
    /// <summary>All functions and PLC boxes by object number, merged by identity; throws if any required read fails.</summary>
    public PlcImportState Read(string projectPath);

    /// <summary>Runs plcservice/BUSDATAIMPORT with IMPORTMATCH=0 and LANGUAGE=de_DE inside the write's transaction.</summary>
    public bool Import(string projectPath, string filePath, string converter);
}

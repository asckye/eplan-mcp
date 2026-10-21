namespace EplanMcp.Bridge.Core.Operations;

/// <summary>What an export of a whole project makes: one file (docs/tools/eplan_export_pxf.md, eplan_export_parts.md).</summary>
public enum ProjectExportKind
{
    Pxf,
    PartsList,
}

/// <summary>
/// Exports a whole project into one file, implemented by the EPLAN adapter layer (the tool specs, section 5; api-notes
/// A25). Main thread only. Files are full paths in the folder the Host created. A failed export throws
/// EPLAN_OPERATION_FAILED.
/// </summary>
public interface IProjectExporter
{
    /// <summary>
    /// Exports the project, connections included, as one EPJ (PXF) file; <paramref name="file"/> has no extension,
    /// which EPLAN adds (S25). <paramref name="includeMasterData"/> adds the project's master data.
    /// </summary>
    public void ExportPxf(string projectPath, string file, bool includeMasterData);

    /// <summary>Exports the project's parts list as XML to <paramref name="file"/>, which ends with <c>.xml</c> (S25).</summary>
    public void ExportPartsList(string projectPath, string file);
}

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// Imports an EPJ (PXF) file as a new project, implemented by the EPLAN adapter layer (docs/tools/eplan_import_pxf.md,
/// section 5). Main thread only.
/// </summary>
public interface IProjectImporter
{
    /// <summary>
    /// Imports the file at <paramref name="filePath"/> as a new project at <paramref name="target"/>, a full path
    /// without extension: S22 found that <c>Import.PXFProject</c> takes the new project's path there, not a folder as
    /// the XML docs say (api-notes A29). The project is not opened. Takes about a minute for the sample project. Throws
    /// EPLAN_OPERATION_FAILED when EPLAN refuses the file.
    /// </summary>
    public void ImportPxf(string filePath, string target);
}

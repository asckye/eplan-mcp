using EplanMcp.Bridge.Core.Operations;

namespace EplanMcp.Bridge.Core.Tests;

/// <summary>
/// Exports of a whole project that write files the way S25 found EPLAN writes them (api-notes A25): PXF adds
/// <c>.epj</c> to the name and leaves an empty folder of that name next to the file; the parts list is the file named.
/// Records every export in
/// <see cref="Exports"/>; <see cref="During"/> runs inside each export, as a cancel arriving meanwhile would.
/// </summary>
internal sealed class FakeProjectExporter : IProjectExporter
{
    /// <summary>Thrown by every export, after it wrote its files.</summary>
    public Exception? Failure { get; set; }

    public Action? During { get; set; }

    public List<string> Exports { get; } = [];

    public void ExportPxf(string projectPath, string file, bool includeMasterData)
    {
        Exports.Add($"pxf {projectPath} {Path.GetFileName(file)} {includeMasterData}");
        During?.Invoke();
        File.WriteAllText(file + ".epj", "pxf");
        Directory.CreateDirectory(file);
        ThrowIfFailing();
    }

    public void ExportPartsList(string projectPath, string file)
    {
        Exports.Add($"parts {projectPath} {Path.GetFileName(file)}");
        During?.Invoke();
        File.WriteAllText(file, "<parts/>");
        ThrowIfFailing();
    }

    private void ThrowIfFailing()
    {
        if (Failure is not null)
        {
            throw Failure;
        }
    }
}

using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.HEServices;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>
/// Imports a DXF or DWG file onto a page the way spike S22 did in the dispatcher (api-notes A29):
/// <c>new Import().DXFPage(file, page, new Transformation(file, page, "", scaling, position, false), "")</c>, and counts
/// <c>Page.AllPlacements</c> before and after. The empty scheme is EPLAN's default import scheme, as in S22; the same
/// scheme fills the transformation, as the XML docs ask. Main thread; <see cref="ImportDxf"/> inside the write's
/// TransactionScope, whose SafetyPoint takes the import back (S22).
/// </summary>
internal sealed class EplanDrawingImporter : IDrawingImporter
{
    public MacroTargetPage? ReadPage(string projectPath, string pageName) => new EplanMacroInserter().ReadPage(projectPath, pageName);

    public int ImportDxf(string projectPath, string pageName, string filePath, bool fit, bool center)
    {
        MainThread.Assert();
        Project project = EplanProjects.ByPath(projectPath);
        Page page = Find(project, pageName) ?? throw new InvalidOperationException("The planned page is gone.");
        int before = page.AllPlacements.Length;
        try
        {
            using var transformation = new Transformation(
                filePath,
                page,
                string.Empty,
                fit ? Transformation.ScalingMode.Automatic : Transformation.ScalingMode.None,
                center ? Transformation.PositionMode.Center : Transformation.PositionMode.LowerLeft,
                false);
            using var import = new Import();
            import.DXFPage(filePath, page, transformation, string.Empty);
        }
        catch (Exception ex) when (ex is BaseException or ArgumentException or ApplicationException && new EplanExceptionMapper().Map(ex) is null)
        {
            var failed = new BridgeErrorException(
                ErrorReason.EplanOperationFailed,
                "EPLAN could not import the drawing, so nothing was changed. metadata.eplanMessage has EPLAN's message; the file may be damaged or of a version EPLAN does not read.",
                ex);
            failed.Error.Metadata = new Dictionary<string, string> { ["eplanMessage"] = EplanConnectionGenerator.ForModel(ex.Message) };
            throw failed;
        }

        return page.AllPlacements.Length - before;
    }

    /// <summary>The page whose full name is exactly <paramref name="pageName"/>: EPLAN's exact name filter (S6), checked ordinally.</summary>
    private static Page? Find(Project project, string pageName)
    {
        using var finder = new DMObjectsFinder(project);
        using var filter = new PagesFilter { Name = pageName, ExactNameMatching = true };
        return finder.GetPages(filter).FirstOrDefault(p => string.Equals(p.Name, pageName, StringComparison.Ordinal));
    }
}

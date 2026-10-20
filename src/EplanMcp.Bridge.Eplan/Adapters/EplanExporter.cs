using System.Collections;
using Eplan.EplApi.ApplicationFramework;
using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.HEServices;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>
/// Exports the way spikes S10 and S11 did (api-notes A14, A16), in QuietModeStep(ShowNoDialogs) without SafetyPoint
/// or UndoStep, because exports change no design data: <c>Export.PdfPages</c> with a full file name, which overrides
/// the scheme's folder; <c>DxfDwgPagesToDisk</c> with the folder and an empty language, so the project is not
/// translated first; <c>GraphicPage</c> per page with colour depth 24, no compression and in colour. Schemes through
/// <c>SchemeSetting</c> on the nodes the EPLAN XML docs name. Main thread, inside the dispatcher's LockingStep.
/// </summary>
internal sealed class EplanExporter : IExporter
{
    private const int ColorDepth = 24;

    public bool SchemeExists(ExportKind kind, string scheme) => WithSchemes(kind, setting => setting.CheckIfSchemeExists(scheme));

    /// <summary><c>SetScheme(i)</c> leaves the last used scheme as it is (EPLAN XML docs).</summary>
    public IReadOnlyList<string> Schemes(ExportKind kind) => WithSchemes(kind, setting =>
    {
        var names = new List<string>();
        for (int i = 0; i < setting.GetCount(); i++)
        {
            setting.SetScheme(i);
            names.Add(setting.GetName());
        }

        return (IReadOnlyList<string>)names;
    });

    public string LastUsedScheme(ExportKind kind) => WithSchemes(kind, setting => setting.GetLastUsed() ?? string.Empty);

    public bool TrySetLastUsedScheme(ExportKind kind, string scheme)
    {
        try
        {
            return WithSchemes(kind, setting =>
            {
                setting.SetLastUsed(scheme);
                return true;
            });
        }
        catch (Exception ex) when (ex is BaseException or ArgumentException)
        {
            return false; // an empty or unknown name (EPLAN XML docs); the operation warns
        }
    }

    public void ExportPdf(string projectPath, IReadOnlyList<string> pages, string scheme, ExportColor color, string file)
    {
        Export.DegreeOfColor degree = color switch
        {
            ExportColor.Grey => Export.DegreeOfColor.GreyLevel,
            ExportColor.BlackWhite => Export.DegreeOfColor.BlackAndWhite,
            _ => Export.DegreeOfColor.Color,
        };
        Run(projectPath, pages, (export, found) => export.PdfPages(new ArrayList(found), scheme, file, degree, false, string.Empty, false));
    }

    public void ExportDxf(string projectPath, IReadOnlyList<string> pages, string scheme, string directory) =>
        Run(projectPath, pages, (export, found) => export.DxfDwgPagesToDisk(found, scheme, directory, string.Empty));

    public void ExportImage(string projectPath, string page, string directory, ImageFormat format, int widthPx)
    {
        Export.Format imageFormat = format == ImageFormat.Jpg ? Export.Format.JPG : Export.Format.PNG;
        Run(projectPath, [page], (export, found) => export.GraphicPage(found[0], directory, imageFormat, ColorDepth, widthPx, Export.Compression.None, false));
    }

    private static T WithSchemes<T>(ExportKind kind, Func<SchemeSetting, T> read)
    {
        MainThread.Assert();
        using var setting = new SchemeSetting();
        setting.Init(kind == ExportKind.Pdf ? "USER.PDFExportGUI.SCHEMAS" : "USER.DXF.SCHEMES");
        return read(setting);
    }

    /// <summary>
    /// Finds the pages, then exports them quietly. What EPLAN throws for a failed export (EPLAN XML docs) goes back as
    /// EPLAN_OPERATION_FAILED with EPLAN's message; what the exception mapper knows (a missing licence) stays for it.
    /// </summary>
    private static void Run(string projectPath, IReadOnlyList<string> pages, Action<Export, Page[]> export)
    {
        MainThread.Assert();
        using var finder = new DMObjectsFinder(EplanProjects.ByPath(projectPath));
        Page[] found = [.. pages.Select(name => EplanDeviceReader.FindPage(finder, name) ?? throw new InvalidOperationException("A page the operation resolved is gone."))];
        try
        {
            using var exporter = new Export();
            using (new QuietModeStep(QuietModes.ShowNoDialogs))
            {
                export(exporter, found);
            }
        }
        catch (Exception ex) when (ex is BaseException or ArgumentException or UnauthorizedAccessException or ApplicationException or IOException
            && new EplanExceptionMapper().Map(ex) is null)
        {
            var failed = new BridgeErrorException(
                ErrorReason.EplanOperationFailed,
                "EPLAN could not export these pages, so no files were kept. metadata.eplanMessage has EPLAN's message; ask the engineer to check the scheme and the pages in EPLAN.",
                ex);
            failed.Error.Metadata = new Dictionary<string, string> { ["eplanMessage"] = EplanConnectionGenerator.ForModel(ex.Message) };
            throw failed;
        }
    }
}

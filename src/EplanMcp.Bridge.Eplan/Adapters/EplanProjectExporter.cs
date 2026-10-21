using Eplan.EplApi.ApplicationFramework;
using Eplan.EplApi.Base;
using Eplan.EplApi.HEServices;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>
/// Exports a whole project the way spike S25 did (api-notes A25): in QuietModeStep(ShowNoDialogs) without SafetyPoint
/// or UndoStep, because these exports change no design data (S25: only User.eod), always to a file in the Host's
/// folder. Main thread, inside the dispatcher's LockingStep.
/// </summary>
internal sealed class EplanProjectExporter : IProjectExporter
{
    private const string LabellingSchemes = "USER.Labelling.Config";
    private const string LabelFormatSetting = "Data.File.Format";

    /// <summary>
    /// <c>Export.PXFProject</c> with connections, as S25 ran it; the overload with two arguments leaves the connections
    /// out (EPLAN XML docs). S25: 19 s and 598 MB for the sample project without master data, 37 s and 840 MB with it.
    /// </summary>
    public void ExportPxf(string projectPath, string file, bool includeMasterData) => Run("the project as PXF", () =>
    {
        using var export = new Export();
        export.PXFProject(EplanProjects.ByPath(projectPath), file, includeMasterData, true);
    });

    /// <summary>
    /// <c>PartsService.ExportPartsList</c> as XML, as S25 ran it: 62 s and 14 MB for the sample project. CSV is not
    /// offered: after a CSV export S25 saw EPLAN fail with an access violation (A25), not yet explained.
    /// </summary>
    public void ExportPartsList(string projectPath, string file) => Run("the parts list", () =>
    {
        using var parts = new PartsService();
        parts.ExportPartsList(EplanProjects.ByPath(projectPath), file, PartsService.Format.XML);
    });

    /// <summary>
    /// The schemes of <c>USER.Labelling.Config</c> (A24), each with the converter in its <c>Data.File.Format</c>, the
    /// path of EPLAN's own labelling configurations (api-notes B9). That <c>GetStringSetting</c> reads it there is left
    /// to the Large tests (docs/tools/eplan_export_labels.md, section 5); a format that cannot be read is null, and the
    /// operation refuses the scheme rather than guess.
    /// </summary>
    public IReadOnlyList<LabelScheme> LabelSchemes()
    {
        MainThread.Assert();
        using var setting = new SchemeSetting();
        setting.Init(LabellingSchemes);
        var schemes = new List<LabelScheme>();
        for (int i = 0; i < setting.GetCount(); i++)
        {
            setting.SetScheme(i); // leaves the last used scheme as it is (EPLAN XML docs)
            schemes.Add(new LabelScheme { Name = setting.GetName(), Format = LabelFormat(setting) });
        }

        return schemes;
    }

    public string? LanguageCode(string? language)
    {
        MainThread.Assert();
        if (language is null)
        {
            using var languages = new Languages();
            return languages.GuiLanguage.GetString();
        }

        using var iso = new ISOCode(language);
        return iso.IsValid() ? language : null;
    }

    /// <summary>
    /// <c>Label.DoLabel(project, scheme, language, file, 1, 1)</c> as S25 ran it: a scheme's name does not change the
    /// last used one (A25). Not the overload with <c>bShowOutput</c>, which opens the file (EPLAN XML docs).
    /// </summary>
    public void ExportLabels(string projectPath, string scheme, string language, string file) => Run("labels", () =>
    {
        using var label = new Label();
        label.DoLabel(EplanProjects.ByPath(projectPath), scheme, language, file, 1, 1);
    });

    private static string? LabelFormat(SchemeSetting setting)
    {
        try
        {
            return setting.GetStringSetting(LabelFormatSetting, 0);
        }
        catch (BaseException)
        {
            return null; // the setting is not defined (EPLAN XML docs)
        }
    }

    /// <summary>
    /// Runs one export quietly. What EPLAN throws for a failed export (EPLAN XML docs) goes back as
    /// EPLAN_OPERATION_FAILED with EPLAN's message; what the exception mapper knows (a missing licence) stays for it.
    /// </summary>
    private static void Run(string what, System.Action export)
    {
        MainThread.Assert();
        try
        {
            using (new QuietModeStep(QuietModes.ShowNoDialogs))
            {
                export();
            }
        }
        catch (Exception ex) when (ex is BaseException or ArgumentException or UnauthorizedAccessException or ApplicationException or IOException
            && new EplanExceptionMapper().Map(ex) is null)
        {
            var failed = new BridgeErrorException(
                ErrorReason.EplanOperationFailed,
                $"EPLAN could not export {what}, so no files were kept. metadata.eplanMessage has EPLAN's message; ask the engineer to check the project in EPLAN.",
                ex);
            failed.Error.Metadata = new Dictionary<string, string> { ["eplanMessage"] = EplanConnectionGenerator.ForModel(ex.Message) };
            throw failed;
        }
    }
}

using Eplan.EplApi.ApplicationFramework;
using Eplan.EplApi.Base;
using Eplan.EplApi.MasterData;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>
/// Exports the configured parts database the way spike S24 r11 did (api-notes A27, A30), in
/// QuietModeStep(ShowNoDialogs), always to a file in the Host's folder. The database is opened with
/// <c>OpenDatabase()</c> and closed again, as for part.search (<see cref="EplanPartsDatabase"/>); nothing else is
/// called that would create, upgrade or select a database or set a setting, and BannedSymbols.txt keeps it so. S24
/// saw no export change its database; the Large tests compare the data files before and after.
/// Main thread, inside the dispatcher's LockingStep.
/// </summary>
internal sealed class EplanMasterDataExporter : IMasterDataExporter
{
    /// <summary>
    /// <c>ExportParts(file, format)</c> for every part, as S24 r11 ran it: XML 2 s and 5 MB, EDZ 44 s and 128 MB for the
    /// 464 parts of the sample database. Given part numbers are looked up with <c>GetPartVariants</c>, every variant of
    /// each, since <c>GetPart</c> takes the first variant only (EPLAN XML docs), then exported as a list.
    /// </summary>
    public PartsExport ExportParts(IReadOnlyList<string>? partNumbers, MasterDataFormat format, string? file)
    {
        MainThread.Assert();
        string selected = MDPartsManagement.SelectedPartsDatabaseAsString ?? string.Empty;
        try
        {
            using MDPartsDatabase database = new MDPartsManagement().OpenDatabase() ?? throw new InvalidOperationException("EPLAN opened no parts database.");
            try
            {
                return Export(database, selected, partNumbers, format == MasterDataFormat.Edz ? MDPartsDatabase.DataFormat.EDZ : MDPartsDatabase.DataFormat.XML, file);
            }
            finally
            {
                database.Close();
            }
        }
        catch (Exception ex) when (IsEplanFailure(ex))
        {
            throw Failed(
                file is null ? "EPLAN could not open or read its parts database" : "EPLAN could not export its parts database, so no files were kept",
                "the configured database in EPLAN's parts management",
                ex);
        }
    }

    private static PartsExport Export(MDPartsDatabase database, string selected, IReadOnlyList<string>? partNumbers, MDPartsDatabase.DataFormat format, string? file)
    {
        var found = new PartsExport { Database = selected };
        if (partNumbers is null)
        {
            MDPart[] all = database.Parts;
            found.VariantCount = all.Length;
            found.PartCount = all.Select(part => part.PartNr).Distinct(StringComparer.Ordinal).Count();
            if (file is not null)
            {
                using (new QuietModeStep(QuietModes.ShowNoDialogs))
                {
                    database.ExportParts(file, format);
                }
            }

            return found;
        }

        var parts = new List<MDPart>();
        foreach (string partNumber in partNumbers)
        {
            MDPart[] variants = database.GetPartVariants(partNumber) ?? [];
            if (variants.Length == 0)
            {
                found.Missing.Add(partNumber);
            }

            parts.AddRange(variants);
        }

        found.VariantCount = parts.Count;
        found.PartCount = partNumbers.Count - found.Missing.Count;
        if (file is not null && found.Missing.Count == 0)
        {
            using (new QuietModeStep(QuietModes.ShowNoDialogs))
            {
                database.ExportParts(file, format, parts);
            }
        }

        return found;
    }

    /// <summary>What EPLAN throws for a database it cannot open or an export it cannot write; what the exception mapper knows (a missing licence) stays for it.</summary>
    private static bool IsEplanFailure(Exception ex) =>
        ex is BaseException or ArgumentException or InvalidOperationException or UnauthorizedAccessException or IOException
        && new EplanExceptionMapper().Map(ex) is null;

    private static BridgeErrorException Failed(string what, string check, Exception ex)
    {
        var failed = new BridgeErrorException(
            ErrorReason.EplanOperationFailed,
            $"{what}. metadata.eplanMessage has EPLAN's message; ask the engineer to check {check}.",
            ex);
        failed.Error.Metadata = new Dictionary<string, string> { ["eplanMessage"] = EplanConnectionGenerator.ForModel(ex.Message) };
        return failed;
    }
}

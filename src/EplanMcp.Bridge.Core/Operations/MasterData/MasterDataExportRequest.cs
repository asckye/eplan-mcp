using System.Globalization;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// The checked arguments of the plan or run of a database export (docs/tools/eplan_part_export.md,
/// eplan_translation_export.md, section 2): every problem at once. Part numbers are taken as the database writes them,
/// trimmed, each once.
/// </summary>
internal sealed class MasterDataExportRequest
{
    public const int MaxParts = 500;

    private MasterDataExportRequest(MasterDataFormat format, string extension, IReadOnlyList<string>? parts)
    {
        Format = format;
        Extension = extension;
        Parts = parts;
    }

    public MasterDataFormat Format { get; }

    /// <summary>The extension the Host gives the file: <c>.xml</c>, <c>.edz</c>, or <c>.txt</c> for tab-separated text.</summary>
    public string Extension { get; }

    /// <summary>Parts: the part numbers to export; null for every part, and always for translations.</summary>
    public IReadOnlyList<string>? Parts { get; }

    public static MasterDataExportRequest Check(MasterDataExportKind kind, ExportArgs args)
    {
        var violations = new List<string>();
        string format = string.IsNullOrWhiteSpace(args.Format) ? string.Empty : args.Format!.Trim().ToLowerInvariant();
        (MasterDataFormat Format, string Extension)? file = (kind, format) switch
        {
            (MasterDataExportKind.Parts, "" or "xml") => (MasterDataFormat.Xml, ".xml"),
            (MasterDataExportKind.Parts, "edz") => (MasterDataFormat.Edz, ".edz"),
            (MasterDataExportKind.Parts, "backup") => (MasterDataFormat.Backup, MasterDataBackupOperation.PartsExtension),
            (MasterDataExportKind.Translations, "" or "tab") => (MasterDataFormat.Tab, ".txt"),
            (MasterDataExportKind.Translations, "xml") => (MasterDataFormat.Xml, ".xml"),
            _ => null,
        };
        if (file is null)
        {
            violations.Add(kind == MasterDataExportKind.Parts ? "'format' must be xml, edz or backup." : "'format' must be tab or xml.");
        }
        else if (file.Value.Format == MasterDataFormat.Backup)
        {
            CheckBackup(args, violations);
        }

        List<string>? parts = kind == MasterDataExportKind.Parts ? CheckParts(args.Parts, violations) : null;
        return violations.Count > 0
            ? throw WriteArguments.Invalid(violations)
            : new MasterDataExportRequest(file!.Value.Format, file.Value.Extension, parts);
    }

    /// <summary>
    /// PART_NOT_FOUND when <paramref name="found"/> misses part numbers (tool spec 4): at most
    /// <see cref="ProjectResolver.MaxCandidates"/> of them in <c>metadata.missing</c>, all counted.
    /// </summary>
    public static PartsExport AllFound(PartsExport found) => found.Missing.Count == 0
        ? found
        : throw new BridgeErrorException(
            ErrorReason.PartNotFound,
            "The parts database has no part with some of these part numbers, so nothing was exported; metadata.missing lists them. Find the exact part numbers with eplan_part_search.",
            retryable: false,
            new Dictionary<string, string>
            {
                ["missing"] = string.Join("\n", found.Missing.Take(ProjectResolver.MaxCandidates)),
                ["missingCount"] = found.Missing.Count.ToString(CultureInfo.InvariantCulture),
            });

    /// <summary>
    /// A backup archives the whole database, and its file name is EPLAN's <c>ARCHIVENAME</c>: ASCII letters, digits,
    /// <c>-</c> and <c>_</c> only, as S24 and the backup of ADR-0019 used it (docs/tools/eplan_part_export.md, section 2).
    /// </summary>
    private static void CheckBackup(ExportArgs args, List<string> violations)
    {
        if (args.Parts is not null)
        {
            violations.Add("'parts' cannot be used with format backup: the backup archives the whole database.");
        }

        if (args.FileName is { } fileName && !MasterDataBackupOperation.IsPlainName(ArchiveName(fileName)))
        {
            violations.Add("'fileName' must use only ASCII letters, digits, - and _ (at most 64) for format backup.");
        }
    }

    /// <summary>
    /// INVALID_ARGUMENT for a backup of a database on SQL Server: the backup of ADR-0019 is verified for file databases
    /// only (S24), so it is not tried; xml exports any database.
    /// </summary>
    public static void CheckBackupOf(MasterDataExportRequest request, string database)
    {
        if (request.Format == MasterDataFormat.Backup && MasterDatabases.IsServer(database))
        {
            throw WriteArguments.Invalid(["format backup archives only a file database, and EPLAN's parts database is on SQL Server. Use format xml."]);
        }
    }

    /// <summary>The archive name EPLAN gets for a backup's file name: the name without <c>.zw6</c>.</summary>
    public static string ArchiveName(string fileName) =>
        fileName.EndsWith(MasterDataBackupOperation.PartsExtension, StringComparison.OrdinalIgnoreCase) ? fileName.Substring(0, fileName.Length - MasterDataBackupOperation.PartsExtension.Length) : fileName;

    /// <summary>1 to <see cref="MaxParts"/> part numbers, none blank; null stays null (every part).</summary>
    private static List<string>? CheckParts(List<string>? parts, List<string> violations)
    {
        if (parts is null)
        {
            return null;
        }

        if (parts.Count is < 1 or > MaxParts)
        {
            violations.Add($"'parts' must hold 1 to {MaxParts.ToString(CultureInfo.InvariantCulture)} part numbers; omit it for every part.");
            return null;
        }

        for (int i = 0; i < parts.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(parts[i]))
            {
                violations.Add($"parts[{i.ToString(CultureInfo.InvariantCulture)}] must be a part number, such as SIE.3RH2122-1HB40.");
            }
        }

        return [.. parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()).Distinct(StringComparer.Ordinal)];
    }
}

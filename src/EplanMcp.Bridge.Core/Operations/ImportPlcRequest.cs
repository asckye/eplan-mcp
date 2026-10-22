using EplanMcp.Contracts;
using Newtonsoft.Json;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Shared argument checks, fingerprint and mandatory identity comparison (tool spec sections 2 and 5).</summary>
internal sealed class ImportPlcRequest(OpenProject project, ImportSource source, string converter, PlcImportState before)
{
    public OpenProject Project { get; } = project;
    public ImportSource Source { get; } = source;
    public string Converter { get; } = converter;
    public PlcImportState Before { get; } = before;

    public static ImportPlcRequest Check(ImportPlcArgs args, IProjectCatalog catalog, IPlcImporter importer, Action<ImportSource> readSource)
    {
        var violations = new List<string>();
        WriteArguments.RequireProject(args.Project, violations);
        string? extension = args.Converter switch { "PlcDcXMLExchangerUniversal" => ".pbf", "PlcDcAMLExchangerGeneral" => ".aml", _ => null };
        if (extension is null)
        {
            violations.Add("'converter' must be PlcDcXMLExchangerUniversal or PlcDcAMLExchangerGeneral.");
        }

        ImportSource? source = ImportSource.Check(args.File, args.FilePath, args.Sha256, extension is null ? [".pbf", ".aml"] : [extension], violations);
        if (extension is not null && !(args.File?.EndsWith(extension, StringComparison.OrdinalIgnoreCase) ?? false))
        {
            violations.Add("'file' must end with " + extension + " for the given converter.");
        }

        if (violations.Count > 0)
        {
            throw WriteArguments.Invalid(violations);
        }

        OpenProject open = WriteArguments.Writable(catalog, args.Project);
        readSource(source!);
        if (source!.Bytes is < 1 or > 100 * 1024 * 1024)
        {
            throw WriteArguments.Invalid(["'file' must be nonempty and no larger than 100 MB."]);
        }

        return new ImportPlcRequest(open, source, args.Converter!, importer.Read(open.Path));
    }

    public string StateFingerprint() => Fingerprint.Of([Before.StateFingerprint(), Source.FingerprintLine, Source.File, Source.FullPath, Converter]);

    /// <summary>Rejects names that already existed; sharing a name among new objects is informational only.</summary>
    public static int Verify(PlcImportState before, PlcImportState after)
    {
        var existing = before.Functions.Values.GroupBy(f => f.Name, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var added = after.Functions.Values.Where(f => !before.Functions.ContainsKey(f.Id)).GroupBy(f => f.Name, StringComparer.Ordinal).ToList();
        var duplicates = added.Where(g => existing.ContainsKey(g.Key))
            .Select(g => new { name = g.Key, existingCount = existing[g.Key] }).OrderBy(d => d.name, StringComparer.Ordinal).ToList();
        if (duplicates.Count > 0)
        {
            throw new BridgeErrorException(ErrorReason.AlreadyExists,
                "The PLC import added objects whose full names already existed before the import and was rolled back; the backup was retained. Correct the conflicting names or choose a different target project before importing again.",
                retryable: false, new Dictionary<string, string>
                {
                    ["duplicates"] = JsonConvert.SerializeObject(duplicates.Take(50)),
                    ["duplicateCount"] = duplicates.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["truncated"] = duplicates.Count > 50 ? "true" : "false",
                });
        }

        string[] changed = [.. before.Functions.Values.Where(f => !after.Functions.TryGetValue(f.Id, out PlcImportFunction? now)
            || !string.Equals(now.Name, f.Name, StringComparison.Ordinal) || !string.Equals(now.Category, f.Category, StringComparison.Ordinal) || now.IsPlcBox != f.IsPlcBox).Select(f => f.Name)];
        if (changed.Length > 0 || after.Counts.Functions < before.Counts.Functions || after.Counts.PlcBoxes < before.Counts.PlcBoxes || after.Counts.ArticleReferences < before.Counts.ArticleReferences)
        {
            throw new BridgeErrorException(ErrorReason.EplanOperationFailed,
                "The PLC import removed or changed existing objects and was rolled back. Ask the engineer to check the source file and target project; the backup was retained.",
                retryable: false, new Dictionary<string, string> { ["changed"] = string.Join("\n", changed.Take(50)) });
        }

        return added.Where(g => g.Count() > 1).Sum(g => g.Count());
    }
}

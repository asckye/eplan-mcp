using System.Globalization;
using EplanMcp.Contracts;
using Newtonsoft.Json;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Read-only file validation and preview; every import plans a change, including a repeat (ADR-0019).</summary>
public sealed class TranslationImportPlanOperation(ITranslationDatabaseWriter writer) : Operation<TranslationImportArgs, WritePlan>
{
    internal const long MaxFileBytes = 50L * 1024 * 1024;

    public override string Name => OperationNames.TranslationImportPlan;

    protected override WritePlan Execute(TranslationImportArgs args)
    {
        (ImportSource source, TranslationTable table, ConfiguredDatabase database) = Check(args, writer);
        string entries = table.Rows.Count.ToString(CultureInfo.InvariantCulture);
        return new WritePlan
        {
            Fingerprint = source.Sha256,
            MasterData = new MasterDataTarget { Kind = MasterDataKinds.Translations, Database = database.Database, Path = database.Path },
            Changes = [new PlannedChange { Action = "update", Kind = "translations", Name = source.File, Property = "entries", NewValue = entries }],
            Details = new Dictionary<string, string>
            {
                ["file"] = source.File,
                ["bytes"] = source.Bytes.ToString(CultureInfo.InvariantCulture),
                ["sha256"] = source.Sha256,
                ["entries"] = entries,
                ["referenceLanguage"] = table.ReferenceLanguage,
                ["languages"] = string.Join(", ", table.Columns.Where(c => c != table.ReferenceLanguage)),
                ["preview"] = JsonConvert.SerializeObject(table.Rows.Take(10).Select(row => table.Columns
                    .Select((column, i) => new { column, text = row[i].Substring(0, Math.Min(row[i].Length, 40)) })
                    .ToDictionary(cell => cell.column, cell => cell.text, StringComparer.Ordinal))),
            },
        };
    }

    internal static (ImportSource Source, TranslationTable Table, ConfiguredDatabase Database) Check(TranslationImportArgs args, ITranslationDatabaseWriter writer)
    {
        var violations = new List<string>();
        ImportSource? source = ImportSource.Check(args.File, args.FilePath, args.Sha256, [".txt"], violations);
        if (violations.Count > 0)
        {
            throw WriteArguments.NotStarted(WriteArguments.Invalid(violations));
        }

        TranslationTable table;
        // Hold a read lock so the hash and parsed bytes describe the same file.
        try
        {
            source!.Read(hash: false, MaxFileBytes);
            using var stream = new FileStream(source.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            source.Read(hash: true, MaxFileBytes);
            table = TranslationTable.Parse(File.ReadAllBytes(source.FullPath), args.ReferenceLanguage);
        }
        catch (BridgeErrorException ex)
        {
            throw WriteArguments.NotStarted(ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw WriteArguments.NotStarted(new BridgeErrorException(ErrorReason.InvalidArgument,
                "The translation file cannot be read. Close it in other programs and call again.", ex));
        }

        ConfiguredDatabase database = writer.Configured();
        if (MasterDatabases.IsServer(database.Database))
        {
            throw MasterDatabases.Denied("translation");
        }

        if (database.Database.Length == 0)
        {
            throw WriteArguments.NotStarted(new BridgeErrorException(ErrorReason.EplanOperationFailed,
                "EPLAN has no translation database configured. Ask the engineer to select one in EPLAN's settings."));
        }

        return (source, table, database);
    }
}

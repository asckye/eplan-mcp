using System.Globalization;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// The checked arguments of part.import.plan and part.import.execute (docs/tools/eplan_part_import.md, sections 2 and
/// 5): the file the Host resolved below its import root, read again for its SHA-256 and its part numbers, the configured
/// parts database (never one on SQL Server), and the file's part numbers classified by <see cref="Mode"/> against the
/// database. Both operations build it the same way and compute the fingerprint here.
/// </summary>
internal sealed class PartImportRequest
{
    /// <summary>The most part numbers a list of the plan or the result holds (tool spec 3).</summary>
    public const int MaxListed = 50;

    public static readonly string[] Extensions = [".xml"];

    public static readonly string[] Modes = ["append_new", "update_existing", "update_and_append", "replace_and_append"];

    public static readonly string[] Matches = ["part_number", "unique_id", "unique_id_or_part_number"];

    private PartImportRequest(ImportSource source, string mode, string matchBy, ConfiguredDatabase database, PartsDatabaseState state, IReadOnlyList<FilePart> parts)
    {
        Source = source;
        Mode = mode;
        MatchBy = matchBy;
        Database = database;
        PartCount = state.PartCount;
        foreach (FilePart part in parts)
        {
            bool exists = state.PartNumbers.Contains(part.PartNumber);
            if (exists)
            {
                Existing.Add(part.PartNumber);
            }

            bool adds = !exists && mode != "update_existing";
            bool updates = exists && mode != "append_new";
            (adds ? Added : updates ? Updated : Skipped).Add(part.PartNumber);
            AddedVariants += adds ? part.Variants : 0;
        }
    }

    public ImportSource Source { get; }

    public string Mode { get; }

    public string MatchBy { get; }

    public ConfiguredDatabase Database { get; }

    /// <summary>The database's parts now, each variant counted.</summary>
    public int PartCount { get; }

    /// <summary>The file's part numbers the database has now, which the fingerprint holds.</summary>
    public List<string> Existing { get; } = [];

    public List<string> Added { get; } = [];

    public List<string> Updated { get; } = [];

    public List<string> Skipped { get; } = [];

    /// <summary>The variants the file has of the added parts: how many parts the database should gain.</summary>
    public int AddedVariants { get; }

    /// <summary>
    /// Every argument problem at once (mcp-tool-design 6.2), then the file, the database (POLICY_DENIED on SQL Server,
    /// ADR-0019) and its parts.
    /// </summary>
    public static PartImportRequest Check(PartImportArgs args, IPartsDatabaseWriter writer)
    {
        var violations = new List<string>();
        ImportSource? source = ImportSource.Check(args.File, args.FilePath, args.Sha256, Extensions, violations);
        string mode = Given(args.Mode) ?? Modes[0];
        string matchBy = Given(args.MatchBy) ?? Matches[0];
        if (!Modes.Contains(mode, StringComparer.Ordinal))
        {
            violations.Add("'mode' must be " + string.Join(", ", Modes) + ".");
        }

        if (!Matches.Contains(matchBy, StringComparer.Ordinal))
        {
            violations.Add("'match_by' must be " + string.Join(", ", Matches) + ".");
        }

        if (violations.Count > 0)
        {
            throw WriteArguments.Invalid(violations);
        }

        source!.Read(hash: true);
        IReadOnlyList<FilePart> parts = PartsXmlFile.Read(source.FullPath, source.File);
        ConfiguredDatabase database = writer.Configured();
        if (MasterDatabases.IsServer(database.Database))
        {
            throw MasterDatabases.Denied();
        }

        if (database.Database.Length == 0)
        {
            throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "EPLAN has no parts database configured, so nothing was imported. Ask the engineer to choose one in EPLAN's parts management.");
        }

        return new PartImportRequest(source, mode, matchBy, database, writer.Read(), parts);
    }

    /// <summary>The database as the plan reports it (ADR-0019).</summary>
    public MasterDataTarget Target() => new() { Kind = MasterDataKinds.Parts, Database = Database.Database, Path = Database.Path };

    /// <summary>
    /// The plan's state (tool spec 5): the file (size, last write time, SHA-256), the database, and which of the file's
    /// part numbers it has. Other parts may change meanwhile without invalidating the plan.
    /// </summary>
    public string StateFingerprint() => Fingerprint.Of(
    [
        Source.FingerprintLine,
        "database\t" + Database.Database,
        .. Existing.Select(number => "existing\t" + number),
    ]);

    /// <summary>At most <see cref="MaxListed"/> part numbers, one per line, for the plan's details.</summary>
    public static string Lines(IEnumerable<string> numbers) => string.Join("\n", numbers.Take(MaxListed));

    public static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string? Given(string? value) => string.IsNullOrWhiteSpace(value) ? null : value!.Trim();
}

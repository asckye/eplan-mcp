using EplanMcp.Contracts;
using Newtonsoft.Json;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Shared validation and fingerprint for import.list (tool spec 5, SEC-31).</summary>
internal sealed class ImportListRequest(OpenProject project, ImportSource source, string kind, string format, ListImportState state)
{
    public OpenProject Project { get; } = project;
    public ImportSource Source { get; } = source;
    public string Kind { get; } = kind;
    public ListImportState State { get; } = state;
    public string Converter { get; } = kind == "parts" ? "XPalXmlExporter" : format == "csv" ? "XDLCsvImporterExporter" : "XDLXmlExporter";

    public static ImportListRequest Check(ImportListArgs args, IProjectCatalog catalog, IListImporter importer, Action<ImportSource> read)
    {
        var violations = new List<string>();
        WriteArguments.RequireProject(args.Project, violations);
        string kind = args.Kind?.Trim() ?? string.Empty;
        string format = args.Format is null ? "xml" : args.Format.Trim();
        if (kind is not ("devices" or "parts"))
        {
            violations.Add("'kind' must be devices or parts.");
        }

        if (format is not ("xml" or "csv") || (kind == "parts" && format != "xml"))
        {
            violations.Add("'format' must be xml or csv for devices, and xml for parts.");
        }

        ImportSource? source = ImportSource.Check(args.File, args.FilePath, args.Sha256, ["." + format], violations);
        if (args.File is { } file && (file.Length > 260 || !file.EndsWith("." + format, StringComparison.OrdinalIgnoreCase)))
        {
            violations.Add("'file' must be 1 to 260 characters with an extension matching 'format', below imports.");
        }

        if (violations.Count > 0)
        {
            throw WriteArguments.Invalid(violations);
        }

        OpenProject project = WriteArguments.Writable(catalog, args.Project);
        read(source!);
        if (source!.Bytes is < 1 or > 100 * 1024 * 1024)
        {
            throw WriteArguments.Invalid(["'file' must be nonempty and at most 100 MB; use a file below imports."]);
        }

        return new ImportListRequest(project, source, kind, format, importer.Read(project.Path));
    }

    public string StateFingerprint() => Fingerprint.Of([Project.Path, Kind, Converter, Source.File, Source.FingerprintLine, State.Fingerprint]);

    public Dictionary<string, string> Details() => new()
    {
        ["kind"] = Kind,
        ["file"] = Source.File,
        ["bytes"] = Source.Bytes.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["sha256"] = Source.Sha256,
        ["converter"] = Converter,
        ["before"] = Protocol.BridgeJson.FromObject(State.Counts).ToString(Formatting.None),
    };

    public void RequireFingerprint(string? fingerprint)
    {
        if (!string.Equals(StateFingerprint(), fingerprint, StringComparison.Ordinal))
        {
            throw WriteArguments.NotStarted(new BridgeErrorException(ErrorReason.PlanStale,
                "The file or project list data changed after planning. Nothing was imported. Call eplan_import_list again for a new plan."));
        }
    }
}

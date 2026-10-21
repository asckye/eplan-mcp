using System.Globalization;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// The checked arguments of import.pxf.plan and import.pxf.execute (docs/tools/eplan_import_pxf.md, sections 2 and 5):
/// the EPJ file the Host resolved below its import root (SEC-31), and the new project's path without extension in the
/// scratch folder, where no project of that name may be yet. No open project is involved. Both operations build it the
/// same way and compute the fingerprint here.
/// </summary>
internal sealed class ImportPxfRequest
{
    /// <summary>The extensions the tool imports (tool spec 2).</summary>
    public static readonly string[] Extensions = [".epj", ".pxf"];

    /// <summary>
    /// What a project at a path without extension consists of: the link file and the data folder EPLAN made in S22
    /// (A29), and the link file a revision turns <c>.elk</c> into (A28).
    /// </summary>
    private static readonly string[] _projectFiles = [".elk", ".ell", ".edb"];

    private ImportPxfRequest(ImportSource source, string name, string target)
    {
        Source = source;
        Name = name;
        Target = target;
    }

    public ImportSource Source { get; }

    /// <summary>The new project's name.</summary>
    public string Name { get; }

    /// <summary>The new project's full path without extension, as <c>Import.PXFProject</c> takes it (A29).</summary>
    public string Target { get; }

    /// <summary>The new project's link file, which EPLAN's project list names it by.</summary>
    public string LinkFile => Target + ".elk";

    /// <summary>The new project's data folder.</summary>
    public string DataFolder => Target + ".edb";

    /// <summary>Every argument problem at once (mcp-tool-design 6.2), then a project of that name, then the file.</summary>
    public static ImportPxfRequest Check(ImportPxfArgs args)
    {
        var violations = new List<string>();
        ImportSource? source = ImportSource.Check(args.File, args.FilePath, args.Sha256, Extensions, violations);
        string name = args.Name?.Trim() ?? string.Empty;
        string target = args.Target ?? string.Empty;
        if (name.Length == 0)
        {
            violations.Add("'name' is required: the new project's name.");
        }
        else if (!Path.IsPathRooted(target) || !string.Equals(Path.GetFileName(target), name, StringComparison.Ordinal) || !Directory.Exists(Path.GetDirectoryName(target)))
        {
            violations.Add("'target' must be the new project's full path without extension, ending with its name, in a folder that exists: the one the Host chose in its scratch folder.");
        }

        if (violations.Count > 0)
        {
            throw WriteArguments.Invalid(violations);
        }

        string[] taken = [.. _projectFiles.Select(e => target + e).Where(p => File.Exists(p) || Directory.Exists(p)).Select(Path.GetFileName)];
        if (taken.Length > 0)
        {
            throw new BridgeErrorException(
                ErrorReason.AlreadyExists,
                $"A project named '{name}' is in the scratch folder's 'imported' folder already, so nothing was imported. Choose another 'name'.",
                retryable: false,
                new Dictionary<string, string> { ["name"] = name, ["existing"] = string.Join("\n", taken) });
        }

        // Up to 2 GB (tool spec 2): too much to read again on EPLAN's main thread, so only its size and time are taken.
        source!.Read(hash: false);
        return new ImportPxfRequest(source, name, target);
    }

    /// <summary>The one change, for the dry run and the audit (tool spec 5): a new project named after the file it comes from.</summary>
    public PlannedChange Change() =>
        new() { Action = "create", Kind = "project", Name = Name, NewValue = Source.Describe() + " into the scratch folder's 'imported' folder" };

    /// <summary>The plan's state: the file's size, last write time and SHA-256, and where the project goes.</summary>
    public string StateFingerprint() => Fingerprint.Of([Source.FingerprintLine, "target\t" + Target]);

    /// <summary>The plan's details (tool spec 3): the file and where the new project will be.</summary>
    public Dictionary<string, string> Details() => new()
    {
        ["file"] = Source.File,
        ["sha256"] = Source.Sha256,
        ["bytes"] = Source.Bytes.ToString(CultureInfo.InvariantCulture),
        ["path"] = LinkFile,
    };
}

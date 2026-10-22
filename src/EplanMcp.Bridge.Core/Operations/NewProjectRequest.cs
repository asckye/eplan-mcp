using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// The checked arguments of the project.create and project.restore operations (docs/design/2026-10-21-m6-bridge-snapshot.md,
/// 4.8, 4.9): the file the Host resolved below its import root (SEC-31), the new project's name, and its folder, which
/// must be absent or empty: EPLAN overwrites a project it restores onto without a word (api-notes A31), so nothing can be
/// there to overwrite. The plan and the execution build it the same way and compute the fingerprint here.
/// </summary>
internal sealed class NewProjectRequest
{
    private NewProjectRequest(ImportSource source, string name, string destination, bool existed)
    {
        Source = source;
        Name = name;
        Destination = destination;
        Existed = existed;
    }

    public ImportSource Source { get; }

    public string Name { get; }

    /// <summary>The folder the project goes into.</summary>
    public string Destination { get; }

    /// <summary>Whether the folder was there (empty) when checked.</summary>
    public bool Existed { get; }

    /// <summary>The new project's path without extension.</summary>
    public string Target => Path.Combine(Destination, Name);

    public string LinkFile => Target + ".elk";

    public string DataFolder => Target + ".edb";

    /// <summary>Every argument problem at once (mcp-tool-design 6.2), then a folder that is not empty, then the file.</summary>
    public static NewProjectRequest Check(NewProjectArgs args, string extension)
    {
        var violations = new List<string>();
        ImportSource? source = ImportSource.Check(args.File, args.FilePath, args.Sha256, [extension], violations);
        string name = args.Name ?? string.Empty;
        if (name.Trim().Length == 0 || name != name.Trim() || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.IndexOf('.') >= 0)
        {
            violations.Add("'name' must be the new project's name: no path, no dot, no leading or trailing space.");
        }

        string destination = (args.Destination ?? string.Empty).TrimEnd('\\', '/');
        if (!ProjectPaths.IsPlainFullPath(destination) || destination.Length <= 3)
        {
            violations.Add("'destination' must be the full local path of a new folder chosen by the Host; missing parent folders are made only during execution.");
        }

        if (violations.Count > 0)
        {
            throw WriteArguments.Invalid(violations);
        }

        var folder = new DirectoryInfo(destination);
        if (File.Exists(destination) || (folder.Exists && ((folder.Attributes & FileAttributes.ReparsePoint) != 0 || folder.EnumerateFileSystemInfos().Any())))
        {
            throw new BridgeErrorException(
                ErrorReason.AlreadyExists,
                "The destination folder is not empty, and EPLAN would overwrite a project there, so nothing was made. Choose a new folder.",
                retryable: false,
                new Dictionary<string, string> { ["name"] = name });
        }

        source!.Read(hash: false);
        return new NewProjectRequest(source, name, destination, folder.Exists);
    }

    /// <summary>The one change, for the dry run and the audit: a new project from the file.</summary>
    public PlannedChange Change(string verb) =>
        new() { Action = "create", Kind = "project", Name = Name, NewValue = verb + " " + Source.Describe() + "; file " + Source.File + "; sha256 " + Source.Sha256 + "; destination " + Destination + "; new project, closed afterwards; compatibility checked during execution" };

    /// <summary>The plan's state: the file's size, last write time and SHA-256, and where the project goes.</summary>
    public string StateFingerprint() => Fingerprint.Of([Source.FingerprintLine, "target\t" + Target, "folder\t" + (Existed ? "empty" : "absent")]);

    /// <summary>The plan's details: the file and where the new project will be.</summary>
    public Dictionary<string, string> Details() => new()
    {
        ["file"] = Source.File,
        ["sha256"] = Source.Sha256,
        ["bytes"] = Source.Bytes.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["path"] = LinkFile,
    };
}

using System.Buffers;
using System.Security;
using System.Security.Cryptography;
using EplanMcp.Contracts;
using EplanMcp.Host.Exports;

namespace EplanMcp.Host.Importing;

/// <summary>
/// The import root <c>&lt;data root&gt;\imports\</c> and the files the import tools read from it (M4c design 4.5, Q4;
/// SEC-31). The root is fixed. A tool's file argument is a path below it, checked by the rules of SEC-19: no drive, UNC
/// or leading separator, no <c>.</c> or <c>..</c>, and below the root no step a symbolic link or junction, so the file
/// lies physically below the root; the root itself may be a link. The check is the Host's, as the root is in the Host's
/// data root. No error names the root's local path (SEC-12). Stateless apart from its inputs.
/// </summary>
internal sealed class ImportFiles(HostDataLocations locations)
{
    /// <summary>The longest file argument.</summary>
    public const int MaxLength = 260;

    /// <summary>How the model is told where the import root is: never by its local path (SEC-12).</summary>
    public const string RootName = "the 'imports' folder in EplanMcp's data folder";

    private static readonly SearchValues<char> _invalid = SearchValues.Create(Path.GetInvalidFileNameChars());

    /// <summary>The import root's full path, which only the log and the engineer see.</summary>
    public string Root => locations.Imports;

    /// <summary>
    /// What is wrong with the form of <paramref name="file"/>, the tool argument <paramref name="argument"/>, as one
    /// violation; null when it is fine. Reads nothing, so a tool reports it with its other argument problems at once
    /// (mcp-tool-design 6.2). <paramref name="extensions"/> are the tool's, with the dot, such as <c>.dxf</c>.
    /// </summary>
    public static string? Problem(string argument, string? file, IReadOnlyList<string> extensions)
    {
        if (string.IsNullOrWhiteSpace(file))
        {
            return $"'{argument}' is required: a path below {RootName}, such as drawings/plan{extensions[0]}.";
        }

        string given = file.Trim();
        if (given.Length > MaxLength)
        {
            return $"'{argument}' must have at most {MaxLength} characters.";
        }

        // ':' covers drives and alternate data streams; an empty step covers a leading separator, UNC and '//'. Windows
        // drops dots and spaces at the end of a name, so such a step could name another one, '..' included.
        if (given.Contains(':', StringComparison.Ordinal) || Steps(given).Any(s =>
            s.Length == 0 || s is "." or ".." || s.AsSpan().ContainsAny(_invalid) || s.EndsWith('.') || s.EndsWith(' ') || ExportFileName.IsReserved(s)))
        {
            return $"'{argument}' must be a path below {RootName}, such as drawings/plan{extensions[0]}: no drive, no leading '/' or '\\', no '.' or '..', and no name Windows reserves.";
        }

        return extensions.Any(e => given.EndsWith(e, StringComparison.OrdinalIgnoreCase))
            ? null
            : $"'{argument}' must end with {string.Join(" or ", extensions)}.";
    }

    /// <summary>
    /// The file <paramref name="file"/> names, once <see cref="Problem"/> found nothing wrong with it: every step below
    /// the root must exist and be no symbolic link or junction, and the file at most <paramref name="maxBytes"/> long.
    /// Reads the file once for its SHA-256. Throws <see cref="ImportFileException"/> with INVALID_ARGUMENT or
    /// FILE_NOT_FOUND. A missing root is created first, so the engineer finds where to put files.
    /// </summary>
    public async Task<ImportFile> ResolveAsync(string argument, string file, long maxBytes, CancellationToken cancellationToken)
    {
        string given = Normalized(file);
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Root));
        CreateRoot(root);
        string[] steps = Steps(given);
        string current = root;
        for (int i = 0; i < steps.Length; i++)
        {
            current = Path.Combine(current, steps[i]);
            FileSystemInfo step = i < steps.Length - 1 ? new DirectoryInfo(current) : new FileInfo(current);
            if (!step.Exists)
            {
                throw new ImportFileException(ErrorReason.FileNotFound, $"No file '{given}' is in {RootName} (next to 'exports'). Put the file there first, then call again.", argument, given);
            }

            if ((step.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw Invalid(argument, given, $"'{argument}' leads through a symbolic link or junction, which the import tools never follow; put the file itself into {RootName}.");
            }
        }

        // Each step was checked above, so this only guards against a name Windows resolves to another place.
        string relative = Path.GetRelativePath(root, current);
        if (Path.IsPathRooted(relative) || relative.Split(Path.DirectorySeparatorChar)[0] == "..")
        {
            throw Invalid(argument, given, $"'{argument}' must be a path below {RootName}.");
        }

        var info = new FileInfo(current);
        if (info.Length > maxBytes)
        {
            throw Invalid(argument, given, $"'{argument}' is {ByteSize.Format(info.Length)}; this tool imports files of at most {ByteSize.Format(maxBytes)}.");
        }

        try
        {
            var stream = new FileStream(current, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using (stream.ConfigureAwait(false))
            {
                byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
                return new ImportFile(given, current, info.Length, info.LastWriteTimeUtc, Convert.ToHexStringLower(hash));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            throw Invalid(argument, given, $"'{argument}' cannot be read; another program may have it open. Close it there, then call again.");
        }
    }

    /// <summary>The argument as the tools return it: trimmed, with <c>/</c> between folders.</summary>
    public static string Normalized(string file) => file.Trim().Replace('\\', '/');

    private static string[] Steps(string file) => file.Replace('\\', '/').Split('/');

    private static void CreateRoot(string root)
    {
        try
        {
            Directory.CreateDirectory(root);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not fatal: the file is then not found, which says the same.
        }
    }

    private static ImportFileException Invalid(string argument, string given, string violation) =>
        new(ErrorReason.InvalidArgument, "Invalid arguments: " + violation, argument, given, violation);
}

/// <summary>
/// A file below the import root, as <see cref="ImportFiles.ResolveAsync"/> found it: the path as the tool returns it
/// (<c>/</c> between folders), the full path (for the Bridge and the log only, SEC-12), its size, last write time and
/// SHA-256 as lowercase hex.
/// </summary>
internal sealed record ImportFile(string Path, string FullPath, long Bytes, DateTime LastWriteUtc, string Sha256);

/// <summary>
/// A file argument the import root does not hold, or holds otherwise than SEC-31 allows; <see cref="Error"/> is the
/// tool's error. Every import tool writes, so it says <c>not_started</c> (ADR-0010).
/// </summary>
internal sealed class ImportFileException : Exception
{
    public ImportFileException()
        : this(ErrorReason.InvalidArgument, "Invalid arguments.", "file", string.Empty)
    {
    }

    public ImportFileException(string message)
        : this(ErrorReason.InvalidArgument, message, "file", string.Empty)
    {
    }

    public ImportFileException(string message, Exception innerException)
        : base(message, innerException)
    {
        Error = new BridgeError { Reason = ErrorReason.InvalidArgument, Message = message, Execution = ExecutionState.NotStarted };
    }

    public ImportFileException(string reason, string message, string argument, string file, string? violation = null)
        : base(message)
    {
        Error = new BridgeError
        {
            Reason = reason,
            Message = message,
            Execution = ExecutionState.NotStarted,
            Metadata = violation is null
                ? new Dictionary<string, string> { [argument] = file }
                : new Dictionary<string, string> { [argument] = file, ["violations"] = violation },
        };
    }

    public BridgeError Error { get; }
}

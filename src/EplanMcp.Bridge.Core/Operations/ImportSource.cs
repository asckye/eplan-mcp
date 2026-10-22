using System.Globalization;
using System.Security.Cryptography;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// The file an import operation reads (M4c design 4.5, SEC-31). The Host resolves the tool's path below its import root,
/// which the Bridge does not know, and passes the full path with the SHA-256 it read; the Bridge checks their form,
/// finds the file, and reads its size, last write time and, unless the file is too large to read on EPLAN's main
/// thread, its SHA-256 again. Neither the errors nor the change name the full path (SEC-12).
/// </summary>
internal sealed class ImportSource
{
    private ImportSource(string file, string fullPath, string sha256)
    {
        File = file;
        FullPath = fullPath;
        Sha256 = sha256;
    }

    /// <summary>The path below the import folder, as the tool returns it.</summary>
    public string File { get; }

    /// <summary>The full path, which never leaves the Bridge in a result or an error (SEC-12).</summary>
    public string FullPath { get; }

    /// <summary>The SHA-256 the Host read, lowercase hex.</summary>
    public string Sha256 { get; }

    /// <summary>The file's size as <see cref="Read"/> found it.</summary>
    public long Bytes { get; private set; }

    /// <summary>The file's last write time (UTC ticks) as <see cref="Read"/> found it.</summary>
    public long Written { get; private set; }

    /// <summary>The line the plan's fingerprint takes from the file: its size, last write time and SHA-256.</summary>
    public string FingerprintLine => string.Join("\t", "file", Bytes.ToString(CultureInfo.InvariantCulture), Written.ToString(CultureInfo.InvariantCulture), Sha256);

    /// <summary>
    /// The source the three arguments name, or null after adding what is wrong with them to <paramref name="violations"/>.
    /// Only the file's form is checked here; <see cref="Read"/> looks for it.
    /// </summary>
    public static ImportSource? Check(string? file, string? filePath, string? sha256, IReadOnlyList<string> extensions, List<string> violations)
    {
        int before = violations.Count;
        string given = file?.Trim() ?? string.Empty;
        string full = filePath ?? string.Empty;
        if (given.Length == 0)
        {
            violations.Add("'file' is required: a path below the import folder.");
        }

        if (full.Length == 0 || !Path.IsPathRooted(full) || !extensions.Any(e => full.EndsWith(e, StringComparison.OrdinalIgnoreCase)))
        {
            violations.Add($"'filePath' must be the full path the Host resolved below its import folder, ending with {string.Join(" or ", extensions)}.");
        }

        if (sha256 is null || sha256.Length != 64 || !sha256.All(c => c is (>= '0' and <= '9') or (>= 'a' and <= 'f')))
        {
            violations.Add("'sha256' must be the file's SHA-256 as 64 lowercase hex digits.");
        }

        return violations.Count == before ? new ImportSource(given, full, sha256!) : null;
    }

    /// <summary>
    /// Finds the file and reads its size and last write time: FILE_NOT_FOUND when it is gone. With
    /// <paramref name="hash"/>, reads it whole: PLAN_STALE when its SHA-256 is no longer the Host's.
    /// </summary>
    public void Read(bool hash, long maxBytes = long.MaxValue)
    {
        var info = new FileInfo(FullPath);
        if (!info.Exists)
        {
            throw new BridgeErrorException(
                ErrorReason.FileNotFound,
                $"The file '{File}' is no longer in the import folder, so nothing was imported. Put it there again, then call again.",
                retryable: false,
                new Dictionary<string, string> { ["file"] = File });
        }

        if (info.Length > maxBytes)
        {
            throw WriteArguments.Invalid(["'file' exceeds the import size limit; use a smaller file below imports."]);
        }

        Observe(info.Length, info.LastWriteTimeUtc.Ticks, hash ? Hash() : Sha256);
    }

    /// <summary>Checks observed file metadata; Small tests supply it without accessing disk.</summary>
    internal void Observe(long bytes, long written, string sha256)
    {
        Bytes = bytes;
        Written = written;
        if (!string.Equals(sha256, Sha256, StringComparison.Ordinal))
        {
            throw new BridgeErrorException(
                ErrorReason.PlanStale,
                $"The file '{File}' changed after it was read, so nothing was imported. Call again once it stays as it is.",
                retryable: false,
                new Dictionary<string, string> { ["file"] = File });
        }
    }

    /// <summary>The file for the change list (tool spec 5): <c>4685200F.DXF (25 KB, sha256 1a2b3c4d5e6f…)</c>.</summary>
    public string Describe() => string.Format(
        CultureInfo.InvariantCulture,
        "{0} ({1}, sha256 {2}…)",
        File.Substring(File.LastIndexOf('/') + 1),
        Size(Bytes),
        Sha256.Substring(0, 12));

    /// <summary>A size as the Host's summaries give it: bytes, whole KB, then MB and GB with one decimal (1 KB = 1024 bytes).</summary>
    internal static string Size(long bytes) => bytes switch
    {
        < 1024 => string.Format(CultureInfo.InvariantCulture, "{0} bytes", bytes),
        < 1024 * 1024 => string.Format(CultureInfo.InvariantCulture, "{0:0} KB", bytes / 1024.0),
        < 1024L * 1024 * 1024 => string.Format(CultureInfo.InvariantCulture, "{0:0.#} MB", bytes / (1024.0 * 1024)),
        _ => string.Format(CultureInfo.InvariantCulture, "{0:0.#} GB", bytes / (1024.0 * 1024 * 1024)),
    };

    private string Hash()
    {
        try
        {
            using var stream = new FileStream(FullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.SequentialScan);
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new BridgeErrorException(
                ErrorReason.InvalidArgument,
                $"Invalid arguments: the file '{File}' cannot be read; another program may have it open. Close it there, then call again.",
                ex);
        }
    }
}

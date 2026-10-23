// Adapted from TIA_Portal_Openness_MCP, src/Studio/Gui/Configuration/ClientConfigurationChange.cs (Apply) and
// ConfigCore.cs (AtomicText). Copyright (c) 2026 bulaofen0036-coder. Licensed under the MIT License.

using System.Globalization;
using System.Text;

namespace EplanMcp.Host.ClientConfig;

/// <summary>
/// A client's file as it was read, to be shown and then written in the same run. <see cref="Apply"/> refuses when the
/// file changed in between (a running client rewrote it); it copies the original next to the file as
/// <c>&lt;file&gt;.bak_&lt;UTC yyyyMMddTHHmmssfffffffZ&gt;</c> (never overwriting or deleting a backup), writes through a
/// temporary file in the same folder, and puts the original back when the write fails, as TIA does. A file that is a
/// symbolic link is refused.
/// </summary>
internal sealed class ClientFileChange
{
    private readonly byte[]? _original;

    private ClientFileChange(string filePath, byte[]? original, string text)
    {
        FilePath = filePath;
        _original = original;
        Text = text;
    }

    public string FilePath { get; }

    /// <summary>The file's text as read: UTF-8 (a byte order mark is dropped), empty when there is no file.</summary>
    public string Text { get; }

    public static ClientFileChange Read(string filePath)
    {
        if (new FileInfo(filePath).LinkTarget is { } target)
        {
            // Replacing the file would replace the link with a copy, and the file it points to would not change.
            throw new ClientConfigException(ClientProblem.InvalidFile, $"{filePath} is a link to {target}; change it by hand.");
        }

        byte[]? original = File.Exists(filePath) ? File.ReadAllBytes(filePath) : null;
        try
        {
            using var reader = new StreamReader(new MemoryStream(original ?? []), new UTF8Encoding(false, throwOnInvalidBytes: true), detectEncodingFromByteOrderMarks: true);
            return new(filePath, original, reader.ReadToEnd());
        }
        catch (DecoderFallbackException ex)
        {
            throw new ClientConfigException(ClientProblem.InvalidFile, "The file is not UTF-8 text.", ex);
        }
    }

    /// <summary>Writes <paramref name="text"/> as UTF-8; the backup's path, or null when there was no file.</summary>
    public string? Apply(string text, DateTimeOffset now)
    {
        byte[]? current = File.Exists(FilePath) ? File.ReadAllBytes(FilePath) : null;
        if (!Same(current))
        {
            throw new ClientConfigException(ClientProblem.FileChanged, $"{FilePath} changed after it was read.");
        }

        string? backup = null;
        if (_original is not null)
        {
            backup = FilePath + ".bak_" + now.UtcDateTime.ToString("yyyyMMdd'T'HHmmssfffffff'Z'", CultureInfo.InvariantCulture);
            // Uninstall can remove local and VM entries from the same file within one clock tick.
            backup = File.Exists(backup) ? backup + "_" + Guid.NewGuid().ToString("N") : backup;
            using var copy = new FileStream(backup, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            copy.Write(_original);
        }

        try
        {
            WriteThroughTemporary(text);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            // A new file is never deleted: if one is there now, another program put it there.
            if (backup is not null && !Same(File.Exists(FilePath) ? File.ReadAllBytes(FilePath) : null))
            {
                Restore(backup, failure);
            }

            throw;
        }

        return backup;
    }

    private bool Same(byte[]? current) => current is null ? _original is null : _original is not null && current.AsSpan().SequenceEqual(_original);

    private void WriteThroughTemporary(string text)
    {
        string folder = Path.GetDirectoryName(Path.GetFullPath(FilePath))!;
        Directory.CreateDirectory(folder);
        string temporary = Path.Combine(folder, $"{Path.GetFileName(FilePath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporary, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            if (File.Exists(FilePath))
            {
                File.Replace(temporary, FilePath, destinationBackupFileName: null);
            }
            else
            {
                File.Move(temporary, FilePath);
            }
        }
        finally
        {
            try
            {
                File.Delete(temporary);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // a stray temporary file beside the configuration, which the client ignores
            }
        }
    }

    private void Restore(string backup, Exception failure)
    {
        try
        {
            File.Copy(backup, FilePath, overwrite: true);
        }
        catch (Exception rollback) when (rollback is IOException or UnauthorizedAccessException)
        {
            throw new IOException($"Writing {FilePath} failed, and so did restoring it; the original is in {backup}.", new AggregateException(failure, rollback));
        }
    }
}

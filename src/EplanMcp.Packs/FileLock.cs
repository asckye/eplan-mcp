using System.Diagnostics;

namespace EplanMcp.Packs;

/// <summary>An exclusive lock file shared between EplanMcp processes: the pack store's, a workspace's.</summary>
internal static class FileLock
{
    /// <summary>
    /// The lock file at <paramref name="path"/> opened without sharing, or null when another holder keeps it longer than
    /// <paramref name="wait"/>. The file stays in place: deleting it on close would let a waiting opener fail while the
    /// deletion is pending.
    /// </summary>
    public static async Task<FileStream?> AcquireAsync(string path, TimeSpan wait, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var waited = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (waited.Elapsed < wait)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken).ConfigureAwait(false);
            }
            catch (IOException)
            {
                return null;
            }
        }
    }
}

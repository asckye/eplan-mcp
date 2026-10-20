using System.Diagnostics;
using System.Security;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// The window macros below the macro directory, found the way SEC-19 requires: folder by folder, never through a
/// symbolic link or junction. Every macro found therefore lies physically below the directory, and no link can lead
/// the scan to a network share or round in a loop. The directory itself may be a link or a network path.
/// </summary>
public static class MacroFiles
{
    /// <summary>The extension of a window macro.</summary>
    public const string Extension = ".ema";

    /// <summary>
    /// Every window macro below <paramref name="root"/>, as paths below it with <c>/</c> between folders, sorted ordinal
    /// ignoring case. Folders that cannot be read are skipped. The scan stops once <paramref name="budget"/> has passed,
    /// with <see cref="MacroScan.Complete"/> false.
    /// </summary>
    public static MacroScan Scan(string root, TimeSpan budget)
    {
        var clock = Stopwatch.StartNew();
        var found = new List<string>();
        var pending = new Stack<(DirectoryInfo Folder, string Path)>();
        pending.Push((new DirectoryInfo(root), string.Empty));
        bool complete = true;
        while (pending.Count > 0)
        {
            if (clock.Elapsed >= budget)
            {
                complete = false;
                break;
            }

            (DirectoryInfo folder, string folderPath) = pending.Pop();
            FileSystemInfo[] entries;
            try
            {
                entries = folder.GetFileSystemInfos();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
            {
                continue;
            }

            foreach (FileSystemInfo entry in entries)
            {
                if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    continue; // a symbolic link or junction could lead out of the macro directory (SEC-19)
                }

                string path = folderPath.Length == 0 ? entry.Name : folderPath + "/" + entry.Name;
                if (entry is DirectoryInfo subfolder)
                {
                    pending.Push((subfolder, path));
                }
                else if (entry.Name.EndsWith(Extension, StringComparison.OrdinalIgnoreCase))
                {
                    found.Add(path);
                }
            }
        }

        found.Sort(StringComparer.OrdinalIgnoreCase);
        return new MacroScan { Paths = found, Complete = complete };
    }

    /// <summary>The file name in a macro path, without the extension.</summary>
    public static string NameOf(string path)
    {
        string file = path.Substring(path.LastIndexOf('/') + 1);
        return file.EndsWith(Extension, StringComparison.OrdinalIgnoreCase) ? file.Substring(0, file.Length - Extension.Length) : file;
    }
}

/// <summary>What <see cref="MacroFiles.Scan"/> found.</summary>
public sealed class MacroScan
{
    /// <summary>The macros' paths below the macro directory, sorted.</summary>
    public List<string> Paths { get; set; } = [];

    /// <summary>False when the scan stopped at its time limit, so macros may be missing.</summary>
    public bool Complete { get; set; }
}

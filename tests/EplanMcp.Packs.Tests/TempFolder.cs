using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace EplanMcp.Packs.Tests;

/// <summary>A temporary folder for Medium tests, deleted afterwards; files are written below it by relative path.</summary>
internal sealed class TempFolder : IDisposable
{
    public TempFolder()
    {
        Root = Directory.CreateTempSubdirectory("eplanmcp-packs-").FullName;
    }

    public string Root { get; }

    public string PathOf(string relative) => Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));

    public string Write(string relative, string content) => Write(relative, Encoding.UTF8.GetBytes(content));

    public string Write(string relative, byte[] content)
    {
        string path = PathOf(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
        return path;
    }

    /// <summary>A directory junction at <paramref name="relative"/> to <paramref name="target"/> (no privilege needed, unlike symbolic links).</summary>
    public string Junction(string relative, string target)
    {
        string link = PathOf(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        using Process mklink = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })!;
        mklink.WaitForExit();
        Assert.True(Directory.Exists(link), "mklink /J failed");
        return link;
    }

    public static string Sha256(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    public void Dispose() => Delete(new DirectoryInfo(Root));

    /// <summary>Removes a junction as a link, never what it points to; clears read-only marks (installed packs have them).</summary>
    private static void Delete(DirectoryInfo directory)
    {
        if ((directory.Attributes & FileAttributes.ReparsePoint) == 0)
        {
            foreach (FileSystemInfo entry in directory.EnumerateFileSystemInfos("*", new EnumerationOptions { AttributesToSkip = 0 }))
            {
                if (entry is DirectoryInfo folder)
                {
                    Delete(folder);
                }
                else
                {
                    entry.Attributes = FileAttributes.Normal;
                    entry.Delete();
                }
            }

            directory.Attributes = FileAttributes.Directory;
        }

        directory.Delete();
    }
}

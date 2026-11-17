using System.Security;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// Takes away what a failed PXF import made at its target (docs/tools/eplan_import_pxf.md, section 5): the link files
/// and the data folder of a project whose path without extension is the target, which the plan found absent. A symbolic
/// link or junction inside is removed itself, never followed, as an export folder is deleted (SEC-20).
/// </summary>
internal static class ImportedProject
{
    private static readonly string[] _linkFiles = [".elk", ".ell"];

    /// <summary>False when something could not be deleted, for example because EPLAN still holds a file.</summary>
    public static bool TryDelete(string target)
    {
        try
        {
            foreach (string link in _linkFiles.Select(e => target + e).Where(File.Exists))
            {
                File.SetAttributes(link, FileAttributes.Normal);
                File.Delete(link);
            }

            var data = new DirectoryInfo(target + ".edb");
            if (data.Exists)
            {
                Delete(data);
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            return false;
        }
    }

    /// <summary>Deletes <paramref name="folder"/> and everything in it; a link or junction is removed itself, never followed.</summary>
    internal static void Delete(DirectoryInfo folder)
    {
        if ((folder.Attributes & FileAttributes.ReparsePoint) == 0)
        {
            foreach (FileSystemInfo entry in folder.GetFileSystemInfos())
            {
                if (entry is DirectoryInfo subfolder)
                {
                    Delete(subfolder);
                }
                else
                {
                    if ((entry.Attributes & (FileAttributes.ReadOnly | FileAttributes.ReparsePoint)) == FileAttributes.ReadOnly)
                    {
                        entry.Attributes &= ~FileAttributes.ReadOnly;
                    }

                    entry.Delete();
                }
            }
        }

        folder.Delete(); // a link or junction: only the link goes
    }
}

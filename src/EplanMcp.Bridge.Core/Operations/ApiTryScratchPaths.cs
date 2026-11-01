namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Checks borrowed-object ownership against the approved path and the project's physical link and database.</summary>
internal static class ApiTryScratchPaths
{
    internal static bool IsApproved(string approved, string project, string linkFile, Func<string, FileAttributes> attributes)
    {
        string extension = Path.GetExtension(linkFile);
        if (approved.Length == 0 || !ProjectResolver.SamePath(approved, project) || !ProjectPaths.IsPlainFullPath(project)
            || !ProjectPaths.IsPlainFullPath(linkFile)
            || !(extension.Equals(".elk", StringComparison.OrdinalIgnoreCase) || extension.Equals(".ell", StringComparison.OrdinalIgnoreCase) || extension.Equals(".elr", StringComparison.OrdinalIgnoreCase))
            || !ProjectResolver.SamePath(project, Path.ChangeExtension(linkFile, null)))
        {
            return false;
        }

        try
        {
            // ProjectFullName has no extension and is not a file (A10/A68); inspect the actual link and database.
            for (string? part = linkFile; part is not null; part = Path.GetDirectoryName(part))
            {
                if ((attributes(part) & FileAttributes.ReparsePoint) != 0)
                {
                    return false;
                }
            }

            return (attributes(Path.ChangeExtension(linkFile, ".edb")) & FileAttributes.ReparsePoint) == 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}

using System.Text;

namespace EplanMcp.Host.Plans;

/// <summary>The data root may be a link; every descendant must be an ordinary file or directory (SEC-19).</summary>
internal sealed class PlanPaths(string dataRoot)
{
    private readonly string _root = Path.GetFullPath(dataRoot);
    public string Root => Check(Path.Combine(_root, "state", "plans"));

    public string ProjectLock(string project) => Check(Path.Combine(DirectoryAt(Root), ".project-" + PlanStore.Hash(Encoding.UTF8.GetBytes(PlanSnapshotHashes.ProjectKey(project))) + ".lock"));

    public string Check(string path)
    {
        string relative = Path.GetRelativePath(_root, path);
        string current = _root;
        foreach (string part in relative.Split(Path.DirectorySeparatorChar))
        {
            if (part is ".." or "." || Path.IsPathRooted(part))
            {
                throw new IOException("A plan path must stay below the data root.");
            }
            current = Path.Combine(current, part);
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new IOException("Plan storage refuses links and junctions.");
                }
            }
            catch (FileNotFoundException) { /* A new ordinary entry can be created here. */ }
            catch (DirectoryNotFoundException) { /* Its parent has not been created yet. */ }
        }
        return path;
    }

    public string DirectoryAt(string path)
    {
        Directory.CreateDirectory(Check(path));
        return Check(path);
    }

    public IEnumerable<string> Files(string directory)
    {
        Check(directory);
        if (!Directory.Exists(directory))
        {
            yield break;
        }
        foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
        {
            Check(entry);
            if (Directory.Exists(entry))
            {
                foreach (string file in Files(entry))
                {
                    yield return file;
                }
            }
            else
            {
                yield return entry;
            }
        }
    }

    public string Record(string id) => Check(Path.Combine(Root, Id(id) + ".json"));
    public string Assets(string id) => Check(Path.Combine(Root, Id(id)));
    public static string Id(string id) => IsHex(id, 32) ? id : throw new ArgumentException("A plan ID must be 32 lowercase hex characters.", nameof(id));
    public static bool IsHex(string value, int length) => value.Length == length && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
}

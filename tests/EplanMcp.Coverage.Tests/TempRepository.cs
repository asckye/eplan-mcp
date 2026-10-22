namespace EplanMcp.Coverage.Tests;

/// <summary>A throwaway repository folder in the temp directory for tests that read repository files.</summary>
internal sealed class TempRepository : IDisposable
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("coverage-repo-");

    public string Root => _directory.FullName;

    public TempRepository Write(string relativePath, string text)
    {
        string path = Path.Combine(Root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return this;
    }

    public string Read(string relativePath) => File.ReadAllText(Path.Combine(Root, relativePath));

    public void Dispose() => _directory.Delete(recursive: true);
}

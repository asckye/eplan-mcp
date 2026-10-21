using System.Reflection;

namespace EplanMcp.Workbench.Tests;

/// <summary>
/// The Workbench's source files, which this project embeds (ported from TIA's tests): scanning the real markup keeps a
/// test from passing forever against a list of its own while the window drifts away from it.
/// </summary>
internal static class SourceScan
{
    private const string Prefix = "Source/";
    private static readonly Lazy<IReadOnlyList<SourceFile>> _files = new(Load);

    /// <summary>Every .xaml file.</summary>
    public static IEnumerable<SourceFile> Markup => _files.Value.Where(f => f.Path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase));

    /// <summary>Every .cs file.</summary>
    public static IEnumerable<SourceFile> Code => _files.Value.Where(f => f.Path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase));

    /// <summary>The files vendored from TIA (src/EplanMcp.Workbench/Tia).</summary>
    public static IEnumerable<SourceFile> Vendored => _files.Value.Where(f => f.Path.StartsWith("Tia/", StringComparison.Ordinal));

    /// <summary>The Workbench's own files: everything but the vendored ones.</summary>
    public static IEnumerable<SourceFile> Own => _files.Value.Where(f => !f.Path.StartsWith("Tia/", StringComparison.Ordinal));

    public static SourceFile Single(string path) => _files.Value.Single(f => f.Path == path);

    private static List<SourceFile> Load()
    {
        Assembly assembly = typeof(SourceScan).Assembly;
        var files = new List<SourceFile>();
        foreach (string name in assembly.GetManifestResourceNames().Where(n => n.StartsWith(Prefix, StringComparison.Ordinal)))
        {
            using Stream stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            files.Add(new SourceFile(name[Prefix.Length..].Replace('\\', '/'), reader.ReadToEnd()));
        }

        // Without the EmbeddedResource items these tests would pass by scanning nothing.
        return files.Count > 0 ? files : throw new InvalidOperationException("No Workbench source is embedded; check EplanMcp.Workbench.Tests.csproj.");
    }
}

/// <summary>A source file: its path below src/EplanMcp.Workbench, with forward slashes, and its text.</summary>
internal sealed record SourceFile(string Path, string Text);

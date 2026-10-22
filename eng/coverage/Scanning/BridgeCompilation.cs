using System.Diagnostics;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace EplanMcp.Coverage.Scanning;

/// <summary>
/// One Roslyn compilation of the add-in (EplanMcp.Bridge.Eplan) together with the sources of Bridge.Core and Contracts,
/// so that a Bridge.Core operation, the adapter interface it calls and the EPLAN members the adapter uses are symbols of
/// the same compilation. The compiler arguments come from the add-in's own build (CoreCompile with
/// SkipCompilerExecution), so the EPLAN assemblies, the .NET Framework reference assemblies and the language version are
/// exactly those of a real build.
/// </summary>
internal static class BridgeCompilation
{
    private static readonly string[] _sourceProjects = ["EplanMcp.Bridge.Core", "EplanMcp.Contracts"];

    public static CSharpCompilation Load(string repositoryRoot, string? eplanBinPath)
    {
        string project = Path.Combine(repositoryRoot, "src", "EplanMcp.Bridge.Eplan", "EplanMcp.Bridge.Eplan.csproj");
        CSharpCommandLineArguments parsed = CSharpCommandLineParser.Default.Parse(
            CompilerArguments(project, eplanBinPath), Path.GetDirectoryName(project)!, sdkDirectory: null);
        IEnumerable<string> sources = parsed.SourceFiles.Select(f => f.Path)
            .Concat(_sourceProjects.SelectMany(p => SourceFiles(Path.Combine(repositoryRoot, "src", p))));
        SyntaxTree[] trees = [.. sources.Select(path => CSharpSyntaxTree.ParseText(SourceText.From(File.ReadAllText(path)), parsed.ParseOptions, path))];

        // The two projects compiled from source here are no longer references.
        MetadataReference[] references = [.. parsed.MetadataReferences
            .Select(r => r.Reference)
            .Where(path => !_sourceProjects.Contains(Path.GetFileNameWithoutExtension(path), StringComparer.OrdinalIgnoreCase))
            .Select(path => MetadataReference.CreateFromFile(path))];
        CSharpCompilation compilation = CSharpCompilation.Create(
            "EplanMcp.Bridge.Eplan", trees, references, parsed.CompilationOptions.WithGeneralDiagnosticOption(ReportDiagnostic.Default));
        Diagnostic[] errors = [.. compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).Take(10)];
        return errors.Length == 0
            ? compilation
            : throw new InvalidOperationException("The Bridge does not compile, so its symbols cannot be trusted:" + Environment.NewLine + string.Join(Environment.NewLine, errors.Select(e => e.ToString())));
    }

    /// <summary>The .cs files of a project folder, without bin and obj.</summary>
    public static IEnumerable<string> SourceFiles(string projectDirectory) =>
        Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !Path.GetRelativePath(projectDirectory, path).Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
            .Order(StringComparer.Ordinal);

    /// <summary>The csc arguments of the add-in's Release build; restores, but builds nothing (BuildProjectReferences=false).</summary>
    private static string[] CompilerArguments(string project, string? eplanBinPath)
    {
        // MSBuild reports its errors on stdout; stderr goes straight to the console.
        var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, UseShellExecute = false };
        string[] args = ["msbuild", project, "-nologo", "-restore", "-t:Compile", "-getItem:CscCommandLineArgs", "-p:Configuration=Release",
            "-p:SkipCompilerExecution=true", "-p:ProvideCommandLineArgs=true", "-p:BuildProjectReferences=false",
            "-p:NonExistentFile=__NonExistentSubDir__\\__NonExistentFile__", .. eplanBinPath is null ? [] : new[] { "-p:EplanBinPath=" + eplanBinPath }];
        args.ToList().ForEach(start.ArgumentList.Add);
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("dotnet could not be started.");
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        int json = output.IndexOf('{', StringComparison.Ordinal);
        if (process.ExitCode != 0 || json < 0)
        {
            throw new InvalidOperationException($"dotnet msbuild failed for {project} (exit code {process.ExitCode}). Is EPLAN installed? Pass --eplan-bin <Bin folder>." + Environment.NewLine + output);
        }

        using JsonDocument document = JsonDocument.Parse(output[json..]);
        string[] compiler = [.. document.RootElement.GetProperty("Items").GetProperty("CscCommandLineArgs").EnumerateArray().Select(a => a.GetProperty("Identity").GetString()!)];
        return compiler.Length > 0 ? compiler : throw new InvalidOperationException("CoreCompile ran but reported no compiler arguments.");
    }
}

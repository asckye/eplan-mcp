using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis.CSharp;

namespace EplanMcp.Coverage.Scanning;

/// <summary>One Bridge operation in catalog/mapping/bridge-usage.json; Kind is the enum value of a shared class (ExportKind.Pdf).</summary>
internal sealed record OperationEntry(string Name, string Class, string? Kind);

/// <summary>
/// catalog/mapping/bridge-usage.json: the EPLAN API members (doc-comment IDs) and actions (A: IDs) the add-in uses, with
/// their use sites and the Bridge operations that reach them. <see cref="SourceHash"/> identifies the Bridge sources it
/// was scanned from, so that a machine without EPLAN can still tell that it is out of date.
/// </summary>
internal sealed class BridgeUsage
{
    public const string RelativePath = "catalog/mapping/bridge-usage.json";

    private static readonly string[] _scannedPaths = ["src/EplanMcp.Bridge.Eplan", "src/EplanMcp.Bridge.Core", "src/EplanMcp.Contracts/OperationNames.cs"];

    /// <summary>File version of the scanned Eplan.EplApi.DataModelu.dll, for example 2026.0.3.25421.</summary>
    public required string EplanApi { get; init; }

    /// <summary>UTC date of the scan, yyyy-MM-dd.</summary>
    public required string ScannedOn { get; init; }

    /// <summary>The last commit that changed the scanned sources, plus "+ uncommitted changes" when the tree had some.</summary>
    public required string ScannedSources { get; init; }

    public required string SourceHash { get; init; }

    public required IReadOnlyList<OperationEntry> Operations { get; init; }

    public required SortedDictionary<string, List<UsageSite>> Usages { get; init; }

    /// <summary>
    /// Whether a use site counts for coverage. Generated building blocks (<c>*.g.cs</c>, written from
    /// catalog/wrappers.yaml) count only where a Bridge operation reaches them: generated but unused code is not an
    /// implementation (docs/design/2026-10-21-api-capability-coverage.md, section 8). Hand-written code that no
    /// operation reaches is infrastructure (add-in start, dispatcher) and counts as before.
    /// </summary>
    public static bool Counts(UsageSite site) => site.Operations.Count > 0 || !IsGenerated(site.File);

    /// <summary>A file the wrapper generator writes: its name ends in <c>.g.cs</c>.</summary>
    public static bool IsGenerated(string file) => file.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// SHA-256 of the tokens (no comments or whitespace) of the add-in, Bridge.Core and OperationNames sources, which are
    /// what a scan reads; a comment or formatting change does not make the scan stale.
    /// </summary>
    public static string ComputeSourceHash(string repositoryRoot)
    {
        string src = Path.Combine(repositoryRoot, "src");
        IEnumerable<string> files = BridgeCompilation.SourceFiles(Path.Combine(src, "EplanMcp.Bridge.Eplan"))
            .Concat(BridgeCompilation.SourceFiles(Path.Combine(src, "EplanMcp.Bridge.Core")))
            .Append(Path.Combine(src, "EplanMcp.Contracts", "OperationNames.cs"));
        var text = new StringBuilder();
        foreach (string file in files.Select(f => Path.GetRelativePath(repositoryRoot, f).Replace('\\', '/')).Order(StringComparer.Ordinal))
        {
            text.Append(file).Append('\n');
            foreach (var token in CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(repositoryRoot, file))).GetRoot().DescendantTokens())
            {
                text.Append(token.Text).Append(' ');
            }

            text.Append('\n');
        }

        return "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    /// <summary>
    /// Which sources a scan read, for people: the last commit that touched them (stable when the stack above it is
    /// rebased) and whether the tree had uncommitted changes to them. "unknown" without git.
    /// </summary>
    public static string DescribeSources(string repositoryRoot)
    {
        string commit = Git(repositoryRoot, ["log", "-1", "--format=%h", "--", .. _scannedPaths]);
        bool changed = Git(repositoryRoot, ["status", "--porcelain", "--", .. _scannedPaths]).Length > 0;
        return (commit.Length == 0 ? "unknown" : commit) + (changed ? " + uncommitted changes" : string.Empty);
    }

    private static string Git(string repositoryRoot, string[] args)
    {
        var start = new ProcessStartInfo("git") { RedirectStandardOutput = true, UseShellExecute = false, WorkingDirectory = repositoryRoot };
        args.ToList().ForEach(start.ArgumentList.Add);
        try
        {
            using Process process = Process.Start(start)!;
            string output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();
            return process.ExitCode == 0 ? output : string.Empty;
        }
        catch (Win32Exception)
        {
            return string.Empty; // no git on this machine
        }
    }
}

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using Microsoft.Extensions.Logging.Abstractions;

namespace EplanMcp.Large.Tests;

/// <summary>
/// project.snapshot on the EPLAN under test through the Host's own BridgeClient (no tool serves it yet,
/// docs/design/2026-10-21-m6-bridge-snapshot.md): a whole snapshot taken chunk by chunk, and its hash computed the way
/// the Host does (design 4.4).
/// </summary>
internal static class LiveSnapshots
{
    /// <summary>A client of the Bridge in EPLAN process <paramref name="processId"/>; it reads, so no policy is bypassed.</summary>
    public static async Task<BridgeClient> BridgeAsync(int processId)
    {
        var bridge = new BridgeClient(new PipeBridgeDiscovery(), new PipeBridgeConnector(), NullLogger<BridgeClient>.Instance);
        await bridge.SelectAsync(processId, TestContext.Current.CancellationToken);
        return bridge;
    }

    /// <summary>Every chunk of the snapshot <paramref name="args"/> asks for, and how long each took.</summary>
    public static async Task<Snapshot> TakeAsync(BridgeClient bridge, ProjectSnapshotArgs args)
    {
        var chunks = new List<ProjectSnapshotData>();
        var clock = Stopwatch.StartNew();
        long? after = null;
        do
        {
            args.AfterPage = after;
            ProjectSnapshotData chunk = await bridge.InvokeAsync<ProjectSnapshotArgs, ProjectSnapshotData>(OperationNames.ProjectSnapshot, args, TestContext.Current.CancellationToken);
            chunks.Add(chunk);
            after = chunk.NextAfterPage;
            Assert.True(chunks.Count <= 1000, "The snapshot does not end.");
        }
        while (after is not null);

        return new Snapshot(chunks, clock.Elapsed);
    }

    /// <summary>The snapshot hash of <paramref name="pages"/> as the Host computes it (design 4.4), written out on purpose.</summary>
    public static string Hash(IEnumerable<SnapshotPage> pages)
    {
        IEnumerable<string> lines = pages.Select(p => $"{p.Type}\t{p.Oid}\t{p.Hash}").Order(StringComparer.Ordinal);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", ["eplanmcp.snapshot/1 standard", .. lines]))));
    }

    /// <summary>SHA-256 of every file of a project's data folder but EPLAN's lock and session files (A16, A35).</summary>
    public static Dictionary<string, string> DesignFiles(string projectPath)
    {
        string data = projectPath + ".edb";
        return Directory.EnumerateFiles(data, "*", SearchOption.AllDirectories)
            .Where(f => !f.EndsWith(".lck", StringComparison.OrdinalIgnoreCase) && !Path.GetFileName(f).StartsWith("User.", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(f => Path.GetRelativePath(data, f), Sha256, StringComparer.OrdinalIgnoreCase);
    }

    // EPLAN keeps the files of an open project open; S31 hashed them this way.
    private static string Sha256(string file)
    {
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    /// <summary>The chunks of one snapshot.</summary>
    public sealed record Snapshot(List<ProjectSnapshotData> Chunks, TimeSpan Elapsed)
    {
        public List<SnapshotPage> Pages => [.. Chunks.SelectMany(c => c.Pages)];

        public string Hash => LiveSnapshots.Hash(Pages);

        public IEnumerable<SnapshotObject> Objects(string kind) => Pages.SelectMany(p => p.Objects).Where(o => o.Kind == kind);

        public SnapshotPage Page(string name) => Assert.Single(Pages, p => p.Name == name);

        public string Describe() =>
            $"{Pages.Count} pages in {Chunks.Count} chunk(s), {Elapsed.TotalSeconds:0.0} s (Bridge {Chunks.Sum(c => c.Milliseconds)} ms, slowest chunk {Chunks.Max(c => c.Milliseconds)} ms); "
            + $"{Objects("function").Count()} functions, {Objects("connection").Count()} connections, {Chunks[^1].SkippedConnections} connections left out";
    }
}

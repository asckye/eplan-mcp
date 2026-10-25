using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Tests;

/// <summary>Mutable live project, independent member-hash oracle, no filesystem or EPLAN.</summary>
internal sealed class PlanGuardReader : IProjectCatalog, ISnapshotReader, ISnapshotSource, IObjectFinder, IPlanGuardFunctionReader
{
    public const string Project = @"D:\Projects\Line1";
    public string Path { get; set; } = Project;
    public string Name => "Line1";
    public string? ProjectGuid => "sample@1";
    public string? ModifiedDate => throw new InvalidOperationException("Guard must not use modification hints.");
    public string? ModifiedTime => throw new InvalidOperationException("Guard must not use modification hints.");
    public int SkippedConnections => 0;
    public int Opens { get; private set; }
    public int Disposals { get; private set; }
    public int Reads { get; private set; }
    public long Milliseconds { get; set; }
    public long PerRead { get; set; }
    public int NameReads { get; private set; }
    public int Finds { get; private set; }
    public IReadOnlyList<string> GuardCandidates { get; private set; } = [];
    public Action<Action>? GuardScan { get; set; }
    public List<PlanFunction> Names { get; } = [];
    public Dictionary<long, List<SnapshotSourceObject>> Content { get; } = new()
    {
        [1] =
        [
            new() { Kind = "page", Identifier = "28/4/1/0", Oid = 1, Name = "+A1/1", PageType = "Circuit", Values = [new(11011, null, "en_US@Original;")] },
            new() { Kind = "function", Identifier = "28/17/2/0", Oid = 2, Name = "+A1-K1", Page = "+A1/1", Category = "Coil", Main = true, X = 10, Y = 20 },
        ],
    };
    public IReadOnlyList<KeyValuePair<long, string>> Pages => [.. Content.Select(p => new KeyValuePair<long, string>(p.Key, p.Value[0].Name!))];
    public IReadOnlyList<OpenProject> ListOpenProjects() => [new() { Name = Name, Path = Path, ReadOnly = true }];
    public ISnapshotSource Open(string projectPath)
    {
        Assert.Equal(Path, projectPath);
        Opens++;
        return this;
    }
    public ISnapshotSource OpenReadOnly(string linkFile) => throw new InvalidOperationException("Must never open a closed project.");
    public IReadOnlyList<SnapshotSourceObject> ReadPage(long pageOid, string profile)
    {
        Assert.Equal(SnapshotProfiles.StandardName, profile);
        Reads++;
        Milliseconds += PerRead;
        return Content[pageOid];
    }
    public IReadOnlyList<SnapshotSourceObject?> Find(string projectPath, IReadOnlyList<ObjectLocator> locators, string profile)
    {
        Finds++;
        return [.. locators.Select(l => Content.Values.SelectMany(p => p).FirstOrDefault(o => o.Kind == l.Kind && o.Oid == l.Oid))];
    }
    public IReadOnlyList<PlanFunction> Read(string projectPath) => Names;
    public IReadOnlyList<PlanFunction> ReadForGuard(string projectPath, IReadOnlyList<string> candidates, Action checkpoint)
    {
        NameReads++;
        GuardCandidates = candidates;
        checkpoint();
        GuardScan?.Invoke(checkpoint);
        checkpoint();
        return Names;
    }
    public void Dispose() => Disposals++;

    public SnapshotObject[] Objects() => [.. Content.Values.SelectMany(p => p)
        .Select(o => SnapshotCanonical.ObjectOf(o, SnapshotCanonical.Registered("standard")!))];
    public static ObjectLocator Locator(SnapshotObject obj, string project = Project) => new()
    {
        Project = project,
        Kind = obj.Kind,
        Type = obj.Type,
        Oid = obj.Oid,
        Name = obj.Name,
        Page = obj.Page,
        Category = obj.Category,
        Pos = obj.Pos,
    };
    public PlanStepGuardArgs Args(bool all = false)
    {
        var scope = new PlanGuardScope { Pages = ["+A1/1"] };
        SnapshotObject[] objects = Objects();
        var args = new PlanStepGuardArgs
        {
            Project = Path,
            ReadScope = scope,
            Preconditions = [new() { Tag = "members", Scope = scope, Hash = MemberHash(Path, objects), Count = objects.Length }],
        };
        if (all)
        {
            args.Preconditions.AddRange(objects.Select(o => new PlanGuardCondition { Tag = "objectHash", Locator = Locator(o, Path), Hash = o.Hash }));
            args.Preconditions.Add(new() { Tag = "pageHash", Locator = Locator(objects[0], Path), Hash = SnapshotCanonical.PageHash("standard", objects) });
            args.Preconditions.Add(new() { Tag = "absent", Kind = "page", Name = "+A1/9" });
            args.Preconditions.Add(new() { Tag = "absent", Kind = "function", Name = "+A1-K9" });
            args.Preconditions.Add(new() { Tag = "resource", Sha256 = new string('a', 64) });
            args.Preconditions.Add(new() { Tag = "binding", Revision = 3, LockDigest = new string('b', 64) });
            args.ResourceHashes.Add(new string('a', 64));
            args.BindingRevision = 3;
            args.LockDigest = new string('b', 64);
        }
        return args;
    }

    public static string MemberHash(string project, IEnumerable<SnapshotObject> objects)
    {
        string full = System.IO.Path.GetFullPath(project);
        string key = (full.EndsWith(".elk", StringComparison.OrdinalIgnoreCase) ? full[..^4] : full).ToUpperInvariant();
        string[] rows = [.. objects.Select(o => JsonSerializer.Serialize(new[]
        {
            string.Create(CultureInfo.InvariantCulture, $"{o.Kind}/{o.Type}/{o.Oid}"), o.Hash,
        })).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n",
            new[] { SnapshotProfiles.Format + " standard", "members", JsonSerializer.Serialize(key) }.Concat(rows)))));
    }
}

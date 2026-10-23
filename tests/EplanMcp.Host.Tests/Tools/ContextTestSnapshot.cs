using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Host.Tests.Tools;

/// <summary>Standard snapshot fixture shared by direct, stdio and stateless HTTP context tests; no EPLAN discovery.</summary>
internal sealed class ContextTestSnapshot : Operation<ProjectSnapshotArgs, ProjectSnapshotData>
{
    public const string ProjectPath = @"D:\Fake\Line1";
    public override string Name => OperationNames.ProjectSnapshot;
    protected override ProjectSnapshotData Execute(ProjectSnapshotArgs args) => Data(args);

    public static ProjectSnapshotData Data(ProjectSnapshotArgs args, bool split = false, bool changed = false)
    {
        var all = new[] { Page("+A10/2", 7, changed), Page("+A1/1", 9, changed) };
        SnapshotPage[] wanted = [.. all.Where(p => args.Pages is null || args.Pages.Contains(p.Name, StringComparer.Ordinal))];
        SnapshotPage[] pages = [.. wanted.Where(p => args.AfterPage is null || p.Oid > args.AfterPage).Take(split ? 1 : 100)];
        bool complete = !split || pages.Length == 0 || pages[^1].Oid == wanted[^1].Oid;
        return new()
        {
            Project = "Line1",
            ProjectPath = ProjectPath,
            ProjectGuid = "sample@1",
            Profile = "standard",
            Format = SnapshotProfiles.Format,
            PageCount = wanted.Length,
            Pages = [.. pages],
            Complete = complete,
            NextAfterPage = complete ? null : pages[^1].Oid,
            MissingPages = [.. (args.Pages ?? []).Except(all.Select(p => p.Name), StringComparer.Ordinal)],
            SnapshotHash = args.Pages is null && args.AfterPage is null && complete ? SnapshotCanonical.SnapshotHash("standard", pages) : null,
            SkippedConnections = 2,
        };
    }

    private static SnapshotPage Page(string name, long oid, bool changed)
    {
        var page = new SnapshotObject { Kind = "page", Type = 4, Oid = oid, Name = name, PageType = "Circuit" };
        var function = new SnapshotObject
        {
            Kind = "function",
            Type = 17,
            Oid = oid + 20,
            Name = name.Split('/')[0] + "-K1",
            Page = name,
            Category = "Coil",
            Main = true,
            Pos = "10,20",
            Values = [new() { Id = 20027, Name = "Function text", Value = changed ? "changed" : "Motor\u202e\u0001" }, new() { Id = 20028, Value = "24 V" }],
        };
        page.Hash = SnapshotCanonical.Sha256(SnapshotCanonical.Text(page));
        function.Hash = SnapshotCanonical.Sha256(SnapshotCanonical.Text(function));
        return new()
        {
            Name = name,
            Type = 4,
            Oid = oid,
            Objects = [function, page],
            Hash = SnapshotCanonical.PageHash("standard", [function, page])
        };
    }
}

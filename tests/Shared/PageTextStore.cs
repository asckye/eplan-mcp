using System.Globalization;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Tests;

/// <summary>Immutable fake inventories and rollback-capable writes, shared by Small and pipe integration tests.</summary>
internal sealed class PageTextStore : IProjectCatalog, IPageTextWriter, ITransactionScopeFactory
{
    public const string Page = "=F+A2&EFA1/2";
    public const string OtherPage = "=F+A2&EFA1/3";

    public OpenProject Project { get; } = new() { Name = "Line1", Path = @"D:\Scratch\Line1" };

    public Dictionary<string, PageTextSnapshot> Pages { get; } = new(StringComparer.Ordinal)
    {
        [Page] = new(Page, "page:1", [Text(1, new("en_US", "Motor Motor"), new("de_DE", "Motor"), new("??_??", "Free"), new("zh_CN", string.Empty)), Text(2, new TextEntry("en_US", "Motor"))]),
        [OtherPage] = new(OtherPage, "page:2", [Text(3, new TextEntry("en_US", "Motor"))]),
    };

    public List<string> Calls { get; } = [];

    public int Reads { get; private set; }

    public List<(string Page, long Oid)> Writes { get; } = [];

    public Func<PageFreeText, PageFreeText>? Readback { get; set; }

    public Exception? ReadFailure { get; set; }

    public Exception? WriteFailure { get; set; }

    public int FailWrite { get; set; } = 2;

    public string? FailTransaction { get; set; }

    public Action? AfterWrite { get; set; }

    public IReadOnlyList<OpenProject> ListOpenProjects() => [Project];

    public string? LanguageCode(string language) => language == "en_US" ? language : null;

    public PageTextSnapshot? Read(string projectPath, string pageName)
    {
        Reads++;
        Assert.Equal(Project.Path, projectPath);
        if (ReadFailure is not null)
        {
            throw ReadFailure;
        }

        return Pages.GetValueOrDefault(pageName);
    }

    public PageFreeText Write(string projectPath, string pageName, PageFreeText target, IReadOnlyList<TextEntry> entries)
    {
        Assert.Equal(Project.Path, projectPath);
        Assert.StartsWith("Begin AI:", Calls[0], StringComparison.Ordinal);
        Writes.Add((pageName, target.Oid));
        var written = new PageFreeText(target.Oid, target.Identifier, entries);
        PageFreeText stored = Readback?.Invoke(written) ?? written;
        PageTextSnapshot page = Pages[pageName];
        Pages[pageName] = new PageTextSnapshot(page.Name, page.Identifier, [.. page.Texts.Select(t => t.Oid == target.Oid ? stored : t)]);
        AfterWrite?.Invoke();
        if (Writes.Count == FailWrite && WriteFailure is not null)
        {
            throw WriteFailure;
        }

        return stored;
    }

    public ITransactionScope Begin(string undoDescription)
    {
        Step("Begin " + undoDescription, "Begin");
        return new Scope(this, Pages.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal));
    }

    public static PageTextReplaceArgs Args() => new()
    {
        Project = "Line1",
        Pages = [Page, OtherPage],
        Find = "Motor",
        Replace = "Drive",
        Language = "en_US",
    };

    public static PageFreeText Text(long oid, params TextEntry[] entries) => new(oid, "text:" + oid.ToString(CultureInfo.InvariantCulture), entries);

    private void Step(string call, string step)
    {
        Calls.Add(call);
        if (FailTransaction == step)
        {
            throw new InvalidOperationException(step + " failed");
        }
    }

    private sealed class Scope(PageTextStore store, Dictionary<string, PageTextSnapshot> before) : ITransactionScope
    {
        public void Commit() => store.Step("Commit", "Commit");

        public void CloseUndo() => store.Step("CloseUndo", "CloseUndo");

        public void Dispose() => store.Step("Dispose", "Dispose");

        public void Rollback()
        {
            store.Step("Rollback", "Rollback");
            store.Pages.Clear();
            foreach (KeyValuePair<string, PageTextSnapshot> page in before)
            {
                store.Pages.Add(page.Key, page.Value);
            }
        }
    }
}

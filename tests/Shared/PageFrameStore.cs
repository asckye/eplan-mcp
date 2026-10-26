using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Tests;

internal sealed class PageFrameStore : IProjectCatalog, IPageReader, IPageDescriptions, IPagePlotFrames, ITransactionScopeFactory
{
    public const string First = "=F+A1&EFS1/1";
    public const string Second = "=F+A1&EFS1/2";
    public const string Third = "=F+A1&EFS1/3";

    public OpenProject Project { get; } = new() { Name = "Line1", Path = @"D:\Scratch\Line1" };
    public Dictionary<string, PagePlotFrameState> Frames { get; } = new(StringComparer.Ordinal)
    {
        [First] = new("page:1", null, "Frame_A"),
        [Second] = new("page:2", "Frame_B", "Frame_B"),
        [Third] = new("page:3", "Frame_A", "Frame_A"),
    };
    public Dictionary<string, List<TextEntry>> Descriptions { get; } = new(StringComparer.Ordinal)
    {
        [First] = [new("en_US", "Power"), new("de_DE", "Netz")],
        [Second] = [new("en_US", "Control")],
        [Third] = [],
    };

    public List<string> Calls { get; } = [];
    public List<string> FrameWrites { get; } = [];
    public List<string> DescriptionWrites { get; } = [];
    public IReadOnlyList<string>? AvailableOverride { get; set; }
    public Func<PagePlotFrameState, PagePlotFrameState>? Tamper { get; set; }
    public string? FailPage { get; set; }
    public Action? AfterFrameWrite { get; set; }
    public int DiscoveryCalls { get; private set; }

    public void Dispose()
    {
    }

    public IReadOnlyList<OpenProject> ListOpenProjects() => [Project];
    public IReadOnlyCollection<string> PageTypes => ["Circuit"];
    public IReadOnlyList<string> PageNames(string projectPath) => [.. Frames.Keys];
    public IReadOnlyList<PageSummary> ListPages(string projectPath, string? namePrefix, string? pageType, string? language) => throw new NotSupportedException();
    public PageContent? ReadPage(string projectPath, string pageName, bool includeProperties, string? language) => throw new NotSupportedException();
    public string? LanguageCode(string? language) => language == "unknown" ? null : language ?? "en_US";

    public IReadOnlyList<string> Available(string projectPath)
    {
        DiscoveryCalls++;
        return AvailableOverride ?? [.. Frames.Values.Select(f => f.Effective).OfType<string>().Distinct(StringComparer.Ordinal)];
    }

    PagePlotFrameState IPagePlotFrames.Read(string projectPath, string page) => Frames[page];

    public PagePlotFrameState Write(string projectPath, string page, string frame)
    {
        Assert.StartsWith("Begin AI: eplan_page_update", Calls[0], StringComparison.Ordinal);
        FrameWrites.Add(page);
        var stored = new PagePlotFrameState(Frames[page].Identifier, frame, frame);
        Frames[page] = Tamper?.Invoke(stored) ?? stored;
        AfterFrameWrite?.Invoke();
        if (page == FailPage)
        {
            throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "Injected frame failure.");
        }

        return Frames[page];
    }

    public PageDescription Read(string projectPath, string pageName)
    {
        List<TextEntry> entries = Descriptions[pageName];
        return new(entries.Count == 0 ? null : string.Concat(entries.Select(e => $"{e.Language}@{e.Text};")), [.. entries]);
    }

    public PageDescription Write(string projectPath, string pageName, IReadOnlyList<TextEntry> entries)
    {
        DescriptionWrites.Add(pageName);
        Descriptions[pageName] = [.. entries];
        return Read(projectPath, pageName);
    }

    public ITransactionScope Begin(string undoDescription)
    {
        Calls.Add("Begin " + undoDescription);
        return new Scope(this, Frames.ToDictionary(p => p.Key, p => p.Value), Descriptions.ToDictionary(p => p.Key, p => p.Value.ToList()));
    }

    public static PageUpdateArgs Args(params PageUpdateItem[] pages) => new() { Project = "Line1", Language = "en_US", Pages = [.. pages] };
    public static PageUpdateItem Item(string name = First, string? frame = "Frame_B", string? description = null) =>
        new() { Name = name, PlotFrame = frame, Description = description };

    private sealed class Scope(PageFrameStore store, Dictionary<string, PagePlotFrameState> frames, Dictionary<string, List<TextEntry>> descriptions) : ITransactionScope
    {
        public void Commit() => store.Calls.Add("Commit");
        public void CloseUndo() => store.Calls.Add("CloseUndo");
        public void Dispose() => store.Calls.Add("Dispose");

        public void Rollback()
        {
            store.Calls.Add("Rollback");
            store.Frames.Clear();
            store.Descriptions.Clear();
            foreach ((string name, PagePlotFrameState state) in frames)
            {
                store.Frames[name] = state;
                store.Descriptions[name] = descriptions[name];
            }
        }
    }
}

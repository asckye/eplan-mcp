using Eplan.EplApi.DataModel;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>S38/A49 frame switch via PAGE_FORMPLOT. Discovery is limited to frames used by project pages.</summary>
internal sealed class EplanPagePlotFrames : IPagePlotFrames
{
    private readonly Dictionary<string, PagePropertyList> _lists = new(StringComparer.Ordinal);
    private EplanPageDescriptions Descriptions => new(Properties);

    public string? LanguageCode(string? language) => Descriptions.LanguageCode(language);
    PageDescription IPageDescriptions.Read(string projectPath, string pageName) => Descriptions.Read(projectPath, pageName);
    public PageDescription Write(string projectPath, string pageName, IReadOnlyList<TextEntry> entries) => Descriptions.Write(projectPath, pageName, entries);

    private PagePropertyList Properties(string projectPath, string page)
    {
        if (!_lists.TryGetValue(page, out PagePropertyList? list))
        {
            list = EplanPageDescriptions.Find(projectPath, page).Properties;
            _lists.Add(page, list);
        }

        return list;
    }

    public void Dispose()
    {
        foreach (PagePropertyList list in _lists.Values)
        {
            list.Dispose();
        }
    }

    public IReadOnlyList<string> Available(string projectPath)
    {
        MainThread.Assert();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (Page page in EplanProjects.ByPath(projectPath).Pages)
        {
            OperationContext.Current.ThrowIfCancellationRequested();
            if (page.PlotFrame is { } frame)
            {
                names.Add(frame.Name);
            }
        }

        return [.. names.OrderBy(n => n, StringComparer.Ordinal)];
    }

    public PagePlotFrameState Read(string projectPath, string page)
    {
        MainThread.Assert();
        Page target = EplanPageDescriptions.Find(projectPath, page);
        PagePropertyList properties = Properties(projectPath, page);
        using AnyPropertyId id = 11016;
        using PropertyValue value = properties[id];
        return new(target.ToStringIdentifier(), value.IsEmpty ? null : value.ToString(), target.PlotFrame?.Name);
    }

    public PagePlotFrameState Write(string projectPath, string page, string frame)
    {
        MainThread.Assert();
        PagePropertyList properties = Properties(projectPath, page);
        using AnyPropertyId id = 11016;
        using var value = new PropertyValue();
        value.Set(frame);
        properties[id] = value;
        return Read(projectPath, page);
    }
}

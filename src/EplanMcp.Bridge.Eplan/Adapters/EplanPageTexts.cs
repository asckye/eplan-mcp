using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.Graphics;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>S38/A49: exact page-origin Graphics.Text, including groups; frame descendants are excluded first.</summary>
internal sealed class EplanPageTexts : IPageTextWriter
{
    public string? LanguageCode(string language)
    {
        MainThread.Assert();
        using var code = new ISOCode(language);
        return code.IsValid() ? code.GetString() : null;
    }

    public PageTextSnapshot? Read(string projectPath, string pageName)
    {
        MainThread.Assert();
        Page? page = Find(projectPath, pageName);
        return page is null ? null : new PageTextSnapshot(page.Name, page.ToStringIdentifier(),
            [.. FreeTexts(page).Select(ReadText)]);
    }

    public PageFreeText Write(string projectPath, string pageName, PageFreeText target, IReadOnlyList<TextEntry> entries)
    {
        MainThread.Assert();
        Page page = Find(projectPath, pageName) ?? throw Missing();
        Text text = FreeTexts(page).SingleOrDefault(t => t.ObjectIdentifier == target.Oid &&
            string.Equals(t.ToStringIdentifier(), target.Identifier, StringComparison.Ordinal)) ?? throw Missing();
        using var content = new MultiLangString();
        foreach (TextEntry entry in entries)
        {
            OperationContext.Current.ThrowIfCancellationRequested();
            content.AddString(Number(entry.Language), entry.Text);
        }

        text.Contents = content;
        return ReadText(text);
    }

    private static Page? Find(string projectPath, string pageName)
    {
        using var finder = new DMObjectsFinder(EplanProjects.ByPath(projectPath));
        using var filter = new PagesFilter { Name = pageName, ExactNameMatching = true };
        return finder.GetPages(filter).SingleOrDefault(p => string.Equals(p.Name, pageName, StringComparison.Ordinal));
    }

    private static List<Text> FreeTexts(Page page)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<Text>();
        void Walk(IEnumerable<Placement> placements, bool editable)
        {
            foreach (Placement placement in placements)
            {
                OperationContext.Current.ThrowIfCancellationRequested();
                if (!seen.Add(placement.ToStringIdentifier()))
                {
                    continue;
                }

                if (placement is Group group)
                {
                    Walk(group.SubPlacements, editable);
                }

                if (editable && placement.GetType() == typeof(Text))
                {
                    result.Add((Text)placement);
                }
            }
        }

        // Shared placements must already be marked before walking the page; no frame text becomes editable (S38).
        if (page.PlotFrame is { } frame)
        {
            Walk(frame.SubPlacements, editable: false);
        }

        Walk(page.AllPlacements, editable: true);
        return result;
    }

    private static PageFreeText ReadText(Text text)
    {
        using MultiLangString content = text.Contents;
        var languages = new LanguageList();
        int count = content.GetLanguageList(ref languages);
        var entries = new List<TextEntry>();
        for (int i = 0; i < count; i++)
        {
            ISOCode.Language language = languages.get_Language(i);
            using var code = new ISOCode(language);
            entries.Add(new TextEntry(code.GetString(), content.GetString(language)));
        }

        return new PageFreeText(text.ObjectIdentifier, text.ToStringIdentifier(), entries);
    }

    private static ISOCode.Language Number(string language)
    {
        if (language == "??_??")
        {
            return ISOCode.Language.L___;
        }

        using var code = new ISOCode(language);
        return code.GetNumber();
    }

    private static BridgeErrorException Missing() => new(ErrorReason.EplanOperationFailed,
        "A planned page free text no longer exists; all changes were rolled back. Call eplan_page_text_replace again for a new plan.");
}

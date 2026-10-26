using System.Globalization;
using EplanMcp.Contracts;
using Newtonsoft.Json;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// The checked arguments of page.update.plan and page.update.execute, with each page found by its name the way
/// eplan_page_get finds it: exactly, or ignoring case when only one page matches (docs/tools/eplan_page_update.md).
/// </summary>
internal sealed class PageUpdateRequest
{
    public const int MaxPages = 50;
    public const int MaxDescription = 500;

    private PageUpdateRequest(OpenProject project, string language, List<Page> pages)
    {
        Project = project;
        Language = language;
        Pages = pages;
    }

    public OpenProject Project { get; }

    /// <summary>The code of the language that changes, also when the tool's language was omitted.</summary>
    public string Language { get; }

    public IReadOnlyList<Page> Pages { get; }

    public Dictionary<string, PagePlotFrameState> Frames { get; } = new(StringComparer.Ordinal);

    private List<string> AvailableFrames { get; set; } = [];

    public static PageUpdateRequest Check(PageUpdateArgs args, IProjectCatalog catalog, IPageReader reader, IPageDescriptions descriptions, IPagePlotFrames? frames = null)
    {
        var violations = new List<string>();
        WriteArguments.RequireProject(args.Project, violations);
        List<PageUpdateItem> items = args.Pages ?? [];
        if (items.Count is < 1 or > MaxPages)
        {
            violations.Add($"'pages' must hold 1 to {MaxPages.ToString(CultureInfo.InvariantCulture)} pages.");
        }

        for (int i = 0; i < items.Count && i < MaxPages; i++)
        {
            string at = "pages[" + i.ToString(CultureInfo.InvariantCulture) + "]";
            if (items[i] is null)
            {
                violations.Add($"{at} must be a page object.");
                continue;
            }

            int same = items.FindIndex(0, i, p => string.Equals(p?.Name, items[i].Name, StringComparison.OrdinalIgnoreCase));
            AddIf(violations, string.IsNullOrEmpty(items[i].Name), $"{at}.name is required: a full page name as eplan_page_list returns it.");
            AddIf(violations, same >= 0 && !string.IsNullOrEmpty(items[i].Name), $"{at}.name repeats pages[{same.ToString(CultureInfo.InvariantCulture)}].name (letter case does not count).");
            AddIf(violations, items[i].Description is null && items[i].PlotFrame is null, $"{at}.description is required when plot_frame is omitted; an empty string clears it.");
            AddIf(violations, items[i].Description?.Length > MaxDescription, $"{at}.description must have at most {MaxDescription.ToString(CultureInfo.InvariantCulture)} characters.");
            AddIf(violations, items[i].PlotFrame is not null && string.IsNullOrWhiteSpace(items[i].PlotFrame), $"{at}.plot_frame must be a nonempty exact frame name.");
        }

        string? language = descriptions.LanguageCode(string.IsNullOrWhiteSpace(args.Language) ? null : args.Language!.Trim());
        AddIf(violations, language is null, "'language' is not a language EPLAN knows; use a code like zh_CN or en_US, or omit it for EPLAN's user interface language.");
        if (violations.Count > 0)
        {
            throw WriteArguments.Invalid(violations);
        }

        OpenProject project = WriteArguments.Writable(catalog, args.Project);
        IReadOnlyList<string> names = reader.PageNames(project.Path);
        var missing = new List<string>();
        var pages = new List<Page>();
        foreach (PageUpdateItem item in items)
        {
            OperationContext.Current.ThrowIfCancellationRequested();
            string? name = Find(names, item.Name);
            if (name is null)
            {
                missing.Add(item.Name);
            }
            else
            {
                pages.Add(new Page(name, item.Description?.Length == 0 ? null : item.Description, item.Description is not null, item.PlotFrame));
            }
        }

        if (missing.Count > 0)
        {
            throw new BridgeErrorException(
                args.Fingerprint is not null && items.Any(p => p.PlotFrame is not null) ? ErrorReason.PlanStale : ErrorReason.PageNotFound,
                "No page has these names, so nothing was changed. Use eplan_page_list with name_prefix to find the exact page names.",
                retryable: false,
                new Dictionary<string, string> { ["project"] = project.Name, ["missing"] = string.Join("\n", missing) });
        }

        var request = new PageUpdateRequest(project, language!, pages);
        if (pages.Any(p => p.PlotFrame is not null))
        {
            if (frames is null)
            {
                throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "Plot-frame access is unavailable; ask the engineer to update the Bridge.");
            }

            request.AvailableFrames = frames.Available(project.Path).Distinct(StringComparer.Ordinal).OrderBy(n => n, StringComparer.Ordinal).ToList();
            foreach (Page page in pages.Where(p => p.PlotFrame is not null))
            {
                OperationContext.Current.ThrowIfCancellationRequested();
                if (!request.AvailableFrames.Contains(page.PlotFrame!, StringComparer.Ordinal))
                {
                    throw new BridgeErrorException(args.Fingerprint is null ? ErrorReason.InvalidArgument : ErrorReason.PlanStale,
                        "The requested frame is not among frames used by project pages. Choose an available name and make a new plan; nothing was changed.",
                        metadata: new Dictionary<string, string>
                        {
                            ["available"] = string.Join("\n", request.AvailableFrames.Take(20).Select(n => n.Length > 200 ? n.Substring(0, 200) : n)),
                            ["availableCount"] = request.AvailableFrames.Count.ToString(CultureInfo.InvariantCulture),
                        });
                }

                request.Frames[page.Name] = frames.Read(project.Path, page.Name);
            }
        }

        return request;
    }

    /// <summary>The plan's state: each page's name and raw description, so any change to them is PLAN_STALE.</summary>
    public string FingerprintOf(IEnumerable<(Page Page, PageDescription Before)> pages)
    {
        string descriptions = Fingerprint.Of(pages.Select(p => p.Page.Name + "\t" + (p.Before.Raw ?? string.Empty)));
        return Frames.Count == 0 ? descriptions : Fingerprint.Of([descriptions,
            JsonConvert.SerializeObject(new { Project.Path, Frames = Frames.OrderBy(p => p.Key, StringComparer.Ordinal), AvailableFrames })]);
    }

    public bool FrameChanges(Page page) => page.PlotFrame is not null && Frames[page.Name].Effective != page.PlotFrame;

    /// <summary>The exact name, or the one name equal ignoring case; AMBIGUOUS_NAME when several are.</summary>
    internal static string? Find(IReadOnlyList<string> names, string wanted)
    {
        if (names.Contains(wanted, StringComparer.Ordinal))
        {
            return wanted;
        }

        string[] matches = [.. names.Where(n => string.Equals(n, wanted, StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.Ordinal)];
        return matches.Length > 1
            ? throw ProjectResolver.Ambiguous("page", wanted, matches, "Several pages have this name in different letter case; pass one of the candidates exactly.")
            : matches.FirstOrDefault();
    }

    private static void AddIf(List<string> violations, bool condition, string violation)
    {
        if (condition)
        {
            violations.Add(violation);
        }
    }

    /// <summary>One page to change: its name as EPLAN has it and the new text (null clears it).</summary>
    internal sealed class Page(string name, string? text, bool hasDescription, string? plotFrame)
    {
        public string Name { get; } = name;

        public string? Text { get; } = text;

        public bool HasDescription { get; } = hasDescription;

        public string? PlotFrame { get; } = plotFrame;
    }
}

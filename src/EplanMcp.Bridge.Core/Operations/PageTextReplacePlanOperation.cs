using System.Globalization;
using EplanMcp.Contracts;
using Newtonsoft.Json;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Plans bounded literal replacements through a read-only adapter; docs/tools/eplan_page_text_replace.md.</summary>
public sealed class PageTextReplacePlanOperation(IProjectCatalog catalog, IPageTextReader reader) : Operation<PageTextReplaceArgs, WritePlan>
{
    public override string Name => OperationNames.PageTextReplacePlan;

    protected override WritePlan Execute(PageTextReplaceArgs args)
    {
        string language = Check(args);
        OpenProject project = WriteArguments.Writable(catalog, args.Project);
        var pages = new List<PageTextSnapshot>();
        var missing = new List<string>();
        foreach (string name in args.Pages!.OrderBy(n => n, StringComparer.Ordinal))
        {
            OperationContext.Current.ThrowIfCancellationRequested();
            if (reader.Read(project.Path, name) is { } page)
            {
                pages.Add(page);
            }
            else
            {
                missing.Add(name);
            }
        }

        if (missing.Count > 0)
        {
            throw new BridgeErrorException(ErrorReason.PageNotFound,
                "Some exact page names were not found. Use eplan_page_list to check them; nothing was changed.",
                metadata: new Dictionary<string, string> { ["missing"] = string.Join("\n", missing) });
        }

        var changes = new List<PageTextChange>();
        foreach (PageTextSnapshot page in pages)
        {
            foreach (PageFreeText text in page.Texts.OrderBy(t => t.Oid))
            {
                OperationContext.Current.ThrowIfCancellationRequested();
                foreach (TextEntry entry in text.Entries.OrderBy(e => e.Language, StringComparer.Ordinal))
                {
                    if (language != "all" && !string.Equals(language, entry.Language, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    (string after, int occurrences) = LiteralTextReplace.Apply(entry.Text, args.Find!, args.Replace!, args.CaseSensitive);
                    if (string.Equals(entry.Text, after, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    changes.Add(new PageTextChange
                    {
                        Page = page.Name,
                        Oid = text.Oid,
                        Language = entry.Language,
                        Before = entry.Text,
                        After = after,
                        Occurrences = occurrences,
                    });
                    if (changes.Count > args.MaxChanges)
                    {
                        throw new BridgeErrorException(ErrorReason.InvalidArgument,
                            "The change cap was exceeded; narrow pages or language and call again. Nothing was changed.",
                            metadata: new Dictionary<string, string>
                            {
                                ["limit"] = args.MaxChanges.ToString(CultureInfo.InvariantCulture),
                                ["foundAtLeast"] = changes.Count.ToString(CultureInfo.InvariantCulture),
                            });
                    }
                }
            }
        }

        if (changes.Count == 0)
        {
            throw InvalidArgument("No stored text would change. Check 'find', 'language' and exact page names, then call again.");
        }

        return new WritePlan
        {
            Project = project.Name,
            ProjectPath = project.Path,
            PageTextChanges = changes,
            Changes = [.. changes.Select(c => new PlannedChange
            {
                Action = "update",
                Kind = "page",
                Name = c.Page,
                Property = $"text {c.Oid.ToString(CultureInfo.InvariantCulture)} ({c.Language})",
                OldValue = c.Before,
                NewValue = c.After,
            })],
            Fingerprint = StateFingerprint(project, pages),
        };
    }

    // JSON escapes separators in raw text; each page, including empty ones, remains a distinct hash input.
    internal static string StateFingerprint(OpenProject project, IEnumerable<PageTextSnapshot> pages) =>
        Fingerprint.Of(pages.Select(p => JsonConvert.SerializeObject(new
        {
            project.Path,
            p.Name,
            p.Identifier,
            Texts = p.Texts.OrderBy(t => t.Oid).Select(t => new
            {
                t.Oid,
                t.Identifier,
                Entries = t.Entries.OrderBy(e => e.Language, StringComparer.Ordinal),
            }),
        })));

    private string Check(PageTextReplaceArgs args)
    {
        var violations = new List<string>();
        WriteArguments.RequireProject(args.Project, violations);
        AddIf(violations, args.Pages is not { Count: >= 1 and <= 50 }, "'pages' must contain 1 to 50 exact full page names.");
        AddIf(violations, args.Pages?.Any(string.IsNullOrWhiteSpace) == true, "'pages' must not contain empty names.");
        AddIf(violations, args.Pages?.Distinct(StringComparer.Ordinal).Count() != args.Pages?.Count, "'pages' must not repeat a name.");
        AddIf(violations, args.Find is not { Length: >= 1 and <= 500 }, "'find' must contain 1 to 500 literal characters.");
        AddIf(violations, args.Replace is not { Length: <= 500 }, "'replace' is required, at most 500 characters; empty removes matches.");
        AddIf(violations, string.Equals(args.Find, args.Replace, StringComparison.Ordinal), "'find' and 'replace' must differ.");
        AddIf(violations, args.MaxChanges is < 1 or > 500, "'max_changes' must be between 1 and 500.");
        string? language = args.Language is "all" or "??_??" ? args.Language
            : string.IsNullOrWhiteSpace(args.Language) ? null : reader.LanguageCode(args.Language!);
        AddIf(violations, language is null, "'language' must be an explicit known code, ??_??, or all.");
        if (violations.Count > 0)
        {
            throw WriteArguments.Invalid(violations);
        }

        return language!;
    }

    private static void AddIf(List<string> violations, bool condition, string violation)
    {
        if (condition)
        {
            violations.Add(violation);
        }
    }
}

using System.Globalization;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Replans and verifies free-text replacements in one main-thread call and one transaction (A49).</summary>
public sealed class PageTextReplaceExecuteOperation(IProjectCatalog catalog, IPageTextWriter texts, ITransactionScopeFactory transactions)
    : Operation<PageTextReplaceArgs, PageTextReplaceData>, IWriteOperation
{
    /// <summary>The single undo step is named AI: eplan_page_text_replace.</summary>
    public const string Tool = "eplan_page_text_replace";

    public override string Name => OperationNames.PageTextReplaceExecute;

    protected override PageTextReplaceData Execute(PageTextReplaceArgs args)
    {
        WritePlan plan;
        IReadOnlyList<PageTextSnapshot> snapshots;
        try
        {
            if (string.IsNullOrEmpty(args.Fingerprint))
            {
                throw InvalidArgument("'fingerprint' must be the value returned by page.text.replace.plan.");
            }

            plan = new PageTextReplacePlanOperation(catalog, texts).Build(args, args.Fingerprint, out snapshots);
        }
        catch (BridgeErrorException ex)
        {
            throw WriteArguments.NotStarted(ex);
        }

        Committed<List<PageTextChange>> committed = TransactionRunner.Run(transactions, Tool, () =>
        {
            foreach (IGrouping<(string Page, long Oid), PageTextChange> group in plan.PageTextChanges!.GroupBy(c => (c.Page, c.Oid)))
            {
                OperationContext.Current.ThrowIfCancellationRequested();
                PageFreeText before = snapshots.Single(p => p.Name == group.Key.Page).Texts.Single(t => t.Oid == group.Key.Oid);
                Dictionary<string, string> replacements = group.ToDictionary(c => c.Language, c => c.After, StringComparer.Ordinal);
                List<TextEntry> expected = [.. before.Entries.Select(e => new TextEntry(e.Language,
                    replacements.TryGetValue(e.Language, out string? replacement) ? replacement : e.Text))];
                PageFreeText after = texts.Write(plan.ProjectPath, group.Key.Page, before, expected);
                string? differs = expected.Concat(after.Entries).Select(e => e.Language).FirstOrDefault(language =>
                    !expected.Where(e => e.Language == language).Select(e => e.Text)
                        .SequenceEqual(after.Entries.Where(e => e.Language == language).Select(e => e.Text), StringComparer.Ordinal));
                if (after.Oid != before.Oid || after.Identifier != before.Identifier || differs is not null)
                {
                    throw new BridgeErrorException(ErrorReason.EplanOperationFailed,
                        "A free text's identity or stored languages did not read back as planned; all changes were rolled back. Ask the engineer to check the text.",
                        metadata: new Dictionary<string, string>
                        {
                            ["page"] = group.Key.Page,
                            ["oid"] = group.Key.Oid.ToString(CultureInfo.InvariantCulture),
                            ["language"] = differs ?? string.Empty,
                        });
                }
            }

            return plan.PageTextChanges!;
        });
        return new PageTextReplaceData { Project = plan.Project, Changes = committed.Result, Warning = committed.Warning };
    }
}

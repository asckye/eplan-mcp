using System.Globalization;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>One card as the rack will draw it: where its picture goes, how wide it is, and its overview page.</summary>
public sealed class PlcRackCardPlan(string tag, string part, string? partVariant, string macro, string overviewPage, int overviewVariant,
    int pictureVariant, double rackX, double width, IReadOnlyList<string> extraParts, IReadOnlyList<PlcRackSubDevice> subDevices)
{
    public string Tag { get; } = tag;

    public string Part { get; } = part;

    public string? PartVariant { get; } = partVariant;

    /// <summary>The window macro, relative to the macro directory, as the part names it.</summary>
    public string Macro { get; } = macro;

    public string OverviewPage { get; } = overviewPage;

    public int OverviewVariant { get; } = overviewVariant;

    public int PictureVariant { get; } = pictureVariant;

    /// <summary>The picture's insertion x on the rack page: the previous picture's x plus its width.</summary>
    public double RackX { get; } = rackX;

    /// <summary>The picture's width from its macro.</summary>
    public double Width { get; } = width;

    public IReadOnlyList<string> ExtraParts { get; } = extraParts;

    public IReadOnlyList<PlcRackSubDevice> SubDevices { get; } = subDevices;
}

/// <summary>
/// The checked arguments of plc.rack.plan and plc.rack.execute (docs/tools/eplan_plc_rack.md, sections 3 and 4). Every
/// argument problem is reported at once; then each card's part is resolved to its window macro, the pictures are laid
/// side by side from the macros' widths, and all insertions (the overviews first, then the pictures, A122) are checked
/// as one eplan_macro_insert batch, which checks page types, variants, outlines and the layout guard. The rack page and
/// the overview pages must be empty, and no function may carry a card's or sub-device's tag yet.
/// </summary>
internal sealed class PlcRackRequest
{
    public const int MaxCards = 20;
    private static readonly double[] _origin = [24, 264];
    private static readonly double[] _overview = [176, 264];

    private PlcRackRequest(OpenProject project, string rackPage, List<PlcRackCardPlan> cards, List<MacroInsertRequest> inserts)
    {
        Project = project;
        RackPage = rackPage;
        Cards = cards;
        Overviews = inserts.Take(cards.Count).ToList();
        Pictures = inserts.Skip(cards.Count).ToList();
    }

    public OpenProject Project { get; }

    public string RackPage { get; }

    public IReadOnlyList<PlcRackCardPlan> Cards { get; }

    /// <summary>The overview insertion of each card, in card order.</summary>
    public IReadOnlyList<MacroInsertRequest> Overviews { get; }

    /// <summary>The picture insertion of each card, in card order.</summary>
    public IReadOnlyList<MacroInsertRequest> Pictures { get; }

    public static PlcRackRequest Check(PlcRackArgs args, IProjectCatalog catalog, IPageReader pages, IMacroReader macros, IMacroInserter inserter, IDeviceRenamer names)
    {
        List<string> violations = Shape(args);
        if (violations.Count > 0)
        { throw WriteArguments.Invalid(violations); }

        OpenProject project = WriteArguments.Writable(catalog, args.Project);
        string root = macros.MacroRoot() is { } resolved && Directory.Exists(resolved)
            ? resolved
            : throw new BridgeErrorException(ErrorReason.EplanOperationFailed, MacroListOperation.NoMacroDirectory);
        double[] origin = args.Origin ?? _origin, overview = args.OverviewPosition ?? _overview;
        var cards = new List<PlcRackCardPlan>();
        double x = origin[0];
        foreach (PlcRackCard card in args.Cards!)
        {
            string? partVariant = card.PartVariant?.Trim() is { Length: > 0 } v ? v : null;
            string macro = macros.ResolvePartMacro(card.Part!.Trim(), partVariant).Macro;
            if (string.IsNullOrWhiteSpace(macro))
            {
                throw new BridgeErrorException(ErrorReason.MacroNotFound,
                    $"Part {card.Part!.Trim()} names no window macro (ARTICLE_GROUPSYMBOLMACRO); a rack card needs one.", retryable: false);
            }

            int picture = card.PictureVariant!.Value;
            double width = macros.ReadMacro(project.Path, MacroFiles.Resolve(root, macro), representation: MacroInsertRequest.MultiLine, variant: picture).Width;
            cards.Add(new(card.Tag!.Trim(), card.Part!.Trim(), partVariant, macro, card.OverviewPage!, card.OverviewVariant ?? 0, picture, x, width,
                [.. (card.ExtraParts ?? []).Select(p => p.Trim())], [.. card.SubDevices ?? []]));
            x += width;
        }

        // Before the batch check, whose outline rule would refuse the last pictures without saying to split the rack.
        double right = cards.Max(c => c.RackX + c.Width);
        if (inserter.ReadPage(project.Path, args.RackPage!)?.Width is { } pageWidth && right > pageWidth)
        {
            throw WriteArguments.Invalid([string.Format(CultureInfo.InvariantCulture,
                "The rack is {0:0.#} mm wide and ends at x = {1:0.#}, beyond the page ({2:0.#} mm); split the cards over two rack pages.",
                right - origin[0], right, pageWidth)]);
        }

        var batch = new MacroInsertArgs
        {
            Project = args.Project,
            LayoutGuard = "warn", // the pictures touch by design
            Items =
            [
                .. cards.Select(c => Item(c.OverviewPage, c, "Overview", c.OverviewVariant, overview[0], overview[1])),
                .. cards.Select(c => Item(args.RackPage!, c, MacroInsertRequest.MultiLine, c.PictureVariant, c.RackX, origin[1])),
            ],
        };
        List<MacroInsertRequest> inserts = MacroInsertRequest.CheckAll(batch, catalog, pages, macros, inserter);
        violations.AddRange(inserts.Select(r => r.Page).GroupBy(p => p.Name, StringComparer.Ordinal).Select(g => g.First())
            .Where(p => p.Objects.Count > 0)
            .Select(p => $"Page '{p.Name}' is not empty ({p.Objects.Count.ToString(CultureInfo.InvariantCulture)} objects); a rack draws only on empty pages."));
        var taken = new HashSet<string>(names.FunctionNames(project.Path), StringComparer.OrdinalIgnoreCase);
        violations.AddRange(cards.SelectMany(c => c.SubDevices.Select(s => s.Tag!.Trim()).Prepend(c.Tag))
            .Where(taken.Contains)
            .Select(tag => $"Tag '{tag}' is already used; a rack card and its sub-devices need new tags."));
        return violations.Count > 0 ? throw WriteArguments.Invalid(violations) : new PlcRackRequest(project, inserts[cards.Count].Page.Name, cards, inserts);
    }

    /// <summary>The changes a preview lists: per card, the device with its overview page, rack position and parts.</summary>
    public List<PlannedChange> Changes() =>
    [
        .. Cards.Select(c => new PlannedChange
        {
            Action = "create",
            Kind = "plc_card",
            Name = c.Tag,
            Property = "part",
            NewValue = string.Join(", ", c.ExtraParts.Prepend(c.Part)) + "; " + string.Format(CultureInfo.InvariantCulture, "overview on {0} (variant {1}); picture variant {2} at x = {3:0.#} on {4}, {5:0.#} mm wide{6}",
                c.OverviewPage, c.OverviewVariant, c.PictureVariant, c.RackX, RackPage, c.Width,
                c.SubDevices.Count == 0 ? string.Empty : "; sub-devices " + string.Join(", ", c.SubDevices.Select(s => s.Tag!.Trim()))),
        }),
    ];

    /// <summary>What the execution must still find: the plan and the inserted macros' content as the checks read it.</summary>
    public string Fingerprint() => Operations.Fingerprint.Of(
        Changes().Select(c => c.Name + "|" + c.NewValue)
            .Concat(Overviews.Concat(Pictures).Select(r => r.Page.Name + "|" + r.Page.Objects.Count.ToString(CultureInfo.InvariantCulture))));

    private static MacroInsertItem Item(string page, PlcRackCardPlan card, string representation, int variant, double x, double y) => new()
    {
        Page = page,
        Part = card.Part,
        PartVariant = card.PartVariant,
        Representation = representation,
        Variant = variant,
        X = x,
        Y = y,
        Numbering = "keep",
        MacroBox = false,
    };

    private static List<string> Shape(PlcRackArgs args)
    {
        var violations = new List<string>();
        WriteArguments.RequireProject(args.Project, violations);
        if (string.IsNullOrWhiteSpace(args.RackPage))
        { violations.Add("'rack_page' is required: the full name of an empty Circuit page, as eplan_page_list returns it."); }
        if (Point(args.Origin) is { } origin)
        { violations.Add("'origin' " + origin); }
        if (Point(args.OverviewPosition) is { } overview)
        { violations.Add("'overview_position' " + overview); }
        if (args.Cards is not { Count: > 0 and <= MaxCards })
        {
            violations.Add($"'cards' needs 1 to {MaxCards} cards, left to right.");
            return violations;
        }

        for (int i = 0; i < args.Cards.Count; i++)
        {
            string at = "/cards/" + i.ToString(CultureInfo.InvariantCulture);
            PlcRackCard? card = args.Cards[i];
            if (card is null)
            {
                violations.Add(at + ": supply a card object.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(card.Tag))
            { violations.Add(at + "/tag: supply the card's full device tag, such as =K1+UC1-KE3."); }
            if (string.IsNullOrWhiteSpace(card.Part) || card.Part.Any(char.IsControl))
            { violations.Add(at + "/part: supply the card's part number."); }
            if (string.IsNullOrWhiteSpace(card.OverviewPage))
            { violations.Add(at + "/overview_page: supply the full name of an empty Overview page for this card."); }
            if (card.PictureVariant is not >= 0)
            { violations.Add(at + "/picture_variant: supply the multi-line variant of the rack picture (0 or more; eplan_macro_get lists them)."); }
            if (card.OverviewVariant is < 0)
            { violations.Add(at + "/overview_variant: must be 0 or more."); }
            if (card.ExtraParts?.Any(string.IsNullOrWhiteSpace) == true)
            { violations.Add(at + "/extra_parts: every entry must be a part number."); }
            foreach ((PlcRackSubDevice? sub, int j) in (card.SubDevices ?? []).Select((s, j) => (s, j)))
            {
                if (sub is null || string.IsNullOrWhiteSpace(sub.MacroSuffix) || string.IsNullOrWhiteSpace(sub.Tag))
                { violations.Add(at + "/sub_devices/" + j.ToString(CultureInfo.InvariantCulture) + ": supply macro_suffix (such as -K1) and the new full tag."); }
            }
        }

        violations.AddRange(args.Cards.Where(c => c?.Tag is { Length: > 0 }).GroupBy(c => c.Tag!.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1).Select(g => $"Tag '{g.Key}' is given to {g.Count().ToString(CultureInfo.InvariantCulture)} cards; every card needs its own."));
        violations.AddRange(args.Cards.Where(c => c?.OverviewPage is { Length: > 0 }).GroupBy(c => c.OverviewPage!, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1).Select(g => $"Overview page '{g.Key}' is given to several cards; every card needs its own overview page."));
        return violations;
    }

    private static string? Point(double[]? point) =>
        point is null || point.Length == 2 && point.All(v => !double.IsNaN(v) && !double.IsInfinity(v) && v >= 0) ? null : "must be [x, y] in millimetres, both 0 or more.";
}

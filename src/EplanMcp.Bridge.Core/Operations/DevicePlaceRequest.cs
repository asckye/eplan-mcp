using System.Globalization;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// The checked arguments of device.place.plan and device.place.execute (docs/tools/eplan_device_place.md, sections 2
/// and 5): the page, found as eplan_page_get finds it; the part and its variant in the parts database, read as
/// eplan_part_search reads it; the point; and the full tag, which must be free. Both operations build it the same way
/// and compute the fingerprint here.
/// </summary>
internal sealed class DevicePlaceRequest
{
    /// <summary>The variant taken when none is given and the part has it (tool spec 2).</summary>
    public const string DefaultVariant = "1";

    private const string Circuit = "Circuit";

    private DevicePlaceRequest(OpenProject project, MacroTargetPage page, PartSearchItem part, DevicePlaceArgs args, string tag, string[] taken, string[] existing)
    {
        Project = project;
        Page = page;
        Part = part.PartNumber;
        Variant = part.Variant ?? string.Empty;
        X = args.X;
        Y = args.Y;
        Tag = tag;
        Taken = taken;
        Existing = existing;
    }

    public OpenProject Project { get; }

    public MacroTargetPage Page { get; }

    /// <summary>The part number as the parts database has it.</summary>
    public string Part { get; }

    /// <summary>The variant to place, as the parts database has it.</summary>
    public string Variant { get; }

    public double X { get; }

    public double Y { get; }

    /// <summary>The device's full tag.</summary>
    public string Tag { get; }

    /// <summary>Functions that already carry the tag (any case), alone or with a suffix.</summary>
    public IReadOnlyList<string> Taken { get; }

    internal string[] Existing { get; }

    /// <summary>Every argument problem at once (mcp-tool-design 6.2), then the project, the page and the part.</summary>
    public static DevicePlaceRequest Check(DevicePlaceArgs args, IProjectCatalog catalog, IPageReader pages, IDeviceRenamer names, IPartsDatabase parts, IDevicePlacer placer)
    {
        bool box = args.LocationBox is not null;
        bool terminal = args.TerminalStrip is not null;
        DeviceTerminalPlan.Check(args);
        DeviceSymbolPlan.CheckBox(args);
        if (!box && !terminal && args.Library is null && (args.Symbol is not null || args.SymbolVariant is not null || args.DisplayProperties is not null || args.ConnectionDesignations is not null))
        { throw WriteArguments.Invalid(["/library: symbol, display properties and pin designations require a complete symbol source."]); }
        var violations = new List<string>();
        WriteArguments.RequireProject(args.Project, violations);
        string tag = terminal ? args.TerminalStrip! : args.Tag?.Trim() ?? string.Empty;
        string part = args.Part?.Trim() ?? string.Empty;
        AddIf(violations, string.IsNullOrEmpty(args.Page), "'page' is required: a full circuit page name such as =GAA+A1&EFS1/60, as eplan_page_list returns it.");
        AddIf(violations, !box && !terminal && args.Library is null && (part.Length == 0 || part.Any(char.IsControl)), "'part' is required: a part number such as SIE.3RH2122-1HB40, as eplan_part_search returns it.");
        AddIf(violations, !(Finite(args.X) && Finite(args.Y) && args.X >= 0 && args.Y >= 0), "'x' and 'y' must be numbers from 0: millimetres from the page's bottom left corner, y up.");
        AddIf(violations, !box && TagProblem(tag) is not null, "'tag' " + TagProblem(tag));
        if (violations.Count > 0)
        {
            throw WriteArguments.Invalid(violations);
        }

        OpenProject project = WriteArguments.Writable(catalog, args.Project);
        MacroTargetPage page = (PageUpdateRequest.Find(pages.PageNames(project.Path), args.Page) is { } name ? placer.ReadPage(project.Path, name) : null)
            ?? throw new BridgeErrorException(
                ErrorReason.PageNotFound,
                "No page has this name, so nothing was placed. Use eplan_page_list with name_prefix to find the exact page name.",
                retryable: false,
                new Dictionary<string, string> { ["project"] = project.Name, ["page"] = args.Page });
        PartSearchItem? chosen = box || terminal || args.Library is not null ? new PartSearchItem() : ChooseVariant(FindPart(parts.Read(null), part), args.Variant?.Trim() is { Length: > 0 } variant ? variant : null, violations);
        violations.AddRange(PageProblems(page, args.X, args.Y));
        if (box)
        {
            if (page.Width is not { } w || page.Height is not { } h || !Finite(w) || !Finite(h) || w <= 0 || h <= 0)
            { violations.Add("/location_box: page bounds are unknown; select a page with readable bounds."); }
            violations.AddRange(PageProblems(page, args.CornerX!.Value, args.CornerY!.Value));
        }
        if (violations.Count > 0)
        {
            throw WriteArguments.Invalid(violations);
        }

        string full = box ? args.LocationBox! : tag[0] == '-' ? DeviceStructureOf(page.Name) + tag : tag;
        if (terminal)
        { full += ":" + args.TerminalNumber; }
        string[] existing = terminal ? [.. names.FunctionNames(project.Path)] : box ? [.. names.FunctionNames(project.Path), FormattableString.Invariant($"bounds:{page.Width},{page.Height}")] : [];
        if (terminal && existing.FirstOrDefault(n => SameName(full.Substring(0, full.LastIndexOf(':')), n, ignoreCase: true)) is { } canonical)
        { full = canonical + ":" + args.TerminalNumber; }
        string[] taken =
        [
            .. (box ? page.LocationBoxStructures : names.FunctionNames(project.Path))
                .Where(n => SameName(full, n, ignoreCase: true) || n.StartsWith(full + ":", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.Ordinal),
        ];
        return new DevicePlaceRequest(project, page, chosen!, args, full, taken, existing);
    }

    /// <summary>The one change, for the dry run, the approval form and the audit (tool spec 5).</summary>
    public PlannedChange Change() => new() { Action = "create", Kind = "device", Name = Page.Name, NewValue = Describe(Part, Variant, Tag, X, Y) };

    /// <summary>The change's value as the tool spec words it: <c>SIE.3RH2122-1HB40 as =GAA+A1-KF20 at (80, 160)</c>.</summary>
    internal static string Describe(string part, string variant, string tag, double x, double y) => string.Format(
        CultureInfo.InvariantCulture,
        "{0}{1} as {2} at ({3:0.##}, {4:0.##})",
        part,
        variant == DefaultVariant || variant.Length == 0 ? string.Empty : ", variant " + variant,
        tag,
        x,
        y);

    /// <summary>The plan's state (tool spec 5): the objects on the page, and how many functions carry the tag.</summary>
    public string StateFingerprint() =>
        Fingerprint.Of(Page.Objects.Select(id => "object\t" + id).Concat(Existing.Select(n => "name\t" + n)).Concat(Page.LocationBoxStructures.Select(n => "box\t" + n)).Append("taken\t" + Taken.Count.ToString(CultureInfo.InvariantCulture)));

    /// <summary>ALREADY_EXISTS when a device carries the tag: EPLAN would merge the two (A13: it does not check).</summary>
    public void CheckFree()
    {
        if (Taken.Count > 0)
        {
            throw new BridgeErrorException(
                ErrorReason.AlreadyExists,
                Tag.StartsWith("+", StringComparison.Ordinal) ? "A location box already uses this structure on the page; choose another location_box identifier." : $"'{Tag}' is already used by another device; a new device with this tag would merge with it in EPLAN, which this tool does not do. Choose a free tag; eplan_device_list with tag_contains shows the tags in use.",
                retryable: false,
                new Dictionary<string, string> { ["project"] = Project.Name, ["existing"] = string.Join("\n", Taken.Take(ProjectResolver.MaxCandidates)) });
        }
    }

    /// <summary>
    /// The structure a device on the page gets from a tag starting with '-': the page's structure without its document
    /// type, so <c>=GAA+A1&amp;EFS1/60</c> gives <c>=GAA+A1</c>. EPLAN drops parts a device does not use (A13), which
    /// the read-back finds.
    /// </summary>
    internal static string DeviceStructureOf(string pageName)
    {
        string structure = PageNames.StructureOf(pageName);
        int type = structure.IndexOf('&');
        int end = type < 0 ? -1 : structure.IndexOf('#', type);
        return type < 0 ? structure : structure.Substring(0, type) + (end < 0 ? string.Empty : structure.Substring(end));
    }

    /// <summary>The part's variants, found by exact part number, or ignoring case when only one number matches.</summary>
    private static List<PartSearchItem> FindPart(PartsDatabaseContent database, string part)
    {
        List<PartSearchItem> exact = [.. database.Parts.Where(p => string.Equals(p.PartNumber, part, StringComparison.Ordinal))];
        if (exact.Count > 0)
        {
            return exact;
        }

        List<PartSearchItem> found = [.. database.Parts.Where(p => string.Equals(p.PartNumber, part, StringComparison.OrdinalIgnoreCase))];
        string[] numbers = [.. found.Select(p => p.PartNumber).Distinct(StringComparer.Ordinal).OrderBy(n => n, StringComparer.Ordinal)];
        return numbers.Length switch
        {
            0 => throw new BridgeErrorException(
                ErrorReason.PartNotFound,
                "EPLAN's parts database has no part with this number, so nothing was placed. Use eplan_part_search to find the part number.",
                retryable: false,
                new Dictionary<string, string> { ["part"] = part }),
            1 => found,
            _ => throw ProjectResolver.Ambiguous("part", part, numbers, "Several parts have this number in different letter case; pass one of the candidates exactly as 'part'."),
        };
    }

    /// <summary>
    /// The variant to place: the one given, or else 1, or else the part's only one. EPLAN would silently take the first
    /// variant for one that does not exist (EPLAN XML docs), so that is a violation here, as is a part without a macro.
    /// </summary>
    private static PartSearchItem? ChooseVariant(List<PartSearchItem> variants, string? given, List<string> violations)
    {
        PartSearchItem? chosen = given is null
            ? variants.FirstOrDefault(p => p.Variant == DefaultVariant) ?? (variants.Count == 1 ? variants[0] : null)
            : variants.FirstOrDefault(p => string.Equals(p.Variant ?? string.Empty, given, StringComparison.Ordinal));
        string number = variants[0].PartNumber;
        if (chosen is null)
        {
            string known = string.Join(", ", variants.Select(p => p.Variant ?? string.Empty).OrderBy(v => v, StringComparer.Ordinal).Select(v => v.Length == 0 ? "(none)" : v));
            violations.Add(given is null
                ? $"'variant' is required: part {number} has no variant 1; its variants are {known}."
                : $"'variant' {given} does not exist; part {number} has the variants {known}.");
        }
        else if (!chosen.HasMacro)
        {
            violations.Add($"'part' {number} has no macro in the parts database, so it cannot be placed by itself; insert a window macro with eplan_macro_insert instead.");
        }

        return chosen;
    }

    /// <summary>What the page rules out: the page type, and a point outside the page when EPLAN can tell its size.</summary>
    private static IEnumerable<string> PageProblems(MacroTargetPage page, double x, double y)
    {
        if (!string.Equals(page.Type, Circuit, StringComparison.Ordinal))
        {
            yield return $"'page' must be a circuit page (Circuit), but '{page.Name}' is {page.Type}.";
        }

        if (page.Width is { } width && page.Height is { } height && (x > width || y > height))
        {
            yield return string.Format(CultureInfo.InvariantCulture, "'x' and 'y' must lie on the page, {0:0.##} x {1:0.##} mm: x from 0 to {0:0.##} and y from 0 to {1:0.##}.", width, height);
        }
    }

    private static string? TagProblem(string tag) =>
        tag.Length == 0 ? "is required: from '-' (-KF20) for the page's structure, or in full (=GAA+A1-KF20)."
        : tag.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)) ? "must not contain white space or control characters."
        : tag.IndexOf(':') >= 0 ? "must not contain ':'; connection point suffixes are EPLAN's."
        : tag.LastIndexOf('-') is int dash && (dash < 0 || dash == tag.Length - 1) ? "needs a device part after '-', such as -KF20."
        : null;

    private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

    private static void AddIf(List<string> violations, bool condition, string violation)
    {
        if (condition)
        {
            violations.Add(violation);
        }
    }

    /// <summary>
    /// Live T7c: the project's structure settings can drop the higher-level function from strip and terminal names
    /// (=GAA+A1-XT3 is stored as +A1-XT3). Only that leading segment may be missing.
    /// </summary>
    internal static bool SameName(string requested, string? actual, bool ignoreCase = false)
    {
        if (actual is null)
        { return false; }
        StringComparison comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Equals(requested, actual, comparison)
            || (requested.Length > 0 && requested[0] == '=' && actual.Length > 0 && actual[0] is '+' or '-'
                && requested.EndsWith(actual, comparison) && requested.Substring(0, requested.Length - actual.Length).IndexOfAny(['+', '-', ':', '&']) < 0);
    }
}

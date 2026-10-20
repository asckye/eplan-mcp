using System.Globalization;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// The checked arguments of macro.insert.plan and macro.insert.execute (docs/tools/eplan_macro_insert.md, sections 2
/// and 5): the page, found as eplan_page_get finds it; the macro, resolved by SEC-19's rules and opened without
/// inserting it (A14); and where its outline goes. Both operations build it the same way and compute the fingerprint here.
/// </summary>
internal sealed class MacroInsertRequest
{
    /// <summary>The most devices a result lists (tool spec 3).</summary>
    public const int MaxDevices = MacroGetOperation.MaxDevices;

    /// <summary>The one representation this version inserts, and only on circuit pages (tool spec 2).</summary>
    public const string MultiLine = "MultiLine";

    private const string Circuit = "Circuit";

    private MacroInsertRequest(OpenProject project, MacroTargetPage page, string macroPath, MacroContent content, MacroInsertArgs args, bool number)
    {
        Project = project;
        Page = page;
        Macro = MacroFiles.Normalized(args.Macro);
        MacroPath = macroPath;
        Content = content;
        X = args.X;
        Y = args.Y;
        Variant = args.Variant;
        Number = number;
    }

    public OpenProject Project { get; }

    public MacroTargetPage Page { get; }

    /// <summary>The macro's path as eplan_macro_list gives it.</summary>
    public string Macro { get; }

    /// <summary>The macro's full path, which never leaves the Bridge (SEC-12).</summary>
    public string MacroPath { get; }

    public MacroContent Content { get; }

    public double X { get; }

    public double Y { get; }

    public int Variant { get; }

    /// <summary>True for numbering <c>number</c>, false for <c>keep</c>.</summary>
    public bool Number { get; }

    /// <summary>Every argument problem at once (mcp-tool-design 6.2), then the project, the page and the macro.</summary>
    public static MacroInsertRequest Check(MacroInsertArgs args, IProjectCatalog catalog, IPageReader pages, IMacroReader macros, IMacroInserter inserter)
    {
        var violations = new List<string>();
        WriteArguments.RequireProject(args.Project, violations);
        string numbering = string.IsNullOrWhiteSpace(args.Numbering) ? "number" : args.Numbering!.Trim();
        bool number = string.Equals(numbering, "number", StringComparison.OrdinalIgnoreCase);
        AddIf(violations, string.IsNullOrEmpty(args.Page), "'page' is required: a full page name such as =GAA+A1&EFS1/60, as eplan_page_list returns it.");
        AddIf(violations, string.IsNullOrWhiteSpace(args.Macro), "'macro' is required: a path that eplan_macro_list returned.");
        AddIf(violations, !(Finite(args.X) && Finite(args.Y) && args.X >= 0 && args.Y >= 0), "'x' and 'y' must be numbers from 0: millimetres from the page's bottom left corner, y up.");
        AddIf(violations, args.Variant < 0, "'variant' must be 0 or more: a variant number that eplan_macro_get lists.");
        AddIf(violations, !number && !string.Equals(numbering, "keep", StringComparison.OrdinalIgnoreCase), "'numbering' must be number or keep.");
        if (violations.Count > 0)
        {
            throw WriteArguments.Invalid(violations);
        }

        OpenProject project = WriteArguments.Writable(catalog, args.Project);
        MacroTargetPage page = FindPage(pages, inserter, project, args.Page);
        string root = macros.MacroRoot() is { } resolved && Directory.Exists(resolved)
            ? resolved
            : throw new BridgeErrorException(ErrorReason.EplanOperationFailed, MacroListOperation.NoMacroDirectory);
        string path = MacroFiles.Resolve(root, args.Macro);
        var request = new MacroInsertRequest(project, page, path, macros.ReadMacro(project.Path, path), args, number);
        violations.AddRange(request.Problems());
        return violations.Count > 0 ? throw WriteArguments.Invalid(violations) : request;
    }

    /// <summary>The macro's functions other than wires, one per tag, with the macro's own tags.</summary>
    public List<PageDevice> MacroDevices() => Devices(Content.Functions);

    /// <summary>The one change, for the dry run, the approval form and the audit (tool spec 5).</summary>
    public PlannedChange Change(int devices) =>
        new() { Action = "create", Kind = "macro", Name = Page.Name, NewValue = Describe(Macro, X, Y, Variant, Number, devices) };

    /// <summary>The change's value as the tool spec words it: <c>Contactor_Feedback_Control.ema at (40, 200), numbering number, 8 devices</c>.</summary>
    internal static string Describe(string macro, double x, double y, int variant, bool number, int devices) => string.Format(
        CultureInfo.InvariantCulture,
        "{0} at ({1:0.##}, {2:0.##}){3}, numbering {4}, {5} {6}",
        macro.Substring(macro.LastIndexOf('/') + 1),
        x,
        y,
        variant == 0 ? string.Empty : ", variant " + variant.ToString(CultureInfo.InvariantCulture),
        number ? "number" : "keep",
        devices,
        devices == 1 ? "device" : "devices");

    /// <summary>
    /// The plan's state (tool spec 5): the objects on the page; the macro file's size and time; and for <c>keep</c>, the
    /// main functions whose device part (<see cref="DevicePart"/>) a function of the macro has, which a merge would take.
    /// </summary>
    public string StateFingerprint(IDeviceReader devices)
    {
        var file = new FileInfo(MacroPath);
        IEnumerable<string> lines = Page.Objects.Select(id => "object\t" + id)
            .Append("macro\t" + file.Length.ToString(CultureInfo.InvariantCulture) + "\t" + file.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture));
        if (!Number)
        {
            var parts = new HashSet<string>(Content.Functions.Select(f => DevicePart(f.Name)).OfType<string>(), StringComparer.OrdinalIgnoreCase);
            lines = lines.Concat(devices.ReadMainFunctionNames(Project.Path).Where(f => DevicePart(f.Name) is { } part && parts.Contains(part)).Select(f => "tag\t" + f.Name));
        }

        return Fingerprint.Of(lines);
    }

    /// <summary>
    /// Functions other than wires, one per tag in the order of each tag's first function; the one the drawing labels
    /// stands for its tag, as in eplan_macro_get.
    /// </summary>
    internal static List<PageDevice> Devices(IEnumerable<MacroFunctionRecord> functions) =>
    [
        .. functions
            .Where(f => f.Category != MacroGetOperation.Wire && f.Name.Length > 0)
            .GroupBy(f => f.Name, StringComparer.Ordinal)
            .Select(g => g.FirstOrDefault(f => DeviceTags.Shown(f.VisibleName) is not null) ?? g.First())
            .Select(f => new PageDevice { Tag = f.Name, VisibleTag = DeviceTags.Shown(f.VisibleName), Category = f.Category }),
    ];

    /// <summary>
    /// Why the macro outline, <paramref name="width"/> x <paramref name="height"/> mm with its top left corner at
    /// (<paramref name="x"/>, <paramref name="y"/>) (A14), leaves a page of <paramref name="pageWidth"/> x
    /// <paramref name="pageHeight"/> mm whose origin is its bottom left corner (A3); null when it lies within.
    /// </summary>
    internal static string? Outside(double x, double y, double width, double height, double pageWidth, double pageHeight)
    {
        if (x + width <= pageWidth && y <= pageHeight && y - height >= 0)
        {
            return null;
        }

        return width > pageWidth || height > pageHeight
            ? string.Format(CultureInfo.InvariantCulture, "the macro, {0:0.##} x {1:0.##} mm, is larger than the page, {2:0.##} x {3:0.##} mm.", width, height, pageWidth, pageHeight)
            : string.Format(
                CultureInfo.InvariantCulture,
                "'x' and 'y' put the macro's outline, {0:0.##} x {1:0.##} mm below and right of its top left corner at (x, y), outside the page, {2:0.##} x {3:0.##} mm: x must be from 0 to {4:0.##} and y from {1:0.##} to {3:0.##}.",
                width,
                height,
                pageWidth,
                pageHeight,
                pageWidth - width);
    }

    /// <summary>
    /// The tags of devices an insert merged, sorted: tags that had a main function before the insert and have more
    /// after it (S10: numbering None gave the project a second main function <c>+A1-WG6</c>, A14).
    /// </summary>
    internal static List<string> Merged(IEnumerable<string> before, IEnumerable<string> after)
    {
        Dictionary<string, int> had = Count(before);
        return [.. Count(after).Where(tag => had.TryGetValue(tag.Key, out int count) && tag.Value > count).Select(tag => tag.Key).OrderBy(tag => tag, StringComparer.Ordinal)];
    }

    /// <summary>A tag's device part: after its last '-', before a ':' (<c>+-XG8:?</c> gives <c>XG8</c>); null without one.</summary>
    internal static string? DevicePart(string tag)
    {
        int dash = tag.LastIndexOf('-');
        int colon = dash < 0 ? -1 : tag.IndexOf(':', dash);
        string part = dash < 0 ? string.Empty : colon < 0 ? tag.Substring(dash + 1) : tag.Substring(dash + 1, colon - dash - 1);
        return part.Length == 0 ? null : part;
    }

    /// <summary>The page by its exact name, or the one name equal ignoring case (as eplan_page_get finds it).</summary>
    private static MacroTargetPage FindPage(IPageReader pages, IMacroInserter inserter, OpenProject project, string wanted)
    {
        if (inserter.ReadPage(project.Path, wanted) is { } exact)
        {
            return exact;
        }

        string[] matches = [.. pages.PageNames(project.Path).Where(n => string.Equals(n, wanted, StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.Ordinal)];
        if (matches.Length > 1)
        {
            throw ProjectResolver.Ambiguous("page", wanted, matches, "Several pages have this name in different letter case; pass one of the candidates exactly as 'page'.");
        }

        return (matches.Length == 1 ? inserter.ReadPage(project.Path, matches[0]) : null)
            ?? throw new BridgeErrorException(
                ErrorReason.PageNotFound,
                "No page has this name, so nothing was inserted. Use eplan_page_list with name_prefix to find the exact page name.",
                retryable: false,
                new Dictionary<string, string> { ["project"] = project.Name, ["page"] = wanted });
    }

    private static Dictionary<string, int> Count(IEnumerable<string> tags) =>
        tags.Where(tag => tag.Length > 0).GroupBy(tag => tag, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

    private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

    private static void AddIf(List<string> violations, bool condition, string violation)
    {
        if (condition)
        {
            violations.Add(violation);
        }
    }

    /// <summary>What the page and the macro rule out: the page type, the representation, the variant and the outline.</summary>
    private IEnumerable<string> Problems()
    {
        if (!string.Equals(Page.Type, Circuit, StringComparison.Ordinal))
        {
            yield return $"'page' must be a circuit page (Circuit), but '{Page.Name}' is {Page.Type}; this version inserts macros on circuit pages only.";
        }

        List<int>? variants = Content.Representations.FirstOrDefault(r => r.Key == MultiLine).Value;
        if (variants is null)
        {
            string types = Content.Representations.Count == 0 ? "none" : string.Join(", ", Content.Representations.Select(r => r.Key));
            yield return $"'macro' has no MultiLine representation, which circuit pages need (it has {types}); choose another macro, eplan_macro_get shows a macro's representations.";
        }
        else if (!variants.Contains(Variant))
        {
            yield return $"'variant' {Variant.ToString(CultureInfo.InvariantCulture)} does not exist; the macro's MultiLine variants are {string.Join(", ", variants.Select(v => v.ToString(CultureInfo.InvariantCulture)))}.";
        }

        if (Page.Width is { } width && Page.Height is { } height && Outside(X, Y, Content.Width, Content.Height, width, height) is { } outside)
        {
            yield return outside;
        }
    }
}

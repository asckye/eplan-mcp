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

    /// <summary>The default representation, inserted on circuit pages.</summary>
    public const string MultiLine = "MultiLine";

    private MacroInsertRequest(OpenProject project, MacroTargetPage page, string macro, string macroPath, MacroContent content, MacroInsertArgs args, bool number, string representation, PartMacroSource? source)
    {
        Project = project;
        Page = page;
        Macro = MacroFiles.Normalized(macro);
        MacroPath = macroPath;
        Content = content;
        X = args.X ?? 0;
        Y = args.Y ?? 0;
        Variant = args.Variant ?? 0;
        Number = number;
        Representation = representation;
        Source = source;
        MacroBox = args.MacroBox ?? true;
        Values = args.Values is null ? null : new Dictionary<string, string>(args.Values, StringComparer.Ordinal);
        _guard = args.LayoutGuard;
    }

    private readonly string? _guard;

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
    public string Representation { get; }
    public PartMacroSource? Source { get; }
    public bool MacroBox { get; }
    public IReadOnlyDictionary<string, string>? Values { get; }

    /// <summary>Every argument problem at once (the MCP server standard), then the project, the page and the macro.</summary>
    public static MacroInsertRequest Check(MacroInsertArgs args, IProjectCatalog catalog, IPageReader pages, IMacroReader macros, IMacroInserter inserter, bool enforceLayout = true)
    {
        var violations = new List<string>();
        WriteArguments.RequireProject(args.Project, violations);
        string numbering = string.IsNullOrWhiteSpace(args.Numbering) ? "number" : args.Numbering!.Trim();
        bool number = string.Equals(numbering, "number", StringComparison.OrdinalIgnoreCase);
        string representation = string.IsNullOrWhiteSpace(args.Representation) ? MultiLine : args.Representation!.Trim();
        representation = representation.Equals("SingleLine", StringComparison.OrdinalIgnoreCase) ? "SingleLine" : representation.Equals("Overview", StringComparison.OrdinalIgnoreCase) ? "Overview" : representation.Equals(MultiLine, StringComparison.OrdinalIgnoreCase) ? MultiLine : representation;
        bool hasMacro = !string.IsNullOrWhiteSpace(args.Macro), hasPart = !string.IsNullOrWhiteSpace(args.Part);
        AddIf(violations, string.IsNullOrEmpty(args.Page), "'page' is required: a full page name such as =GAA+A1&EFS1/60, as eplan_page_list returns it.");
        AddIf(violations, hasMacro == hasPart, "'macro' is required when 'part' is omitted; supply exactly one of 'macro' and 'part'.");
        AddIf(violations, hasPart && args.Part!.Any(char.IsControl), "'part' must be a part number without control characters.");
        AddIf(violations, args.PartVariant is not null && !hasPart, "'part_variant' requires 'part'.");
        AddIf(violations, representation is not MultiLine and not "Overview" and not "SingleLine", "'representation' must be MultiLine, Overview or SingleLine.");
        AddIf(violations, !(Finite(args.X ?? 0) && Finite(args.Y ?? 0) && (args.X ?? 0) >= 0 && (args.Y ?? 0) >= 0), "'x' and 'y' must be numbers from 0: millimetres from the page's bottom left corner, y up.");
        AddIf(violations, args.Variant < 0, "'variant' must be 0 or more: a variant number that eplan_macro_get lists.");
        AddIf(violations, !number && !string.Equals(numbering, "keep", StringComparison.OrdinalIgnoreCase), "'numbering' must be number or keep.");
        if (violations.Count > 0)
        {
            throw WriteArguments.Invalid(violations);
        }

        OpenProject project = WriteArguments.Writable(catalog, args.Project);
        MacroTargetPage page = FindPage(pages, inserter, project, args.Page!);
        string root = macros.MacroRoot() is { } resolved && Directory.Exists(resolved)
            ? resolved
            : throw new BridgeErrorException(ErrorReason.EplanOperationFailed, MacroListOperation.NoMacroDirectory);
        PartMacroSource? source = hasPart ? macros.ResolvePartMacro(args.Part!.Trim(), args.PartVariant?.Trim() is { Length: > 0 } partVariant ? partVariant : null) : null;
        string macro = source?.Macro ?? args.Macro!;
        if (string.IsNullOrWhiteSpace(macro))
        { throw WriteArguments.Invalid(["'part' has no ARTICLE_GROUPSYMBOLMACRO; use eplan_device_place when the part has a suitable device template."]); }
        string path = MacroFiles.Resolve(root, macro);
        var request = new MacroInsertRequest(project, page, macro, path, macros.ReadMacro(project.Path, path, representation: representation, variant: args.Variant ?? 0), args, number, representation, source);
        violations.AddRange(request.Problems());
        violations.AddRange(ValueProblems(request.Content, request.Values));
        if (violations.Count == 0 && enforceLayout)
        {
            request.LayoutWarnings = Layout(() => page.Layout is null
                ? ["Layout unverified: page geometry is unavailable."]
                : LayoutGuard.Enforce(page.Layout, [LayoutGuard.Proposed("Macro", [request.X, request.Y], request.ProposedOutline())], mode: args.LayoutGuard ?? "refuse"));
        }
        return violations.Count > 0 ? throw WriteArguments.Invalid(violations) : request;
    }
    /// <summary>Checks every insertion before any write and compares all proposed outlines on each page together.</summary>
    public static List<MacroInsertRequest> CheckAll(MacroInsertArgs args, IProjectCatalog catalog, IPageReader pages, IMacroReader macros, IMacroInserter inserter)
    {
        if (args.Items is null)
        {
            return [Check(args, catalog, pages, macros, inserter)];
        }

        List<string> violations = BatchProblems(args);
        if (violations.Count > 0)
        {
            throw WriteArguments.Invalid(violations);
        }

        var requests = new List<MacroInsertRequest>();
        BridgeErrorException? firstFailure = null;
        for (int index = 0; index < args.Items.Count; index++)
        {
            MacroInsertItem? item = args.Items[index];
            string prefix = "/items/" + index.ToString(CultureInfo.InvariantCulture);
            if (item is null)
            {
                violations.Add(prefix + ": Supply an insertion object.");
                continue;
            }

            var single = new MacroInsertArgs
            {
                Project = args.Project,
                Page = item.Page,
                Macro = item.Macro,
                Part = item.Part,
                PartVariant = item.PartVariant,
                Representation = item.Representation,
                X = item.X,
                Y = item.Y,
                Variant = item.Variant,
                Numbering = item.Numbering,
                MacroBox = item.MacroBox,
                Values = item.Values,
                LayoutGuard = args.LayoutGuard,
            };
            try
            {
                requests.Add(Check(single, catalog, pages, macros, inserter, enforceLayout: false));
            }
            catch (BridgeErrorException error) when (error.Error.Reason == ErrorReason.InvalidArgument)
            {
                string problems = error.Error.Metadata?.TryGetValue("violations", out string? found) == true ? found : error.Error.Message;
                violations.AddRange(problems.Split('\n').Select(problem => Prefix(prefix, problem)));
            }
            catch (BridgeErrorException error)
            {
                error.Error.Metadata ??= new Dictionary<string, string>();
                error.Error.Metadata["item"] = index.ToString(CultureInfo.InvariantCulture);
                firstFailure ??= error;
                violations.Add(Prefix(prefix, error.Error.Message));
            }
        }

        if (firstFailure is not null)
        {
            firstFailure.Error.Metadata!["violations"] = string.Join("\n", violations);
            throw firstFailure;
        }
        if (violations.Count > 0)
        {
            throw WriteArguments.Invalid(violations);
        }

        var layoutViolations = new List<LayoutGuard.Violation>();
        var layoutChecks = new List<string>();
        foreach (var group in requests.Select((request, index) => (Request: request, Index: index)).GroupBy(item => item.Request.Page.Name, StringComparer.Ordinal))
        {
            MacroTargetPage page = group.First().Request.Page;
            try
            {
                List<string> warnings = Layout(() => page.Layout is null
                    ? ["Layout unverified: page geometry is unavailable."]
                    : LayoutGuard.Enforce(page.Layout,
                        [.. group.Select(item => LayoutGuard.Proposed("Macro", [item.Request.X, item.Request.Y], item.Request.ProposedOutline(), index: item.Index))],
                        mode: args.LayoutGuard ?? "refuse"));
                foreach (var item in group)
                {
                    item.Request.LayoutWarnings.AddRange(warnings);
                }
            }
            catch (BridgeErrorException error) when (error.Error.Reason == ErrorReason.InvalidArgument)
            {
                if (error.Error.Metadata?.TryGetValue("layoutChecks", out string? checks) == true)
                {
                    layoutChecks.Add(checks);
                }
                if (error.Error.Metadata?.TryGetValue("layoutViolations", out string? json) == true)
                {
                    List<LayoutGuard.Violation> found = Newtonsoft.Json.JsonConvert.DeserializeObject<List<LayoutGuard.Violation>>(json)!;
                    layoutViolations.AddRange(found);
                    violations.AddRange(found.Where(violation => !violation.Warning).Select(violation =>
                        "/items/" + (-violation.Objects[0].Oid - 1).ToString(CultureInfo.InvariantCulture) + ": " + violation.Message));
                }
                else
                {
                    string prefix = "/items/" + group.First().Index.ToString(CultureInfo.InvariantCulture);
                    violations.Add(Prefix(prefix, error.Error.Message));
                }
            }
        }
        if (violations.Count > 0)
        {
            BridgeErrorException error = WriteArguments.Invalid(violations);
            error.Error.Metadata!["layoutViolations"] = Newtonsoft.Json.JsonConvert.SerializeObject(layoutViolations);
            error.Error.Metadata["layoutChecks"] = string.Join("\n", layoutChecks);
            throw error;
        }
        return requests;
    }

    internal static List<string> BatchProblems(MacroInsertArgs args)
    {
        var violations = new List<string>();
        WriteArguments.RequireProject(args.Project, violations);
        AddIf(violations, args.Items is { Count: < 1 or > 20 }, "/items: Supply between 1 and 20 insertions.");
        (string Name, bool Supplied)[] fields =
        [
            ("page", args.Page is not null),
            ("macro", args.Macro is not null),
            ("part", args.Part is not null),
            ("part_variant", args.PartVariant is not null),
            ("representation", args.Representation is not null),
            ("x", args.X is not null),
            ("y", args.Y is not null),
            ("variant", args.Variant is not null),
            ("numbering", args.Numbering is not null),
            ("macro_box", args.MacroBox is not null),
            ("values", args.Values is not null),
        ];
        foreach (var field in fields.Where(field => field.Supplied))
        {
            violations.Add("/" + field.Name + ": Omit this top-level field when 'items' is supplied.");
        }
        return violations;
    }

    private static string Prefix(string prefix, string problem)
    {
        if (problem.StartsWith("/", StringComparison.Ordinal))
        {
            return prefix + problem;
        }

        int end = problem.StartsWith("'", StringComparison.Ordinal) ? problem.IndexOf('\'', 1) : -1;
        return prefix + (end > 1 ? "/" + problem.Substring(1, end - 1) : string.Empty) + ": " + problem;
    }

    public static string FingerprintFor(IReadOnlyList<MacroInsertRequest> requests, IDeviceReader devices, bool batch) => batch
        ? Fingerprint.Of(requests.Select((request, index) => index.ToString(CultureInfo.InvariantCulture) + "\t"
            + request.StateFingerprint(devices) + "\t" + Protocol.BridgeJson.FromObject(new
            {
                request.Page.Name,
                request.X,
                request.Y,
                request.Number,
                Guard = request._guard ?? "refuse",
            }).ToString(Newtonsoft.Json.Formatting.None)))
        : requests[0].StateFingerprint(devices);

    public List<string> LayoutWarnings { get; private set; } = [];

    /// <summary>How far the outline's top left corner lies from the reference point that (x, y) places (A89).</summary>
    private (double X, double Y) OutlineOffset() => Content.ReferencePoint is { Length: 2 } reference && Content.Location is { Length: 2 } location
        ? (location[0] - reference[0], location[1] + Content.Height - reference[1]) : (0, 0);

    private bool ReproducesWithoutBox => _guard == "warn" && !MacroBox;

    private double[]? ProposedOutline()
    {
        if (ReproducesWithoutBox)
        { return null; }
        (double dx, double dy) = OutlineOffset();
        return [X + dx, Y + dy - Content.Height, X + dx + Content.Width, Y + dy];
    }
    internal static List<string> Layout(Func<List<string>> read)
    {
        try
        { return read(); }
        catch (BridgeErrorException error) when (error.Error.Reason == ErrorReason.CapabilityMissing)
        { return ["Layout unverified: " + error.Error.Message]; }
    }

    /// <summary>The macro's functions other than wires, one per tag, with the macro's own tags.</summary>
    public List<PageDevice> MacroDevices() => Devices(Content.Functions);

    /// <summary>The one change, for the dry run, the approval form and the audit (tool spec 5).</summary>
    public PlannedChange Change(int devices) =>
        new() { Action = "create", Kind = "macro", Name = Page.Name, NewValue = Describe(Macro, X, Y, Variant, Number, devices) + (Representation == MultiLine ? "" : ", representation " + Representation) + (MacroBox ? "" : ", without macro box") + (Source is null ? "" : ", part " + Source.Part + ", part variant " + Source.PartVariant) };

    public IEnumerable<PlannedChange> ValueChanges(bool includePage = false) =>
        (Values ?? new Dictionary<string, string>()).OrderBy(value => value.Key, StringComparer.Ordinal)
            .Select(value => new PlannedChange
            {
                Action = "update",
                Kind = "placeholder",
                Name = includePage ? Page.Name + " " + value.Key : value.Key,
                Property = "value set",
                NewValue = value.Value,
            });

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
            .Append(Page.Layout is null ? "layout unavailable" : Protocol.BridgeJson.FromObject(Page.Layout).ToString())
            .Append("macroBox\t" + MacroBox)
            .Append("source\t" + Macro + "\t" + Representation + "\t" + Variant + "\t" + Source?.Part + "\t" + Source?.PartVariant)
            .Append("macro\t" + file.Length.ToString(CultureInfo.InvariantCulture) + "\t" + file.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture));
        if (Values is not null)
        {
            lines = lines.Append("values\t" + Protocol.BridgeJson.FromObject(Values.OrderBy(value => value.Key, StringComparer.Ordinal)
                .Select(value => new MacroValueApplication { Placeholder = value.Key, ValueSet = value.Value })));
        }
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
    internal static string? Outside(double x, double y, double width, double height, double pageWidth, double pageHeight, double offsetX = 0, double offsetY = 0)
    {
        if (x + width <= pageWidth && y <= pageHeight && y - height >= 0)
        {
            return null;
        }

        return width > pageWidth || height > pageHeight
            ? string.Format(CultureInfo.InvariantCulture, "the macro, {0:0.##} x {1:0.##} mm, is larger than the page, {2:0.##} x {3:0.##} mm.", width, height, pageWidth, pageHeight)
            : string.Format(
                CultureInfo.InvariantCulture,
                "'x' and 'y' put the macro's outline, {0:0.##} x {1:0.##} mm below and right of its top left corner at (x, y), outside the page, {2:0.##} x {3:0.##} mm: x must be from {5:0.##} to {4:0.##} and y from {6:0.##} to {7:0.##}.",
                width,
                height,
                pageWidth,
                pageHeight,
                pageWidth - width - offsetX,
                0 - offsetX,
                height - offsetY,
                pageHeight - offsetY);
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
        foreach (string problem in RepresentationProblems(Page, Content, Representation, Variant))
        { yield return problem; }
        (double dx, double dy) = OutlineOffset();
        if (!ReproducesWithoutBox && Page.Width is { } width && Page.Height is { } height && Outside(X + dx, Y + dy, Content.Width, Content.Height, width, height, dx, dy) is { } outside)
        { yield return outside; }
    }
    internal static IEnumerable<string> RepresentationProblems(MacroTargetPage page, MacroContent content, string representation, int variant)
    {
        string pageType = representation == "Overview" ? "Overview" : representation == "SingleLine" ? "CircuitSingleLine" : "Circuit";
        if (page.Type != pageType)
        { yield return $"'page' must be {pageType} for {representation}, but '{page.Name}' is {page.Type}."; }
        List<int>? variants = content.Representations.FirstOrDefault(r => r.Key == representation).Value;
        if (variants is null)
        {
            string types = content.Representations.Count == 0 ? "none" : string.Join(", ", content.Representations.Select(r => r.Key));
            yield return $"'macro' has no {representation} representation (available: {types}); choose another macro.";
        }
        else if (!variants.Contains(variant))
        {
            yield return $"'variant' {variant.ToString(CultureInfo.InvariantCulture)} does not exist; the macro's {representation} variants are {string.Join(", ", variants.Select(v => v.ToString(CultureInfo.InvariantCulture)))}.";
        }
    }

    internal static IEnumerable<string> ValueProblems(MacroContent content, IReadOnlyDictionary<string, string>? values)
    {
        if (values is null)
        {
            yield break;
        }

        if (values.Count is < 1 or > 20)
        {
            yield return "/values: Supply between 1 and 20 placeholder value sets.";
            yield break;
        }

        foreach (KeyValuePair<string, string> value in values.OrderBy(value => value.Key, StringComparer.Ordinal))
        {
            string pointer = "/values/" + value.Key.Replace("~", "~0").Replace("/", "~1");
            MacroPlaceholderRecord[] matches = [.. content.Placeholders.Where(holder => string.Equals(holder.Name, value.Key, StringComparison.Ordinal))];
            if (matches.Length != 1)
            {
                string available = string.Join(", ", content.Placeholders.Select(holder => holder.Name).Distinct(StringComparer.Ordinal).Take(20));
                yield return pointer + ": The name must match exactly one placeholder in the selected macro variant. Available placeholders: " + (available.Length == 0 ? "none" : available) + ".";
            }
            else if (!matches[0].ValueSets.Contains(value.Value, StringComparer.Ordinal))
            {
                string available = string.Join(", ", matches[0].ValueSets.Take(20));
                yield return pointer + ": Choose one of the placeholder's value sets: " + (available.Length == 0 ? "none" : available) + ".";
            }
        }
    }
}

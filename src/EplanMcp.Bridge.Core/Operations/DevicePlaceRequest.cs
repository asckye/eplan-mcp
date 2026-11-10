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

    private bool _joinsDevice;
    private bool _requiresExistingDevice;
    private string? _plcWarning;
    private readonly string? _arrangement;
    private readonly int? _symbolVariant;
    private string? _freeTag;
    private DevicePlaceRequest(OpenProject project, MacroTargetPage page, PartSearchItem part, DevicePlaceArgs args, string tag, string[] taken, string[] existing)
    {
        _arrangement = args.PropertyArrangement;
        _symbolVariant = args.Library is null ? args.SymbolVariant : null;
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

    /// <summary>Every argument problem at once (the MCP server standard), then the project, the page and the part.</summary>
    public static DevicePlaceRequest Check(DevicePlaceArgs args, IProjectCatalog catalog, IPageReader pages, IDeviceRenamer names, IPartsDatabase parts, IDevicePlacer placer, IPlcReader? plc = null)
    {
        if (args.PropertyArrangement is { } arrangement && (string.IsNullOrWhiteSpace(arrangement) || arrangement.Any(char.IsControl)))
        { throw WriteArguments.Invalid(["/property_arrangement: supply an exact nonblank arrangement name without controls."]); }
        if (DeviceJoinMode.Problem(args) is { } joinProblem)
        { throw WriteArguments.Invalid([joinProblem]); }
        if (DevicePlacementRoute.UsesPart(args) && args.Library is not null && (args.DisplayProperties is not null || args.ConnectionDesignations is not null || args.ConnectionDescriptions is not null))
        { throw WriteArguments.Invalid(["/display_properties: part templates accept symbol selection; display and pin overrides require the symbol route."]); }
        bool box = args.LocationBox is not null;
        bool blackBox = DeviceSymbolPlan.BlackBox(args);
        bool terminal = args.TerminalStrip is not null;
        DevicePlcPlan.Check(args);
        DeviceTerminalPlan.Check(args);
        DeviceSymbolPlan.CheckBox(args);
        // Codex drawing eval 2026-11-10: corners and T-nodes asked for here got "'tag' is required" eight times in one redraw.
        if (string.Equals(args.Library, "SPECIAL", StringComparison.OrdinalIgnoreCase) && ConnectionSymbolKind(args.Symbol) is { } kind)
        { throw WriteArguments.Invalid([$"/symbol: SPECIAL/{args.Symbol} is a connection symbol, not a device; place it with eplan_connection_create, item kind {kind}."]); }
        if (!box && !terminal && args.Library is null && (args.Symbol is not null || args.SymbolVariant is not null && !DevicePlacementRoute.UsesPart(args) || args.DisplayProperties is not null || args.ConnectionDesignations is not null || args.ConnectionDescriptions is not null))
        { throw WriteArguments.Invalid(["/library: symbol, display properties and pin designations or descriptions require a complete symbol source."]); }
        var violations = new List<string>();
        AddIf(violations, args.SymbolVariant is < 0, "/symbol_variant: use a nonnegative integer.");
        AddIf(violations, DevicePlacementRoute.UsesPart(args) && (args.DisplayProperties is not null || args.ConnectionDesignations is not null || args.ConnectionDescriptions is not null), "Part placement accepts a symbol override; display and pin options require the symbol route.");
        WriteArguments.RequireProject(args.Project, violations);
        string tag = terminal ? args.TerminalStrip! : args.Tag?.Trim() ?? string.Empty;
        string part = args.Part?.Trim() ?? string.Empty;
        AddIf(violations, string.IsNullOrEmpty(args.Page), "'page' is required: a full circuit page name such as =GAA+A1&EFS1/60, as eplan_page_list returns it.");
        AddIf(violations, !box && !terminal && args.Library is null && (part.Length == 0 || part.Any(char.IsControl)), "'part' is required: a part number such as SIE.3RH2122-1HB40, as eplan_part_search returns it.");
        AddIf(violations, !(Finite(args.X) && Finite(args.Y) && args.X >= 0 && args.Y >= 0), "'x' and 'y' must be numbers from 0: millimetres from the page's bottom left corner, y up.");
        AddIf(violations, !box && (tag.Length > 0 || args.Library is not null || terminal) && TagProblem(tag) is not null, "'tag' " + TagProblem(tag));
        AddIf(violations, DeviceSymbolPlan.ConnectionPoint(args) && args.ConnectionDesignations is not { Length: 1 }, "/connection_designations: a device connection point needs its one pin designation; EPLAN names it tag:pin, such as -PF1:1.");
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
        PartSearchItem? chosen = args.Part is null && (box || terminal || args.Library is not null) ? new PartSearchItem() : ChooseVariant(FindPart(parts.Read(null), part), args.Variant?.Trim() is { Length: > 0 } variant ? variant : null, violations);
        violations.AddRange(PageProblems(page, args.X, args.Y));
        if (box || blackBox)
        {
            if (box && (page.Width is not { } w || page.Height is not { } h || !Finite(w) || !Finite(h) || w <= 0 || h <= 0))
            { violations.Add("/location_box: page bounds are unknown; select a page with readable bounds."); }
            violations.AddRange(PageProblems(page, args.CornerX!.Value, args.CornerY!.Value));
        }
        if (violations.Count > 0)
        {
            throw WriteArguments.Invalid(violations);
        }

        string full = box ? args.LocationBox! : tag.StartsWith("-", StringComparison.Ordinal) ? DeviceStructureOf(page.Name) + tag : tag;
        if (terminal)
        { full += ":" + args.TerminalNumber; }
        if (args.PlcConnectionPoint is not null)
        { full += ":" + DevicePlcPlan.Suffix(args); }
        // The sample names device connection points after their pin (-PF1:1 has pin 1); A86 joins them to the black box by that name.
        if (DeviceSymbolPlan.ConnectionPoint(args) && args.ConnectionDesignations is { Length: > 0 } pins)
        { full += ":" + pins[0]; }
        string[] existing = terminal || args.PlcConnectionPoint is not null ? [.. names.FunctionNames(project.Path)] : box ? [.. names.FunctionNames(project.Path), FormattableString.Invariant($"bounds:{page.Width},{page.Height}")] : [];
        if (terminal && existing.FirstOrDefault(n => SameName(full.Substring(0, full.LastIndexOf(':')), n, ignoreCase: true)) is { } canonical)
        { full = canonical + ":" + args.TerminalNumber; }
        if (args.PlcConnectionPoint is not null && existing.FirstOrDefault(n => SameName(DevicePlcPlan.Parent(args, full), n, ignoreCase: true)) is { } card)
        { full = card + ":" + DevicePlcPlan.Suffix(args); }
        if (args.PlcConnectionPoint is not null)
        {
            string parent = DevicePlcPlan.Parent(args, full);
            PlcRecord? state = plc?.ReadPlc(project.Path, parent, TextLanguages.Independent, detailed: true);
            if (state?.Plc is null)
            { throw new BridgeErrorException(ErrorReason.DeviceNotFound, "An existing native PLC card is required; create it explicitly first."); }
            // MX_Dryer11 2026-11-08: a point placed after the overview was addressed is another function of the channel and stays
            // without an address unless it takes the overview's; take it when the caller gave none, so the plan lists it.
            if (args.PlcAddress is null && state.Channels.FirstOrDefault(c => SameName(full, c.Name, ignoreCase: true) && !string.IsNullOrEmpty(c.Address)) is { } addressed)
            { args.PlcAddress = addressed.Address; }
            if (args.PlcAddress is not null && state.Channels.Any(c => SameName(full, c.Name, ignoreCase: true)
                && c.Address is not null && !string.Equals(c.Address, args.PlcAddress, StringComparison.OrdinalIgnoreCase)))
            { throw new BridgeErrorException(ErrorReason.AlreadyExists, "This PLC channel already uses a different address; keep its address for another representation."); }
            if (args.PlcAddress is not null && state.Channels.Any(c => !SameName(full, c.Name, ignoreCase: true) && string.Equals(c.Address, args.PlcAddress, StringComparison.OrdinalIgnoreCase)))
            { throw new BridgeErrorException(ErrorReason.AlreadyExists, "This PLC card already has the I/O address; choose a free address."); }
            existing = [.. existing, Protocol.BridgeJson.FromObject(state).ToString(Newtonsoft.Json.Formatting.None)];
        }
        // MX_Dryer 2026-11-08: official pages put each field device in its own +M box, so only an exact repeat conflicts.
        IReadOnlyList<string> functions = terminal || args.PlcConnectionPoint is not null || box ? existing : names.FunctionNames(project.Path);
        string[] taken = box ? [.. page.LocationBoxStructures.Where(n => string.Equals(n, MacroTargetPage.BoxKey(full, args.X, args.Y, args.CornerX!.Value, args.CornerY!.Value), StringComparison.OrdinalIgnoreCase))] :
        [
            .. functions
                .Where(n => full.Length > 0 && ((terminal || args.PlcConnectionPoint is not null
                    ? SameName(full, n, ignoreCase: true) : Carries(full, n)) || WithoutEmptyFunction(n).StartsWith(BareOf(full, n) + ":", StringComparison.OrdinalIgnoreCase)))
                .Distinct(StringComparer.Ordinal),
        ];
        if (args.JoinDevice == true && taken.FirstOrDefault(n => SameName(full, n, ignoreCase: true)) is { } joinedTag)
        { full = joinedTag; }
        string? plcWarning = null;
        bool reusesPoint = false;
        if (args.PlcConnectionPoint is not null && taken.Length > 0)
        {
            DeviceNames? device = names.ReadDevice(project.Path, DevicePlcPlan.Parent(args, full));
            reusesPoint = DevicePlcPlan.ReusesPoint(full, taken, device, out plcWarning);
            existing = [.. existing, Protocol.BridgeJson.FromObject(device).ToString(Newtonsoft.Json.Formatting.None)];
        }
        var request = new DevicePlaceRequest(project, page, chosen!, args, full, taken, existing)
        {
            _joinsDevice = reusesPoint || args.JoinDevice == true || DeviceSymbolPlan.BusBarDefinition(args) || DeviceSymbolPlan.BusBarPoint(args),
            _plcWarning = plcWarning,
            _requiresExistingDevice = args.JoinDevice == true,
            _freeTag = !box && !terminal && args.PlcConnectionPoint is null && taken.Length > 0 ? FreeTag(full, functions) : null,
        };
        if (args.Fingerprint is null)
        { request.CheckFree(); }
        if (DevicePlacementRoute.UsesPart(args) && args.Library is null)
        { request.CheckLayout(args); }
        return request;
    }
    public List<string> LayoutWarnings { get; private set; } = [];
    public void CheckLayout(DevicePlaceArgs args)
    {
        LayoutGuard.Model layout = Page.Layout ?? throw new BridgeErrorException(ErrorReason.CapabilityMissing, "R1: read complete page layout before placing a device.");
        layout.SetsTextAttribute = (row, attribute) => attribute is "height_mm" or "position" && row.Kind == "PropertyPlacement" && row.Parent is not null
            && !layout.Page.Placements.Any(p => p.Parent is null && p.Locator.Type == row.Parent.Type && p.Locator.Oid == row.Parent.Oid)
            && (args.DisplayProperties ?? []).Any(p => row.Properties.TryGetValue("property_id", out string? id) && id == p.PropertyId.ToString(CultureInfo.InvariantCulture)
                && row.Location.SequenceEqual(new[] { args.X + p.OffsetXmm, args.Y + p.OffsetYmm }));
        LayoutWarnings = LayoutGuard.Enforce(layout, Page.PredictDevice?.Invoke(args)
            ?? [LayoutGuard.Proposed(args.LocationBox is null ? "Function" : "LocationBox", [args.X, args.Y], null)], mode: args.LayoutGuard ?? "refuse");
        if (_plcWarning is not null)
        { LayoutWarnings.Add(_plcWarning); }
    }

    /// <summary>The one change, for the dry run, the approval form and the audit (tool spec 5).</summary>
    public PlannedChange Change() => new() { Action = "create", Kind = "device", Name = Page.Name, NewValue = Describe(Part, Variant, Tag, X, Y) + (_arrangement is null ? "" : "; property arrangement " + _arrangement) + (_symbolVariant is null ? "" : "; symbol variant " + _symbolVariant.Value.ToString(CultureInfo.InvariantCulture)) };

    /// <summary>The change's value as the tool spec words it: <c>SIE.3RH2122-1HB40 as =GAA+A1-KF20 at (80, 160)</c>.</summary>
    internal static string Describe(string part, string variant, string tag, double x, double y) => string.Format(
        CultureInfo.InvariantCulture,
        "{0}{1} as {2} at ({3:0.##}, {4:0.##})",
        part,
        variant == DefaultVariant || variant.Length == 0 ? string.Empty : ", variant " + variant,
        tag.Length == 0 ? "EPLAN automatic numbering" : tag,
        x,
        y);

    /// <summary>The plan's state (tool spec 5): the objects on the page, and how many functions carry the tag.</summary>
    public string StateFingerprint() =>
        Fingerprint.Of(Page.Objects.Select(id => "object\t" + id).Concat(Existing.Select(n => "name\t" + n)).Concat(Page.LocationBoxStructures.Select(n => "box\t" + n)).Append("taken\t" + Taken.Count.ToString(CultureInfo.InvariantCulture)).Append(Protocol.BridgeJson.FromObject(new { Page.Layout, LayoutWarnings }).ToString()).Concat(_arrangement is null ? [] : new[] { "arrangement\t" + _arrangement }).Concat(_symbolVariant is null ? [] : new[] { "symbol_variant\t" + _symbolVariant.Value.ToString(CultureInfo.InvariantCulture) }));

    /// <summary>Whether function <paramref name="name"/> carries the device tag <paramref name="full"/> (any case).</summary>
    private static bool Carries(string full, string name) => string.Equals(BareOf(full, name), WithoutEmptyFunction(name), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The next free tag with the same letters and a higher counter (-FC1 taken: -FC2, ...), or null for a tag without a counter.
    /// Codex review eval 2026-11-10: the model retried a taken -FC1 because the refusal named no free one.
    /// </summary>
    internal static string? FreeTag(string full, IReadOnlyList<string> functions)
    {
        int digits = full.Length;
        while (digits > 0 && char.IsDigit(full[digits - 1]))
        { digits--; }
        if (digits == full.Length || digits == 0 || !char.IsLetter(full[digits - 1]) || full.Length - digits > 6)
        { return null; }
        int counter = int.Parse(full.Substring(digits), CultureInfo.InvariantCulture);
        for (int next = counter + 1; next <= counter + 500; next++)
        {
            string candidate = full.Substring(0, digits) + next.ToString(CultureInfo.InvariantCulture);
            if (!functions.Any(n => Carries(candidate, n) || WithoutEmptyFunction(n).StartsWith(BareOf(candidate, n) + ":", StringComparison.OrdinalIgnoreCase)))
            { return candidate; }
        }
        return null;
    }

    /// <summary>ALREADY_EXISTS when a device carries the tag: EPLAN would merge the two (A13: it does not check).</summary>
    public void CheckFree()
    {
        if (_requiresExistingDevice && !Taken.Any(n => SameName(Tag, n, ignoreCase: true)))
        { throw new BridgeErrorException(ErrorReason.DeviceNotFound, $"'{Tag}' does not identify an existing device; place its first symbol before using join_device."); }
        if (Taken.Count > 0 && !_joinsDevice)
        {
            throw new BridgeErrorException(
                ErrorReason.AlreadyExists,
                Tag.StartsWith("+", StringComparison.Ordinal) && Tag.IndexOf('-') < 0 ? "A location box with this identifier already covers this area on the page; choose another area or identifier." : "The tag is already used by another device (metadata.existing); a new device with this tag would merge with it in EPLAN, which this tool does not do. "
                    + (_freeTag is null ? "Choose a free tag; eplan_device_list with tag_contains shows the tags in use." : "metadata.free is a free tag; or choose another free tag."),
                retryable: false,
                Metadata());
        }

        Dictionary<string, string> Metadata()
        {
            var metadata = new Dictionary<string, string> { ["project"] = Project.Name, ["existing"] = string.Join("\n", Taken.Take(ProjectResolver.MaxCandidates)) };
            if (_freeTag is not null)
            { metadata["free"] = _freeTag; }
            return metadata;
        }
    }

    /// <summary>
    /// The structure a device on the page gets from a tag starting with '-': the page's structure without its document
    /// type, so <c>=GAA+A1&amp;EFS1/60</c> gives <c>=GAA+A1</c>. EPLAN drops parts a device does not use (A13), which
    /// the read-back finds.
    /// </summary>
    internal static string DeviceStructureOf(string pageName) => DrawingNames.DeviceStructureOf(pageName);

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
    /// variant for one that does not exist (EPLAN XML docs), so that is a violation here.
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
        return chosen;
    }

    /// <summary>What the page rules out: the page type, and a point outside the page when EPLAN can tell its size.</summary>
    private static IEnumerable<string> PageProblems(MacroTargetPage page, double x, double y)
    {
        // MX_NFPA 2026-11-07: single-line pages hold the single-line representation of the same devices (EPLAN takes the
        // representation from the page type).
        if (!string.Equals(page.Type, Circuit, StringComparison.Ordinal) && !string.Equals(page.Type, "CircuitSingleLine", StringComparison.Ordinal))
        {
            yield return $"'page' must be a circuit page (Circuit or CircuitSingleLine), but '{page.Name}' is {page.Type}.";
        }

        if (page.Width is { } width && page.Height is { } height && (x > width || y > height))
        {
            yield return string.Format(CultureInfo.InvariantCulture, "'x' and 'y' must lie on the page, {0:0.##} x {1:0.##} mm: x from 0 to {0:0.##} and y from 0 to {1:0.##}.", width, height);
        }
    }

    /// <summary>The eplan_connection_create item kind of a SPECIAL connection symbol, or null for any other symbol.</summary>
    private static string? ConnectionSymbolKind(string? symbol) => symbol?.ToUpperInvariant() switch
    {
        "CO" => "corner",
        "TLRO" or "TLRU" or "TOUL" or "TOUR" => "t_node",
        "PCP" => "potential_point",
        "ACBP" => "autoconnect_break",
        // Interruption points: BP, BPIN, BPOUT, BPF and the like (Codex eval 2026-11-10 asked for SPECIAL/BPIN with tag 24V).
        { } name when name.StartsWith("BP", StringComparison.Ordinal) => "interruption_point",
        _ => null,
    };

    private static string? TagProblem(string tag) =>
        tag.Length == 0 ? "is required: from '-' (-KF20) for the page's structure, or in full (=GAA+A1-KF20)."
        : tag.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)) ? "must not contain white space or control characters."
        : tag.IndexOf(':') >= 0 ? "must not contain ':'; connection point suffixes are EPLAN's."
        // PDF redraw 2026-11-06: a device can have only a location, as the heaters named +F1 in a location box.
        : tag.TrimEnd('-').LastIndexOf('-') < 0 && (tag[0] is not ('+' or '=') || tag.IndexOf('-') >= 0) ? "needs a device part after '-', such as -KF20."
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
    /// <summary>PDF redraw 2026-11-06: on a page without a higher-level function EPLAN stores +S1-K1 as =+S1-K1; the empty '=' adds nothing.</summary>
    internal static string WithoutEmptyFunction(string name) => name.Length > 1 && name[0] == '=' && name[1] is '+' or '-' ? name.Substring(1) : name;

    /// <summary>A121: in a project without structure EPLAN stores -M1 as M1, so a device named M1 takes -M1 (join_device, ALREADY_EXISTS).</summary>
    private static string BareOf(string full, string actual) =>
        full.Length > 1 && full[0] == '-' && !HasStructure(WithoutEmptyFunction(actual)) ? full.Substring(1) : full;

    /// <summary>Whether the device part (before the first ':') carries structure; a pin such as PS1:+ or PS1:- does not count.</summary>
    internal static bool HasStructure(string name) => (name.IndexOf(':') is var colon and >= 0 ? name.Substring(0, colon) : name).IndexOfAny(['=', '+', '-']) >= 0;

    internal static bool SameName(string requested, string? actual, bool ignoreCase = false)
    {
        if (actual is null)
        { return false; }
        StringComparison comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        actual = WithoutEmptyFunction(actual);
        // A121: in a project without structure (NFPA basic projects) EPLAN stores -DI2 as DI2.
        if (requested.Length > 1 && requested[0] == '-' && !HasStructure(actual) && string.Equals(requested.Substring(1), actual, comparison))
        { return true; }
        return string.Equals(requested, actual, comparison)
            || (requested.Length > 0 && requested[0] == '=' && actual.Length > 0 && actual[0] is '+' or '-'
                && requested.EndsWith(actual, comparison) && requested.Substring(0, requested.Length - actual.Length).IndexOfAny(['+', '-', ':', '&']) < 0);
    }
}

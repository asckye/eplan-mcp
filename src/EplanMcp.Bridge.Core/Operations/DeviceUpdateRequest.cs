using System.Globalization;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// The checked arguments of device.update.plan and device.update.execute (docs/tools/eplan_device_update.md, sections
/// 2 and 5): the device, found as eplan_device_get finds it, the functions to rename, and the new full tag.
/// </summary>
internal sealed class DeviceUpdateRequest
{
    /// <summary>The most pages a result lists (tool spec section 3).</summary>
    public const int MaxPages = 20;

    private DeviceUpdateRequest(OpenProject project, DeviceNames device, string newTag, string[] taken)
    {
        Project = project;
        Device = device;
        NewTag = newTag;
        Taken = taken;
    }

    public OpenProject Project { get; }

    public DeviceNames Device { get; }

    /// <summary>The new full tag.</summary>
    public string NewTag { get; }

    public bool Rename { get; private set; }
    public DeviceNames? MergeTarget { get; private set; }

    /// <summary>A PLC module's points without a main function: EPLAN reports a point's name for the whole device (A89).</summary>
    public bool PointsOnly => Device.Functions.Count > 0 && Device.Functions.All(f => !f.IsMain && f.Category == "PLCTerminal");
    public DevicePartTarget? Main { get; private set; }
    public PartSearchItem? Part { get; private set; }
    public string ResultTag => !Rename && Main is not null ? Main.Name : NewTag;
    public string PreviousTag => !Rename && Main is not null ? Main.Name : Device.Tag;

    /// <summary>Functions outside the rename that already carry the new tag (any case), alone or with a suffix.</summary>
    public IReadOnlyList<string> Taken { get; }

    /// <summary>The pages the functions are on, in EPLAN's order, each once.</summary>
    public IEnumerable<string> Pages => Device.Functions.Select(f => f.Page).OfType<string>().Distinct(StringComparer.Ordinal);

    public static DeviceUpdateRequest Check(DeviceUpdateArgs args, IProjectCatalog catalog, IDeviceReader reader, IDeviceRenamer renamer,
        IPartsDatabase? parts = null, IDevicePartAssigner? assigner = null)
    {
        var violations = new List<string>();
        WriteArguments.RequireProject(args.Project, violations);
        string tag = args.Tag?.Trim() ?? string.Empty;
        string newTag = args.NewTag?.Trim() ?? string.Empty;
        string? part = args.Part?.Trim();
        AddIf(violations, tag.Length == 0, "'tag' is required: the device's tag, such as =GAA+A1-KF1 or -KF1.");
        AddIf(violations, newTag.Length == 0 && part is null, "At least one of 'new_tag' or 'part' is required.");
        AddIf(violations, newTag.Length > 0 && NewTagProblem(newTag) is not null, "'new_tag' " + NewTagProblem(newTag));
        AddIf(violations, part is not null && (part.Length == 0 || part.Any(char.IsControl)), "'part' must be a nonempty part number without control characters.");
        AddIf(violations, args.Variant is not null && (part is null || string.IsNullOrWhiteSpace(args.Variant) || args.Variant.Any(char.IsControl)), "'variant' requires 'part' and must be nonempty without control characters.");
        AddIf(violations, part is not null && (parts is null || assigner is null), "Use eplan_device_update for part assignment; this execution path accepts renaming only.");
        if (violations.Count > 0)
        {
            throw WriteArguments.Invalid(violations);
        }

        OpenProject project = WriteArguments.Writable(catalog, args.Project);
        DevicePartTarget? main = part is null ? null : MainFunction(assigner!, project.Path, tag, includeTerminals: newTag.Length == 0);
        tag = main?.Tag ?? tag;
        DeviceNames device = newTag.Length == 0 && main is not null
            ? new DeviceNames { Tag = tag, Functions = [new() { Id = main.Id, IsMain = main.IsMain, Category = main.Category, Page = main.Page }] }
            : renamer.ReadDevice(project.Path, tag)
            ?? (DeviceTags.FindByShownTag(reader.ReadMainFunctionNames(project.Path), tag) is { } full ? renamer.ReadDevice(project.Path, full) : null)
            ?? throw new BridgeErrorException(
                ErrorReason.DeviceNotFound,
                "No device in the project has this tag. Call eplan_device_list with 'tag_contains' to find the device's full tag.",
                retryable: false,
                new Dictionary<string, string> { ["tag"] = tag });
        string target = newTag.Length == 0 ? device.Tag : Full(device, newTag);
        if (newTag.Length > 0 && string.Equals(target, device.Tag, StringComparison.Ordinal))
        {
            throw Invalid($"'new_tag' gives the tag the device has already, '{device.Tag}'.");
        }

        string[] taken = newTag.Length == 0 ? [] :
        [
            .. renamer.FunctionNames(project.Path)
                .Where(name => !BelongsTo(name, device.Tag)
                    && (string.Equals(name, target, StringComparison.OrdinalIgnoreCase) || name.StartsWith(target + ":", StringComparison.OrdinalIgnoreCase)))
                .Distinct(StringComparer.Ordinal),
        ];
        PartSearchItem? selected = null;
        if (part is not null)
        {
            PartsDatabaseContent database = parts!.Read(null);
            List<PartSearchItem> exact = [.. database.Parts.Where(p => string.Equals(p.PartNumber, part, StringComparison.Ordinal))];
            if (exact.Count == 0)
            { exact = [.. database.Parts.Where(p => string.Equals(p.PartNumber, part, StringComparison.OrdinalIgnoreCase))]; }
            string[] numbers = [.. exact.Select(p => p.PartNumber).Distinct(StringComparer.Ordinal)];
            if (numbers.Length > 1)
            { throw ProjectResolver.Ambiguous("part", part, numbers, "Pass the exact part number from eplan_part_search."); }
            if (numbers.Length == 0)
            { throw new BridgeErrorException(ErrorReason.PartNotFound, "No matching part exists; use eplan_part_search to find the part number."); }
            string variant = args.Variant?.Trim() ?? "1";
            selected = exact.FirstOrDefault(p => string.Equals(p.Variant, variant, StringComparison.Ordinal))
                ?? throw Invalid($"Part '{numbers[0]}' has no variant '{variant}'; use eplan_part_search to find its variants.");
            if (main!.Parts.Any(p => p.PartNumber == selected.PartNumber && p.Variant == selected.Variant))
            { throw Invalid("There is nothing to change: the function already references this part and variant."); }
        }
        DeviceNames? destination = taken.Length > 0 && device.Functions.Count > 0 && device.Functions.All(f => !f.IsMain && f.Category == "PLCTerminal")
            ? renamer.ReadDevice(project.Path, target) : null;
        return new DeviceUpdateRequest(project, device, target, taken)
        {
            Rename = newTag.Length > 0,
            Main = main,
            Part = selected,
            MergeTarget = destination?.Functions.Any(f => f.Category == "PLCBox") == true ? destination : null,
        };
    }

    /// <summary>The plan's state: each function to rename by name and session identifier, and how many carry the new tag.</summary>
    public string StateFingerprint() =>
        Fingerprint.Of(Device.Functions.Select(f => (f.Name.Length == 0 ? Device.Tag : f.Name) + "\t" + f.Id + "\t" + f.IsMain + "\t" + f.Category).Append("taken\t" + Taken.Count.ToString(CultureInfo.InvariantCulture))
            .Concat(MergeTarget?.Functions.Select(f => f.Name + "\t" + f.Id + "\t" + f.IsMain + "\t" + f.Category) ?? [])
            .Concat(Main is null ? [] : Main.Parts.Select(p => FormattableString.Invariant($"{p.PartNumber}\t{p.Variant}\t{p.Count}")).Append(Main.Id).Append(Main.Name))
            .Concat(Part is null ? [] : new[] { Part.PartNumber, Part.Variant ?? string.Empty }));

    internal static string ReferencesFingerprint(IEnumerable<DevicePart> references) =>
        Fingerprint.Of(references.Select(p => FormattableString.Invariant($"{p.PartNumber}\t{p.Variant}\t{p.Count}")));

    internal static DevicePartTarget MainFunction(IDevicePartAssigner assigner, string path, string tag, bool includeTerminals = false)
    {
        IReadOnlyList<DevicePartTarget> candidates = assigner.ReadMainFunctions(path, tag, includeTerminals);
        DevicePartTarget[] terminals = [.. candidates.Where(m => m.Category == "Terminal" && m.Name == tag)];
        if (includeTerminals && terminals.Length > 0)
        { return terminals.Length == 1 ? terminals[0] : throw Invalid($"Part assignment requires one exact terminal; found {terminals.Length}."); }
        candidates = [.. candidates.Where(m => m.IsMain)];
        string? full = candidates.Any(m => m.Tag == tag) ? tag
            : DeviceTags.FindByShownTag(candidates.Select(m => new FunctionName { Name = m.Tag, VisibleName = m.VisibleName ?? string.Empty }), tag);
        DevicePartTarget[] found = [.. candidates.Where(m => m.Tag == full)];
        return found.Length == 1 ? found[0]
            : throw Invalid(FormattableString.Invariant($"Part assignment requires exactly one main function with device tag '{tag}'; found {found.Length}."));
    }

    /// <summary>ALREADY_EXISTS when another device carries the new tag: EPLAN would merge them (A13: it does not check).</summary>
    public void CheckFree()
    {
        if (Taken.Count > 0 && MergeTarget is null)
        {
            throw new BridgeErrorException(
                ErrorReason.AlreadyExists,
                $"'{NewTag}' is already used by another device; giving this device the same tag would merge the two devices in EPLAN, only PLC terminals without a main function may join an existing PLC box. Choose a free tag; eplan_device_list with tag_contains shows the tags in use.",
                retryable: false,
                new Dictionary<string, string> { ["project"] = Project.Name, ["existing"] = string.Join("\n", Taken.Take(ProjectResolver.MaxCandidates)) });
        }
    }

    /// <summary>
    /// The new full tag: a full one as given; one starting with '-' after the device's structure, which is its full tag
    /// without '-' and the tag without structure at the end (tool spec section 5).
    /// </summary>
    private static string Full(DeviceNames device, string newTag)
    {
        if (newTag[0] != '-')
        {
            return newTag;
        }

        string own = "-" + device.DeviceTag;
        return device.DeviceTag.Length > 0 && device.Tag.EndsWith(own, StringComparison.Ordinal)
            ? device.Tag.Substring(0, device.Tag.Length - own.Length) + newTag
            : throw Invalid($"The structure of '{device.Tag}' cannot be told apart from its tag; give 'new_tag' in full, such as =GAA+A1-KF10.");
    }

    private static string? NewTagProblem(string newTag) =>
        newTag.Length == 0 ? "is required: the new tag, in full (=GAA+A1-KF10) or from '-' (-KF10) to keep the structure."
        : newTag.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)) ? "must not contain white space or control characters."
        : newTag.IndexOf(':') >= 0 ? "must not contain ':'; existing connection point suffixes are preserved."
        : newTag.LastIndexOf('-') is int dash && (dash < 0 || dash == newTag.Length - 1) ? "needs a device part after '-', such as -KF10."
        : null;

    internal static bool BelongsTo(string name, string tag) =>
        name == tag || name.StartsWith(tag + ":", StringComparison.Ordinal);

    private static BridgeErrorException Invalid(string violation) => WriteArguments.Invalid([violation]);

    private static void AddIf(List<string> violations, bool condition, string violation)
    {
        if (condition)
        {
            violations.Add(violation);
        }
    }
}

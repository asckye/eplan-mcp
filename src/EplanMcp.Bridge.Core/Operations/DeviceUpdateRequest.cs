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

    /// <summary>Functions outside the rename that already carry the new tag (any case), alone or with a suffix.</summary>
    public IReadOnlyList<string> Taken { get; }

    /// <summary>The pages the functions are on, in EPLAN's order, each once.</summary>
    public IEnumerable<string> Pages => Device.Functions.Select(f => f.Page).OfType<string>().Distinct(StringComparer.Ordinal);

    public static DeviceUpdateRequest Check(DeviceUpdateArgs args, IProjectCatalog catalog, IDeviceReader reader, IDeviceRenamer renamer)
    {
        var violations = new List<string>();
        WriteArguments.RequireProject(args.Project, violations);
        string tag = args.Tag?.Trim() ?? string.Empty;
        string newTag = args.NewTag?.Trim() ?? string.Empty;
        AddIf(violations, tag.Length == 0, "'tag' is required: the device's tag, such as =GAA+A1-KF1 or -KF1.");
        AddIf(violations, NewTagProblem(newTag) is not null, "'new_tag' " + NewTagProblem(newTag));
        if (violations.Count > 0)
        {
            throw WriteArguments.Invalid(violations);
        }

        OpenProject project = WriteArguments.Writable(catalog, args.Project);
        DeviceNames device = renamer.ReadDevice(project.Path, tag)
            ?? (DeviceTags.FindByShownTag(reader.ReadMainFunctionNames(project.Path), tag) is { } full ? renamer.ReadDevice(project.Path, full) : null)
            ?? throw new BridgeErrorException(
                ErrorReason.DeviceNotFound,
                "No device in the project has this tag. Call eplan_device_list with 'tag_contains' to find the device's full tag.",
                retryable: false,
                new Dictionary<string, string> { ["tag"] = tag });
        if (device.SubFunctions.FirstOrDefault(name => !string.Equals(name, device.Tag, StringComparison.Ordinal)) is { } suffixed)
        {
            throw Invalid(
                $"This version renames only devices whose functions all carry the device tag itself; '{device.Tag}' has functions with their own suffixes (such as '{suffixed}').");
        }

        string target = Full(device, newTag);
        if (string.Equals(target, device.Tag, StringComparison.Ordinal))
        {
            throw Invalid($"'new_tag' gives the tag the device has already, '{device.Tag}'.");
        }

        string[] taken =
        [
            .. renamer.FunctionNames(project.Path)
                .Where(name => !string.Equals(name, device.Tag, StringComparison.Ordinal)
                    && (string.Equals(name, target, StringComparison.OrdinalIgnoreCase) || name.StartsWith(target + ":", StringComparison.OrdinalIgnoreCase)))
                .Distinct(StringComparer.Ordinal),
        ];
        return new DeviceUpdateRequest(project, device, target, taken);
    }

    /// <summary>The plan's state: each function to rename by name and session identifier, and how many carry the new tag.</summary>
    public string StateFingerprint() =>
        Fingerprint.Of(Device.Functions.Select(f => Device.Tag + "\t" + f.Id).Append("taken\t" + Taken.Count.ToString(CultureInfo.InvariantCulture)));

    /// <summary>ALREADY_EXISTS when another device carries the new tag: EPLAN would merge them (A13: it does not check).</summary>
    public void CheckFree()
    {
        if (Taken.Count > 0)
        {
            throw new BridgeErrorException(
                ErrorReason.AlreadyExists,
                $"'{NewTag}' is already used by another device; giving this device the same tag would merge the two devices in EPLAN, which this tool does not do. Choose a free tag; eplan_device_list with tag_contains shows the tags in use.",
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
        : newTag.IndexOf(':') >= 0 ? "must not contain ':'; connection point suffixes are not renamed by this tool."
        : newTag.LastIndexOf('-') is int dash && (dash < 0 || dash == newTag.Length - 1) ? "needs a device part after '-', such as -KF10."
        : null;

    private static BridgeErrorException Invalid(string violation) => WriteArguments.Invalid([violation]);

    private static void AddIf(List<string> violations, bool condition, string violation)
    {
        if (condition)
        {
            violations.Add(violation);
        }
    }
}

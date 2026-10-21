using System.Globalization;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// The checked arguments of cable.renumber.plan and cable.renumber.execute and what they read; both build them the same
/// way, so they agree on the scheme, the scope and the fingerprint (docs/tools/eplan_cable_renumber.md, section 5).
/// </summary>
internal sealed class CableRenumberRequest
{
    public const int MaxCables = 200;
    public const int MaxChanged = 200;

    private CableRenumberRequest(OpenProject project, CableRenumberArgs args, string scheme)
    {
        Project = project;
        Cables = args.Cables;
        Scheme = scheme;
        KeepExisting = args.KeepExisting;
        Start = args.Start;
        Step = args.Step;
    }

    public OpenProject Project { get; }

    /// <summary>The cables as given, full or shown tags; null for every cable of the project.</summary>
    public IReadOnlyList<string>? Cables { get; }

    public string Scheme { get; }

    public bool KeepExisting { get; }

    public int Start { get; }

    public int Step { get; }

    /// <summary>Every argument problem at once as INVALID_ARGUMENT (mcp-tool-design 6.2), then the project, then the scheme.</summary>
    public static CableRenumberRequest Check(CableRenumberArgs args, IProjectCatalog catalog, ICableRenumberer renumberer)
    {
        var violations = new List<string>();
        WriteArguments.RequireProject(args.Project, violations);
        if (args.Cables is { } cables)
        {
            if (cables.Count is < 1 or > MaxCables)
            {
                violations.Add($"'cables' must hold 1 to {Number(MaxCables)} cable tags; omit it for every cable of the project.");
            }

            for (int i = 0; i < cables.Count && i < MaxCables; i++)
            {
                int same = cables.FindIndex(0, i, c => string.Equals(c, cables[i], StringComparison.Ordinal));
                AddIf(violations, string.IsNullOrWhiteSpace(cables[i]), $"cables[{Number(i)}] is empty; give a cable tag such as +A2-WD4 (eplan_device_list with category Cable).");
                AddIf(violations, same >= 0 && !string.IsNullOrWhiteSpace(cables[i]), $"cables[{Number(i)}] repeats cables[{Number(same)}].");
            }
        }

        AddIf(violations, string.IsNullOrWhiteSpace(args.Scheme), "'scheme' is required: a cable numbering scheme such as Standard.");
        AddIf(violations, args.Start < 1, "'start' must be at least 1.");
        AddIf(violations, args.Step < 1, "'step' must be at least 1.");
        if (violations.Count > 0)
        {
            throw WriteArguments.Invalid(violations);
        }

        OpenProject project = WriteArguments.Writable(catalog, args.Project);
        return new CableRenumberRequest(project, args, SchemeChoice.Require(args.Scheme!.Trim(), renumberer.Schemes(project.Path), "cable"));
    }

    /// <summary>
    /// The functions of the cables in scope: every cable, or those of the given ones, each found by its full tag or, as
    /// eplan_cable_get finds it, by the tag it shows (AMBIGUOUS_NAME when several show it). A tag of another device is
    /// INVALID_ARGUMENT naming its category; DEVICE_NOT_FOUND names every tag that matches nothing.
    /// </summary>
    public List<PlacedFunction> Scope(ICableRenumberer renumberer, IDeviceReader devices)
    {
        List<PlacedFunction> cables = [.. renumberer.ReadCables(Project.Path)];
        if (Cables is null)
        {
            return cables;
        }

        var names = new HashSet<string>(cables.Select(c => c.Name), StringComparer.Ordinal);
        var tags = new HashSet<string>(StringComparer.Ordinal);
        var missing = new List<string>();
        var others = new List<string>();
        IReadOnlyList<FunctionName>? mains = null;
        foreach (string given in Cables)
        {
            string tag = given.Trim();
            string? full = names.Contains(tag) ? tag : DeviceTags.FindByShownTag(mains ??= devices.ReadMainFunctionNames(Project.Path), tag, "cables");
            if (full is not null && names.Contains(full))
            {
                tags.Add(full);
            }
            else if (devices.ReadDevice(Project.Path, full ?? tag, null, withProperties: false) is { } device)
            {
                // The category is EPLAN's enum name, not project data, so the message may carry it (as cable.get does).
                others.Add($"{given} ({device.Main.Category})");
            }
            else
            {
                missing.Add(given);
            }
        }

        if (others.Count > 0)
        {
            throw new BridgeErrorException(
                ErrorReason.InvalidArgument,
                $"Some tags name devices that are not cables: {string.Join(", ", others)}. Nothing was numbered. Find the cables with eplan_device_list and category Cable.",
                retryable: false,
                new Dictionary<string, string> { ["notCables"] = string.Join("\n", others) });
        }

        return missing.Count == 0
            ? [.. cables.Where(c => tags.Contains(c.Name))]
            : throw new BridgeErrorException(
                ErrorReason.DeviceNotFound,
                $"Project '{Project.Name}' has no device with these tags: {string.Join(", ", missing)}. Nothing was numbered. Find the cables with eplan_device_list and category Cable.",
                retryable: false,
                new Dictionary<string, string> { ["project"] = Project.Name, ["missing"] = string.Join("\n", missing) });
    }

    /// <summary>The full tags of the cables in <paramref name="scope"/>, each once, for EPLAN; null for every cable.</summary>
    public IReadOnlyList<string>? Tags(IEnumerable<PlacedFunction> scope) => Cables is null ? null : [.. scope.Select(c => c.Name).Distinct(StringComparer.Ordinal)];

    /// <summary>How many cables EPLAN is asked to number: every one in scope, or with <see cref="KeepExisting"/> those with '?' (XML).</summary>
    public int ToNumber(IEnumerable<PlacedFunction> scope) =>
        scope.Select(c => c.Name).Where(tag => tag.Length > 0 && (!KeepExisting || DeviceRenumberRequest.Unnumbered(tag))).Distinct(StringComparer.Ordinal).Count();

    /// <summary>The change list's text, e.g. <c>all cable tags in the project (78 cables), scheme: Standard, start 1, step 1</c>.</summary>
    public string Describe(int cables) =>
        (KeepExisting ? "cable tags containing '?'" : "all cable tags")
        + (Cables is null ? " in the project" : " of the given cables")
        + $" ({Number(cables)} {(cables == 1 ? "cable" : "cables")}), scheme: {Scheme}, start {Number(Start)}, step {Number(Step)}";

    /// <summary>The plan's state: every function of the cables in scope by its session identifier and full tag.</summary>
    public static string FingerprintOf(IEnumerable<PlacedFunction> scope) => Fingerprint.Of(scope.Select(c => c.Id + "\t" + c.Name));

    private static void AddIf(List<string> violations, bool condition, string violation)
    {
        if (condition)
        {
            violations.Add(violation);
        }
    }

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}

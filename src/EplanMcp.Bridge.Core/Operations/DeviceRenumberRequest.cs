using System.Globalization;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// The checked arguments of device.renumber.plan and device.renumber.execute and what they read; both build them the same
/// way, so they agree on the scope, the devices and the fingerprint (docs/tools/eplan_device_renumber.md, section 5).
/// </summary>
internal sealed class DeviceRenumberRequest
{
    public const int MaxPages = 50;
    public const int MaxChanged = 200;
    public const int MaxListed = 50;

    private DeviceRenumberRequest(OpenProject project, DeviceRenumberArgs args, string? scheme)
    {
        Project = project;
        Pages = args.Pages;
        OnlyUnnumbered = args.OnlyUnnumbered;
        Scheme = scheme;
        Start = args.Start;
        Step = args.Step;
    }

    public OpenProject Project { get; }

    /// <summary>The pages in scope; null for the whole project.</summary>
    public IReadOnlyList<string>? Pages { get; }

    public bool OnlyUnnumbered { get; }

    /// <summary>The numbering scheme; null for EPLAN's default.</summary>
    public string? Scheme { get; }

    public int Start { get; }

    public int Step { get; }

    /// <summary>Every argument problem at once as INVALID_ARGUMENT (mcp-tool-design 6.2), then the project.</summary>
    public static DeviceRenumberRequest Check(DeviceRenumberArgs args, IProjectCatalog catalog)
    {
        var violations = new List<string>();
        WriteArguments.RequireProject(args.Project, violations);
        if (args.Pages is { } pages)
        {
            if (pages.Count is < 1 or > MaxPages)
            {
                violations.Add($"'pages' must hold 1 to {Number(MaxPages)} full page names; omit it for the whole project.");
            }

            for (int i = 0; i < pages.Count && i < MaxPages; i++)
            {
                int same = pages.FindIndex(0, i, p => string.Equals(p, pages[i], StringComparison.OrdinalIgnoreCase));
                AddIf(violations, string.IsNullOrWhiteSpace(pages[i]), $"pages[{Number(i)}] is empty; give a full page name such as =GAA+A1&EFS1/60 (eplan_page_list).");
                AddIf(violations, same >= 0 && !string.IsNullOrWhiteSpace(pages[i]), $"pages[{Number(i)}] repeats pages[{Number(same)}] (letter case does not count).");
            }
        }

        AddIf(violations, args.Start < 1, "'start' must be at least 1.");
        AddIf(violations, args.Step < 1, "'step' must be at least 1.");
        string? scheme = string.IsNullOrWhiteSpace(args.Scheme) ? null : args.Scheme!.Trim();
        return violations.Count > 0 ? throw WriteArguments.Invalid(violations) : new DeviceRenumberRequest(WriteArguments.Writable(catalog, args.Project), args, scheme);
    }

    /// <summary>The functions in scope, the pages by their exact names; PAGE_NOT_FOUND names every page that is missing.</summary>
    public List<PlacedFunction> Scope(IDeviceRenumberer renumberer)
    {
        if (Pages is null)
        {
            return [.. renumberer.ReadFunctions(Project.Path, null) ?? []];
        }

        var functions = new List<PlacedFunction>();
        var missing = new List<string>();
        foreach (string page in Pages)
        {
            if (renumberer.ReadFunctions(Project.Path, page) is { } onPage)
            {
                functions.AddRange(onPage);
            }
            else
            {
                missing.Add(page);
            }
        }

        return missing.Count == 0
            ? functions
            : throw new BridgeErrorException(
                ErrorReason.PageNotFound,
                $"Project '{Project.Name}' has no page with exactly these names (letter case counts): {string.Join(", ", missing)}. Nothing was numbered. Use eplan_page_list to find the exact names.",
                retryable: false,
                new Dictionary<string, string> { ["project"] = Project.Name, ["missing"] = string.Join("\n", missing) });
    }

    /// <summary>The devices EPLAN is asked to number: every one in scope, or with <see cref="OnlyUnnumbered"/> those with '?'.</summary>
    public List<string> Devices(IEnumerable<PlacedFunction> scope) =>
        [.. scope.Select(f => DeviceOf(f.Name)).Where(tag => tag.Length > 0 && (!OnlyUnnumbered || Unnumbered(tag))).Distinct(StringComparer.Ordinal)];

    /// <summary>The change list's text, e.g. <c>device tags containing '?' on 1 page (5 devices), scheme: EPLAN default, start 1, step 1</c>.</summary>
    public string Describe(int devices) =>
        (OnlyUnnumbered ? "device tags containing '?'" : "all device tags")
        + (Pages is null ? " in the project" : " on " + Number(Pages.Count) + (Pages.Count == 1 ? " page" : " pages"))
        + $" ({Number(devices)} {(devices == 1 ? "device" : "devices")}), scheme: {Scheme ?? "EPLAN default"}, start {Number(Start)}, step {Number(Step)}";

    /// <summary>The device a function belongs to: its full tag before the connection point (<c>:</c>).</summary>
    public static string DeviceOf(string name)
    {
        int colon = name.IndexOf(':');
        return colon < 0 ? name : name.Substring(0, colon);
    }

    /// <summary>Whether the device tag, before any connection point, contains '?' (EPLAN's mark for "not numbered yet").</summary>
    public static bool Unnumbered(string name) => DeviceOf(name).IndexOf('?') >= 0;

    /// <summary>The plan's state: every function in scope by its session identifier and full name.</summary>
    public static string FingerprintOf(IEnumerable<PlacedFunction> scope) => Fingerprint.Of(scope.Select(f => f.Id + "\t" + f.Name));

    /// <summary>
    /// The devices whose tag changed, each pair once (auxiliary functions follow their main function), matched by session
    /// identifier; and how many functions of <paramref name="before"/> had no identifier left afterwards.
    /// </summary>
    public static (List<RenamedObject> Changed, int Unmatched) Changes(IEnumerable<PlacedFunction> before, IEnumerable<PlacedFunction> after)
    {
        var now = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (PlacedFunction function in after)
        {
            now[function.Id] = function.Name;
        }

        var changed = new List<RenamedObject>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        int unmatched = 0;
        foreach (PlacedFunction function in before)
        {
            if (!now.TryGetValue(function.Id, out string? name))
            {
                unmatched++;
                continue;
            }

            string from = DeviceOf(function.Name);
            string to = DeviceOf(name);
            if (from.Length > 0 && !string.Equals(from, to, StringComparison.Ordinal) && seen.Add(from + "\n" + to))
            {
                changed.Add(new RenamedObject { From = from, To = to });
            }
        }

        return (changed, unmatched);
    }

    private static void AddIf(List<string> violations, bool condition, string violation)
    {
        if (condition)
        {
            violations.Add(violation);
        }
    }

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}

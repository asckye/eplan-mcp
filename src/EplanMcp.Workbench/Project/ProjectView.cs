using System.ComponentModel;
using System.IO;
using System.Text.Json.Nodes;
using EplanMcp.Workbench.Approvals;
using TiaOpenness.Gui.Localization;

namespace EplanMcp.Workbench.Project;

internal enum ProjectState
{
    Idle,
    Loading,
    NoHost,
    NotConnected,
    Ready,
    Failed,
}

/// <summary>One device as the page's device table and the group's device list show it.</summary>
internal sealed record ProjectDeviceRow(string Tag, string VisibleTag, string Category, string Page, int Parts)
{
    public string PartsText => Parts.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>One EPLAN check message about the chosen page or one of its devices.</summary>
internal sealed record ProjectCheckRow(string Severity, string Glyph, string Code, string Text)
{
    /// <summary>The text after the code, as the row shows it.</summary>
    public string Detail => " · " + Text;
}

/// <summary>
/// The project page's model (ADR-0065 decision 2): the EPLAN the Host sees, the project, its tree, and the devices of
/// the page or group chosen, all read through <see cref="IProjectTools"/> with read-only tools. Nothing here writes.
/// </summary>
internal sealed class ProjectView(IProjectTools? tools) : INotifyPropertyChanged
{
    /// <summary>How many pages the tree reads at most (four calls of 200); a larger project says so.</summary>
    public const int MaxPages = 800;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ProjectState State { get; private set; } = tools is null ? ProjectState.NoHost : ProjectState.Idle;

    public bool IsLoading => State == ProjectState.Loading;

    /// <summary>Why the page shows nothing: the Host's text when it gave one, made plain.</summary>
    public string Problem { get; private set; } = string.Empty;

    public string ProjectName { get; private set; } = string.Empty;

    public string Eplan { get; private set; } = string.Empty;

    public int PageTotal { get; private set; }

    public int DeviceTotal { get; private set; }

    public bool Truncated { get; private set; }

    public IReadOnlyList<ProjectGroup> Groups { get; private set; } = [];

    /// <summary>The page or group whose devices <see cref="Devices"/> holds.</summary>
    public string DevicesOf { get; private set; } = string.Empty;

    public IReadOnlyList<ProjectDeviceRow> Devices { get; private set; } = [];

    public bool DevicesLoading { get; private set; }

    /// <summary>The chosen page's net list as text lines: its devices, then its connections (Workbench 4.0 "网表摘要").</summary>
    public IReadOnlyList<string> Netlist { get; private set; } = [];

    /// <summary>The check messages about the chosen page or its devices, errors first.</summary>
    public IReadOnlyList<ProjectCheckRow> Checks { get; private set; } = [];

    public int CheckErrors { get; private set; }

    public int CheckWarnings { get; private set; }

    /// <summary>How many check messages the page reads at most (five calls of 200).</summary>
    public const int MaxMessages = 1000;

    private List<JsonObject>? _messages;

    /// <summary>Reads the EPLAN, the project and its pages again.</summary>
    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        if (tools is null || IsLoading)
        {
            return;
        }

        (State, Problem) = (ProjectState.Loading, string.Empty);
        Changed();
        try
        {
            JsonObject status = await tools.ReadAsync("eplan_session_status", [], cancellationToken).ConfigureAwait(true);
            if (status["connected"] is not JsonValue connected || !connected.TryGetValue(out bool isConnected) || !isConnected)
            {
                (State, Problem) = (ProjectState.NotConnected, Plain(status["hint"]));
                return;
            }

            Eplan = status["eplan"] is JsonObject eplan ? "EPLAN " + Plain(eplan["version"]) + (Plain(eplan["variant"]) is { Length: > 0 } variant ? " · " + variant : string.Empty) : string.Empty;
            JsonObject project = await tools.ReadAsync("eplan_project_get", [], cancellationToken).ConfigureAwait(true);
            ProjectName = Plain(project["project"]?["name"]);
            PageTotal = Count(project["counts"]?["pages"]);
            DeviceTotal = Count(project["counts"]?["devices"]);

            var pages = new List<ProjectPageNode>();
            string? cursor = null;
            do
            {
                JsonObject args = new() { ["limit"] = 200 };
                if (cursor is not null)
                {
                    args["cursor"] = cursor;
                }

                JsonObject list = await tools.ReadAsync("eplan_page_list", args, cancellationToken).ConfigureAwait(true);
                pages.AddRange(ProjectTree.Pages(list));
                cursor = list["nextCursor"] is JsonValue next && next.TryGetValue(out string? text) && text.Length > 0 ? text : null;
            }
            while (cursor is not null && pages.Count < MaxPages);

            Truncated = cursor is not null;
            Groups = ProjectTree.Build(pages);
            _messages = null;
            State = ProjectState.Ready;
        }
        catch (Exception ex) when (ex is ProjectToolException or InvalidOperationException or IOException or TimeoutException or System.ComponentModel.Win32Exception or ModelContextProtocol.McpException)
        {
            (State, Problem) = (ProjectState.Failed, ProjectText.Line(ex.Message, 300));
        }
        finally
        {
            Changed();
        }
    }

    /// <summary>The devices of one page (<paramref name="page"/>) or of a group, by its structure prefix.</summary>
    public async Task LoadDevicesAsync(string? page, string? prefix, CancellationToken cancellationToken)
    {
        if (tools is null || State != ProjectState.Ready)
        {
            return;
        }

        DevicesOf = page ?? prefix ?? string.Empty;
        (DevicesLoading, Devices, Netlist, Checks, CheckErrors, CheckWarnings) = (true, [], [], [], 0, 0);
        Changed();
        try
        {
            JsonObject args = new() { ["limit"] = 200 };
            if (page is not null)
            {
                args["page"] = page;
            }
            else if (prefix is { Length: > 0 })
            {
                args["tag_contains"] = prefix;
            }

            JsonObject list = await tools.ReadAsync("eplan_device_list", args, cancellationToken).ConfigureAwait(true);
            Devices =
            [
                .. (list["devices"] as JsonArray ?? []).OfType<JsonObject>().Select(d => new ProjectDeviceRow(
                    Plain(d["tag"]), Plain(d["visibleTag"]), Plain(d["category"]), Plain(d["page"]), Count(d["parts"]))),
            ];
            if (page is not null)
            {
                JsonObject connections = await tools.ReadAsync("eplan_connection_list", new JsonObject { ["page"] = page, ["limit"] = 200 }, cancellationToken).ConfigureAwait(true);
                Netlist = NetlistOf(page, Devices, connections);
                _messages ??= await MessagesAsync(cancellationToken).ConfigureAwait(true);
                HashSet<string> names = [page, .. Devices.Select(d => d.Tag)];
                Checks =
                [
                    .. _messages.Where(m => m["object"]?["name"] is JsonValue name && name.TryGetValue(out string? text) && names.Contains(text))
                        .Select(CheckOf).OrderBy(c => c.Severity switch { "error" => 0, "warning" => 1, _ => 2 }),
                ];
                CheckErrors = Checks.Count(c => c.Severity == "error");
                CheckWarnings = Checks.Count(c => c.Severity == "warning");
            }
        }
        catch (Exception ex) when (ex is ProjectToolException or InvalidOperationException or IOException or TimeoutException or ModelContextProtocol.McpException)
        {
            (Devices, Problem) = ([], ProjectText.Line(ex.Message, 300));
        }
        finally
        {
            DevicesLoading = false;
            Changed();
        }
    }

    /// <summary>A button's call is running (ADR-0065 step 3); the buttons wait for it.</summary>
    public bool Acting { get; private set; }

    /// <summary>What the last button did, or why it did nothing; empty before the first. Plain text.</summary>
    public string ActionResult { get; private set; } = string.Empty;

    /// <summary>Whether <see cref="ActionResult"/> reports a failure.</summary>
    public bool ActionFailed { get; private set; }

    /// <summary>
    /// The engineer's write behind a button: previews it, asks <paramref name="confirm"/> with the change list, and only
    /// then runs it. Null from <paramref name="confirm"/> or a failure leaves the project as it was.
    /// </summary>
    public async Task WriteAsync(EngineerCall call, Func<IReadOnlyList<string>, bool> confirm, CancellationToken cancellationToken)
    {
        if (tools is null || State != ProjectState.Ready || Acting)
        {
            return;
        }

        await ActAsync(async () =>
        {
            JsonObject preview = await tools.RunAsync(call, execute: false, cancellationToken).ConfigureAwait(true);
            IReadOnlyList<string> changes = ChangesOf(preview);
            if (!confirm(changes))
            {
                return Loc.Current["Project.ActionCancelled"];
            }

            JsonObject done = await tools.RunAsync(call, execute: true, cancellationToken).ConfigureAwait(true);
            if (call.Tool == "eplan_check_run")
            {
                _messages = null; // the check replaced EPLAN's messages
            }

            return Outcome(call.Tool, done);
        }).ConfigureAwait(true);
    }

    /// <summary>An export: no preview and no approval, it changes no project (ADR-0012).</summary>
    public async Task ExportAsync(EngineerCall call, CancellationToken cancellationToken)
    {
        if (tools is null || State != ProjectState.Ready || Acting)
        {
            return;
        }

        await ActAsync(async () =>
        {
            JsonObject done = await tools.RunAsync(call, execute: true, cancellationToken).ConfigureAwait(true);
            return Loc.Current.T("Project.Exported", Count(done["fileCount"]), Plain(done["directory"]));
        }).ConfigureAwait(true);
    }

    /// <summary>The change list of a preview, one plain line per change.</summary>
    internal static IReadOnlyList<string> ChangesOf(JsonObject preview) =>
    [
        .. (preview["changes"] as JsonArray ?? []).OfType<JsonObject>().Select(c =>
        {
            string line = $"{Plain(c["action"])} {Plain(c["kind"])} {Plain(c["name"])}".Trim();
            string property = Plain(c["property"]);
            return property.Length == 0 ? line : $"{line} · {property}: {Plain(c["oldValue"])} → {Plain(c["newValue"])}";
        }),
    ];

    private static string Outcome(string tool, JsonObject done)
    {
        if (Plain(done["execution"]) != "committed")
        {
            return Loc.Current.T("Project.NotDone", Plain(done["execution"]), Plain(done["hint"]));
        }

        return tool == "eplan_check_run"
            ? Loc.Current.T("Project.Checked", Count(done["counts"]?["error"]), Count(done["counts"]?["warning"]), Count(done["counts"]?["hint"]))
            : Loc.Current.T("Project.ReportsUpdated", Count(done["reportPages"]?["before"]), Count(done["reportPages"]?["after"]));
    }

    private async Task ActAsync(Func<Task<string>> action)
    {
        (Acting, ActionResult, ActionFailed) = (true, string.Empty, false);
        Changed();
        try
        {
            ActionResult = await action().ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is ProjectToolException or InvalidOperationException or IOException or TimeoutException or ModelContextProtocol.McpException)
        {
            (ActionResult, ActionFailed) = (ProjectText.Line(ex.Message, 300), true);
        }
        finally
        {
            Acting = false;
            Changed();
        }
    }

    /// <summary>A short status for the page's bar, in the current language.</summary>
    public string Summary => State switch
    {
        ProjectState.Ready => Loc.Current.T("Project.Counts", PageTotal, DeviceTotal),
        ProjectState.Loading => Loc.Current["Project.Reading"],
        ProjectState.NoHost => Loc.Current["Host.Missing"],
        ProjectState.NotConnected => Loc.Current["Project.NotConnected"],
        ProjectState.Failed => Loc.Current["Project.ReadFailed"],
        _ => string.Empty,
    };

    /// <summary>
    /// The net list lines of one page: each device with its category, then each connection from pin to pin with its
    /// cross-section and colour. Tags lose the page's structure prefix, as EPLAN shows them on the page.
    /// </summary>
    internal static IReadOnlyList<string> NetlistOf(string page, IReadOnlyList<ProjectDeviceRow> devices, JsonObject connections)
    {
        string prefix = ProjectTree.Prefix(page);
        string Short(string tag) => prefix.Length > 0 && tag.StartsWith(prefix, StringComparison.Ordinal) && tag.Length > prefix.Length ? tag[prefix.Length..] : tag;
        string End(JsonNode? end) => end is JsonObject point ? Short(Plain(point["device"])) + ":" + Plain(point["pin"]) : "—";
        var lines = new List<string>();
        lines.AddRange(devices.Select(d => $"DEVICE  {Short(d.Tag),-16} {d.Category}".TrimEnd()));
        lines.AddRange((connections["connections"] as JsonArray ?? []).OfType<JsonObject>().Where(c => Plain(c["kind"]) != "3d").Select(c =>
            $"WIRE    {End(c["from"]),-16} → {End(c["to"]),-16} {Plain(c["crossSection"])} {Plain(c["colour"])}".TrimEnd()));
        return lines;
    }

    private static ProjectCheckRow CheckOf(JsonObject message)
    {
        string severity = Plain(message["severity"]);
        string glyph = severity switch { "error" => "✕", "warning" => "!", _ => "i" };
        string code = "#" + Count(message["number"]).ToString(System.Globalization.CultureInfo.InvariantCulture)
            + (Plain(message["region"]) is { Length: > 0 } region ? " · " + region : string.Empty);
        return new ProjectCheckRow(severity, glyph, code, Plain(message["text"]));
    }

    private async Task<List<JsonObject>> MessagesAsync(CancellationToken cancellationToken)
    {
        var messages = new List<JsonObject>();
        string? cursor = null;
        do
        {
            JsonObject args = new() { ["limit"] = 200 };
            if (cursor is not null)
            {
                args["cursor"] = cursor;
            }

            JsonObject list = await tools!.ReadAsync("eplan_check_list", args, cancellationToken).ConfigureAwait(true);
            messages.AddRange((list["messages"] as JsonArray ?? []).OfType<JsonObject>());
            cursor = list["nextCursor"] is JsonValue next && next.TryGetValue(out string? text) && text.Length > 0 ? text : null;
        }
        while (cursor is not null && messages.Count < MaxMessages);

        return messages;
    }

    private static string Plain(JsonNode? node) => node is JsonValue value && value.TryGetValue(out string? text) ? ProjectText.Line(text) : string.Empty;

    private static int Count(JsonNode? node) => node is JsonValue value && value.TryGetValue(out int count) ? count : 0;

    private void Changed() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
}

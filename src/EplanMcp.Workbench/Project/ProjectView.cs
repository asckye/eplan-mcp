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

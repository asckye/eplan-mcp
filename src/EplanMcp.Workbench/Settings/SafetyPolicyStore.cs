using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace EplanMcp.Workbench.Settings;

/// <summary>
/// What the Workbench shows and changes of the Host's write policy: <c>writesOutsideScratch</c> and
/// <c>approvalTimeoutSeconds</c> in the Host's <c>config.json</c>. That file is the only source: the Host reads the
/// value at every write (ADR-0061), so what is shown here is what the next write gets. The Workbench never answers an
/// approval for the engineer. A change that cannot be saved changes nothing, and <c>deny</c> is not a state of the
/// switch: it is shown as it is and only an edit of the file lifts it.
/// </summary>
internal sealed class SafetyPolicyStore(string? configFile = null)
{
    private static readonly JsonSerializerOptions _writeOptions = new() { WriteIndented = true };

    private string _mode = "approve";
    private int _approvalTimeoutSeconds = 120;

    public event Action? Changed;

    public string? ConfigFile { get; } = configFile;

    /// <summary>False only when the file says <c>allow</c>: ordinary writes outside scratch then run without approval.</summary>
    public bool RequireNormalWriteApproval => _mode != "allow";

    /// <summary>True when the file says <c>deny</c>: writes outside scratch are refused, and the switch does not apply.</summary>
    public bool WritesDenied => _mode == "deny";

    public int ApprovalTimeoutSeconds => _approvalTimeoutSeconds;

    /// <summary>
    /// The folders where writes need no approval (<c>scratchRoots</c>), shown on the settings page as the file has them,
    /// made plain; the data root's <c>scratch</c> when the file names none. The Workbench never changes them.
    /// </summary>
    public IReadOnlyList<string> ScratchRoots { get; private set; } = [];

    /// <summary>Reads the file. Without one, or with one that cannot be read, the Host asks for approval, and so this shows.</summary>
    public void Load()
    {
        string mode = "approve";
        int timeout = 120;
        if (Read() is { } root)
        {
            string? value = Text(root["writesOutsideScratch"])?.ToLowerInvariant();
            if (value is "deny" or "allow")
            {
                mode = value;
            }

            if (root["approvalTimeoutSeconds"] is JsonValue seconds && seconds.TryGetValue(out int secs) && secs is >= 60 and <= 300)
            {
                timeout = secs;
            }
        }

        _mode = mode;
        _approvalTimeoutSeconds = timeout;
        List<string> roots = [];
        if (Read()?["scratchRoots"] is JsonArray listed)
        {
            roots.AddRange(listed.Select(Text).OfType<string>().Where(r => r.Length > 0).Select(r => Approvals.ProjectText.Line(r)));
        }

        ScratchRoots = roots.Count > 0 ? roots : [Path.Combine(Path.GetDirectoryName(ConfigFile) ?? string.Empty, "scratch")];
        Changed?.Invoke();
    }

    /// <summary>False, with nothing changed, when the file cannot be written or the policy is <c>deny</c>.</summary>
    public bool SetRequireNormalWriteApproval(bool require)
    {
        string mode = require ? "approve" : "allow";
        if (WritesDenied || !UpdateConfig(root => root["writesOutsideScratch"] = mode))
        {
            return false;
        }

        _mode = mode;
        Changed?.Invoke();
        return true;
    }

    public bool SetApprovalTimeoutSeconds(int seconds)
    {
        if (seconds is < 60 or > 300 || !UpdateConfig(root => root["approvalTimeoutSeconds"] = seconds))
        {
            return false;
        }

        _approvalTimeoutSeconds = seconds;
        Changed?.Invoke();
        return true;
    }

    private static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue(out string? text) ? text : null;

    /// <summary>The file's object; null without a file, and when it cannot be read or is not a JSON object.</summary>
    private JsonObject? Read()
    {
        if (ConfigFile is not { Length: > 0 } path || !File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private bool UpdateConfig(Action<JsonObject> mutate)
    {
        if (ConfigFile is not { Length: > 0 } path)
        {
            return true; // no file behind this store: the value lives in memory only
        }

        try
        {
            // A file that is there but is not a JSON object is the engineer's to repair, not ours to replace.
            JsonObject? root = File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path)) as JsonObject : new JsonObject();
            if (root is null)
            {
                return false;
            }

            mutate(root);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, root.ToJsonString(_writeOptions));
            File.Move(tmp, path, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
    }
}

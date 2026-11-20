using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace EplanMcp.Workbench.Settings;

/// <summary>
/// Shared live safety policy bridge between Workbench UI (Status Bar, Approvals bar, Settings page),
/// the approval queue, and Host <c>config.json</c>.
/// Toggling normal-write approval updates <c>writesOutsideScratch</c> (<c>approve</c> / <c>allow</c>);
/// destructive deletes, master-data writes, and ADR-0010 suspension releases are always gated.
/// </summary>
internal sealed class SafetyPolicyStore(string? configFile = null)
{
    private static readonly JsonSerializerOptions _writeOptions = new() { WriteIndented = true };

    private bool _requireNormalWriteApproval = true;
    private int _approvalTimeoutSeconds = 120;

    public event Action? Changed;

    public string? ConfigFile { get; } = configFile;

    public bool RequireNormalWriteApproval => _requireNormalWriteApproval;

    public int ApprovalTimeoutSeconds => _approvalTimeoutSeconds;

    public void Load(bool fallbackRequireApproval = true)
    {
        bool require = fallbackRequireApproval;
        int timeout = 120;
        if (ConfigFile is { Length: > 0 } path && File.Exists(path))
        {
            try
            {
                if (JsonNode.Parse(File.ReadAllText(path)) is JsonObject root)
                {
                    if (root["writesOutsideScratch"]?.GetValue<string>() is { } mode)
                    {
                        require = !string.Equals(mode, "allow", StringComparison.OrdinalIgnoreCase);
                    }

                    if (root["approvalTimeoutSeconds"]?.GetValue<int>() is >= 60 and <= 300 and var secs)
                    {
                        timeout = secs;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
            {
                // Keep fallback when config.json cannot be parsed; SettingsPage still surfaces the raw text.
            }
        }

        _requireNormalWriteApproval = require;
        _approvalTimeoutSeconds = timeout;
        Changed?.Invoke();
    }

    public bool SetRequireNormalWriteApproval(bool require)
    {
        _requireNormalWriteApproval = require;
        bool saved = UpdateConfig(root => root["writesOutsideScratch"] = require ? "approve" : "allow");
        Changed?.Invoke();
        return saved;
    }

    public bool SetApprovalTimeoutSeconds(int seconds)
    {
        if (seconds is < 60 or > 300)
        {
            return false;
        }

        _approvalTimeoutSeconds = seconds;
        bool saved = UpdateConfig(root => root["approvalTimeoutSeconds"] = seconds);
        Changed?.Invoke();
        return saved;
    }

    private bool UpdateConfig(Action<JsonObject> mutate)
    {
        if (ConfigFile is not { Length: > 0 } path)
        {
            return true;
        }

        try
        {
            JsonObject root;
            if (File.Exists(path))
            {
                root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? new JsonObject();
            }
            else
            {
                root = new JsonObject();
            }

            mutate(root);
            string dir = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(dir);
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

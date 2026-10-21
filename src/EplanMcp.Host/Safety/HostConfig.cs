using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EplanMcp.Host.Safety;

/// <summary>What writes to projects outside every scratch root need.</summary>
internal enum OutsideScratchWrites
{
    /// <summary>Refused with POLICY_DENIED; the default during M3a (M3 design Q5), now a choice in config.json.</summary>
    Deny,

    /// <summary>Allowed after the engineer approves each one (ADR-0005); the default since M3a ended (M3 design Q5).</summary>
    Approve,
}

/// <summary>
/// The safety settings of <c>config.json</c> (M3 design 4.3). Only the file changes them, never a tool (SEC-02).
/// </summary>
internal sealed record HostConfig(IReadOnlyList<string> ScratchRoots, OutsideScratchWrites WritesOutsideScratch)
{
    /// <summary>How long the engineer has to answer an approval or a confirmation by default (ADR-0011).</summary>
    public static readonly TimeSpan DefaultApprovalTimeout = TimeSpan.FromSeconds(120);

    /// <summary>The shortest <c>approvalTimeoutSeconds</c> may set (ADR-0011).</summary>
    public static readonly TimeSpan MinApprovalTimeout = TimeSpan.FromSeconds(60);

    /// <summary>The longest <c>approvalTimeoutSeconds</c> may set (ADR-0011).</summary>
    public static readonly TimeSpan MaxApprovalTimeout = TimeSpan.FromSeconds(300);

    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        NumberHandling = JsonNumberHandling.Strict,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };

    /// <summary>How long the engineer has to answer an approval or a confirmation: <c>approvalTimeoutSeconds</c>.</summary>
    public TimeSpan ApprovalTimeout { get; init; } = DefaultApprovalTimeout;

    /// <summary>
    /// Without config.json: the default scratch root, and writes elsewhere each approved by the engineer (ADR-0005;
    /// M3 design Q5 moved this from Deny once the approval dialog and Q1-Q3 passed).
    /// </summary>
    public static HostConfig Defaults(HostDataLocations locations) => new([locations.DefaultScratch], OutsideScratchWrites.Approve);

    /// <summary>
    /// The configuration in <see cref="HostDataLocations.ConfigFile"/>, or the defaults when there is none; null and
    /// the problem when the file is invalid, so serve refuses to start and doctor says why (architecture 8.9).
    /// </summary>
    public static (HostConfig? Config, string? Problem) Load(HostDataLocations locations)
    {
        string text;
        try
        {
            text = File.ReadAllText(locations.ConfigFile);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return (Defaults(locations), null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (null, "cannot be read: " + ex.Message);
        }

        return Parse(text, locations);
    }

    internal static (HostConfig? Config, string? Problem) Parse(string text, HostDataLocations locations)
    {
        ConfigFile? file;
        try
        {
            file = JsonSerializer.Deserialize<ConfigFile>(text, _json);
        }
        catch (JsonException ex)
        {
            return (null, "is not valid: " + ex.Message);
        }

        HostConfig defaults = Defaults(locations);
        List<string> roots = file?.ScratchRoots ?? [.. defaults.ScratchRoots];
        if (roots.Count == 0 || roots.Any(r => string.IsNullOrWhiteSpace(r) || !Path.IsPathFullyQualified(r)))
        {
            return (null, "is not valid: 'scratchRoots' must list at least one full path, such as C:\\EplanScratch");
        }

        TimeSpan timeout = file?.ApprovalTimeoutSeconds is { } seconds ? TimeSpan.FromSeconds(seconds) : defaults.ApprovalTimeout;
        if (timeout < MinApprovalTimeout || timeout > MaxApprovalTimeout)
        {
            return (null, $"is not valid: 'approvalTimeoutSeconds' must be a whole number of seconds from {Seconds(MinApprovalTimeout)} to {Seconds(MaxApprovalTimeout)}");
        }

        return (new HostConfig(roots, file?.WritesOutsideScratch ?? defaults.WritesOutsideScratch) { ApprovalTimeout = timeout }, null);
    }

    /// <summary>A whole number of seconds, as config.json, doctor and the error messages write it.</summary>
    public static string Seconds(TimeSpan span) => ((long)span.TotalSeconds).ToString(CultureInfo.InvariantCulture);

    private sealed class ConfigFile
    {
        public List<string>? ScratchRoots { get; set; }

        public OutsideScratchWrites? WritesOutsideScratch { get; set; }

        public int? ApprovalTimeoutSeconds { get; set; }
    }
}

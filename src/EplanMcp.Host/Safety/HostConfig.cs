using System.Text.Json;
using System.Text.Json.Serialization;

namespace EplanMcp.Host.Safety;

/// <summary>What writes to projects outside every scratch root need.</summary>
internal enum OutsideScratchWrites
{
    /// <summary>Refused with POLICY_DENIED: the default during M3a (M3 design Q5).</summary>
    Deny,

    /// <summary>Allowed after the engineer approves them (ADR-0005).</summary>
    Approve,
}

/// <summary>
/// The safety settings of <c>config.json</c> (M3 design 4.3). Only the file changes them, never a tool (SEC-02).
/// </summary>
internal sealed record HostConfig(IReadOnlyList<string> ScratchRoots, OutsideScratchWrites WritesOutsideScratch)
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };

    public static HostConfig Defaults(HostDataLocations locations) => new([locations.DefaultScratch], OutsideScratchWrites.Deny);

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

        return (new HostConfig(roots, file?.WritesOutsideScratch ?? defaults.WritesOutsideScratch), null);
    }

    private sealed class ConfigFile
    {
        public List<string>? ScratchRoots { get; set; }

        public OutsideScratchWrites? WritesOutsideScratch { get; set; }
    }
}

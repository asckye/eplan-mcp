using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace EplanMcp.Host.Tools;

/// <summary>Immutable process-owned catalog snapshot; its digest invalidates cursors after any data change.</summary>
internal sealed class ApiIndex(ApiIndexData data, string fingerprint)
{
    internal ApiIndexData Data { get; } = data;
    internal string Fingerprint { get; } = fingerprint;

    internal static ApiIndex Load()
    {
        using var reader = new StreamReader(typeof(ApiIndex).Assembly.GetManifestResourceStream("EplanMcp.ApiIndex")!);
        byte[] bytes = Convert.FromBase64String(reader.ReadToEnd());
        using var buffer = new MemoryStream(bytes);
        using var gzip = new GZipStream(buffer, CompressionMode.Decompress);
        return new(JsonSerializer.Deserialize<ApiIndexData>(gzip, HostJson.Options)!, Convert.ToHexStringLower(SHA256.HashData(bytes)));
    }

    internal ApiEntry[] Search(string? ns, string? type, string? member, string? summary, string? kind, string? status) =>
        [.. Data.Entries.Where(e => (ns is null || e.Namespace.Equals(ns, StringComparison.OrdinalIgnoreCase)
            || e.Namespace.StartsWith(ns + ".", StringComparison.OrdinalIgnoreCase))
            && (type is null || e.Type.Equals(type.StartsWith("T:", StringComparison.Ordinal) ? type : "T:" + type, StringComparison.OrdinalIgnoreCase))
            && (member is null || e.Name.Contains(member, StringComparison.OrdinalIgnoreCase))
            && (summary is null || e.Details.Any(d => d.Summary?.Contains(summary, StringComparison.OrdinalIgnoreCase) == true))
            && (kind is null || e.Row.Kind == kind) && MatchesStatus(e.Row.Status, status))];

    internal static bool MatchesStatus(string status, string? filter) => filter switch
    {
        "implemented" => status is "implemented" or "automated_test" or "live_verified",
        "verified" => status == "live_verified",
        "not_implemented" => status is "registered" or "env_blocked",
        "excluded" => status == "excluded",
        _ => filter is null,
    };
}

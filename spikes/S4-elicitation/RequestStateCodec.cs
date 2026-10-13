using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace EplanMcp.Spikes.S4;

/// <summary>
/// The requestState shape ADR-0005 / SEC-06 plan for the Host, reduced to what the spike needs to observe:
/// <c>base64url(payload).base64url(HMAC-SHA256(payload))</c>, where the payload binds the originating request
/// (tool + arguments digest), the completed round and an expiry. The key lives only in this process, which is
/// enough because a stdio server lives exactly as long as its client connection.
/// </summary>
internal static class RequestStateCodec
{
    private static readonly byte[] _key = RandomNumberGenerator.GetBytes(32);

    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    public static string Issue(string requestDigest, int round)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var payload = new JsonObject
        {
            ["d"] = requestDigest,
            ["r"] = round,
            ["iat"] = now,
            ["exp"] = now + (long)Lifetime.TotalSeconds,
            ["n"] = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)),
        };
        byte[] body = Encoding.UTF8.GetBytes(payload.ToJsonString());
        return Base64Url.EncodeToString(body) + "." + Base64Url.EncodeToString(HMACSHA256.HashData(_key, body));
    }

    /// <summary>Returns a status (valid, missing, malformed, badSignature, expired, requestMismatch) and the payload if readable.</summary>
    public static (string Status, JsonObject? Payload) Verify(string? state, string requestDigest)
    {
        if (string.IsNullOrEmpty(state))
        {
            return ("missing", null);
        }

        string[] parts = state.Split('.');
        byte[] body;
        byte[] mac;
        try
        {
            if (parts.Length != 2)
            {
                return ("malformed", null);
            }

            body = Base64Url.DecodeFromChars(parts[0]);
            mac = Base64Url.DecodeFromChars(parts[1]);
        }
        catch (FormatException)
        {
            return ("malformed", null);
        }

        if (!CryptographicOperations.FixedTimeEquals(mac, HMACSHA256.HashData(_key, body)))
        {
            return ("badSignature", null);
        }

        var payload = (JsonObject)JsonNode.Parse(body)!;
        if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > payload["exp"]!.GetValue<long>())
        {
            return ("expired", payload);
        }

        return string.Equals(payload["d"]!.GetValue<string>(), requestDigest, StringComparison.Ordinal)
            ? ("valid", payload)
            : ("requestMismatch", payload);
    }

    public static string Digest(string tool, IDictionary<string, System.Text.Json.JsonElement>? arguments)
    {
        var canonical = new StringBuilder(tool);
        foreach (KeyValuePair<string, System.Text.Json.JsonElement> argument in (arguments ?? new Dictionary<string, System.Text.Json.JsonElement>()).OrderBy(a => a.Key, StringComparer.Ordinal))
        {
            canonical.Append('\n').Append(argument.Key).Append('=').Append(argument.Value.GetRawText());
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())))[..16].ToLower(CultureInfo.InvariantCulture);
    }
}

using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace EplanMcp.Host.Tools;

/// <summary>Immutable per-Host signing key; cursors expire after ten minutes and never retain EPLAN objects.</summary>
internal sealed class PropertySchemaPaging(TimeProvider clock)
{
    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);

    internal string Encode(int offset, string query)
    {
        string payload = string.Create(CultureInfo.InvariantCulture, $"{offset}:{clock.GetUtcNow().AddMinutes(10).ToUnixTimeSeconds()}:{query}");
        byte[] bytes = Encoding.UTF8.GetBytes(payload);
        return Base64Url.EncodeToString(bytes) + "." + Base64Url.EncodeToString(HMACSHA256.HashData(_key, bytes));
    }

    internal int Offset(string? cursor, string query, List<string> violations)
    {
        if (cursor is null)
        {
            return 0;
        }

        try
        {
            string[] parts = (cursor.Length <= 1024 ? cursor : string.Empty).Split('.');
            if (parts.Length == 2)
            {
                byte[] bytes = Base64Url.DecodeFromChars(parts[0]);
                string[] fields = Encoding.UTF8.GetString(bytes).Split(':');
                if (CryptographicOperations.FixedTimeEquals(HMACSHA256.HashData(_key, bytes), Base64Url.DecodeFromChars(parts[1]))
                    && fields.Length == 3 && fields[2] == query
                    && int.TryParse(fields[0], NumberStyles.None, CultureInfo.InvariantCulture, out int offset) && offset > 0
                    && long.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out long expiry)
                    && expiry > clock.GetUtcNow().ToUnixTimeSeconds())
                {
                    return offset;
                }
            }
        }
        catch (FormatException)
        {
            // Malformed base64 is an invalid argument, not a server failure.
        }

        violations.Add("cursor is invalid, expired or belongs to another query/Host session; omit it to restart.");
        return 0;
    }
}

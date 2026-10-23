using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace EplanMcp.Host.Http;

/// <summary>Every path passes this gate. The lock owns the bounded failure table and each source's sliding window.</summary>
internal sealed partial class HttpGate(IEnumerable<string> origins, TimeProvider clock, ILogger<HttpGate> logger)
{
    private readonly HashSet<string> _origins = new(origins, StringComparer.Ordinal);
    private readonly Dictionary<IPAddress, Queue<DateTimeOffset>> _failures = [];
    private readonly Lock _lock = new();

    public static bool KeyMatches(StringValues authorization, string? key)
    {
        byte[] presented = SHA256.HashData(Encoding.UTF8.GetBytes(authorization.Count == 1 ? authorization[0] ?? string.Empty : string.Empty));
        byte[] expected = SHA256.HashData(Encoding.UTF8.GetBytes("Bearer " + key));
        return CryptographicOperations.FixedTimeEquals(presented, expected) && authorization.Count == 1 && key is not null;
    }

    public bool OriginAllowed(StringValues origin) => origin.Count == 0 || (origin.Count == 1 && _origins.Contains(origin[0] ?? string.Empty));

    public int Check(IPAddress source, StringValues origin, StringValues authorization, string? key)
    {
        lock (_lock)
        {
            DateTimeOffset now = clock.GetUtcNow();
            foreach (IPAddress expired in _failures.Where(p => now - p.Value.Last() >= TimeSpan.FromMinutes(1)).Select(p => p.Key).ToArray())
            {
                _failures.Remove(expired);
            }

            _failures.TryGetValue(source, out Queue<DateTimeOffset>? failures);
            if (failures?.Count == 10)
            {
                return 429;
            }

            if (!OriginAllowed(origin))
            {
                return 403;
            }

            if (KeyMatches(authorization, key))
            {
                return 0;
            }

            if (failures is null)
            {
                if (_failures.Count >= 4096)
                {
                    return 429;
                }

                _failures[source] = failures = new Queue<DateTimeOffset>();
            }

            while (failures.Count > 0 && now - failures.Peek() >= TimeSpan.FromMinutes(1))
            {
                failures.Dequeue();
            }

            failures.Enqueue(now);
            if (failures.Count == 10)
            {
                LogLocked(logger, source.ToString());
            }

            return 401;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Source {Source} failed authentication 10 times within one minute; requests blocked for one minute.")]
    private static partial void LogLocked(ILogger logger, string source);
}

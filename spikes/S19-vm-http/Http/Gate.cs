using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace EplanMcp.Spikes.S19.Http;

/// <summary>
/// ADR-0018 items 4 and 5 as one middleware that runs first, for every request and every path:
/// <list type="number">
/// <item>a source address with <see cref="FailureLimit"/> failed keys within <see cref="Window"/> gets 429 for
/// <see cref="Lockout"/>, whatever it sends (logged once);</item>
/// <item>a request with an Origin header that is not on the allow-list gets 403 (empty list by default);</item>
/// <item>a request whose Authorization header is not exactly "Bearer &lt;key&gt;" gets 401 and counts as a failure.
/// The comparison hashes both sides with SHA-256 and compares the hashes with FixedTimeEquals, so neither the length
/// nor the content of the presented value changes the time taken.</item>
/// </list>
/// Every refusal has an empty body and no extra headers (no WWW-Authenticate, no reason). Only then does the request
/// reach routing, so no endpoint, not even an unknown path, answers without the key.
/// </summary>
internal sealed partial class Gate
{
    public const int FailureLimit = 10;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan Lockout = TimeSpan.FromMinutes(1);

    private readonly byte[] _expected;
    private readonly HashSet<string> _origins;
    private readonly TimeProvider _clock;
    private readonly ILogger _logger;
    private readonly RequestLog? _log;
    private readonly ConcurrentDictionary<IPAddress, Source> _sources = new();

    public Gate(string key, IEnumerable<string> origins, TimeProvider clock, ILogger<Gate> logger, RequestLog? log)
    {
        _expected = SHA256.HashData(Encoding.UTF8.GetBytes("Bearer " + key));
        _origins = new HashSet<string>(origins, StringComparer.OrdinalIgnoreCase);
        _clock = clock;
        _logger = logger;
        _log = log;
    }

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        IPAddress source = context.Connection.RemoteIpAddress ?? IPAddress.None;
        DateTimeOffset now = _clock.GetUtcNow();
        bool keyMatches = KeyMatches(context.Request.Headers.Authorization);
        string decision =
            IsLockedOut(source, now) ? "429" :
            !OriginAllowed(context.Request.Headers.Origin) ? "403" :
            !keyMatches ? "401" :
            "pass";

        string? rpc = decision == "pass" ? await RequestLog.PeekRpcAsync(context.Request) : null;
        if (decision == "401")
        {
            CountFailure(source, now);
        }

        if (decision != "pass")
        {
            context.Response.StatusCode = int.Parse(decision, System.Globalization.CultureInfo.InvariantCulture);
            _log?.Write(context, keyMatches, decision, context.Response.StatusCode, rpc);
            return;
        }

        await next(context);
        _log?.Write(context, keyMatches, decision, context.Response.StatusCode, rpc);
    }

    private bool KeyMatches(StringValues authorization)
    {
        // A missing or repeated header fails too, after the same hash and comparison as a wrong key.
        string presented = authorization.Count == 1 ? authorization[0] ?? string.Empty : string.Empty;
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(presented));
        return CryptographicOperations.FixedTimeEquals(hash, _expected) && authorization.Count == 1;
    }

    private bool OriginAllowed(StringValues origin) =>
        origin.Count == 0 || (origin.Count == 1 && origin[0] is { } value && _origins.Contains(value));

    private bool IsLockedOut(IPAddress source, DateTimeOffset now) =>
        _sources.TryGetValue(source, out Source? state) && state.LockedUntil(now);

    private void CountFailure(IPAddress source, DateTimeOffset now)
    {
        Source state = _sources.GetOrAdd(source, _ => new Source());
        if (state.Fail(now, Window, FailureLimit, Lockout))
        {
            LogLockedOut(_logger, source.ToString(), FailureLimit, (int)Window.TotalSeconds, (int)Lockout.TotalSeconds);
        }
    }

    /// <summary>Failures of one source address in the sliding window, and the end of its lockout. Guarded by its lock.</summary>
    private sealed class Source
    {
        private readonly Lock _lock = new();
        private readonly Queue<DateTimeOffset> _failures = new();
        private DateTimeOffset _lockedUntil = DateTimeOffset.MinValue;

        public bool LockedUntil(DateTimeOffset now)
        {
            lock (_lock)
            {
                return now < _lockedUntil;
            }
        }

        /// <summary>Records one failure; true when it starts a lockout.</summary>
        public bool Fail(DateTimeOffset now, TimeSpan window, int limit, TimeSpan lockout)
        {
            lock (_lock)
            {
                while (_failures.Count > 0 && now - _failures.Peek() >= window)
                {
                    _failures.Dequeue();
                }

                _failures.Enqueue(now);
                if (_failures.Count < limit)
                {
                    return false;
                }

                _failures.Clear();
                _lockedUntil = now + lockout;
                return true;
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Source {Source} failed the key {Limit} times within {Window} s; its requests get 429 for {Lockout} s.")]
    private static partial void LogLockedOut(ILogger logger, string source, int limit, int window, int lockout);
}

using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using EplanMcp.Contracts;

namespace EplanMcp.Host.Safety;

/// <summary>
/// A plan waiting for approval: what one write call would do (M3 design 4.3). Only the Host keeps it; the client gets
/// a signed reference (<see cref="PendingPlanStore.Add"/>).
/// </summary>
internal sealed record PendingPlan(string Id, string Tool, string Project, string ArgumentsHash, WritePlan Plan, DateTimeOffset Expires);

/// <summary>Why a <c>requestState</c> was not accepted.</summary>
internal enum PlanRejection
{
    /// <summary>Not a state this Host signed: forged, damaged, or from another Host process.</summary>
    Invalid,

    /// <summary>Expired, already used, or forgotten (the Host restarted): call again for a new plan.</summary>
    Expired,

    /// <summary>Signed for another tool or other arguments.</summary>
    Mismatch,
}

/// <summary>
/// Plans waiting for approval, in memory, and the <c>requestState</c> that refers to them (ADR-0005 section 3,
/// SEC-06, threat T2): base64url(payload) + "." + base64url(HMAC-SHA256(payload)) with a key made when the Host
/// starts and never stored. The payload names the plan, the tool, the arguments' hash and the expiry; the plan itself
/// stays here, so a client cannot change what gets executed. Each plan is taken once. Thread-safe.
/// </summary>
internal sealed class PendingPlanStore(TimeProvider clock)
{
    /// <summary>As long as the longest approval deadline, so no plan expires while the engineer may still answer.</summary>
    public static readonly TimeSpan Lifetime = HostConfig.MaxApprovalTimeout;

    public const int Capacity = 100;

    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);
    private readonly ConcurrentDictionary<string, PendingPlan> _plans = new(StringComparer.Ordinal);

    /// <summary>Keeps the plan and returns the signed <c>requestState</c> for the approval round.</summary>
    public string Add(string tool, string project, string argumentsHash, WritePlan plan)
    {
        DateTimeOffset now = clock.GetUtcNow();
        Forget(now);
        var pending = new PendingPlan(Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16)), tool, project, argumentsHash, plan, now + Lifetime);
        _plans[pending.Id] = pending;
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(new Payload(pending.Id, tool, argumentsHash, pending.Expires.ToUnixTimeSeconds()));
        return Base64Url(payload) + "." + Base64Url(HMACSHA256.HashData(_key, payload));
    }

    /// <summary>
    /// The plan <paramref name="requestState"/> refers to, taken out of the store so it cannot be used twice, when it was
    /// signed here for this tool and these arguments and has not expired.
    /// </summary>
    public (PendingPlan? Plan, PlanRejection? Rejection) Take(string requestState, string tool, string argumentsHash)
    {
        if (Verified(requestState) is not { } payload)
        {
            return (null, PlanRejection.Invalid);
        }

        if (payload.Tool != tool || payload.Arguments != argumentsHash)
        {
            return (null, PlanRejection.Mismatch);
        }

        return _plans.TryRemove(payload.Id, out PendingPlan? plan) && plan.Expires > clock.GetUtcNow()
            ? (plan, null)
            : (null, PlanRejection.Expired);
    }

    /// <summary>The SHA-256 of the arguments as the tool serializes them, so the approval binds to exactly these.</summary>
    public static string HashArguments<TArgs>(TArgs args) =>
        Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(args, HostJson.Options)));

    private Payload? Verified(string requestState)
    {
        string[] parts = requestState.Split('.');
        if (parts.Length != 2 || FromBase64Url(parts[0]) is not { } payload || FromBase64Url(parts[1]) is not { } signature
            || !CryptographicOperations.FixedTimeEquals(signature, HMACSHA256.HashData(_key, payload)))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<Payload>(payload);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Drops expired plans, then the oldest ones beyond <see cref="Capacity"/>.</summary>
    private void Forget(DateTimeOffset now)
    {
        foreach (PendingPlan plan in _plans.Values.Where(p => p.Expires <= now))
        {
            _plans.TryRemove(plan.Id, out _);
        }

        foreach (PendingPlan plan in _plans.Values.OrderBy(p => p.Expires).Take(Math.Max(0, _plans.Count - Capacity + 1)))
        {
            _plans.TryRemove(plan.Id, out _);
        }
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[]? FromBase64Url(string text)
    {
        string padded = text.Replace('-', '+').Replace('_', '/');
        padded += new string('=', (4 - (padded.Length % 4)) % 4);
        try
        {
            return Convert.FromBase64String(padded);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private sealed record Payload(string Id, string Tool, string Arguments, long Expires);
}

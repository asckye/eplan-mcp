using System.Security.Cryptography;
using System.Text;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// The state fingerprint of a write plan (M3 design 4.2, SEC-07): the plan records it, the execute operation computes
/// it again and refuses with PLAN_STALE when it differs. Each write decides which lines describe the objects it
/// touches; only those count, so changes elsewhere in the project do not invalidate a plan.
/// </summary>
public static class Fingerprint
{
    /// <summary>
    /// SHA-256 of the lines sorted ordinally and joined with line feeds, as lowercase hex: the same lines give the same
    /// fingerprint in whatever order EPLAN returns the objects.
    /// </summary>
    public static string Of(IEnumerable<string> lines)
    {
        string text = string.Join("\n", lines.OrderBy(line => line, StringComparer.Ordinal));
        using var sha = SHA256.Create();
        byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
        return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
    }
}

using System.Text;
using System.Text.RegularExpressions;

namespace EplanMcp.Contracts.Security;

/// <summary>Shared, serializer-independent credential redaction for text leaving the application.</summary>
public static class SecretRedactor
{
    /// <summary>The fixed replacement for a credential or an uninspectable text value.</summary>
    public const string Placeholder = "[REDACTED]";

    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture;
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(1);
    private static readonly Regex _pem = new(
        @"-----BEGIN (?<label>[A-Z0-9 ]+)-----[\s\S]*?-----END \k<label>-----", Options, _timeout);
    private static readonly Regex _userInfo = new(
        @"(?<scheme>\b[a-z][a-z0-9+.-]*://)[^\s/?#]*@", Options, _timeout);
    private static readonly Regex _authorization = new(
        @"\b(?<scheme>Bearer)[ \t]+(?<token>[a-z0-9._~+/=-]+)|\b(?<scheme>Basic)[ \t]+(?<token>(?-i:(?:[A-Za-z0-9+/]{4})*(?:[A-Za-z0-9+/]{4}|[A-Za-z0-9+/]{3}=|[A-Za-z0-9+/]{2}==)))(?![a-z0-9+/=])", Options, _timeout);
    private static readonly Regex _assignment = new(
        @"(?<![\w.-])(?<name>[a-z_][a-z0-9_.-]*)[ \t]*=[ \t]*", Options, _timeout);
    private static readonly Regex _value = new(
        "\\G(?:\"(?:\\\\.|[^\"\\\\])*\"|'(?:\\\\.|[^'\\\\])*'|[^\\s;&,}\"']+)",
        Options, _timeout);

    /// <summary>Whether a field name denotes a credential under SEC-20; comparison is ordinal and case-insensitive.</summary>
    public static bool IsSensitiveKey(string key)
    {
        if (key is null)
        {
            throw new ArgumentNullException(nameof(key));
        }

        // SEC-20: field-name rules are shared by structured output and name=value text; no implicit allowlist.
        return key.IndexOf("password", StringComparison.OrdinalIgnoreCase) >= 0
            || key.IndexOf("passwd", StringComparison.OrdinalIgnoreCase) >= 0
            || key.IndexOf("secret", StringComparison.OrdinalIgnoreCase) >= 0
            || key.IndexOf("token", StringComparison.OrdinalIgnoreCase) >= 0
            || key.IndexOf("credential", StringComparison.OrdinalIgnoreCase) >= 0
            || key.IndexOf("authorization", StringComparison.OrdinalIgnoreCase) >= 0
            || key.EndsWith("key", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Replaces SEC-20 credentials without changing ordinary text or retaining caller data.</summary>
    public static string Redact(string value, string? key = null)
    {
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }
        if (key is not null && IsSensitiveKey(key))
        {
            return Placeholder;
        }

        try
        {
            // SEC-20: remove complete PEM blocks before inspecting the remaining free text.
            string result = _pem.Replace(value, Placeholder);
            result = _userInfo.Replace(result, match => match.Groups["scheme"].Value + Placeholder + "@");
            result = _authorization.Replace(result, RedactAuthorization);
            return RedactAssignments(result);
        }
        catch (RegexMatchTimeoutException)
        {
            /* swallow(privacy): SEC-20 forbids exposing source text when its inspection times out. */
            return Placeholder;
        }
    }

    private static string RedactAssignments(string text)
    {
        var result = new StringBuilder(text.Length);
        int consumedUntil = 0;
        // SEC-20: scan prefixes independently so ordinary wrappers cannot hide a nested credential assignment.
        foreach (Match assignment in _assignment.Matches(text))
        {
            if (assignment.Index < consumedUntil || !IsSensitiveKey(assignment.Groups["name"].Value))
            {
                continue;
            }
            int valueStart = assignment.Index + assignment.Length;
            Match value = _value.Match(text, valueStart);
            if (!value.Success)
            {
                continue;
            }
            result.Append(text, consumedUntil, valueStart - consumedUntil);
            result.Append(Placeholder);
            consumedUntil = value.Index + value.Length;
        }
        return result.Append(text, consumedUntil, text.Length - consumedUntil).ToString();
    }

    private static string RedactAuthorization(Match match)
    {
        if (string.Equals(match.Groups["scheme"].Value, "Basic", StringComparison.OrdinalIgnoreCase))
        {
            // SEC-20: a Basic credential encodes user:password; ordinary phrases such as "Basic text" do not.
            byte[] credential = Convert.FromBase64String(match.Groups["token"].Value);
            if (Array.IndexOf(credential, (byte)':') < 0)
            {
                return match.Value;
            }
        }
        return match.Groups["scheme"].Value + " " + Placeholder;
    }
}

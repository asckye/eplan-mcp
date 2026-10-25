using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace EplanMcp.Host.Tools;

/// <summary>
/// Keeps tool results within the token budget and project text inert (mcp-tool-design 7.2 and 7.3, M2 design 4.4).
/// Every string read from a project goes through <see cref="CleanText"/>, every list page through
/// <see cref="FitPage"/>. Only the Host shapes results; the Bridge returns project data as EPLAN gives it.
/// </summary>
internal static class ResultShaper
{
    /// <summary>Longest project text returned whole; longer text is cut to this many characters.</summary>
    public const int MaxTextLength = 500;

    /// <summary>Token budget of a result, unless the caller asked for detail.</summary>
    public const int DefaultTokenBudget = 5_000;

    /// <summary>Hard cap, for <c>response_format: "detailed"</c>: below Claude Code's 25k limit, with room for the rest.</summary>
    public const int MaxTokenBudget = 20_000;

    // C0, DEL and C1, except the two characters that lay out multi-line project texts, plus the bidirectional
    // embedding, override and isolate controls, which make text read differently from what it contains when it is
    // shown to an engineer (approval forms, audit). Other format characters such as ZWJ are needed by real text.
    private static readonly SearchValues<char> _controls = SearchValues.Create(
    [
        .. Enumerable.Range(0, 0xA0).Select(c => (char)c).Where(c => char.IsControl(c) && c is not '\n' and not '\t'),
        '‪', '‫', '‬', '‭', '‮', '⁦', '⁧', '⁨', '⁩',
    ]);

    /// <summary><see cref="CleanText"/> for each string of a list from project data.</summary>
    public static List<string> CleanTexts(IEnumerable<string> texts) => [.. texts.Select(text => CleanText(text))];

    /// <summary>
    /// Returns one string from project data in the form a tool may return it: control characters other than
    /// <c>\n</c> and <c>\t</c>, and bidirectional controls, removed (so <c>\r\n</c> becomes <c>\n</c>), and text longer than
    /// <see cref="MaxTextLength"/> cut to that length followed by <c> … [truncated, N characters in total]</c>, N being
    /// the length after the removal. Null stays null, because an unset value is not an empty one. Never throws.
    /// </summary>
    [return: NotNullIfNotNull(nameof(text))]
    public static string? CleanText(string? text)
    {
        if (text is null)
        {
            return null;
        }

        string clean = CleanFullText(text);
        if (clean.Length <= MaxTextLength)
        {
            return clean;
        }

        // Half a surrogate pair would reach the model as U+FFFD.
        int cut = char.IsHighSurrogate(clean[MaxTextLength - 1]) ? MaxTextLength - 1 : MaxTextLength;
        return string.Create(CultureInfo.InvariantCulture, $"{clean.AsSpan(0, cut)} … [truncated, {clean.Length} characters in total]");
    }

    /// <summary>
    /// Estimates the tokens of serialized JSON on the high side: four ASCII characters per token, one token for
    /// every other character (CJK text costs about one token per character).
    /// </summary>
    public static int EstimateTokens(string json)
    {
        int ascii = 0;
        foreach (char c in json)
        {
            if (char.IsAscii(c))
            {
                ascii++;
            }
        }

        return ((ascii + 3) / 4) + (json.Length - ascii);
    }

    /// <summary>
    /// Leaves a page that fits <paramref name="tokenBudget"/> as it is. Otherwise drops items from the end until the
    /// serialized result fits, sets <c>truncated</c>, points <c>nextCursor</c> at the first dropped item and says in
    /// <c>hint</c> how to go on. When not even one item fits, the page comes back empty rather than over the budget;
    /// the cursor still points at that item, so a call asking for less detail can continue there.
    /// </summary>
    /// <param name="result">The page, changed in place.</param>
    /// <param name="offset">Position of the page's first item in the whole listing.</param>
    /// <param name="fingerprint">The listing's <see cref="PageCursor.Fingerprint"/>.</param>
    /// <param name="tokenBudget"><see cref="DefaultTokenBudget"/>, or up to <see cref="MaxTokenBudget"/> for detailed results.</param>
    public static void FitPage<TItem>(IPagedResult<TItem> result, int offset, string fingerprint, int tokenBudget = DefaultTokenBudget)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(tokenBudget, MaxTokenBudget);
        if (Fits(result, tokenBudget))
        {
            return;
        }

        TItem[] page = [.. result.Items];
        result.Truncated = true;

        // The size grows with the item count, so a binary search finds the most items that fit (the whole page does not).
        int low = 0;
        int high = page.Length - 1;
        while (low < high)
        {
            int mid = (low + high + 1) / 2;
            Keep(mid);
            if (Fits(result, tokenBudget))
            {
                low = mid;
            }
            else
            {
                high = mid - 1;
            }
        }

        Keep(low);

        void Keep(int count)
        {
            result.Items.Clear();
            result.Items.AddRange(page.AsSpan(0, count));
            result.NextCursor = PageCursor.Encode(offset + count, fingerprint);
            result.Hint = count > 0
                ? string.Create(CultureInfo.InvariantCulture, $"Cut to {count} entries to stay within the size budget: pass 'nextCursor' as 'cursor' for the rest, or narrow the filters.")
                : "The next entry alone exceeds the size budget: ask for less detail or narrow the filters. 'nextCursor' still points at it.";
        }
    }

    /// <summary>
    /// The counterpart of <see cref="FitPage"/> for results that are one object with lists rather than a page (the
    /// <c>*_get</c> tools): leaves <paramref name="result"/> as it is when it fits <paramref name="tokenBudget"/>,
    /// otherwise drops entries from the end of <paramref name="items"/>, one of its lists, until it fits or the list is
    /// empty. Returns how many entries were dropped. A caller with several lists calls this list by list, least
    /// important first; it either sets <c>truncated</c> and <c>hint</c> before calling, so they are measured too, or
    /// keeps room for them in the budget.
    /// </summary>
    public static int FitItems<TItem>(object result, List<TItem>? items, int tokenBudget = DefaultTokenBudget)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(tokenBudget, MaxTokenBudget);
        if (items is null || items.Count == 0 || Fits(result, tokenBudget))
        {
            return 0;
        }

        // As in FitPage: the size grows with the count, and the whole list does not fit.
        TItem[] all = [.. items];
        int low = 0;
        int high = all.Length - 1;
        while (low < high)
        {
            int mid = (low + high + 1) / 2;
            Keep(mid);
            if (Fits(result, tokenBudget))
            {
                low = mid;
            }
            else
            {
                high = mid - 1;
            }
        }

        Keep(low);
        return all.Length - low;

        void Keep(int count)
        {
            items.Clear();
            items.AddRange(all.AsSpan(0, count));
        }
    }

    /// <summary>Whether the serialized result is within <paramref name="tokenBudget"/>, counted as <see cref="ResultTokens"/>.</summary>
    public static bool Fits(object result, int tokenBudget) =>
        ResultTokens(JsonSerializer.Serialize(result, result.GetType(), HostJson.Options)) <= tokenBudget;

    /// <summary>
    /// A tool result carries its JSON twice, as structured content and as a text block for older clients
    /// (<see cref="ToolResults.Success"/>), and the budget, like Claude Code's limit, is for the whole result.
    /// </summary>
    internal static int ResultTokens(string json) => 2 * EstimateTokens(json);

    internal static string CleanFullText(string text)
    {
        if (!text.AsSpan().ContainsAny(_controls))
        {
            return text;
        }
        var builder = new StringBuilder(text.Length);
        ReadOnlySpan<char> rest = text;
        int i;
        while ((i = rest.IndexOfAny(_controls)) >= 0)
        {
            builder.Append(rest[..i]);
            rest = rest[(i + 1)..];
        }

        return builder.Append(rest).ToString();
    }
}

namespace EplanMcp.Contracts;

/// <summary>Arguments of page.text.replace.plan and page.text.replace.execute; literal replacement in page free texts.</summary>
public sealed class PageTextReplaceArgs
{
    /// <summary>Explicit project name or path from eplan_project_list.</summary>
    public string? Project { get; set; }

    /// <summary>One to fifty distinct, exact full page names.</summary>
    public List<string>? Pages { get; set; } = [];

    /// <summary>Nonempty literal to find, at most 500 characters; whitespace is significant.</summary>
    public string? Find { get; set; }

    /// <summary>Literal replacement, at most 500 characters; empty removes matches, null is invalid.</summary>
    public string? Replace { get; set; }

    /// <summary>Explicit language code, ??_?? for independent text, or all for every stored language.</summary>
    public string? Language { get; set; }

    /// <summary>Use ordinal case-sensitive matching by default; false uses OrdinalIgnoreCase.</summary>
    public bool CaseSensitive { get; set; } = true;

    /// <summary>Maximum changed object-language entries, from 1 to 500; exceeding it refuses the whole plan.</summary>
    public int MaxChanges { get; set; } = 500;

    /// <summary>Required for execute: the fingerprint returned by the plan operation; null when planning.</summary>
    public string? Fingerprint { get; set; }
}

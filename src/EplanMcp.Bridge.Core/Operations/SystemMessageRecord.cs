namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// One EPLAN system message (<c>Base.BaseException</c>) as an adapter copies it out of its collection: EPLAN's values
/// unchanged, no EPLAN object kept (coding standards 9.5, rule 15).
/// </summary>
public sealed class SystemMessageRecord
{
    /// <summary>
    /// <c>MessageLevel</c> as a number: Trace=0, Message=1, Warning=2, Assert=3, Error=4, FatalError=5 (reflection),
    /// and values outside the enum, such as 7 for EPLAN's "new version" note (S6).
    /// </summary>
    public int Level { get; set; }

    /// <summary><c>GetMessageIndex()</c>: the S number such as <c>S085016</c>; <c>S000000</c> means none.</summary>
    public string Index { get; set; } = string.Empty;

    /// <summary><c>GetMessageText()</c>: the text without the number.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary><c>NumberOfOccurrences</c>: identical consecutive messages EPLAN merged into this one.</summary>
    public int Occurrences { get; set; }
}

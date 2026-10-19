namespace EplanMcp.Contracts;

/// <summary>One EPLAN system message in <see cref="OperationNames.MessageList"/>. The text can contain project data.</summary>
public sealed class SystemMessage
{
    /// <summary>A note: EPLAN's <c>MessageLevel.Message</c>, and levels outside that enum (S6 saw 7).</summary>
    public const string Message = "message";

    /// <summary>EPLAN's <c>MessageLevel.Warning</c>, and <c>Assert</c>, which ranks between warning and error.</summary>
    public const string Warning = "warning";

    /// <summary>EPLAN's <c>MessageLevel.Error</c>.</summary>
    public const string Error = "error";

    /// <summary>EPLAN's <c>MessageLevel.FatalError</c>; always listed.</summary>
    public const string Fatal = "fatal";

    /// <summary><see cref="Message"/>, <see cref="Warning"/>, <see cref="Error"/> or <see cref="Fatal"/>.</summary>
    public string Level { get; set; } = Message;

    /// <summary>EPLAN's message number, for example <c>S085016</c>; null when EPLAN gives none (<c>S000000</c>).</summary>
    public string? Number { get; set; }

    /// <summary>The message text as EPLAN gives it, without the number.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>How many identical consecutive messages EPLAN merged into this one.</summary>
    public int Occurrences { get; set; }
}

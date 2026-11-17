namespace EplanMcp.Contracts;

/// <summary>One check message of a project in <see cref="OperationNames.CheckList"/>. Its text contains project data.</summary>
public sealed class CheckMessage
{
    /// <summary>EPLAN's <c>Classification.Error</c>.</summary>
    public const string Error = "error";

    /// <summary>EPLAN's <c>Classification.Warning</c>.</summary>
    public const string Warning = "warning";

    /// <summary>EPLAN's <c>Classification.Hint</c>.</summary>
    public const string Hint = "hint";

    /// <summary>EPLAN's <c>Classification.Unknown</c>, or a value outside that enum.</summary>
    public const string Unknown = "unknown";

    /// <summary>EPLAN's message number (<c>ProjectMessage.GetId()</c>), for example 4025.</summary>
    public int Number { get; set; }

    /// <summary><see cref="Error"/>, <see cref="Warning"/>, <see cref="Hint"/> or <see cref="Unknown"/>.</summary>
    public string Severity { get; set; } = Unknown;

    /// <summary>EPLAN's message region (<c>IMessage.Region</c> name), for example <c>Connections</c>.</summary>
    public string Region { get; set; } = string.Empty;

    /// <summary>The message text with its parameters filled in, as EPLAN gives it.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>Whether the message is marked as done in EPLAN.</summary>
    public bool Done { get; set; }

    /// <summary>The object the message is about; null when EPLAN names none.</summary>
    public CheckObject? Target { get; set; }
}

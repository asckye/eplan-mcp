namespace EplanMcp.Contracts;

/// <summary>
/// Arguments of <see cref="OperationNames.MessageList"/> (docs/tools/eplan_message_list.md). System messages belong
/// to the EPLAN session, not to a project, so unlike <see cref="ListArgs"/> there is no project.
/// </summary>
public sealed class MessageListArgs
{
    /// <summary>
    /// The lowest level listed, one of <see cref="SystemMessage.Message"/>, <see cref="SystemMessage.Warning"/> and
    /// <see cref="SystemMessage.Error"/>; fatal errors are always listed. Null means <see cref="SystemMessage.Warning"/>.
    /// </summary>
    public string? MinLevel { get; set; }

    /// <summary>Index of the first message to return, taken from the Host's cursor.</summary>
    public int Offset { get; set; }

    /// <summary>The most messages to return.</summary>
    public int Limit { get; set; }
}

namespace EplanMcp.Contracts;

/// <summary>Result data of <see cref="OperationNames.MessageList"/>.</summary>
public sealed class MessageListData
{
    /// <summary>The requested slice, newest message first.</summary>
    public List<SystemMessage> Items { get; set; } = [];

    /// <summary>How many messages are at the requested level or above, before slicing.</summary>
    public int Total { get; set; }
}

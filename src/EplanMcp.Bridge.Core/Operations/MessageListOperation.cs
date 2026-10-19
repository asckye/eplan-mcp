using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// <see cref="OperationNames.MessageList"/> (docs/tools/eplan_message_list.md). The adapter copies every message out
/// of one EPLAN collection in a single pass; level filtering, newest-first order and the slice happen here, on
/// plain records, so no EPLAN message object is touched twice (api-notes A11).
/// </summary>
public sealed class MessageListOperation(ISystemMessageReader reader) : Operation<MessageListArgs, MessageListData>
{
    /// <summary><c>GetMessageIndex()</c> of a message without a number.</summary>
    internal const string NoNumber = "S000000";

    public override string Name => OperationNames.MessageList;

    protected override MessageListData Execute(MessageListArgs args)
    {
        string minLevel = args.MinLevel ?? SystemMessage.Warning;
        if (minLevel is not (SystemMessage.Message or SystemMessage.Warning or SystemMessage.Error))
        {
            const string violation = "'min_level' must be message, warning or error.";
            throw InvalidArgument(violation);
        }

        int minimum = Rank(minLevel);
        List<SystemMessage> matching =
        [
            .. reader.ReadAll()
                .Reverse() // EPLAN enumerates the oldest first; the tool lists the newest first
                .Select(record => (record, level: LevelName(record.Level)))
                .Where(m => m.level is not null && Rank(m.level) >= minimum)
                .Select(m => new SystemMessage
                {
                    Level = m.level!,
                    Number = m.record.Index.Length == 0 || m.record.Index == NoNumber ? null : m.record.Index,
                    Text = m.record.Text,
                    Occurrences = m.record.Occurrences,
                }),
        ];

        return new MessageListData { Items = Paging.Slice(matching, args.Offset, args.Limit), Total = matching.Count };
    }

    /// <summary>
    /// The tool's level for EPLAN's <c>MessageLevel</c>; null for Trace, which the tool never lists. Assert ranks between
    /// Warning and Error and counts as a warning; levels outside the enum (S6 saw 7) count as notes (tool spec 3).
    /// </summary>
    internal static string? LevelName(int eplanLevel) => eplanLevel switch
    {
        0 => null,
        2 or 3 => SystemMessage.Warning,
        4 => SystemMessage.Error,
        5 => SystemMessage.Fatal,
        _ => SystemMessage.Message,
    };

    private static int Rank(string level) => level switch
    {
        SystemMessage.Message => 1,
        SystemMessage.Warning => 2,
        SystemMessage.Error => 3,
        _ => 4, // fatal: above every min_level, so always listed
    };
}

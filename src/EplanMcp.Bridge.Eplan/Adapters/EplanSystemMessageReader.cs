using Eplan.EplApi.Base;
using EplanMcp.Bridge.Core.Operations;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>
/// Reads EPLAN's system messages. Main thread, inside the dispatcher's LockingStep. Only one
/// <see cref="SysMessagesCollection"/> may be in use at a time: reading a message text of one collection after another
/// was created is a native access violation that managed code cannot catch (api-notes A11, coding standards 9.5, rule
/// 15). So each call creates exactly one collection, copies every message in one forward pass (the way S6 read all 490
/// without a fault) and keeps no <see cref="BaseException"/>; nothing else in the add-in creates such a collection.
/// </summary>
internal sealed class EplanSystemMessageReader : ISystemMessageReader
{
    public IReadOnlyList<SystemMessageRecord> ReadAll()
    {
        MainThread.Assert();
        var records = new List<SystemMessageRecord>();

        // The parameterless collection holds the whole session's messages, Trace included (S6); the bookmark and level
        // constructors are not used because whether their level is a minimum or exact is unverified. The collection is
        // not IDisposable; its enumerator is, and foreach disposes it. Messages are EPLAN's and are not disposed here.
        var messages = new SysMessagesCollection();
        foreach (object item in messages)
        {
            if (item is BaseException message)
            {
                records.Add(new SystemMessageRecord
                {
                    Level = (int)message.MessageLevel,
                    Index = message.GetMessageIndex() ?? string.Empty,
                    Text = message.GetMessageText() ?? string.Empty,
                    Occurrences = message.NumberOfOccurrences,
                });
            }
        }

        return records;
    }
}

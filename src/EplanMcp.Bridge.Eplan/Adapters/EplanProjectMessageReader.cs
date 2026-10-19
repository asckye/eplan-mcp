using Eplan.EplApi.DataModel;
using Eplan.EplApi.EServices;
using EplanMcp.Bridge.Core.Operations;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>
/// Reads the check messages stored in a project (EPLAN's message management: the last check's results). Main thread,
/// inside the dispatcher's LockingStep; the project is found by path (api-notes A11). Only reads: the members of
/// <see cref="PrjMessagesCollection"/> and <see cref="ProjectMessage"/> that write are in BannedSymbols.txt, and a
/// check is never run. S6 read the messages this way in an action without changing the project's design data.
/// </summary>
internal sealed class EplanProjectMessageReader : IProjectMessageReader
{
    public IReadOnlyList<ProjectMessageRecord> ReadMessages(string projectPath)
    {
        MainThread.Assert();
        Project project = EplanProjects.ByPath(projectPath);
        var records = new List<ProjectMessageRecord>();

        // The collection is not IDisposable; its enumerator is, and foreach disposes it. The messages are EPLAN's.
        var messages = new PrjMessagesCollection(project);
        foreach (object item in messages)
        {
            // S6 found only ProjectMessage elements; a BaseProjectMessage is not stored in the project (EPLAN XML docs).
            if (item is ProjectMessage message)
            {
                StorableObject? target = message.GetObject1();
                records.Add(new ProjectMessageRecord
                {
                    Id = message.GetId(),
                    Category = message.GetCategory().ToString(),
                    Region = message.GetGroup().ToString(),
                    Text = message.GetText() ?? string.Empty,
                    Done = message.GetDone(),
                    ObjectKind = target?.GetType().Name,
                    ObjectName = target switch
                    {
                        FunctionBase function => function.Name,
                        Page page => page.Name,
                        _ => null,
                    },
                });
            }
        }

        return records;
    }
}

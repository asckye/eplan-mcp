using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>One main-thread invocation's borrowed functions and owned lists (A36/A42/A44/A48).</summary>
internal sealed class EplanPropertySetSession(string projectPath) : IPropertySetSession
{
    private readonly Dictionary<(int, long), FunctionPropertyList> _lists = [];
    private Function[] _functions = [];

    public IReadOnlyList<SnapshotObject> Functions
    {
        get
        {
            MainThread.Assert();
            using var finder = new DMObjectsFinder(EplanProjects.ByPath(projectPath));
            _functions = EplanPropertyReadAdapter.Functions(finder, Check).GroupBy(Key).Select(g => g.First()).ToArray();
            return _functions.Select(f =>
            {
                Check();
                SnapshotObject obj = EplanPropertyReadAdapter.Describe(f);
                obj.Parts = f.IsMainFunction ? [.. f.ArticleReferences.Select(r => r.PartNr ?? string.Empty)] : null;
                return obj;
            }).ToArray();
        }
    }

    public IReadOnlyList<string> Languages => LanguageCodes.Split(new EplanProjectLanguages().Read(projectPath).Translation);

    public IPropertyReadScope Open(string resolvedProject, PropertyTarget target)
    {
        Check();
        Function function = PropertyReadTargets.Located(resolvedProject, target.Locator!, _functions, EplanPropertyReadAdapter.Describe);
        if (!_lists.TryGetValue(Key(function), out FunctionPropertyList? list))
        {
            list = function.Properties;
            _lists.Add(Key(function), list);
        }

        return new EplanPropertyReadAdapter.Scope(list, function.GetType().Name, "", Check, ownsList: false);
    }

    public void Write(PropertySetGuard guard)
    {
        Check();
        FunctionPropertyList list = _lists[(guard.Target.Type, guard.Target.Oid)];
        using AnyPropertyId id = guard.PropertyId;
        if (guard.PropertyId == 20027)
        {
            using PropertyValue value = PropertyValueComparison.TextIn(guard.After, TextLanguages.Independent);
            list[id] = value;
            return;
        }

        using var text = new MultiLangString();
        foreach (SnapshotText entry in guard.After)
        {
            if (entry.Language == TextLanguages.Independent)
            {
                text.AddString(ISOCode.Language.L___, entry.Text);
                continue;
            }

            using var iso = new ISOCode(entry.Language);
            text.AddString(iso.GetNumber(), entry.Text);
        }

        using PropertyValue translated = text;
        list[id] = translated;
    }

    public void Dispose()
    {
        var failures = new List<Exception>();
        try
        {
            foreach (FunctionPropertyList list in _lists.Values)
            {
                try
                {
                    list.Dispose();
                }
                catch (Exception ex)
                {
                    failures.Add(ex);
                }
            }
        }
        finally
        {
            _lists.Clear();
        }

        if (failures.Count > 0)
        {
            throw new AggregateException("Property list release failed.", failures);
        }
    }

    private static (int, long) Key(Function f) => (SnapshotCanonical.TypeOf(f.ToStringIdentifier()), f.ObjectIdentifier);
    private static void Check()
    {
        MainThread.Assert();
        OperationContext.Current.ThrowIfCancellationRequested();
    }
}

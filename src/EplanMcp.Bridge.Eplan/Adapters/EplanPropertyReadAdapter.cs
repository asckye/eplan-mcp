using System.Diagnostics;
using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>One main-thread list per target per invoke; borrowed objects are never disposed (A36/A39).</summary>
internal sealed class EplanPropertyReadAdapter : IPropertyReadAdapter
{
    public IPropertyReadScope Open(string projectPath, PropertyTarget target)
    {
        MainThread.Assert();
        var watch = Stopwatch.StartNew();
        void Check()
        {
            OperationContext.Current.ThrowIfCancellationRequested();
            if (watch.Elapsed >= TimeSpan.FromSeconds(20))
            {
                throw new BridgeErrorException(ErrorReason.Timeout, "Property reading exceeded 20 seconds. Reduce the selected batch.");
            }
        }

        Check();
        string language;
        using (var languages = new Languages())
        using (ISOCode gui = languages.GuiLanguage)
        {
            Check();
            language = gui.GetString();
        }

        Check();
        Project project = EplanProjects.ByPath(projectPath);
        Check();
        if (target.ProjectObject == true)
        {
            return new Scope(project.Properties, "Project", language, Check);
        }

        using var objects = new DMObjectsFinder(project);
        if (target.DeviceTag is not null)
        {
            Function main = PropertyReadTargets.Main(projectPath, target, Functions(objects, Check), Describe);
            Check();
            return new Scope(main.Properties, main.GetType().Name, language, Check);
        }

        if (target.Locator is { } locator)
        {
            StorableObject found = PropertyReadTargets.Located(projectPath, locator,
                EplanObjectReader.Candidates(objects, locator.Kind, Check), obj => EplanObjectReader.Describe(obj, locator.Kind));
            Check();
            return new Scope(EplanObjectReader.Properties(found), ObjectReadTypes.DictionaryType(found.GetType().Name), language, Check);
        }

        Page page;
        using (var finder = new DMObjectsFinder(project))
        using (var filter = new PagesFilter { Name = target.Page, ExactNameMatching = true })
        {
            Check();
            page = finder.GetPages(filter).SingleOrDefault(p => string.Equals(p.Name, target.Page, StringComparison.Ordinal))
                ?? throw new BridgeErrorException(ErrorReason.PageNotFound, "The page is no longer in the project. Call eplan_page_list to see its pages.");
        }

        Check();
        return new Scope(page.Properties, "Page", language, Check);
    }

    // As EplanPlanFunctionReader: A12's unfiltered finder omits some representations; include placements and
    // page-less functions. Selection deduplicates by type/oid. These identity getters never acquire Properties.
    internal static IEnumerable<Function> Functions(DMObjectsFinder finder, Action check)
    {
        check();
        foreach (Function function in finder.GetFunctions(null))
        {
            check();
            yield return function;
        }

        foreach (Page page in Pages(finder, check))
        {
            check();
            foreach (Function function in page.AllPlacements.OfType<Function>())
            {
                check();
                yield return function;
            }
        }
    }

    private static IEnumerable<Page> Pages(DMObjectsFinder finder, Action check)
    {
        check();
        foreach (Page page in finder.GetPages(null))
        {
            check();
            yield return page;
        }
    }

    internal static SnapshotObject Describe(StorableObject obj)
    {
        var identity = new SnapshotObject
        {
            Type = SnapshotCanonical.TypeOf(obj.ToStringIdentifier()),
            Oid = obj.ObjectIdentifier,
        };
        switch (obj)
        {
            case Page page:
                identity.Kind = "page";
                identity.Name = page.Name;
                break;
            case Function function:
                identity.Kind = "function";
                identity.Name = function.Name;
                identity.Page = function.Page?.Name;
                identity.Main = function.IsMainFunction;
                identity.Category = function.FunctionCategory.ToString();
                PointD position = function.Location;
                identity.Pos = SnapshotValues.Position(position.X, position.Y);
                break;
            case Connection connection:
                identity.Kind = "connection";
                identity.Page = EplanConnectionReader.PageOf(connection)?.Name;
                // Connection3D's pin getters are unsupported (S6); never probe them.
                identity.Name = connection is global::Eplan.EplApi.DataModel.E3D.Connection3D ? null
                    : SnapshotCanonical.ConnectionName(End(connection.StartPin), End(connection.EndPin));
                break;
        }

        return identity;
    }

    private static SnapshotEnd? End(Pin? pin) => pin?.ParentFunction is not { } function ? null : new SnapshotEnd
    {
        Type = SnapshotCanonical.TypeOf(function.ToStringIdentifier()),
        Oid = function.ObjectIdentifier,
        Pin = pin.Name is { } name ? SnapshotValues.Text(name) : null,
    };

    internal sealed class Scope(UniversalPropertyList list, string objectType, string language, Action check,
        bool ownsList = true) : IPropertyReadScope
    {
        public string ObjectType => objectType;
        public string DefaultLanguage => language;

        public IPropertyReadHandle? OpenProperty(int propertyId)
        {
            check();
            AnyPropertyId id = propertyId;
            PropertyValue? parent = null;
            bool transferred = false;
            try
            {
                check();
                parent = list[id];
                var handle = new Handle(list, id, parent, check);
                transferred = true;
                return handle;
            }
            catch (PropertyNotFoundException)
            {
                return null;
            }
            finally
            {
                if (!transferred)
                {
                    try
                    {
                        parent?.Dispose();
                    }
                    finally
                    {
                        id.Dispose();
                    }
                }
            }
        }

        public void Dispose()
        {
            if (ownsList)
            {
                list.Dispose();
            }
        }
    }

    private sealed class Handle(UniversalPropertyList list, AnyPropertyId id, PropertyValue parent, Action check) : IPropertyReadHandle
    {
        public IPropertyReadDefinition OpenDefinition()
        {
            check();
            return new EplanPropertyReadDefinition(parent.Definition, check);
        }

        public IReadOnlyList<int> ReadIndexes(int offset, int count)
        {
            check();
            return parent.Indexes.OrderBy(i => i).Skip(offset).Take(Math.Min(count, 200)).ToArray();
        }

        public IPropertyReadValue? OpenValue(int? index)
        {
            check();
            try
            {
                return new EplanPropertyReadValue(index.HasValue ? list[id, index.Value] : list[id], check);
            }
            catch (PropertyNotFoundException)
            {
                return null;
            }
            catch (InvalidIndexException)
            {
                throw new BridgeErrorException(ErrorReason.InvalidArgument, "The property index is outside the runtime definition's range.");
            }
        }

        public void Dispose()
        {
            try
            {
                parent.Dispose();
            }
            finally
            {
                id.Dispose();
            }
        }
    }
}

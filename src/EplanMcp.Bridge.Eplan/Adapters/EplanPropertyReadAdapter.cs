using System.Diagnostics;
using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>One main-thread list per target per invoke; borrowed projects/pages are never disposed (A36/A39).</summary>
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
            return new Scope(project.Properties, language, Check);
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
        return new Scope(page.Properties, language, Check);
    }

    private sealed class Scope(UniversalPropertyList list, string language, Action check) : IPropertyReadScope
    {
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

        public void Dispose() => list.Dispose();
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

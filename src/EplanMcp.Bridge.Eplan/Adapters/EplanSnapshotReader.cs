using Eplan.EplApi.ApplicationFramework;
using Eplan.EplApi.Base;
using Eplan.EplApi.Base.Enums;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.EObjects;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>
/// Reads projects for project.snapshot the way spike S31 did in the dispatcher (api-notes A36): the functions placed on
/// each page (<c>Page.AllPlacements</c>), the connections by <c>Connection.Page</c>, and only the registered properties,
/// each through its typed accessor. Each object's property list is acquired once and disposed after all its scopes
/// are read (A36); each <c>PropertyValue</c> is disposed once read, because thousands left to the finalizer exhausted
/// EPLAN's memory in S31. Main thread, inside the dispatcher's LockingStep.
/// </summary>
internal sealed class EplanSnapshotReader : ISnapshotReader
{
    // The accessor of every registered property (SnapshotProfiles.Standard), by the property list it is read from.
    // Terminals, PLC boxes and cables are functions: their properties come from the function's list.
    private static readonly Dictionary<int, Func<PagePropertyList, PropertyValue>> _page = new()
    {
        [11011] = p => p.PAGE_NOMINATIOMN,
        [11016] = p => p.PAGE_FORMPLOT,
        [SnapshotCanonical.PageModifiedDate] = p => p.PAGE_LASTAUTOMODIFICATIONDATE,
        [SnapshotCanonical.PageModifiedTime] = p => p.PAGE_LASTAUTOMODIFICATIONTIME,
    };

    private static readonly Dictionary<int, Func<FunctionPropertyList, PropertyValue>> _function = new()
    {
        [20011] = p => p.FUNC_TEXT,
        [20027] = p => p.FUNC_TECHNICAL_CHARACTERISTIC,
        [20400] = p => p.FUNC_PLCADDRESS,
        [20405] = p => p.FUNC_PLCDATATYPE,
        [20403] = p => p.FUNC_PLCSYMBOLICADDRESS_CALCULATED,
        [20408] = p => p.FUNC_PLCSTATIONNAME,
        [20410] = p => p.FUNC_PLCRACK,
        [20411] = p => p.FUNC_PLCSOCKET,
        [20040] = p => p.FUNC_CABLETYPE,
        [20042] = p => p.FUNC_CABLEWIRECOUNT,
        [20043] = p => p.FUNC_CABLEWIRECROSSSECTION,
        [20257] = p => p.FUNC_CABLELENGTH_FULL,
    };

    private static readonly Dictionary<int, Func<ConnectionPropertyList, PropertyValue>> _connection = new()
    {
        [31011] = p => p.CONNECTION_DESIGNATION,
        [31002] = p => p.CONNECTION_WIRECROSSSECTION,
        [31004] = p => p.CONNECTION_WIRENUMBER,
    };

    public ISnapshotSource Open(string projectPath)
    {
        MainThread.Assert();
        return new Source(EplanProjects.ByPath(projectPath), null);
    }

    /// <summary>
    /// <c>OpenProject(link file, ReadOnly)</c> without taking locks, as the backup's page count opens its copy (A20:
    /// the current project stays, and the copy is not left open). The ProjectManager lives until the project is closed
    /// again: A34 found a project object unusable for <c>Close()</c> once its manager was disposed.
    /// </summary>
    public ISnapshotSource OpenReadOnly(string linkFile)
    {
        MainThread.Assert();
#pragma warning disable CA2000 // Handed to the source, which disposes it after closing the project; the finally disposes it otherwise.
        ProjectManager? manager = new();
#pragma warning restore CA2000
        Project? project = null;
        try
        {
            bool lockByDefault = manager.LockProjectByDefault;
            manager.LockProjectByDefault = false;
            try
            {
                using var quiet = new QuietModeStep(QuietModes.ShowNoDialogs);
                project = manager.OpenProject(linkFile, ProjectManager.OpenMode.ReadOnly);
            }
            finally
            {
                manager.LockProjectByDefault = lockByDefault;
            }

            var source = new Source(project, manager);
            manager = null; // the source owns it now
            return source;
        }
        finally
        {
            if (manager is not null)
            {
                try
                {
                    project?.Close(); // opened, but no source to close it
                }
                finally
                {
                    manager.Dispose();
                }
            }
        }
    }

    /// <summary>A page as the snapshot reads it, with its registered properties (hints included).</summary>
    internal static SnapshotSourceObject PageObject(Page page, IEnumerable<SnapshotProperty> registered)
    {
        using PagePropertyList properties = page.Properties;
        return new SnapshotSourceObject
        {
            Kind = "page",
            Identifier = page.ToStringIdentifier(),
            Oid = page.ObjectIdentifier,
            Name = page.Name,
            PageType = page.PageType.ToString(),
            Values = Values(properties, registered, "page", _page),
        };
    }

    /// <summary>A function placed on <paramref name="page"/> as the snapshot reads it.</summary>
    internal static SnapshotSourceObject FunctionObject(Function f, string? page, IEnumerable<SnapshotProperty> registered)
    {
        using FunctionPropertyList properties = f.Properties;
        List<SnapshotSourceValue> values = Values(properties, registered, "function", _function);
        // Kinds as the cable, PLC and terminal readers tell them (A24).
        string? scope = f is Cable && f.FunctionCategory == FunctionCategory.Cable ? "cable"
            : f is PLC && f.FunctionCategory == FunctionCategory.PLCBox ? "plc"
            : f is Terminal ? "terminal"
            : null;
        if (scope is not null)
        {
            values.AddRange(Values(properties, registered, scope, _function));
        }

        PointD location = f.Location;
        return new SnapshotSourceObject
        {
            Kind = "function",
            Identifier = f.ToStringIdentifier(),
            Oid = f.ObjectIdentifier,
            Name = f.Name,
            Page = page,
            Category = f.FunctionCategory.ToString(),
            Shown = string.IsNullOrEmpty(f.VisibleName) ? null : f.VisibleName,
            Main = f.IsMainFunction,
            X = location.X,
            Y = location.Y,
            Parts = f.IsMainFunction ? [.. f.ArticleReferences.Select(r => r.PartNr ?? string.Empty)] : null,
            Values = values,
        };
    }

    /// <summary>As connection.list reads one (EplanConnectionReader): StartPin and EndPin are null on an open side.</summary>
    internal static SnapshotSourceObject ConnectionObject(Connection c, string? page, IEnumerable<SnapshotProperty> registered)
    {
        using ConnectionPropertyList properties = c.Properties;
        return new SnapshotSourceObject
        {
            Kind = "connection",
            Identifier = c.ToStringIdentifier(),
            Oid = c.ObjectIdentifier,
            Page = page,
            Start = End(c.StartPin),
            End = End(c.EndPin),
            Values = Values(properties, registered, "connection", _connection),
        };
    }

    private static SnapshotSourceEnd? End(Pin? pin) => pin?.ParentFunction is not { } function
        ? null
        : new SnapshotSourceEnd { Identifier = function.ToStringIdentifier(), Oid = function.ObjectIdentifier, Function = function.Name, Pin = pin.Name };

    /// <summary>Reads one scope; the caller owns the list and disposes it after all scopes are read (api-notes A36).</summary>
    private static List<SnapshotSourceValue> Values<TList>(TList list, IEnumerable<SnapshotProperty> registered, string scope, Dictionary<int, Func<TList, PropertyValue>> accessors)
        where TList : UniversalPropertyList
    {
        var values = new List<SnapshotSourceValue>();
        foreach (SnapshotProperty property in registered.Where(p => p.Scope == scope))
        {
            try
            {
                Read(accessors[property.Id](list), property.Id, values);
            }
            catch (PropertyNotFoundException)
            {
                // Registered properties may be absent on objects in the scope, e.g. PLC properties on ordinary terminals (A36).
            }
        }

        return values;
    }

    /// <summary>
    /// The raw text of a value that is not empty (coding standards 13): <c>ToString()</c>, as S31 read 5 million values.
    /// An indexed property throws IndexedPropertyException on <c>IsEmpty</c> (S6), so its indexes are read one by one.
    /// </summary>
    private static void Read(PropertyValue value, int id, List<SnapshotSourceValue> values)
    {
        using (value)
        {
            try
            {
                if (!value.IsEmpty)
                {
                    values.Add(new SnapshotSourceValue(id, null, value.ToString()));
                }
            }
            catch (IndexedPropertyException)
            {
                foreach (int index in value.Indexes)
                {
                    using PropertyValue item = value[index];
                    if (!item.IsEmpty)
                    {
                        values.Add(new SnapshotSourceValue(id, index, item.ToString()));
                    }
                }
            }
        }
    }

    private static string? Raw(PropertyValue value)
    {
        using (value)
        {
            return value.IsEmpty ? null : value.ToString();
        }
    }

    private sealed class Source : ISnapshotSource
    {
        private readonly Project _project;
        private readonly ProjectManager? _owner;
        private readonly DMObjectsFinder _finder;
        private readonly Dictionary<long, Page> _pages;
        private Dictionary<long, List<Connection>>? _connections;

        public Source(Project project, ProjectManager? owner)
        {
            _project = project;
            _owner = owner;
            _finder = new DMObjectsFinder(project);
            _pages = _finder.GetPages(null).ToDictionary(p => p.ObjectIdentifier);
            Name = project.ProjectName;
            Path = project.ProjectFullName;
            using ProjectPropertyList properties = project.Properties;
            ProjectGuid = Raw(properties.PROJ_GUID);
            ModifiedDate = Raw(properties.PROJ_LASTMODIFICATIONDATE);
            ModifiedTime = Raw(properties.PROJ_LASTMODIFICATIONTIME);
        }

        public string Name { get; }

        public string Path { get; }

        public string? ProjectGuid { get; }

        public string? ModifiedDate { get; }

        public string? ModifiedTime { get; }

        public IReadOnlyList<KeyValuePair<long, string>> Pages => [.. _pages.Select(p => new KeyValuePair<long, string>(p.Key, p.Value.Name ?? string.Empty))];

        public int SkippedConnections { get; private set; }

        public IReadOnlyList<SnapshotSourceObject> ReadPage(long pageOid, string profile)
        {
            IEnumerable<SnapshotProperty> registered = SnapshotCanonical.Registered(profile)!.Values;
            Page page = _pages[pageOid];
            var objects = new List<SnapshotSourceObject> { PageObject(page, registered) };
            objects.AddRange(page.AllPlacements.OfType<Function>().Select(f => FunctionObject(f, page.Name, registered)));
            _connections ??= Connections();
            if (_connections.TryGetValue(pageOid, out List<Connection>? connections))
            {
                objects.AddRange(connections.Select(c => ConnectionObject(c, page.Name, registered)));
            }

            return objects;
        }

        public void Dispose()
        {
            _finder.Dispose();
            if (_owner is not null)
            {
                try
                {
                    _project.Close();
                }
                finally
                {
                    _owner.Dispose();
                }
            }
        }

        /// <summary>
        /// Every connection grouped by its page, once per source: a Connection3D and a connection without a page are
        /// left out and counted (EplanConnectionReader.PageOf).
        /// </summary>
        private Dictionary<long, List<Connection>> Connections()
        {
            var byPage = new Dictionary<long, List<Connection>>();
            foreach (Connection connection in _finder.GetConnections(null))
            {
                if (EplanConnectionReader.PageOf(connection) is not { } page)
                {
                    SkippedConnections++;
                    continue;
                }

                if (!byPage.TryGetValue(page.ObjectIdentifier, out List<Connection>? list))
                {
                    byPage[page.ObjectIdentifier] = list = [];
                }

                list.Add(connection);
            }

            return byPage;
        }
    }
}

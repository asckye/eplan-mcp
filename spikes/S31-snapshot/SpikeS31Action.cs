using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Threading;
using Eplan.EplApi.ApplicationFramework;
using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.Graphics;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Spikes.S31;

/// <summary>
/// Spike S31 (M6 design 4.6): a whole-project snapshot, on the main thread through the dispatcher. Per page: every
/// placement (type, ObjectIdentifier, position), every existing property of the page, its functions and connections
/// (<c>UniversalPropertyList.ExistingIds/ExistingValues</c>), connection ends and placeholders, as canonical text with a
/// SHA-256 per page. <c>extract</c> takes two snapshots in a row and compares them, and hashes the project's files
/// before and after; <c>reopen</c> closes and reopens a scratch project between the two; <c>marker</c> changes one page
/// description of a scratch project and reports which modification dates and page hashes changed.
/// <para>Usage: <c>EplanMcpSpikeS31 /Mode:extract|reopen|marker /Report:"…\x.json" /Project:"x.elk"</c>.</para>
/// </summary>
public sealed class SpikeS31Action : IEplAction
{
    private static readonly string _scratchRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "EplanMcp", "scratch") + @"\";

    public bool OnRegister(ref string Name, ref int Ordinal)
    {
        string assemblyName = typeof(SpikeS31Action).Assembly.GetName().Name ?? "EplanMcp.Spikes.S31";
        Name = assemblyName.Replace("EplanMcp.Spikes.", "EplanMcpSpike").Replace(".", string.Empty);
        Ordinal = 20;
        return true;
    }

    public void GetActionProperties(ref ActionProperties actionProperties)
    {
    }

    public bool Execute(ActionCallingContext oActionCallingContext)
    {
        var p = new Params
        {
            Report = Parameter(oActionCallingContext, "Report"),
            Mode = Parameter(oActionCallingContext, "Mode"),
            Project = Parameter(oActionCallingContext, "Project"),
            Light = Parameter(oActionCallingContext, "Part") == "light",
            Once = Parameter(oActionCallingContext, "Part") == "once",
        };
        Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
        var thread = new Thread(() => RunLater(dispatcher, p)) { IsBackground = true };
        thread.Start();
        return true;
    }

    private static void RunLater(Dispatcher dispatcher, Params p)
    {
        var report = new JObject { ["spike"] = "S31-snapshot", ["mode"] = p.Mode, ["timestamp"] = DateTime.Now.ToString("o", CultureInfo.InvariantCulture) };
        try
        {
            Thread.Sleep(300);
            object? done = dispatcher.Invoke(DispatcherPriority.Normal, TimeSpan.FromSeconds(900), new Func<bool>(() =>
            {
                try
                {
                    using (new LockingStep())
                    using (new QuietModeStep(QuietModes.ShowNoDialogs))
                    {
                        report[p.Mode] = p.Mode switch
                        {
                            "extract" => Extract(p.Project, p.Report, p.Light, p.Once),
                            "reopen" => Reopen(p.Project, p.Light),
                            "marker" => Marker(p.Project, p.Light),
                            _ => "unknown mode",
                        };
                    }
                }
                catch (Exception ex)
                {
                    report["fatal"] = Describe(ex);
                }

                return true;
            }));
            if (done is null)
            {
                report["fatal"] = "the dispatcher did not run the probes within 900 s";
            }
        }
        catch (Exception ex)
        {
            report["fatal"] = Describe(ex);
        }

        File.WriteAllText(p.Report + ".partial", report.ToString(Formatting.Indented));
        File.Delete(p.Report);
        File.Move(p.Report + ".partial", p.Report);
    }

    private static Project Open(string path)
    {
        using var manager = new ProjectManager();
        return manager.OpenProjects.FirstOrDefault(o => string.Equals(o.ProjectLinkFilePath, path, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("not open: " + path);
    }

    private static void InScratch(string path)
    {
        if (!Path.GetFullPath(path).StartsWith(_scratchRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("this probe works only under " + _scratchRoot);
        }
    }

    /// <summary>Two snapshots in a row, compared page by page; the project's files hashed before and after; the first snapshot written next to the report.</summary>
    private static JObject Extract(string path, string reportPath, bool light, bool once)
    {
        Project project = Open(path);
        Dictionary<string, string> filesBefore = Files(path);
        var memoryBefore = Process.GetCurrentProcess().WorkingSet64;
        Snapshot a = Take(project, light);
        var memoryAfter = Process.GetCurrentProcess().WorkingSet64;
        Snapshot b = once ? a : Take(project, light);
        Dictionary<string, string> filesAfter = Files(path);
        File.WriteAllText(reportPath + ".snapshot.txt", string.Join("\n", a.Text), new UTF8Encoding(false));
        return new JObject
        {
            ["first"] = a.Summary(),
            ["second"] = b.Summary(),
            ["workingSetMB"] = new JObject { ["before"] = memoryBefore >> 20, ["afterFirst"] = memoryAfter >> 20 },
            ["compare"] = Compare(a, b),
            ["changedFiles"] = new JArray(filesBefore.Keys.Union(filesAfter.Keys).Where(k => !filesBefore.TryGetValue(k, out string? x) || !filesAfter.TryGetValue(k, out string? y) || x != y).OrderBy(k => k, StringComparer.Ordinal)),
            ["positions"] = a.Positions(),
        };
    }

    private static JObject Reopen(string path, bool light)
    {
        InScratch(path);
        Project project = Open(path);
        Snapshot a = Take(project, light);
        project.Close();
        Project reopened;
        using (var manager = new ProjectManager())
        {
            reopened = manager.OpenProject(path);
        }

        Snapshot b = Take(reopened, light);
        return new JObject { ["first"] = a.Summary(), ["second"] = b.Summary(), ["compare"] = Compare(a, b) };
    }

    /// <summary>
    /// Reads the project's and every page's modification dates, changes one page description (committed), and reads them
    /// again with a new snapshot: which dates moved and which page hashes changed.
    /// </summary>
    private static JObject Marker(string path, bool light)
    {
        InScratch(path);
        Project project = Open(path);
        Snapshot a = Take(project, light);
        Dictionary<string, string> datesBefore = Dates(project);
        Page target;
        using (var finder = new DMObjectsFinder(project))
        {
            target = finder.GetPages(null).First(pg => pg.PageType == DocumentTypeManager.DocumentType.Circuit);
        }

        string targetName = target.Name;
        using (SafetyPoint safety = SafetyPoint.Create())
        {
            using var text = new MultiLangString();
            text.AddString(ISOCode.Language.L___, "S31 marker " + DateTime.UtcNow.ToString("HHmmss", CultureInfo.InvariantCulture));
            target.Properties.PAGE_NOMINATIOMN = text;
            safety.Commit();
        }

        Dictionary<string, string> datesAfter = Dates(project);
        Snapshot b = Take(project, light);
        return new JObject
        {
            ["changedPage"] = targetName,
            ["datesChanged"] = new JArray(datesBefore.Keys.Where(k => datesAfter.TryGetValue(k, out string? v) && v != datesBefore[k]).Select(k => k + ": " + datesBefore[k] + " -> " + datesAfter[k])),
            ["compare"] = Compare(a, b),
        };
    }

    /// <summary>Modification dates and times of the project and of each page (values only: they hold no names).</summary>
    private static Dictionary<string, string> Dates(Project project)
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["project.date"] = Safe(() => project.Properties.PROJ_LASTMODIFICATIONDATE.ToString()),
            ["project.time"] = Safe(() => project.Properties.PROJ_LASTMODIFICATIONTIME.ToString()),
        };
        using var finder = new DMObjectsFinder(project);
        foreach (Page page in finder.GetPages(null))
        {
            d["page " + page.Name + " manual date"] = Safe(() => page.Properties.PAGE_LASTMANUMODIFICATIONDATE.ToString());
            d["page " + page.Name + " auto date"] = Safe(() => page.Properties.PAGE_LASTAUTOMODIFICATIONDATE.ToString());
            d["page " + page.Name + " auto time"] = Safe(() => page.Properties.PAGE_LASTAUTOMODIFICATIONTIME.ToString());
        }

        return d;
    }

    /// <summary>
    /// The snapshot; <paramref name="light"/> (r2) keeps every property of the pages but only the head line of each
    /// placement, plus a function's full name, and a connection's ends without its properties.
    /// </summary>
    private static Snapshot Take(Project project, bool light)
    {
        var snapshot = new Snapshot();
        var total = Stopwatch.StartNew();
        using var finder = new DMObjectsFinder(project);
        var connections = new Dictionary<long, List<Connection>>();
        foreach (Connection connection in finder.GetConnections(null))
        {
            long key = connection.Page?.ObjectIdentifier ?? -1;
            if (!connections.TryGetValue(key, out List<Connection>? list))
            {
                connections[key] = list = [];
            }

            list.Add(connection);
        }

        foreach (Page page in finder.GetPages(null).OrderBy(pg => pg.ObjectIdentifier))
        {
            var clock = Stopwatch.StartNew();
            var lines = new List<string> { "PAGE|" + page.ObjectIdentifier.ToString(CultureInfo.InvariantCulture) + "|" + page.Name };
            lines.AddRange(Properties(page.Properties, snapshot));
            foreach (Placement placement in page.AllPlacements.OrderBy(pl => pl.GetType().Name, StringComparer.Ordinal).ThenBy(pl => pl.ObjectIdentifier))
            {
                snapshot.Placements++;
                string head = placement.GetType().Name + "|" + placement.ObjectIdentifier.ToString(CultureInfo.InvariantCulture);
                switch (placement)
                {
                    case Function f:
                        lines.Add(head + "|" + Point(f.Location, snapshot));
                        if (light)
                        {
                            lines.Add("  name=" + Text(() => f.Properties.FUNC_FULLNAME));
                        }
                        else
                        {
                            lines.AddRange(Properties(f.Properties, snapshot));
                        }

                        snapshot.Functions++;
                        break;
                    case PlaceHolder h:
                        lines.Add(head + "|" + Safe(() => h.Name) + "|" + string.Join(",", Enumerable.Range(0, Math.Max(0, h.NumberOfRecords)).Select(h.get_NameOfRecord)));
                        snapshot.Placeholders++;
                        break;
                    default:
                        lines.Add(head);
                        break;
                }
            }

            if (connections.TryGetValue(page.ObjectIdentifier, out List<Connection>? onPage))
            {
                foreach (Connection c in onPage.OrderBy(c => c.ObjectIdentifier))
                {
                    lines.Add("Connection|" + c.ObjectIdentifier.ToString(CultureInfo.InvariantCulture) + "|" + Ref(() => c.StartSymbolReference) + ">" + Ref(() => c.EndSymbolReference));
                    if (!light)
                    {
                        lines.AddRange(Properties(c.Properties, snapshot));
                    }

                    snapshot.Connections++;
                }
            }

            snapshot.PageMillis.Add(clock.Elapsed.TotalMilliseconds);
            snapshot.Text.AddRange(lines);
            snapshot.PageHash[page.Name] = Sha256(string.Join("\n", lines));
        }

        snapshot.Seconds = Math.Round(total.Elapsed.TotalSeconds, 2);
        return snapshot;
    }

    /// <summary>Every existing property of the object as "id=value", in the order EPLAN returns them.</summary>
    private static IEnumerable<string> Properties(UniversalPropertyList list, Snapshot snapshot)
    {
        using (list)
        {
            AnyPropertyId[] ids = list.ExistingIds;
            PropertyValue[] values = list.ExistingValues;
            var result = new List<string>(ids.Length);
            try
            {
                for (int i = 0; i < ids.Length && i < values.Length; i++)
                {
                    snapshot.Properties++;
                    result.Add("  " + ids[i].AsInt.ToString(CultureInfo.InvariantCulture) + "=" + Safe(() => values[i].ToString()).Replace("\n", "\\n").Replace("\r", "\\r"));
                }

                if (ids.Length != values.Length)
                {
                    snapshot.LengthMismatches++;
                }
            }
            finally
            {
                foreach (AnyPropertyId id in ids)
                {
                    id.Dispose();
                }

                foreach (PropertyValue value in values)
                {
                    value.Dispose();
                }
            }

            return result;
        }
    }

    private static string Text(Func<PropertyValue> read)
    {
        try
        {
            using PropertyValue value = read();
            return value.ToString() ?? string.Empty;
        }
        catch (Exception ex)
        {
            return "!" + ex.GetType().Name;
        }
    }

    private static string Point(PointD p, Snapshot snapshot)
    {
        snapshot.PositionSamples.Add(p.X);
        snapshot.PositionSamples.Add(p.Y);
        return p.X.ToString("R", CultureInfo.InvariantCulture) + "," + p.Y.ToString("R", CultureInfo.InvariantCulture);
    }

    private static string Ref(Func<StorableObject?> read)
    {
        try
        {
            return read()?.ObjectIdentifier.ToString(CultureInfo.InvariantCulture) ?? "-";
        }
        catch (Exception ex)
        {
            return "!" + ex.GetType().Name;
        }
    }

    private static JObject Compare(Snapshot a, Snapshot b)
    {
        var differing = a.PageHash.Keys.Where(k => !b.PageHash.TryGetValue(k, out string? h) || h != a.PageHash[k]).ToList();
        var onlyB = b.PageHash.Keys.Where(k => !a.PageHash.ContainsKey(k)).ToList();
        return new JObject
        {
            ["pages"] = a.PageHash.Count,
            ["samePages"] = a.PageHash.Count - differing.Count,
            ["differingPages"] = new JArray(differing.Take(20)),
            ["onlyInSecond"] = new JArray(onlyB.Take(20)),
            ["firstDifference"] = FirstDifference(a, b),
        };
    }

    private static string FirstDifference(Snapshot a, Snapshot b)
    {
        for (int i = 0; i < Math.Min(a.Text.Count, b.Text.Count); i++)
        {
            if (a.Text[i] != b.Text[i])
            {
                return i.ToString(CultureInfo.InvariantCulture) + ": " + Cut(a.Text[i]) + " | " + Cut(b.Text[i]);
            }
        }

        return a.Text.Count == b.Text.Count ? string.Empty : "lengths " + a.Text.Count + " vs " + b.Text.Count;
    }

    private static string Cut(string s) => s.Length <= 200 ? s : s.Substring(0, 200);

    /// <summary>SHA-256 of every file of the project's data folder except EPLAN's lock files.</summary>
    private static Dictionary<string, string> Files(string elk)
    {
        string edb = Path.ChangeExtension(elk, ".edb");
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string file in Directory.EnumerateFiles(edb, "*", SearchOption.AllDirectories).Where(f => !f.EndsWith(".lck", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var sha = SHA256.Create();
                result[file.Substring(edb.Length)] = BitConverter.ToString(sha.ComputeHash(stream));
            }
            catch (IOException ex)
            {
                result[file.Substring(edb.Length)] = "!" + ex.GetType().Name;
            }
        }

        return result;
    }

    private static string Sha256(string text)
    {
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", string.Empty);
    }

    private static string Safe(Func<string?> read)
    {
        try
        {
            return read() ?? string.Empty;
        }
        catch (Exception ex)
        {
            return "!" + ex.GetType().Name;
        }
    }

    private static JObject Describe(Exception ex)
    {
        var o = new JObject { ["type"] = ex.GetType().FullName, ["message"] = ex.Message };
        if (ex.InnerException is not null)
        {
            o["inner"] = Describe(ex.InnerException);
        }

        return o;
    }

    private static string Parameter(ActionCallingContext context, string name)
    {
        string value = string.Empty;
        context.GetParameter(name, ref value);
        return value ?? string.Empty;
    }

    private sealed class Params
    {
        public string Report { get; set; } = string.Empty;

        public string Mode { get; set; } = string.Empty;

        public string Project { get; set; } = string.Empty;

        public bool Light { get; set; }

        public bool Once { get; set; }
    }

    private sealed class Snapshot
    {
        public List<string> Text { get; } = [];

        public Dictionary<string, string> PageHash { get; } = new(StringComparer.Ordinal);

        public List<double> PageMillis { get; } = [];

        public List<double> PositionSamples { get; } = [];

        public int Placements { get; set; }

        public int Functions { get; set; }

        public int Connections { get; set; }

        public int Placeholders { get; set; }

        public int Properties { get; set; }

        public int LengthMismatches { get; set; }

        public double Seconds { get; set; }

        public JObject Summary() => new()
        {
            ["seconds"] = Seconds,
            ["pages"] = PageHash.Count,
            ["placements"] = Placements,
            ["functions"] = Functions,
            ["connections"] = Connections,
            ["placeholders"] = Placeholders,
            ["propertyValues"] = Properties,
            ["idValueLengthMismatches"] = LengthMismatches,
            ["lines"] = Text.Count,
            ["megabytes"] = Math.Round(Text.Sum(t => (long)t.Length) / 1048576.0, 1),
            ["slowestPageMs"] = PageMillis.Count == 0 ? 0 : Math.Round(PageMillis.Max(), 1),
            ["averagePageMs"] = PageMillis.Count == 0 ? 0 : Math.Round(PageMillis.Average(), 1),
        };

        /// <summary>How positions look: how many are whole millimetres, on a 0.1 mm or 0.001 mm step, or finer.</summary>
        public JObject Positions()
        {
            int whole = 0, tenth = 0, thousandth = 0, finer = 0;
            foreach (double v in PositionSamples)
            {
                if (Math.Abs(v - Math.Round(v)) < 1e-9)
                {
                    whole++;
                }
                else if (Math.Abs((v * 10) - Math.Round(v * 10)) < 1e-6)
                {
                    tenth++;
                }
                else if (Math.Abs((v * 1000) - Math.Round(v * 1000)) < 1e-4)
                {
                    thousandth++;
                }
                else
                {
                    finer++;
                }
            }

            return new JObject
            {
                ["samples"] = PositionSamples.Count,
                ["whole"] = whole,
                ["tenth"] = tenth,
                ["thousandth"] = thousandth,
                ["finer"] = finer,
                ["finerExamples"] = new JArray(PositionSamples.Where(v => Math.Abs((v * 1000) - Math.Round(v * 1000)) >= 1e-4).Take(10).Select(v => v.ToString("R", CultureInfo.InvariantCulture))),
            };
        }
    }
}

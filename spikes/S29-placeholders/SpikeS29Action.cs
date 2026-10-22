using System.Diagnostics;
using System.Globalization;
using System.Windows.Threading;
using Eplan.EplApi.ApplicationFramework;
using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.Graphics;
using Eplan.EplApi.DataModel.MasterData;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using EplanInsert = Eplan.EplApi.HEServices.Insert;

namespace EplanMcp.Spikes.S29;

/// <summary>
/// Spike S29 (M6 design): placeholder objects and their value sets (records), on the main thread through the
/// dispatcher. <c>scan</c> opens every window macro under a folder without inserting it (api-notes A13/S10) and lists the
/// placeholders of every representation type and variant; <c>list</c> lists the placeholders of open projects;
/// <c>apply</c> inserts a macro on a new page of a project under the scratch root, applies each record and reads back
/// what changed, inside a SafetyPoint that is rolled back.
/// <para>Usage: <c>EplanMcpSpikeS29 /Mode:scan|list|apply /Report:"…\x.json" /Project:"…\x.elk" /Part:"folder or .ema" /Variant:n</c>.</para>
/// </summary>
public sealed class SpikeS29Action : IEplAction
{
    private static readonly string _scratchRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "EplanMcp", "scratch") + @"\";

    public bool OnRegister(ref string Name, ref int Ordinal)
    {
        string assemblyName = typeof(SpikeS29Action).Assembly.GetName().Name ?? "EplanMcp.Spikes.S29";
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
            Part = Parameter(oActionCallingContext, "Part"),
            Variant = int.TryParse(Parameter(oActionCallingContext, "Variant"), out int v) ? v : -1,
        };
        Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
        var thread = new Thread(() => RunLater(dispatcher, p)) { IsBackground = true };
        thread.Start();
        return true;
    }

    private static void RunLater(Dispatcher dispatcher, Params p)
    {
        var report = new JObject { ["spike"] = "S29-placeholders", ["mode"] = p.Mode, ["timestamp"] = DateTime.Now.ToString("o", CultureInfo.InvariantCulture) };
        try
        {
            Thread.Sleep(300);
            object? done = dispatcher.Invoke(DispatcherPriority.Normal, TimeSpan.FromSeconds(600), new Func<bool>(() =>
            {
                try
                {
                    using (new LockingStep())
                    using (new QuietModeStep(QuietModes.ShowNoDialogs))
                    {
                        report[p.Mode] = p.Mode switch
                        {
                            "scan" => Scan(Open(p.Project), p.Part),
                            "list" => new JArray(p.Project.Split('|').Select(path => List(Open(path)))),
                            "apply" => Apply(Open(p.Project), p.Part, p.Variant),
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
                report["fatal"] = "the dispatcher did not run the probes within 600 s";
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

    /// <summary>
    /// Every .ema under <paramref name="root"/>, opened in the context of <paramref name="project"/> without inserting:
    /// for each representation type and variant, the placeholders among the macro's objects. A root with EPLAN path
    /// variables ($(MD_MACROS)) is expanded by EPLAN, so non-ASCII folders need not pass through Remote Client (S24).
    /// </summary>
    private static JObject Scan(Project project, string root)
    {
        root = PathMap.SubstitutePath(root);
        var clock = Stopwatch.StartNew();
        int macros = 0, failed = 0, variants = 0;
        var found = new JArray();
        var errors = new JArray();
        int pagesBefore = Pages(project);
        foreach (string path in Directory.EnumerateFiles(root, "*.ema", SearchOption.AllDirectories))
        {
            macros++;
            try
            {
                using var macro = new WindowMacro();
                macro.Open(path, project);
                foreach (WindowMacro.Enums.RepresentationType type in macro.RepresentationTypes)
                {
                    foreach (int variant in macro.GetVariants(type))
                    {
                        variants++;
                        macro.ChangeCurrentVariant(type, variant);
                        PlaceHolder[] holders = macro.Objects.OfType<PlaceHolder>().ToArray();
                        if (holders.Length > 0 && found.Count < 200)
                        {
                            found.Add(new JObject
                            {
                                ["macro"] = path.Substring(root.Length).TrimStart('\\'),
                                ["type"] = type.ToString(),
                                ["variant"] = variant,
                                ["objects"] = macro.Objects.Length,
                                ["placeholders"] = new JArray(holders.Select(h => Describe(h, values: false))),
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                failed++;
                if (errors.Count < 10)
                {
                    errors.Add(path.Substring(root.Length) + ": " + ex.GetType().Name + " " + ex.Message);
                }
            }
        }

        return new JObject
        {
            ["macros"] = macros,
            ["variants"] = variants,
            ["failed"] = failed,
            ["withPlaceholders"] = found.Count,
            ["seconds"] = Seconds(clock),
            ["projectPagesBefore"] = pagesBefore,
            ["projectPagesAfter"] = Pages(project),
            ["found"] = found,
            ["errors"] = errors,
        };
    }

    /// <summary>The placeholders of an open project, as the placeholder navigator lists them without a filter.</summary>
    private static JObject List(Project project)
    {
        var clock = Stopwatch.StartNew();
        using var finder = new DMObjectsFinder(project);
        PlaceHolder[] holders = finder.GetPlaceholdersWithFilterScheme(null);
        double seconds = Seconds(clock);
        return new JObject
        {
            ["project"] = project.ProjectName,
            ["count"] = holders.Length,
            ["seconds"] = seconds,
            ["placeholders"] = new JArray(holders.Take(20).Select(h => Describe(h, values: true))),
        };
    }

    /// <summary>
    /// Inserts <paramref name="macroPath"/> on a new page of a project under the scratch root, then applies each record
    /// of each inserted placeholder and reads the assigned objects' names and the current values after each; everything
    /// is rolled back with the SafetyPoint (api-notes A13: inserted macros roll back only so).
    /// </summary>
    private static JObject Apply(Project project, string macroPath, int variant)
    {
        if (!project.ProjectLinkFilePath.StartsWith(_scratchRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("apply writes only to projects under " + _scratchRoot);
        }

        var result = new JObject { ["project"] = project.ProjectName, ["pagesBefore"] = Pages(project) };
        using (SafetyPoint safety = SafetyPoint.Create())
        {
            var clock = Stopwatch.StartNew();
            Page page = NewPage(project);
            result["page"] = page.Name;
            using var macro = new WindowMacro();
            macro.Open(PathMap.SubstitutePath(macroPath), project);
            if (variant < 0)
            {
                variant = macro.GetVariants(WindowMacro.Enums.RepresentationType.MultiLine).FirstOrDefault();
            }

            using var insert = new EplanInsert();
            StorableObject[] placed = insert.WindowMacro(macro, WindowMacro.Enums.RepresentationType.MultiLine, variant, page, new PointD(100, 150), EplanInsert.MoveKind.Absolute, WindowMacro.Enums.NumerationMode.None);
            result["inserted"] = placed.Length;
            result["insertSeconds"] = Seconds(clock);
            PlaceHolder[] holders = page.AllPlacements.OfType<PlaceHolder>().ToArray();
            result["placeholdersOnPage"] = holders.Length;
            var applied = new JArray();
            foreach (PlaceHolder holder in holders)
            {
                var entry = new JObject { ["before"] = Describe(holder, values: true) };
                var records = new JArray();
                for (int i = 0; i < holder.NumberOfRecords; i++)
                {
                    string record = holder.get_NameOfRecord(i);
                    clock.Restart();
                    try
                    {
                        holder.ApplyRecord(record);
                        records.Add(new JObject
                        {
                            ["record"] = record,
                            ["seconds"] = Seconds(clock),
                            ["assigned"] = new JArray(holder.AssignedObjects.Take(20).Select(Name)),
                            ["currentValues"] = CurrentValues(holder),
                            ["functionsOnPage"] = new JArray(page.Functions.Take(20).Select(f => f.Name)),
                        });
                    }
                    catch (Exception ex)
                    {
                        records.Add(new JObject { ["record"] = record, ["error"] = Describe(ex) });
                    }
                }

                entry["records"] = records;
                applied.Add(entry);
            }

            result["applied"] = applied;
            result["created"] = CreateRecord(holders.FirstOrDefault(h => h.NumberOfVariables > 0 && h.NumberOfRecords > 0));
            safety.Rollback();
        }

        result["pagesAfterRollback"] = Pages(project);
        return result;
    }

    /// <summary>
    /// A new record made through the API (AddRecord, then SetValue for each variable as a language-independent text),
    /// applied like the macro's own; reports what the records, the stored values and the current values are after.
    /// </summary>
    private static JObject CreateRecord(PlaceHolder? holder)
    {
        if (holder is null)
        {
            return new JObject { ["skipped"] = "no placeholder with records and variables" };
        }

        var o = new JObject { ["placeholder"] = holder.Name };
        try
        {
            var clock = Stopwatch.StartNew();
            o["addRecord"] = holder.AddRecord("S29");
            foreach (string variable in holder.VariableNames.Cast<string>())
            {
                using var text = new MultiLangString();
                text.AddString(ISOCode.Language.L___, "S29 " + variable);
                holder.SetValue("S29", variable, text);
            }

            holder.ApplyRecord("S29");
            o["seconds"] = Seconds(clock);
            o["records"] = new JArray(Range(holder.NumberOfRecords).Select(i => Safe(() => holder.get_NameOfRecord(i))));
            o["stored"] = new JObject(holder.VariableNames.Cast<string>().Select(v => new JProperty(v, Safe(() => holder.get_Value("S29", v)))));
            o["currentValues"] = CurrentValues(holder);
        }
        catch (Exception ex)
        {
            o["error"] = Describe(ex);
        }

        return o;
    }

    private static Page NewPage(Project project)
    {
        using var finder = new DMObjectsFinder(project);
        Page[] pages = finder.GetPages(null);
        Page template = pages.First(pg => pg.PageType == DocumentTypeManager.DocumentType.Circuit);
        var used = new HashSet<string>(pages.Select(pg => pg.Name), StringComparer.OrdinalIgnoreCase);
        using var parts = new PagePropertyList();
        parts.DESIGNATION_FUNCTIONALASSIGNMENT = template.Properties.DESIGNATION_FUNCTIONALASSIGNMENT.ToString();
        parts.DESIGNATION_PLANT = template.Properties.DESIGNATION_PLANT.ToString();
        parts.DESIGNATION_PLACEOFINSTALLATION = template.Properties.DESIGNATION_PLACEOFINSTALLATION.ToString();
        parts.DESIGNATION_LOCATION = template.Properties.DESIGNATION_LOCATION.ToString();
        parts.DESIGNATION_DOCTYPE = template.Properties.DESIGNATION_DOCTYPE.ToString();
        for (int counter = 929; ; counter++)
        {
            parts.PAGE_COUNTER = counter.ToString(CultureInfo.InvariantCulture);
            if (!used.Any(name => name.EndsWith("/" + counter.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)))
            {
                break;
            }
        }

        var page = new Page();
        page.Create(project, DocumentTypeManager.DocumentType.Circuit, parts);
        return page;
    }

    private static JObject Describe(PlaceHolder holder, bool values)
    {
        var o = new JObject
        {
            ["name"] = Safe(() => holder.Name),
            ["page"] = Safe(() => holder.Page?.Name),
            ["records"] = new JArray(Range(holder.NumberOfRecords).Select(i => Safe(() => holder.get_NameOfRecord(i)))),
            ["variables"] = new JArray(holder.VariableNames.Cast<string>().Take(30)),
            ["variableCount"] = (int)holder.NumberOfVariables,
            ["references"] = holder.NumberOfReferences,
        };
        if (values)
        {
            var table = new JObject();
            foreach (string record in Range(holder.NumberOfRecords).Select(holder.get_NameOfRecord).Take(10))
            {
                table[record] = new JObject(holder.VariableNames.Cast<string>().Take(15).Select(v => new JProperty(v, Safe(() => holder.get_Value(record, v)))));
            }

            o["values"] = table;
            o["assigned"] = new JArray(holder.AssignedObjects.Take(20).Select(Name));
        }

        return o;
    }

    private static JArray CurrentValues(PlaceHolder holder)
    {
        try
        {
            return new JArray(holder.GetAllCurrentValues().Cast<object>().Take(20).Select(v => v?.ToString() ?? string.Empty));
        }
        catch (Exception ex)
        {
            return new JArray("!" + ex.GetType().Name + " " + ex.Message);
        }
    }

    private static string Name(StorableObject o) => o switch
    {
        Function f => "F " + f.Name,
        Placement pl => pl.GetType().Name + " " + pl.ToStringIdentifier(),
        _ => o.GetType().Name + " " + o.ObjectIdentifier.ToString(CultureInfo.InvariantCulture),
    };

    private static int Pages(Project project)
    {
        using var finder = new DMObjectsFinder(project);
        return finder.GetPages(null).Length;
    }

    private static IEnumerable<int> Range(int count) => Enumerable.Range(0, Math.Max(0, count));

    private static double Seconds(Stopwatch clock) => Math.Round(clock.Elapsed.TotalSeconds, 2);

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

        public string Part { get; set; } = string.Empty;

        public int Variant { get; set; } = -1;
    }
}

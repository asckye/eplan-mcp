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

namespace EplanMcp.Spikes.S32;

/// <summary>
/// Spike S32 (M6 design 4.10): licence options, and writing a module back as macros, on the main thread through the
/// dispatcher. <c>licence</c> reads every licence option (<c>License.GetLicenseModules</c>) and the Cogineer options
/// (<c>HasOption</c>; never <c>LockOption</c>, which takes a licence). <c>create</c> works on a project under the scratch
/// root inside a SafetyPoint that is rolled back: inserts a macro on a new page, creates a placeholder through the API
/// and applies its records, writes the page as a window macro and a page macro under the scratch root, reopens the
/// window macro, and inserts it on a second page.
/// <para>Usage: <c>EplanMcpSpikeS32 /Mode:licence|create /Report:"…\x.json" /Project:"…\x.elk" /Part:"source .ema" /Variant:n</c>.</para>
/// </summary>
public sealed class SpikeS32Action : IEplAction
{
    private static readonly string _scratchRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "EplanMcp", "scratch") + @"\";

    public bool OnRegister(ref string Name, ref int Ordinal)
    {
        string assemblyName = typeof(SpikeS32Action).Assembly.GetName().Name ?? "EplanMcp.Spikes.S32";
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
        var report = new JObject { ["spike"] = "S32-macro-create", ["mode"] = p.Mode, ["timestamp"] = DateTime.Now.ToString("o", CultureInfo.InvariantCulture) };
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
                            "licence" => Licence(),
                            "create" => Create(p.Project, p.Part, p.Variant),
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

    /// <summary>Every licence option and whether it is available; the Cogineer options once more through HasOption.</summary>
    private static JObject Licence()
    {
        using var licence = new License();
        var modules = new Dictionary<LicenseOptions, bool>();
        licence.GetLicenseModules(ref modules);
        var cogineer = new JObject();
        foreach (LicenseOptions option in new[] { LicenseOptions.CogineerDesigner, LicenseOptions.CogineerDesignerAdvanced, LicenseOptions.CogineerBuilder, LicenseOptions.CogineerBuilderAdvanced, LicenseOptions.CogineerSilentMode, LicenseOptions.ImportAndInsertMacros })
        {
            cogineer[option.ToString()] = licence.HasOption(option);
        }

        return new JObject
        {
            ["modules"] = modules.Count,
            ["available"] = new JArray(modules.Where(m => m.Value).Select(m => m.Key.ToString()).OrderBy(s => s, StringComparer.Ordinal)),
            ["unavailable"] = modules.Count(m => !m.Value),
            ["hasOption"] = cogineer,
        };
    }

    private static JObject Create(string projectPath, string macroPath, int variant)
    {
        Project project = Open(projectPath);
        if (!project.ProjectLinkFilePath.StartsWith(_scratchRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("create writes only to projects under " + _scratchRoot);
        }

        string folder = Path.Combine(_scratchRoot, "spikes", "s32", "macros", DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(folder);
        var result = new JObject { ["pagesBefore"] = Pages(project), ["folder"] = folder.Substring(_scratchRoot.Length) };
        using (SafetyPoint safety = SafetyPoint.Create())
        {
            Page page = NewPage(project, 929);
            using (var source = new WindowMacro())
            {
                source.Open(PathMap.SubstitutePath(macroPath), project);
                if (variant < 0)
                {
                    variant = source.GetVariants(WindowMacro.Enums.RepresentationType.MultiLine).FirstOrDefault();
                }

                using var insert = new EplanInsert();
                result["inserted"] = insert.WindowMacro(source, WindowMacro.Enums.RepresentationType.MultiLine, variant, page, new PointD(100, 150), EplanInsert.MoveKind.Absolute, WindowMacro.Enums.NumerationMode.None).Length;
            }

            result["newPlaceholder"] = NewPlaceholder(page);
            Placement[] placements = page.AllPlacements;
            result["placementsOnPage"] = placements.Length;

            string windowMacro = Path.Combine(folder, "S32_window.ema");
            result["windowMacro"] = Try(() =>
            {
                using var description = Text("S32 window macro");
                using var created = new WindowMacro();
                created.Create(windowMacro, WindowMacro.Enums.RepresentationType.MultiLine, 0, placements, description);
                return new JObject { ["exists"] = File.Exists(windowMacro), ["bytes"] = File.Exists(windowMacro) ? new FileInfo(windowMacro).Length : 0 };
            });

            string pageMacro = Path.Combine(folder, "S32_page.emp");
            result["pageMacro"] = Try(() =>
            {
                using var description = Text("S32 page macro");
                using var created = new PageMacro();
                created.Create(pageMacro, [page], description);
                return new JObject { ["exists"] = File.Exists(pageMacro), ["bytes"] = File.Exists(pageMacro) ? new FileInfo(pageMacro).Length : 0 };
            });

            result["reopened"] = Try(() =>
            {
                using var reopened = new WindowMacro();
                reopened.Open(windowMacro, project);
                PlaceHolder[] holders = reopened.Objects.OfType<PlaceHolder>().ToArray();
                return new JObject
                {
                    ["objects"] = reopened.Objects.Length,
                    ["representations"] = new JArray(reopened.RepresentationTypes.Select(t => t.ToString() + ":" + string.Join(",", reopened.GetVariants(t)))),
                    ["placeholders"] = new JArray(holders.Select(Describe)),
                };
            });

            result["insertedAgain"] = Try(() =>
            {
                Page second = NewPage(project, 930);
                using var again = new WindowMacro();
                again.Open(windowMacro, project);
                using var insert = new EplanInsert();
                StorableObject[] placed = insert.WindowMacro(again, WindowMacro.Enums.RepresentationType.MultiLine, 0, second, new PointD(100, 150), EplanInsert.MoveKind.Absolute, WindowMacro.Enums.NumerationMode.None);
                PlaceHolder[] holders = second.AllPlacements.OfType<PlaceHolder>().ToArray();
                return new JObject { ["page"] = second.Name, ["placed"] = placed.Length, ["placeholders"] = new JArray(holders.Select(Describe)) };
            });

            safety.Rollback();
        }

        result["pagesAfterRollback"] = Pages(project);
        result["filesAfterRollback"] = new JArray(Directory.GetFiles(folder).Select(f => Path.GetFileName(f) + " " + new FileInfo(f).Length));
        return result;
    }

    /// <summary>
    /// A placeholder made through the API: variable V1 bound to the function text of the first main function on the
    /// page (SetPropertyEntry with the variable name in angle brackets), records A and B with different values, each applied and the
    /// function text read back.
    /// </summary>
    private static JObject NewPlaceholder(Page page)
    {
        var o = new JObject();
        try
        {
            Function target = page.Functions.First(f => f.IsMainFunction);
            o["target"] = target.Name;
            o["textBefore"] = Read(() => target.Properties.FUNC_TEXT);
#pragma warning disable CA2000 // A placement created inside the SafetyPoint, as EplanPageWriter does with pages.
            var holder = new PlaceHolder();
#pragma warning restore CA2000
            holder.Create(page);
            holder.Name = "S32";
            o["addVariable"] = Try(() => new JValue(holder.AddVariable("V1").ToString(CultureInfo.InvariantCulture)));
            holder.AddReference(target);
            AnyPropertyId text = Eplan.EplApi.DataModel.Properties.Function.FUNC_TEXT;
            o["setPropertyEntry"] = Try(() =>
            {
                using var entry = Text("<V1>"); // r2: the variable in angle brackets, as EPLAN stores it in macros (S115x1904 A1913)
                holder.SetPropertyEntry(target, text, entry);
                return new JValue("ok");
            });
            foreach (string record in new[] { "A", "B" })
            {
                o["addRecord" + record] = Try(() => new JValue(holder.AddRecord(record).ToString(CultureInfo.InvariantCulture)));
                using var value = Text("S32 value " + record);
                o["setValue" + record] = Try(() =>
                {
                    holder.SetValue(record, "V1", value);
                    return new JValue("ok");
                });
            }

            foreach (string record in new[] { "A", "B" })
            {
                o["apply" + record] = Try(() =>
                {
                    holder.ApplyRecord(record);
                    return new JValue(Read(() => target.Properties.FUNC_TEXT));
                });
            }

            o["placeholder"] = Describe(holder);
        }
        catch (Exception ex)
        {
            o["error"] = Describe(ex);
        }

        return o;
    }

    private static JObject Describe(PlaceHolder holder)
    {
        var records = Enumerable.Range(0, Math.Max(0, holder.NumberOfRecords)).Select(holder.get_NameOfRecord).ToList();
        var values = new JObject();
        foreach (string record in records.Take(10))
        {
            values[record] = new JObject(holder.VariableNames.Cast<string>().Take(10).Select(v => new JProperty(v, Safe(() => holder.get_Value(record, v)))));
        }

        return new JObject
        {
            ["name"] = Safe(() => holder.Name),
            ["records"] = new JArray(records),
            ["variables"] = new JArray(holder.VariableNames.Cast<string>().Take(20)),
            ["references"] = holder.NumberOfReferences,
            ["values"] = values,
        };
    }

    private static Project Open(string path)
    {
        using var manager = new ProjectManager();
        return manager.OpenProjects.FirstOrDefault(o => string.Equals(o.ProjectLinkFilePath, path, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("not open: " + path);
    }

    private static Page NewPage(Project project, int counter)
    {
        using var finder = new DMObjectsFinder(project);
        Page template = finder.GetPages(null).First(pg => pg.PageType == DocumentTypeManager.DocumentType.Circuit);
        using var parts = new PagePropertyList();
        parts.DESIGNATION_FUNCTIONALASSIGNMENT = template.Properties.DESIGNATION_FUNCTIONALASSIGNMENT.ToString();
        parts.DESIGNATION_PLANT = template.Properties.DESIGNATION_PLANT.ToString();
        parts.DESIGNATION_PLACEOFINSTALLATION = template.Properties.DESIGNATION_PLACEOFINSTALLATION.ToString();
        parts.DESIGNATION_LOCATION = template.Properties.DESIGNATION_LOCATION.ToString();
        parts.DESIGNATION_DOCTYPE = template.Properties.DESIGNATION_DOCTYPE.ToString();
        parts.PAGE_COUNTER = counter.ToString(CultureInfo.InvariantCulture);
#pragma warning disable CA2000 // Spikes S0–S10 never disposed a new Page before the commit (as EplanPageWriter).
        var page = new Page();
#pragma warning restore CA2000
        page.Create(project, DocumentTypeManager.DocumentType.Circuit, parts);
        return page;
    }

    private static int Pages(Project project)
    {
        using var finder = new DMObjectsFinder(project);
        return finder.GetPages(null).Length;
    }

    private static MultiLangString Text(string value)
    {
        var text = new MultiLangString();
        text.AddString(ISOCode.Language.L___, value);
        return text;
    }

    private static string Read(Func<PropertyValue> read)
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

    private static JToken Try(Func<JToken> probe)
    {
        try
        {
            return probe();
        }
        catch (Exception ex)
        {
            return new JObject { ["error"] = Describe(ex) };
        }
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

        public string Part { get; set; } = string.Empty;

        public int Variant { get; set; } = -1;
    }
}

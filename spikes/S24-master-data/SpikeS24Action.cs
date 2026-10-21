using System.Diagnostics;
using System.Globalization;
using System.Windows.Threading;
using Eplan.EplApi.ApplicationFramework;
using Eplan.EplApi.Base;
using Eplan.EplApi.HEServices;
using Eplan.EplApi.MasterData;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Spikes.S24;

/// <summary>
/// Spike S24 (M4c design 4.8, 4.9; ADR-0019): which parts and translation databases EPLAN is configured with, and
/// reading parts from the parts database the way eplan_part_search and eplan_device_place would, on the main thread
/// through the dispatcher. The configured databases are only read and backed up, never written, and their settings
/// are never set (XML: that changes the global user setting); the write part writes only to folders and database
/// copies under the scratch root.
/// <para>
/// Usage: <c>EplanMcpSpikeS24 /Mode:partsdb|translationdb|opencopy /Report:"…\x.json" [/Part:number] [/Contains:text]</c>;
/// for <c>opencopy</c> (write part) <c>/Part</c> is the path of the copy, which must be under the scratch root.
/// <c>backupparts</c>, <c>backuptranslation</c>, <c>restore</c> (archive in <c>/Part</c>) and <c>copyio</c> write only to
/// <c>/Destination</c>, also under the scratch root.
/// </para>
/// </summary>
public sealed class SpikeS24Action : IEplAction
{
    public bool OnRegister(ref string Name, ref int Ordinal)
    {
        string assemblyName = typeof(SpikeS24Action).Assembly.GetName().Name ?? "EplanMcp.Spikes.S24";
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
            Part = Parameter(oActionCallingContext, "Part"),
            Contains = Parameter(oActionCallingContext, "Contains"),
            Destination = Parameter(oActionCallingContext, "Destination"),
        };
        Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
        var thread = new Thread(() => RunLater(dispatcher, p)) { IsBackground = true };
        thread.Start();
        return true;
    }

    private static void RunLater(Dispatcher dispatcher, Params p)
    {
        var report = new JObject { ["spike"] = "S24-master-data", ["mode"] = p.Mode, ["timestamp"] = DateTime.Now.ToString("o", CultureInfo.InvariantCulture) };
        try
        {
            Thread.Sleep(300); // let the scheduling action return first
            object? done = dispatcher.Invoke(DispatcherPriority.Normal, TimeSpan.FromSeconds(600), new Func<bool>(() =>
            {
                // Everything is caught in here: an exception escaping a dispatched delegate ends EPLAN (S1).
                try
                {
                    using (new QuietModeStep(QuietModes.ShowNoDialogs))
                    {
                        Run(report, p);
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

    private static void Run(JObject report, Params p)
    {
        switch (p.Mode)
        {
            case "partsdb":
                report["parts"] = PartsDatabase(p);
                break;
            case "opencopy":
                report["copy"] = OpenCopy(p.Part);
                break;
            case "importpart":
                report["importPart"] = ImportIntoConfigured(p.Part);
                break;
            case "syncparts":
                report["syncParts"] = SyncProjectParts();
                break;
            case "importtranslation":
                report["importTranslation"] = ImportTranslation(p.Destination, p.Contains);
                break;
            case "exporttranslation":
                report["exportTranslation"] = ExportTranslation(p.Destination);
                break;
            case "labelformat":
                report["labelFormat"] = LabelFormats();
                break;
            case "copyio":
                report["copyio"] = CopyImportExport(p.Part, p.Destination);
                break;
            case "backupparts":
                report["backup"] = RunAction(p.Destination, "backup", MasterDataBackup("ARTICLES", MDPartsManagement.SelectedPartsDatabaseAsString, p.Destination, "s24-parts"));
                break;
            case "backuptranslation":
                using (var translate = new Translate())
                {
                    report["backup"] = RunAction(p.Destination, "backup", MasterDataBackup("LANGUAGES", translate.TranslateDatabase.ToString(), p.Destination, "s24-translate"));
                }

                break;
            case "restore":
                report["restore"] = RunAction(p.Destination, "restore", new Dictionary<string, string>
                {
                    ["TYPE"] = "MASTERDATA",
                    ["ARCHIVENAME"] = p.Part,
                    ["DESTINATIONPATH"] = p.Destination,
                });
                break;
            case "translationdb":
                report["translation"] = new JObject
                {
                    ["translateDatabase"] = Try(() => new Translate().TranslateDatabase.ToString()),
                    ["activeTranslateDatabase"] = Try(() => new Translate().ActiveTranslateDatabase.ToString()),
                };
                break;
            default:
                report["refused"] = "unknown mode " + p.Mode;
                break;
        }
    }

    private static JObject PartsDatabase(Params p)
    {
        var result = new JObject();
        var management = new MDPartsManagement();
        string selected = MDPartsManagement.SelectedPartsDatabaseAsString;
        result["selected"] = selected;
        result["partsServiceDatabase"] = Try(() => new PartsService().PartsDatabase?.ToString());
        result["fileBefore"] = FileState(selected);
        var clock = Stopwatch.StartNew();
        MDPartsDatabase database = management.OpenDatabase();
        result["openSeconds"] = Seconds(clock);
        try
        {
            result["isOpen"] = Try(() => database.IsOpen);
            result["isReadOnly"] = Try(() => database.IsReadOnly);
            result["type"] = Try(() => database.Type.ToString());
            result["schemeUpToDate"] = Try(() => database.IsSchemeUpToDate);

            clock.Restart();
            MDPart? part = database.GetPart(p.Part);
            result["getPartSeconds"] = Seconds(clock);
            result["part"] = part is null ? JValue.CreateNull() : Describe(part);
            result["variants"] = Try(() => new JArray(database.GetPartVariants(p.Part).Select(v => v.Variant)));
            result["missingPart"] = Try(() => database.GetPart("EPLANMCP.DOES-NOT-EXIST") is null ? "null" : "found");
            result["existsPart"] = Try(() => database.ExistsPart(p.Part));

            clock.Restart();
            MDPart[] all = database.Parts;
            result["allPartsSeconds"] = Seconds(clock);
            result["allParts"] = all.Length;
            clock.Restart();
            MDPart[] matching = [.. all.Where(x => (x.PartNr ?? string.Empty).IndexOf(p.Contains, StringComparison.OrdinalIgnoreCase) >= 0)];
            result["containsSeconds"] = Seconds(clock);
            result["containsCount"] = matching.Length;
            result["containsSamples"] = new JArray(matching.Take(5).Select(x => x.PartNr));
            clock.Restart();
            result["descriptionsOfFirst200"] = all.Take(200).Count(x => Text(x.Properties.ARTICLE_DESCR1).Length > 0);
            result["descriptionsSeconds"] = Seconds(clock);
        }
        finally
        {
            database.Close();
        }

        result["fileAfter"] = FileState(selected);
        return result;
    }

    /// <summary>
    /// Write part (ADR-0019): opens a copy of the parts database restored under the scratch root, never the configured
    /// one, checks that this leaves the configured database unchanged, and adds a test part once in a transaction that
    /// is rolled back and once in one that is committed.
    /// </summary>
    private static JObject OpenCopy(string path)
    {
        var result = new JObject { ["path"] = path, ["selectedBefore"] = MDPartsManagement.SelectedPartsDatabaseAsString };
        if (path.IndexOf(@"\EplanMcp\scratch\", StringComparison.OrdinalIgnoreCase) < 0)
        {
            result["refused"] = "the copy must be under the scratch root";
            return result;
        }

        var clock = Stopwatch.StartNew();
        MDPartsDatabase database = new MDPartsManagement().OpenDatabase(path);
        result["openSeconds"] = Seconds(clock);
        result["selectedAfterOpen"] = MDPartsManagement.SelectedPartsDatabaseAsString;
        try
        {
            result["isReadOnly"] = Try(() => database.IsReadOnly);
            result["allParts"] = Try(() => database.Parts.Length);
            const string TestPart = "EPLANMCP.S24-TEST";
            result["existsBefore"] = Try(() => database.ExistsPart(TestPart));
            // r1 did not dispose the transactions: after AddPart threw inside one, the next CreateTransaction threw an
            // SEHException. Every transaction is disposed now, also when the write in it throws.
            result["rolledBack"] = Try(() =>
            {
                using MDPartsDatabaseTransaction transaction = database.CreateTransaction();
                database.AddPart(TestPart, "1");
                bool during = database.ExistsPart(TestPart);
                transaction.Rollback();
                return new JObject { ["during"] = during, ["after"] = database.ExistsPart(TestPart) };
            });
            result["committed"] = Try(() =>
            {
                using MDPartsDatabaseTransaction transaction = database.CreateTransaction();
                MDPart part = database.AddPart(TestPart, "1");
                part.Properties.ARTICLE_DESCR1 = "EplanMcp S24 test part";
                transaction.Commit();
                return new JObject { ["after"] = database.ExistsPart(TestPart), ["allParts"] = database.Parts.Length };
            });
            result["addedTwice"] = Try(() =>
            {
                using MDPartsDatabaseTransaction transaction = database.CreateTransaction();
                database.AddPart(TestPart, "1");
                transaction.Commit();
                return "no exception";
            });
            result["removedRolledBack"] = Try(() =>
            {
                using MDPartsDatabaseTransaction transaction = database.CreateTransaction();
                database.RemovePart(database.GetPart(TestPart));
                bool during = database.ExistsPart(TestPart);
                transaction.Rollback();
                return new JObject { ["during"] = during, ["after"] = database.ExistsPart(TestPart), ["allParts"] = database.Parts.Length };
            });
        }
        finally
        {
            database.Close();
        }

        result["selectedAfterClose"] = MDPartsManagement.SelectedPartsDatabaseAsString;
        return result;
    }

    /// <summary>
    /// r11 (batch 4 specs): exports from a parts database copy under the scratch root, imports a part file back into
    /// it with and without a transaction, and exports the configured translation database (read only) to files under
    /// the scratch root.
    /// </summary>
    private static JObject CopyImportExport(string path, string destination)
    {
        var result = new JObject { ["path"] = path, ["destination"] = destination };
        if (path.IndexOf(@"\EplanMcp\scratch\", StringComparison.OrdinalIgnoreCase) < 0
            || destination.IndexOf(@"\EplanMcp\scratch\", StringComparison.OrdinalIgnoreCase) < 0)
        {
            result["refused"] = "the copy and the destination must be under the scratch root";
            return result;
        }

        Directory.CreateDirectory(destination);
        string all = Path.Combine(destination, "all.xml");
        string one = Path.Combine(destination, "one.xml");
        const string TestPart = "EPLANMCP.S24-TEST";
        MDPartsDatabase database = new MDPartsManagement().OpenDatabase(path);
        try
        {
            result["allParts"] = Try(() => database.Parts.Length);
            result["exportXml"] = Timed(() => database.ExportParts(all, MDPartsDatabase.DataFormat.XML), all);
            string edz = Path.Combine(destination, "all.edz");
            result["exportEdz"] = Timed(() => database.ExportParts(edz, MDPartsDatabase.DataFormat.EDZ), edz);
            result["exportOne"] = Try(() =>
            {
                using (MDPartsDatabaseTransaction transaction = database.CreateTransaction())
                {
                    MDPart part = database.AddPart(TestPart, "1");
                    part.Properties.ARTICLE_DESCR1 = "EplanMcp S24 test part";
                    transaction.Commit();
                }

                database.ExportParts(one, MDPartsDatabase.DataFormat.XML, [database.GetPart(TestPart)]);
                database.RemovePart(database.GetPart(TestPart));
                return new JObject { ["bytes"] = new FileInfo(one).Length, ["existsAfterRemove"] = database.ExistsPart(TestPart) };
            });
            result["importRolledBack"] = Try(() =>
            {
                using MDPartsDatabaseTransaction transaction = database.CreateTransaction();
                database.ImportParts(one, "XPamImportXml", MDPartsDatabase.ImportMode.AppendNewRecords, false, MDPartsDatabase.ImportIdentifier.PartNumber, string.Empty);
                bool during = database.ExistsPart(TestPart);
                transaction.Rollback();
                return new JObject { ["during"] = during, ["after"] = database.ExistsPart(TestPart), ["allParts"] = database.Parts.Length };
            });
            result["imported"] = Try(() =>
            {
                var clock = Stopwatch.StartNew();
                database.ImportParts(one, "XPamImportXml", MDPartsDatabase.ImportMode.AppendNewRecords, false, MDPartsDatabase.ImportIdentifier.PartNumber, string.Empty);
                return new JObject { ["seconds"] = Seconds(clock), ["exists"] = database.ExistsPart(TestPart), ["allParts"] = database.Parts.Length, ["description"] = Text(database.GetPart(TestPart).Properties.ARTICLE_DESCR1) };
            });
            result["importedAgain"] = Try(() =>
            {
                database.ImportParts(one, "XPamImportXml", MDPartsDatabase.ImportMode.UpdateAndAppend, false, MDPartsDatabase.ImportIdentifier.PartNumber, string.Empty);
                return new JObject { ["allParts"] = database.Parts.Length, ["variants"] = database.GetPartVariants(TestPart).Length };
            });
            result["importAll"] = Try(() =>
            {
                var clock = Stopwatch.StartNew();
                database.ImportParts(all, "XPamImportXml", MDPartsDatabase.ImportMode.UpdateAndAppend, false, MDPartsDatabase.ImportIdentifier.PartNumber, string.Empty);
                return new JObject { ["seconds"] = Seconds(clock), ["allParts"] = database.Parts.Length };
            });
        }
        finally
        {
            database.Close();
        }

        using (var translate = new Translate())
        {
            string xml = Path.Combine(destination, "translate.xml");
            result["translationExportXml"] = Timed(() => translate.ExportDatabaseTexts(xml, "XTrLanguageDbXmlConverterImpl"), xml);
            string tab = Path.Combine(destination, "translate.txt");
            result["translationExportTab"] = Timed(() => translate.ExportDatabaseTexts(tab, "XTrLanguageDbXml2TabConverterImpl"), tab);
        }

        result["selectedAfter"] = MDPartsManagement.SelectedPartsDatabaseAsString;
        return result;
    }

    // r12 (owner-approved, 2026-10-21): the probes below write the CONFIGURED parts and translation databases. They
    // run only after both databases were backed up and hashed; afterwards EPLAN is closed and both are restored.

    /// <summary>
    /// Read only, for eplan_export_labels (Large test 2026-10-21: <c>Data.File.Format</c> could not be read): tries
    /// several ways to read each labelling scheme's output format.
    /// </summary>
    private static JArray LabelFormats()
    {
        var result = new JArray();
        using var setting = new SchemeSetting();
        setting.Init("USER.Labelling.Config");
        for (int i = 0; i < setting.GetCount() && i < 6; i++)
        {
            setting.SetScheme(i);
            string name = setting.GetName();
            var entry = new JObject { ["name"] = name };
            foreach (string path in new[] { "Data.File.Format", "File.Format", "Format", ".Data.File.Format" })
            {
                entry["scheme:" + path] = Try(() => setting.GetStringSetting(path, 0));
            }

            entry["settings:full"] = Try(() => new Settings().GetStringSetting("USER.Labelling.Config." + name + ".Data.File.Format", 0));
            entry["settings:exists"] = Try(() => new Settings().ExistSetting("USER.Labelling.Config." + name + ".Data.File.Format"));
            result.Add(entry);
        }

        return result;
    }

    /// <summary>r12: imports a part file into the configured parts database, not in a transaction (r11).</summary>
    private static JObject ImportIntoConfigured(string file)
    {
        const string TestPart = "EPLANMCP.S24-TEST";
        var result = new JObject { ["file"] = file, ["selected"] = MDPartsManagement.SelectedPartsDatabaseAsString };
        MDPartsDatabase database = new MDPartsManagement().OpenDatabase();
        try
        {
            result["before"] = Try(() => new JObject { ["allParts"] = database.Parts.Length, ["exists"] = database.ExistsPart(TestPart) });
            result["imported"] = Try(() =>
            {
                var clock = Stopwatch.StartNew();
                database.ImportParts(file, "XPamImportXml", MDPartsDatabase.ImportMode.AppendNewRecords, false, MDPartsDatabase.ImportIdentifier.PartNumber, string.Empty);
                return new JObject
                {
                    ["seconds"] = Seconds(clock),
                    ["allParts"] = database.Parts.Length,
                    ["exists"] = database.ExistsPart(TestPart),
                    ["description"] = Text(database.GetPart(TestPart).Properties.ARTICLE_DESCR1),
                };
            });
            result["importedAgain"] = Try(() =>
            {
                database.ImportParts(file, "XPamImportXml", MDPartsDatabase.ImportMode.AppendNewRecords, false, MDPartsDatabase.ImportIdentifier.PartNumber, string.Empty);
                return new JObject { ["allParts"] = database.Parts.Length, ["variants"] = database.GetPartVariants(TestPart).Length };
            });
        }
        finally
        {
            database.Close();
        }

        return result;
    }

    /// <summary>r12: adds the current project's parts (the fixture copy under scratch) to the configured parts database.</summary>
    private static JObject SyncProjectParts()
    {
        var result = new JObject();
        using var locking = new Eplan.EplApi.DataModel.LockingStep();
        Eplan.EplApi.DataModel.Project project = new SelectionSet().GetCurrentProject(true);
        result["project"] = project.ProjectLinkFilePath;
        if (project.ProjectLinkFilePath.IndexOf(@"\EplanMcp\scratch\", StringComparison.OrdinalIgnoreCase) < 0)
        {
            result["refused"] = "the current project must be under the scratch root";
            return result;
        }

        result["projectPartsBefore"] = Try(() => project.Articles.Length);
        result["databasePartsBefore"] = Try(() => CountConfiguredParts());
        result["synchronized"] = Try(() =>
        {
            var clock = Stopwatch.StartNew();
            using (var synchronize = new Synchronize())
            {
                synchronize.Parts(project, Synchronize.StoreMode.AppendNew);
            }

            return new JObject { ["seconds"] = Seconds(clock) };
        });
        result["projectPartsAfter"] = Try(() => project.Articles.Length);
        result["databasePartsAfter"] = Try(() => CountConfiguredParts());
        return result;
    }

    private static int CountConfiguredParts()
    {
        MDPartsDatabase database = new MDPartsManagement().OpenDatabase();
        try
        {
            return database.Parts.Length;
        }
        finally
        {
            database.Close();
        }
    }

    /// <summary>r12: imports a tab-separated file into the configured translation database.</summary>
    private static JObject ImportTranslation(string file, string languages)
    {
        var result = new JObject { ["file"] = file, ["languages"] = languages };
        using var translate = new Translate();
        result["database"] = Try(() => translate.TranslateDatabase.ToString());
        var clock = Stopwatch.StartNew();
        result["returned"] = Try(() => translate.ImportDatabaseTexts(file, "XTrLanguageDbXml2TabConverterImpl", languages));
        result["seconds"] = Seconds(clock);
        return result;
    }

    /// <summary>r12: exports the configured translation database as tab-separated text (read only).</summary>
    private static JObject ExportTranslation(string file)
    {
        using var translate = new Translate();
        return new JObject { ["export"] = Timed(() => translate.ExportDatabaseTexts(file, "XTrLanguageDbXml2TabConverterImpl"), file) };
    }

    private static JToken Timed(Func<object?> run, string file) => Try(() =>
    {
        var clock = Stopwatch.StartNew();
        object? returned = run();
        return new JObject
        {
            ["returned"] = returned is null ? JValue.CreateNull() : JToken.FromObject(returned),
            ["seconds"] = Seconds(clock),
            ["bytes"] = File.Exists(file) ? new FileInfo(file).Length : -1,
        };
    });

    private static JToken Timed(System.Action run, string file) => Timed(() =>
    {
        run();
        return null;
    }, file);

    /// <summary>The backup ADR-0019 makes before every master-data write, of the configured database.</summary>
    private static Dictionary<string, string> MasterDataBackup(string type, string configured, string destination, string archive) => new()
    {
        ["TYPE"] = "MASTERDATA",
        ["MDTYPE"] = type,
        ["SOURCEPATH"] = Path.GetDirectoryName(configured) ?? string.Empty,
        ["FILENAME"] = Path.GetFileName(configured),
        ["DESTINATIONPATH"] = destination,
        ["ARCHIVENAME"] = archive,
    };

    /// <summary>
    /// Runs a built-in action the way the Bridge would, in process through <see cref="ActionManager"/> on the main
    /// thread, writing only to a folder under the scratch root, and lists that folder afterwards.
    /// </summary>
    private static JObject RunAction(string destination, string name, Dictionary<string, string> parameters)
    {
        var result = new JObject { ["action"] = name, ["parameters"] = JObject.FromObject(parameters) };
        if (destination.IndexOf(@"\EplanMcp\scratch\", StringComparison.OrdinalIgnoreCase) < 0)
        {
            result["refused"] = "the destination must be under the scratch root";
            return result;
        }

        Directory.CreateDirectory(destination);
        using var context = new ActionCallingContext();
        foreach (KeyValuePair<string, string> parameter in parameters)
        {
            context.AddParameter(parameter.Key, parameter.Value);
        }

        var clock = Stopwatch.StartNew();
        result["returned"] = Try(() => new ActionManager().FindAction(name).Execute(context));
        result["seconds"] = Seconds(clock);
        result["files"] = new JArray(Directory.EnumerateFiles(destination, "*", SearchOption.AllDirectories)
            .Select(f => f.Substring(destination.Length).TrimStart('\\') + " " + new FileInfo(f).Length));
        return result;
    }

    private static JObject Describe(MDPart part) => new()
    {
        ["partNr"] = part.PartNr,
        ["variant"] = part.Variant,
        ["type"] = Try(() => part.Type.ToString()),
        ["productGroup"] = Try(() => part.ProductGroup.ToString()),
        ["description"] = Text(part.Properties.ARTICLE_DESCR1),
        ["manufacturer"] = Text(part.Properties.ARTICLE_MANUFACTURER),
        ["typeNumber"] = Text(part.Properties.ARTICLE_TYPENR),
        ["orderNumber"] = Text(part.Properties.ARTICLE_ORDERNR),
        ["macro"] = Text(part.Properties.ARTICLE_MACRO),
        ["functionTemplates"] = Try(() => part.FunctionTemplatePositions.Length),
    };

    private static JToken FileState(string path) => Try(() =>
    {
        string? file = path.IndexOf('|') >= 0 ? null : path;
        return file is not null && File.Exists(file)
            ? new JObject { ["bytes"] = new FileInfo(file).Length, ["written"] = File.GetLastWriteTimeUtc(file).ToString("o", CultureInfo.InvariantCulture) }
            : new JObject { ["file"] = false };
    });

    private static string Text(MDPropertyValue value)
    {
        try
        {
            return value.ToString() ?? string.Empty;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    private static double Seconds(Stopwatch clock) => Math.Round(clock.Elapsed.TotalSeconds, 2);

    private static JToken Try(Func<JToken?> probe)
    {
        try
        {
            return probe() ?? JValue.CreateNull();
        }
        catch (Exception ex)
        {
            return Describe(ex);
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

        public string Part { get; set; } = string.Empty;

        public string Contains { get; set; } = string.Empty;

        public string Destination { get; set; } = string.Empty;
    }
}

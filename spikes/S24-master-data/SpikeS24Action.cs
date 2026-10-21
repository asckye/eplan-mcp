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
/// <c>backupparts</c>, <c>backuptranslation</c> and <c>restore</c> (archive in <c>/Part</c>) write only to
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

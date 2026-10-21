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
/// Spike S24, read part (M4c design 4.8, 4.9): which parts and translation databases EPLAN is configured with, and
/// reading parts from the parts database the way eplan_part_search and eplan_device_place would, on the main thread
/// through the dispatcher. Nothing is written: no database is created, changed, upgraded or selected, and the
/// configured databases are only read (their settings are never set, XML: that changes the global user setting).
/// <para>
/// Usage: <c>EplanMcpSpikeS24 /Mode:partsdb|translationdb /Report:"…\x.json" [/Part:number] [/Contains:text]</c>.
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
    }
}

using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Threading;
using Eplan.EplApi.ApplicationFramework;
using Eplan.EplApi.Base;
using Eplan.EplApi.Base.Enums;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.Graphics;
using Eplan.EplApi.HEServices;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Spikes.S34;

/// <summary>
/// S34: probe M4c actions, one candidate per SafetyPoint, on an already open scratch copy.
/// The scheduling action returns before the Dispatcher runs the probes (S32/S33).
/// The interlocked gate owns the worker and prevents re-entry through action message loops.
/// </summary>
public sealed partial class SpikeS34Action : IEplAction
{
    private const int ReportLimit = 290 * 1024;
    private static readonly string _scratchRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "EplanMcp", "scratch") + @"\";
    private static int _busy;

    public bool OnRegister(ref string Name, ref int Ordinal)
    {
        string assemblyName = typeof(SpikeS34Action).Assembly.GetName().Name ?? "EplanMcp.Spikes.S34";
        Name = assemblyName.Replace("EplanMcp.Spikes.", "EplanMcpSpike").Replace(".", string.Empty);
        Ordinal = 20;
        return true;
    }

    public void GetActionProperties(ref ActionProperties actionProperties)
    {
    }

    public bool Execute(ActionCallingContext oActionCallingContext)
    {
        string reportPath = string.Empty;
        bool ownsGate = false;
        try
        {
            reportPath = Parameter(oActionCallingContext, "Report");
            var p = new Params(reportPath, Parameter(oActionCallingContext, "Mode"), Parameter(oActionCallingContext, "Project"), Parameter(oActionCallingContext, "Part"));
            ownsGate = Interlocked.CompareExchange(ref _busy, 1, 0) == 0;
            if (!ownsGate)
            {
                throw new InvalidOperationException("S34 is already running; do not queue another mode.");
            }

            Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
            var thread = new Thread(() => RunLater(dispatcher, p)) { IsBackground = true };
            thread.Start();
            return true;
        }
        catch (Exception ex)
        {
            if (ownsGate)
            {
                Interlocked.Exchange(ref _busy, 0);
            }

            WriteReport(reportPath, new JObject { ["spike"] = "S34-m4c-actions", ["fatal"] = Describe(ex) });
            return false;
        }
    }

    private static void RunLater(Dispatcher dispatcher, Params p)
    {
        var report = new JObject
        {
            ["spike"] = "S34-m4c-actions", ["mode"] = p.Mode,
            ["revision"] = "r3", ["assembly"] = typeof(SpikeS34Action).Assembly.GetName().Name,
            ["timestampUtc"] = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
            ["quietMode"] = "ShowNoDialogs", ["engineerDialogObservation"] = JValue.CreateNull(),
        };
        try
        {
            Thread.Sleep(300);
            object? done = dispatcher.Invoke(DispatcherPriority.Normal, TimeSpan.FromSeconds(1800), new Func<bool>(() =>
            {
                // S1: never allow an exception to escape the main-thread delegate.
                try
                {
                    using var locking = new LockingStep();
                    if (MainWindowDisabled())
                    {
                        throw new InvalidOperationException("EPLAN has a modal dialog open; close it before running S34.");
                    }

                    using var quiet = new QuietModeStep(QuietModes.ShowNoDialogs);
                    Run(report, p);
                }
                catch (Exception ex)
                {
                    report["fatal"] = Describe(ex);
                }

                return true;
            }));
            if (done is null)
            {
                report["fatal"] = "The Dispatcher did not start the probes within 1800 s.";
            }
        }
        catch (Exception ex)
        {
            report["fatal"] = Describe(ex);
        }
        finally
        {
            // Release before publishing completion, so the driver can schedule the next mode immediately.
            Interlocked.Exchange(ref _busy, 0);
            WriteReport(p.Report, report);
        }
    }

    private static void Run(JObject report, Params p)
    {
        string path = ScratchPath(p.Project);
        using var manager = new ProjectManager();
        Project project = manager.OpenProjects.FirstOrDefault(o => string.Equals(o.ProjectLinkFilePath, path, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Open the scratch project copy in EPLAN before running S34.");
        ScratchPath(project.ProjectLinkFilePath);
        report["project"] = path;
        string folder = Path.Combine(_scratchRoot, "spikes", "s34", DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffffZ", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        report["outputFolder"] = folder;
        var baseline = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        Capture(report, "before", () => Counts(project));
        Capture(report, "projectFilesBefore", () =>
        {
            baseline = Files(path);
            return FileSummary(baseline, baseline, true);
        });
        var attempts = new JArray();
        report["attempts"] = attempts;

        // Action names/types and parameter names come from catalog/eplan-2026.0.3/actions.json.
        switch (p.Mode)
        {
            case "cables":
                Capture(report, "cableSchemes", () => CableSchemes(project));
                foreach (Dictionary<string, string> candidate in new[]
                {
                    Options(),
                    Options("CREATIONSCHEME", "", "NUMBERINGSCHEME", "", "AUTOSELECTSCHEME", "", "KEEPOLDNAMES", "1", "STARTVALUE", "1", "STEPVALUE", "1", "ONLYAUTOCABLES", "1"),
                    Options("KEEPOLDNAMES", "0", "STARTVALUE", "5", "STEPVALUE", "2", "ONLYAUTOCABLES", "0"),
                })
                {
                    Probe(attempts, project, path, baseline, folder, "generate", "CABLES", candidate);
                }

                break;
            case "devicelist":
                foreach (string? format in new string?[] { null, "XDLXmlExporter", "XDLCsvImporterExporter" })
                {
                    Dictionary<string, string> candidate = format is null ? Options() : Options("FORMAT", format);
                    RoundTrip(attempts, project, path, baseline, folder, "devicelist", format == "XDLCsvImporterExporter" ? ".csv" : ".xml", candidate, [Options()]);
                    SeededDeviceListRoundTrip(report, attempts, project, path, baseline, folder, format == "XDLCsvImporterExporter" ? ".csv" : ".xml", candidate);
                }

                break;
            case "partslist":
                // S25: XML only; CSV was followed by an access violation in an earlier spike.
                foreach (string? format in new string?[] { null, "XPalXmlExporter" })
                {
                    Dictionary<string, string> candidate = format is null ? Options() : Options("FORMAT", format);
                    RoundTrip(attempts, project, path, baseline, folder, "partslist", ".xml", candidate, [Options(), Options("MODE", "1"), Options("MODE", "2")]);
                }

                break;
            case "plc":
                Plc(report, attempts, project, path, baseline, folder);
                break;
            case "pdfcomments":
                try
                {
                    string source = Path.GetFullPath(p.Part);
                    if (!string.Equals(Path.GetExtension(source), ".pdf", StringComparison.OrdinalIgnoreCase) || !File.Exists(source))
                    {
                        throw new ArgumentException("/Part must name an existing PDF with comments exported from this project.");
                    }

                    string copy = Path.Combine(folder, "comments.pdf");
                    File.Copy(source, copy);
                    Probe(attempts, project, path, baseline, folder, "import", "PDFCOMMENTS", Options("IMPORTFILE", copy));
                }
                catch (Exception ex)
                {
                    attempts.Add(new JObject { ["action"] = "import", ["type"] = "PDFCOMMENTS", ["inputError"] = Describe(ex) });
                }

                break;
            default:
                throw new ArgumentException("Unknown mode; use cables, devicelist, partslist, plc or pdfcomments.");
        }

        Capture(report, "afterAllAttempts", () => Counts(project));
        Capture(report, "projectFilesAfterAllAttempts", () => FileSummary(Files(path), baseline, false));
        Capture(report, "outputFiles", () => OutputFiles(folder));
    }

    private static void Plc(JObject report, JArray attempts, Project project, string path, Dictionary<string, string> baseline, string folder)
    {
        string[] configurations = [];
        try
        {
            using var service = new PlcService();
            configurations = service.GetPLCConfigurationProjects(project).Distinct(StringComparer.Ordinal).OrderBy(s => s, StringComparer.Ordinal).ToArray();
            report["configurationProjects"] = new JArray(configurations.Take(12));
            report["configurationProjectCount"] = configurations.Length;
            report["configurationProjectsTested"] = new JArray(configurations.Take(2));
        }
        catch (Exception ex)
        {
            report["configurationDiscoveryError"] = Describe(ex);
        }

        // Empty is an explicit fallback probe, not an invented configuration name.
        foreach (string configuration in configurations.Length == 0 ? new[] { string.Empty } : configurations.Take(2))
        {
            foreach (Dictionary<string, string> candidate in new[]
            {
                Options("CONVERTERID", "PlcDcXMLExchangerUniversal"),
                Options("CONVERTERID", "PlcDcAMLExchangerGeneral"),
                Options("CONVERTERID", "PlcDcAMLExchangerGeneral", "FORMAT", "AutomationML AR APC V1.4.0"),
            })
            {
                candidate["LANGUAGE"] = "de_DE"; // The official plcservice examples use this language code.
                candidate["CONFIGURATIONPROJECT"] = configuration;
                candidate["OVERWRITE"] = "0";
                RoundTrip(attempts, project, path, baseline, folder, "plcservice", candidate["CONVERTERID"] == "PlcDcXMLExchangerUniversal" ? ".pbf" : ".aml", candidate, [Options("IMPORTMATCH", "0"), Options("IMPORTMATCH", "2"), Options()]);
            }
        }
    }

    private static void RoundTrip(JArray attempts, Project project, string path, Dictionary<string, string> baseline, string root, string action, string extension, Dictionary<string, string> candidate, Dictionary<string, string>[] importOptions)
    {
        try
        {
            string folder = Path.Combine(root, "export-" + attempts.Count.ToString("D2", CultureInfo.InvariantCulture));
            Directory.CreateDirectory(folder);
            string expected = Path.Combine(folder, "roundtrip" + extension);
            var export = new Dictionary<string, string>(candidate, StringComparer.Ordinal);
            export[action == "plcservice" ? "DESTINATIONFILE" : "EXPORTFILE"] = expected;
            JObject result = Probe(attempts, project, path, baseline, folder, action, action == "plcservice" ? "BUSDATAEXPORT" : "EXPORT", export);
            string? source = null;
            if (result.Value<bool?>("returned") == true && result["exception"] is null)
            {
                if (File.Exists(expected) && new FileInfo(expected).Length > 0)
                {
                    source = expected;
                }
                else
                {
                    // partslist documents extension addition. Never select an ambiguous or old file.
                    string[] produced = Directory.GetFiles(folder, "*", SearchOption.TopDirectoryOnly)
                        .Where(f => string.Equals(Path.GetExtension(f), extension, StringComparison.OrdinalIgnoreCase) && new FileInfo(f).Length > 0).ToArray();
                    if (produced.Length == 1)
                    {
                        source = produced[0];
                    }
                }
            }

            foreach (Dictionary<string, string> options in importOptions)
            {
                var import = new Dictionary<string, string>(candidate, StringComparer.Ordinal);
                if (action == "plcservice")
                {
                    import.Remove("CONFIGURATIONPROJECT");
                    import.Remove("OVERWRITE");
                    import.Remove("FORMAT"); // FORMAT is export-only for plcservice.
                }

                foreach (KeyValuePair<string, string> option in options)
                {
                    import[option.Key] = option.Value;
                }

                import[action == "plcservice" ? "SOURCEFILE" : "IMPORTFILE"] = source ?? expected;
                if (source is null)
                {
                    import["TYPE"] = action == "plcservice" ? "BUSDATAIMPORT" : "IMPORT";
                    import["PROJECTNAME"] = path;
                    attempts.Add(new JObject { ["action"] = action, ["parameters"] = JObject.FromObject(import), ["skipped"] = "Export failed or produced no unambiguous nonempty file in its new folder." });
                    continue;
                }

                JObject imported = Probe(attempts, project, path, baseline, folder, action, action == "plcservice" ? "BUSDATAIMPORT" : "IMPORT", import);
                imported["roundTripSource"] = source;
            }
        }
        catch (Exception ex)
        {
            attempts.Add(new JObject { ["action"] = action, ["parameters"] = JObject.FromObject(candidate), ["roundTripError"] = Describe(ex) });
        }
    }

    private static JObject Probe(JArray attempts, Project project, string path, Dictionary<string, string> baseline, string folder, string action, string type, Dictionary<string, string> candidate)
    {
        var parameters = new Dictionary<string, string>(candidate, StringComparer.Ordinal) { ["TYPE"] = type, ["PROJECTNAME"] = path };
        var result = new JObject { ["action"] = action, ["parameters"] = JObject.FromObject(parameters), ["returned"] = JValue.CreateNull(), ["elapsedMs"] = JValue.CreateNull() };
        attempts.Add(result); // Attach first so even setup or disposal failures leave a record.
        Capture(result, "before", () => Counts(project));
        Capture(result, "projectFilesBefore", () => FileSummary(Files(path), baseline, false));
        NameSnapshot? cableBefore = null;
        NameSnapshot? plcBefore = null;
        if (type == "CABLES")
        {
            Capture(result, "cablesBefore", () => { cableBefore = Names(project, true); return cableBefore.Report(200); });
        }

        if (type == "BUSDATAIMPORT")
        {
            Capture(result, "plcIdentityBefore", () => { plcBefore = Names(project, false); return plcBefore.Summary(); });
        }

        int bookmark = -1;
        Capture(result, "systemMessageBookmark", () => { bookmark = new SysMessagesCollection().Count; return new JValue(bookmark); });
        try
        {
            using (SafetyPoint safety = SafetyPoint.Create())
            {
                try
                {
                    ExecuteBuiltIn(result, action, parameters);
                }
                catch (Exception ex)
                {
                    result["exception"] = Describe(ex);
                }
                finally
                {
                    Capture(result, "after", () => Counts(project));
                    Capture(result, "projectFilesAfter", () => FileSummary(Files(path), baseline, false));
                    if (type == "CABLES")
                    {
                        CableStage(result, "After", project, cableBefore);
                    }

                    if (type == "BUSDATAIMPORT")
                    {
                        Capture(result, "plcImportChanges", () => PlcAdded(plcBefore, Names(project, false)));
                    }

                    var clock = Stopwatch.StartNew();
                    try
                    {
                        safety.Rollback();
                        result["rollbackReturned"] = true;
                    }
                    catch (Exception ex)
                    {
                        result["rollbackException"] = Describe(ex);
                    }
                    finally
                    {
                        result["rollbackMs"] = clock.Elapsed.TotalMilliseconds;
                        Capture(result, "afterRollback", () => Counts(project));
                        if (type == "CABLES")
                        {
                            CableStage(result, "AfterRollback", project, cableBefore);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            result["scopeException"] = Describe(ex);
        }
        finally
        {
            // A failed explicit rollback may be retried by Dispose; record that boundary separately.
            Capture(result, "afterSafetyPointDisposed", () => Counts(project));
            Capture(result, "projectFilesAfterRollback", () => FileSummary(Files(path), baseline, false));
            Capture(result, "files", () => OutputFiles(folder));
            Capture(result, "systemMessages", () => MessagesSince(bookmark));
            Capture(result, "mainWindowDisabledAfter", () => new JValue(MainWindowDisabled()));
        }

        return result;
    }

    private static JObject Counts(Project project)
    {
        using var finder = new DMObjectsFinder(project);
        var counts = new JObject();
        // Exact members checked in DataModel.jsonl, Graphics.jsonl and EObjects.jsonl.
        Capture(counts, "pages", () => new JValue(finder.GetPages(null).Length));
        Capture(counts, "functions", () => new JValue(finder.GetFunctions(null).Length));
        Capture(counts, "connections", () => new JValue(finder.GetConnections(null).Length));
        Capture(counts, "cables", () =>
        {
            using var filter = new FunctionsFilter { FunctionCategory = FunctionCategory.Cable };
            return new JValue(finder.GetFunctions(filter).Length);
        });
        Capture(counts, "plcs", () => new JValue(finder.GetPLCs(null).Length));
        Capture(counts, "plcTerminals", () => new JValue(finder.GetTerminals(null).Count(t => t.PLC is not null)));
        Capture(counts, "articles", () => new JValue(project.Articles.Length));
        Capture(counts, "articleReferences", () => new JValue(finder.GetArticleReferences(null).Length));
        Capture(counts, "planningSegments", () => new JValue(finder.GetPlanningSegments(null).Length));
        Capture(counts, "comments", () => new JValue(finder.GetAll<Comment>(true).Length));
        Capture(counts, "deviceListEntries", () => new JValue(DeviceListItems(project).Count));
        return counts;
    }

    private static void ExecuteBuiltIn(JObject result, string action, Dictionary<string, string> parameters)
    {
        try
        {
            using var context = new ActionCallingContext();
            foreach (KeyValuePair<string, string> parameter in parameters)
            {
                context.AddParameter(parameter.Key, parameter.Value);
            }

            if (MainWindowDisabled())
            {
                throw new InvalidOperationException("EPLAN's main window is disabled; refusing this action attempt.");
            }

            // S24: typed context only, never concatenate a built-in action command line.
            var found = new ActionManager().FindAction(action);
            var clock = Stopwatch.StartNew();
            try
            {
                result["returned"] = found.Execute(context);
            }
            finally
            {
                result["elapsedMs"] = clock.Elapsed.TotalMilliseconds;
            }
        }
        catch (Exception ex)
        {
            result["exception"] = Describe(ex);
        }
    }

    private static Dictionary<string, string> Files(string path)
    {
        string edb = Path.ChangeExtension(path, ".edb");
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string file in Directory.EnumerateFiles(edb, "*", SearchOption.AllDirectories).Where(f => !f.EndsWith(".lck", StringComparison.OrdinalIgnoreCase)))
        {
            string name = file.Substring(edb.Length).TrimStart('\\');
            try
            {
                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var sha = SHA256.Create();
                files[name] = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty);
            }
            catch (Exception ex)
            {
                files[name] = "!" + ex.GetType().Name + ": " + Cut(ex.Message, 200);
            }
        }

        return files;
    }

    private static JObject FileSummary(Dictionary<string, string> files, Dictionary<string, string> baseline, bool includeAll)
    {
        string[] changed = files.Keys.Union(baseline.Keys, StringComparer.OrdinalIgnoreCase)
            .Where(k => !files.TryGetValue(k, out string? a) || !baseline.TryGetValue(k, out string? b) || !string.Equals(a, b, StringComparison.Ordinal))
            .OrderBy(k => k, StringComparer.Ordinal).ToArray();
        string[] keys = (includeAll ? files.Keys.OrderBy(k => k, StringComparer.Ordinal) : changed.AsEnumerable()).ToArray();
        using var sha = SHA256.Create();
        string digest = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(string.Join("\n", files.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Key + "=" + kv.Value))))).Replace("-", string.Empty);
        int limit = includeAll ? 128 : 4;
        return new JObject
        {
            ["total"] = files.Count, ["hashErrors"] = files.Values.Count(v => v.StartsWith("!", StringComparison.Ordinal)),
            ["manifestSha256"] = digest, ["changedFromModeBefore"] = changed.Length,
            ["entriesOmitted"] = Math.Max(0, keys.Length - limit),
            ["entries"] = new JArray(keys.Take(limit).Select(k => new JObject { ["name"] = Cut(k, 240), ["sha256OrError"] = files.TryGetValue(k, out string? value) ? value : "deleted" })),
        };
    }

    private static JObject OutputFiles(string folder)
    {
        string[] files = Directory.GetFiles(folder, "*", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal).ToArray();
        var entries = new JArray();
        foreach (string file in files.Take(16))
        {
            try
            {
                entries.Add(new JObject { ["name"] = Cut(file.Substring(folder.Length).TrimStart('\\'), 240), ["bytes"] = new FileInfo(file).Length });
            }
            catch (Exception ex)
            {
                entries.Add(new JObject { ["error"] = Describe(ex) });
            }
        }

        return new JObject { ["total"] = files.Length, ["entriesOmitted"] = Math.Max(0, files.Length - 16), ["entries"] = entries };
    }

    private static JObject MessagesSince(int before)
    {
        if (before < 0)
        {
            return new JObject { ["unavailable"] = "No before bookmark." };
        }

        // A11: never retain messages or read an old collection after creating another collection.
        var messages = new SysMessagesCollection();
        var entries = new JArray();
        int index = 0;
        foreach (object item in messages)
        {
            if (index++ >= before && entries.Count < 6 && item is BaseException message)
            {
                entries.Add(new JObject { ["level"] = (int)message.MessageLevel, ["text"] = Cut(message.GetMessageText() ?? string.Empty, 500) });
            }
        }

        return new JObject { ["beforeCount"] = before, ["afterCount"] = messages.Count, ["entries"] = entries, ["entriesOmitted"] = Math.Max(0, messages.Count - before - entries.Count) };
    }

    private static void Capture(JObject target, string name, Func<JToken> probe)
    {
        try
        {
            target[name] = probe();
        }
        catch (Exception ex)
        {
            target[name] = new JObject { ["error"] = Describe(ex) };
        }
    }

    private static void WriteReport(string path, JObject report)
    {
        try
        {
            // Missing licences can make the same count fail at every stage of every attempt.
            // Store repeated exception details once instead of losing attempts to the report budget.
            var errors = new JObject();
            JObject[] descriptions = report.Descendants().OfType<JObject>().Where(o => o["type"] is JValue && o["message"] is JValue).ToArray();
            foreach (IGrouping<string, JObject> group in descriptions.GroupBy(o => o.ToString(Formatting.None), StringComparer.Ordinal).Where(g => g.Count() > 1))
            {
                string key = "E" + errors.Count.ToString(CultureInfo.InvariantCulture);
                errors[key] = group.First().DeepClone();
                foreach (JObject description in group)
                {
                    description.Replace(new JObject { ["errorRef"] = key });
                }
            }

            if (errors.Count > 0)
            {
                report["exceptionDefinitions"] = errors;
            }

            string json = report.ToString(Formatting.None);
            // Keep every attempt and its parameters/result; trim only diagnostic arrays if needed.
            while (Encoding.UTF8.GetByteCount(json) > ReportLimit)
            {
                JArray? largest = report.Descendants().OfType<JArray>().Where(a => a.Parent is JProperty property && property.Name != "attempts" && property.Name != "deviceListFixtures" && property.Name != "creationAttempts" && property.Name != "deletions" && a.Count > 0).OrderByDescending(a => a.ToString(Formatting.None).Length).FirstOrDefault();
                if (largest is null)
                {
                    throw new InvalidOperationException("S34 report exceeded its size limit after diagnostic list truncation.");
                }

                largest.RemoveAt(largest.Count - 1);
                if (largest.Parent?.Parent is JObject owner && owner["entriesOmitted"] is JValue omitted)
                {
                    owner["entriesOmitted"] = omitted.Value<int>() + 1;
                }

                report["additionalListTruncation"] = true;
                json = report.ToString(Formatting.None);
            }

            string fullPath = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            string partial = fullPath + ".partial";
            File.WriteAllText(partial, json, new UTF8Encoding(false));
            if (File.Exists(fullPath))
            {
                File.Replace(partial, fullPath, null);
            }
            else
            {
                File.Move(partial, fullPath);
            }
        }
        catch (Exception ex)
        {
            // Last boundary: neither a background thread nor an add-in entry may terminate EPLAN.
            Trace.WriteLine("S34 report write failed: " + Describe(ex).ToString(Formatting.None));
        }
    }

    private static string ScratchPath(string path)
    {
        string full = Path.GetFullPath(path);
        if (!full.StartsWith(_scratchRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("S34 accepts only project copies under %USERPROFILE%\\EplanMcp\\scratch\\.");
        }

        return full;
    }

    private static bool MainWindowDisabled()
    {
        using var process = Process.GetCurrentProcess();
        IntPtr window = process.MainWindowHandle;
        return window != IntPtr.Zero && !IsWindowEnabled(window);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowEnabled(IntPtr window);

    private static Dictionary<string, string> Options(params string[] pairs)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < pairs.Length; i += 2)
        {
            result.Add(pairs[i], pairs[i + 1]);
        }

        return result;
    }

    private static JObject Describe(Exception ex) => new() { ["type"] = ex.GetType().FullName, ["message"] = Cut(ex.Message, 700), ["inner"] = Cut(ex.InnerException?.Message ?? string.Empty, 300) };

    private static string Cut(string value, int length) => value.Length <= length ? value : value.Substring(0, length) + "...";

    private static string Parameter(ActionCallingContext context, string name)
    {
        string value = string.Empty;
        context.GetParameter(name, ref value);
        if (value?.Length > 1024)
        {
            throw new ArgumentException("S34 parameter exceeds 1024 characters: " + name);
        }

        return value ?? string.Empty;
    }

    private sealed class Params(string report, string mode, string project, string part)
    {
        public string Report { get; } = report;

        public string Mode { get; } = mode;

        public string Project { get; } = project;

        public string Part { get; } = part;
    }
}

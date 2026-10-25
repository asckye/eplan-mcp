using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Threading;
using Eplan.EplApi.ApplicationFramework;
using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.MasterData;
using Eplan.EplApi.HEServices;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Numeration = Eplan.EplApi.DataModel.MasterData.PageMacro.Enums.NumerationMode;

namespace EplanMcp.Spikes.S37;

/// <summary>Scratch-only content growth experiment. EPLAN objects never leave a dispatched batch.</summary>
public sealed class SpikeS37Action : IEplAction
{
    private static int _running;

    public bool OnRegister(ref string Name, ref int Ordinal)
    {
        Name = (typeof(SpikeS37Action).Assembly.GetName().Name ?? "EplanMcp.Spikes.S37")
            .Replace("EplanMcp.Spikes.", "EplanMcpSpike").Replace(".", string.Empty);
        Ordinal = 20;
        return true;
    }

    public void GetActionProperties(ref ActionProperties actionProperties) { }

    public bool Execute(ActionCallingContext oActionCallingContext)
    {
        // Validate the marker before accepting work; an invalid marker must never be written even for a refusal.
        Options? options = null;
        try
        {
            options = new Options(oActionCallingContext);
            if (Interlocked.CompareExchange(ref _running, 1, 0) != 0) { return false; }
            Dispatcher main = Dispatcher.CurrentDispatcher; // Same action-thread capture as S32.
            var worker = new Thread(() => Run(main, options)) { IsBackground = true, Name = "S37" };
            try { worker.Start(); }
            catch { Interlocked.Exchange(ref _running, 0); throw; }
            return true;
        }
        catch (Exception ex)
        {
            options?.WriteFailure("rejected", ex);
            return false;
        }
    }

    private static void Run(Dispatcher dispatcher, Options p)
    {
        try
        {
            Thread.Sleep(300); // Let the Remote Client action return before entering the dispatcher (S32).
            p.Validate();
            p.Write("started", new JObject { ["parameters"] = p.Parameters, ["actionAssembly"] = typeof(SpikeS37Action).Assembly.FullName,
                ["numerationModes"] = new JArray(Enum.GetNames(typeof(Numeration))) }); // Reflection only lists the enum.
            string[] sources = [];
            int initialPages = 0;
            Dispatch(dispatcher, () => WithProject(p, project =>
            {
                using var quiet = new QuietModeStep(QuietModes.ShowNoDialogs);
                Measurement baseline = Measurement.Read(project);
                initialPages = (int)baseline.Data["pages"]!;
                string[] available = ((JArray)baseline.Data["pageDetails"]!).Select(x => (string)x["name"]!).ToArray();
                sources = p.Pages.Length == 0 ? available : p.Pages;
                if (sources.Length == 0 || sources.Distinct(StringComparer.OrdinalIgnoreCase).Count() != sources.Length
                    || sources.Except(available, StringComparer.Ordinal).Any())
                { throw new ArgumentException("Pages must be a nonempty, unique list of existing full page names."); }
                using var process = Process.GetCurrentProcess();
                using var license = new License();
                p.Write("count", new JObject { ["snapshot"] = baseline.Data, ["projectBytes"] = ScratchPaths.ProjectBytes(p.Project),
                    ["current"] = IsCurrent(project), ["eplanBuild"] = process.MainModule?.FileVersionInfo.FileVersion,
                    ["windowTitle"] = process.MainWindowTitle, ["sourcePages"] = new JArray(sources),
                    ["licence"] = new JObject { ["APIExtension"] = license.HasOption(LicenseOptions.APIExtension),
                        ["ImportAndInsertMacros"] = license.HasOption(LicenseOptions.ImportAndInsertMacros),
                        ["PagesUnlimited"] = license.HasOption(LicenseOptions.PagesUnlimited), ["ProPanel"] = license.HasOption(LicenseOptions.ProPanel) } });
            }));
            if (p.Mode != "count")
            {
                int needed = checked(sources.Length * p.Copies);
                if (p.TargetPages != 0)
                {
                    if (p.TargetPages <= initialPages || p.TargetPages - initialPages > needed)
                    { throw new ArgumentException("TargetPages must exceed the current count and fit within Copies times the source-page count."); }
                    needed = p.TargetPages - initialPages;
                }

                if (p.Rollback || p.Mode == "undo") { needed = Math.Min(needed, Math.Min(p.BatchSize, sources.Length)); }
                if (initialPages + needed > 10000) { throw new ArgumentException("This spike stops at 10000 pages; specify TargetPages to truncate the last copy."); }
                HashSet<string>? protectedNames = null;
                int previousCopy = -1;
                int batch = 0;
                for (int offset = 0; offset < needed;)
                {
                    p.CheckCancellation();
                    int copy = offset / sources.Length;
                    int take = Math.Min(Math.Min(p.BatchSize, sources.Length - offset % sources.Length), needed - offset);
                    string[] selected = sources.Skip(offset % sources.Length).Take(take).ToArray();
                    string group = p.Prefix + (p.Start + copy).ToString(CultureInfo.InvariantCulture);
                    if (copy != previousCopy) { protectedNames = null; previousCopy = copy; }
                    int number = ++batch;
                    Dispatch(dispatcher, () => WithProject(p, project => Batch(project, p, selected, group, number, ref protectedNames)));
                    offset += take;
                    Thread.Sleep(50); // Release the main thread and all EPLAN wrappers between batches.
                }

                Dispatch(dispatcher, () => WithProject(p, project =>
                {
                    Measurement final = Measurement.Read(project);
                    p.Write("final_count", new JObject { ["snapshot"] = final.Data, ["projectBytes"] = ScratchPaths.ProjectBytes(p.Project) });
                    int expected = initialPages + (p.Rollback ? 0 : needed);
                    if ((int)final.Data["pages"]! != expected) { throw new InvalidOperationException("Final page count differs from the intended growth."); }
                }));
            }

            p.Write("completed", new JObject { ["ok"] = true });
        }
        catch (Exception ex)
        {
            // A dispatcher start timeout does not cancel an API call already running; never retry automatically.
            p.WriteFailure("completed", ex);
        }
        finally { Interlocked.Exchange(ref _running, 0); }
    }

    private static void Dispatch(Dispatcher dispatcher, System.Action action)
    {
        Exception? failure = null;
        object? ran = dispatcher.Invoke(DispatcherPriority.Background, TimeSpan.FromSeconds(60), new Func<bool>(() =>
        {
            try
            {
                using var locking = new LockingStep(); // First EPLAN API object in every dispatch (A9).
                using var process = Process.GetCurrentProcess();
                if (process.MainWindowHandle != IntPtr.Zero && !IsWindowEnabled(process.MainWindowHandle))
                { throw new InvalidOperationException("EPLAN has a modal dialog; no work was started."); }
                action();
            }
            catch (Exception ex) { failure = ex; } // Never let exceptions escape into the WPF dispatcher (S1).
            return true;
        }));
        if (ran is null) { throw new TimeoutException("Main-thread start timed out; do not retry without inspecting the marker."); }
        if (failure is not null) { throw new InvalidOperationException("Dispatched operation failed.", failure); }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowEnabled(IntPtr hWnd);

    private static void WithProject(Options p, System.Action<Project> work)
    {
        string path = ScratchPaths.Require(p.Project, ".elk");
        ScratchPaths.ProjectBytes(path); // Also refuse nested database links before any write.
        using var manager = new ProjectManager();
        Project project = manager.OpenProjects.SingleOrDefault(pr => string.Equals(
            Path.GetFullPath(pr.ProjectLinkFilePath), path, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("The requested scratch project must already be open.");
        ScratchPaths.Require(project.ProjectLinkFilePath, ".elk");
        if (p.Mode == "undo" && !IsCurrent(project))
        { throw new InvalidOperationException("Undo probe requires the scratch project to be CURRENT (A41)."); }
        work(project); // Returned Project/Page/Function/Placement/Connection objects are never disposed (A39).
    }

    private static bool IsCurrent(Project project) => string.Equals(new SelectionSet().GetCurrentProject(false)?.ProjectLinkFilePath,
        project.ProjectLinkFilePath, StringComparison.OrdinalIgnoreCase);

    private static void Batch(Project project, Options p, string[] sources, string group, int number, ref HashSet<string>? protectedNames)
    {
        var total = Stopwatch.StartNew();
        Measurement before = Measurement.Read(project);
        protectedNames ??= before.Names(); // Names predating this copy, including earlier copies, are protected.
        HashSet<string> protectedSet = protectedNames;
        JObject messages = Measurement.Messages();
        long bytesBefore = ScratchPaths.ProjectBytes(p.Project);
        var report = new JObject { ["batch"] = number, ["group"] = group, ["sources"] = new JArray(sources), ["before"] = before.Summary() };
        p.Write("batch_started", new JObject { ["batch"] = number, ["group"] = group, ["sources"] = new JArray(sources) });
        Exception? failure = null;
        bool committed = false;
        var mutation = new Stopwatch();
        try
        {
            using var finder = new DMObjectsFinder(project);
            Dictionary<string, Page> pages = finder.GetPages(null).ToDictionary(pg => pg.Name, StringComparer.Ordinal);
            using SafetyPoint safety = SafetyPoint.Create();
            using var undoManager = new UndoManager();
            UndoStep undo = undoManager.CreateUndoStep(); // Manager owns the step, matching Bridge TransactionScope.
            undo.SetUndoDescription("S37 " + p.Mode + " " + group + " batch " + number.ToString(CultureInfo.InvariantCulture));
            try
            {
                using (new QuietModeStep(QuietModes.ShowNoDialogs))
                {
                    mutation.Start();
                    Page[] created = p.Mode == "pagemacro"
                        ? InsertMacro(project, p, sources.Select(s => pages[s]).ToArray(), group, number)
                        : sources.Select(source => CopyPage(project, p, pages[source], group)).ToArray();
                    mutation.Stop();
                    report["mutationMs"] = mutation.Elapsed.TotalMilliseconds;
                    report["copiedPages"] = new JArray(created.Select(pg => pg.Name));
                    Measurement inside = Measurement.Read(project);
                    report["insideBeforeGenerate"] = inside.Summary();
                    if (p.GenerateConnections)
                    {
                        using var generate = new Generate();
                        var generateClock = Stopwatch.StartNew();
                        generate.Connections(created, false);
                        report["generateConnectionsMs"] = generateClock.Elapsed.TotalMilliseconds;
                        inside = Measurement.Read(project);
                    }

                    report["inside"] = inside.Summary();
                    report["copiedPageDetails"] = new JArray(((JArray)inside.Data["pageDetails"]!).Where(x => created.Any(pg => pg.Name == (string)x["name"]!)).Select(x => x.DeepClone()));
                    JObject[] added = inside.Functions.Where(f => !before.Functions.ContainsKey(f.Key)).Select(f => f.Value).ToArray();
                    HashSet<string> occupied = before.Names();
                    JObject[] collisions = added.Where(f => occupied.Contains((string)f["name"]!)).ToArray();
                    report["newFunctions"] = added.Length;
                    report["newStandardObjects"] = (long)inside.Data["standardObjects"]! - (long)before.Data["standardObjects"]!;
                    report["newPlacements"] = (long)inside.Data["placements"]! - (long)before.Data["placements"]!;
                    report["newConnections"] = (long)inside.Data["connections"]! - (long)before.Data["connections"]!;
                    report["collidingNewFunctions"] = collisions.Length;
                    report["collisionNameSamples"] = new JArray(collisions.Select(f => (string)f["name"]!).Distinct(StringComparer.OrdinalIgnoreCase).Take(30));
                    int unsafeCollisions = added.Count(f => protectedSet.Contains((string)f["name"]!));
                    report["collisionsOutsideCopy"] = unsafeCollisions;
                    report["collisionNameCounts"] = new JObject(added.Where(f => protectedSet.Contains((string)f["name"]!))
                        .GroupBy(f => (string)f["name"]!, StringComparer.Ordinal).OrderByDescending(g => g.Count()).Take(100)
                        .Select(g => new JProperty(g.Key, g.Count())));
                    int changedExisting = before.Functions.Count(f => !inside.Functions.TryGetValue(f.Key, out JObject? current) || !JToken.DeepEquals(f.Value, current));
                    report["changedOrMissingExistingFunctions"] = changedExisting;
                    report["pagesCreated"] = (int)inside.Data["pages"]! - (int)before.Data["pages"]!;
                    if ((unsafeCollisions != 0 && !p.AllowCollisions) || changedExisting != 0 || (int)report["pagesCreated"]! != sources.Length)
                    { throw new InvalidOperationException("Copy crossed a protected function-name domain, changed existing functions, or created an unexpected page count."); }
                    // Cross-page auxiliaries within this copy may share names. Density loss is still a failed copy.
                    foreach (Page page in created)
                    {
                        JObject actual = ((JArray)inside.Data["pageDetails"]!).OfType<JObject>().Single(x => (string)x["name"]! == page.Name);
                        string source = sources[Array.IndexOf(created, page)];
                        JObject original = ((JArray)before.Data["pageDetails"]!).OfType<JObject>().Single(x => (string)x["name"]! == source);
                        if ((int)actual["functions"]! < (int)original["functions"]! || (int)actual["placements"]! < (int)original["placements"]!)
                        { throw new InvalidOperationException("Content density decreased on copied page " + page.Name); }
                    }
                }

                p.CheckCancellation();
                if (p.Rollback) { safety.Rollback(); }
                else { safety.Commit(); committed = true; undo.CloseUndo(); }
            }
            catch (Exception ex)
            {
                mutation.Stop();
                try
                {
                    Measurement failed = Measurement.Read(project);
                    report["insideAtFailure"] = failed.Summary();
                    report["pagesCreated"] = (int)failed.Data["pages"]! - (int)before.Data["pages"]!;
                    report["newFunctions"] = failed.Functions.Count(f => !before.Functions.ContainsKey(f.Key));
                    report["newStandardObjects"] = (long)failed.Data["standardObjects"]! - (long)before.Data["standardObjects"]!;
                }
                catch (Exception measurementError) { report["failureMeasurementError"] = measurementError.ToString(); }
                if (!committed)
                {
                    try { safety.Rollback(); }
                    catch (Exception rollbackError) { throw new AggregateException("Batch failed and rollback also failed.", ex, rollbackError); }
                }
                throw;
            }
        }
        catch (Exception ex) { mutation.Stop(); failure = ex; report["error"] = ex.ToString(); }
        finally
        {
            report["committed"] = committed;
            report["mutationMs"] = mutation.Elapsed.TotalMilliseconds;
            try
            {
                Measurement after = Measurement.Read(project);
                report["after"] = after.Summary();
                report["copiesDisappeared"] = !committed && JToken.DeepEquals(before.Data["pageDetails"], after.Data["pageDetails"])
                    && before.Functions.Count == after.Functions.Count && before.Functions.All(f => after.Functions.TryGetValue(f.Key, out JObject? current) && JToken.DeepEquals(f.Value, current));
                report["projectBytesBefore"] = bytesBefore;
                long bytesAfter = ScratchPaths.ProjectBytes(p.Project);
                report["projectBytesAfter"] = bytesAfter;
                report["projectGrowthBytes"] = bytesAfter - bytesBefore;
                JObject afterMessages = Measurement.Messages();
                report["systemMessagesAccessible"] = messages["unavailable"] is null && afterMessages["unavailable"] is null;
                report["systemMessageReadErrors"] = new JArray(new[] { messages["unavailable"], afterMessages["unavailable"] }
                    .Where(x => x is not null).Select(x => x!.DeepClone()));
                report["systemMessages"] = new JObject(afterMessages.Properties().Where(m => !JToken.DeepEquals(messages[m.Name], m.Value)).Select(m => new JProperty(m.Name, m.Value.DeepClone())));
                int retained = (int)after.Data["pages"]! - (int)before.Data["pages"]!;
                if (retained > 0) { report["growthBytesPer1000Pages"] = (bytesAfter - bytesBefore) * 1000.0 / retained; }
            }
            catch (Exception ex) { report["verificationError"] = ex.ToString(); failure ??= ex; }
            finally
            {
                report["totalMs"] = total.Elapsed.TotalMilliseconds;
                if ((int?)report["pagesCreated"] is > 0)
                {
                    report["mutationMsPer1000Pages"] = mutation.Elapsed.TotalMilliseconds * 1000 / (int)report["pagesCreated"]!;
                    report["totalMsPer1000Pages"] = total.Elapsed.TotalMilliseconds * 1000 / (int)report["pagesCreated"]!;
                }
                p.Write("batch", report);
            }
        }

        if (failure is not null) { throw new InvalidOperationException("Batch failed; later batches were not attempted.", failure); }
        if (!committed && !(bool)report["copiesDisappeared"]!) { throw new InvalidOperationException("Rollback verification failed; inspect this scratch copy."); }
    }

    private static Page CopyPage(Project project, Options p, Page source, string group)
    {
        string expected = string.Empty;
        try
        {
            p.CheckCancellation();
            using PagePropertyList parts = TargetParts(source, group, out expected);
            var apiClock = Stopwatch.StartNew();
            Page copy = source.CopyTo(project, parts, p.Numeration, bOverwrite: false, bDontUseProgressBar: true)
                ?? throw new InvalidOperationException("CopyTo returned null (possibly an occupied page name).");
            apiClock.Stop();
            if (!string.Equals(copy.Name, expected, StringComparison.Ordinal))
            { throw new InvalidOperationException("Requested page name was normalized or a structure part was dropped: " + expected + " -> " + copy.Name); }
            p.Write("page_copied", new JObject { ["source"] = source.Name, ["target"] = copy.Name, ["copyApiMs"] = apiClock.Elapsed.TotalMilliseconds });
            return copy;
        }
        catch (Exception ex)
        {
            p.Write("page_error", new JObject { ["source"] = source.Name, ["target"] = expected, ["error"] = ex.ToString() });
            throw;
        }
    }

    private static Page[] InsertMacro(Project project, Options p, Page[] sources, string group, int number)
    {
        string file = ScratchPaths.Require(Path.Combine(p.MacroDirectory, "batch-" + number.ToString(CultureInfo.InvariantCulture) + ".emp"), ".emp");
        if (File.Exists(file)) { throw new IOException("Refusing to overwrite an existing macro."); }
        Directory.CreateDirectory(p.MacroDirectory);
        using var description = new MultiLangString();
        description.AddString(ISOCode.Language.L___, "S37 sample pages");
        var apiClock = Stopwatch.StartNew();
        using (var export = new PageMacro()) { export.Create(file, sources, description); }
        apiClock.Stop();
        p.Write("macro_created", new JObject { ["file"] = file, ["bytes"] = new FileInfo(file).Length, ["createApiMs"] = apiClock.Elapsed.TotalMilliseconds });
        using var macro = new PageMacro();
        macro.Open(file, project);
        Page[] macroPages = macro.Pages;
        var sourceIds = new HashSet<string>(sources.Select(pg => pg.ToStringIdentifier()), StringComparer.Ordinal);
        if (macroPages.Any(pg => sourceIds.Contains(pg.ToStringIdentifier())))
        { throw new InvalidOperationException("Macro returned live source page identities; refusing to rename them."); }
        var expected = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Page source in sources)
        {
            try
            {
                p.CheckCancellation();
                using PagePropertyList parts = TargetParts(source, group, out string name);
                Page page = macroPages.Single(pg => pg.Name == source.Name);
                page.Name = name; // Documented by the bool[] Insert.PageMacro overload; do not rename source pages.
                if (!string.Equals(page.Name, name, StringComparison.Ordinal)) { throw new InvalidOperationException("Macro page name did not round-trip."); }
                expected.Add(source.Name, name);
            }
            catch (Exception ex) { p.Write("page_error", new JObject { ["source"] = source.Name, ["error"] = ex.ToString() }); throw; }
        }

        p.CheckCancellation();
        using var insert = new Insert();
        // All false: no page can be overwritten. No reflection invocation or guessed overloads.
        apiClock.Restart();
        StorableObject[] inserted = insert.PageMacro(macro, project, new bool[macroPages.Length], p.Numeration);
        apiClock.Stop();
        p.Write("macro_inserted", new JObject { ["file"] = file, ["insertApiMs"] = apiClock.Elapsed.TotalMilliseconds, ["returnedObjects"] = inserted.Length });
        Page[] created = inserted.OfType<Page>().ToArray();
        if (created.Length != sources.Length || !new HashSet<string>(created.Select(pg => pg.Name), StringComparer.Ordinal).SetEquals(expected.Values))
        { throw new InvalidOperationException("Inserted macro page names/count differ from the requested names."); }
        return sources.Select(source => created.Single(pg => pg.Name == expected[source.Name])).ToArray();
    }

    private static PagePropertyList TargetParts(Page source, string group, out string expected)
    {
        using PagePropertyList original = source.NameParts; // Fetch once, dispose once (A36).
        string value;
        try { using PropertyValue plant = original.DESIGNATION_PLANT; value = plant.IsEmpty ? string.Empty : plant.ToString() ?? string.Empty; }
        catch (PropertyNotFoundException) { value = string.Empty; }
        string place;
        try { using PropertyValue location = original.DESIGNATION_LOCATION; place = location.IsEmpty ? string.Empty : location.ToString() ?? string.Empty; }
        catch (PropertyNotFoundException) { place = string.Empty; }
        expected = FixtureNames.Target(source.Name, value, place, group);
        var target = new PagePropertyList();
        try
        {
            original.CopyTo(target, true); // Preserve every other name part, including subcounters and nested structures.
            target.DESIGNATION_PLANT = group + (value.Length == 0 ? string.Empty : "." + value);
            target.DESIGNATION_LOCATION = group + (place.Length == 0 ? string.Empty : "." + place);
            return target;
        }
        catch { target.Dispose(); throw; }
    }

    private sealed class Options
    {
        internal Options(ActionCallingContext context)
        {
            foreach (string name in new[] { "Mode", "Project", "Marker", "Pages", "Copies", "Prefix", "Start", "BatchSize", "NumerationMode", "Rollback", "TargetPages", "GenerateConnections", "AllowCollisions", "RunId" })
            {
                string value = string.Empty;
                context.GetParameter(name, ref value);
                Parameters[name] = value;
            }

            Marker = ScratchPaths.Require(Get("Marker"), ".jsonl");
            Directory.CreateDirectory(Path.GetDirectoryName(Marker)!);
            RunId = Get("RunId").Length == 0 ? Guid.NewGuid().ToString("N") : Get("RunId");
        }

        internal JObject Parameters { get; } = new();
        internal string Marker { get; }
        internal string RunId { get; }
        internal string Project { get; private set; } = string.Empty;
        internal string Mode => Get("Mode");
        internal string Prefix => Get("Prefix").Length == 0 ? "S37C" : Get("Prefix");
        internal string[] Pages => Get("Pages").Length == 0 ? [] : Get("Pages").Split(';');
        internal int Copies => Number("Copies", 1, 1, 100);
        internal int Start => Number("Start", 1, 1, 100000);
        internal int BatchSize => Number("BatchSize", 25, 1, 50);
        internal int TargetPages => Number("TargetPages", 0, 0, 10000);
        internal bool Rollback => Number("Rollback", 0, 0, 1) == 1;
        internal bool GenerateConnections => Number("GenerateConnections", 0, 0, 1) == 1;
        // Records, but tolerates, new functions that join existing names (unnamed functions, explicitly located cables).
        internal bool AllowCollisions => Number("AllowCollisions", 0, 0, 1) == 1;
        internal Numeration Numeration { get; private set; }
        internal string MacroDirectory => Path.Combine(Path.GetDirectoryName(Marker)!, "macros-" + RunId);

        internal void Validate()
        {
            Project = ScratchPaths.Require(Get("Project"), ".elk");
            if (Mode is not ("count" or "copypages" or "pagemacro" or "undo")) { throw new ArgumentException("Unknown Mode."); }
            if (!Regex.IsMatch(Prefix, "^[A-Za-z][A-Za-z0-9_]{0,19}$", RegexOptions.CultureInvariant)
                || !Regex.IsMatch(RunId, "^[A-Za-z0-9_-]{1,80}$", RegexOptions.CultureInvariant))
            { throw new ArgumentException("Invalid Prefix or RunId."); }
            Numeration = Get("NumerationMode") switch
            {
                "" or "Number" => Numeration.Number, "None" => Numeration.None, "Ignore" => Numeration.Ignore,
                "NumberPreferPrefix" => Numeration.NumberPreferPrefix, "NumberWithQuestionMark" => Numeration.NumberWithQuestionMark,
                _ => throw new ArgumentException("Use a listed NumerationMode name, not a numeric value."),
            };
            _ = Copies; _ = Start; _ = BatchSize; _ = TargetPages; _ = Rollback; _ = GenerateConnections; _ = AllowCollisions;
        }

        internal void CheckCancellation()
        {
            if (File.Exists(Marker + ".cancel")) { throw new System.OperationCanceledException("Scratch cancellation marker found."); }
        }

        internal void WriteFailure(string kind, Exception error)
        {
            try { Write(kind, new JObject { ["ok"] = false, ["error"] = error.ToString() }); }
            catch (Exception loggingError) { Trace.WriteLine("S37 marker unavailable: " + loggingError); }
        }

        internal void Write(string kind, JObject data)
        {
            ScratchPaths.Require(Marker, ".jsonl");
            data["event"] = kind; data["runId"] = RunId; data["mode"] = Mode;
            data["utc"] = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            using var stream = new FileStream(Marker, FileMode.Append, FileAccess.Write, FileShare.Read);
            using var writer = new StreamWriter(stream, new UTF8Encoding(false));
            writer.WriteLine(data.ToString(Formatting.None)); writer.Flush(); stream.Flush(true);
        }

        private string Get(string name) => (string?)Parameters[name] ?? string.Empty;

        private int Number(string name, int fallback, int min, int max) => Get(name).Length == 0 ? fallback
            : int.TryParse(Get(name), NumberStyles.None, CultureInfo.InvariantCulture, out int value) && value >= min && value <= max
                ? value : throw new ArgumentException("Invalid numeric parameter " + name);
    }
}

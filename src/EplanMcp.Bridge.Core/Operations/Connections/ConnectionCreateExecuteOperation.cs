using EplanMcp.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>O3/A52 wires use two steps; D5 interruptions and A54 moves keep one undo step open through readback.</summary>
public sealed class ConnectionCreateExecuteOperation(IConnectionCreator creator, IConnectionGenerator generator, ITransactionScopeFactory transactions, IPlacementMover? mover = null)
    : Operation<JObject, JObject>, IWriteOperation
{
    public override string Name => OperationNames.ConnectionCreateExecute;
    protected override JObject Execute(JObject args)
    {
        JObject input = args["request"] as JObject ?? throw WriteArguments.NotStarted(WriteArguments.Invalid(["/request: supply the planned request."]));
        string fingerprint = (string?)args["fingerprint"] ?? throw WriteArguments.NotStarted(WriteArguments.Invalid(["/fingerprint: supply the plan fingerprint."]));
        if (input.ContainsKey("moves") || input.ContainsKey("property_moves"))
        { return PlacementMoveExecution.Execute(mover ?? throw WriteArguments.NotStarted(new BridgeErrorException(ErrorReason.CapabilityMissing, "Placement moving is unavailable in this Bridge.")), transactions, input, fingerprint); }
        return new Execution(creator, generator, transactions, input, fingerprint).Run();
    }

    /// <summary>
    /// One execution: the plan is made again from the page as it is now, then placed and generated on one of four routes
    /// (placement only, with definition points, with interruption points, or wires in two undo steps), each reading back
    /// what EPLAN holds before it reports the write as committed.
    /// </summary>
    private sealed class Execution(IConnectionCreator creator, IConnectionGenerator generator, ITransactionScopeFactory transactions, JObject input, string fingerprint)
    {
        private PageScene _scene = new();
        private JObject _plan = new();
        private Action _place = () => { };
        private bool _generate = true;
        private string _project = "";
        private string _page = "";
        private HashSet<string> _requestedNames = [];
        private bool _interruptions;
        private string[] _pages = [];
        private Dictionary<string, LayoutGuard.Model> _layouts = [];
        private bool _generationCommitted;

        public JObject Run()
        {
            try
            {
                _scene = creator.Read((string?)input["project"] ?? "", (string?)input["page"] ?? "");
                _plan = ConnectionCreatePlanner.Plan(input.ToString(Formatting.None), _scene, creator, fingerprint);
                _generate = (bool?)input["generate"] ?? true;
                _place = _plan["cableDefinitions"] is null ? creator.Prepare(_scene.Project.Path, _scene.Page.Page, (JArray)_plan["symbols"]!, actual =>
                {
                    actual.Lines = _generate ? ConnectionCreatePlanner.ExpectedGraphical(_scene, _plan).Select(s => new LayoutGuard.Segment { Points = s }).ToList() : [];
                    _plan["warning"] = LayoutGuard.Summary(new[] { (string?)_plan["warning"], _plan["symbols"]!.Any(s => s["interruption"]?["property_arrangement"] is not null && (s["activePropertySet"]?.Type != JTokenType.String || s["propertyArrangement"]?.Type != JTokenType.String)) ? "Property 19307 could not be read; verify the selected arrangement in EPLAN." : null }.Concat(LayoutGuard.AfterWriting(LayoutGuard.From(_scene), actual)));
                }) : () => { };
            }
            catch (BridgeErrorException ex) { throw WriteArguments.NotStarted(ex); }
            if (_plan["cableDefinitions"] is not null)
            { return CableDefinitionExecution.Run(creator, generator, transactions, _scene, input, _plan); }
            _project = _scene.Project.Path;
            _page = _scene.Page.Page;
            _requestedNames = new HashSet<string>(input["items"]!.Where(item => (string?)item["kind"] == "interruption_point")
                .Select(item => (string)item["name"]!), StringComparer.Ordinal);
            _interruptions = _plan["interruptionScope"] is not null;
            _pages = _interruptions ? InterruptionPointPlanning.Pages(_plan) : [_page];
            _layouts = _pages.ToDictionary(p => p, p => LayoutGuard.From(p == _page ? _scene : creator.Read(_project, p)), StringComparer.Ordinal);
            if (!_generate)
            { return PlaceOnly(); }
            if (_plan["definitionPoints"] is JArray definitions)
            { return PlaceWithDefinitionPoints(definitions); }
            return _interruptions ? PlaceWithInterruptionPoints() : PlaceThenGenerate();
        }

        private string[] ReadNameWarnings(IConnectionPlacementReadback reader)
        {
            IReadOnlyList<InterruptionNameReadback> names = reader.VerifyCreatedSymbols(_project, _page, (JArray)_plan["symbols"]!);
            return names.Where(name => _requestedNames.Contains(name.RequestedName))
                .GroupBy(name => name.RequestedName, StringComparer.Ordinal)
                .Select(group => group.Select(name => name.StructureWarning).FirstOrDefault(warning => warning is not null))
                .Where(warning => warning is not null).Select(warning => warning!).ToArray();
        }

        private void VerifySymbols(IConnectionPlacementReadback reader)
        {
            _plan["warning"] = LayoutGuard.Summary(new[] { (string?)_plan["warning"] }
                .Concat(ReadNameWarnings(reader)));
        }

        private void ReadInterruptionNames()
        {
            if (!_plan["symbols"]!.Any(symbol => symbol["interruption"] is not null))
            {
                return;
            }
            if (creator is IConnectionPlacementReadback reader)
            {
                VerifySymbols(reader);
            }
            else
            {
                _plan["warning"] = LayoutGuard.Summary([(string?)_plan["warning"], "Interruption name readback is unavailable. Verify the stored names in EPLAN."]);
            }
        }

        private void CheckLayout(bool predicted = false)
        {
            foreach (string target in _pages)
            {
                LayoutGuard.Model actual = creator.Measure(_project, target);
                if (!_generate)
                { actual.Lines = []; }
                if (_generate && predicted && target == _page)
                { actual.Lines = ConnectionCreatePlanner.ExpectedGraphical(_scene, _plan).Select(s => new LayoutGuard.Segment { Points = s }).ToList(); }
                _plan["warning"] = LayoutGuard.Summary(new[] { (string?)_plan["warning"] }.Concat(LayoutGuard.AfterWriting(_layouts[target], actual)));
            }
        }

        private void VerifyGraphical(string stage = "placement")
        {
            string Segments(IEnumerable<double[][]> values) => string.Join(";", values.Select(s => string.Join("|", s.Select(p => new JArray(p).ToString(Formatting.None)).OrderBy(p => p, StringComparer.Ordinal))).Distinct(StringComparer.Ordinal).OrderBy(s => s, StringComparer.Ordinal));
            if (Segments(creator.Read(_project, _page).Graphical) is var drawn && Segments(ConnectionCreatePlanner.ExpectedGraphical(_scene, _plan)) is var predicted && drawn != predicted)
            { throw Mismatch("autoconnect lines after " + stage + ": " + drawn + "; predicted: " + predicted); }
        }

        private JObject PlaceOnly()
        {
            IConnectionDefinitionPointWriter? writer = creator as IConnectionDefinitionPointWriter;
            JArray? points = _plan["definitionPoints"] as JArray;
            if (points is not null && writer is null)
            { throw WriteArguments.NotStarted(new BridgeErrorException(ErrorReason.EplanOperationFailed, "The connection creator cannot place definition points.")); }
            Action define = points is null ? () => { }
            : writer!.PrepareDefinitionPoints(_project, _page, points);
            Committed<bool> deferred = TransactionRunner.Run(creator.PlacementTransactions(_project, undoCommitted: true), "eplan_connection_create",
                () => { _place(); define(); CheckLayout(); return true; }, _ => true,
                _ => new OperationContext(OperationContext.Current.RequestId, true, null).Run(() =>
                {
                    CheckLayout();
                    PageDrawingGeometry actual = creator.Read(_project, _page).Page;
                    DrawingPlacement[] created = [.. actual.Placements.Where(p => !_scene.Page.Placements.Any(b => b.Locator.Type == p.Locator.Type && b.Locator.Oid == p.Locator.Oid))];
                    if (creator is IConnectionPlacementReadback symbols)
                    {
                        VerifySymbols(symbols);
                    }
                    else if (_plan["symbols"]!.Any())
                    { throw Mismatch("committed symbol readback is unavailable"); }
                    _plan["actualPlacements"] = JArray.FromObject(created);
                    if (points is not null)
                    { _plan["definitionPoints"] = ConnectionDefinitionPoints.Verify(points, writer!.ReadDefinitionPoints(_project, _page, points)); }
                    return true;
                }),
                () => new OperationContext(OperationContext.Current.RequestId, true, null).Run(() => ConnectionCreatePlanner.Plan(input.ToString(Formatting.None), creator.Read(_project, _page), creator, fingerprint)));
            _plan["execution"] = ExecutionState.Committed;
            _plan["dryRun"] = false;
            _plan["warning"] = LayoutGuard.Summary([(string?)_plan["warning"], deferred.Warning]);
            return _plan;
        }

        private JObject PlaceWithDefinitionPoints(JArray definitions)
        {
            IConnectionDefinitionPointWriter writer = creator as IConnectionDefinitionPointWriter
                ?? throw WriteArguments.NotStarted(new BridgeErrorException(ErrorReason.EplanOperationFailed, "The connection creator cannot place definition points."));
            Action define;
            try
            { define = writer.PrepareDefinitionPoints(_project, _page, definitions); }
            catch (BridgeErrorException ex) { throw WriteArguments.NotStarted(ex); }
            Committed<bool> result = TransactionRunner.Run(creator.PlacementTransactions(_project, undoCommitted: true), "eplan_connection_create",
                () => { _place(); define(); return true; }, _ => true,
                _ => new OperationContext(OperationContext.Current.RequestId, true, null).Run(() =>
                {
                    // A52/D5: generation only sees committed placement; the undo step spans the committed transactions.
                    writer.AddDefinitionArticles(_project, _page, definitions);
                    _plan["actualEndpoints"] = JArray.FromObject(GenerateAndVerify(definitions.Any(d => (string?)d["type"] == "ConnectionDefinitionPoint") || _plan["logicalAfterGenerate"]!.Any()));
                    writer.WriteDefinitionSources(_project, _page, definitions);
                    VerifyGraphical("generation");
                    CheckLayout();
                    ReadInterruptionNames();
                    _plan["definitionPoints"] = ConnectionDefinitionPoints.Verify(definitions, writer.ReadDefinitionPoints(_project, _page, definitions));
                    return true;
                }),
                () => new OperationContext(OperationContext.Current.RequestId, true, null).Run(() => ConnectionCreatePlanner.Plan(input.ToString(Formatting.None), creator.Read(_project, _page), creator, fingerprint)));
            _plan["execution"] = ExecutionState.Committed;
            _plan["dryRun"] = false;
            _plan["warning"] = LayoutGuard.Summary([(string?)_plan["warning"], result.Warning, ConnectionDefinitionPoints.SourceDeviations(definitions, (JArray)_plan["definitionPoints"]!)]);
            return _plan;
        }

        private JObject PlaceWithInterruptionPoints()
        {
            // A55/D5: commit placement before generation; the documented UndoStep lifetime spans committed transactions.
            Committed<bool> result = TransactionRunner.Run(creator.PlacementTransactions(_project, undoCommitted: true), "eplan_connection_create", () => { _place(); CheckLayout(predicted: true); return true; }, _ => true,
                _ => new OperationContext(OperationContext.Current.RequestId, true, null).Run(() =>
                {
                    VerifyGraphical();
                    CheckLayout();
                    if (creator is IConnectionPlacementReadback reader && ReadNameWarnings(reader) is { Length: > 0 } warnings)
                    {
                        throw WriteArguments.Invalid(["/items: Interruption name readback differs. " + LayoutGuard.Summary(warnings)]);
                    }
                    _plan["actualEndpoints"] = JArray.FromObject(GenerateAndVerify());
                    VerifyGraphical("generation");
                    CheckLayout();
                    _plan["actualGraphicalAutoconnect"] = JArray.FromObject(creator.Read(_project, _page).Graphical);
                    _plan["interruptionPoints"] = InterruptionPointPlanning.Verify(_plan, creator.Interruptions(_project, _pages), _requestedNames);
                    return true;
                }),
                () => new OperationContext(OperationContext.Current.RequestId, true, null).Run(() => ConnectionCreatePlanner.Plan(input.ToString(Formatting.None), creator.Read(_project, _page), creator, fingerprint)));
            _plan["execution"] = ExecutionState.Committed;
            _plan["dryRun"] = false;
            _plan["warning"] = LayoutGuard.Summary([(string?)_plan["warning"], result.Warning]);
            return _plan;
        }

        private JObject PlaceThenGenerate()
        {
            Committed<bool> placed = TransactionRunner.Run(transactions, "eplan_connection_create", () => { _place(); CheckLayout(predicted: true); return true; }, _ => true,
                _ => new OperationContext(OperationContext.Current.RequestId, true, null).Run(() =>
                {
                    CheckLayout();
                    ReadInterruptionNames();
                    return true;
                }),
                () => new OperationContext(OperationContext.Current.RequestId, true, null).Run(() => ConnectionCreatePlanner.Plan(input.ToString(Formatting.None), creator.Read(_project, _page), creator, fingerprint)));
            if (placed.Warning is not null)
            { throw Unknown(); }
            try
            {
                VerifyGraphical();
                if ((int)_plan["undoSteps"]! == 1)
                {
                    List<string?[]> actual = creator.Endpoints(_project, _page);
                    var remaining = actual.ToList();
                    foreach (ConnectionRecord before in _scene.Logical)
                    {
                        int index = remaining.FindIndex(p => p[0] == before.StartDevice && p[1] == before.EndDevice);
                        if (index < 0)
                        { throw Mismatch("existing connection " + before.StartDevice + " - " + before.EndDevice + " is gone after node placement; connections now: " + Listed(actual)); }
                        remaining.RemoveAt(index);
                    }
                    if (remaining.Count != 0)
                    { throw Mismatch("node placement added an unplanned connection"); }
                    _plan["execution"] = ExecutionState.Committed;
                    _plan["dryRun"] = false;
                    _plan["actualEndpoints"] = JArray.FromObject(actual);
                    return _plan;
                }
                Committed<List<string?[]>> generated = TransactionRunner.Run(transactions, "eplan_connection_create", () => GenerateAndVerify(), _ => true,
                    _ => new OperationContext(OperationContext.Current.RequestId, true, null).Run(() => { CheckLayout(); return true; }));
                if (generated.Warning is not null)
                { throw Unknown(); }
                _generationCommitted = true;
                _plan["execution"] = ExecutionState.Committed;
                _plan["dryRun"] = false;
                _plan["actualEndpoints"] = JArray.FromObject(generated.Result);
                return _plan;
            }
            catch (Exception failure) when (failure is not BridgeErrorException { Error.Execution: ExecutionState.Unknown })
            {
                try
                {
                    new OperationContext(OperationContext.Current.RequestId, write: true, progress: null).Run(() =>
                    { if (_generationCommitted) { creator.Undo(_project); } creator.Undo(_project); return ConnectionCreatePlanner.Plan(input.ToString(Formatting.None), creator.Read(_project, _page), creator, fingerprint); });
                }
                catch (Exception nativeFailure) { WriteExceptionDiagnostics.Report(nativeFailure); throw Unknown(); }
                if (failure is BridgeErrorException { Error.Reason: ErrorReason.InvalidArgument } refusal)
                { refusal.Error.Execution = ExecutionState.RolledBack; throw; }
                string cause = failure is BridgeErrorException { Error.Reason: ErrorReason.EplanOperationFailed } known ? " Cause: " + known.Error.Message : string.Empty;
                throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "Connection creation failed; generation and placement were rolled back. Re-read the page before retrying." + cause, failure)
                { Error = { Execution = ExecutionState.RolledBack } };
            }
        }

        private List<string?[]> GenerateAndVerify(bool generate = true)
        {
            if (generate)
            { generator.Generate(_project, _pages); }
            List<string?[]> actual = _pages.SelectMany(p => creator.Endpoints(_project, p)).ToList();
            List<string?[]> added = CheckPreserved(actual);
            CheckGroups(actual, added);
            CheckLayout();
            return actual;
        }

        private List<string?[]> CheckPreserved(List<string?[]> actual)
        {
            if (_interruptions && _plan["interruptionScope"]!["preservedEndpoints"]!.Any(p => !actual.Any(a => a[0] == (string?)p[0] && a[1] == (string?)p[1])))
            { throw Mismatch("an interruption point's existing connection was lost"); }
            var added = actual.Select(p => p.ToArray()).ToList();
            IEnumerable<string?[]> preserved = _interruptions ? _plan["interruptionScope"]!["preservedEndpoints"]!.ToObject<List<string?[]>>()!
                : _scene.Logical.Select(c => new string?[] { c.StartDevice, c.EndDevice });
            foreach (string?[] before in preserved)
            {
                int index = added.FindIndex(p => p[0] == before[0] && p[1] == before[1]);
                if (index < 0)
                { throw Mismatch("existing connection " + before[0] + " - " + before[1] + " is gone after generation; connections now: " + Listed(actual)); }
                added.RemoveAt(index);
            }
            if (_plan["definitionPoints"] is not null && added.Any(p => preserved.Any(before => p[0] == before[0] && p[1] == before[1] || p[0] == before[1] && p[1] == before[0])))
            { throw Mismatch("generation duplicated existing connection endpoints"); }
            return added;
        }

        private void CheckGroups(List<string?[]> actual, List<string?[]> added)
        {
            List<HashSet<string>> groups = _plan["logicalAfterGenerate"]!.Select(g => new HashSet<string>(g["pins"]!.Values<string>().Select(p => p!)
                .Concat(g["interruptionPins"]?.Values<string>().Select(p => p!) ?? []).Concat((bool)g["potentialStartIsNull"]! ? ["<potential>"] : []), StringComparer.Ordinal)).ToList();
            // Customer rehearsal: EPLAN connects devices through interruption points, so a point whose counterpart side has
            // no device yet gets no connection; interruption ends are never required.
            var optional = new HashSet<string>(_plan["logicalAfterGenerate"]!.SelectMany(g => (g["interruptionPins"]?.Values<string>() ?? []).Concat(g["pins"]!.Values<string>()))
                .Select(p => p!).Where(p => p.StartsWith("<interruption:", StringComparison.Ordinal) || (_plan["logicalAfterGenerate"]!.Any(g => g["interruptionPins"]?.Values<string>().Contains(p) == true))), StringComparer.Ordinal);
            foreach (string?[] pair in added)
            {
                if (pair[1] is null || pair[0] == pair[1] || !groups.Any(g => g.Contains(pair[0] ?? "<potential>") && g.Contains(pair[1]!)))
                { throw Mismatch("generation added the unplanned connection " + (pair[0] ?? "<potential>") + " - " + (pair[1] ?? "<none>") + "; generate existing autoconnect lines first"); }
            }
            foreach (HashSet<string> group in groups)
            {
                var required = group.Except(optional).ToArray();
                if (required.Length == 0)
                { continue; }
                var reached = new HashSet<string>(StringComparer.Ordinal) { required[0] };
                for (int i = 0; i < group.Count; i++)
                { foreach (string?[] pair in actual.Where(p => group.Contains(p[0] ?? "<potential>") && group.Contains(p[1]!) && (reached.Contains(p[0] ?? "<potential>") || reached.Contains(p[1]!)))) { reached.Add(pair[0] ?? "<potential>"); reached.Add(pair[1]!); } }
                if (group.Except(optional).Any(p => !reached.Contains(p)))
                {
                    IEnumerable<string> seen = actual.Where(p => group.Contains(p[0] ?? "<potential>") || group.Contains(p[1] ?? "<none>")).Select(p => (p[0] ?? "<potential>") + "-" + (p[1] ?? "<none>"));
                    throw Mismatch("planned pins " + string.Join(", ", group.Except(reached)) + " were not connected (group " + string.Join(", ", group)
                        + "; generated with these pins: " + string.Join(", ", seen.DefaultIfEmpty("none")) + ")");
                }
            }
            if (added.Count == 0 && groups.Count > 0 && !_interruptions)
            { throw Mismatch("generation added no connection"); }
        }
    }

    // What EPLAN holds instead tells a lost connection from one whose ends changed.
    private static string Listed(List<string?[]> connections) => connections.Count == 0 ? "none"
        : string.Join("; ", connections.Select(p => (p[0] ?? "<potential>") + " - " + (p[1] ?? "<none>")));

    private static BridgeErrorException Mismatch(string detail) => new(ErrorReason.EplanOperationFailed, "Actual connection geometry or logical endpoints differ from the plan: " + detail + ".");
    private static BridgeErrorException Unknown() => new(ErrorReason.OutcomeUnknown, "Connection creation outcome is unknown; ask the engineer to inspect the project before further writes.") { Error = { Execution = ExecutionState.Unknown } };
}

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
        ConnectionCreatePlanner.Scene scene;
        JObject plan;
        Action place;
        try
        {
            scene = creator.Read((string?)input["project"] ?? "", (string?)input["page"] ?? "");
            plan = ConnectionCreatePlanner.Plan(input.ToString(Formatting.None), scene, creator, fingerprint);
            place = plan["cableDefinitions"] is null ? creator.Prepare(scene.Project.Path, scene.Page.Page, (JArray)plan["symbols"]!, actual =>
            {
                actual.Lines = ConnectionCreatePlanner.ExpectedGraphical(scene, plan).Select(s => new LayoutGuard.Segment { Points = s }).ToList();
                plan["warning"] = LayoutGuard.Summary(new[] { (string?)plan["warning"] }.Concat(LayoutGuard.AfterWriting(LayoutGuard.From(scene), actual)));
            }) : () => { };
        }
        catch (BridgeErrorException ex) { throw WriteArguments.NotStarted(ex); }
        if (plan["cableDefinitions"] is not null)
        { return CableDefinitionExecution.Run(creator, generator, transactions, scene, input, plan); }
        string project = scene.Project.Path, page = scene.Page.Page;
        bool interruptions = plan["interruptionScope"] is not null, generationCommitted = false;
        string[] pages = interruptions ? InterruptionPointPlanning.Pages(plan) : [page];
        Dictionary<string, LayoutGuard.Model> layouts = pages.ToDictionary(p => p, p => LayoutGuard.From(p == page ? scene : creator.Read(project, p)), StringComparer.Ordinal);
        void CheckLayout(bool predicted = false)
        {
            foreach (string target in pages)
            {
                LayoutGuard.Model actual = creator.Measure(project, target);
                if (predicted && target == page)
                { actual.Lines = ConnectionCreatePlanner.ExpectedGraphical(scene, plan).Select(s => new LayoutGuard.Segment { Points = s }).ToList(); }
                plan["warning"] = LayoutGuard.Summary(new[] { (string?)plan["warning"] }.Concat(LayoutGuard.AfterWriting(layouts[target], actual)));
            }
        }
        void VerifyGraphical(string stage = "placement")
        {
            string Segments(IEnumerable<double[][]> values) => string.Join(";", values.Select(s => string.Join("|", s.Select(p => new JArray(p).ToString(Formatting.None)).OrderBy(p => p, StringComparer.Ordinal))).Distinct(StringComparer.Ordinal).OrderBy(s => s, StringComparer.Ordinal));
            if (Segments(creator.Read(project, page).Graphical) is var drawn && Segments(ConnectionCreatePlanner.ExpectedGraphical(scene, plan)) is var predicted && drawn != predicted)
            { throw Mismatch("autoconnect lines after " + stage + ": " + drawn + "; predicted: " + predicted); }
        }
        if (plan["definitionPoints"] is JArray definitions)
        {
            IConnectionDefinitionPointWriter writer = creator as IConnectionDefinitionPointWriter
                ?? throw WriteArguments.NotStarted(new BridgeErrorException(ErrorReason.EplanOperationFailed, "The connection creator cannot place definition points."));
            Action define;
            try
            { define = writer.PrepareDefinitionPoints(project, page, definitions); }
            catch (BridgeErrorException ex) { throw WriteArguments.NotStarted(ex); }
            Committed<bool> result = TransactionRunner.Run(creator.PlacementTransactions(project, undoCommitted: true), "eplan_connection_create",
                () => { place(); define(); return true; }, _ => true,
                _ => new OperationContext(OperationContext.Current.RequestId, true, null).Run(() =>
                {
                    // A52/D5: generation only sees committed placement; the undo step spans the committed transactions.
                    plan["actualEndpoints"] = JArray.FromObject(GenerateAndVerify(definitions.Any(d => (string?)d["type"] == "ConnectionDefinitionPoint") || plan["logicalAfterGenerate"]!.Any()));
                    VerifyGraphical("generation");
                    CheckLayout();
                    plan["definitionPoints"] = ConnectionDefinitionPoints.Verify(definitions, writer.ReadDefinitionPoints(project, page, definitions));
                    return true;
                }),
                () => new OperationContext(OperationContext.Current.RequestId, true, null).Run(() => ConnectionCreatePlanner.Plan(input.ToString(Formatting.None), creator.Read(project, page), creator, fingerprint)));
            plan["execution"] = ExecutionState.Committed;
            plan["dryRun"] = false;
            plan["warning"] = LayoutGuard.Summary([(string?)plan["warning"], result.Warning]);
            return plan;
        }
        if (interruptions)
        {
            // A55/D5: commit placement before generation; the documented UndoStep lifetime spans committed transactions.
            Committed<bool> result = TransactionRunner.Run(creator.PlacementTransactions(project, undoCommitted: true), "eplan_connection_create", () => { place(); CheckLayout(predicted: true); return true; }, _ => true,
                _ => new OperationContext(OperationContext.Current.RequestId, true, null).Run(() =>
                {
                    VerifyGraphical();
                    CheckLayout();
                    plan["actualEndpoints"] = JArray.FromObject(GenerateAndVerify());
                    VerifyGraphical("generation");
                    CheckLayout();
                    plan["actualGraphicalAutoconnect"] = JArray.FromObject(creator.Read(project, page).Graphical);
                    plan["interruptionPoints"] = InterruptionPointPlanning.Verify(plan, creator.Interruptions(project, pages));
                    return true;
                }),
                () => new OperationContext(OperationContext.Current.RequestId, true, null).Run(() => ConnectionCreatePlanner.Plan(input.ToString(Formatting.None), creator.Read(project, page), creator, fingerprint)));
            plan["execution"] = ExecutionState.Committed;
            plan["dryRun"] = false;
            plan["warning"] = LayoutGuard.Summary([(string?)plan["warning"], result.Warning]);
            return plan;
        }
        Committed<bool> placed = TransactionRunner.Run(transactions, "eplan_connection_create", () => { place(); CheckLayout(predicted: true); return true; }, _ => true,
            _ => new OperationContext(OperationContext.Current.RequestId, true, null).Run(() => { CheckLayout(); return true; }),
            () => new OperationContext(OperationContext.Current.RequestId, true, null).Run(() => ConnectionCreatePlanner.Plan(input.ToString(Formatting.None), creator.Read(project, page), creator, fingerprint)));
        if (placed.Warning is not null)
        { throw Unknown(); }
        try
        {
            VerifyGraphical();
            if ((int)plan["undoSteps"]! == 1)
            {
                List<string?[]> actual = creator.Endpoints(project, page);
                var remaining = actual.ToList();
                foreach (ConnectionRecord before in scene.Logical)
                {
                    int index = remaining.FindIndex(p => p[0] == before.StartDevice && p[1] == before.EndDevice);
                    if (index < 0)
                    { throw Mismatch("existing connection " + before.StartDevice + " - " + before.EndDevice + " is gone after node placement"); }
                    remaining.RemoveAt(index);
                }
                if (remaining.Count != 0)
                { throw Mismatch("node placement added an unplanned connection"); }
                plan["execution"] = ExecutionState.Committed;
                plan["dryRun"] = false;
                plan["actualEndpoints"] = JArray.FromObject(actual);
                return plan;
            }
            Committed<List<string?[]>> generated = TransactionRunner.Run(transactions, "eplan_connection_create", () => GenerateAndVerify(), _ => true,
                _ => new OperationContext(OperationContext.Current.RequestId, true, null).Run(() => { CheckLayout(); return true; }));
            if (generated.Warning is not null)
            { throw Unknown(); }
            generationCommitted = true;
            plan["execution"] = ExecutionState.Committed;
            plan["dryRun"] = false;
            plan["actualEndpoints"] = JArray.FromObject(generated.Result);
            return plan;
        }
        catch (Exception failure) when (failure is not BridgeErrorException { Error.Execution: ExecutionState.Unknown })
        {
            try
            {
                new OperationContext(OperationContext.Current.RequestId, write: true, progress: null).Run(() =>
                { if (generationCommitted) { creator.Undo(project); } creator.Undo(project); return ConnectionCreatePlanner.Plan(input.ToString(Formatting.None), creator.Read(project, page), creator, fingerprint); });
            }
            catch (Exception nativeFailure) { WriteExceptionDiagnostics.Report(nativeFailure); throw Unknown(); }
            if (failure is BridgeErrorException { Error.Reason: ErrorReason.InvalidArgument } refusal)
            { refusal.Error.Execution = ExecutionState.RolledBack; throw; }
            string cause = failure is BridgeErrorException { Error.Reason: ErrorReason.EplanOperationFailed } known ? " Cause: " + known.Error.Message : string.Empty;
            throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "Connection creation failed; generation and placement were rolled back. Re-read the page before retrying." + cause, failure)
            { Error = { Execution = ExecutionState.RolledBack } };
        }
        List<string?[]> GenerateAndVerify(bool generate = true)
        {
            if (generate)
            { generator.Generate(project, pages); }
            List<string?[]> actual = pages.SelectMany(p => creator.Endpoints(project, p)).ToList();
            if (interruptions && plan["interruptionScope"]!["preservedEndpoints"]!.Any(p => !actual.Any(a => a[0] == (string?)p[0] && a[1] == (string?)p[1])))
            { throw Mismatch("an interruption point's existing connection was lost"); }
            var added = actual.Select(p => p.ToArray()).ToList();
            IEnumerable<string?[]> preserved = interruptions ? plan["interruptionScope"]!["preservedEndpoints"]!.ToObject<List<string?[]>>()!
                : scene.Logical.Select(c => new string?[] { c.StartDevice, c.EndDevice });
            foreach (string?[] before in preserved)
            {
                int index = added.FindIndex(p => p[0] == before[0] && p[1] == before[1]);
                if (index < 0)
                { throw Mismatch("existing connection " + before[0] + " - " + before[1] + " is gone after generation"); }
                added.RemoveAt(index);
            }
            if (plan["definitionPoints"] is not null && added.Any(p => preserved.Any(before => p[0] == before[0] && p[1] == before[1] || p[0] == before[1] && p[1] == before[0])))
            { throw Mismatch("generation duplicated existing connection endpoints"); }
            List<HashSet<string>> groups = plan["logicalAfterGenerate"]!.Select(g => new HashSet<string>(g["pins"]!.Values<string>().Select(p => p!)
                .Concat(g["interruptionPins"]?.Values<string>().Select(p => p!) ?? []).Concat((bool)g["potentialStartIsNull"]! ? ["<potential>"] : []), StringComparer.Ordinal)).ToList();
            // Customer rehearsal: EPLAN connects devices through interruption points, so a point whose counterpart side has
            // no device yet gets no connection; interruption ends are never required.
            var optional = new HashSet<string>(plan["logicalAfterGenerate"]!.SelectMany(g => (g["interruptionPins"]?.Values<string>() ?? []).Concat(g["pins"]!.Values<string>()))
                .Select(p => p!).Where(p => p.StartsWith("<interruption:", StringComparison.Ordinal) || (plan["logicalAfterGenerate"]!.Any(g => g["interruptionPins"]?.Values<string>().Contains(p) == true))), StringComparer.Ordinal);
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
            if (added.Count == 0 && groups.Count > 0 && !interruptions)
            { throw Mismatch("generation added no connection"); }
            CheckLayout();
            return actual;
        }
    }
    private static BridgeErrorException Mismatch(string detail) => new(ErrorReason.EplanOperationFailed, "Actual connection geometry or logical endpoints differ from the plan: " + detail + ".");
    private static BridgeErrorException Unknown() => new(ErrorReason.OutcomeUnknown, "Connection creation outcome is unknown; ask the engineer to inspect the project before further writes.") { Error = { Execution = ExecutionState.Unknown } };
}

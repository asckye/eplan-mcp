using EplanMcp.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>O3/A52: creation uses two commits; A54 moves share this entry and one still-open undo step.</summary>
public sealed class ConnectionCreateExecuteOperation(IConnectionCreator creator, IConnectionGenerator generator, ITransactionScopeFactory transactions, IPlacementMover? mover = null)
    : Operation<JObject, JObject>, IWriteOperation
{
    public override string Name => OperationNames.ConnectionCreateExecute;
    protected override JObject Execute(JObject args)
    {
        JObject input = args["request"] as JObject ?? throw WriteArguments.NotStarted(WriteArguments.Invalid(["/request: supply the planned request."]));
        string fingerprint = (string?)args["fingerprint"] ?? throw WriteArguments.NotStarted(WriteArguments.Invalid(["/fingerprint: supply the plan fingerprint."]));
        if (input.ContainsKey("moves"))
        { return PlacementMoveExecution.Execute(mover ?? throw WriteArguments.NotStarted(new BridgeErrorException(ErrorReason.CapabilityMissing, "Placement moving is unavailable in this Bridge.")), transactions, input, fingerprint); }
        ConnectionCreatePlanner.Scene scene;
        JObject plan;
        Action place;
        try
        {
            scene = creator.Read((string?)input["project"] ?? "", (string?)input["page"] ?? "");
            plan = ConnectionCreatePlanner.Plan(input.ToString(Formatting.None), scene, creator, fingerprint);
            if (plan["cableDefinitions"] is null && (!plan["symbols"]!.Any() || (!plan["logicalAfterGenerate"]!.Any() && plan["splitSegments"] is null) || plan["interruptionScope"] is not null && !plan["graphicalAutoconnect"]!.Any()))
            { throw new BridgeErrorException(ErrorReason.CapabilityMissing, "Supply symbols with connected logical endpoints; empty generation cannot provide 2 undo steps."); }
            place = plan["cableDefinitions"] is null ? creator.Prepare(scene.Project.Path, scene.Page.Page, (JArray)plan["symbols"]!) : () => { };
        }
        catch (BridgeErrorException ex) { throw WriteArguments.NotStarted(ex); }
        if (plan["cableDefinitions"] is not null)
        { return CableDefinitionExecution.Run(creator, generator, transactions, scene, input, plan); }
        string project = scene.Project.Path, page = scene.Page.Page;
        bool interruptions = plan["interruptionScope"] is not null, generationCommitted = false;
        string[] pages = interruptions ? InterruptionPointPlanning.Pages(plan) : [page];
        Committed<bool> placed = TransactionRunner.Run(interruptions ? creator.PlacementTransactions(project) : transactions, "eplan_connection_create", () => { place(); return true; }, _ => true);
        if (placed.Warning is not null)
        { throw Unknown(); }
        try
        {
            string Segments(IEnumerable<double[][]> values) => string.Join(";", values.Select(s => string.Join("|", s.Select(p => new JArray(p).ToString(Formatting.None)).OrderBy(p => p, StringComparer.Ordinal))).Distinct(StringComparer.Ordinal).OrderBy(s => s, StringComparer.Ordinal));
            if (Segments(creator.Read(project, page).Graphical) is var drawn && Segments(ConnectionCreatePlanner.ExpectedGraphical(scene, plan)) is var predicted && drawn != predicted)
            { throw Mismatch("autoconnect lines after placement: " + drawn + "; predicted: " + predicted); }
            Committed<List<string?[]>> generated = TransactionRunner.Run(transactions, "eplan_connection_create", () =>
            {
                generator.Generate(project, pages);
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
                List<HashSet<string>> groups = plan["logicalAfterGenerate"]!.Select(g => new HashSet<string>(g["pins"]!.Values<string>().Select(p => p!)
                    .Concat(g["interruptionPins"]?.Values<string>().Select(p => p!) ?? []).Concat((bool)g["potentialStartIsNull"]! ? ["<potential>"] : []), StringComparer.Ordinal)).ToList();
                var optional = new HashSet<string>(plan["logicalAfterGenerate"]!.SelectMany(g => g["interruptionPins"]?.Values<string>().Select(p => p!) ?? []), StringComparer.Ordinal);
                foreach (string?[] pair in added)
                {
                    if (pair[1] is null || pair[0] == pair[1] || !groups.Any(g => g.Contains(pair[0] ?? "<potential>") && g.Contains(pair[1]!)))
                    { throw Mismatch("generation added the unplanned connection " + (pair[0] ?? "<potential>") + " - " + (pair[1] ?? "<none>") + "; generate existing autoconnect lines first"); }
                }
                foreach (HashSet<string> group in groups)
                {
                    var reached = new HashSet<string>(StringComparer.Ordinal) { group.First(p => !optional.Contains(p)) };
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
                // Live T7b: EPLAN computes interruption cross-references only after the commit; verify below.
                return actual;
            }, _ => true);
            if (generated.Warning is not null)
            { throw Unknown(); }
            generationCommitted = true;
            if (interruptions)
            { plan["interruptionPoints"] = InterruptionPointPlanning.Verify(plan, creator.Interruptions(project, pages)); }
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
            catch (Exception) { throw Unknown(); }
            string cause = failure is BridgeErrorException { Error.Reason: ErrorReason.EplanOperationFailed } known ? " Cause: " + known.Error.Message : string.Empty;
            throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "Connection creation failed; generation and placement were rolled back. Re-read the page before retrying." + cause, failure)
            { Error = { Execution = ExecutionState.RolledBack } };
        }
    }
    private static BridgeErrorException Mismatch(string detail) => new(ErrorReason.EplanOperationFailed, "Actual connection geometry or logical endpoints differ from the plan: " + detail + ".");
    private static BridgeErrorException Unknown() => new(ErrorReason.OutcomeUnknown, "Connection creation outcome is unknown; ask the engineer to inspect the project before further writes.") { Error = { Execution = ExecutionState.Unknown } };
}
